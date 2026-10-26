using QuestSim.Domain;

public static class AuthorityChecks
{
    public static void Run(Action<bool, string> check)
    {
        string alice = new string('a', 32), bob = new string('b', 32);
        var world = new WorldAuthority(new WorldSave());
        for (int i = 0; i < 20; i++) world.Step();
        check(world.State.Tick == 0, "empty world clock is frozen");
        var a = world.Join(2, alice, "Alice");
        check(ReferenceEquals(a, world.Join(2, alice, "Alice")) && world.State.Characters.Count == 1, "repeated join is idempotent");
        bool duplicate = false;
        try { world.Join(3, alice, "Imposter"); } catch (InvalidOperationException) { duplicate = true; }
        check(duplicate && world.ActiveCount == 1, "duplicate active identity cannot take over a character");
        var b = world.Join(3, bob, "Bob");
        check(world.IsSafe(b.X, b.Z, b.Id) && Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) > 0, "simultaneous arrivals receive separate safe positions");
        check(!world.SetIntent(999, 1, 1, 0, 0) && !world.SetIntent(2, 1, double.NaN, 0, 0) &&
              !world.SetIntent(2, 1, 100, 0, 0), "unjoined and invalid movement rejected");
        check(world.SetIntent(2, 1, 1, 1, 0) && !world.SetIntent(2, 1, -1, 0, 0) && !world.SetIntent(2, 0, 0, 0, 0), "duplicate/stale movement cannot replace accepted intent");
        double x = a.X, z = a.Z;
        world.Step();
        check(Math.Abs(Math.Sqrt(Math.Pow(a.X - x, 2) + Math.Pow(a.Z - z, 2)) - WorldAuthority.Speed / WorldAuthority.TickRate) < 1e-9,
            "diagonal movement uses server speed and fixed timestep");
        for (int i = 0; i < 6; i++) world.Step();
        x = a.X; z = a.Z;
        world.Step();
        check(a.X == x && a.Z == z, "stale held input expires without further movement");
        a.Inventory["questsim:bandage"] = 3; a.Experience = 42;
        world.Leave(2);
        check(world.Snapshot().Actors.All(p => p.Id != a.Id) && world.State.Characters.Count == 2, "disconnect removes active presence and retains character record");
        long tick = world.State.Tick;
        world.Step();
        check(world.State.Tick == tick + 1, "one remaining player keeps simulation running");
        world.Leave(3); tick = world.State.Tick;
        for (int i = 0; i < 20; i++) world.Step();
        check(world.State.Tick == tick, "last leave freezes authoritative simulation ticks");
        string dir = Path.Combine(Path.GetTempPath(), "questsim-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, world.State);
            File.WriteAllText(path + ".tmp", "interrupted partial write");
            var loaded = new WorldAuthority(WorldStorage.Load(path));
            check(loaded.ActiveCount == 0 && loaded.State.Tick == tick, "restart has no active bodies or offline clock catch-up; incomplete temp ignored");
            var reconnect = loaded.Join(5, alice, "Renamed");
            check(reconnect.Id == a.Id && reconnect.Inventory["questsim:bandage"] == 3 && reconnect.Experience == 42 && reconnect.X == x && reconnect.Z == z,
                "reconnect restores exact safe position, identity, inventory and progression");
            loaded.Leave(5);
            loaded.State.Blocks.Add(new BlockRecord("questsim:changed-world", reconnect.X, reconnect.Z, 1, 1, 1));
            WorldStorage.Save(path, loaded.State);
            var changed = new WorldAuthority(WorldStorage.Load(path));
            var relocated = changed.Join(6, alice, "Alice");
            check(relocated.Id == a.Id && changed.IsSafe(relocated.X, relocated.Z, relocated.Id) && (relocated.X != x || relocated.Z != z),
                "changed-world reconnect relocates safely without replacing character");
            changed.Step();
            check(changed.State.Tick == tick + 1, "rejoin resumes persisted simulation clock");
            string valid = File.ReadAllText(path);
            File.WriteAllText(path, "{}");
            bool incomplete = false;
            try { WorldStorage.Load(path); } catch (System.Text.Json.JsonException) { incomplete = true; }
            check(incomplete, "missing save fields rejected instead of silently replacing world");
            File.WriteAllText(path, valid.Replace("\"Version\": 4", "\"Version\": 999"));
            bool rejected = false;
            try { WorldStorage.Load(path); } catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(path).Contains("999"), "unsupported save rejected without overwriting original");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
