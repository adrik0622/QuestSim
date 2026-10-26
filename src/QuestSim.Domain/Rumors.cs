namespace QuestSim.Domain;

public sealed record AskResult(string Speaker, string[] Lines, string Reason);
public sealed record FactionInfo(int Id, string Name, int Standing, long Members, int[]? Relations = null, string LastMove = "");

// Facts worth talking about become rumors in the population (E1 spread model); NPCs answer from their own beliefs.
// Faction standing toward the players is the first consequence loop: kill a faction's people and it turns hostile.
public sealed partial class WorldAuthority
{
    public const long RumorLifetime = 3600;          // population ticks (2 game days at 48:1)
    public const double WitnessRadius = 150, AskReach = 3;
    public const int HostileBelow = -50;
    static readonly HashSet<string> RumorKinds = ["defeat", "capture", "rescued", "turned", "died", "claimed", "attack", "shortage", "hardship", "trade", "raid", "truce", "overrun", "threat"];
    public const int AttackFactThreshold = 10;

    // Background attrition becomes news when a settlement loses enough people in one coarse tick.
    void ReportLosses()
    {
        var pop = Pop!;
        foreach (var (home, lost) in pop.SettlementLosses.Where(l => l.Value >= AttackFactThreshold).OrderBy(l => l.Key))
        {
            var s = pop.Settlements[home];
            AddFact("attack", $"faction:{pop.SettlementFaction[home]}", $"settlement:{home}:{lost}", s.X, s.Z);
        }
        pop.SettlementLosses.Clear();
    }
    readonly int[] slotFact = Enumerable.Repeat(-1, 64).ToArray();
    readonly long[] slotStart = new long[64];
    readonly uint[] slotCredibility = new uint[64];
    readonly List<(int Slot, double X, double Z)> pendingSeeds = new();
    readonly List<int> pendingClears = new();

    partial void OnFact(Fact fact)
    {
        if (Pop is null || !RumorKinds.Contains(fact.Kind)) return;
        if (fact.Kind == "claimed" && State.Opportunities.FirstOrDefault(o => $"opportunity:{o.Id}" == fact.Object)?.Kind != "revenge") return;
        int slot = Array.IndexOf(slotFact, -1);
        if (slot < 0) slot = Enumerable.Range(0, 64).OrderBy(s => slotStart[s]).First();   // recycle the oldest story
        if (slotFact[slot] >= 0) pendingClears.Add(slot);   // population arrays are only touched between ticks
        slotFact[slot] = fact.Id;
        slotStart[slot] = Pop.Tick;
        slotCredibility[slot] = 0xA000 + Population.Hash4((uint)State.WorldSeed, 51, (uint)fact.Id, 0) % 0x5000;
        double x = fact.X, z = fact.Z;
        if (fact.Kind == "claimed")
        {
            var claimant = State.Characters.FirstOrDefault(c => c.Id == fact.Subject);
            if (claimant is not null) { x = claimant.X; z = claimant.Z; }
        }
        pendingSeeds.Add((slot, x, z));
    }

    // Between population ticks: witnesses near new facts learn them; rumors nobody carries any more retire.
    void PrepareRumors()
    {
        var pop = Pop!;
        foreach (int slot in pendingClears)
            for (int i = 0; i < pop.Count; i++) { if (slot < 32) pop.MaskLo[i] &= ~(1u << slot); else pop.MaskHi[i] &= ~(1u << (slot - 32)); }
        pendingClears.Clear();
        for (int slot = 0; slot < 64; slot++)
            if (slotFact[slot] >= 0 && pop.Carriers[slot] == 0 && pop.Tick - slotStart[slot] > 2 && !pendingSeeds.Any(p => p.Slot == slot))
                slotFact[slot] = -1;
        foreach (var (slot, x, z) in pendingSeeds)
            foreach (uint i in pop.Near(x, z, WitnessRadius))
                if (!Population.IsZombie(pop.Info[i])) pop.Tell(i, slot);
        pendingSeeds.Clear();
    }

    Population.RumorInput RumorInput()
    {
        var pop = Pop!;
        var credibility = new uint[64];
        uint lo = 0, hi = 0;
        for (int slot = 0; slot < 64; slot++)
        {
            if (slotFact[slot] < 0) continue;
            if (slot < 32) lo |= 1u << slot; else hi |= 1u << (slot - 32);
            long age = pop.Tick - slotStart[slot];
            credibility[slot] = age >= RumorLifetime ? 0 : (uint)(slotCredibility[slot] * (RumorLifetime - age) / RumorLifetime);
        }
        var trust = new uint[64];
        for (int a = 0; a < Population.HumanFactions; a++)
            for (int b = 0; b < Population.HumanFactions; b++)
                trust[a * 8 + b] = a == b ? 0xC000u : (uint)(0x1800 + (Relation(a, b) + 100) * (0xC000 - 0x1800) / 200);   // allies are believed more
        return new Population.RumorInput(credibility, trust, lo, hi, (uint)(0.02 * uint.MaxValue));
    }

