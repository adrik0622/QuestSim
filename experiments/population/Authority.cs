namespace QuestSim.Population;

public sealed record Fact(int Id, uint Tick, uint X, uint Y, string Kind, string Provenance);

// CPU-owned facts and rumor slots. Backends only ever see slot bits, credibility and seeds.
public sealed class RumorBook(Settings s, World w)
{
    public List<Fact> Facts { get; } = new();
    public int[] SlotFact { get; } = Enumerable.Repeat(-1, 64).ToArray();
    public uint[] SlotVersion { get; } = new uint[64];
    public uint[] SlotCredibility { get; } = new uint[64];
    public uint[] SlotStart { get; } = new uint[64];
    public Dictionary<int, long> Heard { get; } = new();
    public long Dropped { get; set; }
    public Action<int>? SlotReused;

    // Decisions use carriers from a readback ReadbackDelay ticks old, identically for every backend.
    public void Consume(Readback r)
    {
        for (int slot = 0; slot < 64; slot++)
        {
            if (SlotFact[slot] < 0) continue;
            Heard[SlotFact[slot]] = Heard.GetValueOrDefault(SlotFact[slot]) + r.Counters[66 + slot];
            if (r.Counters[2 + slot] == 0 && SlotStart[slot] < r.Tick) { SlotFact[slot] = -1; SlotVersion[slot]++; }
        }
    }

    public TickInput Prepare(uint tick)
    {
        var input = new TickInput { Tick = tick };
        var seeded = new List<int>();
        if (tick > 0 && tick % s.EventEveryTicks == 0 && (s.MaxEvents == 0 || Facts.Count < s.MaxEvents))
        {
            uint settlement = H.Hash4((uint)s.Seed, 0xE0, tick, 0) % w.Count;
            int slot = Array.FindIndex(SlotFact, 0, (int)s.RumorSlots, f => f < 0);
            if (slot < 0) Dropped++;
            else
            {
                if (SlotVersion[slot] > 0) SlotReused?.Invoke(slot);
                var fact = new Fact(Facts.Count, tick, w.Settlements[2 * settlement], w.Settlements[2 * settlement + 1], "leader-died", "scripted-event");
                Facts.Add(fact);
                SlotFact[slot] = fact.Id; SlotStart[slot] = tick;
                SlotCredibility[slot] = 0x9999 + H.Hash4((uint)s.Seed, 0xE1, tick, 0) % 0x6666;   // 0.6 .. 1.0
                seeded.Add(slot);
            }
        }
        for (int slot = 0; slot < 64; slot++)
        {
            if (SlotFact[slot] < 0) continue;
            if (slot < 32) input.ActiveLo |= 1u << slot; else input.ActiveHi |= 1u << (slot - 32);
            uint age = tick - SlotStart[slot];   // old news is less believable; zero once past its lifetime
            input.Rumor[slot] = age >= s.RumorLifetimeTicks ? 0 : (uint)((ulong)SlotCredibility[slot] * (s.RumorLifetimeTicks - age) / s.RumorLifetimeTicks);
        }
        for (uint fa = 0; fa < Generator.HumanFactions; fa++)
            for (uint fb = 0; fb < Generator.HumanFactions; fb++)
                input.Rumor[64 + fa * 8 + fb] = fa == fb ? 0xC000u : 0x4000u;
        foreach (int slot in seeded.Take(TickInput.MaxSeeds))
        {
            var fact = Facts[SlotFact[slot]];
            int k = (int)input.SeedCount++;
            input.Rumor[128 + 4 * k] = fact.X; input.Rumor[129 + 4 * k] = fact.Y;
            input.Rumor[130 + 4 * k] = s.WitnessRadiusMm / 256; input.Rumor[131 + 4 * k] = (uint)slot;
        }
        return input;
    }
}

public sealed class Simulation : IDisposable
{
    public Settings S { get; }
    public World W { get; }
    public IBackend Backend { get; }
    public RumorBook Book { get; }
    public uint NextTick { get; private set; }
    public Dictionary<uint, Readback> Pending { get; } = new();   // collected, not yet consumed
    public Action<Readback>? Observed;

    public Simulation(Settings s, World w, IBackend backend, uint nextTick = 0, RumorBook? book = null)
    {
        S = s; W = w; Backend = backend; NextTick = nextTick; Book = book ?? new RumorBook(s, w);
    }

    public void Step()
    {
        uint t = NextTick;
        if (S.ReadbackDelay > 0 && t >= S.ReadbackDelay) Consume(t - S.ReadbackDelay);
        Backend.Submit(Book.Prepare(t));
        if (S.ReadbackDelay == 0) Consume(t);   // synchronous: wait for this tick's own results
        NextTick++;
    }

