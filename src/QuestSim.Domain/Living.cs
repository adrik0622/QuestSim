namespace QuestSim.Domain;

// Detailed NPC owned by the 20 Hz authority while a player is near; its slot in Population is flagged Detailed.
public sealed class Npc
{
    public required uint Index { get; init; }
    public required bool Zombie { get; init; }
    public required int Faction { get; init; }
    public required string Name { get; init; }
    public double X, Z, Heading, WanderX, WanderZ;
    public int Health, Cooldown;
    public bool Dead, Wandering;
}

public sealed partial class WorldAuthority
{
    public const int PopulationEvery = TickRate, MaxDetailed = 400, MeleeCooldown = 12, ShotCooldown = 8;
    public const double PromoteRadius = 120, DemoteRadius = 180, ViewRadius = 120, MeleeReach = 1.8, ShotRange = 40;
    public const string Pistol = "questsim:pistol", Ammo = "questsim:ammo-9mm";
    readonly bool synchronous;
    readonly Dictionary<uint, Npc> npcs = new();
    readonly Dictionary<string, int> attackCooldown = new();
    Task? populationTask;
    public Population? Pop { get; private set; }
    public IReadOnlyDictionary<uint, Npc> Npcs => npcs;
    public long PopulationLagged { get; private set; }
    public Action<CharacterRecord, Npc>? Downed;

    void StartPopulation()
    {
        Pop = new Population(City, State.Population);
        foreach (var d in State.AgentDeltas.Where(d => d.Dead)) Pop.Info[d.Index] |= Population.Dead;
    }

    void StepLiving()
    {
        foreach (var key in attackCooldown.Keys.ToArray()) if (--attackCooldown[key] <= 0) attackCooldown.Remove(key);
        StepConditions();
        StepNpcs();
        if (Pop is not null && State.Tick % PopulationEvery == 0) { StepPopulation(); RefreshOpportunities(); }
    }

    // The coarse tick runs on a background task; tiers change only while it is idle.
    void StepPopulation()
    {
        if (populationTask is { IsCompleted: false }) { PopulationLagged++; return; }
        populationTask?.GetAwaiter().GetResult();
        ReportLosses();
        ApplyPossessions();
        SyncTiers();
        PrepareRumors();
        if (PopulationSaveRequested) CapturePopulation();
        var rumors = RumorInput();
        if (synchronous) Pop!.Step(rumors);
        else populationTask = Task.Run(() => Pop!.Step(rumors));
    }

    public void WaitForPopulation() => populationTask?.GetAwaiter().GetResult();

    void SyncTiers()
    {
        var pop = Pop!;
        var players = active.Values.ToArray();
        foreach (var npc in npcs.Values.ToArray())
        {
            uint i = npc.Index;
            pop.PosX[i] = pop.TgtX[i] = Population.ToMm(npc.X);
            pop.PosZ[i] = pop.TgtZ[i] = Population.ToMm(npc.Z);
            if (npc.Dead) pop.Info[i] |= Population.Dead;
            if (npc.Dead || players.All(p => Distance(p.X, p.Z, npc.X, npc.Z) > DemoteRadius))
            {
                pop.Info[i] &= ~Population.Detailed;
                npcs.Remove(i);
            }
        }
        // Share the detail budget fairly: take each player's nearest candidates in turn.
        var queues = players.Select(p => new Queue<uint>(pop.Near(p.X, p.Z, PromoteRadius)
            .OrderBy(i => Distance(p.X, p.Z, Population.ToMetres(pop.PosX[i]), Population.ToMetres(pop.PosZ[i]))).ThenBy(i => i))).ToArray();
        while (npcs.Count < MaxDetailed && queues.Any(q => q.Count > 0))
            foreach (var queue in queues)
            {
                if (npcs.Count >= MaxDetailed || !queue.TryDequeue(out uint i)) continue;
                if ((pop.Info[i] & (Population.Detailed | Population.Dead)) != 0) continue;
                double x = Population.ToMetres(pop.PosX[i]), z = Population.ToMetres(pop.PosZ[i]);
                if (!FindWalkable(ref x, ref z)) continue;   // coarse agents ignore buildings; step out of one or stay coarse
                var delta = State.AgentDeltas.FirstOrDefault(d => d.Index == i);
                npcs[i] = new Npc
                {
                    Index = i, Zombie = Population.IsZombie(pop.Info[i]), Faction = Population.FactionOf(pop.Info[i]), Name = pop.NameOf(i),
                    X = x, Z = z, Health = delta?.Health ?? pop.BaseHealth(i) + (State.Named.FirstOrDefault(n => n.Index == i)?.BonusHealth ?? 0)
                };
                pop.Info[i] |= Population.Detailed;
            }
    }

