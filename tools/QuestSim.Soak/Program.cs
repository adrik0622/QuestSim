// Soak run (F19): fast-forward a seeded population and its settlement economy for weeks of game time, record a daily
// timeline and map images, and fail on degenerate history. Headless; no server, players or engine.
//   dotnet run --project tools/QuestSim.Soak -c Release -- [--days 30] [--agents 100000] [--seed N] [--out DIR] [--map-every 5]
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using QuestSim.Domain;

var options = args.Select((a, i) => (a, i)).Where(p => p.a.StartsWith("--")).ToDictionary(p => p.a[2..], p => p.i + 1 < args.Length ? args[p.i + 1] : "");
int days = int.Parse(options.GetValueOrDefault("days", "30")), agents = int.Parse(options.GetValueOrDefault("agents", "100000"));
int mapEvery = int.Parse(options.GetValueOrDefault("map-every", "5"));
ulong seed = ulong.Parse(options.GetValueOrDefault("seed", WorldSave.DefaultSeed.ToString()));
string output = options.GetValueOrDefault("out", Path.Combine(".artifacts", "soak", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(output);

var clock = Stopwatch.StartNew();
var city = new CityGen(seed);
var pop = new Population(city, agents);
var states = Economy.Create(pop, city);
var factions = Politics.Create();
var factionShort = new int[Population.HumanFactions];
var actions = new List<FactionAction>();
var moves = new Dictionary<string, int>();   // faction:kind -> count
var factionEvents = new List<FactionEvent>();
int n = states.Count;
int[] FactionResidents() => Enumerable.Range(0, Population.HumanFactions).Select(f => Enumerable.Range(0, n).Where(s => pop.SettlementFaction[s] == f).Sum(s => pop.Residents[s])).ToArray();
var startResidents = FactionResidents();
var timeline = new List<object>();
var eventCounts = new Dictionary<string, int>();
var crisisHours = new int[Economy.Goods];      // after day 3: hours a good was short in every human faction at once
int refugees = 0, minOrder = 100, maxOrder = 0, born = 0, overruns = 0, migrated = 0, lateOverruns = 0;
long startZombies = pop.TargetZombies;
int startHumans = pop.Residents.Sum();
long hour = 0;
var events = new List<EconomyEvent>();
Map(0);
for (int day = 1; day <= days; day++)
{
    for (int h = 0; h < 24; h++)
    {
        for (int t = 0; t < Economy.TicksPerHour; t++) pop.Step();
        var tally = new EconomyTally();
        events.Clear();
        Economy.Hour(pop, states, ++hour, events, tally, null, factionShort);
        factionEvents.Clear();
        Politics.Hour(pop, states, factions, actions, hour, factionEvents);
        foreach (var e in factionEvents)
        {
            string key = $"{Population.FactionNames[e.From]}:{e.Kind}{(e.Kind == "raid" ? (e.Success ? "-took" : "-repelled") : "")}";
            moves[key] = moves.GetValueOrDefault(key) + 1;
        }
        refugees += tally.Refugees; born += tally.Born; overruns += tally.Overrun; migrated += tally.Migrated;
        if (day > days / 2) lateOverruns += tally.Overrun;
        foreach (var e in events) eventCounts[$"{e.Kind}:{Economy.GoodNames[(int)e.Good]}"] = eventCounts.GetValueOrDefault($"{e.Kind}:{Economy.GoodNames[(int)e.Good]}") + 1;
        for (int g = 0; g < Economy.Goods; g++)
            if (day > 3 && Enumerable.Range(0, Population.HumanFactions).All(f => states.Any(s => pop.SettlementFaction[s.Index] == f && (s.Reported >> g & 1) != 0)))
                crisisHours[g]++;
        minOrder = Math.Min(minOrder, states.Min(s => s.Order)); maxOrder = Math.Max(maxOrder, states.Max(s => s.Order));
    }
    var residents = FactionResidents();
    var record = new
    {
        day, residents, humans = pop.Residents.Sum(), refugees, born, overruns, ruined = states.Count(s => s.Damage >= Economy.RuinedDamage),
        relations = new[] { factions[0].Relations[1], factions[1].Relations[0], factions[0].Relations[2], factions[2].Relations[0], factions[1].Relations[2], factions[2].Relations[1] },
        lastMoves = factions.Take(Population.HumanFactions).Select(f => f.LastMove).ToArray(),
        shortFood = Enumerable.Range(0, n).Count(s => (states[s].Reported & 1) != 0),
        shortMedicine = Enumerable.Range(0, n).Count(s => (states[s].Reported & 2) != 0),
        shortByFaction = Enumerable.Range(0, Population.HumanFactions).Select(f => Enumerable.Range(0, n).Count(s => states[s].Reported != 0 && pop.SettlementFaction[s] == f)).ToArray(),
        meanOrder = Math.Round(states.Average(s => s.Order), 1),
        unrest = states.Count(s => s.Order < Economy.UnrestOrder),
        daysOfStock = Enumerable.Range(0, Economy.Goods).Select(g => Math.Round(Enumerable.Range(0, n).Sum(s => states[s].Stock[g]) / Math.Max(1.0, pop.Residents.Sum() * (double)Need(g)), 1)).ToArray(),
        factionFoodDays = Enumerable.Range(0, Population.HumanFactions).Select(f => Math.Round(Enumerable.Range(0, n).Where(s => pop.SettlementFaction[s] == f).Sum(s => states[s].Stock[0]) /
            Math.Max(1.0, Enumerable.Range(0, n).Where(s => pop.SettlementFaction[s] == f).Sum(s => pop.Residents[s]) * 1000.0), 1)).ToArray()
    };
    timeline.Add(record);
    Console.WriteLine($"day {day,3}: humans {record.humans,7:N0} by faction {string.Join("/", residents)}, short food {record.shortFood,3} medicine {record.shortMedicine,3} (by faction {string.Join("/", record.shortByFaction)}), " +
        $"order {record.meanOrder} (unrest {record.unrest}), stock days {string.Join("/", record.daysOfStock)}, faction food days {string.Join("/", record.factionFoodDays)}, refugees {refugees:N0}, born {born:N0}, zombies {pop.Members[3] + pop.Members[4]:N0} (+{migrated:N0} from the edges), overrun {overruns} (in ruins {record.ruined}), " +
        $"relations U-T {record.relations[0]}/{record.relations[1]} U-C {record.relations[2]}/{record.relations[3]} T-C {record.relations[4]}/{record.relations[5]}");
    if (day % mapEvery == 0 || day == days) Map(day);
}

File.WriteAllText(Path.Combine(output, "timeline.json"), JsonSerializer.Serialize(new { seed, agents, days, events = eventCounts, moves, timeline }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"events: {string.Join(", ", eventCounts.OrderBy(e => e.Key).Select(e => $"{e.Key} {e.Value}"))}");
Console.WriteLine($"faction moves: {string.Join(", ", moves.OrderBy(e => e.Key).Select(e => $"{e.Key} {e.Value}"))}");
Console.WriteLine($"{days} game days ({hour} hours, {pop.Tick:N0} population ticks, {agents:N0} agents) in {clock.Elapsed.TotalSeconds:F0} s; output {output}");

// Degenerate history checks.
int failures = 0;
void Check(bool ok, string text) { Console.WriteLine((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; }
var endResidents = FactionResidents();
Check(crisisHours.All(h => h < 24), $"no good is short in every faction for a day or more (worldwide crisis hours: {string.Join("/", crisisHours)})");
Check(eventCounts.Keys.Any(k => k.StartsWith("shortage")) && eventCounts.Keys.Any(k => k.StartsWith("recovered")),
    $"factions run short and recover ({eventCounts.Where(e => e.Key.StartsWith("shortage")).Sum(e => e.Value)} shortages, {eventCounts.Where(e => e.Key.StartsWith("recovered")).Sum(e => e.Value)} recoveries)");
for (int f = 0; f < Population.HumanFactions; f++)
    Check(moves.Keys.Any(k => k.StartsWith(Population.FactionNames[f] + ":")), $"{Population.FactionNames[f]} makes moves ({string.Join(", ", moves.Where(m => m.Key.StartsWith(Population.FactionNames[f] + ":")).Select(m => $"{m.Key.Split(':')[1]} {m.Value}"))})");
Check(moves.Keys.Any(k => k.EndsWith(":trade")), "factions trade");
Check(Enumerable.Range(0, Population.HumanFactions).Any(a => Enumerable.Range(0, Population.HumanFactions).Any(b => a != b && factions[a].Relations[b] > -100)),
    $"relations are not pinned at -100 everywhere (end: {string.Join(" ", factions.Take(3).Select(f => string.Join("/", f.Relations.Take(3))))})");
for (int f = 0; f < Population.HumanFactions; f++)
    Check(endResidents[f] * 2 > startResidents[f], $"{Population.FactionNames[f]} keeps more than half its people ({startResidents[f]:N0} -> {endResidents[f]:N0})");
Check(pop.Residents.Sum() * 100 >= startHumans * 85 && pop.Residents.Sum() * 100 <= startHumans * 130,
    $"births keep the living population steady ({startHumans:N0} -> {pop.Residents.Sum():N0}; {born:N0} born, zombies {pop.Members[3] + pop.Members[4]:N0})");
Check(overruns > 0 && eventCounts.Keys.Any(k => k.StartsWith("rebuilt")) && lateOverruns > 0,
    $"the dead overrun settlements all along and they rebuild ({overruns} overruns, {lateOverruns} in the second half; {eventCounts.Where(e => e.Key.StartsWith("threat")).Sum(e => e.Value)} great hordes)");
Check((pop.Members[3] + pop.Members[4]) * 100 >= startZombies * 70, $"the dead stay a force ({startZombies:N0} at the start, {pop.Members[3] + pop.Members[4]:N0} now; {migrated:N0} migrated in)");
Check(minOrder >= 0 && maxOrder <= 100 && states.All(s => s.Stock.All(v => v >= 0)), $"order stays within 0-100 ({minOrder}-{maxOrder}) and no stock goes negative");
return failures == 0 ? 0 : 1;

static int Need(int good) => good switch { 0 => 1000, 1 => 20, 2 => 10, _ => 50 };

// Settlements as discs on a 512 px map of the populated area: faction colour, brightness by days of food (dark = hungry),
// a red ring around those reporting a shortage. Written as a PNG without dependencies.
void Map(int day)
{
    const int size = 512;
    var rgb = new byte[size * size * 3];
    for (int i = 0; i < rgb.Length; i++) rgb[i] = 24;
    int minX = pop.Settlements.Min(s => s.X) - 400, maxX = pop.Settlements.Max(s => s.X) + 400, minZ = pop.Settlements.Min(s => s.Z) - 400, maxZ = pop.Settlements.Max(s => s.Z) + 400;
    double scale = (size - 1) / (double)Math.Max(maxX - minX, maxZ - minZ);
    (int, int) At(int x, int z) => ((int)((x - minX) * scale), (int)((z - minZ) * scale));
    void Put(int px, int pz, byte r, byte g, byte b) { if (px < 0 || pz < 0 || px >= size || pz >= size) return; int k = (pz * size + px) * 3; rgb[k] = r; rgb[k + 1] = g; rgb[k + 2] = b; }
    for (int a = 0; a < n; a++)
        foreach (int b in pop.Neighbours(a).Where(b => b > a))
        {
            var (ax, az) = At(pop.Settlements[a].X, pop.Settlements[a].Z); var (bx, bz) = At(pop.Settlements[b].X, pop.Settlements[b].Z);
            int steps = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az)) + 1;
            for (int k = 0; k <= steps; k++) Put(ax + (bx - ax) * k / steps, az + (bz - az) * k / steps, 60, 60, 60);
        }
    byte[][] colours = [[90, 140, 230], [230, 190, 90], [210, 50, 60]];
    for (int s = 0; s < n; s++)
    {
        var (cx, cz) = At(pop.Settlements[s].X, pop.Settlements[s].Z);
        int radius = 2 + (int)Math.Sqrt(pop.Residents[s] / 40.0);
        double light = Math.Clamp(Economy.Days10(states[s], pop.Residents[s], Good.Food) / 100.0, .15, 1);
        var c = colours[pop.SettlementFaction[s]];
        for (int dz = -radius - 2; dz <= radius + 2; dz++)
            for (int dx = -radius - 2; dx <= radius + 2; dx++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 <= radius * radius) Put(cx + dx, cz + dz, (byte)(c[0] * light), (byte)(c[1] * light), (byte)(c[2] * light));
                else if (states[s].Reported != 0 && d2 <= (radius + 2) * (radius + 2)) Put(cx + dx, cz + dz, 255, 40, 40);
            }
    }
    WritePng(Path.Combine(output, $"map-day{day:00}.png"), size, rgb);
}

static void WritePng(string path, int size, byte[] rgb)
{
    var raw = new MemoryStream();
    for (int y = 0; y < size; y++) { raw.WriteByte(0); raw.Write(rgb, y * size * 3, size * 3); }
    var packed = new MemoryStream();
    using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(z);
    using var file = File.Create(path);
    file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    void Chunk(string type, byte[] data)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        file.Write(BigEndian((uint)data.Length)); file.Write(body); file.Write(BigEndian(Crc(body)));
    }
    Chunk("IHDR", [.. BigEndian((uint)size), .. BigEndian((uint)size), 8, 2, 0, 0, 0]);
    Chunk("IDAT", packed.ToArray());
    Chunk("IEND", []);
}

static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

static uint Crc(byte[] data)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte b in data)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
    }
    return ~crc;
}
