namespace QuestSim.Domain;

// Defeat instead of death, and a deterministic Virtual Game Master that turns facts into opportunities.
// Every item movement between holders is ledgered (State.Transfers); story-time inventions come from "vgm".
public sealed partial class WorldAuthority
{
    public const string Injured = "questsim:injured", Infected = "questsim:infected", Captive = "questsim:captive", Antiviral = "questsim:antiviral";
    public const long InjuredTicks = TickRate * 60 * 5, InfectionTicks = TickRate * 60 * 20, CaptiveTicks = TickRate * 60 * 3;
    public const double RescueReach = 2.5, LootReach = 2.5;
    static readonly string[] Titles = ["the Butcher", "the Collector", "Grey Hand", "the Choirmaster", "Hollow-Eyed", "the Warden"];
    public List<(int Peer, string Reason)> PendingKicks { get; } = new();

    public Fact AddFact(string kind, string subject, string obj, double x, double z, string provenance = "world")
    {
        var fact = new Fact(State.Facts.Count, State.Tick, kind, subject, obj, Math.Round(x, 1), Math.Round(z, 1), provenance);
        State.Facts.Add(fact);
        OnFact(fact);
        return fact;
    }

    Dictionary<string, int>? HolderInventory(string holder)
    {
        if (holder.StartsWith("actor:")) return State.Characters.Single(c => c.Id == holder).Inventory;
        if (holder.StartsWith("npc:")) return NamedFor(uint.Parse(holder[4..])).Inventory;
        if (holder.StartsWith("cache:")) return State.Caches.Single(k => k.Id == int.Parse(holder[6..])).Inventory;
        if (holder == "vgm") return null;
        throw new InvalidOperationException("Unknown holder " + holder);
    }

    public void Transfer(string from, string to, string item, int quantity, string reason)
    {
        if (quantity <= 0) return;
        var source = HolderInventory(from);
        if (source is not null)
        {
            if (source.GetValueOrDefault(item) < quantity) throw new InvalidOperationException($"{from} lacks {quantity} {item}");
            if ((source[item] -= quantity) == 0) source.Remove(item);
        }
        var target = HolderInventory(to)!;
        target[item] = target.GetValueOrDefault(item) + quantity;
        State.Transfers.Add(new ItemTransfer(State.Tick, from, to, item, quantity, reason));
    }

    void TransferAll(string from, string to, string reason)
    {
        foreach (var (item, amount) in HolderInventory(from)!.ToArray()) Transfer(from, to, item, amount, reason);
    }

    NamedNpc NamedFor(uint index)
    {
        var named = State.Named.FirstOrDefault(n => n.Index == index);
        if (named is null) State.Named.Add(named = new NamedNpc { Index = index });
        return named;
    }

    public string DisplayName(Npc npc)
    {
        var named = State.Named.FirstOrDefault(n => n.Index == npc.Index && n.Title.Length > 0);
        return named is null ? npc.Name : $"{npc.Name} {named.Title}";
    }

    LootCache NewCache(double x, double z, string label)
    {
        var cache = new LootCache { Id = State.Caches.Count == 0 ? 0 : State.Caches.Max(k => k.Id) + 1, X = Math.Round(x, 2), Z = Math.Round(z, 2), Label = label };
        State.Caches.Add(cache);
        return cache;
    }

    int PeerOf(CharacterRecord c) => active.FirstOrDefault(a => a.Value == c).Key;

