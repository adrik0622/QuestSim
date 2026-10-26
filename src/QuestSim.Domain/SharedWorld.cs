using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace QuestSim.Domain;

public sealed record BlockRecord(string Id, double X, double Z, double Width, double Depth, double Height);
public sealed record ActorState(string Id, string Name, double X, double Z, double Heading);
public sealed record SupplyState(string Id, string ItemId, double X, double Z, int Count);
public sealed record PickupReceipt(string RequestId, string ActorId, string ContainerId, string ItemId, bool Success, string Reason, int Quantity);
public sealed record WorldSnapshot(int Version, long Sequence, long Tick, double HalfExtent, BlockRecord[] Blocks, ActorState[] Actors, SupplyState Supply);

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
    [JsonRequired] public int Version { get; set; } = 2;
    [JsonRequired] public string ContentId { get; set; } = "questsim:q02-room-v1";
    [JsonRequired] public long Tick { get; set; }
    [JsonRequired] public double HalfExtent { get; set; } = 6;
    [JsonRequired] public SupplyState Supply { get; set; } = new("questsim:starter-crate", "questsim:bandage", -3, -1.5, 1);
    [JsonRequired] public List<PickupReceipt> Receipts { get; set; } = new();
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
    public const int ProtocolVersion = 2;
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
        var supply = State.Supply;
        int held = character.Inventory.GetValueOrDefault(supply.ItemId);
        string reason = containerId != supply.Id ? "unknown_container" :
            Math.Pow(character.X - supply.X, 2) + Math.Pow(character.Z - supply.Z, 2) > 4 ? "out_of_reach" :
            supply.Count == 0 ? "empty" : held == int.MaxValue ? "inventory_full" : "taken";
        bool success = reason == "taken";
        var receipt = new PickupReceipt(requestId, character.Id, containerId, supply.ItemId, success, reason, success ? 1 : 0);
        // One synchronous domain operation; the server saves all three changes before replying.
        if (success)
        {
            State.Supply = supply with { Count = supply.Count - 1 };
            character.Inventory[supply.ItemId] = held + 1;
        }
        State.Receipts.Add(receipt);
        return receipt;
    }

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
        (Math.Abs(x - State.Supply.X) >= .3 + Radius || Math.Abs(z - State.Supply.Z) >= .3 + Radius) &&
        State.Blocks.All(b => Math.Abs(x - b.X) >= b.Width / 2 + Radius || Math.Abs(z - b.Z) >= b.Depth / 2 + Radius) &&
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
        State.Blocks.ToArray(), active.Values.OrderBy(c => c.Id).Select(c => new ActorState(c.Id, c.Name, c.X, c.Z, c.Heading)).ToArray(), State.Supply);
}

public static class WorldStorage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, RespectRequiredConstructorParameters = true };
    public static WorldSave Load(string path)
    {
        if (!File.Exists(path)) return new WorldSave();
        string original = File.ReadAllText(path);
        var root = JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("Empty world save");
        bool migrate = root["Version"]?.GetValue<int>() == 1;
        if (migrate)
        {
            if (root["ContentId"]?.GetValue<string>() != "questsim:q01-room-v1") throw new InvalidDataException("Unsupported v1 content");
            root["Version"] = 2;
            root["ContentId"] = "questsim:q02-room-v1";
            root["Supply"] = JsonSerializer.SerializeToNode(new WorldSave().Supply);
            root["Receipts"] = new JsonArray();
        }
        var world = JsonSerializer.Deserialize<WorldSave>(root.ToJsonString(), JsonOptions) ?? throw new InvalidDataException("Empty world save");
        Validate(world);
        // Validate first; backup is immutable and created before any v2 save can replace v1.
        if (migrate) File.Copy(path, path + ".v1-" + Guid.NewGuid().ToString("N") + ".bak", overwrite: false);
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
        if (w.Version != 2 || w.ContentId != "questsim:q02-room-v1") throw new InvalidDataException("Unsupported world save version/content");
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
        var supply = w.Supply;
        if (supply is null || supply.Id != "questsim:starter-crate" || supply.ItemId != "questsim:bandage" ||
            !double.IsFinite(supply.X) || !double.IsFinite(supply.Z) || Math.Abs(supply.X) > 5.5 || Math.Abs(supply.Z) > 5.5 || supply.Count is < 0 or > 1 || w.Receipts is null)
            throw new InvalidDataException("Invalid supply data");
        if (w.Receipts.Any(r => r is null || !Guid.TryParseExact(r.RequestId, "N", out var guid) || r.RequestId != guid.ToString("N") ||
            !w.Characters.Any(c => c.Id == r.ActorId) || string.IsNullOrWhiteSpace(r.ContainerId) || r.ContainerId.Length > 128 || r.ItemId != supply.ItemId ||
            (r.Success ? r.Quantity != 1 || r.Reason != "taken" : r.Quantity != 0 || r.Reason is not ("empty" or "out_of_reach" or "unknown_container" or "inventory_full"))) ||
            w.Receipts.Select(r => (r.ActorId, r.RequestId)).Distinct().Count() != w.Receipts.Count)
            throw new InvalidDataException("Invalid/duplicate pickup receipts");
        if (w.Receipts.Count(r => r.Success) != 1 - supply.Count || w.Receipts.Any(r => r.Success && r.ContainerId != supply.Id))
            throw new InvalidDataException("Supply and pickup receipts disagree");
    }
}
