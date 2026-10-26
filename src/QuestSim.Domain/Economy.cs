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
// moved within each faction by its logistics. Shortages lower order and, after a hungry day, send people away. Runs once per
// game hour between population ticks; integer-only and in index order, so a seed always produces the same history.
public static class Economy
{
    public const int Goods = 4, TicksPerHour = 3600 / Population.GameSecondsPerTick, CalmOrder = 70, MaxOrder = 90;
    public const int ShareDivisor = 20, ShortDays = 2, RecoveredDays = 3, UnrestOrder = 40;
    public const int HardshipDays = 4, HardshipChance = 30, HardshipOutput = 10;   // per faction per 4-day window: 30% chance, output 10%

    // Seeded, so nothing is saved: in each 4-day window a faction may suffer one hardship (blight, fever, a lost store,
    // failed salvage) cutting its output of one good. Returns the good, or -1.
    public static int Hardship(ulong seed, int faction, long day)
    {
        uint h = Population.Hash4((uint)seed, 64, (uint)faction, (uint)(day / HardshipDays));
        return h % 100 < HardshipChance ? (int)(h / 100 % Goods) : -1;
    }
    public static readonly string[] GoodNames = ["food", "medicine", "ammo", "materials"];
    public static readonly string[] ProfileNames = ["farm", "workshop", "clinic", "mixed"];
    public const int Farm = 0, Workshop = 1, Clinic = 2, Mixed = 3;
    // Per resident per game day, in milli person-days. Farms feed themselves and more; overall the world makes a modest
    // surplus (food ~6%, medicine ~12%, unevenly by faction). Output varies ±30% per settlement per day (weather, luck),
    // and hardships (below) knock a faction's output of one good down for days, so shortages come and go and give
    // factions reasons to trade or fight. (Measured in the soak: with no surplus nobody can ever spare anything.)
    internal static readonly int[] Need = [1000, 20, 10, 50];
    static readonly int[][] Make = [[2350, 5, 3, 30], [500, 5, 50, 250], [500, 160, 3, 30], [650, 10, 8, 50]];
    static readonly int[] StoreDays = [10, 30, 30, 30];   // beyond this much surplus spoils or is lost: people live close to the edge
    internal static readonly int[] BasePrice = [2, 25, 5, 3];

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
            state.Stock[g] = residents * Need[g] * (3 + Population.Hash4((uint)city.Seed, 62, (uint)s.Index, (uint)g) % 4);   // 3-6 days
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
            long luck = 70 + Population.Hash4((uint)pop.Seed, 63, (uint)s, (uint)(hour / 24)) % 61;   // 70-130% today
            int faction = pop.SettlementFaction[s], hardship = faction < Population.HumanFactions ? Hardship(pop.Seed, faction, hour / 24) : -1;
            for (int g = 0; g < Goods; g++)
            {
                long made = residents * Make[state.Profile][g] * luck * (g == hardship ? HardshipOutput : 100) / 240000, need = residents * Need[g] / 24;
                state.Stock[g] += made; tally.Produced[g] += made;
                long eaten = Math.Min(state.Stock[g], need);
                state.Stock[g] -= eaten; tally.Consumed[g] += eaten;
                unmet[s, g] = need - eaten;
                long cap = residents * Need[g] * StoreDays[g];
                if (state.Stock[g] > cap) { tally.Spoiled[g] += state.Stock[g] - cap; state.Stock[g] = cap; }
            }
        }
        // 2. Faction logistics: within each faction, settlements above the faction's per-person level give 1/20 of their
        //    surplus each hour into a pool that is split among those below it by their shortfall. (Passing goods along
        //    roads hop by hop was far too slow across the city: clinics' medicine spoiled while distant towns ran dry.)
        for (int faction = 0; faction < Population.HumanFactions; faction++)
        {
            var members = Enumerable.Range(0, n).Where(x => pop.SettlementFaction[x] == faction && pop.Residents[x] > 0).ToArray();
            long people = members.Sum(x => (long)pop.Residents[x]);
            if (members.Length < 2 || people == 0) continue;
            for (int g = 0; g < Goods; g++)
            {
                long total = members.Sum(x => states[x].Stock[g]), pool = 0;
                var shortfall = new long[members.Length];
                for (int k = 0; k < members.Length; k++)
                {
                    long fair = total * pop.Residents[members[k]] / people, have = states[members[k]].Stock[g];
                    if (have > fair) { long give = (have - fair) / ShareDivisor; states[members[k]].Stock[g] -= give; pool += give; }
                    else shortfall[k] = fair - have;
                }
                long wanted = shortfall.Sum();
                if (pool == 0 || wanted == 0) { if (pool > 0) states[members[0]].Stock[g] += pool; continue; }
                long handed = 0;
                int neediest = 0;
                for (int k = 0; k < members.Length; k++)
                {
                    long share = (long)((Int128)pool * shortfall[k] / wanted);
                    states[members[k]].Stock[g] += share; handed += share;
                    if (shortfall[k] > shortfall[neediest]) neediest = k;
                }
                states[members[neediest]].Stock[g] += pool - handed;   // rounding remainder, so nothing is created or lost
            }
        }
        if (hour % (24 * HardshipDays) == 0)   // a new window: news of any hardship, from the faction's largest settlement
            for (int f = 0; f < Population.HumanFactions; f++)
                if (Hardship(pop.Seed, f, hour / 24) is int bad and >= 0)
                    events.Add(new("hardship", Enumerable.Range(0, n).Where(x => pop.SettlementFaction[x] == f).OrderByDescending(x => pop.Residents[x]).ThenBy(x => x).First(), (Good)bad));
        // 3. Order, hunger and news. Order falls while people go without food or medicine and recovers slowly otherwise.
        //    Each settlement marks its own shortages (the map shows them); news is per faction and good (with logistics
        //    a faction runs short together): when its first settlement runs short and when its last one recovers.
        int[,] reportedBefore = ReportedCounts(pop, states);
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
                if ((unmet[s, (int)good] > 0 || Days10(state, residents, good) < ShortDays * 10) && (state.Reported & bit) == 0) state.Reported |= bit;
                else if ((state.Reported & bit) != 0 && Days10(state, residents, good) >= RecoveredDays * 10) state.Reported &= ~bit;
            }
        }
        int[,] reportedAfter = ReportedCounts(pop, states);
        for (int f = 0; f < Population.HumanFactions; f++)
            foreach (var good in new[] { Good.Food, Good.Medicine })
            {
                bool started = reportedBefore[f, (int)good] == 0 && reportedAfter[f, (int)good] > 0, ended = reportedBefore[f, (int)good] > 0 && reportedAfter[f, (int)good] == 0;
                if (!started && !ended) continue;
                // Located at the hardest-hit (or, on recovery, the largest) settlement of the faction.
                int at = Enumerable.Range(0, n).Where(x => pop.SettlementFaction[x] == f && pop.Residents[x] > 0)
                    .OrderBy(x => started ? Days10(states[x], pop.Residents[x], good) : -pop.Residents[x]).ThenBy(x => x).DefaultIfEmpty(0).First();
                events.Add(new(started ? "shortage" : "recovered", at, good));
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

    static int[,] ReportedCounts(Population pop, List<SettlementState> states)
    {
        var counts = new int[Population.HumanFactions, Goods];
        foreach (var s in states)
            for (int g = 0; g < Goods; g++)
                if ((s.Reported >> g & 1) != 0 && pop.SettlementFaction[s.Index] < Population.HumanFactions) counts[pop.SettlementFaction[s.Index], g]++;
        return counts;
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
        long hour = State.EconomyTicks / Economy.TicksPerHour;
        Economy.Hour(pop, State.Settlements, hour, events, tally);
        LastEconomyHour = tally;
        foreach (var e in events)
        {
            var s = pop.Settlements[e.Settlement];
            AddFact(e.Kind, $"settlement:{e.Settlement}", Economy.GoodNames[(int)e.Good], s.X, s.Z);
        }
        StepFactions(hour);
    }

    public IEnumerable<SettlementInfo> SettlementInfos() =>
        Pop is null ? [] : State.Settlements.Select(s => Economy.Info(Pop, s));
}
