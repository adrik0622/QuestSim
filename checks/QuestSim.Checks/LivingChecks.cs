using QuestSim.Domain;

public static class LivingChecks
{
    internal static void Steps(WorldAuthority w, int n) { for (int i = 0; i < n; i++) w.Step(); }
    static double Face(double fromX, double fromZ, double toX, double toZ) => Math.Atan2(-(toX - fromX), -(toZ - fromZ));

    // Moves a coarse agent of the requested kind next to a point and lets the next tier sync promote it.
    internal static Npc Summon(WorldAuthority w, bool zombie, double x, double z, int faction = -1)
    {
        var pop = w.Pop!;
        uint i = (uint)Enumerable.Range(0, pop.Count).First(k => Population.IsZombie(pop.Info[k]) == zombie &&
            (faction < 0 || Population.FactionOf(pop.Info[k]) == faction) && (pop.Info[k] & (Population.Detailed | Population.Dead)) == 0);
        pop.PosX[i] = pop.TgtX[i] = Population.ToMm(x); pop.PosZ[i] = pop.TgtZ[i] = Population.ToMm(z);
        pop.Step();   // rebuild the grid with the moved agent (it walks at most one coarse step)
        pop.PosX[i] = pop.TgtX[i] = Population.ToMm(x); pop.PosZ[i] = pop.TgtZ[i] = Population.ToMm(z);
        long next = (w.State.Tick / WorldAuthority.PopulationEvery + 1) * WorldAuthority.PopulationEvery;
        while (w.State.Tick < next) w.Step();
        return w.Npcs.TryGetValue(i, out var npc) ? npc : throw new Exception("summoned agent was not promoted");
    }

    public static void Run(Action<bool, string> check)
    {
        var save = new WorldSave { Population = 20_000, StartingKit = new() { [WorldAuthority.Pistol] = 1, [WorldAuthority.Ammo] = 3 } };
        var w = new WorldAuthority(save, synchronousPopulation: true);
        var twin = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true);
        check(w.Pop!.PosX.SequenceEqual(twin.Pop!.PosX) && w.Pop.Info.SequenceEqual(twin.Pop.Info) &&
              w.Pop.Info.Count(Population.IsZombie) is > 2_000 and < 8_000, "the seeded population regenerates identically (about a quarter zombies)");
        var alice = w.Join(2, new string('a', 32), "Alice");
        check(alice.Inventory[WorldAuthority.Pistol] == 1 && alice.Inventory[WorldAuthority.Ammo] == 3 && alice.Stat(CharacterRecord.Health) == 100,
            "new characters get the starting kit and default stats");
        Steps(w, WorldAuthority.PopulationEvery);
        check(w.Npcs.Count > 0 && w.Npcs.Values.All(n => Math.Sqrt((n.X - alice.X) * (n.X - alice.X) + (n.Z - alice.Z) * (n.Z - alice.Z)) <= WorldAuthority.PromoteRadius + 13) &&
              w.Npcs.Keys.All(i => (w.Pop.Info[i] & Population.Detailed) != 0),
            $"agents near a player are promoted to 20 Hz detail ({w.Npcs.Count} near the plaza)");
        Steps(w, WorldAuthority.PopulationEvery);
        check(w.Npcs.Values.All(n => Math.Abs(Population.ToMetres(w.Pop.PosX[n.Index]) - n.X) < 3 && Math.Abs(Population.ToMetres(w.Pop.PosZ[n.Index]) - n.Z) < 3),
            "the coarse tick never moves detailed agents; their positions sync from the authority");

        var bob = w.Join(3, new string('b', 32), "Bob");
        bob.X = 2000; bob.Z = 0;
        double bx = bob.X, bz = bob.Z;
        while (!w.IsSafe(bx, bz, bob.Id)) bx += 1;
        bob.X = bx; bob.Z = bz;
        Steps(w, WorldAuthority.PopulationEvery);
        var forAlice = w.Snapshot(2).Actors.Where(a => a.Id.StartsWith("npc:")).ToArray();
        var forBob = w.Snapshot(3).Actors.Where(a => a.Id.StartsWith("npc:")).ToArray();
        check(forAlice.All(a => Math.Abs(a.X - alice.X) <= 121) && forBob.All(a => Math.Abs(a.X - bob.X) <= 121) &&
              !forAlice.Select(a => a.Id).Intersect(forBob.Select(a => a.Id)).Any() && w.Snapshot(2).Actors.Count(a => a.Kind == 0) == 2,
            "each player's snapshot carries only nearby NPCs, plus every player");

