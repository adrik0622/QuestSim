using QuestSim.Domain;
using System.Text.Json.Nodes;

public static class BuildChecks
{
    public static void Run(Action<bool, string> check)
    {
        string panel = WorldSave.Panel, crate = WorldSave.InitialSupplies[1].Id, site = WorldSave.BuildSite.Id;
        var world = new WorldAuthority(new WorldSave());
        var a = world.Join(2, new string('a', 32), "Alice");
        var b = world.Join(3, new string('b', 32), "Bob");
        check(world.TakeItem(2, Guid.NewGuid().ToString("N"), crate).Success && world.TakeItem(3, Guid.NewGuid().ToString("N"), crate).Success &&
            world.State.Supplies[1].Count == 0 && a.Inventory[panel] == 1 && b.Inventory[panel] == 1, "both players take one panel each from the panel crate");
        string bandages = WorldSave.InitialSupplies[0].Id;
        world.TakeItem(3, Guid.NewGuid().ToString("N"), bandages); world.TakeItem(2, Guid.NewGuid().ToString("N"), bandages);
        string id = Guid.NewGuid().ToString("N"); // Same ID across different actors must stay isolated.
        var first = world.PlaceWall(2, id, site);
        var second = world.PlaceWall(3, id, site);
        check(first.Success && first.Cost == 1 && !second.Success && second.Reason == "occupied" && second.Cost == 0 &&
            a.Inventory[panel] == 0 && b.Inventory[panel] == 1 && world.State.Blocks.Count(x => x.Id == site) == 1,
            "last-cell race places one wall and spends only the winner's panel");
        check(world.PlaceWall(2, id.ToUpperInvariant(), site) == first && world.PlaceWall(3, id, site) == second &&
            world.State.BuildReceipts.Count == 2 && a.Inventory[panel] == 0 && b.Inventory[panel] == 1,
            "winner and loser build retries replay original receipts without another cost");
        bool conflict = false, invalid = false, absent = false;
        try { world.PlaceWall(2, id, "questsim:other-cell"); } catch (InvalidOperationException) { conflict = true; }
        try { world.PlaceWall(2, "bad-id", site); } catch (ArgumentException) { invalid = true; }
        world.Leave(3);
        try { world.PlaceWall(3, Guid.NewGuid().ToString("N"), site); } catch (InvalidOperationException) { absent = true; }
        check(conflict && invalid && absent && world.State.BuildReceipts.Count == 2 && b.Inventory[panel] == 1,
            "conflicting reuse, invalid IDs and disconnected actors cannot build or spend");
        check(!world.IsSafe(WorldSave.BuildSite.X, WorldSave.BuildSite.Z), "placed wall blocks movement");

        var other = new WorldAuthority(new WorldSave());
        var c = other.Join(2, new string('c', 32), "Carol");
        var d = other.Join(3, new string('d', 32), "Dave");
        check(other.PlaceWall(2, Guid.NewGuid().ToString("N"), site).Reason == "no_material" &&
            other.PlaceWall(2, Guid.NewGuid().ToString("N"), "questsim:missing").Reason == "unknown_site", "missing material and unknown site rejected");
        other.TakeItem(2, Guid.NewGuid().ToString("N"), crate);
        d.X = WorldSave.BuildSite.X; d.Z = WorldSave.BuildSite.Z;
        var blocked = other.PlaceWall(2, Guid.NewGuid().ToString("N"), site);
        c.X = 5; c.Z = 5;
        var far = other.PlaceWall(2, Guid.NewGuid().ToString("N"), site);
        check(blocked.Reason == "blocked" && far.Reason == "out_of_reach" && c.Inventory[panel] == 1 && other.State.Blocks.All(x => x.Id != site),
            "active body in the cell and out-of-reach builders keep their panel");
        c.X = -3; c.Z = 0; d.X = -4; d.Z = 0;
        check(other.PlaceWall(2, blocked.RequestId, site) == blocked && other.PlaceWall(2, Guid.NewGuid().ToString("N"), site).Success && c.Inventory[panel] == 0,
            "old rejection replays unchanged; a new request succeeds once the cell is clear");

        string dir = Path.Combine(Path.GetTempPath(), "questsim-build-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, world.State);
            var loaded = new WorldAuthority(WorldStorage.Load(path));
            var restored = loaded.Join(5, a.ProfileId, "Alice");
            loaded.Join(6, b.ProfileId, "Bob");
            check(loaded.PlaceWall(5, id, site) == first && loaded.PlaceWall(6, id, site) == second && restored.Inventory[panel] == 0 &&
                loaded.State.Blocks.Count(x => x.Id == site) == 1 && loaded.State.Supplies[1].Count == 0,
                "restart preserves wall, spent panel, stock and both build receipts");

            var v2 = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            v2["Version"] = 2; v2["ContentId"] = "questsim:q02-room-v1";
            v2["Supply"] = v2["Supplies"]![0]!.DeepClone();
            v2.Remove("Supplies"); v2.Remove("BuildReceipts");
            v2["Blocks"]!.AsArray().RemoveAt(v2["Blocks"]!.AsArray().Count - 1);
            foreach (var character in v2["Characters"]!.AsArray()) character!["Inventory"]!.AsObject().Remove(panel);
            v2["Receipts"] = new JsonArray(v2["Receipts"]!.AsArray().Where(r => r!["ItemId"]!.GetValue<string>() != panel).Select(r => r!.DeepClone()).ToArray());
            string original = v2.ToJsonString(); File.WriteAllText(path, original);
            var migrated = WorldStorage.Load(path);
            string backup = Directory.GetFiles(dir, "world.json.v2-*.bak").Single();
            check(File.ReadAllText(path) == original && File.ReadAllText(backup) == original && migrated.Version == 4 &&
                migrated.Supplies[0] == WorldSave.InitialSupplies[0] with { Count = 0 } && migrated.Supplies[1] == WorldSave.InitialSupplies[1] &&
                migrated.BuildReceipts.Count == 0 && migrated.Receipts.Count == 2,
                "v2 migration backs up original bytes, keeps bandage results and adds one stocked panel crate");
            var after = new WorldAuthority(migrated);
            after.Join(7, a.ProfileId, "Alice");
            after.TakeItem(7, Guid.NewGuid().ToString("N"), crate);
            WorldStorage.Save(path, after.State);
            check(WorldStorage.Load(path).Supplies[1].Count == 1 && Directory.GetFiles(dir, "*.bak").Length == 1,
                "v3 reload does not repeat migration, back up again or restock panels");

            WorldStorage.Save(path, world.State);
            string valid = File.ReadAllText(path);
            bool Rejects(Action<JsonObject> corrupt)
            {
                var node = JsonNode.Parse(valid)!.AsObject(); corrupt(node);
                File.WriteAllText(path, node.ToJsonString());
                try { WorldStorage.Load(path); return false; } catch (InvalidDataException) { return true; }
            }
            JsonNode Owner(JsonObject w, string actor) => w["Characters"]!.AsArray().Single(x => x!["Id"]!.GetValue<string>() == actor)!;
            check(Rejects(w => w["Blocks"]!.AsArray().RemoveAt(w["Blocks"]!.AsArray().Count - 1)), "successful build receipt without its wall rejected");
            check(Rejects(w => Owner(w, a.Id)["Inventory"]![panel] = 1), "spent panel restored to the builder rejected as duplication");
            check(Rejects(w => Owner(w, b.Id)["Inventory"]![panel] = 0), "loser's retained panel disappearing rejected");
            check(Rejects(w => w["BuildReceipts"]!.AsArray().Add(w["BuildReceipts"]![0]!.DeepClone())), "duplicate build receipts rejected");
            check(Rejects(w => w["Supplies"]![1]!["Count"] = 2), "panel restock without matching receipts rejected");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
