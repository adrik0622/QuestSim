using QuestSim.Domain;
using System.Text.Json.Nodes;

public static class PickupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = new WorldAuthority(new WorldSave());
        var a = world.Join(2, new string('a', 32), "Alice");
        var b = world.Join(3, new string('b', 32), "Bob");
        string item = world.State.Supply.ItemId, target = world.State.Supply.Id;
        string firstId = Guid.NewGuid().ToString("N"), secondId = Guid.NewGuid().ToString("N");
        var first = world.TakeItem(2, firstId, target);
        var second = world.TakeItem(3, secondId, target);
        check(first.Success && !second.Success && second.Reason == "empty" &&
            a.Inventory.GetValueOrDefault(item) + b.Inventory.GetValueOrDefault(item) + world.State.Supply.Count == 1,
            "last-item race transfers exactly one bandage and conserves resources");
        check(world.TakeItem(2, firstId.ToUpperInvariant(), target) == first && world.TakeItem(3, secondId, target) == second && world.State.Receipts.Count == 2,
            "winner and loser retries, including GUID case variants, replay original receipts");
        bool conflict = false;
        try { world.TakeItem(2, firstId, "questsim:other"); } catch (InvalidOperationException) { conflict = true; }
        check(conflict && world.State.Receipts.Count == 2 && a.Inventory[item] == 1, "conflicting reuse cannot alter the original receipt or inventory");
        bool invalid = false, absent = false;
        try { world.TakeItem(2, "bad-id", target); } catch (ArgumentException) { invalid = true; }
        world.Leave(3);
        try { world.TakeItem(3, Guid.NewGuid().ToString("N"), target); } catch (InvalidOperationException) { absent = true; }
        check(invalid && absent && world.State.Receipts.Count == 2, "invalid IDs and disconnected actors cannot perform item transactions");
        var other = new WorldAuthority(new WorldSave());
        var far = other.Join(2, new string('c', 32), "Far"); far.X = 5; far.Z = 5;
        var denied = other.TakeItem(2, Guid.NewGuid().ToString("N"), target);
        var missing = other.TakeItem(2, Guid.NewGuid().ToString("N"), "questsim:missing");
        check(denied.Reason == "out_of_reach" && missing.Reason == "unknown_container" && other.State.Supply.Count == 1 && far.Inventory.Count == 0,
            "reach and target validation prevent remote or unknown-container transfers");
        far.X = -3; far.Z = 0;
        check(other.TakeItem(2, denied.RequestId, target) == denied && other.State.Supply.Count == 1,
            "previous rejection remains stable after player moves into reach");
        far.Inventory[item] = int.MaxValue;
        check(other.TakeItem(2, Guid.NewGuid().ToString("N"), target).Reason == "inventory_full" && other.State.Supply.Count == 1,
            "inventory overflow rejected without consuming supply");
        string dir = Path.Combine(Path.GetTempPath(), "questsim-pickup-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, world.State);
            var loaded = new WorldAuthority(WorldStorage.Load(path));
            var restored = loaded.Join(5, a.ProfileId, "Alice");
            check(loaded.TakeItem(5, firstId, target) == first && restored.Inventory[item] == 1 && loaded.State.Supply.Count == 0,
                "reconnect and restart preserve consumed supply, inventory and successful receipt");
            loaded.Join(6, b.ProfileId, "Bob");
            check(loaded.TakeItem(6, secondId, target) == second, "losing receipt persists across reconnect/restart");
            var v1 = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            v1["Version"] = 1; v1["ContentId"] = "questsim:q01-room-v1"; v1.Remove("Supply"); v1.Remove("Receipts");
            string original = v1.ToJsonString(); File.WriteAllText(path, original);
            var migrated = WorldStorage.Load(path);
            string backup = Directory.GetFiles(dir, "world.json.v1-*.bak").Single();
            check(File.ReadAllText(path) == original && File.ReadAllText(backup) == original && migrated.Version == 2 &&
                migrated.Characters.Single(c => c.Id == a.Id).Inventory[item] == 1 && migrated.Tick == world.State.Tick && migrated.Blocks.SequenceEqual(world.State.Blocks),
                "Q01 migration validates and backs up original bytes, preserving character/world facts");
            WorldStorage.Save(path, migrated);
            var migratedAuthority = new WorldAuthority(WorldStorage.Load(path));
            migratedAuthority.Join(9, a.ProfileId, "Alice");
            migratedAuthority.TakeItem(9, Guid.NewGuid().ToString("N"), target);
            WorldStorage.Save(path, migratedAuthority.State);
            check(WorldStorage.Load(path).Supply.Count == 0 && Directory.GetFiles(dir, "*.bak").Length == 1,
                "v2 reload does not repeat migration, create another backup or restock the crate");
            var corrupt = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            corrupt["Supply"]!["Count"] = 1;
            File.WriteAllText(path, corrupt.ToJsonString());
            bool inconsistent = false;
            try { WorldStorage.Load(path); } catch (InvalidDataException) { inconsistent = true; }
            check(inconsistent, "stock/receipt disagreement rejected instead of restoring a consumed item");
            corrupt["Supply"]!["Count"] = 0;
            corrupt["Receipts"]!.AsArray().Add(corrupt["Receipts"]![0]!.DeepClone());
            File.WriteAllText(path, corrupt.ToJsonString());
            bool rejected = false;
            try { WorldStorage.Load(path); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "duplicate persisted receipts rejected rather than making replay ambiguous");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
