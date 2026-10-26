using QuestSim.Domain;
using System.Diagnostics;

public static class CityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var gen = new CityGen(20261026);
        var again = new CityGen(20261026);
        var other = new CityGen(7);
        var sample = new[] { (0, 0), (3, -2), (-30, 12), (60, 60), (-100, -40) };
        check(sample.All(c => gen.Chunk(c.Item1, c.Item2).SequenceEqual(again.Chunk(c.Item1, c.Item2))) &&
              !sample.All(c => gen.Chunk(c.Item1, c.Item2).SequenceEqual(other.Chunk(c.Item1, c.Item2))),
            "city chunks regenerate identically from the seed and differ across seeds");
        var clock = Stopwatch.StartNew();
        int n = CityGen.Half / CityGen.ChunkSize;
        var all = new List<Building>();
        for (int cx = -n; cx < n; cx++)
            for (int cz = -n; cz < n; cz++)
                foreach (var b in gen.Chunk(cx, cz))
                {
                    if (b.X < cx * CityGen.ChunkSize || b.X >= (cx + 1) * CityGen.ChunkSize || b.Z < cz * CityGen.ChunkSize || b.Z >= (cz + 1) * CityGen.ChunkSize)
                        throw new Exception("building outside its chunk: " + b.Id);
                    all.Add(b);
                }
        check(all.Select(b => b.Id).Distinct().Count() == all.Count && all.Count > 10000,
            $"whole 40x40 km world: {all.Count:N0} buildings, each owned by exactly one chunk ({clock.ElapsedMilliseconds} ms)");
        var grid = all.GroupBy(b => (b.X / 64, b.Z / 64)).ToDictionary(g => g.Key, g => g.ToArray());
        bool overlap = all.Any(a => Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dz => (a.X / 64 + dx, a.Z / 64 + dz)))
            .Where(grid.ContainsKey).SelectMany(k => grid[k])
            .Any(b => b != a && Math.Abs(a.X - b.X) * 2 < a.Width + b.Width && Math.Abs(a.Z - b.Z) * 2 < a.Depth + b.Depth));
        check(!overlap, "generated buildings never overlap each other");
        check(all.All(b => Math.Abs(b.X) - b.Width / 2 > 40 || Math.Abs(b.Z) - b.Depth / 2 > 40) &&
              all.All(b => !gen.OnRoad(b.X, b.Z, Math.Max(b.Width, b.Depth) / 2)),
            "the spawn plaza and every arterial road stay clear of buildings");
        check(gen.Towns.Count == 8 && gen.Towns.All(t => Math.Abs(t.X) < CityGen.Half - t.Radius && Math.Abs(t.Z) < CityGen.Half - t.Radius) &&
              gen.BiomeAt(0, 0) == Biome.Urban && gen.Towns.All(t => gen.BiomeAt(t.X, t.Z) == Biome.Town),
            "eight outskirt towns sit inside the world with town biome; the centre is urban");
        var biomes = new HashSet<Biome>();
        for (int x = -19000; x < 19000; x += 997) for (int z = -19000; z < 19000; z += 997) biomes.Add(gen.BiomeAt(x, z));
        check(biomes.SetEquals(Enum.GetValues<Biome>()), "countryside mixes fields and forests");
        var (settlements, links) = gen.Settlements();
        var neighbours = links.SelectMany(l => new[] { (l.A, l.B), (l.B, l.A) }).ToLookup(l => l.Item1, l => l.Item2);
        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
            foreach (int next in neighbours[queue.Dequeue()])
                if (seen.Add(next)) queue.Enqueue(next);
        check(seen.Count == settlements.Count && settlements.Count(s => s.Spawn) == 1 && settlements.Count > 100,
            $"{settlements.Count} population settlements (city districts + towns) form one connected road network");
    }
}
