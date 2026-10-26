using QuestSim.Domain;
using static LivingChecks;

public static class RumorChecks
{
    public static void Run(Action<bool, string> check)
    {
        var w = new WorldAuthority(new WorldSave { Population = 40_000, StartingKit = new() { [WorldAuthority.Pistol] = 1, [WorldAuthority.Ammo] = 5 } }, synchronousPopulation: true) { PromotionClearance = 0 };
        var alice = w.Join(2, new string('a', 32), "Alice");
        Steps(w, WorldAuthority.PopulationEvery);
        alice.X = 3; alice.Z = 20;
        var cultist = Summon(w, false, alice.X + 1, alice.Z, faction: Population.Hostile);
        alice.Stats[CharacterRecord.Health] = 1;
        for (int i = 0; i < 200 && !w.IsCaptive(alice); i++) w.Step();
        var capture = w.State.Facts.Last(f => f.Kind == "capture");
        Steps(w, WorldAuthority.PopulationEvery * 2);
        long knowers = Enumerable.Range(0, w.Pop!.Count).Count(i => Enumerable.Range(0, 64).Any(s => w.Pop.Knows((uint)i, s)));
        check(w.IsCaptive(alice) && knowers > 0, $"the capture becomes a rumor: {knowers} witnesses near the plaza know it");
        Steps(w, WorldAuthority.PopulationEvery * 30);
        long later = Enumerable.Range(0, w.Pop.Count).Count(i => Enumerable.Range(0, 64).Any(s => w.Pop.Knows((uint)i, s)));
        check(later > knowers, $"word of mouth spreads it over 30 population ticks ({knowers} -> {later} people)");

        // Ask a friendly NPC that has heard it; a hostile one refuses.
        alice.Conditions.Remove(WorldAuthority.Captive);
        var bob = w.Join(3, new string('b', 32), "Bob");
        bob.X = 3; bob.Z = 23;
        uint listener = (uint)Enumerable.Range(0, w.Pop.Count).First(i => !Population.IsZombie(w.Pop.Info[i]) && Population.FactionOf(w.Pop.Info[i]) == 0 &&
            (w.Pop.Info[i] & (Population.Detailed | Population.Dead)) == 0 && Enumerable.Range(0, 64).Any(s => w.Pop.Knows((uint)i, s)));
        var witness = SummonIndex(w, listener, bob.X + 1, bob.Z);
        witness.Wandering = true; witness.WanderX = witness.X; witness.WanderZ = witness.Z;
        var answer = w.Ask(3);
        check(answer.Reason == "rumors" && answer.Lines.Any(l => l.Contains("Alice")),
            $"an NPC who heard about Alice retells it from its own beliefs ({answer.Speaker}: \"{answer.Lines.FirstOrDefault()}\")");
        bob.X = 300; bob.Z = 0; while (!w.IsSafe(bob.X, bob.Z, bob.Id)) bob.X += 1;
        Steps(w, WorldAuthority.PopulationEvery);
        var stranger = w.Npcs.Values.FirstOrDefault(n => !n.Zombie && !w.IsHostile(n) && Enumerable.Range(0, 64).All(s => !w.Pop.Knows(n.Index, s)));
        if (stranger is not null)
        {
            bob.X = stranger.X + 1; bob.Z = stranger.Z;
            var blank = w.Ask(3);
            check(blank.Reason is "nothing" or "rumors", $"NPCs only know what reached them ({blank.Speaker}: \"{blank.Lines.FirstOrDefault()}\")");
        }
        bob.X = 3; bob.Z = 23;

        // Standing: killing Union members turns the Union hostile; its people then attack on sight.
        int before = w.Standing(0);
        for (int k = 0; k < 4; k++)
        {
            var victim = Summon(w, false, bob.X + 1, bob.Z, faction: 0);
            victim.Health = 1;
            bob.Heading = Math.Atan2(-(victim.X - bob.X), -(victim.Z - bob.Z));
            var hit = w.Attack(3, "melee");
            if (!hit.Killed) throw new Exception("setup kill failed: " + hit.Reason);
            Steps(w, WorldAuthority.MeleeCooldown);
        }
        var angry = Summon(w, false, bob.X + 4, bob.Z, faction: 0);
        int health = bob.Stat(CharacterRecord.Health);
        Steps(w, 80);
        check(before == 20 && w.Standing(0) <= WorldAuthority.HostileBelow && w.IsHostile(angry) && bob.Stat(CharacterRecord.Health) < health &&
              w.State.Facts.Any(f => f.Kind == "hostile" && f.Subject == "faction:0") && w.Ask(3).Reason is "refused" or "nobody",
            $"killing Union members drops their standing ({before} -> {w.Standing(0)}); the Union turns hostile and attacks");
        string dir = Path.Combine(Path.GetTempPath(), "questsim-pop-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            Directory.CreateDirectory(dir);
            var bytes = w.CapturePopulationNow()!;
            string name = WorldAuthority.WritePopulationFile(path, w.Pop.Tick, bytes);
            w.CommitPopulation(name, WorldAuthority.Hash(bytes));
            WorldStorage.Save(path, w.State);
            var reloaded = new WorldAuthority(WorldStorage.Load(path), synchronousPopulation: true);
            reloaded.LoadPopulation(path);
            long knowing = Enumerable.Range(0, w.Pop.Count).Count(i => Enumerable.Range(0, 64).Any(s => w.Pop.Knows((uint)i, s)));
            check(reloaded.PopulationLoad == "snapshot" && reloaded.Pop!.Tick == w.Pop.Tick && reloaded.Pop.PosX.SequenceEqual(w.Pop.PosX) &&
                  reloaded.Pop.MaskLo.SequenceEqual(w.Pop.MaskLo) && reloaded.Pop.Info.SequenceEqual(w.Pop.Info.Select(i => i & ~Population.Detailed)),
                $"a population snapshot ({bytes.Length / 1024:N0} KiB) restores positions, rumor knowledge ({knowing} people) and the tick exactly");
            File.WriteAllBytes(Path.Combine(dir, name), bytes[..(bytes.Length / 2)]);
            var fallback = new WorldAuthority(WorldStorage.Load(path), synchronousPopulation: true);
            fallback.LoadPopulation(path);
            check(fallback.PopulationLoad.StartsWith("seed-fallback") && fallback.State.AgentDeltas.Where(d => d.Dead).All(d => (fallback.Pop!.Info[d.Index] & Population.Dead) != 0),
                "a damaged population file is detected by hash and falls back to seed + deltas; deaths still hold");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        var inspected = w.Inspect(3);
        check(inspected.Length == 4 && inspected[0].Contains("npc:") && inspected[1].StartsWith("Health"),
            $"I inspects the nearest NPC: \"{inspected[0]}\" / \"{inspected[3]}\"");
        var factions = w.Snapshot(3).Factions!;
        check(factions.Length == 5 && factions.Where(f => f.Id < 3).Sum(f => f.Members) > 20_000 && factions.Single(f => f.Id == 2).Standing == -100,
            "snapshots report faction standing and living member counts from the population");
    }
}