    public int Standing(int faction) => State.Standing.TryGetValue(faction, out int s) ? s :
        faction == Population.Hostile ? -100 : faction == 0 ? 20 : 0;

    void AdjustStanding(int faction, int change)
    {
        if (faction >= Population.HumanFactions) return;
        int before = Standing(faction), after = Math.Clamp(before + change, -100, 100);
        State.Standing[faction] = after;
        if (before > HostileBelow && after <= HostileBelow) AddFact("hostile", $"faction:{faction}", "players", 0, 0);
    }

    public bool IsHostile(Npc npc) => npc.Zombie || Standing(npc.Faction) <= HostileBelow;

    public IEnumerable<FactionInfo> Factions() => Enumerable.Range(0, Population.FactionNames.Length)
        .Select(f => new FactionInfo(f, Population.FactionNames[f], f < Population.HumanFactions ? Standing(f) : -100, Pop?.Members[f] ?? 0,
            Enumerable.Range(0, Politics.Count).Select(o => Relation(f, o)).ToArray(), State.FactionStates.ElementAtOrDefault(f)?.LastMove ?? ""));

    // H: the nearest human NPC in reach tells you what it has actually heard.
    public AskResult Ask(int peer)
    {
        var c = CharacterFor(peer);
        var npc = npcs.Values.Where(n => !n.Dead && !n.Zombie && Distance(n.X, n.Z, c.X, c.Z) <= AskReach)
            .OrderBy(n => Distance(n.X, n.Z, c.X, c.Z)).ThenBy(n => n.Index).FirstOrDefault();
        if (npc is null) return new("", [], "nobody");
        if (IsHostile(npc)) return new(DisplayName(npc), [$"{Population.FactionNames[npc.Faction]} doesn't talk to you."], "refused");
        var lines = Enumerable.Range(0, 64).Where(s => slotFact[s] >= 0 && Pop!.Knows(npc.Index, s))
            .Select(s => State.Facts[slotFact[s]]).OrderByDescending(f => f.Tick).Select(Describe).ToArray();
        return new(DisplayName(npc), lines.Length > 0 ? lines : ["Haven't heard anything worth telling."], lines.Length > 0 ? "rumors" : "nothing");
    }

    string NameOf(string id)
    {
        if (id.StartsWith("actor:")) return State.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "someone";
        if (id.StartsWith("npc:") && Pop is not null)
        {
            uint i = uint.Parse(id[4..]);
            var title = State.Named.FirstOrDefault(n => n.Index == i)?.Title;
            return Pop.NameOf(i) + (string.IsNullOrEmpty(title) ? "" : " " + title);
        }
        return "someone";
    }

    // "the Survivors' Union", "the Hollow Choir": names with or without their own article, capitalised at a sentence start.
    static string The(string name, bool start = false) => (start ? "The " : "the ") + (name.StartsWith("The ") ? name[4..] : name);
    static string FactionOf(string id, bool start = false) => The(Population.FactionNames[int.Parse(id.Split(':')[1])], start);
    string SettlementFaction(string id, bool start = false) => The(Population.FactionNames[Pop?.SettlementFaction[int.Parse(id.Split(':')[1])] ?? 0], start);

    static string Place(double x, double z)
    {
        double d = Math.Sqrt(x * x + z * z);
        if (d < 60) return "at the plaza";
        string compass = Math.Abs(x) > Math.Abs(z) ? (x > 0 ? "east" : "west") : (z > 0 ? "south" : "north");
        return $"about {Math.Round(d / 50) * 50:0} m {compass} of the plaza";
    }

