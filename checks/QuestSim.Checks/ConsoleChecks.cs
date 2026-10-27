using QuestSim.Domain;
using System.Text.Json;

public static class ConsoleChecks
{
    static string Fingerprint(WorldAuthority w) =>
        JsonSerializer.Serialize(w.State) + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(w.CapturePopulationNow() ?? []));

    public static void Run(Action<bool, string> check)
    {
        var w = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true);
        var alice = w.Join(2, new string('a', 32), "Alice");
        var bob = w.Join(3, new string('b', 32), "Bob");

        string admins = Path.Combine(Path.GetTempPath(), $"questsim-admins-{Guid.NewGuid():N}");
        check(WorldAuthority.ReadAdmins(admins).Count == 0, "no admins file means nobody is admin");
        check(w.RunCommand(2, "speed 4", WorldAuthority.ReadAdmins(admins))[0].Contains("needs admin") && w.WorldSpeed == 1 &&
              w.RunCommand(2, "alwayson on", []).Single().Contains("needs admin") && !w.AlwaysOn,
            "without admin rights speed and alwayson are refused and change nothing");
        File.WriteAllText(admins, $"# hosts list character IDs\n\n{alice.Id}  # alice\n");
        try
        {
            var listed = WorldAuthority.ReadAdmins(admins);
            check(listed.SetEquals([alice.Id]), "the admins file lists character IDs, ignoring blank lines and # comments");
            check(w.RunCommand(2, "speed 8", listed).Single() == "Speed set to 8x." && w.WorldSpeed == 8, "an admin sets the world speed");
            check(w.RunCommand(3, "speed 1", listed).Single().Contains("needs admin") && w.WorldSpeed == 8, "another player still cannot");
            check(w.RunCommand(2, "speed 3", listed).Single().Contains("must be") && w.WorldSpeed == 8 &&
                  w.RunCommand(2, "speed", listed).Single() == "Speed is 8x.", "only 1, 2, 4, 8 and 16 are accepted; speed alone reports it");
            check(w.RunCommand(3, "help", listed).All(l => !l.StartsWith("speed")) && w.RunCommand(2, "help", listed).Any(l => l.StartsWith("speed")),
                "help lists admin commands only to admins");
            check(w.RunCommand(3, "who", listed).SequenceEqual([$"Alice {alice.Id} (admin)", $"Bob {bob.Id}"]) &&
                  w.RunCommand(3, "time", listed).Single().StartsWith("Day 1, 08:00 | speed 8x | always-on off") &&
                  w.RunCommand(3, "dance", listed).Single().StartsWith("Unknown command") &&
                  w.RunCommand(3, new string('x', 201), listed).Single() == "Command too long.", "who, time, unknown and oversized commands answer plainly");
        }
        finally { File.Delete(admins); }

        // N steps in one frame equal N separate steps (synchronous population and no time budget: nothing holds a frame back).
        var fast = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true);
        var slow = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true);
        var fastAlice = fast.Join(2, new string('a', 32), "Alice");
        slow.Join(2, new string('a', 32), "Alice").Id = fastAlice.Id;   // character IDs are random GUIDs
        fast.RunCommand(2, "speed 16", [fastAlice.Id]);
        int frames = 0, steps = 0;
        while (steps < 160) { steps += fast.StepFrame(budgetMs: double.PositiveInfinity); frames++; }
        LivingChecks.Steps(slow, 160);
        check(frames == 10 && fast.State.Tick == 160 && Fingerprint(fast) == Fingerprint(slow),
            "at 16x ten frames run 160 steps and reach exactly the world 160 single steps reach");

        // Time pauses with nobody connected unless always-on is set.
        var lonely = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true);
        LivingChecks.Steps(lonely, 40);
        long paused = lonely.State.Tick;
        lonely.AlwaysOn = true;
        LivingChecks.Steps(lonely, 40);
        check(paused == 0 && lonely.State.Tick == 40 && lonely.Pop!.Tick > 0 && lonely.Npcs.Count == 0,
            "an empty world stays paused by default; always-on keeps time and the population running with nobody there");
    }
}
