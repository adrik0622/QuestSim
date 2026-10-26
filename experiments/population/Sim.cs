using System.Numerics;

namespace QuestSim.Population;

// Experiment settings. Distances in millimetres; one population tick = GameSecondsPerTick of game time (48:1 by default).
public sealed record Settings
{
    public const uint GeneratorVersion = 1;
    public long Agents { get; init; } = 100_000;
    public ulong Seed { get; init; } = 1;
    public uint WorldKm { get; init; } = 200;
    public double HumanFraction { get; init; } = 0.6;
    public uint GameSecondsPerTick { get; init; } = 48;
    public uint HumanSpeedMmPerS { get; init; } = 1300;
    public uint ZombieSpeedMmPerS { get; init; } = 500;
    public uint WanderRadiusMm { get; init; } = 1_500_000;
    public uint ZombieSpreadMm { get; init; } = 800_000;
    public double TravelChance { get; init; } = 0.03;
    public uint HordePeriodTicks { get; init; } = 75;
    public uint ContactRadiusMm { get; init; } = 30_000;
    public double FadePerTick { get; init; } = 0.02;
    public uint EventEveryTicks { get; init; } = 60;
    public uint WitnessRadiusMm { get; init; } = 1_000_000;
    public uint RumorLifetimeTicks { get; init; } = 900;
    public uint ReadbackDelay { get; init; } = 2;
    public uint EventCapacity { get; init; } = 1 << 20;
    public uint MaxEvents { get; init; } = 0;          // 0 = unlimited
    public uint RumorSlots { get; init; } = 64;

    public uint WorldMax => checked(WorldKm * 1_000_000u);
    public uint StepHuman => checked(HumanSpeedMmPerS * GameSecondsPerTick);
    public uint StepZombie => checked(ZombieSpeedMmPerS * GameSecondsPerTick);
    public uint SettlementCount => (uint)Math.Clamp(Agents / 5000, 16, 1 << 23);
    public uint TableMask => (uint)BitOperations.RoundUpToPowerOf2((ulong)Math.Max(1024, Agents * 2)) - 1;
    public const uint RegionsPerAxis = 16, RegionCount = RegionsPerAxis * RegionsPerAxis, FactionSlots = 8;
    public uint RegionShift { get { int s = 0; while ((WorldMax >> s) >= RegionsPerAxis) s++; return (uint)s; } }

    public void Validate()
    {
        if (Agents is < 1 or > (1L << 30)) throw new ArgumentException("Agents must be 1..2^30");
        if (WorldMax >= 1u << 31) throw new ArgumentException("World must stay below 2^31 mm");
        if (StepHuman >= 65536 || StepZombie >= 65536) throw new ArgumentException("Per-tick step must be < 65.536 m");
        if (ContactRadiusMm >= 32768 || WitnessRadiusMm / 256 >= 32768) throw new ArgumentException("Radius too large for 32-bit math");
        if (ContactRadiusMm > 65536) throw new ArgumentException("Contact radius must fit one grid cell");
        if (RumorSlots is < 1 or > 64) throw new ArgumentException("RumorSlots must be 1..64");
    }
}

// Pinned generator: SplitMix64 for world creation, a 32-bit integer hash for every per-tick decision.
public struct SplitMix64(ulong state)
{
    public ulong Next()
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public uint Below(uint n) => (uint)(Next() % n);
}

public static class H
{
    public static uint Mix(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        return x;
    }
    public static uint Hash4(uint a, uint b, uint c, uint d) => Mix(a ^ Mix(b ^ Mix(c ^ Mix(d))));
    public static uint Isqrt(uint n)
    {
        uint res = 0, bit = 1u << 30;
        while (bit > n) bit >>= 2;
        while (bit != 0)
        {
            if (n >= res + bit) { n -= res + bit; res = (res >> 1) + bit; } else res >>= 1;
            bit >>= 2;
        }
        return res;
    }
    public static uint CellOf(uint cx, uint cy, uint mask) => Mix(cx * 0x9E3779B1u ^ Mix(cy)) & mask;
    public static uint Threshold(double chance) => (uint)Math.Clamp(chance * 4294967296.0, 0, uint.MaxValue);
}

public sealed class World
{
    public required uint[] Settlements { get; init; }   // x, y pairs
    public required uint[] AdjOff { get; init; }
    public required uint[] Adj { get; init; }
    public uint Count => (uint)(Settlements.Length / 2);
}

// Structure of arrays; agent ID = slot index, never reused.
public sealed class AgentState
{
    public uint[] PosX, PosY, TgtX, TgtY, Info, MaskLo, MaskHi;
    public AgentState(long n) { PosX = new uint[n]; PosY = new uint[n]; TgtX = new uint[n]; TgtY = new uint[n]; Info = new uint[n]; MaskLo = new uint[n]; MaskHi = new uint[n]; }
    public IEnumerable<uint[]> Arrays => [PosX, PosY, TgtX, TgtY, Info, MaskLo, MaskHi];
    public ulong Hash()
    {
        ulong h = 1469598103934665603UL;
        foreach (var array in Arrays)
            foreach (uint v in array) h = (h ^ v) * 1099511628211UL;
        return h;
    }
}

public static class Generator
{
    public const uint HumanFactions = 3;

