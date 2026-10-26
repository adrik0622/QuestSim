using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace QuestSim.Population;

// Reference implementation: the same integer rules as shaders/*.comp, multithreaded on the CPU.
public sealed class CpuBackend : IBackend
{
    const uint None = uint.MaxValue;
    readonly Settings s;
    readonly World w;
    readonly AgentState a;
    readonly uint[] heads, next, nextLo, nextHi;
    readonly Dictionary<uint, Readback> results = new();
    public string Name => "cpu";
    public Dictionary<string, double> LastTimingsMs { get; } = new();

    public CpuBackend(Settings settings, World world, AgentState state)
    {
        s = settings; w = world; a = state;
        heads = new uint[s.TableMask + 1L]; next = new uint[s.Agents];
        nextLo = new uint[s.Agents]; nextHi = new uint[s.Agents];
    }

    public void Submit(TickInput input)
    {
        var p = Push.For(s, w, input);
        var r = new Readback { Tick = input.Tick };
        var clock = Stopwatch.StartNew();
        Parallel(lo => new uint[r.Agg.Length], (i, agg) => Move(p, i, agg), agg => { lock (r) for (int k = 0; k < agg.Length; k++) r.Agg[k] += agg[k]; });
        LastTimingsMs["move"] = Lap(clock);
        Array.Fill(heads, None);
        for (uint i = 0; i < s.Agents; i++)
        {
            uint h = H.CellOf(a.PosX[i] >> 16, a.PosY[i] >> 16, p.TableMask);
            next[i] = heads[h]; heads[h] = i;
        }
        LastTimingsMs["insert"] = Lap(clock);
        var events = new ConcurrentBag<ulong[]>();
        Parallel(lo => (Counters: new uint[130], Events: new List<ulong>()), (i, local) => Spread(p, input.Rumor, i, local.Counters, local.Events),
            local => { lock (r) for (int k = 0; k < 130; k++) r.Counters[k] += local.Counters[k]; events.Add(local.Events.ToArray()); });
        Array.Copy(nextLo, a.MaskLo, s.Agents); Array.Copy(nextHi, a.MaskHi, s.Agents);
        r.Events = events.SelectMany(e => e).Order().Take((int)s.EventCapacity).ToArray();
        r.Counters[1] = r.Counters[0] > s.EventCapacity ? r.Counters[0] - s.EventCapacity : 0;
        LastTimingsMs["spread"] = Lap(clock);
        results[input.Tick] = r;
    }

    static double Lap(Stopwatch clock) { double ms = clock.Elapsed.TotalMilliseconds; clock.Restart(); return ms; }

    void Parallel<T>(Func<long, T> init, Action<uint, T> body, Action<T> merge) =>
        System.Threading.Tasks.Parallel.ForEach(Partitioner.Create(0L, s.Agents, Math.Max(4096, s.Agents / (Environment.ProcessorCount * 8))),
            () => init(0), (range, _, local) => { for (long i = range.Item1; i < range.Item2; i++) body((uint)i, local); return local; }, merge);

    void Move(Push p, uint i, uint[] agg)
    {
        uint inf = a.Info[i], kind = inf >> 31, mode = (inf >> 30) & 1, faction = (inf >> 24) & 63, anchor = inf & 0xFFFFFF;
        uint x = a.PosX[i], y = a.PosY[i], tx = a.TgtX[i], ty = a.TgtY[i];
        uint step = kind == 1 ? p.StepZombie : p.StepHuman;
        int dx = (int)tx - (int)x, dy = (int)ty - (int)y;
        uint ax = (uint)Math.Abs(dx), ay = (uint)Math.Abs(dy);
        bool arrived = ax <= step && ay <= step;
        if (arrived) { x = tx; y = ty; }
        else
        {
            int sh = 0;
            while ((Math.Max(ax, ay) >> sh) >= 32768) sh++;
            uint sx = ax >> sh, sy = ay >> sh, d = H.Isqrt(sx * sx + sy * sy);
            uint mx = sx * step / d, my = sy * step / d;
            x = dx < 0 ? x - mx : x + mx;
            y = dy < 0 ? y - my : y + my;
        }
        if (arrived)
        {
            if (kind == 1)
            {
                uint target = H.Hash4(p.Seed, 9, anchor, p.Tick / p.HordePeriod) % p.Settlements, span = 2 * p.ZombieSpread + 1;
                tx = Axis((int)w.Settlements[2 * target] + (int)(H.Hash4(p.Seed, p.Tick, i, 5) % span) - (int)p.ZombieSpread, p.WorldMax);
                ty = Axis((int)w.Settlements[2 * target + 1] + (int)(H.Hash4(p.Seed, p.Tick, i, 6) % span) - (int)p.ZombieSpread, p.WorldMax);
            }
            else
            {
                uint off = w.AdjOff[anchor], count = w.AdjOff[anchor + 1] - off;
                if (mode == 0 && count > 0 && H.Hash4(p.Seed, p.Tick, i, 1) < p.TravelThreshold)
                {
                    anchor = w.Adj[off + H.Hash4(p.Seed, p.Tick, i, 2) % count];
                    mode = 1;
                    tx = w.Settlements[2 * anchor]; ty = w.Settlements[2 * anchor + 1];
                }
                else
                {
                    mode = 0;
                    uint span = 2 * p.WanderRadius + 1;
                    tx = Axis((int)w.Settlements[2 * anchor] + (int)(H.Hash4(p.Seed, p.Tick, i, 3) % span) - (int)p.WanderRadius, p.WorldMax);
                    ty = Axis((int)w.Settlements[2 * anchor + 1] + (int)(H.Hash4(p.Seed, p.Tick, i, 4) % span) - (int)p.WanderRadius, p.WorldMax);
                }
            }
        }
        a.PosX[i] = x; a.PosY[i] = y; a.TgtX[i] = tx; a.TgtY[i] = ty;
        a.Info[i] = (kind << 31) | (mode << 30) | (faction << 24) | anchor;
        uint region = Math.Min(x >> (int)p.RegionShift, p.RegionsPerAxis - 1) + Math.Min(y >> (int)p.RegionShift, p.RegionsPerAxis - 1) * p.RegionsPerAxis;
        agg[faction * p.RegionCount + region]++;
    }

