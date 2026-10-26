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

    // Rumor inputs decided by the authority (credibility 16-bit per slot, trust 16-bit per faction pair), and outputs it reads back.
    public sealed record RumorInput(uint[] Credibility, uint[] Trust, uint ActiveLo, uint ActiveHi, uint FadeThreshold);
    public uint[] Carriers { get; } = new uint[64];
    public long[] Members { get; } = new long[16];
    public const uint ContactRadius = 30_000;

    public void Step() => Step(null);

    public void Step(RumorInput? rumors)
    {
        uint tick = Tick;
        var partitions = Partitioner.Create(0, Count, Math.Max(4096, Count / (Environment.ProcessorCount * 4)));
        Parallel.ForEach(partitions, range => { for (int i = range.Item1; i < range.Item2; i++) Move((uint)i, tick); });
        BuildGrid();
        var carriers = new uint[64];
        var members = new long[16];
        bool spreading = rumors is not null && (rumors.ActiveLo | rumors.ActiveHi) != 0;
        Parallel.ForEach(partitions, () => (Carriers: new uint[64], Members: new long[16]), (range, _, local) =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                uint info = Info[i];
                if ((info & Dead) == 0) local.Members[FactionOf(info)]++;
                if (spreading) Spread((uint)i, tick, rumors!, local.Carriers);
                else
                {
                    nextLo[i] = MaskLo[i]; nextHi[i] = MaskHi[i];
                    for (uint m = MaskLo[i]; m != 0; m &= m - 1) local.Carriers[BitOperations.TrailingZeroCount(m)]++;
                    for (uint m = MaskHi[i]; m != 0; m &= m - 1) local.Carriers[32 + BitOperations.TrailingZeroCount(m)]++;
                }
            }
            return local;
        }, local => { lock (carriers) for (int k = 0; k < 64; k++) { carriers[k] += local.Carriers[k]; } lock (members) for (int k = 0; k < 16; k++) members[k] += local.Members[k]; });
        Array.Copy(nextLo, MaskLo, Count); Array.Copy(nextHi, MaskHi, Count);
        Array.Copy(carriers, Carriers, 64); Array.Copy(members, Members, 16);
        Tick++;
    }

    // E1 spread rule: reads previous masks only, deterministic per (agent, neighbour, rumor), so thread order never matters.
    void Spread(uint i, uint tick, RumorInput r, uint[] carriers)
    {
        uint info = Info[i];
        if (IsZombie(info) || (info & Dead) != 0) { nextLo[i] = 0; nextHi[i] = 0; return; }
        uint x = PosX[i], z = PosZ[i], faction = (uint)FactionOf(info) & 7, oldLo = MaskLo[i], oldHi = MaskHi[i], heardLo = 0, heardHi = 0;
        uint cx = x >> 16, cz = z >> 16;
        ulong c2 = (ulong)ContactRadius * ContactRadius;
        for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
                for (uint b = heads[CellOf(cx + (uint)ox, cz + (uint)oz, TableMask)]; b != None; b = next[b])
                {
                    uint binfo = Info[b];
                    if (b == i || IsZombie(binfo)) continue;
                    long dx = (long)PosX[b] - x, dz = (long)PosZ[b] - z;
                    if ((ulong)(dx * dx + dz * dz) > c2) continue;
                    uint trust = r.Trust[faction * 8 + ((uint)FactionOf(binfo) & 7)];
                    heardLo |= Listen(i, b, tick, MaskLo[b] & ~oldLo & r.ActiveLo, 0, trust, r.Credibility);
                    heardHi |= Listen(i, b, tick, MaskHi[b] & ~oldHi & r.ActiveHi, 32, trust, r.Credibility);
                }
        uint newLo = (Forget(i, tick, oldLo, 0, r.FadeThreshold) | heardLo) & r.ActiveLo, newHi = (Forget(i, tick, oldHi, 32, r.FadeThreshold) | heardHi) & r.ActiveHi;
        nextLo[i] = newLo; nextHi[i] = newHi;
        for (uint m = newLo; m != 0; m &= m - 1) carriers[BitOperations.TrailingZeroCount(m)]++;
        for (uint m = newHi; m != 0; m &= m - 1) carriers[32 + BitOperations.TrailingZeroCount(m)]++;
    }

    uint Listen(uint i, uint b, uint tick, uint candidates, uint @base, uint trust, uint[] credibility)
    {
        uint heard = 0;
        for (uint m = candidates; m != 0; m &= m - 1)
        {
            uint r = @base + (uint)BitOperations.TrailingZeroCount(m);
            if (Hash4((uint)Seed, tick, i, Mix(b) ^ (0x400 + r)) < credibility[r] * trust) heard |= 1u << (int)(r - @base);
        }
        return heard;
    }

    uint Forget(uint i, uint tick, uint bits, uint @base, uint threshold)
    {
        uint keep = bits;
        for (uint m = bits; m != 0; m &= m - 1)
        {
            uint r = @base + (uint)BitOperations.TrailingZeroCount(m);
            if (Hash4((uint)Seed, tick, i, 0x800 + r) < threshold) keep &= ~(1u << (int)(r - @base));
        }
        return keep;
    }

    // Snapshot of the mutable arrays (detail flags excluded: a loaded world starts with no detailed agents).
    const string Magic = "QSPOP";
    public void Write(BinaryWriter w)
    {
        w.Write(Magic); w.Write(1); w.Write(Seed); w.Write(Count); w.Write(Tick);
        foreach (var array in new[] { PosX, PosZ, TgtX, TgtZ, Info, MaskLo, MaskHi })
            foreach (uint v in array) w.Write(array == Info ? v & ~Detailed : v);
    }

    public void Read(BinaryReader r)
    {
        if (r.ReadString() != Magic || r.ReadInt32() != 1 || r.ReadUInt64() != Seed || r.ReadInt32() != Count)
            throw new InvalidDataException("Population snapshot does not match this world");
        uint tick = r.ReadUInt32();
        foreach (var array in new[] { PosX, PosZ, TgtX, TgtZ, Info, MaskLo, MaskHi })
            for (int i = 0; i < Count; i++) array[i] = r.ReadUInt32();
        Tick = tick;
        BuildGrid();
    }

    public bool Knows(uint i, int slot) => slot < 32 ? (MaskLo[i] & (1u << slot)) != 0 : (MaskHi[i] & (1u << (slot - 32))) != 0;
    public void Tell(uint i, int slot) { if (slot < 32) MaskLo[i] |= 1u << slot; else MaskHi[i] |= 1u << (slot - 32); }

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
    public string NameOf(uint i) => GeneratedName(Seed, i, IsZombie(Info[i]));
    public static string GeneratedName(ulong seed, uint i, bool zombie) =>
        zombie ? "Zombie" : $"{First[Hash4((uint)seed, 11, i, 0) % First.Length]} {Last[Hash4((uint)seed, 12, i, 0) % Last.Length]}";
    public int BaseHealth(uint i) => IsZombie(Info[i]) ? 60 : 80 + (int)(Hash4((uint)Seed, 13, i, 0) % 21);
}