    void Defeat(CharacterRecord player, Npc victor)
    {
        AddFact("defeat", player.Id, $"npc:{victor.Index}", player.X, player.Z);
        player.Stats[CharacterRecord.Health] = 50;
        player.Conditions[Injured] = InjuredTicks;
        if (State.Permadeath) { Retire(player, "dead", "You died. Permadeath is on: rejoin to start a new character."); return; }
        if (victor.Zombie)
        {
            player.Conditions[Infected] = InfectionTicks;
            var safe = FindSafe(-3, 0, player.Id);
            player.X = safe.X; player.Z = safe.Z;
            CreateCureOpportunity(player);
            return;
        }
        // Captured: the victor takes the player's actual items and rises to a named persistent NPC.
        var named = NamedFor(victor.Index);
        if (named.Title.Length == 0) named.Title = Titles[Population.Hash4((uint)State.WorldSeed, 31, victor.Index, 0) % Titles.Length];
        named.Victories++; named.BonusHealth += 40; named.BonusDamage += 4;
        victor.Health += 40;
        TransferAll(player.Id, $"npc:{victor.Index}", "defeat");
        player.Conditions[Captive] = CaptiveTicks;
        AddFact("capture", player.Id, $"npc:{victor.Index}", player.X, player.Z);
        CreateOpportunity("rescue", player.Id, player.Id, Ammo, 6, $"{player.Name} is held by {DisplayName(victor)} near ({player.X:0}, {player.Z:0}). Free them.");
        CreateOpportunity("revenge", player.Id, $"npc:{victor.Index}", Ammo, 12,
            $"Bounty: defeat {DisplayName(victor)}, who took {player.Name}'s gear. Last seen near ({victor.X:0}, {victor.Z:0}).");
    }

    void Retire(CharacterRecord player, string status, string reason)
    {
        AddFact(status == "turned" ? "turned" : "died", player.Id, "", player.X, player.Z);
        if (player.Inventory.Count > 0) TransferAll(player.Id, $"cache:{NewCache(player.X, player.Z, $"Remains of {player.Name}").Id}", status);
        player.Status = status;
        player.Conditions.Clear();
        int peer = PeerOf(player);
        if (peer != 0) { Leave(peer); PendingKicks.Add((peer, reason)); }
        player.ProfileId = Guid.NewGuid().ToString("N");   // the profile starts a new character on its next join
    }

    // Conditions count down only while their player is online (absent players are protected).
    void StepConditions()
    {
        foreach (var player in active.Values.ToArray())
            foreach (var (name, remaining) in player.Conditions.ToArray())
            {
                if (remaining > 1) { player.Conditions[name] = remaining - 1; continue; }
                player.Conditions.Remove(name);
                if (name == Captive) { AddFact("escaped", player.Id, "", player.X, player.Z); Expire("rescue", player.Id); }
                if (name == Infected) Retire(player, "turned", "The infection took you. Rejoin to start a new character.");
            }
    }

    public bool IsCaptive(CharacterRecord c) => c.Conditions.ContainsKey(Captive);

    // A free player within reach releases the nearest captive and claims the rescue opportunity.
    public string Rescue(int peer)
    {
        var rescuer = CharacterFor(peer);
        if (IsCaptive(rescuer)) return "captive";
        var captive = active.Values.Where(c => c != rescuer && IsCaptive(c) && Distance(c.X, c.Z, rescuer.X, rescuer.Z) <= RescueReach)
            .OrderBy(c => Distance(c.X, c.Z, rescuer.X, rescuer.Z)).FirstOrDefault();
        if (captive is null) return "nobody";
        captive.Conditions.Remove(Captive);
        AddFact("rescued", captive.Id, rescuer.Id, captive.X, captive.Z);
        Claim("rescue", captive.Id, rescuer);
        return "rescued";
    }

    // Looting takes everything from the nearest cache once; an antiviral cures infection on pickup.
    public string Loot(int peer)
    {
        var c = CharacterFor(peer);
        var cache = State.Caches.Where(k => k.Inventory.Count > 0 && Distance(k.X, k.Z, c.X, c.Z) <= LootReach).OrderBy(k => Distance(k.X, k.Z, c.X, c.Z)).FirstOrDefault();
        if (cache is null) return "nothing";
        TransferAll($"cache:{cache.Id}", c.Id, "loot");
        AddFact("looted", c.Id, $"cache:{cache.Id}", cache.X, cache.Z);
        if (c.Conditions.ContainsKey(Infected) && c.Inventory.GetValueOrDefault(Antiviral) > 0)
        {
            if ((c.Inventory[Antiviral] -= 1) == 0) c.Inventory.Remove(Antiviral);
            c.Conditions.Remove(Infected);
            AddFact("cured", c.Id, "", c.X, c.Z);
            Claim("cure", c.Id, c);
        }
        return "looted";
    }

