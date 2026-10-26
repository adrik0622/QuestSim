using QuestSim.Domain;
using static LivingChecks;

// F12: the DIRECTOR.md weekly-review story as one chained run over a living world.
public static class AcceptanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var content = ContentRegistry.Load("content/core", "mods/example-rations");
        var w = new WorldAuthority(new WorldSave { Population = 60_000, StartingKit = new() { [WorldAuthority.Pistol] = 1, [WorldAuthority.Ammo] = 12 }, Mods = content.Mods },
            synchronousPopulation: true) { Content = content };
        var a = w.Join(2, new string('a', 32), "Ana");
        var b = w.Join(3, new string('b', 32), "Ben");
        b.X = 900; b.Z = 0; while (!w.IsSafe(b.X, b.Z, b.Id)) b.X += 1;     // B is elsewhere
        Steps(w, WorldAuthority.PopulationEvery);

        // 1. A material event: Ana is captured by the Hollow Choir; the victor takes her actual gear.
        a.X = 3; a.Z = 20;
        var victor = Summon(w, false, a.X + 1, a.Z, faction: Population.Hostile);
        a.Stats[CharacterRecord.Health] = 1;
        for (int i = 0; i < 200 && !w.IsCaptive(a); i++) w.Step();
        var victorName = w.DisplayName(victor);
        check(w.IsCaptive(a) && w.State.Named.Single().Inventory[WorldAuthority.Ammo] == 12 && w.State.Opportunities.Count(o => o.Status == "open") == 2,
            $"acceptance 1: Ana is captured by {victorName}; the victor carries her gear; rescue and revenge open");

        // 2. The fact becomes a rumor; Ben, far away, learns it only by walking in and asking someone who heard it.
        Steps(w, WorldAuthority.PopulationEvery * 20);
        uint witness = (uint)Enumerable.Range(0, w.Pop!.Count).First(i => !Population.IsZombie(w.Pop.Info[i]) && Population.FactionOf(w.Pop.Info[i]) != Population.Hostile &&
            (w.Pop.Info[i] & (Population.Detailed | Population.Dead)) == 0 && Enumerable.Range(0, 64).Any(s => w.Pop.Knows((uint)i, s)));
        b.X = 3; b.Z = 26;
        var teller = SummonIndex(w, witness, b.X + 1, b.Z);
        teller.Wandering = true; teller.WanderX = teller.X; teller.WanderZ = teller.Z;
        var heard = w.Ask(3);
        check(heard.Reason == "rumors" && heard.Lines.Any(l => l.Contains("Ana")), $"acceptance 2: Ben hears it from {heard.Speaker}: \"{heard.Lines.First(l => l.Contains("Ana"))}\"");

        // 3. Ben frees Ana (single-claim reward) and, before Ana can, brings down the victor: her revenge resolves through Ben.
        b.X = a.X + 1.5; b.Z = a.Z;
        victor.Cooldown = 10_000;
        check(w.Rescue(3) == "rescued" && !w.IsCaptive(a) && w.Rescue(3) == "nobody", "acceptance 3: Ben frees Ana; the rescue cannot be claimed twice");
        victor.Health = 1;
        b.X = victor.X - 1; b.Z = victor.Z;
        b.Heading = Math.Atan2(-(victor.X - b.X), -(victor.Z - b.Z));
        Steps(w, WorldAuthority.MeleeCooldown);
        var blow = w.Attack(3, "melee");
        var revenge = w.State.Opportunities.Single(o => o.Kind == "revenge");
        check(blow.Killed && revenge.Status == "claimed" && revenge.ClaimedBy == b.Id && b.Inventory[WorldAuthority.Ammo] >= 12 + 12 + 6,
            "acceptance 4: Ben kills the victor, recovers Ana's gear and claims the bounty; Ana's own revenge is no longer available");

        // 4. Everyone disconnects; the world (facts, ledger, standing, population snapshot) survives a restart unchanged.
        w.Leave(2); w.Leave(3);
        string dir = Path.Combine(Path.GetTempPath(), "questsim-accept-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            Directory.CreateDirectory(dir);
            var bytes = w.CapturePopulationNow()!;
            w.CommitPopulation(WorldAuthority.WritePopulationFile(path, w.Pop.Tick, bytes), WorldAuthority.Hash(bytes));
            WorldStorage.Save(path, w.State);
            var r = new WorldAuthority(WorldStorage.Load(path), synchronousPopulation: true) { Content = content };
            r.LoadPopulation(path);
            content.Require(r.State.Mods);
            var a2 = r.Join(2, a.ProfileId, "Ana");
            var b2 = r.Join(3, b.ProfileId, "Ben");
            check(r.PopulationLoad == "snapshot" && r.State.Facts.Count == w.State.Facts.Count && r.State.Transfers.Count == w.State.Transfers.Count &&
                  r.Standing(0) == w.Standing(0) && (r.Pop!.Info[victor.Index] & Population.Dead) != 0 && a2.Id == a.Id && b2.Inventory.SequenceEqual(b.Inventory) &&
                  r.State.Opportunities.All(o => o.Status != "open"),
                $"acceptance 5: after a restart everything is consistent ({r.State.Facts.Count} facts, {r.State.Transfers.Count} ledgered transfers, Union standing {r.Standing(0)})");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
