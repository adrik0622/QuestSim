using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace QuestSim.Domain;

public sealed record BlockRecord(string Id, double X, double Z, double Width, double Depth, double Height);
public sealed record ActorState(string Id, string Name, double X, double Z, double Heading);
public sealed record SupplyState(string Id, string ItemId, double X, double Z, int Count);
public sealed record PickupReceipt(string RequestId, string ActorId, string ContainerId, string ItemId, bool Success, string Reason, int Quantity);
public sealed record BuildReceipt(string RequestId, string ActorId, string SiteId, bool Success, string Reason, int Cost);
public sealed record WorldSnapshot(int Version, long Sequence, long Tick, double HalfExtent, BlockRecord[] Blocks, ActorState[] Actors, SupplyState[] Supplies);

public sealed class CharacterRecord
{
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string ProfileId { get; set; } = "";
    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired] public double X { get; set; } = -3;
    [JsonRequired] public double Z { get; set; }
    [JsonRequired] public double Heading { get; set; } = -Math.PI / 2;
    [JsonRequired] public Dictionary<string, int> Inventory { get; set; } = new();
    [JsonRequired] public int Experience { get; set; }
}

public sealed class WorldSave
{
    public const string Content = "questsim:q02b-room-v1", Panel = "questsim:wall-panel";
    // Fixed content: each crate's initial stock; the build cell's single panel wall.
    public static readonly SupplyState[] InitialSupplies =
    {
        new("questsim:starter-crate", "questsim:bandage", -3, -1.5, 1),
        new("questsim:panel-crate", Panel, -3, 1.5, 2)
    };
    public static readonly BlockRecord BuildSite = new("questsim:build-cell-1", -4, 1.5, 1, .25, 2);
    [JsonRequired] public int Version { get; set; } = 3;
    [JsonRequired] public string ContentId { get; set; } = Content;
    [JsonRequired] public long Tick { get; set; }
    [JsonRequired] public double HalfExtent { get; set; } = 6;
    [JsonRequired] public List<SupplyState> Supplies { get; set; } = InitialSupplies.ToList();
    [JsonRequired] public List<PickupReceipt> Receipts { get; set; } = new();
    [JsonRequired] public List<BuildReceipt> BuildReceipts { get; set; } = new();
    [JsonRequired] public List<CharacterRecord> Characters { get; set; } = new();
    [JsonRequired] public List<BlockRecord> Blocks { get; set; } = new()
    {
        new("questsim:wall-north", 0, -3.5, .25, 5, 2.8),
        new("questsim:wall-south", 0, 3.5, .25, 5, 2.8),
        new("questsim:module", -2, 2, 1, 1, 1)
    };
}

public sealed class WorldAuthority
{
    public const int ProtocolVersion = 3;
    public const int TickRate = 20;
    public const double Speed = 2.5;
    public const double Radius = .25;
    public WorldSave State { get; }
    public int ActiveCount => active.Count;
    private readonly Dictionary<int, CharacterRecord> active = new();
    private readonly Dictionary<int, (long Sequence, double X, double Z, double Heading, long Expires)> intents = new();
    private long snapshotSequence;

    public WorldAuthority(WorldSave state) { WorldStorage.Validate(state); State = state; }

    public CharacterRecord Join(int peer, string profileId, string name)
    {
        if (peer <= 1 || !Guid.TryParseExact(profileId, "N", out _) || string.IsNullOrWhiteSpace(name) || name.Length > 24)
            throw new ArgumentException("Invalid join identity");
        if (active.TryGetValue(peer, out var joined))
        {
            if (joined.ProfileId != profileId) throw new InvalidOperationException("Peer already joined");
            return joined;
        }
        if (active.Values.Any(c => c.ProfileId == profileId)) throw new InvalidOperationException("Character is already connected");
        var character = State.Characters.SingleOrDefault(c => c.ProfileId == profileId);
        bool created = character is null;
        character ??= new CharacterRecord { Id = "actor:" + Guid.NewGuid().ToString("N"), ProfileId = profileId, Name = name.Trim() };
        var safe = FindSafe(character.X, character.Z, character.Id);
        character.X = safe.X; character.Z = safe.Z;
        if (created) State.Characters.Add(character);
        active.Add(peer, character);
        return character;
    }

    public CharacterRecord CharacterFor(int peer) => active.TryGetValue(peer, out var character)
        ? character : throw new InvalidOperationException("Character is not connected");

