namespace QuestSim.Domain;

// Saved per faction (F20). Relations run -100..100 toward every faction (index = faction id).
public sealed class FactionState
{
    public int Id { get; set; }
    public int[] Relations { get; set; } = new int[Politics.Count];
    public string LastMove { get; set; } = "";
    public long LastMoveHour { get; set; }
}

// A decided move on its way (F21 will make these travelling groups). Goods are taken when it resolves, not before.
public sealed class FactionAction
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";                // trade | raid
    public int From { get; set; }
    public int To { get; set; }
    public int FromSettlement { get; set; }
    public int ToSettlement { get; set; }
    public int Good { get; set; }                         // trade: what From gives; raid: what From is after
    public long Amount { get; set; }
    public int ReturnGood { get; set; }                   // trade only: what To gives back
    public long ReturnAmount { get; set; }
    public long DueHour { get; set; }
}

public sealed record FactionEvent(string Kind, int From, int To, int Settlement, Good Good, long Amount, bool Success = true);

// F20: once per game day each human faction makes one move for its neediest settlement: barter with a friendly
// neighbour, raid an enemy neighbour, seek a truce, or hold. Moves are pending actions that resolve after a
// travel delay, moving real goods between border settlements. Integer-only and in id order, so seeds replay exactly.
public static class Politics
{
    public const int Count = 5, Union = 0, Traders = 1, Choir = Population.Hostile;
    public const int TradeDelay = 12, RaidDelay = 6, Lacking = 30, Desperate = 15, Spare = 30;   // hours; days of stock ×10 (people keep 3-6 days)
    public const int TradeAbove = -10, RaidBelow = -20, TruceBelow = -40;
    // Until F22 leaders bring traits: Union cautious, Traders mercantile, Choir aggressive.
    public static readonly string[] Temperaments = ["cautious", "mercantile", "aggressive"];

    public static int StartRelation(int a, int b) =>
        a == b ? 100 : a >= Population.HumanFactions || b >= Population.HumanFactions ? -100 : a == Choir || b == Choir ? -60 : 20;

    public static List<FactionState> Create() => Enumerable.Range(0, Count).Select(f => new FactionState
    {
        Id = f, Relations = Enumerable.Range(0, Count).Select(o => StartRelation(f, o)).ToArray()
    }).ToList();

    public static int DecisionHour(int faction) => 6 + 4 * faction;

    static long FactionResidents(Population pop, int faction) =>
        Enumerable.Range(0, pop.Settlements.Count).Where(s => pop.SettlementFaction[s] == faction).Sum(s => (long)pop.Residents[s]);

    static long FactionStock(Population pop, List<SettlementState> settlements, int faction, int good) =>
        settlements.Where(s => pop.SettlementFaction[s.Index] == faction).Sum(s => s.Stock[good]);

    static int FactionDays10(Population pop, List<SettlementState> settlements, int faction, int good)
    {
        long people = FactionResidents(pop, faction);
        return people == 0 ? 999 : (int)Math.Min(999, FactionStock(pop, settlements, faction, good) * 10 / (people * Economy.Need[good]));
    }

    // Takes up to `amount` of a good from every settlement of a faction in proportion to its stock (the remainder from the
    // largest), so nothing is created or lost. Returns what was taken.
    static long TakeFromFaction(Population pop, List<SettlementState> settlements, int faction, int good, long amount)
    {
        var members = settlements.Where(s => pop.SettlementFaction[s.Index] == faction && s.Stock[good] > 0).ToArray();
        long total = members.Sum(s => s.Stock[good]);
        amount = Math.Min(amount, total);
        if (amount <= 0) return 0;
        long taken = 0;
        foreach (var s in members)
        {
            long part = (long)((Int128)s.Stock[good] * amount / total);
            s.Stock[good] -= part; taken += part;
        }
        foreach (var s in members.OrderByDescending(s => s.Stock[good]).ThenBy(s => s.Index))
        {
            long part = Math.Min(s.Stock[good], amount - taken);
            s.Stock[good] -= part; taken += part;
            if (taken == amount) break;
        }
        return taken;
    }

    // Adjacent settlements across two factions' border, in index order.
    static IEnumerable<(int A, int B)> Border(Population pop, int a, int b) =>
        Enumerable.Range(0, pop.Settlements.Count).Where(s => pop.SettlementFaction[s] == a && pop.Residents[s] > 0)
            .SelectMany(s => pop.Neighbours(s).Where(n => pop.SettlementFaction[n] == b && pop.Residents[n] > 0).Select(n => (s, n)));

    static void Adjust(List<FactionState> factions, int a, int b, int change)
    {
        if (a >= Population.HumanFactions || b >= Population.HumanFactions || a == b) return;
        factions[a].Relations[b] = Math.Clamp(factions[a].Relations[b] + change, -100, 100);
    }

