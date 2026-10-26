using QuestSim.Domain;
using static LivingChecks;

public static class DefeatChecks
{
    static WorldAuthority NewWorld(bool permadeath = false) => new(new WorldSave
    {
        Population = 20_000, Permadeath = permadeath, StartingKit = new() { [WorldAuthority.Pistol] = 1, [WorldAuthority.Ammo] = 5 }
    }, synchronousPopulation: true);

    static void Down(WorldAuthority w, CharacterRecord victim, Npc attacker)
    {
        victim.Stats[CharacterRecord.Health] = 1;
        for (int i = 0; i < 200 && victim.Stat(CharacterRecord.Health) == 1; i++) w.Step();
        if (victim.Stat(CharacterRecord.Health) == 1) throw new Exception("attacker never landed a blow");
    }

    static void RoundTrip(WorldAuthority w, string what, Action<bool, string> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), "questsim-defeat-" + Guid.NewGuid().ToString("N"));
        try
        {
            WorldStorage.Save(Path.Combine(dir, "world.json"), w.State);
            var loaded = WorldStorage.Load(Path.Combine(dir, "world.json"));
            check(loaded.Transfers.Count == w.State.Transfers.Count && loaded.Facts.Count == w.State.Facts.Count, what);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    public static void Run(Action<bool, string> check)
    {
        // Zombie defeat: infection, injury, back in the starter room, and a story-time cure.
        var w = NewWorld();
        var alice = w.Join(2, new string('a', 32), "Alice");
        Steps(w, WorldAuthority.PopulationEvery);
        var zombie = Summon(w, true, alice.X + 1, alice.Z);
        Down(w, alice, zombie);
        var cure = w.State.Opportunities.Single(o => o.Kind == "cure");
        var stash = w.State.Caches.Single(k => cure.Target == $"cache:{k.Id}");
        check(alice.Conditions.ContainsKey(WorldAuthority.Infected) && alice.Conditions.ContainsKey(WorldAuthority.Injured) &&
              alice.Stat(CharacterRecord.Health) == 50 && Math.Abs(alice.X) < 6 && Math.Abs(alice.Z) < 6 &&
              stash.Inventory[WorldAuthority.Antiviral] == 1 && w.State.Transfers.Any(t => t.From == "vgm" && t.Reason == "story") &&
              w.State.Facts.Any(f => f.Kind == "defeat" && f.Subject == alice.Id),
            $"a zombie defeat infects and injures the player, who wakes in the starter room; the game master invents a cure stash ({cure.Text[..40]}...)");
        long remaining = alice.Conditions[WorldAuthority.Infected];
        w.Join(3, new string('b', 32), "Bob"); w.Leave(2);
        Steps(w, 100);
        check(alice.Conditions[WorldAuthority.Infected] == remaining, "the infection clock pauses while its player is offline");
        w.Join(2, alice.ProfileId, "Alice");
        alice.X = stash.X; alice.Z = stash.Z;
        check(w.Loot(2) == "looted" && !alice.Conditions.ContainsKey(WorldAuthority.Infected) && cure.Status == "claimed" &&
              w.Loot(2) == "nothing", "looting the stash cures the infection once and claims the cure opportunity");
        RoundTrip(w, "facts, opportunities and the transfer ledger validate after save/reload", check);

        // Turning: the infection runs out, gear drops in remains, the profile starts a new character.
        alice.X = -3; alice.Z = 0;
        alice.Conditions[WorldAuthority.Infected] = 2;
        Steps(w, 3);
        var remains = w.State.Caches.Single(k => k.Label == "Remains of Alice");
        check(alice.Status == "turned" && w.PendingKicks.Any(k => k.Peer == 2) && remains.Inventory.ContainsKey(WorldAuthority.Pistol) && alice.Inventory.Count == 0,
            "an untreated infection turns the player; their gear drops into a remains cache");
        var fresh = w.Join(4, new string('a', 32), "Alice");
        check(fresh.Id != alice.Id && fresh.Inventory[WorldAuthority.Pistol] == 1 && w.State.Characters.Contains(alice),
            "the same profile rejoins as a new character; the turned character's record is kept");

        // Choir capture: the victor takes the actual items, gains a title and buffs; rescue and revenge open.
        var c = NewWorld();
        var cara = c.Join(2, new string('c', 32), "Cara");
        var dan = c.Join(3, new string('d', 32), "Dan");
        Steps(c, WorldAuthority.PopulationEvery);
        cara.X = -3; cara.Z = -1;
        check(c.TakeItem(2, Guid.NewGuid().ToString("N"), WorldSave.InitialSupplies[0].Id).Success, "setup: Cara holds the bandage");
        dan.X = 30; dan.Z = 30; while (!c.IsSafe(dan.X, dan.Z, dan.Id)) dan.X += 1;
        cara.X = 3; cara.Z = 20;
        var cultist = Summon(c, false, cara.X + 1, cara.Z, faction: Population.Hostile);
        int ammoBefore = cara.Inventory[WorldAuthority.Ammo];
        Down(c, cara, cultist);
        var named = c.State.Named.Single(n => n.Index == cultist.Index);
        check(c.IsCaptive(cara) && cara.Inventory.Count == 0 && named.Inventory["questsim:bandage"] == 1 && named.Inventory[WorldAuthority.Ammo] == ammoBefore &&
              named.Title.Length > 0 && cultist.Health > 80 && c.State.Opportunities.Count(o => o.Status == "open") == 2,
            $"a Hollow Choir defeat captures the player; the victor takes their actual items and becomes {c.DisplayName(cultist)}");
        double x = cara.X, z = cara.Z;
        c.SetIntent(2, 1, 1, 0, 0); Steps(c, 10);
        check(cara.X == x && cara.Z == z && c.Attack(2, "melee").Reason == "captive", "a captive can neither move nor attack");
        long held = cara.Conditions[WorldAuthority.Captive];
        c.Leave(2); Steps(c, 60); c.Join(2, cara.ProfileId, "Cara");
        check(cara.Conditions[WorldAuthority.Captive] == held, "captivity pauses while the captive is offline, even with others playing");
        cultist.Wandering = true; cultist.WanderX = cultist.X; cultist.WanderZ = cultist.Z;
        dan.X = cara.X + 1.5; dan.Z = cara.Z;
        check(c.Rescue(3) == "rescued" && !c.IsCaptive(cara) && dan.Inventory[WorldAuthority.Ammo] == 5 + 6 &&
              c.State.Opportunities.Single(o => o.Kind == "rescue").ClaimedBy == dan.Id && c.Rescue(3) == "nobody",
            "a teammate within reach frees the captive and claims the rescue reward exactly once");
        cultist.Health = 1; cultist.Cooldown = 1000;
        dan.X = cultist.X - 1; dan.Z = cultist.Z;
        dan.Heading = Math.Atan2(-(cultist.X - dan.X), -(cultist.Z - dan.Z));
        var blow = c.Attack(3, "melee");
        check(blow.Killed && dan.Inventory["questsim:bandage"] == 1 && dan.Inventory[WorldAuthority.Ammo] == 11 + ammoBefore + 12 &&
              c.State.Opportunities.Single(o => o.Kind == "revenge").Status == "claimed" && named.Inventory.Count == 0,
            "killing the named victor recovers the stolen gear and pays the revenge bounty once");
        RoundTrip(c, "the bandage's journey (crate, captive, victor, avenger) passes conservation after reload", check);

        // Permadeath server setting.
        var p = NewWorld(permadeath: true);
        var eve = p.Join(2, new string('e', 32), "Eve");
        Steps(p, WorldAuthority.PopulationEvery);
        Down(p, eve, Summon(p, true, eve.X + 1, eve.Z));
        check(eve.Status == "dead" && p.State.Caches.Any(k => k.Label == "Remains of Eve") && p.PendingKicks.Any(),
            "with permadeath on, a defeat is final and the character's gear stays where they fell");
    }
}
