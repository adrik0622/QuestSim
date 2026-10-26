namespace QuestSim.Domain;

public enum Good { Food, Medicine, Ammo, Materials }

// Saved per settlement. Stock is in milli person-days: 1000 food feeds one resident for one game day.
public sealed class SettlementState
{
    public int Index { get; set; }
    public int Profile { get; set; }
    public long[] Stock { get; set; } = new long[Economy.Goods];
    public int Order { get; set; } = Economy.CalmOrder;
    public int HoursHungry { get; set; }   // consecutive game hours in which not everyone could be fed
    public int Reported { get; set; }      // bit per good currently reported as short (hysteresis)
}

public sealed record EconomyEvent(string Kind, int Settlement, Good Good);
// What the debug map shows: days of stock ×10 per good, order, reported shortages.
public sealed record SettlementInfo(int Index, int Faction, int Profile, int Residents, int[] Days10, int Order, int Short);

// Accounting for one hour, so tests can check that goods only appear or vanish through these.
public sealed class EconomyTally
{
    public long[] Produced = new long[Economy.Goods], Consumed = new long[Economy.Goods], Spoiled = new long[Economy.Goods];
    public int Refugees;
}

// F19: four goods per settlement, made by residents according to the settlement's profile, eaten by residents,
// shared along roads within a faction. Shortages lower order and, after a hungry day, send people away. Runs once per
// game hour between population ticks; integer-only and in index order, so a seed always produces the same history.
public static class Economy
{
    public const int Goods = 4, TicksPerHour = 3600 / Population.GameSecondsPerTick, CalmOrder = 70, MaxOrder = 90;
    public const int ShareDivisor = 20, RecoveredDays = 3, UnrestOrder = 40;
    public static readonly string[] GoodNames = ["food", "medicine", "ammo", "materials"];
    public static readonly string[] ProfileNames = ["farm", "workshop", "clinic", "mixed"];
    public const int Farm = 0, Workshop = 1, Clinic = 2, Mixed = 3;
    // Per resident per game day, in milli person-days. Farms feed themselves and about one more person; overall output
    // roughly matches need, so shortages come from geography and politics, not from a starving world.
    static readonly int[] Need = [1000, 20, 10, 50];
    static readonly int[][] Make = [[2200, 5, 3, 30], [500, 5, 50, 250], [500, 150, 3, 30], [650, 10, 8, 50]];
    static readonly int[] StoreDays = [30, 60, 60, 60];   // beyond this much surplus spoils or is lost
    static readonly int[] BasePrice = [2, 25, 5, 3];

    // Districts outside the urban core (the towns and countryside) farm; urban districts get a seeded trade.
    public static int ProfileOf(CityGen city, Settlement s)
    {
        if (city.BiomeAt(s.X, s.Z) != Biome.Urban) return Farm;
        uint h = Population.Hash4((uint)city.Seed, 61, (uint)s.Index, 0) % 100;
        return h < 24 ? Farm : h < 40 ? Workshop : h < 48 ? Clinic : Mixed;
    }

    public static List<SettlementState> Create(Population pop, CityGen city) => pop.Settlements.Select(s =>
    {
        var state = new SettlementState { Index = s.Index, Profile = ProfileOf(city, s) };
        long residents = pop.Residents[s.Index];
        for (int g = 0; g < Goods; g++)
            state.Stock[g] = residents * Need[g] * (5 + Population.Hash4((uint)city.Seed, 62, (uint)s.Index, (uint)g) % 6);   // 5-10 days
        return state;
    }).ToList();

    public static bool Allied(Population pop, int a, int b) => pop.SettlementFaction[a] == pop.SettlementFaction[b];

    public static int Days10(SettlementState s, int residents, Good good) =>
        residents == 0 ? 999 : (int)Math.Min(999, s.Stock[(int)good] * 10 / ((long)residents * Need[(int)good]));

    // Price in scrip, from days of stock: the base price at 10 days, up to ~8x when nearly gone, down to 1/4 in plenty.
    public static int Price(SettlementState s, int residents, Good good) =>
        Math.Max(1, BasePrice[(int)good] * 100 / Math.Clamp(Days10(s, residents, good), 12, 400));