    public static (World, AgentState) Create(Settings s)
    {
        var rng = new SplitMix64(s.Seed ^ ((ulong)Settings.GeneratorVersion << 56));
        uint count = s.SettlementCount, side = (uint)Math.Ceiling(Math.Sqrt(count)), cell = s.WorldMax / side;
        var settlements = new uint[2 * count];
        var weights = new long[count];
        long total = 0;
        for (uint i = 0; i < count; i++)
        {
            uint gx = i % side, gy = i / side, jitter = cell * 3 / 5;
            settlements[2 * i] = gx * cell + cell / 5 + rng.Below(jitter);
            settlements[2 * i + 1] = gy * cell + cell / 5 + rng.Below(jitter);
            weights[i] = total += 1 + rng.Below(8);
        }
        // Roads: grid neighbours, always connected.
        var adjOff = new uint[count + 1];
        var adj = new List<uint>();
        for (uint i = 0; i < count; i++)
        {
            adjOff[i] = (uint)adj.Count;
            uint gx = i % side;
            if (gx > 0) adj.Add(i - 1);
            if (gx + 1 < side && i + 1 < count) adj.Add(i + 1);
            if (i >= side) adj.Add(i - side);
            if (i + side < count) adj.Add(i + side);
        }
        adjOff[count] = (uint)adj.Count;
        var world = new World { Settlements = settlements, AdjOff = adjOff, Adj = adj.ToArray() };

        var a = new AgentState(s.Agents);
        long zombies = 0;
        uint hordes = (uint)Math.Clamp((long)(s.Agents * (1 - s.HumanFraction)) / 20000, 2, 1 << 23);
        ulong humanCut = (ulong)(s.HumanFraction * ulong.MaxValue);
        for (long i = 0; i < s.Agents; i++)
        {
            uint ox = rng.Below(2 * s.WanderRadiusMm + 1), oy = rng.Below(2 * s.WanderRadiusMm + 1);
            uint x, y, info;
            if (rng.Next() < humanCut)
            {
                long pick = (long)(rng.Next() % (ulong)total);
                int found = Array.BinarySearch(weights, pick + 1);
                uint home = (uint)(found >= 0 ? found : ~found);
                uint faction = home % side * HumanFactions / side;   // three territorial bands
                x = Clamp((long)settlements[2 * home] + ox - s.WanderRadiusMm, s.WorldMax);
                y = Clamp((long)settlements[2 * home + 1] + oy - s.WanderRadiusMm, s.WorldMax);
                info = (faction << 24) | home;
            }
            else
            {
                uint horde = rng.Below(hordes), target = H.Hash4((uint)s.Seed, 9, horde, 0) % count;
                x = Clamp((long)settlements[2 * target] + ox % (2 * s.ZombieSpreadMm + 1) - s.ZombieSpreadMm, s.WorldMax);
                y = Clamp((long)settlements[2 * target + 1] + oy % (2 * s.ZombieSpreadMm + 1) - s.ZombieSpreadMm, s.WorldMax);
                info = (1u << 31) | ((HumanFactions + horde % 2) << 24) | horde;
                zombies++;
            }
            a.PosX[i] = a.TgtX[i] = x; a.PosY[i] = a.TgtY[i] = y; a.Info[i] = info;
        }
        return (world, a);
    }

    static uint Clamp(long v, uint max) => (uint)Math.Clamp(v, 0, max);
}

// Per-tick inputs decided by the CPU authority; identical for every backend.
public sealed class TickInput
{
    public uint Tick;
    public uint ActiveLo, ActiveHi;
    public readonly uint[] Rumor = new uint[128 + 4 * MaxSeeds];   // credibility16[64], trust16[8x8], seeds
    public uint SeedCount;
    public const int MaxSeeds = 8;
}

public sealed class Readback
{
    public uint Tick;
    public uint[] Counters = new uint[130];   // 0 events, 1 overflow, 2+ carriers per slot, 66+ newly heard per slot
    public uint[] Agg = new uint[Settings.FactionSlots * Settings.RegionCount];
    public ulong[] Events = [];               // (agent << 6) | slot, sorted
}

// Pushed to the GPU as 21 uints; the CPU reference reads the same values.
public readonly record struct Push(uint Agents, uint Settlements, uint Tick, uint Seed, uint StepHuman, uint StepZombie,
    uint WanderRadius, uint ZombieSpread, uint TravelThreshold, uint HordePeriod, uint WorldMax, uint RegionShift,
    uint RegionsPerAxis, uint RegionCount, uint TableMask, uint ContactRadius, uint FadeThreshold, uint ActiveLo,
    uint ActiveHi, uint SeedCount, uint EventCapacity)
{
    public static Push For(Settings s, World w, TickInput t) => new((uint)s.Agents, w.Count, t.Tick, (uint)s.Seed,
        s.StepHuman, s.StepZombie, s.WanderRadiusMm, s.ZombieSpreadMm, H.Threshold(s.TravelChance), s.HordePeriodTicks,
        s.WorldMax, s.RegionShift, Settings.RegionsPerAxis, Settings.RegionCount, s.TableMask, s.ContactRadiusMm,
        H.Threshold(s.FadePerTick), t.ActiveLo, t.ActiveHi, t.SeedCount, s.EventCapacity);
}

public interface IBackend : IDisposable
{
    string Name { get; }
    void Submit(TickInput input);
    Readback Collect(uint tick);
    AgentState Download();
    Dictionary<string, double> LastTimingsMs { get; }
}
