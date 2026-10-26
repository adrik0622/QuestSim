namespace QuestSim.Domain;

public enum Biome { Urban, Town, Field, Forest }
public sealed record Building(string Id, int X, int Z, int Width, int Depth, int Height);
public sealed record Town(int Index, int X, int Z, int Radius);
public sealed record Settlement(int Index, int X, int Z, int Radius, bool Spawn);

// Deterministic world layout from a seed. Integer metres only, so every machine regenerates the same chunks;
// saves keep the seed and generator version, never the generated geometry.
public sealed class CityGen
{
    public const int Version = 1, Half = 20000, ChunkSize = 128, CityRadius = 5000, Pitch = 120, BlockHalf = 50,
        TownPitch = 80, TownBlockHalf = 32, RoadHalfWidth = 7, DistrictBlocks = 5;
    static readonly (int X, int Z)[] Directions = [(1000, 0), (707, 707), (0, 1000), (-707, 707), (-1000, 0), (-707, -707), (0, -1000), (707, -707)];
    public ulong Seed { get; }
    public IReadOnlyList<Town> Towns { get; }
    public IReadOnlyList<(int X1, int Z1, int X2, int Z2)> Roads { get; }
    readonly Dictionary<(int, int), Building[]> chunks = new();

    public CityGen(ulong seed)
    {
        Seed = seed;
        var towns = new List<Town>();
        var roads = new List<(int, int, int, int)>();
        for (int i = 0; i < Directions.Length; i++)
        {
            var (dx, dz) = Directions[i];
            int distance = 13000 + (int)(Hash(i, 0, 1) % 4000), side = (int)(Hash(i, 0, 2) % 3000) - 1500;
            int x = dx * distance / 1000 - dz * side / 1000, z = dz * distance / 1000 + dx * side / 1000;
            var town = new Town(i, x, z, 900 + (int)(Hash(i, 0, 3) % 500));
            towns.Add(town);
            // Straight road from the city edge toward the town.
            long length = Isqrt((long)x * x + (long)z * z);
            roads.Add(((int)(x * (CityRadius - 200) / length), (int)(z * (CityRadius - 200) / length), x, z));
        }
        Towns = towns;
        Roads = roads;
    }