    void OnKilled(CharacterRecord killer, Npc npc)
    {
        AddFact("kill", killer.Id, $"npc:{npc.Index}", npc.X, npc.Z);
        var named = State.Named.FirstOrDefault(n => n.Index == npc.Index);
        if (named is null) return;
        TransferAll($"npc:{npc.Index}", killer.Id, "avenged");   // the victor's carried gear, including the victim's
        foreach (var o in State.Opportunities.Where(o => o.Status == "open" && o.Kind == "revenge" && o.Target == $"npc:{npc.Index}").ToArray())
            Claim(o, killer);
    }

    // ---- Virtual Game Master: deterministic rules over facts; creations are committed, shared and claimable once. ----

    Opportunity CreateOpportunity(string kind, string subject, string target, string rewardItem, int reward, string text)
    {
        var o = new Opportunity
        {
            Id = State.Opportunities.Count == 0 ? 0 : State.Opportunities.Max(x => x.Id) + 1, Kind = kind, Subject = subject, Target = target,
            RewardItem = rewardItem, RewardQuantity = reward, CreatedTick = State.Tick, Text = text
        };
        State.Opportunities.Add(o);
        AddFact("opportunity", subject, $"opportunity:{o.Id}", 0, 0, "vgm");
        return o;
    }

    void CreateCureOpportunity(CharacterRecord player)
    {
        // Story-time invention: an antiviral stash appears at a real, reachable spot 150-400 m from the starter room.
        uint h = Population.Hash4((uint)State.WorldSeed, 41, (uint)State.Tick, (uint)State.Facts.Count);
        double angle = h % 360 * Math.PI / 180, distance = 150 + Population.Mix(h) % 250;
        double x = Math.Cos(angle) * distance, z = Math.Sin(angle) * distance;
        FindWalkable(ref x, ref z);
        var cache = NewCache(x, z, "Clinic stash");
        Transfer("vgm", $"cache:{cache.Id}", Antiviral, 1, "story");
        string direction = (Math.Abs(x) > Math.Abs(z) ? (x > 0 ? "east" : "west") : (z > 0 ? "south" : "north"));
        CreateOpportunity("cure", player.Id, $"cache:{cache.Id}", "", 0,
            $"A Union medic heard of antiviral in a clinic stash about {distance:0} m {direction} of the plaza, near ({x:0}, {z:0}). Find it before the infection turns you.");
    }

    void Claim(string kind, string subject, CharacterRecord claimant)
    {
        foreach (var o in State.Opportunities.Where(o => o.Status == "open" && o.Kind == kind && o.Subject == subject).ToArray()) Claim(o, claimant);
    }

    void Claim(Opportunity o, CharacterRecord claimant)
    {
        if (o.Status != "open") return;   // single claim
        o.Status = "claimed"; o.ClaimedBy = claimant.Id;
        if (o.RewardQuantity > 0) Transfer("vgm", claimant.Id, o.RewardItem, o.RewardQuantity, $"reward:{o.Id}");
        AddFact("claimed", claimant.Id, $"opportunity:{o.Id}", claimant.X, claimant.Z, "vgm");
    }

    void Expire(string kind, string subject)
    {
        foreach (var o in State.Opportunities.Where(o => o.Status == "open" && o.Kind == kind && o.Subject == subject)) o.Status = "expired";
    }

    // Keep revenge bounties pointing at where the victor actually is (coarse position, rounded).
    void RefreshOpportunities()
    {
        foreach (var o in State.Opportunities.Where(o => o.Status == "open" && o.Kind == "revenge"))
        {
            uint i = uint.Parse(o.Target[4..]);
            if (Pop is null || i >= Pop.Count) continue;
            if ((Pop.Info[i] & Population.Dead) != 0) { o.Status = "expired"; continue; }
            double x = npcs.TryGetValue(i, out var n) ? n.X : Population.ToMetres(Pop.PosX[i]);
            double z = npcs.TryGetValue(i, out n) ? n.Z : Population.ToMetres(Pop.PosZ[i]);
            int cut = o.Text.LastIndexOf(" Last seen near ", StringComparison.Ordinal);
            if (cut >= 0) o.Text = o.Text[..cut] + $" Last seen near ({Math.Round(x / 50) * 50:0}, {Math.Round(z / 50) * 50:0}).";
        }
    }

    partial void OnFact(Fact fact);
}