    static uint Axis(int v, uint max) => (uint)Math.Clamp(v, 0, (int)max);

    void Spread(Push p, uint[] rumor, uint i, uint[] counters, List<ulong> events)
    {
        uint inf = a.Info[i];
        if (inf >> 31 == 1) { nextLo[i] = 0; nextHi[i] = 0; return; }
        uint x = a.PosX[i], y = a.PosY[i], faction = (inf >> 24) & 7;
        uint oldLo = a.MaskLo[i], oldHi = a.MaskHi[i], heardLo = 0, heardHi = 0;
        for (uint k = 0; k < p.SeedCount; k++)
        {
            uint sx = rumor[128 + 4 * k], sy = rumor[129 + 4 * k], radius = rumor[130 + 4 * k], slot = rumor[131 + 4 * k];
            uint dx = (uint)Math.Abs((int)x - (int)sx) >> 8, dy = (uint)Math.Abs((int)y - (int)sy) >> 8;
            if (dx <= radius && dy <= radius && dx * dx + dy * dy <= radius * radius)
            {
                if (slot < 32) heardLo |= 1u << (int)slot; else heardHi |= 1u << (int)(slot - 32);
            }
        }
        uint cx = x >> 16, cy = y >> 16, c2 = p.ContactRadius * p.ContactRadius;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                for (uint b = heads[H.CellOf(cx + (uint)ox, cy + (uint)oy, p.TableMask)]; b != None; b = next[b])
                {
                    uint binf = a.Info[b];
                    if (b == i || binf >> 31 == 1) continue;
                    uint dx = (uint)Math.Abs((int)a.PosX[b] - (int)x), dy = (uint)Math.Abs((int)a.PosY[b] - (int)y);
                    if (dx > p.ContactRadius || dy > p.ContactRadius || dx * dx + dy * dy > c2) continue;
                    uint trust = rumor[64 + faction * 8 + ((binf >> 24) & 7)];
                    heardLo |= Listen(p, rumor, i, b, a.MaskLo[b] & ~oldLo & p.ActiveLo, 0, trust);
                    heardHi |= Listen(p, rumor, i, b, a.MaskHi[b] & ~oldHi & p.ActiveHi, 32, trust);
                }
        heardLo &= ~oldLo; heardHi &= ~oldHi;
        uint newLo = Forget(p, i, oldLo, 0) | heardLo, newHi = Forget(p, i, oldHi, 32) | heardHi;
        nextLo[i] = newLo; nextHi[i] = newHi;
        Record(i, heardLo, newLo, 0, counters, events);
        Record(i, heardHi, newHi, 32, counters, events);
    }

    static uint Forget(Push p, uint i, uint bits, uint @base)
    {
        uint keep = bits;
        for (uint m = bits; m != 0; m &= m - 1)
        {
            uint r = @base + (uint)BitOperations.TrailingZeroCount(m);
            if (H.Hash4(p.Seed, p.Tick, i, 0x800 + r) < p.FadeThreshold) keep &= ~(1u << (int)(r - @base));
        }
        return keep;
    }

    static uint Listen(Push p, uint[] rumor, uint i, uint b, uint candidates, uint @base, uint trust)
    {
        uint heard = 0;
        for (uint m = candidates; m != 0; m &= m - 1)
        {
            uint r = @base + (uint)BitOperations.TrailingZeroCount(m);
            if (H.Hash4(p.Seed, p.Tick, i, H.Mix(b) ^ (0x400 + r)) < rumor[r] * trust) heard |= 1u << (int)(r - @base);
        }
        return heard;
    }

    static void Record(uint i, uint heard, uint carriers, uint @base, uint[] counters, List<ulong> events)
    {
        for (uint m = heard; m != 0; m &= m - 1)
        {
            uint r = @base + (uint)BitOperations.TrailingZeroCount(m);
            counters[0]++; counters[66 + r]++;
            events.Add(((ulong)i << 6) | r);
        }
        for (uint m = carriers; m != 0; m &= m - 1) counters[2 + @base + (uint)BitOperations.TrailingZeroCount(m)]++;
    }

    public Readback Collect(uint tick) => results.Remove(tick, out var r) ? r : throw new InvalidOperationException($"No readback for tick {tick}");
    public AgentState Download() => a;
    public void Dispose() { }
}
