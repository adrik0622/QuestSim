using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using QuestSim.Population;

// Usage: check | bench | run, with --agents 1e6 --ticks 60 --backend cpu,gpu --delay 2 --pace-ms 0 --out FILE --image FILE.png
var command = args.FirstOrDefault() ?? "check";
var options = new Dictionary<string, string>();
for (int i = 1; i + 1 < args.Length; i += 2) options[args[i].TrimStart('-')] = args[i + 1];
string Opt(string key, string fallback) => options.GetValueOrDefault(key, fallback);
long Count(string text) => (long)double.Parse(text, CultureInfo.InvariantCulture);
string shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
Settings Configure(long agents) => new Settings
{
    Agents = agents, Seed = ulong.Parse(Opt("seed", "1")), WorldKm = uint.Parse(Opt("world-km", "200")),
    ReadbackDelay = uint.Parse(Opt("delay", "2")), EventEveryTicks = uint.Parse(Opt("event-every", "60")),
    RumorLifetimeTicks = uint.Parse(Opt("lifetime", "900"))
};
IBackend Make(string name, Settings s, World w, AgentState a) =>
    name == "gpu" ? new GpuBackend(s, w, a, shaderDir) : new CpuBackend(s, w, a);

try
{
    return command switch
    {
        "check" => Checks.Run(shaderDir, Opt("backend", "cpu,gpu").Contains("gpu")),
        "bench" => Bench(),
        "run" => RunOnce(),
        "density" => Density(),
        _ => throw new ArgumentException("Commands: check, bench, run")
    };
}
catch (Exception e)
{
    Console.Error.WriteLine("FAIL " + e);
    return 1;
}