    uint Hash(int a, int b, int c) => Mix((uint)Seed ^ Mix((uint)(Seed >> 32) ^ Mix((uint)a ^ Mix((uint)b ^ Mix((uint)c)))));
    public static uint Mix(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
    static long Isqrt(long n) { long r = (long)Math.Sqrt(n); while (r * r > n) r--; while ((r + 1) * (r + 1) <= n) r++; return r; }
    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    public static bool InCity(int x, int z) => (long)x * x + (long)z * z <= (long)CityRadius * CityRadius;

    public Biome BiomeAt(int x, int z)
    {
        if (InCity(x, z)) return Biome.Urban;
        foreach (var t in Towns) if ((long)(x - t.X) * (x - t.X) + (long)(z - t.Z) * (z - t.Z) <= (long)t.Radius * t.Radius) return Biome.Town;
        // Coarse value noise: forest where the averaged 1 km cell hash is high.
        int cx = FloorDiv(x, 1024), cz = FloorDiv(z, 1024);
        uint sum = 0;
        for (int dx = 0; dx <= 1; dx++) for (int dz = 0; dz <= 1; dz++) sum += Hash(cx + dx, cz + dz, 7) % 1000;
        return sum > 2300 ? Biome.Forest : Biome.Field;
    }

    public bool OnRoad(int x, int z, int margin = 0) => Roads.Any(r => SegmentDistanceSquared(x, z, r) <= (long)(RoadHalfWidth + margin) * (RoadHalfWidth + margin));

    static long SegmentDistanceSquared(int px, int pz, (int X1, int Z1, int X2, int Z2) r)
    {
        long vx = r.X2 - r.X1, vz = r.Z2 - r.Z1, wx = px - r.X1, wz = pz - r.Z1, len = vx * vx + vz * vz;
        long dot = Math.Clamp(wx * vx + wz * vz, 0, len);
        long qx = r.X1 + vx * dot / len - px, qz = r.Z1 + vz * dot / len - pz;
        return qx * qx + qz * qz;
    }

    // Buildings whose centre lies in this chunk. Each building belongs to exactly one chunk.
    public Building[] Chunk(int cx, int cz)
    {
        lock (chunks)
            if (chunks.TryGetValue((cx, cz), out var cached)) return cached;
        int x0 = cx * ChunkSize, z0 = cz * ChunkSize, x1 = x0 + ChunkSize, z1 = z0 + ChunkSize;
        var list = new List<Building>();
        for (int bx = FloorDiv(x0 - Pitch, Pitch); bx <= FloorDiv(x1 + Pitch, Pitch); bx++)
            for (int bz = FloorDiv(z0 - Pitch, Pitch); bz <= FloorDiv(z1 + Pitch, Pitch); bz++)
                CityBlock(bx, bz, list);
        foreach (var t in Towns)
        {
            if (Math.Abs(t.X - (x0 + x1) / 2) > t.Radius + ChunkSize || Math.Abs(t.Z - (z0 + z1) / 2) > t.Radius + ChunkSize) continue;
            for (int bx = FloorDiv(x0 - t.X - TownPitch, TownPitch); bx <= FloorDiv(x1 - t.X + TownPitch, TownPitch); bx++)
                for (int bz = FloorDiv(z0 - t.Z - TownPitch, TownPitch); bz <= FloorDiv(z1 - t.Z + TownPitch, TownPitch); bz++)
                    TownBlock(t, bx, bz, list);
        }
        var result = list.Where(b => b.X >= x0 && b.X < x1 && b.Z >= z0 && b.Z < z1).OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        lock (chunks) chunks[(cx, cz)] = result;
        return result;
    }

    void CityBlock(int bx, int bz, List<Building> output)
    {
        int cx = bx * Pitch, cz = bz * Pitch;
        if (!InCity(cx, cz) || (bx == 0 && bz == 0) || Hash(bx, bz, 10) % 12 == 0) return;   // plaza and parks stay open
        long distance = Isqrt((long)cx * cx + (long)cz * cz);
        var (low, span, lots) = distance < 1500 ? (20, 60, 4) : distance < 3500 ? (8, 17, 4) : (4, 6, 2);
        for (int lot = 0; lot < 4; lot++)
        {
            if (lot >= lots && Hash(bx, bz, 20 + lot) % 2 == 0) continue;
            int lx = cx + (lot % 2 == 0 ? -25 : 25), lz = cz + (lot < 2 ? -25 : 25);
            int w = 26 + (int)(Hash(bx, bz, 30 + lot) % 18), d = 26 + (int)(Hash(bx, bz, 40 + lot) % 18);
            int jx = (int)(Hash(bx, bz, 50 + lot) % (uint)(46 - w + 1)) - (46 - w) / 2, jz = (int)(Hash(bx, bz, 60 + lot) % (uint)(46 - d + 1)) - (46 - d) / 2;
            int h = low + (int)(Hash(bx, bz, 70 + lot) % (uint)span);
            if (OnRoad(lx + jx, lz + jz, Math.Max(w, d) / 2 + 2)) continue;
            output.Add(new Building($"gen:c:{bx}:{bz}:{lot}", lx + jx, lz + jz, w, d, h));
        }
    }

    void TownBlock(Town t, int bx, int bz, List<Building> output)
    {
        int x = t.X + bx * TownPitch, z = t.Z + bz * TownPitch;
        if ((long)(x - t.X) * (x - t.X) + (long)(z - t.Z) * (z - t.Z) > (long)t.Radius * t.Radius || Hash(t.Index * 1000 + bx, bz, 11) % 4 == 0) return;
        int w = 14 + (int)(Hash(t.Index * 1000 + bx, bz, 12) % 30), d = 14 + (int)(Hash(t.Index * 1000 + bx, bz, 13) % 30);
        if (OnRoad(x, z, Math.Max(w, d) / 2 + 2)) return;   // keep roads clear through towns
        output.Add(new Building($"gen:t{t.Index}:{bx}:{bz}", x, z, w, d, 3 + (int)(Hash(t.Index * 1000 + bx, bz, 14) % 6)));
    }

    // Population homes: 5x5-block city districts plus one per town. Neighbouring districts and town roads are linked.
    public (List<Settlement> Settlements, List<(int A, int B)> Links) Settlements()
    {
        var list = new List<Settlement>();
        var index = new Dictionary<(int, int), int>();
        int span = DistrictBlocks * Pitch, reach = CityRadius / span + 1;
        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = dx * span, z = dz * span;
                if (!InCity(x, z)) continue;
                index[(dx, dz)] = list.Count;
                list.Add(new Settlement(list.Count, x, z, span / 2, dx == 0 && dz == 0));
            }
        var links = new List<(int, int)>();
        foreach (var ((dx, dz), i) in index)
        {
            if (index.TryGetValue((dx + 1, dz), out int right)) links.Add((i, right));
            if (index.TryGetValue((dx, dz + 1), out int down)) links.Add((i, down));
        }
        foreach (var t in Towns)
        {
            var road = Roads[t.Index];
            int nearest = list.Take(index.Count).OrderBy(s => (long)(s.X - road.X1) * (s.X - road.X1) + (long)(s.Z - road.Z1) * (s.Z - road.Z1)).ThenBy(s => s.Index).First().Index;
            links.Add((nearest, list.Count));
            list.Add(new Settlement(list.Count, t.X, t.Z, t.Radius, false));
        }
        return (list, links);
    }
}