    public PickupReceipt TakeItem(int peer, string requestId, string containerId)
    {
        var character = CharacterFor(peer);
        if (!Guid.TryParseExact(requestId, "N", out var guid) || string.IsNullOrWhiteSpace(containerId) || containerId.Length > 128)
            throw new ArgumentException("Invalid pickup request");
        requestId = guid.ToString("N");
        var previous = State.Receipts.SingleOrDefault(r => r.ActorId == character.Id && r.RequestId == requestId);
        if (previous is not null)
        {
            if (previous.ContainerId != containerId) throw new InvalidOperationException("Request ID reused for a different target");
            return previous;
        }
        int index = State.Supplies.FindIndex(s => s.Id == containerId);
        var supply = index < 0 ? null : State.Supplies[index];
        int held = supply is null ? 0 : character.Inventory.GetValueOrDefault(supply.ItemId);
        string reason = supply is null ? "unknown_container" :
            !InReach(character, supply.X, supply.Z) ? "out_of_reach" :
            supply.Count == 0 ? "empty" : held == int.MaxValue ? "inventory_full" : "taken";
        bool success = reason == "taken";
        var receipt = new PickupReceipt(requestId, character.Id, containerId, supply?.ItemId ?? "", success, reason, success ? 1 : 0);
        // One synchronous domain operation; the server saves all three changes before replying.
        if (success)
        {
            State.Supplies[index] = supply! with { Count = supply.Count - 1 };
            character.Inventory[supply.ItemId] = held + 1;
        }
        State.Receipts.Add(receipt);
        return receipt;
    }

    public BuildReceipt PlaceWall(int peer, string requestId, string siteId)
    {
        var character = CharacterFor(peer);
        if (!Guid.TryParseExact(requestId, "N", out var guid) || string.IsNullOrWhiteSpace(siteId) || siteId.Length > 128)
            throw new ArgumentException("Invalid build request");
        requestId = guid.ToString("N");
        var previous = State.BuildReceipts.SingleOrDefault(r => r.ActorId == character.Id && r.RequestId == requestId);
        if (previous is not null)
        {
            if (previous.SiteId != siteId) throw new InvalidOperationException("Request ID reused for a different site");
            return previous;
        }
        var site = WorldSave.BuildSite;
        int panels = character.Inventory.GetValueOrDefault(WorldSave.Panel);
        // Occupied precedes material so the last-cell loser keeps its panel.
        string reason = siteId != site.Id ? "unknown_site" : !InReach(character, site.X, site.Z) ? "out_of_reach" :
            State.Blocks.Any(b => b.Id == site.Id) ? "occupied" : panels < 1 ? "no_material" :
            active.Values.Any(c => !Clear(c.X, c.Z, site)) ? "blocked" : "placed";
        bool success = reason == "placed";
        var receipt = new BuildReceipt(requestId, character.Id, siteId, success, reason, success ? 1 : 0);
        // Block, spent panel and receipt are saved together by the server before replying.
        if (success)
        {
            State.Blocks.Add(site);
            character.Inventory[WorldSave.Panel] = panels - 1;
        }
        State.BuildReceipts.Add(receipt);
        return receipt;
    }

    private static bool InReach(CharacterRecord c, double x, double z) => Math.Pow(c.X - x, 2) + Math.Pow(c.Z - z, 2) <= 4;
    private static bool Clear(double x, double z, BlockRecord b) => Math.Abs(x - b.X) >= b.Width / 2 + Radius || Math.Abs(z - b.Z) >= b.Depth / 2 + Radius;

    public bool Leave(int peer) { intents.Remove(peer); return active.Remove(peer); }

    public bool SetIntent(int peer, long sequence, double x, double z, double heading)
    {
        if (!active.ContainsKey(peer) || sequence <= 0 || !double.IsFinite(x) || !double.IsFinite(z) ||
            Math.Abs(x) > 1 || Math.Abs(z) > 1 || !double.IsFinite(heading) ||
            (intents.TryGetValue(peer, out var previous) && sequence <= previous.Sequence)) return false;
        double length = Math.Sqrt(x * x + z * z);
        if (length > 1) { x /= length; z /= length; }
        intents[peer] = (sequence, x, z, Math.IEEERemainder(heading, Math.Tau), State.Tick + 5);
        return true;
    }

    public void Step()
    {
        if (ActiveCount == 0) return;
        State.Tick++;
        foreach (var (peer, c) in active)
        {
            if (!intents.TryGetValue(peer, out var intent) || intent.Expires < State.Tick) continue;
            c.Heading = intent.Heading;
            double dx = intent.X * Speed / TickRate, dz = intent.Z * Speed / TickRate;
            if (IsSafe(c.X + dx, c.Z, c.Id)) c.X += dx;
            if (IsSafe(c.X, c.Z + dz, c.Id)) c.Z += dz;
        }
    }

