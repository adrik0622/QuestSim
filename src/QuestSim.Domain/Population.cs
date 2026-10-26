using System.Collections.Concurrent;
using System.Numerics;

namespace QuestSim.Domain;

// The coarse world population (E1 model, CPU path). One tick = 48 game seconds. Agent ID = slot index, never reused.
// Positions are uint millimetres offset by CityGen.Half so the whole 40 km world is non-negative.
// Info bits: 31 zombie, 30 travelling, 29 detailed (owned by the 20 Hz authority), 28 dead, 24-27 faction, 0-23 home settlement.
public sealed class Population
{
    public const uint Zombie = 1u << 31, Travelling = 1u << 30, Detailed = 1u << 29, Dead = 1u << 28, None = uint.MaxValue;
    public const int GameSecondsPerTick = 48, HumanFactions = 3, Hostile = 2;
    public const uint StepHuman = 1300 * GameSecondsPerTick, StepZombie = 500 * GameSecondsPerTick, WanderRadius = 250_000, HordeSpread = 300_000;
    public static readonly string[] FactionNames = ["Survivors' Union", "Free Traders", "The Hollow Choir", "Shambling Mass", "The Pale Tide"];
    public readonly uint[] PosX, PosZ, TgtX, TgtZ, Info, MaskLo, MaskHi;
    readonly uint[] heads, next, nextLo, nextHi;
    public uint TableMask { get; }
    public int Count { get; }
    public ulong Seed { get; }
    public uint Tick { get; private set; }
    public List<Settlement> Settlements { get; }
    public int[] SettlementFaction { get; }
    readonly uint[] settleX, settleZ, adjOff, adj;

    public static uint ToMm(double metres) => (uint)Math.Clamp((long)Math.Round((metres + CityGen.Half) * 1000), 0, 2L * CityGen.Half * 1000);
    public static double ToMetres(uint mm) => mm / 1000.0 - CityGen.Half;
    public static uint Mix(uint x) => CityGen.Mix(x);
    public static uint Hash4(uint a, uint b, uint c, uint d) => Mix(a ^ Mix(b ^ Mix(c ^ Mix(d))));

    public Population(CityGen city, int count, double humanFraction = 0.75)
    {
        Count = count; Seed = city.Seed;
        (Settlements, var links) = city.Settlements();
        int s = Settlements.Count;
        settleX = Settlements.Select(x => ToMm(x.X)).ToArray(); settleZ = Settlements.Select(x => ToMm(x.Z)).ToArray();
        SettlementFaction = Settlements.Select(x => x.Spawn ? 0 : Hash4((uint)Seed, 77, (uint)x.Index, 0) % 10 == 0 ? Hostile : x.X < 0 ? 0 : 1).ToArray();
        var lists = Enumerable.Range(0, s).Select(_ => new List<uint>()).ToArray();
        foreach (var (a, b) in links) { lists[a].Add((uint)b); lists[b].Add((uint)a); }
        adjOff = new uint[s + 1]; var all = new List<uint>();
        for (int i = 0; i < s; i++) { adjOff[i] = (uint)all.Count; all.AddRange(lists[i]); }
        adjOff[s] = (uint)all.Count; adj = all.ToArray();
        PosX = new uint[count]; PosZ = new uint[count]; TgtX = new uint[count]; TgtZ = new uint[count]; Info = new uint[count];
        MaskLo = new uint[count]; MaskHi = new uint[count]; next = new uint[count]; nextLo = new uint[count]; nextHi = new uint[count];
        TableMask = (uint)BitOperations.RoundUpToPowerOf2((ulong)Math.Max(1024, count * 2)) - 1;
        heads = new uint[TableMask + 1L];
        uint hordes = (uint)Math.Max(2, count * (1 - humanFraction) / 5000);
        uint humanCut = (uint)(humanFraction * uint.MaxValue);
        for (uint i = 0; i < count; i++)
        {
            uint home, info;
            if (Hash4((uint)Seed, 1, i, 0) < humanCut)
            {
                home = Hash4((uint)Seed, 2, i, 0) % (uint)s;
                info = ((uint)SettlementFaction[home] << 24) | home;
            }
            else
            {
                uint horde = Hash4((uint)Seed, 3, i, 0) % hordes;
                home = HordeTarget(horde, 0);
                info = Zombie | ((uint)(HumanFactions + horde % 2) << 24) | horde;
            }
            uint spread = (info & Zombie) != 0 ? HordeSpread : WanderRadius;
            PosX[i] = TgtX[i] = Offset(settleX[home], Hash4((uint)Seed, 4, i, 0), spread);
            PosZ[i] = TgtZ[i] = Offset(settleZ[home], Hash4((uint)Seed, 5, i, 0), spread);
            Info[i] = info;
        }
        BuildGrid();
    }

    static uint Offset(uint centre, uint hash, uint radius) =>
        (uint)Math.Clamp((long)centre + hash % (2 * radius + 1) - radius, 0, 2L * CityGen.Half * 1000);

    // Hordes wander between settlements, never into the spawn district.
    uint HordeTarget(uint horde, uint tick)
    {
        uint target = Hash4((uint)Seed, 9, horde, tick / 75) % (uint)Settlements.Count;
        return Settlements[(int)target].Spawn ? (target + 1) % (uint)Settlements.Count : target;
    }

    public static bool IsZombie(uint info) => (info & Zombie) != 0;
    public static int FactionOf(uint info) => (int)((info >> 24) & 15);

