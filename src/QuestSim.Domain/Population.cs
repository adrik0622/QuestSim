using System.Collections.Concurrent;
using System.Numerics;

namespace QuestSim.Domain;

// The coarse world population (E1 model, CPU path). One tick = 48 game seconds. Agent ID = slot index, never reused.
// Positions are uint millimetres offset by CityGen.Half so the whole 40 km world is non-negative.
// Info bits: 31 zombie, 30 travelling, 29 detailed (owned by the 20 Hz authority), 28 dead, 24-27 faction, 0-23 home settlement.
public sealed class Population
{
    public const uint Zombie = 1u << 31, Travelling = 1u << 30, Detailed = 1u << 29, Dead = 1u << 28, None = uint.MaxValue;
    public const uint Dormant = Dead | 0xFFFFFF;   // a not-yet-arrived agent: dead flag plus an impossible home settlement
    public static bool IsDormant(uint info) => (info & Dormant) == Dormant;
    public uint Hordes { get; }
    public long TargetHumans { get; private set; }
    public long TargetZombies { get; private set; }
    public long Arrived { get; private set; }
    public long Risen { get; private set; }
    public static uint RiseThreshold = (uint)(0.3 * uint.MaxValue);
    readonly int towns;
    public const int GameSecondsPerTick = 48, HumanFactions = 3, Hostile = 2;
    // A coarse tick is one real second: people walk at real-time speed, the same as in detail. The 48:1 compression
    // applies to the clock (days, quests), not to walking. Hordes pick a new target every 20 real minutes.
    public const uint StepHuman = 1300, StepZombie = 500, WanderRadius = 250_000, HordeSpread = 300_000, HordePeriod = 1200;
    public static readonly string[] FactionNames = ["Survivors' Union", "Free Traders", "The Hollow Choir", "Shambling Mass", "The Pale Tide"];
    public readonly uint[] PosX, PosZ, TgtX, TgtZ, Info, MaskLo, MaskHi;
    readonly uint[] heads, next, nextLo, nextHi, biteHeads, biteNext, contactStart, contactFill, contactCell;
    // Three grids, each with cells at least its search radius so a 3×3 block finds every pair: all living agents in
    // 65.5 m cells (Near), living humans in 32.8 m cells (rumor contact, 30 m) and in 8.2 m cells (zombie encounters, 5 m).
    // Cells sized to the radius keep the scans from wading through ~500 non-neighbours per hit, which is what made a
    // 1M-agent tick take seconds on CPUs with smaller caches. The contact grid is the hottest scan, so its humans are
    // also laid out contiguously per cell (counting sort) with the fields the scan reads copied alongside.
    struct Contact { public uint Agent, X, Z, Info, Lo, Hi; }
    readonly Contact[] contacts;
    const int NearShift = 16, ContactShift = 15, BiteShift = 13;
    public uint TableMask { get; }
    public int Count { get; }
    public ulong Seed { get; }
    public uint Tick { get; private set; }
    public List<Settlement> Settlements { get; }
    public int[] SettlementFaction { get; }
    public int[] Residents { get; }   // living humans per home settlement, counted each step (the economy's workforce and mouths)
    readonly uint[] settleX, settleZ, adjOff, adj;