    string Describe(Fact f) => f.Kind switch
    {
        "defeat" => $"{NameOf(f.Object)} beat {NameOf(f.Subject)} {Place(f.X, f.Z)}.",
        "capture" => $"{NameOf(f.Object)} is holding {NameOf(f.Subject)} {Place(f.X, f.Z)}.",
        "rescued" => $"{NameOf(f.Object)} freed {NameOf(f.Subject)} {Place(f.X, f.Z)}.",
        "turned" => $"{NameOf(f.Subject)} turned; the infection took them {Place(f.X, f.Z)}.",
        "died" => $"{NameOf(f.Subject)} died {Place(f.X, f.Z)}.",
        "attack" => $"The dead overran the settlement {Place(f.X, f.Z)}; {f.Object.Split(':')[2]} people were lost.",
        "shortage" => f.Object == "food" ? $"Food is running short among {SettlementFaction(f.Subject)}; people are going hungry {Place(f.X, f.Z)}."
            : $"{SettlementFaction(f.Subject, true)} are running out of {f.Object}; it's worst {Place(f.X, f.Z)}.",
        "threat" => $"A great horde is moving on the {f.Object} {Place(f.X, f.Z)}.",
        "overrun" => $"The dead overran the {f.Object} {Place(f.X, f.Z)}; it will take days to rebuild.",
        "rebuilt" => $"The {f.Object} {Place(f.X, f.Z)} is working again.",
        "hardship" => f.Object switch
        {
            "food" => $"Blight has hit the fields of {SettlementFaction(f.Subject)}.",
            "medicine" => $"Fever is going round {SettlementFaction(f.Subject)}; their medicine won't last.",
            "ammo" => $"{SettlementFaction(f.Subject, true)} lost an ammunition store.",
            _ => $"{SettlementFaction(f.Subject, true)}'s salvage crews keep coming back empty-handed."
        },
        "recovered" => $"Supplies of {f.Object} reached the settlement {Place(f.X, f.Z)} again.",
        "trade" => $"{FactionOf(f.Subject, true)} traded with {FactionOf(f.Object)} {Place(f.X, f.Z)}; {f.Object.Split(':')[2]} changed hands.",
        "raid" when f.Object.Split(':')[2] == "took" => $"{FactionOf(f.Subject, true)} raided the settlement {Place(f.X, f.Z)} and carried off {f.Object.Split(':')[3]}.",
        "raid" => $"{FactionOf(f.Subject, true)} attacked the settlement {Place(f.X, f.Z)} but were driven off.",
        "truce" => $"{FactionOf(f.Subject, true)} and {FactionOf(f.Object)} agreed a truce.",
        "claimed" => $"{NameOf(f.Subject)} brought down {NameOf(State.Opportunities.First(o => $"opportunity:{o.Id}" == f.Object).Target)}.",
        _ => $"Something happened {Place(f.X, f.Z)}."
    };
}

public sealed partial class WorldAuthority
{
    public const double InspectReach = 10;

    // I: a debugging view of the nearest NPC (generated identity, state, carried items, beliefs). Read-only.
    public string[] Inspect(int peer)
    {
        var c = CharacterFor(peer);
        var npc = npcs.Values.Where(n => !n.Dead && Distance(n.X, n.Z, c.X, c.Z) <= InspectReach)
            .OrderBy(n => Distance(n.X, n.Z, c.X, c.Z)).ThenBy(n => n.Index).FirstOrDefault();
        if (npc is null || Pop is null) return ["Nobody within 10 m to inspect."];
        var named = State.Named.FirstOrDefault(n => n.Index == npc.Index);
        var home = Pop.Settlements[(int)(Pop.Info[npc.Index] & 0xFFFFFF) % Pop.Settlements.Count];
        var beliefs = Enumerable.Range(0, 64).Where(s => slotFact[s] >= 0 && Pop.Knows(npc.Index, s)).Select(s => Describe(State.Facts[slotFact[s]])).ToArray();
        return
        [
            $"{DisplayName(npc)} (npc:{npc.Index}) - {(npc.Zombie ? "zombie" : "human")}, {Population.FactionNames[npc.Faction]}" +
                (npc.Zombie ? "" : $", standing {Standing(npc.Faction):+0;-0}") + (IsHostile(npc) ? ", HOSTILE" : ""),
            $"Health {npc.Health}; at ({npc.X:0}, {npc.Z:0}); " + (npc.Zombie ? $"horde {Pop.Info[npc.Index] & 0xFFFFFF}" : $"home settlement {home.Index} at ({home.X}, {home.Z})"),
            named is null ? "Unremarkable (generated, nothing stored)." :
                $"Named: \"{named.Title}\", {named.Victories} victories, +{named.BonusHealth} health, carrying {(named.Inventory.Count == 0 ? "nothing" : string.Join(", ", named.Inventory.Select(i => $"{i.Key.Split(':')[1]} {i.Value}")))}",
            beliefs.Length == 0 ? "Has heard nothing worth telling." : $"Has heard {beliefs.Length}: {beliefs[0]}"
        ];
    }
}