    void Consume(uint tick)
    {
        if (!Pending.Remove(tick, out var r)) r = Backend.Collect(tick);
        Book.Consume(r);
        Observed?.Invoke(r);
    }

    // Bring every submitted tick back to the host without consuming it (for snapshots and comparisons).
    public void CollectOutstanding()
    {
        for (uint t = NextTick > S.ReadbackDelay ? NextTick - S.ReadbackDelay : 0; t < NextTick; t++)
            if (!Pending.ContainsKey(t)) Pending[t] = Backend.Collect(t);
    }

    public void Dispose() => Backend.Dispose();
}

public static class Snapshot
{
    const string Magic = "QSPOP";
    const int Format = 1;

    public static void Save(string path, Simulation sim)
    {
        sim.CollectOutstanding();
        var state = sim.Backend.Download();
        var tmp = path + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Magic); w.Write(Format); w.Write(Settings.GeneratorVersion);
            w.Write(System.Text.Json.JsonSerializer.Serialize(sim.S));
            w.Write(WorldHash(sim.W)); w.Write(sim.NextTick); w.Write(sim.S.Agents);
            foreach (var array in state.Arrays) foreach (uint v in array) w.Write(v);
            var b = sim.Book;
            w.Write(b.Facts.Count);
            foreach (var f in b.Facts) { w.Write(f.Id); w.Write(f.Tick); w.Write(f.X); w.Write(f.Y); w.Write(f.Kind); w.Write(f.Provenance); }
            for (int i = 0; i < 64; i++) { w.Write(b.SlotFact[i]); w.Write(b.SlotVersion[i]); w.Write(b.SlotCredibility[i]); w.Write(b.SlotStart[i]); }
            w.Write(b.Heard.Count);
            foreach (var (k, v) in b.Heard.OrderBy(x => x.Key)) { w.Write(k); w.Write(v); }
            w.Write(b.Dropped);
            w.Write(sim.Pending.Count);
            foreach (var r in sim.Pending.Values.OrderBy(x => x.Tick))
            {
                w.Write(r.Tick);
                foreach (uint v in r.Counters) w.Write(v);
                foreach (uint v in r.Agg) w.Write(v);
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static (Settings, World, AgentState, uint NextTick, RumorBook Book, Dictionary<uint, Readback> Pending) Load(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (r.ReadString() != Magic || r.ReadInt32() != Format || r.ReadUInt32() != Settings.GeneratorVersion)
            throw new InvalidDataException("Unsupported population snapshot");
        var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(r.ReadString()) ?? throw new InvalidDataException("Missing settings");
        var world = Generator.Create(s).Item1;
        if (r.ReadUInt64() != WorldHash(world)) throw new InvalidDataException("Regenerated world differs from snapshot");
        uint next = r.ReadUInt32();
        if (r.ReadInt64() != s.Agents) throw new InvalidDataException("Agent count mismatch");
        var state = new AgentState(s.Agents);
        foreach (var array in state.Arrays) for (long i = 0; i < array.Length; i++) array[i] = r.ReadUInt32();
        var book = new RumorBook(s, world);
        int facts = r.ReadInt32();
        for (int i = 0; i < facts; i++) book.Facts.Add(new Fact(r.ReadInt32(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32(), r.ReadString(), r.ReadString()));
        for (int i = 0; i < 64; i++) { book.SlotFact[i] = r.ReadInt32(); book.SlotVersion[i] = r.ReadUInt32(); book.SlotCredibility[i] = r.ReadUInt32(); book.SlotStart[i] = r.ReadUInt32(); }
        int heard = r.ReadInt32();
        for (int i = 0; i < heard; i++) book.Heard[r.ReadInt32()] = r.ReadInt64();
        book.Dropped = r.ReadInt64();
        var pending = new Dictionary<uint, Readback>();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var rb = new Readback { Tick = r.ReadUInt32() };
            for (int k = 0; k < rb.Counters.Length; k++) rb.Counters[k] = r.ReadUInt32();
            for (int k = 0; k < rb.Agg.Length; k++) rb.Agg[k] = r.ReadUInt32();
            pending[rb.Tick] = rb;
        }
        return (s, world, state, next, book, pending);
    }

    public static ulong WorldHash(World w)
    {
        ulong h = 1469598103934665603UL;
        foreach (var array in new[] { w.Settlements, w.AdjOff, w.Adj })
            foreach (uint v in array) h = (h ^ v) * 1099511628211UL;
        return h;
    }
}
