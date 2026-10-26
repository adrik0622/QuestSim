using System.Text.Json;
using QuestSim.Domain;
using static LivingChecks;

public static class PoliticsChecks
{
    static long Total(List<SettlementState> s, int good) => s.Sum(x => x.Stock[good]);

    public static void Run(Action<bool, string> check)
    {
        var city = new CityGen(WorldSave.DefaultSeed);
        var pop = new Population(city, 40_000);
        var states = Economy.Create(pop, city);
        var factions = Politics.Create();
        var actions = new List<FactionAction>();
        check(factions[0].Relations[1] == 20 && factions[0].Relations[2] == -60 && factions[2].Relations[1] == -60 && factions[0].Relations[3] == -100 && factions[1].Relations[1] == 100,
            "relations start friendly between the Union and the Traders, hostile with the Choir, and fixed against the hordes");

        // A Traders shortage of food next to a well-fed Union: the Traders barter for it at faction scale.
        int union = 0, traders = 1;
        var border = Enumerable.Range(0, states.Count).Where(s => pop.SettlementFaction[s] == traders && pop.Residents[s] > 0)
            .SelectMany(s => pop.Neighbours(s).Where(b => pop.SettlementFaction[b] == union && pop.Residents[b] > 0).Select(b => (T: s, U: b))).First();
        foreach (var s in states)
        {
            int f = pop.SettlementFaction[s.Index];
            s.Stock[0] = f == traders ? pop.Residents[s.Index] * 1000L / 2 : pop.Residents[s.Index] * 1000L * 8;   // Traders: half a day; Union: 8 days
            s.Stock[3] = f == traders ? pop.Residents[s.Index] * 50L * 20 : pop.Residents[s.Index] * 50L * 2;      // Traders rich in materials
        }
        long hour = Politics.DecisionHour(traders) - 1;
        var events = new List<FactionEvent>();
        var before = Enumerable.Range(0, Economy.Goods).Select(g => Total(states, g)).ToArray();
        Politics.Hour(pop, states, factions, actions, ++hour, events);
        var deal = actions.SingleOrDefault(a => a.From == traders);
        check(deal is { Kind: "trade", To: 0, ReturnGood: 0 } && factions[traders].LastMove.StartsWith("trade with Survivors' Union"),
            $"a faction short of food barters for it with a friendly faction that can spare it ({factions[traders].LastMove})");
        while (actions.Count > 0 && hour < deal!.DueHour) Politics.Hour(pop, states, factions, actions, ++hour, events);
        var trade = events.Single(e => e.Kind == "trade");
        check(trade.Amount > 0 && Enumerable.Range(0, Economy.Goods).All(g => Total(states, g) == before[g]) && factions[traders].Relations[union] > 20,
            $"the trade arrives after {Politics.TradeDelay} hours, moves goods without creating any, and warms relations ({trade.Amount / 1000} food)");

        // Raids: the Choir hits an adjacent settlement for what it lacks; loot moves, people die on both sides, relations fall.
        var raid = new FactionAction
        {
            Id = 7, Kind = "raid", From = Politics.Choir, To = traders, FromSettlement = border.U, ToSettlement = border.T, Good = 3, DueHour = hour + 1
        };
        actions.Add(raid);
        long targetMaterials = states[border.T].Stock[3];
        int attackers = pop.Residents[border.U], defenders = pop.Residents[border.T];
        int relationBefore = factions[traders].Relations[Politics.Choir];
        events.Clear();
        before = Enumerable.Range(0, Economy.Goods).Select(g => Total(states, g)).ToArray();
        Politics.Hour(pop, states, factions, actions, ++hour, events);
        var hit = events.Single(e => e.Kind == "raid");
        check(pop.Residents[border.U] < attackers && pop.Residents[border.T] < defenders && factions[traders].Relations[Politics.Choir] < relationBefore &&
              Enumerable.Range(0, Economy.Goods).All(g => Total(states, g) == before[g]) && (hit.Success ? states[border.T].Stock[3] < targetMaterials : hit.Amount == 0),
            $"a raid resolves with casualties on both sides, loot moved not made, and worse relations ({(hit.Success ? $"took {hit.Amount / 1000} materials" : "repelled")})");

        // Truce after long hostility; relations drift back toward where they started.
        factions[union].Relations[traders] = factions[traders].Relations[union] = -60;
        actions.Clear();
        foreach (var s in states) for (int g = 0; g < Economy.Goods; g++) s.Stock[g] = pop.Residents[s.Index] * 1000L * 5;   // nobody needy
        hour = (hour / 24 + 1) * 24 + Politics.DecisionHour(union) - 1;
        events.Clear();
        Politics.Hour(pop, states, factions, actions, ++hour, events);
        check(events.Any(e => e.Kind == "truce") && factions[union].Relations[traders] == -40 && factions[traders].Relations[union] == -40,
            "long hostility between the Union and the Traders ends in a truce");
        int drifted = factions[union].Relations[traders];
        hour = (hour / 24 + 1) * 24 - 1;
        Politics.Hour(pop, states, factions, actions, ++hour, events);
        check(factions[union].Relations[traders] == drifted + 1, "relations drift a point a day back toward where they started");

        // Determinism and persistence: the same seed makes the same moves; state survives a JSON round trip.
        string History()
        {
            var p = new Population(city, 40_000);
            var s = Economy.Create(p, city);
            var f = Politics.Create();
            var a = new List<FactionAction>();
            var log = new List<FactionEvent>();
            for (long h = 1; h <= 48; h++)
            {
                for (int t = 0; t < Economy.TicksPerHour / 5; t++) p.Step();
                Economy.Hour(p, s, h, new());
                Politics.Hour(p, s, f, a, h, log);
            }
            return JsonSerializer.Serialize(new { s, f, a, log });
        }
        check(History() == History(), "the same seed makes the same faction moves");

        var world = new WorldAuthority(new WorldSave { Population = 20_000 }, synchronousPopulation: true) { PromotionClearance = 0 };
        var eve = world.Join(2, new string('f', 32), "Fay");
        Steps(world, WorldAuthority.PopulationEvery);
        while (world.State.FactionStates.Count == 0) Steps(world, WorldAuthority.PopulationEvery);
        world.State.FactionActions.Add(new FactionAction
        {
            Id = 99, Kind = "raid", From = Politics.Choir, To = 0, FromSettlement = border.U, ToSettlement = world.Pop!.Settlements.First(x => x.Spawn).Index,
            Good = 0, DueHour = world.State.EconomyTicks / Economy.TicksPerHour + 1
        });
        var listener = Summon(world, false, eve.X + 1.5, eve.Z, faction: 0);   // a witness at the plaza
        while (!world.State.Facts.Any(f => f.Kind == "raid")) { listener.X = eve.X + 1.5; listener.Z = eve.Z; Steps(world, WorldAuthority.PopulationEvery); }
        Steps(world, WorldAuthority.PopulationEvery);
        listener.X = eve.X + 1.5; listener.Z = eve.Z;
        var told = world.Ask(2);
        var snapshot = world.Snapshot(2);
        check(told.Lines.Any(l => l.Contains("The Hollow Choir") && (l.Contains("raided") || l.Contains("driven off"))) &&
              snapshot.Factions!.Single(f => f.Id == 0).Relations![Politics.Choir] < -60,
            $"a raid on the plaza becomes a rumor witnesses repeat and shows in the faction data (\"{told.Lines.FirstOrDefault(l => l.Contains("Choir"))}\")");

        string dir = Path.Combine(Path.GetTempPath(), "questsim-politics-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "world.json");
        try
        {
            WorldStorage.Save(path, world.State);
            var saved = WorldStorage.Load(path);
            check(JsonSerializer.Serialize(saved.FactionStates) == JsonSerializer.Serialize(world.State.FactionStates) &&
                  JsonSerializer.Serialize(saved.FactionActions) == JsonSerializer.Serialize(world.State.FactionActions), "relations and moves in flight are saved with the world");
            saved.FactionActions.Add(new FactionAction { Kind = "bribe" });
            bool rejected = false;
            try { WorldStorage.Save(path, saved); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "an unknown faction move is rejected instead of saved");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
