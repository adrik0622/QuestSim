using System.Security.Cryptography;
using System.Text.Json;

namespace QuestSim.Domain;

// Data-defined content. Stackable item *types* only for v1.0; unique instances with rarity/stats come later.
public sealed record ItemDefinition(string Id, string Name, string Kind, bool Stackable, Dictionary<string, int> Properties);
public sealed record ModManifest(string Id, string Version, int Api, string[] Dependencies, Dictionary<string, int> StartingKit);
public sealed record ModRecord(string Id, string Version, string Hash);

public sealed class ContentRegistry
{
    public const int Api = 1;
    public Dictionary<string, ItemDefinition> Items { get; } = new();
    public List<ModRecord> Mods { get; } = new();
    public Dictionary<string, int> StartingKit { get; } = new();

    // Each directory holds manifest.json + items.json. Conflicts, foreign namespaces and missing dependencies refuse to load.
    public static ContentRegistry Load(params string[] directories)
    {
        var registry = new ContentRegistry();
        var manifests = new List<(ModManifest Manifest, string Dir)>();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")), WorldStorage.JsonOptions)
                ?? throw new InvalidDataException($"Empty manifest in {dir}");
            if (manifest.Api != Api) throw new InvalidDataException($"Mod {manifest.Id} targets API {manifest.Api}, this build supports {Api}");
            if (manifests.Any(m => m.Manifest.Id == manifest.Id)) throw new InvalidDataException($"Mod {manifest.Id} is installed twice");
            manifests.Add((manifest, dir));
        }
        foreach (var (manifest, dir) in manifests)
        {
            foreach (var dependency in manifest.Dependencies)
                if (manifests.All(m => m.Manifest.Id != dependency)) throw new InvalidDataException($"Mod {manifest.Id} needs missing mod {dependency}");
            string itemsPath = Path.Combine(dir, "items.json");
            var items = File.Exists(itemsPath) ? JsonSerializer.Deserialize<ItemDefinition[]>(File.ReadAllText(itemsPath), WorldStorage.JsonOptions) ?? [] : [];
            foreach (var item in items)
            {
                if (!item.Id.StartsWith(manifest.Id + ":", StringComparison.Ordinal)) throw new InvalidDataException($"Mod {manifest.Id} defines {item.Id} outside its namespace");
                if (!registry.Items.TryAdd(item.Id, item)) throw new InvalidDataException($"Item {item.Id} is defined twice");
            }
            foreach (var (item, amount) in manifest.StartingKit) registry.StartingKit[item] = registry.StartingKit.GetValueOrDefault(item) + amount;
            var bytes = Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal).SelectMany(File.ReadAllBytes).ToArray();
            registry.Mods.Add(new ModRecord(manifest.Id, manifest.Version, Convert.ToHexString(SHA256.HashData(bytes))[..16]));
        }
        foreach (var item in registry.StartingKit.Keys)
            if (!registry.Items.ContainsKey(item)) throw new InvalidDataException($"Starting kit item {item} is not defined");
        return registry;
    }

    // A world remembers the mods it was made with; a missing one refuses to start instead of dropping data.
    public void Require(IEnumerable<ModRecord> recorded)
    {
        var missing = recorded.Where(r => Mods.All(m => m.Id != r.Id)).Select(r => r.Id).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("This world needs missing mods: " + string.Join(", ", missing));
    }
}