    // Game clock at 48:1: one population tick is 48 game seconds; the world starts at 08:00 on day one.
    public static double GameHour(double gameSeconds) => (8 + gameSeconds / 3600) % 24;
    public static bool Night(double hour) => hour >= 22 || hour < 6;
    public const uint NightRadius = 40_000;

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
        Residents = new int[s];
        SettlementFaction = Settlements.Select(x => x.Spawn ? 0 : Hash4((uint)Seed, 77, (uint)x.Index, 0) % 10 == 0 ? Hostile : x.X < 0 ? 0 : 1).ToArray();
        var lists = Enumerable.Range(0, s).Select(_ => new List<uint>()).ToArray();
        foreach (var (a, b) in links) { lists[a].Add((uint)b); lists[b].Add((uint)a); }
        adjOff = new uint[s + 1]; var all = new List<uint>();
        for (int i = 0; i < s; i++) { adjOff[i] = (uint)all.Count; all.AddRange(lists[i]); }
        adjOff[s] = (uint)all.Count; adj = all.ToArray();
        PosX = new uint[count]; PosZ = new uint[count]; TgtX = new uint[count]; TgtZ = new uint[count]; Info = new uint[count];
        MaskLo = new uint[count]; MaskHi = new uint[count]; next = new uint[count]; nextLo = new uint[count]; nextHi = new uint[count];
        TableMask = (uint)BitOperations.RoundUpToPowerOf2((ulong)Math.Max(1024, count * 2)) - 1;
        heads = new uint[TableMask + 1L]; biteHeads = new uint[TableMask + 1L]; biteNext = new uint[count];
        contactStart = new uint[TableMask + 2L]; contactFill = new uint[TableMask + 1L]; contactCell = new uint[count]; contacts = new Contact[count];
        uint hordes = (uint)Math.Max(2, count * (1 - humanFraction) / 5000);
        Hordes = hordes;
        towns = city.Towns.Count;
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
            // About a tenth of the slots start dormant: future arrivals get fresh IDs instead of reusing the dead's.
            if (Hash4((uint)Seed, 14, i, 0) % 10 == 0) Info[i] = (info & ~0xFFFFFFu) | Dormant;
            else if (IsZombie(info)) TargetZombies++; else TargetHumans++;
        }
        CountResidents();
        BuildGrid();
    }

    static uint Offset(uint centre, uint hash, uint radius) =>
        (uint)Math.Clamp((long)centre + hash % (2 * radius + 1) - radius, 0, 2L * CityGen.Half * 1000);

    // Hordes wander between settlements of their faction's territory (alternate settlements), never the spawn district.
    public uint HordeTarget(uint horde, uint tick)
    {
        uint count = (uint)Settlements.Count, target = Hash4((uint)Seed, 9, horde, tick / HordePeriod) % count;
        if (target % 2 != horde % 2) target = (target + 1) % count;
        while (Settlements[(int)target].Spawn || target % 2 != horde % 2) target = (target + 2) % count;
        return target;
    }

    public static bool IsZombie(uint info) => (info & Zombie) != 0;

    // At night, people who have made it home are indoors: never shown or promoted, and out of the zombies' reach.
    // (Without this a whole district stood in a 40 m square all night.)
    public bool Indoors(uint i) => IndoorsAt(i, Night(GameHour(Tick * (double)GameSecondsPerTick)));
    bool IndoorsAt(uint i, bool night)
    {
        uint info = Info[i];
        if (!night || (info & (Zombie | Travelling | Dead | Detailed)) != 0) return false;
        uint anchor = info & 0xFFFFFF;
        return Math.Max(Math.Abs((long)PosX[i] - settleX[anchor]), Math.Abs((long)PosZ[i] - settleZ[anchor])) <= NightRadius;
    }
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
        Encounters(tick, partitions);
        Recover(tick);
        var carriers = new uint[64];
        var members = new long[16];
        bool spreading = rumors is not null && (rumors.ActiveLo | rumors.ActiveHi) != 0;
        if (spreading)   // after encounters and arrivals, so the copies match what Spread used to read live
            Parallel.ForEach(Partitioner.Create(0, (int)contactStart[^1], 16384), range =>
            {
                for (int k = range.Item1; k < range.Item2; k++)
                {
                    ref var c = ref contacts[k];
                    c.Info = Info[c.Agent]; c.Lo = MaskLo[c.Agent]; c.Hi = MaskHi[c.Agent];
                }
            });
        var residents = new int[Settlements.Count];
        Parallel.ForEach(partitions, () => (Carriers: new uint[64], Members: new long[16], Residents: new int[Settlements.Count]), (range, _, local) =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                uint info = Info[i];
                if ((info & Dead) == 0) local.Members[FactionOf(info)]++;
                if ((info & (Dead | Zombie)) == 0) local.Residents[info & 0xFFFFFF]++;
                if (spreading) Spread((uint)i, tick, rumors!, local.Carriers);
                else
                {
                    nextLo[i] = MaskLo[i]; nextHi[i] = MaskHi[i];
                    for (uint m = MaskLo[i]; m != 0; m &= m - 1) local.Carriers[BitOperations.TrailingZeroCount(m)]++;
                    for (uint m = MaskHi[i]; m != 0; m &= m - 1) local.Carriers[32 + BitOperations.TrailingZeroCount(m)]++;
                }
            }
            return local;
        }, local =>
        {
            lock (carriers) for (int k = 0; k < 64; k++) { carriers[k] += local.Carriers[k]; }
            lock (members) for (int k = 0; k < 16; k++) members[k] += local.Members[k];
            lock (residents) for (int k = 0; k < residents.Length; k++) residents[k] += local.Residents[k];
        });
        Array.Copy(residents, Residents, residents.Length);
        Array.Copy(nextLo, MaskLo, Count); Array.Copy(nextHi, MaskHi, Count);
        Array.Copy(carriers, Carriers, 64); Array.Copy(members, Members, 16);
        Tick++;
    }

    // Below starting strength, dormant agents wake: survivors arrive at the outlying towns, zombies migrate in from the edges.
    void Recover(uint tick)
    {
        long humans = 0, zombies = 0;
        for (int i = 0; i < Count; i++)
        {
            uint info = Info[i];
            if ((info & Dead) != 0) continue;
            if (IsZombie(info)) zombies++; else humans++;
        }
        long cap = Math.Max(1, Count / 50_000), humanGap = Math.Min(cap, TargetHumans - humans), zombieGap = Math.Min(cap, TargetZombies - zombies);
        for (uint i = 0; i < Count && (humanGap > 0 || zombieGap > 0); i++)
        {
            if (!IsDormant(Info[i])) continue;
            bool zombie = IsZombie(Info[i]);
            if (zombie ? zombieGap <= 0 : humanGap <= 0) continue;
            uint h = Hash4((uint)Seed, tick, i, 0xD00);
            if (zombie)
            {
                uint horde = h % Hordes, edge = Mix(h) % 4, along = Mix(h ^ 1) % (2u * CityGen.Half * 1000);
                uint far = 2u * CityGen.Half * 1000 - 500_000, near = 500_000;
                PosX[i] = edge switch { 0 => near, 1 => far, _ => along };
                PosZ[i] = edge switch { 2 => near, 3 => far, _ => along };
                Info[i] = Zombie | ((uint)(HumanFactions + horde % 2) << 24) | horde;
                zombieGap--;
            }
            else
            {
                int town = Settlements.Count - 1 - (int)(h % (uint)Math.Max(1, towns));
                PosX[i] = Offset(settleX[town], Mix(h), WanderRadius);
                PosZ[i] = Offset(settleZ[town], Mix(h ^ 2), WanderRadius);
                Info[i] = ((uint)SettlementFaction[town] << 24) | (uint)town;
                humanGap--;
            }
            TgtX[i] = PosX[i]; TgtZ[i] = PosZ[i]; MaskLo[i] = MaskHi[i] = 0;
            Arrived++;
        }
    }

    // Away from players, zombies and humans in contact (5 m) may kill each other: a small deterministic chance per pair
    // and tick. Detailed agents belong to the authority and are skipped. Losses per settlement feed "attack" facts.
    public const uint EncounterRadius = 5_000;
    public static uint HumanLossThreshold = (uint)(0.0005 * uint.MaxValue), ZombieLossThreshold = (uint)(0.0005 * uint.MaxValue);
    public Dictionary<int, int> SettlementLosses { get; } = new();
    public long ZombieLosses { get; private set; }

    void Encounters(uint tick, OrderablePartitioner<Tuple<int, int>> partitions)
    {
        bool night = Night(GameHour(tick * (double)GameSecondsPerTick));
        var dying = new ConcurrentBag<uint>();
        var bitten = new ConcurrentBag<(uint Human, uint Zombie)>();
        ulong r2 = (ulong)EncounterRadius * EncounterRadius;
        Parallel.ForEach(partitions, range =>
        {
            for (int k = range.Item1; k < range.Item2; k++)
            {
                uint z = (uint)k, info = Info[z];
                if (!IsZombie(info) || (info & (Dead | Detailed)) != 0) continue;
                uint cx = PosX[z] >> BiteShift, cz = PosZ[z] >> BiteShift;
                for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                        for (uint h = biteHeads[CellOf(cx + (uint)ox, cz + (uint)oz, TableMask)]; h != None; h = biteNext[h])
                        {
                            if ((Info[h] & Detailed) != 0 || IndoorsAt(h, night)) continue;
                            long dx = (long)PosX[h] - PosX[z], dz = (long)PosZ[h] - PosZ[z];
                            if ((ulong)(dx * dx + dz * dz) > r2) continue;
                            if (Hash4((uint)Seed, tick, z, Mix(h) ^ 0xA00) < HumanLossThreshold) bitten.Add((h, z));
                            if (Hash4((uint)Seed, tick, h, Mix(z) ^ 0xB00) < ZombieLossThreshold) dying.Add(z);
                        }
            }
        });
        SettlementLosses.Clear();
        ZombieLosses = 0;
        // Some victims of zombies rise and join the horde that killed them (same slot, same identity).
        foreach (var (h, z) in bitten.GroupBy(b => b.Human).Select(g => g.OrderBy(b => b.Zombie).First()).OrderBy(b => b.Human))
        {
            int home = (int)(Info[h] & 0xFFFFFF);
            SettlementLosses[home] = SettlementLosses.GetValueOrDefault(home) + 1;
            if (Hash4((uint)Seed, tick, h, 0xC00) < RiseThreshold)
            {
                Info[h] = Zombie | (Info[z] & (15u << 24)) | (Info[z] & 0xFFFFFF);
                MaskLo[h] = MaskHi[h] = 0;
                TgtX[h] = PosX[h]; TgtZ[h] = PosZ[h];
                Risen++;
            }
            else Info[h] |= Dead;
        }
        foreach (uint d in dying.Distinct().Order())
        {
            if ((Info[d] & Dead) != 0) continue;
            Info[d] |= Dead;
            ZombieLosses++;
        }
    }

    // E1 spread rule: reads previous masks only, deterministic per (agent, neighbour, rumor), so thread order never matters.
    void Spread(uint i, uint tick, RumorInput r, uint[] carriers)
    {
        uint info = Info[i];
        if (IsZombie(info) || (info & Dead) != 0) { nextLo[i] = 0; nextHi[i] = 0; return; }
        uint x = PosX[i], z = PosZ[i], faction = (uint)FactionOf(info) & 7, oldLo = MaskLo[i], oldHi = MaskHi[i], heardLo = 0, heardHi = 0;
        uint cx = x >> ContactShift, cz = z >> ContactShift;
        ulong c2 = (ulong)ContactRadius * ContactRadius;
        for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                uint cell = CellOf(cx + (uint)ox, cz + (uint)oz, TableMask);
                for (uint k = contactStart[cell], end = contactStart[cell + 1]; k < end; k++)
                {
                    ref readonly var b = ref contacts[k];
                    if (b.Agent == i) continue;
                    long dx = (long)b.X - x, dz = (long)b.Z - z;
                    if ((ulong)(dx * dx + dz * dz) > c2) continue;
                    uint trust = r.Trust[faction * 8 + ((uint)FactionOf(b.Info) & 7)];
                    heardLo |= Listen(i, b.Agent, tick, b.Lo & ~oldLo & r.ActiveLo, 0, trust, r.Credibility);
                    heardHi |= Listen(i, b.Agent, tick, b.Hi & ~oldHi & r.ActiveHi, 32, trust, r.Credibility);
                }
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

    public IEnumerable<int> Neighbours(int settlement)
    {
        for (uint k = adjOff[settlement]; k < adjOff[settlement + 1]; k++) yield return (int)adj[k];
    }

    // Living humans at home in a settlement (not detailed, not already on the road), in index order.
    public IEnumerable<uint> HomeResidents(int settlement)
    {
        for (uint i = 0; i < Count; i++)
            if ((Info[i] & (Zombie | Dead | Detailed | Travelling)) == 0 && (Info[i] & 0xFFFFFF) == settlement) yield return i;
    }

    // A refugee: new home (and its faction), walking there along the road. Only between ticks.
    public void Relocate(uint i, int settlement)
    {
        Info[i] = Travelling | ((uint)SettlementFaction[settlement] << 24) | (uint)settlement;
        TgtX[i] = settleX[settlement]; TgtZ[i] = settleZ[settlement];
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
        CountResidents();
        BuildGrid();
    }

    void CountResidents()
    {
        Array.Clear(Residents);
        for (uint i = 0; i < Count; i++) if ((Info[i] & (Dead | Zombie)) == 0) Residents[Info[i] & 0xFFFFFF]++;
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
        // Nightfall: people out wandering turn for home now rather than finishing a long daytime walk.
        if (!zombie && !travelling && Night(GameHour(tick * (double)GameSecondsPerTick)) &&
            Math.Max(Math.Abs((long)tx - settleX[anchor]), Math.Abs((long)tz - settleZ[anchor])) > NightRadius)
        {
            tx = Offset(settleX[anchor], Hash4((uint)Seed, tick, i, 3), NightRadius);
            tz = Offset(settleZ[anchor], Hash4((uint)Seed, tick, i, 4), NightRadius);
        }
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
            else if (!travelling && !Night(GameHour(tick * (double)GameSecondsPerTick)) && adjOff[anchor + 1] > adjOff[anchor] && Hash4((uint)Seed, tick, i, 1) % 100 < 3)
            {
                uint count = adjOff[anchor + 1] - adjOff[anchor];
                anchor = adj[adjOff[anchor] + Hash4((uint)Seed, tick, i, 2) % count];
                travelling = true; tx = settleX[anchor]; tz = settleZ[anchor];
                info = (info & ~0xFFFFFFu) | anchor;
            }
            else
            {
                travelling = false;
                uint radius = Night(GameHour(tick * (double)GameSecondsPerTick)) ? NightRadius : WanderRadius;   // home at night
                tx = Offset(settleX[anchor], Hash4((uint)Seed, tick, i, 3), radius);
                tz = Offset(settleZ[anchor], Hash4((uint)Seed, tick, i, 4), radius);
            }
        }
        PosX[i] = x; PosZ[i] = z; TgtX[i] = tx; TgtZ[i] = tz;
        Info[i] = travelling ? info | Travelling : info & ~Travelling;
    }

    static uint CellOf(uint cx, uint cz, uint mask) => Mix(cx * 0x9E3779B1u ^ Mix(cz)) & mask;

    void BuildGrid()
    {
        Array.Fill(heads, None);
        Array.Fill(biteHeads, None);
        Array.Clear(contactStart);
        for (uint i = 0; i < Count; i++)
        {
            contactCell[i] = None;
            if ((Info[i] & Dead) != 0) { next[i] = biteNext[i] = None; continue; }
            uint h = CellOf(PosX[i] >> NearShift, PosZ[i] >> NearShift, TableMask);
            next[i] = heads[h]; heads[h] = i;
            if (IsZombie(Info[i])) continue;
            h = CellOf(PosX[i] >> BiteShift, PosZ[i] >> BiteShift, TableMask);
            biteNext[i] = biteHeads[h]; biteHeads[h] = i;
            h = contactCell[i] = CellOf(PosX[i] >> ContactShift, PosZ[i] >> ContactShift, TableMask);
            contactStart[h + 1]++;
        }
        for (long h = 1; h < contactStart.Length; h++) contactStart[h] += contactStart[h - 1];
        Array.Copy(contactStart, contactFill, contactFill.Length);
        for (uint i = 0; i < Count; i++)
            if (contactCell[i] != None) contacts[contactFill[contactCell[i]]++] = new Contact { Agent = i, X = PosX[i], Z = PosZ[i] };
    }

    // Living agents within a radius (metres) of a point, from the grid of the last tick. Detailed agents included.
    public List<uint> Near(double x, double z, double radius)
    {
        var found = new List<uint>();
        uint mx = ToMm(x), mz = ToMm(z), r = (uint)(radius * 1000);
        long r2 = (long)r * r;
        int reach = (int)(r >> NearShift) + 1;
        uint cx = mx >> NearShift, cz = mz >> NearShift;
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