int Bench()
{
    int ticks = int.Parse(Opt("ticks", "30")), pace = int.Parse(Opt("pace-ms", "0"));
    var results = new List<object>();
    foreach (long n in Opt("agents", "1e5,1e6,1e7").Split(',').Select(Count))
        foreach (string backend in Opt("backend", "cpu,gpu").Split(','))
        {
            var s = Configure(n); s.Validate();
            var clock = Stopwatch.StartNew();
            var (w, a) = Generator.Create(s);
            double generate = clock.Elapsed.TotalMilliseconds; clock.Restart();
            using var sim = new Simulation(s, w, Make(backend, s, w, a));
            double upload = clock.Elapsed.TotalMilliseconds;
            var sums = new Dictionary<string, double>();
            var steps = new List<double>();
            long heard = 0, overflow = 0;
            sim.Observed = r => { heard += r.Counters[0]; overflow += r.Counters[1]; };
            for (int t = 0; t < ticks + 3; t++)
            {
                long start = Stopwatch.GetTimestamp();
                sim.Step();
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (t >= 3)
                {
                    steps.Add(ms);
                    foreach (var (k, v) in sim.Backend.LastTimingsMs) sums[k] = sums.GetValueOrDefault(k) + v;
                }
                if (pace > 0) Thread.Sleep(Math.Max(0, pace - (int)ms));
            }
            sim.CollectOutstanding();
            var roundTrips = new List<double>();
            if (sim.Backend is GpuBackend gpu)
                for (uint k = 0; k < 5; k++) roundTrips.Add(gpu.RoundTrip(sim.Book.Prepare(sim.NextTick + 10 + k)).Ms);
            steps.Sort();
            var row = new Dictionary<string, object>
            {
                ["backend"] = backend, ["agents"] = n, ["ticks"] = ticks, ["paceMs"] = pace, ["delay"] = s.ReadbackDelay,
                ["generateMs"] = generate, ["uploadMs"] = upload,
                ["stepMeanMs"] = steps.Average(), ["stepP50Ms"] = steps[steps.Count / 2], ["stepMaxMs"] = steps[^1],
                ["phaseMeanMs"] = sums.ToDictionary(x => x.Key, x => x.Value / ticks),
                ["roundTripMs"] = roundTrips, ["heardEvents"] = heard, ["eventOverflow"] = overflow,
                ["facts"] = sim.Book.Facts.Count, ["peakRssBytes"] = Process.GetCurrentProcess().PeakWorkingSet64,
                ["gpuDeviceBytes"] = sim.Backend is GpuBackend g ? g.DeviceBytes : 0, ["device"] = sim.Backend is GpuBackend d ? d.DeviceName : Environment.ProcessorCount + " CPU threads"
            };
            results.Add(row);
            Console.WriteLine($"{backend,-4} {n,11:N0} agents: step mean {steps.Average(),8:F2} ms p50 {steps[steps.Count / 2],8:F2} max {steps[^1],8:F2} | " +
                string.Join(", ", sums.Select(x => $"{x.Key} {x.Value / ticks:F2}")) +
                (roundTrips.Count > 0 ? $" | round trip {roundTrips.Average():F2} ms" : "") + $" | heard {heard:N0}");
            a = null!; GC.Collect();
        }
    string output = Opt("out", ".artifacts/population/bench.json");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, JsonSerializer.Serialize(new { commit = Environment.GetEnvironmentVariable("QUESTSIM_COMMIT"), os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("RESULTS " + output);
    return 0;
}

// Diagnostic: how many list entries and real contacts a sample of humans scans (explains spread cost by scale).
int Density()
{
    foreach (long n in Opt("agents", "1e7,1e8").Split(',').Select(Count))
    {
        var s = Configure(n);
        var (w, a) = Generator.Create(s);
        var heads = new uint[s.TableMask + 1L]; Array.Fill(heads, uint.MaxValue);
        var next = new uint[n];
        for (uint i = 0; i < n; i++) { uint h = H.CellOf(a.PosX[i] >> 16, a.PosY[i] >> 16, s.TableMask); next[i] = heads[h]; heads[h] = i; }
        long scanned = 0, contacts = 0, humans = 0, maxScan = 0;
        for (long i = 0; i < n; i += Math.Max(1, n / 200_000))
        {
            if (a.Info[i] >> 31 == 1) continue;
            humans++;
            long local = 0;
            uint cx = a.PosX[i] >> 16, cy = a.PosY[i] >> 16;
            for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                for (uint b = heads[H.CellOf(cx + (uint)ox, cy + (uint)oy, s.TableMask)]; b != uint.MaxValue; b = next[b])
                {
                    local++;
                    long dx = Math.Abs((long)a.PosX[b] - a.PosX[i]), dy = Math.Abs((long)a.PosY[b] - a.PosY[i]);
                    if (b != i && dx * dx + dy * dy <= (long)s.ContactRadiusMm * s.ContactRadiusMm) contacts++;
                }
            scanned += local; maxScan = Math.Max(maxScan, local);
        }
        Console.WriteLine($"{n,13:N0} agents, {w.Count:N0} settlements: per human scanned {scanned / (double)humans:F1} (max {maxScan}), contacts {contacts / (double)humans:F2}");
    }
    return 0;
}

int RunOnce()
{
    var s = Configure(Count(Opt("agents", "1e6"))); s.Validate();
    var (w, a) = Generator.Create(s);
    using var sim = new Simulation(s, w, Make(Opt("backend", "gpu"), s, w, a));
    var coverage = new List<uint>();
    sim.Observed = r => coverage.Add(r.Counters.Skip(2).Take(64).Aggregate(0u, Math.Max));
    for (int t = 0; t < int.Parse(Opt("ticks", "120")); t++) sim.Step();
    sim.CollectOutstanding();
    var state = sim.Backend.Download();
    Console.WriteLine($"tick {sim.NextTick} facts {sim.Book.Facts.Count} heard {string.Join(",", sim.Book.Heard.Select(x => $"{x.Key}:{x.Value}"))}");
    Console.WriteLine("max carriers per tick: " + string.Join(" ", coverage));
    if (options.TryGetValue("image", out var image)) Picture.Write(image, s, state);
    return 0;
}

static class Picture
{
    // Red: zombies, green: humans, blue: humans carrying any rumor. Written as PPM, converted with ImageMagick.
    public static void Write(string png, Settings s, AgentState a)
    {
        const int size = 512;
        var counts = new int[3, size * size];
        for (long i = 0; i < s.Agents; i++)
        {
            int px = (int)((long)a.PosX[i] * size / (s.WorldMax + 1L)), py = (int)((long)a.PosY[i] * size / (s.WorldMax + 1L)), cell = py * size + px;
            if (a.Info[i] >> 31 == 1) counts[0, cell]++;
            else { counts[1, cell]++; if ((a.MaskLo[i] | a.MaskHi[i]) != 0) counts[2, cell]++; }
        }
        var max = Enumerable.Range(0, 3).Select(c => Math.Log(1 + Enumerable.Range(0, size * size).Max(i => counts[c, i]))).ToArray();
        string ppm = Path.ChangeExtension(png, ".ppm");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!);
        using (var f = new BinaryWriter(File.Create(ppm)))
        {
            f.Write(System.Text.Encoding.ASCII.GetBytes($"P6 {size} {size} 255\n"));
            for (int i = 0; i < size * size; i++)
                for (int c = 0; c < 3; c++) f.Write((byte)(max[c] == 0 ? 0 : 255 * Math.Log(1 + counts[c, i]) / max[c]));
        }
        Process.Start("magick", [ppm, png])!.WaitForExit();
        File.Delete(ppm);
        Console.WriteLine("IMAGE " + png);
    }
}