    public static void Hour(Population pop, List<SettlementState> settlements, List<FactionState> factions, List<FactionAction> actions,
        long hour, List<FactionEvent> events)
    {
        Resolve(pop, settlements, factions, actions, hour, events);
        if (hour % 24 == 0)   // memories fade: relations drift a point a day back toward where they started
            foreach (var f in factions.Where(f => f.Id < Population.HumanFactions))
                for (int o = 0; o < Population.HumanFactions; o++)
                    f.Relations[o] += Math.Sign(StartRelation(f.Id, o) - f.Relations[o]);
        for (int f = 0; f < Population.HumanFactions; f++)
            if (hour % 24 == DecisionHour(f)) Decide(pop, settlements, factions, actions, f, hour, events);
    }

    // The move serves the faction's neediest settlements (lowest days of any good). Goods change hands at a road between
    // the needy settlement and a foreign neighbour, or, for settlements deep inside the faction, at the faction's border
    // with that partner, from where everyday sharing carries them inward.
    static void Decide(Population pop, List<SettlementState> settlements, List<FactionState> factions, List<FactionAction> actions,
        int f, long hour, List<FactionEvent> events)
    {
        var me = factions[f];
        if (actions.Any(a => a.From == f)) return;   // one move in flight at a time
        int aggression = f == Choir ? 3 : f == Traders ? 1 : 0, mercantile = f == Traders ? 2 : 1;
        var options = new List<(int Score, Action Apply, string Move)>();
        var needs = Enumerable.Range(0, settlements.Count).Where(x => pop.SettlementFaction[x] == f && pop.Residents[x] > 0)
            .SelectMany(x => Enumerable.Range(0, Economy.Goods).Select(g => (Settlement: x, Good: g, Days: Economy.Days10(settlements[x], pop.Residents[x], (Good)g))))
            .Where(n => n.Days < Lacking).OrderBy(n => n.Days).ThenBy(n => n.Settlement).ThenBy(n => n.Good).Take(6).ToArray();
        foreach (var need in needs)
        {
            int want = need.Good;
            var direct = pop.Neighbours(need.Settlement).Where(b => pop.SettlementFaction[b] != f).OrderBy(b => b).Select(b => (A: need.Settlement, B: b));
            var routes = direct.Any() ? direct.ToArray() : Enumerable.Range(0, Population.HumanFactions).Where(p => p != f)
                .Select(p => Border(pop, f, p).OrderByDescending(x => Economy.Days10(settlements[x.B], pop.Residents[x.B], (Good)want)).ThenBy(x => x.A).ThenBy(x => x.B)
                    .Cast<(int A, int B)?>().FirstOrDefault()).Where(x => x is not null).Select(x => x!.Value).ToArray();
            foreach (var (a, b) in routes)
            {
                var home = settlements[a];
                int residents = Math.Max(pop.Residents[a], pop.Residents[need.Settlement]);
                int p = pop.SettlementFaction[b];
                if (p == f || pop.Residents[b] == 0) continue;
                var there = settlements[b];
                // Trade at faction scale (logistics pools each faction's goods): barter what we have most of for what we
                // lack, with a faction that can spare it; at most 3 days of our need and a quarter of their stock.
                long ourPeople = FactionResidents(pop, f), theirPeople = FactionResidents(pop, p);
                int ourDays = FactionDays10(pop, settlements, f, want), theirDays = FactionDays10(pop, settlements, p, want);
                if (f != Choir && p != Choir && me.Relations[p] >= TradeAbove && factions[p].Relations[f] >= TradeAbove && theirDays >= Spare && theirDays > ourDays + 15)
                {
                    int give = Enumerable.Range(0, Economy.Goods).Where(g => g != want)
                        .OrderByDescending(g => FactionDays10(pop, settlements, f, g) - FactionDays10(pop, settlements, p, g)).ThenBy(g => g).First();
                    long receive = Math.Min(ourPeople * Economy.Need[want] * 3, FactionStock(pop, settlements, p, want) / 4);
                    long pay = Math.Min(receive * Economy.BasePrice[want] / Economy.BasePrice[give] * Economy.Need[give] / Economy.Need[want], FactionStock(pop, settlements, f, give) / 3);
                    if (receive > 0 && pay > 0)
                    {
                        int partner = p, to = b;
                        options.Add(((Lacking - need.Days) * mercantile + me.Relations[p] / 5, () => actions.Add(new FactionAction
                        {
                            Id = NextId(actions, hour), Kind = "trade", From = f, To = partner, FromSettlement = a, ToSettlement = to,
                            Good = give, Amount = pay, ReturnGood = want, ReturnAmount = receive, DueHour = hour + TradeDelay
                        }), $"trade with {Population.FactionNames[partner]}: {Economy.GoodNames[give]} for {Economy.GoodNames[want]}"));
                    }
                }
                // Raid: an enemy neighbour that has what we lack (the Choir treats everyone as an enemy).
                if (p < Population.HumanFactions && (f == Choir || me.Relations[p] <= RaidBelow) && there.Stock[want] > 0 && (need.Days < Desperate || f == Choir))
                {
                    int target = p, to = b;
                    options.Add(((Desperate + 30 - need.Days) / 2 + aggression * 15 - me.Relations[p] / 10, () => actions.Add(new FactionAction
                    {
                        Id = NextId(actions, hour), Kind = "raid", From = f, To = target, FromSettlement = a, ToSettlement = to, Good = want, DueHour = hour + RaidDelay
                    }), $"raid {Population.FactionNames[target]} for {Economy.GoodNames[want]}"));
                }
            }
        }

        // Truce: long hostility with a faction that is not the Choir.
        if (f != Choir)
            for (int p = 0; p < Population.HumanFactions; p++)
            {
                if (p == f || p == Choir || me.Relations[p] > TruceBelow) continue;
                int other = p;
                options.Add(((TruceBelow - me.Relations[p]) / 2 + 5, () =>
                {
                    Adjust(factions, f, other, 20); Adjust(factions, other, f, 20);
                    var s = Border(pop, f, other).Select(x => x.A).DefaultIfEmpty(0).First();
                    events.Add(new("truce", f, other, s, Good.Food, 0));
                }, $"truce with {Population.FactionNames[other]}"));
            }

        var best = options.OrderByDescending(o => o.Score).ThenBy(o => o.Move, StringComparer.Ordinal).FirstOrDefault();
        if (best.Apply is null || best.Score <= 0) { me.LastMove = "hold"; me.LastMoveHour = hour; return; }
        best.Apply();
        me.LastMove = best.Move; me.LastMoveHour = hour;
    }