    public bool IsSafe(double x, double z, string exceptId = "") =>
        double.IsFinite(x) && double.IsFinite(z) && Math.Abs(x) <= State.HalfExtent - Radius && Math.Abs(z) <= State.HalfExtent - Radius &&
        State.Supplies.All(s => Math.Abs(x - s.X) >= .3 + Radius || Math.Abs(z - s.Z) >= .3 + Radius) &&
        State.Blocks.All(b => Clear(x, z, b)) &&
        active.Values.All(c => c.Id == exceptId || (x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z) >= .36);

    private (double X, double Z) FindSafe(double x, double z, string id)
    {
        if (IsSafe(x, z, id)) return (x, z);
        var candidates = from gx in Enumerable.Range(-5, 11) from gz in Enumerable.Range(-5, 11)
                         orderby (gx - x) * (gx - x) + (gz - z) * (gz - z), gx, gz
                         select (X: (double)gx, Z: (double)gz);
        foreach (var p in candidates) if (IsSafe(p.X, p.Z, id)) return p;
        throw new InvalidOperationException("No safe re-entry position in this room");
    }

    public WorldSnapshot Snapshot() => new(ProtocolVersion, ++snapshotSequence, State.Tick, State.HalfExtent,
        State.Blocks.ToArray(), active.Values.OrderBy(c => c.Id).Select(c => new ActorState(c.Id, c.Name, c.X, c.Z, c.Heading)).ToArray(), State.Supplies.ToArray());
}