    public static void Hour(Population pop, List<SettlementState> states, long hour, List<EconomyEvent> events, EconomyTally? tally = null)
    {
        tally ??= new EconomyTally();
        int n = states.Count;
        var hungry = new bool[n];
        var unmet = new long[n, Goods];
        // 1. Make, eat, spoil.
        for (int s = 0; s < n; s++)
        {
            var state = states[s];
            long residents = pop.Residents[s];
            for (int g = 0; g < Goods; g++)
            {
                long made = residents * Make[state.Profile][g] / 24, need = residents * Need[g] / 24;
                state.Stock[g] += made; tally.Produced[g] += made;
                long eaten = Math.Min(state.Stock[g], need);
                state.Stock[g] -= eaten; tally.Consumed[g] += eaten;
                unmet[s, g] = need - eaten;
                long cap = residents * Need[g] * StoreDays[g];
                if (state.Stock[g] > cap) { tally.Spoiled[g] += state.Stock[g] - cap; state.Stock[g] = cap; }
            }
        }
        // 2. Everyday sharing along roads within a faction: each link closes 1/20 of the per-person gap, computed from the
        //    stock before any sharing this hour, so the result does not depend on link order.
        var delta = new long[n, Goods];
        for (int a = 0; a < n; a++)
            foreach (int b in pop.Neighbours(a))
            {
                if (b <= a || !Allied(pop, a, b)) continue;
                long ra = pop.Residents[a], rb = pop.Residents[b];
                if (ra + rb == 0) continue;
                for (int g = 0; g < Goods; g++)
                {
                    long move = (states[a].Stock[g] * rb - states[b].Stock[g] * ra) / (ra + rb) / ShareDivisor;   // + means a gives to b
                    delta[a, g] -= move; delta[b, g] += move;
                }
            }
        for (int s = 0; s < n; s++)
            for (int g = 0; g < Goods; g++) states[s].Stock[g] += delta[s, g];
        // 3. Order, hunger and news. Order falls while people go without food or medicine and recovers slowly otherwise.
        for (int s = 0; s < n; s++)
        {
            var state = states[s];
            int residents = pop.Residents[s];
            if (residents == 0) continue;
            bool noFood = unmet[s, (int)Good.Food] > 0, noMedicine = unmet[s, (int)Good.Medicine] > 0;
            int foodDays10 = Days10(state, residents, Good.Food);
            int change = noFood ? -3 : foodDays10 < 20 ? -1 : 0;
            if (noMedicine) change -= 1;
            if (!noFood && !noMedicine && foodDays10 >= RecoveredDays * 10 && state.Order < MaxOrder) change += 1;
            state.Order = Math.Clamp(state.Order + change, 0, 100);
            state.HoursHungry = noFood ? state.HoursHungry + 1 : 0;
            hungry[s] = state.HoursHungry >= 24 && state.Order < UnrestOrder;
            foreach (var good in new[] { Good.Food, Good.Medicine })
            {
                int bit = 1 << (int)good;
                if (unmet[s, (int)good] > 0 && (state.Reported & bit) == 0) { state.Reported |= bit; events.Add(new("shortage", s, good)); }
                else if ((state.Reported & bit) != 0 && Days10(state, residents, good) >= RecoveredDays * 10) { state.Reported &= ~bit; events.Add(new("recovered", s, good)); }
            }
        }
        // 4. After a hungry day in unrest, 1% of the people at home (at least one) leave each hour for the neighbour with the most
        //    food per person (and join its faction); the Hollow Choir only takes in its own.
        var destination = Enumerable.Repeat(-1, n).ToArray();
        for (int s = 0; s < n; s++)
        {
            if (!hungry[s]) continue;
            bool choir = pop.SettlementFaction[s] == Population.Hostile;
            destination[s] = pop.Neighbours(s)
                .Where(b => (pop.SettlementFaction[b] == Population.Hostile) == choir && Days10(states[b], pop.Residents[b], Good.Food) >= RecoveredDays * 10)
                .OrderByDescending(b => Days10(states[b], pop.Residents[b], Good.Food)).ThenBy(b => b).DefaultIfEmpty(-1).First();
        }
        if (destination.All(d => d < 0)) return;
        var leaving = new Dictionary<int, List<(uint Hash, uint Agent)>>();
        for (uint i = 0; i < pop.Count; i++)
        {
            uint info = pop.Info[i];
            if ((info & (Population.Zombie | Population.Dead | Population.Detailed | Population.Travelling)) != 0) continue;
            int home = (int)(info & 0xFFFFFF);
            if (destination[home] < 0) continue;
            if (!leaving.TryGetValue(home, out var list)) leaving[home] = list = new();
            list.Add((Population.Hash4((uint)pop.Seed, (uint)hour, i, 31), i));
        }
        foreach (var (home, list) in leaving.OrderBy(l => l.Key))
            foreach (var (_, i) in list.OrderBy(c => c.Hash).ThenBy(c => c.Agent).Take(Math.Max(1, list.Count / 100)))
            {
                pop.Relocate(i, destination[home]);
                pop.Residents[home]--; pop.Residents[destination[home]]++;
                tally.Refugees++;
            }
    }

    public static SettlementInfo Info(Population pop, SettlementState s)
    {
        int residents = pop.Residents[s.Index];
        return new(s.Index, pop.SettlementFaction[s.Index], s.Profile, residents,
            Enumerable.Range(0, Goods).Select(g => Days10(s, residents, (Good)g)).ToArray(), s.Order, s.Reported);
    }
}

public sealed partial class WorldAuthority
{
    public EconomyTally LastEconomyHour { get; private set; } = new();

    // Between population ticks: the economy keeps its own clock (coarse ticks while the world runs), so a population
    // restored from an older snapshot or regenerated from the seed never replays or skips economy hours.
    void StepEconomy()
    {
        var pop = Pop!;
        if (State.Settlements.Count == 0) State.Settlements = Economy.Create(pop, City);
        if (State.Settlements.Count != pop.Settlements.Count) throw new InvalidDataException("Economy does not match this world's settlements");
        if (++State.EconomyTicks % Economy.TicksPerHour != 0) return;
        var events = new List<EconomyEvent>();
        var tally = new EconomyTally();
        Economy.Hour(pop, State.Settlements, State.EconomyTicks / Economy.TicksPerHour, events, tally);
        LastEconomyHour = tally;
        foreach (var e in events)
        {
            var s = pop.Settlements[e.Settlement];
            AddFact(e.Kind, $"settlement:{e.Settlement}", Economy.GoodNames[(int)e.Good], s.X, s.Z);
        }
    }

    public IEnumerable<SettlementInfo> SettlementInfos() =>
        Pop is null ? [] : State.Settlements.Select(s => Economy.Info(Pop, s));
}