    static int NextId(List<FactionAction> actions, long hour) => (int)(hour * 8 + actions.Count);

    static void Resolve(Population pop, List<SettlementState> settlements, List<FactionState> factions, List<FactionAction> actions,
        long hour, List<FactionEvent> events)
    {
        foreach (var act in actions.Where(a => a.DueHour <= hour).OrderBy(a => a.DueHour).ThenBy(a => a.Id).ToArray())
        {
            actions.Remove(act);
            var from = settlements[act.FromSettlement]; var to = settlements[act.ToSettlement];
            if (act.Kind == "trade")
            {
                // Each faction hands over what it still has of the agreed amount, drawn from all its settlements; goods arrive
                // at the border settlements and logistics carries them inward.
                long give = TakeFromFaction(pop, settlements, act.From, act.Good, act.Amount);
                long back = TakeFromFaction(pop, settlements, act.To, act.ReturnGood, act.ReturnAmount);
                to.Stock[act.Good] += give; from.Stock[act.ReturnGood] += back;
                Adjust(factions, act.From, act.To, 2); Adjust(factions, act.To, act.From, 2);
                events.Add(new("trade", act.From, act.To, act.ToSettlement, (Good)act.ReturnGood, back));
                continue;
            }
            // Raid: odds from the people on each side; losers lose more people.
            long attackers = pop.Residents[act.FromSettlement], defenders = pop.Residents[act.ToSettlement];
            int bonus = act.From == Choir ? 15 : 0;
            bool success = attackers + defenders > 0 &&
                Population.Hash4((uint)pop.Seed, (uint)hour, (uint)act.Id, 41) % 100 < Math.Clamp(attackers * 100 / (attackers + defenders) + bonus, 10, 90);
            pop.KillResidents(act.FromSettlement, (int)(attackers / (success ? 200 : 60)) + 1, (uint)(act.Id * 2));
            pop.KillResidents(act.ToSettlement, (int)(defenders / (success ? 60 : 200)) + 1, (uint)(act.Id * 2 + 1));
            long loot = 0;
            if (success)
                for (int g = 0; g < Economy.Goods; g++)
                {
                    long take = to.Stock[g] * (g == act.Good ? 30 : 10) / 100;
                    to.Stock[g] -= take; from.Stock[g] += take;
                    if (g == act.Good) loot = take;
                }
            to.Order = Math.Max(0, to.Order - (success ? 15 : 5));
            Adjust(factions, act.To, act.From, -25); Adjust(factions, act.From, act.To, -10);
            events.Add(new("raid", act.From, act.To, act.ToSettlement, (Good)act.Good, loot, success));
        }
    }
}

public sealed partial class WorldAuthority
{
    void StepFactions(long hour)
    {
        var pop = Pop!;
        if (State.FactionStates.Count == 0) State.FactionStates = Politics.Create();
        var events = new List<FactionEvent>();
        Politics.Hour(pop, State.Settlements, State.FactionStates, State.FactionActions, hour, events);
        foreach (var e in events)
        {
            var s = pop.Settlements[e.Settlement];
            string what = e.Kind switch
            {
                "trade" => $"{Economy.GoodNames[(int)e.Good]}:{e.Amount / 1000}",
                "raid" => $"{(e.Success ? "took" : "repelled")}:{Economy.GoodNames[(int)e.Good]}",
                _ => ""
            };
            AddFact(e.Kind, $"faction:{e.From}", $"faction:{e.To}:{what}", s.X, s.Z);
        }
    }

    public int Relation(int a, int b) => State.FactionStates.Count == 0 ? Politics.StartRelation(a, b) : State.FactionStates[a].Relations[b];
}