        var nearBob = w.Npcs.Values.Where(n => Math.Abs(n.X - bob.X) < 200).Select(n => n.Index).ToArray();
        w.Leave(3);
        Steps(w, WorldAuthority.PopulationEvery);
        check(nearBob.Length > 0 && nearBob.All(i => !w.Npcs.ContainsKey(i) && (w.Pop.Info[i] & Population.Detailed) == 0),
            $"agents demote back to the coarse tick when no player is near ({nearBob.Length} demoted)");

        var zombie = Summon(w, true, alice.X + 4, alice.Z);
        Steps(w, 60);
        check(alice.Stat(CharacterRecord.Health) < 100, $"a nearby zombie chases and damages the player (health {alice.Stat(CharacterRecord.Health)})");
        alice.Heading = Face(alice.X, alice.Z, zombie.X, zombie.Z);
        var swings = new List<AttackResult>();
        for (int k = 0; k < 40 && !zombie.Dead; k++)
        {
            alice.Heading = Face(alice.X, alice.Z, zombie.X, zombie.Z);
            swings.Add(w.Attack(2, "melee"));
            Steps(w, WorldAuthority.MeleeCooldown);
        }
        check(zombie.Dead && swings.Any(s => s.Reason == "hit") && swings[^1].Killed &&
              w.State.AgentDeltas.Single(d => d.Index == zombie.Index).Dead, "melee hits within reach, kills the zombie, and records the death as a delta");
        check(w.Attack(2, "melee").Reason is "cooldown" or "miss", "melee respects its cooldown");
        Steps(w, WorldAuthority.PopulationEvery);
        check(!w.Npcs.ContainsKey(zombie.Index) && (w.Pop.Info[zombie.Index] & Population.Dead) != 0 &&
              w.Snapshot(2).Actors.All(a => a.Id != $"npc:{zombie.Index}"), "the dead agent leaves detail and stays dead in the coarse population");

        var human = Summon(w, false, alice.X + 8, alice.Z, faction: 0);
        human.Wandering = true; human.WanderX = human.X; human.WanderZ = human.Z;
        alice.Heading = Face(alice.X, alice.Z, human.X, human.Z);
        int healthBefore = human.Health;
        var shot = w.Attack(2, "shoot");
        check(shot.Fired && (shot.Reason is "hit" or "killed" or "blocked") && alice.Inventory[WorldAuthority.Ammo] == 2,
            $"a pistol shot consumes exactly one round ({shot.Reason}, {healthBefore}->{human.Health})");
        Steps(w, WorldAuthority.ShotCooldown);
        w.Attack(2, "shoot"); Steps(w, WorldAuthority.ShotCooldown);
        w.Attack(2, "shoot"); Steps(w, WorldAuthority.ShotCooldown);
        var empty = w.Attack(2, "shoot");
        check(empty.Reason == "no_ammo" && !empty.Fired && !alice.Inventory.ContainsKey(WorldAuthority.Ammo), "an empty pistol cannot fire");

        // Inside the starter room, a target outside the east wall is shielded.
        alice.Inventory[WorldAuthority.Ammo] = 1;
        alice.X = 4; alice.Z = 0;
        var outside = Summon(w, false, 9, 0, faction: 1);
        outside.Wandering = true; outside.WanderX = outside.X; outside.WanderZ = outside.Z;
        alice.Heading = Face(alice.X, alice.Z, outside.X, outside.Z);
        var walled = w.Attack(2, "shoot");
        check(walled.Reason == "blocked" && !alice.Inventory.ContainsKey(WorldAuthority.Ammo), "walls stop bullets; the round is still spent");

        bool downed = false;
        alice.X = -3; alice.Z = 0;
        alice.Stats[CharacterRecord.Health] = 5;
        var biter = Summon(w, true, alice.X + 1, alice.Z);
        w.Downed = (p, by) => { downed = p == alice && by == biter; p.Stats[CharacterRecord.Health] = 100; };
        Steps(w, 40);
        check(downed, "a player reduced to zero health is downed by the attacker (defeat hook)");

        string dir = Path.Combine(Path.GetTempPath(), "questsim-living-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, w.State);
            var reloaded = new WorldAuthority(WorldStorage.Load(path), synchronousPopulation: true);
            check((reloaded.Pop!.Info[zombie.Index] & Population.Dead) != 0 && reloaded.State.AgentDeltas.Count == w.State.AgentDeltas.Count,
                "deaths and damage persist as deltas over the regenerated population");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        w.Leave(2);
        uint tick = w.Pop.Tick;
        Steps(w, 100);
        check(w.Pop.Tick == tick, "the population pauses with the world when nobody is connected");
    }
}