public static class WorldStorage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectRequiredConstructorParameters = true };
    public static WorldSave Load(string path)
    {
        if (!File.Exists(path)) return new WorldSave();
        string original = File.ReadAllText(path);
        var root = JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("Empty world save");
        int? from = root["Version"]?.GetValue<int>();
        if (from == 1)
        {
            if (root["ContentId"]?.GetValue<string>() != "questsim:q01-room-v1") throw new InvalidDataException("Unsupported v1 content");
            root["Version"] = 2;
            root["ContentId"] = "questsim:q02-room-v1";
            root["Supply"] = JsonSerializer.SerializeToNode(WorldSave.InitialSupplies[0]);
            root["Receipts"] = new JsonArray();
        }
        if (root["Version"]?.GetValue<int>() == 2)
        {
            if (root["ContentId"]?.GetValue<string>() != "questsim:q02-room-v1") throw new InvalidDataException("Unsupported v2 content");
            var supply = root["Supply"] ?? throw new InvalidDataException("Missing v2 supply");
            root.Remove("Supply");
            root["Supplies"] = new JsonArray(supply, JsonSerializer.SerializeToNode(WorldSave.InitialSupplies[1]));
            root["BuildReceipts"] = new JsonArray();
            root["Version"] = 3;
            root["ContentId"] = WorldSave.Content;
        }
        var world = JsonSerializer.Deserialize<WorldSave>(root.ToJsonString(), JsonOptions) ?? throw new InvalidDataException("Empty world save");
        Validate(world);
        // Validate first; backup is immutable and created before any v3 save can replace the original.
        if (from is 1 or 2) File.Copy(path, path + $".v{from}-" + Guid.NewGuid().ToString("N") + ".bak", overwrite: false);
        return world;
    }
    public static void Save(string path, WorldSave world)
    {
        Validate(world);
        WriteAtomic(path, JsonSerializer.Serialize(world, JsonOptions));
    }
    public static void WriteAtomic(string path, string json)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(file, leaveOpen: true)) { writer.Write(json); writer.Flush(); }
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
    }
    public static void Validate(WorldSave w)
    {
        if (w.Version != 3 || w.ContentId != WorldSave.Content) throw new InvalidDataException("Unsupported world save version/content");
        if (w.Tick < 0 || !double.IsFinite(w.HalfExtent) || w.HalfExtent != 6 || w.Characters is null || w.Blocks is null)
            throw new InvalidDataException("Invalid world data");
        if (w.Characters.Any(c => c is null || string.IsNullOrEmpty(c.Id) || !c.Id.StartsWith("actor:") || !Guid.TryParseExact(c.Id[6..], "N", out _) ||
            !Guid.TryParseExact(c.ProfileId, "N", out _) || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 24 ||
            !double.IsFinite(c.X) || !double.IsFinite(c.Z) || !double.IsFinite(c.Heading) || c.Inventory is null || c.Experience < 0 ||
            c.Inventory.Any(i => string.IsNullOrWhiteSpace(i.Key) || i.Value < 0)) ||
            w.Characters.Select(c => c.Id).Distinct().Count() != w.Characters.Count ||
            w.Characters.Select(c => c.ProfileId).Distinct().Count() != w.Characters.Count)
            throw new InvalidDataException("Invalid/duplicate character data");
        if (w.Blocks.Any(b => b is null || string.IsNullOrWhiteSpace(b.Id) || !double.IsFinite(b.X) || !double.IsFinite(b.Z) ||
            !double.IsFinite(b.Width) || !double.IsFinite(b.Depth) || !double.IsFinite(b.Height) ||
            b.Width <= 0 || b.Depth <= 0 || b.Height <= 0 || b.Width > 12 || b.Depth > 12 || b.Height > 6) ||
            w.Blocks.Select(b => b.Id).Distinct().Count() != w.Blocks.Count)
            throw new InvalidDataException("Invalid/duplicate world block data");
        if (w.Supplies is null || w.Receipts is null || w.BuildReceipts is null || w.Supplies.Count != WorldSave.InitialSupplies.Length ||
            w.Supplies.Zip(WorldSave.InitialSupplies).Any(p => p.First is null || p.First with { Count = p.Second.Count } != p.Second ||
            p.First.Count < 0 || p.First.Count > p.Second.Count))
            throw new InvalidDataException("Invalid supply data");
        var items = w.Supplies.Select(s => s.ItemId).ToHashSet();
        if (w.Receipts.Any(r => r is null || !Guid.TryParseExact(r.RequestId, "N", out var guid) || r.RequestId != guid.ToString("N") ||
            !w.Characters.Any(c => c.Id == r.ActorId) || string.IsNullOrWhiteSpace(r.ContainerId) || r.ContainerId.Length > 128 ||
            (r.Success ? r.Quantity != 1 || r.Reason != "taken" || !w.Supplies.Any(s => s.Id == r.ContainerId && s.ItemId == r.ItemId)
                : r.Quantity != 0 || r.ItemId is null || (r.ItemId != "" && !items.Contains(r.ItemId)) ||
                  r.Reason is not ("empty" or "out_of_reach" or "unknown_container" or "inventory_full"))) ||
            w.Receipts.Select(r => (r.ActorId, r.RequestId)).Distinct().Count() != w.Receipts.Count)
            throw new InvalidDataException("Invalid/duplicate pickup receipts");
        if (w.BuildReceipts.Any(r => r is null || !Guid.TryParseExact(r.RequestId, "N", out var guid) || r.RequestId != guid.ToString("N") ||
            !w.Characters.Any(c => c.Id == r.ActorId) || string.IsNullOrWhiteSpace(r.SiteId) || r.SiteId.Length > 128 ||
            (r.Success ? r.Cost != 1 || r.Reason != "placed" || r.SiteId != WorldSave.BuildSite.Id
                : r.Cost != 0 || r.Reason is not ("unknown_site" or "out_of_reach" or "occupied" or "no_material" or "blocked"))) ||
            w.BuildReceipts.Select(r => (r.ActorId, r.RequestId)).Distinct().Count() != w.BuildReceipts.Count)
            throw new InvalidDataException("Invalid/duplicate build receipts");
        int placed = w.BuildReceipts.Count(r => r.Success);
        if (w.Supplies.Zip(WorldSave.InitialSupplies).Any(p => w.Receipts.Count(r => r.Success && r.ContainerId == p.First.Id) != p.Second.Count - p.First.Count) ||
            w.Characters.Any(c => items.Any(item => c.Inventory.GetValueOrDefault(item) <
                w.Receipts.Count(r => r.Success && r.ActorId == c.Id && r.ItemId == item) -
                (item == WorldSave.Panel ? w.BuildReceipts.Count(r => r.Success && r.ActorId == c.Id) : 0))) ||
            w.Characters.Sum(c => (long)c.Inventory.GetValueOrDefault(WorldSave.Panel)) != w.Receipts.Count(r => r.Success && r.ItemId == WorldSave.Panel) - placed ||
            placed != w.Blocks.Count(b => b.Id == WorldSave.BuildSite.Id) || (placed == 1 && w.Blocks.Single(b => b.Id == WorldSave.BuildSite.Id) != WorldSave.BuildSite))
            throw new InvalidDataException("Supplies, inventories, walls and receipts disagree");
    }
}
