using System.Text.Json;
using QuestSim.Domain;
using static LivingChecks;

public static class EconomyChecks
{
    static void Hours(Population pop, List<SettlementState> states, ref long hour, int hours, EconomyTally? tally = null, List<EconomyEvent>? events = null)
    {
        for (int h = 0; h < hours; h++)
        {
            for (int t = 0; t < Economy.TicksPerHour; t++) pop.Step();
            Economy.Hour(pop, states, ++hour, events ?? new(), tally);
        }
    }

    static string Json(List<SettlementState> states) => JsonSerializer.Serialize(states);
    static long Total(List<SettlementState> states, int good) => states.Sum(s => s.Stock[good]);

    public static void Run(Action<bool, string> check)
    {
        var city = new CityGen(WorldSave.DefaultSeed);
        var pop = new Population(city, 40_000);
        var states = Economy.Create(pop, city);
        var profiles = states.GroupBy(s => s.Profile).ToDictionary(g => g.Key, g => g.Count());
        check(states.Count == pop.Settlements.Count && profiles.Count == 4 &&
              pop.Settlements.Where(s => city.BiomeAt(s.X, s.Z) != Biome.Urban).All(s => states[s.Index].Profile == Economy.Farm) &&
              states.All(s => pop.Residents[s.Index] == 0 || Economy.Days10(s, pop.Residents[s.Index], Good.Food) is >= 29 and <= 61),
            $"every settlement gets a profile ({string.Join(", ", profiles.OrderBy(p => p.Key).Select(p => $"{Economy.ProfileNames[p.Key]} {p.Value}"))}; countryside farms) and 3-6 days of stock");

        // Same seed, same history; goods only appear or vanish through production, consumption and spoilage.
        var before = Enumerable.Range(0, Economy.Goods).Select(g => Total(states, g)).ToArray();
        var tally = new EconomyTally();
        long hour = 0;
        Hours(pop, states, ref hour, 6, tally);
        var twin = new Population(city, 40_000);
        var twinStates = Economy.Create(twin, city);
        long twinHour = 0;
        Hours(twin, twinStates, ref twinHour, 6);
        check(Json(states) == Json(twinStates), "the same seed produces the same economy, hour by hour");
        check(Enumerable.Range(0, Economy.Goods).All(g => Total(states, g) == before[g] + tally.Produced[g] - tally.Consumed[g] - tally.Spoiled[g] - tally.Destroyed[g]),
            "goods are conserved: stock changes only by production, consumption, spoilage and destruction (logistics moves, never creates)");

        // Saving the economy and the population mid-run and continuing equals running straight through.
        var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true)) pop.Write(w);
        var restored = new Population(city, 40_000);
        bytes.Position = 0;
        using (var r = new BinaryReader(bytes)) restored.Read(r);
        var restoredStates = JsonSerializer.Deserialize<List<SettlementState>>(Json(states))!;
        restored.Attractors = Economy.Attractors(restored, restoredStates);   // as the server does after loading
        long restoredHour = hour;
        Hours(pop, states, ref hour, 3);
        Hours(restored, restoredStates, ref restoredHour, 3);
        check(Json(states) == Json(restoredStates) && pop.Residents.SequenceEqual(restored.Residents), "save mid-run and reload continues exactly like running straight");

        // Faction logistics: a well-stocked settlement's surplus reaches a hungry one of the same faction, never a rival.
        int giver = Enumerable.Range(0, states.Count).First(s => pop.Residents[s] > 0 && pop.Neighbours(s).Any(b => Economy.Allied(pop, s, b) && pop.Residents[b] > 0));
        int taker = pop.Neighbours(giver).First(b => Economy.Allied(pop, giver, b) && pop.Residents[b] > 0);
        states[giver].Stock[0] = pop.Residents[giver] * 1000L * 25; states[taker].Stock[0] = 0;
        Economy.Hour(pop, states, ++hour, new());
        check(states[taker].Stock[0] > 0, $"faction logistics move surplus to need (settlement {giver} fed {taker})");
        var choirSettlements = Enumerable.Range(0, states.Count).Where(s => pop.SettlementFaction[s] == Population.Hostile).ToArray();
        foreach (int c in choirSettlements) { states[c].Profile = Economy.Mixed; states[c].Stock[0] = 0; states[c].Damage = 100; }   // no output of their own
        foreach (int b in choirSettlements.SelectMany(pop.Neighbours).Where(b => pop.SettlementFaction[b] != Population.Hostile)) states[b].Stock[0] = pop.Residents[b] * 1000L * 25;
        Economy.Hour(pop, states, ++hour, new());
        check(choirSettlements.All(c => states[c].Stock[0] == 0), $"goods never cross between factions on their own (the starving Choir's {choirSettlements.Length} settlements get nothing from rich neighbours)");
        foreach (int c in choirSettlements) states[c].Damage = 0;

        // A settlement that runs out reports a shortage; after a hungry day in unrest people leave for the best-fed allied
        // neighbour; supplies arriving end the shortage.
        int hungry = Enumerable.Range(0, states.Count).First(s => pop.Residents[s] > 50 && pop.SettlementFaction[s] != Population.Hostile &&
            pop.Neighbours(s).Any(b => pop.SettlementFaction[b] != Population.Hostile && pop.Residents[b] > 0));
        foreach (int b in pop.Neighbours(hungry)) states[b].Stock[0] = pop.Residents[b] * 1000L * 20;
        var s0 = states[hungry];
        s0.Profile = Economy.Mixed; s0.Stock[0] = 0; s0.Order = 30; s0.HoursHungry = 23; s0.Reported = 0; s0.Damage = 100;   // makes nothing: really hungry
        int residentsBefore = pop.Residents[hungry];
        var events = new List<EconomyEvent>();
        var moved = new EconomyTally();
        Economy.Hour(pop, states, ++hour, events, moved);
        var refugees = Enumerable.Range(0, pop.Count).Where(i => (pop.Info[i] & Population.Travelling) != 0 && Population.FactionOf(pop.Info[i]) < Population.HumanFactions &&
            pop.Neighbours(hungry).Contains((int)(pop.Info[i] & 0xFFFFFF))).ToArray();
        check((s0.Reported & 1) != 0 && s0.Order < 30 && s0.HoursHungry == 24,
            "running out of food marks the settlement short (on the map), lowers order and counts hungry hours");
        check(moved.Refugees > 0 && pop.Residents[hungry] == residentsBefore - moved.Refugees && refugees.Length >= moved.Refugees,
            $"after a hungry day in unrest, people leave for a better-fed neighbour and join it ({moved.Refugees} of {residentsBefore} this hour)");
        s0.Damage = 0;
        var factionShort = new int[Population.HumanFactions];
        foreach (var x in states.Where(x => pop.SettlementFaction[x.Index] == pop.SettlementFaction[hungry])) x.Stock[0] = pop.Residents[x.Index] * 100L;
        events.Clear();
        Economy.Hour(pop, states, ++hour, events, null, null, factionShort);
        check(events.Count(e => e.Kind == "shortage" && e.Good == Good.Food && pop.SettlementFaction[e.Settlement] == pop.SettlementFaction[hungry]) == 1 &&
              (factionShort[pop.SettlementFaction[hungry]] & 1) != 0,
            "a faction running low on food is one piece of news, not one per settlement");
        foreach (var x in states.Where(x => pop.SettlementFaction[x.Index] == pop.SettlementFaction[hungry])) x.Stock[0] = pop.Residents[x.Index] * 1000L * 10;
        events.Clear();
        Economy.Hour(pop, states, ++hour, events, null, null, factionShort);
        check(events.Any(e => e.Kind == "recovered" && e.Good == Good.Food && pop.SettlementFaction[e.Settlement] == pop.SettlementFaction[hungry]) && (s0.Reported & 1) == 0,
            "supplies arriving end the faction's shortage (one recovery fact)");
        check(Economy.Price(s0, pop.Residents[hungry], Good.Food) < Economy.Price(new SettlementState { Stock = [1000, 0, 0, 0] }, pop.Residents[hungry], Good.Food),
            "prices rise as stock runs low");

        // Births reuse the slots of the dead nobody refers to, where people have plenty, order and room.
        var growing = new Population(city, 40_000);
        var homes = Economy.Create(growing, city);
        int nursery = homes.First(h => growing.Residents[h.Index] > 100 && growing.SettlementFaction[h.Index] != Population.Hostile).Index;
        growing.KillResidents(nursery, 40, 1);
        var deadSlots = Enumerable.Range(0, growing.Count).Select(i => (uint)i)
            .Where(i => (growing.Info[i] & Population.Dead) != 0 && !Population.IsDormant(growing.Info[i])).ToArray();
        uint remembered = deadSlots[0];   // say a fact mentions this one
        foreach (var h in homes) { h.Stock[0] = growing.Residents[h.Index] * 1000L * 8; h.Order = 90; }
        var births = new EconomyTally();
        long day = 0;
        for (int h = 0; h < 24; h++) Economy.Hour(growing, homes, ++day, new(), births, i => i != remembered);
        check(births.Born > 0 && (growing.Info[remembered] & Population.Dead) != 0 &&
              deadSlots.Where(i => i != remembered).Any(i => (growing.Info[i] & Population.Dead) == 0 && !Population.IsZombie(growing.Info[i])),
            $"people are born where there is plenty, reusing only dead slots nothing refers to ({births.Born} born in a day)");
        foreach (var h in homes) h.Stock[0] = growing.Residents[h.Index] * 1000L;   // one day of food: no births
        var lean = new EconomyTally();
        Economy.Hour(growing, homes, ++day, new(), lean);
        check(lean.Born == 0, "no births when food is short");

        // A great horde: word comes first, the hordes are drawn to the target, and when it arrives the settlement is overrun
        // (stores destroyed, output crippled); it rebuilds faster with materials.
        var target = homes.First(h => h.Profile == Economy.Clinic && !growing.Settlements[h.Index].Spawn);
        target.ThreatHour = day + 2;
        target.Stock[(int)Good.Materials] = growing.Residents[target.Index] * 50L * 20;
        Economy.Hour(growing, homes, ++day, new());
        check(growing.Attractors is [var only] && only == target.Index, "while a great horde is on its way, the hordes head for its target");
        long medicineBefore = target.Stock[(int)Good.Medicine];
        var hit = new EconomyTally();
        var news = new List<EconomyEvent>();
        Economy.Hour(growing, homes, ++day, news, hit);
        check(news.Contains(new EconomyEvent("overrun", target.Index, Good.Ammo)) && target.Damage == Economy.OverrunDamage && hit.Destroyed[(int)Good.Medicine] > 0 &&
              target.Stock[(int)Good.Medicine] < medicineBefore && target.ThreatHour == 0,
            $"the horde arrives and overruns the clinic: {hit.Destroyed[(int)Good.Medicine] / 1000} medicine destroyed, output down {target.Damage}%");
        for (int h = 0; h < 5; h++) Economy.Hour(growing, homes, ++day, new());
        check(target.Damage == Economy.OverrunDamage - 10, "with materials to hand, rebuilding restores 2% of output an hour");

        // In the server: the economy runs once per game hour; a shortage becomes a fact, a rumor and something NPCs tell you.
        var world = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true) { PromotionClearance = 0 };
        var alice = world.Join(2, new string('e', 32), "Eve");
        Steps(world, WorldAuthority.PopulationEvery);
        int plaza = world.Pop!.Settlements.First(s => s.Spawn).Index;
        var home = world.State.Settlements[plaza];
        home.Profile = Economy.Mixed;
        foreach (var s in world.State.Settlements.Where(s => world.Pop.SettlementFaction[s.Index] == world.Pop.SettlementFaction[plaza]))
            s.Stock[0] = s.Index == plaza ? 0 : world.Pop.Residents[s.Index] * 1000L;   // the whole faction low, the plaza worst
        while (!world.State.Facts.Any(f => f.Kind == "shortage")) Steps(world, WorldAuthority.PopulationEvery);
        var listener = Summon(world, false, alice.X + 1.5, alice.Z, faction: world.Pop.SettlementFaction[plaza]);
        Steps(world, WorldAuthority.PopulationEvery * 3);
        listener.X = alice.X + 1.5; listener.Z = alice.Z;
        var told = world.Ask(2);
        check(world.State.Facts.Any(f => f.Kind == "shortage" && f.Subject == $"settlement:{plaza}" && f.Object == "food") &&
              world.Snapshot(2).Settlements!.Single(s => s.Index == plaza).Short != 0 && told.Lines.Any(l => l.Contains("going hungry")),
            $"a shortage at the plaza becomes a fact, shows on the map data and NPCs talk about it (\"{told.Lines.FirstOrDefault()}\")");

        string dir = Path.Combine(Path.GetTempPath(), "questsim-economy-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, world.State);
            var saved = WorldStorage.Load(path);
            check(Json(saved.Settlements) == Json(world.State.Settlements) && saved.EconomyTicks == world.State.EconomyTicks, "the economy is saved with the world (save v5)");
            var v4 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            v4["Version"] = 4; v4.Remove("Settlements"); v4.Remove("EconomyTicks");
            string original = v4.ToJsonString(); File.WriteAllText(path, original);
            var migrated = WorldStorage.Load(path);
            string backup = Directory.GetFiles(dir, "world.json.v4-*.bak").Single();
            check(File.ReadAllText(backup) == original && migrated.Version == 5 && migrated.Settlements.Count == 0,
                "v4 saves migrate to v5 with an exact backup; their economy is generated on the first step");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