    bool FindWalkable(ref double x, ref double z)
    {
        if (Walkable(x, z)) return true;
        for (int r = 2; r <= 12; r += 2)
            for (int k = 0; k < 8; k++)
            {
                double nx = x + r * Math.Cos(k * Math.PI / 4), nz = z + r * Math.Sin(k * Math.PI / 4);
                if (Walkable(nx, nz)) { x = nx; z = nz; return true; }
            }
        return false;
    }

    static double Distance(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    bool Walkable(double x, double z) =>
        Math.Abs(x) <= State.HalfExtent - Radius && Math.Abs(z) <= State.HalfExtent - Radius &&
        State.Blocks.All(b => Clear(x, z, b)) && ClearOfBuildings(x, z) &&
        State.Supplies.All(s => Math.Abs(x - s.X) >= .3 + Radius || Math.Abs(z - s.Z) >= .3 + Radius);

    bool PointBlocked(double x, double z) =>
        State.Blocks.Any(b => Math.Abs(x - b.X) < b.Width / 2 && Math.Abs(z - b.Z) < b.Depth / 2) || !ClearOfBuildingsAt(x, z, 0);

    bool ClearOfBuildingsAt(double x, double z, double margin)
    {
        int cx = (int)Math.Floor(x / CityGen.ChunkSize), cz = (int)Math.Floor(z / CityGen.ChunkSize);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                foreach (var b in City.Chunk(cx + dx, cz + dz))
                    if (Math.Abs(x - b.X) < b.Width / 2.0 + margin && Math.Abs(z - b.Z) < b.Depth / 2.0 + margin) return false;
        return true;
    }

    void StepNpcs()
    {
        var players = active.Values.ToArray();
        foreach (var npc in npcs.Values)
        {
            if (npc.Dead) continue;
            if (npc.Cooldown > 0) npc.Cooldown--;
            CharacterRecord? prey = null;
            if (IsHostile(npc))
            {
                double sight = npc.Zombie ? 18 : 25;
                prey = players.Where(p => !IsCaptive(p) && Distance(p.X, p.Z, npc.X, npc.Z) <= sight).OrderBy(p => Distance(p.X, p.Z, npc.X, npc.Z)).ThenBy(p => p.Id).FirstOrDefault();
            }
            if (prey is null && !npc.Dead)
            {
                // Factions act on each other: zombies and humans fight, the Choir fights everyone, rival hordes clash.
                var rival = npcs.Values.Where(r => !r.Dead && r != npc && Enemies(npc, r) && Distance(r.X, r.Z, npc.X, npc.Z) <= 6)
                    .OrderBy(r => Distance(r.X, r.Z, npc.X, npc.Z)).ThenBy(r => r.Index).FirstOrDefault();
                if (rival is not null)
                {
                    if (Distance(rival.X, rival.Z, npc.X, npc.Z) > 1.1) MoveNpc(npc, rival.X, rival.Z, npc.Zombie ? 1.6 : 2.1);
                    else if (npc.Cooldown == 0)
                    {
                        npc.Cooldown = 24;
                        rival.Health = Math.Max(0, rival.Health - 8);
                        rival.Dead = rival.Health == 0;
                        RecordDelta(rival.Index, rival.Health, rival.Dead);
                        if (rival.Dead)
                        {
                            AddFact(npc.Zombie && rival.Zombie ? "horde-clash" : "clash", $"npc:{npc.Index}", $"npc:{rival.Index}", rival.X, rival.Z);
                            DropCarried(rival);
                        }
                    }
                    continue;
                }
            }
            if (prey is not null)
            {
                double d = Distance(prey.X, prey.Z, npc.X, npc.Z);
                if (d <= 1.1)
                {
                    if (npc.Cooldown == 0) { npc.Cooldown = 24; DamagePlayer(prey, (npc.Zombie ? 10 : 8) + (State.Named.FirstOrDefault(n => n.Index == npc.Index)?.BonusDamage ?? 0), npc); }
                }
                else MoveNpc(npc, prey.X, prey.Z, npc.Zombie ? 1.6 : 2.1);
                continue;
            }
            if (!npc.Wandering || Distance(npc.X, npc.Z, npc.WanderX, npc.WanderZ) < .3)
            {
                uint h = Population.Hash4((uint)State.WorldSeed, (uint)State.Tick, npc.Index, 21);
                npc.WanderX = npc.X + (h % 1201) / 100.0 - 6;
                npc.WanderZ = npc.Z + (Population.Mix(h) % 1201) / 100.0 - 6;
                npc.Wandering = true;
            }
            if (!MoveNpc(npc, npc.WanderX, npc.WanderZ, .8)) npc.Wandering = false;
        }
    }

    static bool Enemies(Npc a, Npc b) =>
        a.Zombie != b.Zombie ||                                                         // the living and the dead
        (a.Zombie && b.Zombie && a.Faction != b.Faction) ||                             // rival hordes
        (!a.Zombie && !b.Zombie && (a.Faction == Population.Hostile) != (b.Faction == Population.Hostile));   // the Choir vs everyone

    bool MoveNpc(Npc npc, double tx, double tz, double speed)
    {
        double dx = tx - npc.X, dz = tz - npc.Z, d = Math.Sqrt(dx * dx + dz * dz);
        if (d < 1e-6) return false;
        double step = Math.Min(d, speed / TickRate), mx = dx / d * step, mz = dz / d * step;
        npc.Heading = Math.Atan2(-dx, -dz);
        bool moved = false;
        if (Walkable(npc.X + mx, npc.Z)) { npc.X += mx; moved = true; }
        if (Walkable(npc.X, npc.Z + mz)) { npc.Z += mz; moved = true; }
        return moved;
    }

    void DamagePlayer(CharacterRecord player, int damage, Npc attacker)
    {
        int health = Math.Max(0, player.Stat(CharacterRecord.Health) - damage);
        player.Stats[CharacterRecord.Health] = health;
        if (health > 0) return;
        if (Downed is not null) { Downed(player, attacker); return; }
        Defeat(player, attacker);
    }

    IEnumerable<ActorState> NpcsVisibleTo(CharacterRecord? viewer, bool all) =>
        npcs.Values.Where(n => !n.Dead && (all || viewer is not null && Distance(viewer.X, viewer.Z, n.X, n.Z) <= ViewRadius))
            .OrderBy(n => n.Index)
            .Select(n => new ActorState($"npc:{n.Index}", DisplayName(n), n.X, n.Z, n.Heading, n.Zombie ? 2 : 1, n.Faction, n.Health));

    // Melee hits the nearest living NPC in a 120° arc within reach; the pistol is a hitscan ray stopped by walls.
    public AttackResult Attack(int peer, string kind)
    {
        var c = CharacterFor(peer);
        if (kind is not ("melee" or "shoot")) throw new ArgumentException("Unknown attack");
        if (IsCaptive(c)) return new(kind, false, "", 0, false, "captive");
        if (attackCooldown.ContainsKey(c.Id)) return new(kind, false, "", 0, false, "cooldown");
        double fx = -Math.Sin(c.Heading), fz = -Math.Cos(c.Heading);
        Npc? target;
        int damage;
        if (kind == "melee")
        {
            attackCooldown[c.Id] = MeleeCooldown;
            target = npcs.Values.Where(n => !n.Dead && Distance(c.X, c.Z, n.X, n.Z) <= MeleeReach &&
                    ((n.X - c.X) * fx + (n.Z - c.Z) * fz) >= .5 * Distance(c.X, c.Z, n.X, n.Z))
                .OrderBy(n => Distance(c.X, c.Z, n.X, n.Z)).ThenBy(n => n.Index).FirstOrDefault();
            damage = 12 + c.Stat(CharacterRecord.Strength) - (c.Conditions.ContainsKey(Injured) ? 4 : 0);
        }
        else
        {
            if (c.Inventory.GetValueOrDefault(Pistol) < 1) return new(kind, false, "", 0, false, "no_weapon");
            if (c.Inventory.GetValueOrDefault(Ammo) < 1) return new(kind, false, "", 0, false, "no_ammo");
            attackCooldown[c.Id] = ShotCooldown;
            c.Inventory[Ammo]--;
            if (c.Inventory[Ammo] == 0) c.Inventory.Remove(Ammo);
            target = null;
            double best = ShotRange;
            foreach (var n in npcs.Values.Where(n => !n.Dead))
            {
                double rx = n.X - c.X, rz = n.Z - c.Z, along = rx * fx + rz * fz, perp = Math.Abs(rx * fz - rz * fx);
                if (along > 0 && along <= best && perp <= .45) { best = along; target = n; }
            }
            for (double t = .5; target is not null && t < best; t += .5)
                if (PointBlocked(c.X + fx * t, c.Z + fz * t)) return new(kind, true, "", 0, false, "blocked");
            damage = 30 + c.Stat(CharacterRecord.Perception) / 2;
        }
        if (target is null) return new(kind, kind == "shoot", "", 0, false, "miss");
        target.Health = Math.Max(0, target.Health - damage);
        target.Dead = target.Health == 0;
        RecordDelta(target.Index, target.Health, target.Dead);
        if (target.Dead) OnKilled(c, target);
        return new(kind, kind == "shoot", $"npc:{target.Index}", damage, target.Dead, target.Dead ? "killed" : "hit");
    }

    void RecordDelta(uint index, int health, bool dead)
    {
        State.AgentDeltas.RemoveAll(d => d.Index == index);
        State.AgentDeltas.Add(new AgentDelta(index, health, dead));
    }
}
