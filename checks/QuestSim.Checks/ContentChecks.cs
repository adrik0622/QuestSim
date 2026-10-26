using QuestSim.Domain;

public static class ContentChecks
{
    static string Mod(string root, string id, string items, string deps = "[]", int api = 1)
    {
        string dir = Path.Combine(root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $"{{\"Id\":\"{id}\",\"Version\":\"1.0.0\",\"Api\":{api},\"Dependencies\":{deps},\"StartingKit\":{{}}}}");
        File.WriteAllText(Path.Combine(dir, "items.json"), items);
        return dir;
    }

    static bool Refuses(Func<object> load) { try { load(); return false; } catch (InvalidDataException) { return true; } }

    public static void Run(Action<bool, string> check)
    {
        var registry = ContentRegistry.Load("content/core", "mods/example-rations");
        check(registry.Items.Count == 6 && registry.Items["rations:canned-beans"].Properties["heal"] == 15 && registry.StartingKit["rations:canned-beans"] == 2 &&
              registry.Mods.Select(m => m.Id).SequenceEqual(["questsim", "rations"]), "core content and the example data-only mod load: 6 item definitions, mod starting kit");
        string root = Path.Combine(Path.GetTempPath(), "questsim-mods-" + Guid.NewGuid().ToString("N"));
        try
        {
            string clash = Mod(root, "rations", "[{\"Id\":\"rations:canned-beans\",\"Name\":\"x\",\"Kind\":\"consumable\",\"Stackable\":true,\"Properties\":{}}]");
            string foreign = Mod(root, "sneaky", "[{\"Id\":\"questsim:bandage\",\"Name\":\"x\",\"Kind\":\"consumable\",\"Stackable\":true,\"Properties\":{}}]");
            string future = Mod(root, "future", "[]", api: 2);
            check(Refuses(() => ContentRegistry.Load("content/core", "mods/example-rations", clash)) &&
                  Refuses(() => ContentRegistry.Load("content/core", foreign)) &&
                  Refuses(() => ContentRegistry.Load("mods/example-rations")) &&
                  Refuses(() => ContentRegistry.Load("content/core", future)),
                "duplicate mods, foreign namespaces, missing dependencies and wrong API versions refuse to load");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        check(Refuses(() => { ContentRegistry.Load("content/core").Require(registry.Mods); return 0; }),
            "a world made with a mod refuses to start without it (no silent data loss)");

        var w = new WorldAuthority(new WorldSave { StartingKit = new(registry.StartingKit) }) { Content = registry };
        var a = w.Join(2, new string('a', 32), "Alice");
        a.X = -3; a.Z = -1;
        w.TakeItem(2, Guid.NewGuid().ToString("N"), WorldSave.InitialSupplies[0].Id);
        a.Stats[CharacterRecord.Health] = 40;
        check(w.Use(2, "questsim:bandage") == "used" && a.Stat(CharacterRecord.Health) == 65 && !a.Inventory.ContainsKey("questsim:bandage") &&
              w.Use(2, "rations:canned-beans") == "used" && a.Stat(CharacterRecord.Health) == 80 && a.Inventory["rations:canned-beans"] == 1 &&
              w.Use(2, "questsim:pistol") == "not_usable" && w.Use(2, "examplemod:nothing") == "unknown_item" && w.Use(2, "questsim:bandage") == "none_left",
            "consumables heal by their data-defined amount; uses are limited by what you hold");
        string dir = Path.Combine(Path.GetTempPath(), "questsim-use-" + Guid.NewGuid().ToString("N"));
        try
        {
            WorldStorage.Save(Path.Combine(dir, "world.json"), w.State);
            check(WorldStorage.Load(Path.Combine(dir, "world.json")).Transfers.Count(t => t.To == "consumed") == 2,
                "using the crate's bandage is ledgered, so supply conservation still validates after reload");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