    public void Step()
    {
        uint tick = Tick;
        Parallel.ForEach(Partitioner.Create(0, Count, Math.Max(4096, Count / (Environment.ProcessorCount * 4))), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++) Move((uint)i, tick);
        });
        BuildGrid();
        Tick++;
    }

    void Move(uint i, uint tick)
    {
        uint info = Info[i];
        if ((info & (Detailed | Dead)) != 0) return;
        uint x = PosX[i], z = PosZ[i], tx = TgtX[i], tz = TgtZ[i], anchor = info & 0xFFFFFF;
        bool zombie = IsZombie(info), travelling = (info & Travelling) != 0;
        uint step = zombie ? StepZombie : StepHuman;
        int dx = (int)tx - (int)x, dz = (int)tz - (int)z;
        uint ax = (uint)Math.Abs(dx), az = (uint)Math.Abs(dz);
        bool arrived = ax <= step && az <= step;
        if (arrived) { x = tx; z = tz; }
        else
        {
            int shift = 0;
            while ((Math.Max(ax, az) >> shift) >= 32768) shift++;
            uint sx = ax >> shift, sz = az >> shift, d = (uint)Math.Sqrt(sx * sx + sz * sz);
            while (d * d > sx * sx + sz * sz) d--;
            uint mx = sx * step / Math.Max(1, d), mz = sz * step / Math.Max(1, d);
            x = dx < 0 ? x - mx : x + mx; z = dz < 0 ? z - mz : z + mz;
        }
        if (arrived)
        {
            if (zombie)
            {
                uint target = HordeTarget(anchor, tick);
                tx = Offset(settleX[target], Hash4((uint)Seed, tick, i, 5), HordeSpread);
                tz = Offset(settleZ[target], Hash4((uint)Seed, tick, i, 6), HordeSpread);
            }
            else if (!travelling && adjOff[anchor + 1] > adjOff[anchor] && Hash4((uint)Seed, tick, i, 1) % 100 < 3)
            {
                uint count = adjOff[anchor + 1] - adjOff[anchor];
                anchor = adj[adjOff[anchor] + Hash4((uint)Seed, tick, i, 2) % count];
                travelling = true; tx = settleX[anchor]; tz = settleZ[anchor];
                info = (info & ~0xFFFFFFu) | anchor;
            }
            else
            {
                travelling = false;
                tx = Offset(settleX[anchor], Hash4((uint)Seed, tick, i, 3), WanderRadius);
                tz = Offset(settleZ[anchor], Hash4((uint)Seed, tick, i, 4), WanderRadius);
            }
        }
        PosX[i] = x; PosZ[i] = z; TgtX[i] = tx; TgtZ[i] = tz;
        Info[i] = travelling ? info | Travelling : info & ~Travelling;
    }

    static uint CellOf(uint cx, uint cz, uint mask) => Mix(cx * 0x9E3779B1u ^ Mix(cz)) & mask;

    void BuildGrid()
    {
        Array.Fill(heads, None);
        for (uint i = 0; i < Count; i++)
        {
            if ((Info[i] & Dead) != 0) { next[i] = None; continue; }
            uint h = CellOf(PosX[i] >> 16, PosZ[i] >> 16, TableMask);
            next[i] = heads[h]; heads[h] = i;
        }
    }

    // Living agents within a radius (metres) of a point, from the grid of the last tick. Detailed agents included.
    public List<uint> Near(double x, double z, double radius)
    {
        var found = new List<uint>();
        uint mx = ToMm(x), mz = ToMm(z), r = (uint)(radius * 1000);
        long r2 = (long)r * r;
        int reach = (int)(r >> 16) + 1;
        uint cx = mx >> 16, cz = mz >> 16;
        var seen = new HashSet<uint>();
        for (int ox = -reach; ox <= reach; ox++)
            for (int oz = -reach; oz <= reach; oz++)
            {
                uint h = CellOf((uint)(cx + ox), (uint)(cz + oz), TableMask);
                if (!seen.Add(h)) continue;
                for (uint b = heads[h]; b != None; b = next[b])
                {
                    long dx = (long)PosX[b] - mx, dz = (long)PosZ[b] - mz;
                    if (dx * dx + dz * dz <= r2 && (Info[b] & Dead) == 0) found.Add(b);
                }
            }
        found.Sort();
        return found;
    }

    // Generated identity: never stored, identical everywhere for the same seed and agent.
    static readonly string[] First = ["Ada", "Bram", "Cora", "Dell", "Esme", "Finn", "Greer", "Hal", "Ines", "Jory", "Kit", "Lena", "Milo", "Nell", "Otto", "Pia", "Quin", "Rhea", "Sol", "Tess", "Uri", "Vera", "Wade", "Yara"];
    static readonly string[] Last = ["Ash", "Barrow", "Cole", "Dunn", "Ember", "Fisk", "Grove", "Hale", "Irons", "Kerr", "Lowe", "Marsh", "North", "Pike", "Reed", "Stone", "Thorne", "Vale", "Wren", "Yates"];
    public string NameOf(uint i) => IsZombie(Info[i]) ? "Zombie" : $"{First[Hash4((uint)Seed, 11, i, 0) % First.Length]} {Last[Hash4((uint)Seed, 12, i, 0) % Last.Length]}";
    public int BaseHealth(uint i) => IsZombie(Info[i]) ? 60 : 80 + (int)(Hash4((uint)Seed, 13, i, 0) % 21);
}
