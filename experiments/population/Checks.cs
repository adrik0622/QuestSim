namespace QuestSim.Population;

// E1 acceptance checks. Each prints PASS or throws.
public static class Checks
{
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("Check failed: " + description);
        Console.WriteLine("PASS " + description);
    }

    // Dense enough that rumors spread; few slots and frequent events so slots retire and get reused.
    static Settings Small(uint delay = 2) => new()
    {
        Agents = 40_000, WorldKm = 20, EventEveryTicks = 10, RumorLifetimeTicks = 60, RumorSlots = 4, ReadbackDelay = delay, FadePerTick = 0.08
    };

    public static int Run(string shaderDir, bool gpu)
    {
        var s = Small();
        var (w1, a1) = Generator.Create(s);
        var (_, a2) = Generator.Create(s);
        var (_, a3) = Generator.Create(s with { Seed = 2 });
        Check(a1.Hash() == a2.Hash() && a1.Hash() != a3.Hash(), "world generation is deterministic per seed and differs across seeds");

        long initialHumans = a1.Info.LongCount(i => i >> 31 == 0);
        var factionTotals = Enumerable.Range(0, 8).Select(f => a1.Info.LongCount(i => ((i >> 24) & 63) == f)).ToArray();
        var identity = a1.Info.Select(i => i & 0xBF000000u).ToArray();   // kind + faction, never change
        var (straight, straightReads) = RunCpu(s, 200);
        var state = straight.Backend.Download();
        Check(state.Info.Select(i => i & 0xBF000000u).SequenceEqual(identity) && state.Info.LongCount(i => i >> 31 == 0) == initialHumans,
            "agent count, kinds and factions are conserved; slots never change owner");
        Check(straightReads.Values.All(r => Enumerable.Range(0, 8).All(f => r.Agg.Skip(f * (int)Settings.RegionCount).Take((int)Settings.RegionCount).Sum(x => (long)x) == factionTotals[f])),
            "per-region faction aggregates always sum to per-agent faction totals");
        var (again, _) = RunCpu(s, 200);
        Check(again.Backend.Download().Hash() == state.Hash() && again.Book.Facts.SequenceEqual(straight.Book.Facts) &&
            again.Book.Heard.OrderBy(x => x.Key).SequenceEqual(straight.Book.Heard.OrderBy(x => x.Key)), "same seed reproduces state, facts and rumor tallies");

        int fired = Enumerable.Range(1, 199).Count(t => t % s.EventEveryTicks == 0);
        long heard = straightReads.Values.Sum(r => (long)r.Counters[0]);
        Console.WriteLine($"  facts {straight.Book.Facts.Count} dropped {straight.Book.Dropped} fired {fired} retirements {straight.Book.SlotVersion.Sum(v => v)}");
        Check(straight.Book.Facts.Count + straight.Book.Dropped == fired && heard > 0 && straight.Book.SlotVersion.Sum(v => v) > 0,
            $"every scripted event becomes one fact or a counted drop; rumors spread ({heard:N0} hearings) and slots retire");
        Check(straight.Book.Facts.Select((f, i) => f.Id == i && f.Provenance == "scripted-event").All(x => x),
            "spread never edits the fact log; facts are append-only with provenance");

        using (var leak = new Simulation(s, w1, new CpuBackend(s, w1, Generator.Create(s).Item2)))
        {
            int reuses = 0;
            leak.Book.SlotReused = slot =>
            {
                var snap = leak.Backend.Download();
                uint bit = 1u << (slot % 32);
                if ((slot < 32 ? snap.MaskLo : snap.MaskHi).Any(m => (m & bit) != 0)) throw new Exception($"slot {slot} reused while carried");
                reuses++;
            };
            for (int t = 0; t < 200; t++) leak.Step();
            Check(reuses > 0, $"retired rumor slots carry no old beliefs when reused ({reuses} reuses)");
        }

        var fade = s with { MaxEvents = 1, EventEveryTicks = 5 };
        var peak = new List<uint>();
        using (var one = new Simulation(fade, w1, new CpuBackend(fade, w1, Generator.Create(fade).Item2)))
        {
            one.Observed = r => peak.Add(r.Counters.Skip(2).Take(64).Max());
            for (int t = 0; t < 300; t++) one.Step();
            Check(peak.Max() > 10 && peak[^1] == 0 && one.Book.SlotFact.All(f => f < 0),
                $"a single rumor spreads (peak {peak.Max()} carriers), fades to zero carriers and its slot retires");
        }

        string path = Path.Combine(Path.GetTempPath(), $"questsim-pop-{Guid.NewGuid():N}.bin");
        try
        {
            var (first, _) = RunCpu(s, 120);
            Snapshot.Save(path, first);
            var (ls, lw, la, next, book, pending) = Snapshot.Load(path);
            using var resumed = new Simulation(ls, lw, new CpuBackend(ls, lw, la), next, book);
            foreach (var (k, v) in pending) resumed.Pending[k] = v;
            var resumedReads = new Dictionary<uint, Readback>();
            resumed.Observed = r => resumedReads[r.Tick] = r;
            for (int t = 120; t < 200; t++) resumed.Step();
            Check(resumed.Backend.Download().Hash() == state.Hash() && resumed.Book.Facts.SequenceEqual(straight.Book.Facts) &&
                resumedReads.All(x => x.Value.Counters.SequenceEqual(straightReads[x.Key].Counters)),
                $"snapshot at tick 120 + 80 ticks equals 200 straight ticks ({new FileInfo(path).Length:N0} bytes)");
        }
        finally { File.Delete(path); }

        if (!gpu) return 0;
        foreach (uint delay in new uint[] { 0, 2, 4 })
        {
            var d = Small(delay);
            var (w, a) = Generator.Create(d);
            using var cpu = new Simulation(d, w, new CpuBackend(d, w, a));
            using var gpuSim = new Simulation(d, w, new GpuBackend(d, w, Generator.Create(d).Item2, shaderDir));
            var cpuReads = new Dictionary<uint, Readback>();
            var gpuReads = new Dictionary<uint, Readback>();
            cpu.Observed = r => cpuReads[r.Tick] = r;
            gpuSim.Observed = r => gpuReads[r.Tick] = r;
            int ticks = delay == 2 ? 200 : 80;
            for (int t = 0; t < ticks; t++)
            {
                cpu.Step(); gpuSim.Step();
                if (cpu.Backend.Download().Hash() != gpuSim.Backend.Download().Hash()) throw new Exception($"CPU/GPU state diverged at tick {t} (delay {delay})");
            }
            cpu.CollectOutstanding(); gpuSim.CollectOutstanding();
            foreach (var (k, v) in cpu.Pending) cpuReads[k] = v;
            foreach (var (k, v) in gpuSim.Pending) gpuReads[k] = v;
            bool same = cpuReads.Count == ticks && cpuReads.All(x => gpuReads.TryGetValue(x.Key, out var g) &&
                g.Counters.SequenceEqual(x.Value.Counters) && g.Agg.SequenceEqual(x.Value.Agg) && g.Events.SequenceEqual(x.Value.Events));
            Check(same && cpuReads.Values.All(r => r.Counters[1] == 0) && cpu.Book.Facts.SequenceEqual(gpuSim.Book.Facts),
                $"CPU reference and Vulkan GPU are bit-identical every tick with readback delay {delay} ({ticks} ticks, {cpuReads.Values.Sum(r => (long)r.Counters[0]):N0} hearings)");
        }
        return 0;
    }

    static (Simulation, Dictionary<uint, Readback>) RunCpu(Settings s, int ticks)
    {
        var (w, a) = Generator.Create(s);
        var sim = new Simulation(s, w, new CpuBackend(s, w, a));
        var reads = new Dictionary<uint, Readback>();
        sim.Observed = r => reads[r.Tick] = r;
        for (int t = 0; t < ticks; t++) sim.Step();
        return (sim, reads);
    }
}
