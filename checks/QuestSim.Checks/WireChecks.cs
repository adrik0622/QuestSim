using QuestSim.Domain;
using static LivingChecks;

public static class WireChecks
{
    public static void Run(Action<bool, string> check)
    {
        var w = new WorldAuthority(new WorldSave { Population = 40_000 }, synchronousPopulation: true) { PromotionClearance = 0 };
        var a = w.Join(2, new string('a', 32), "Alice");
        a.X = 3.123456789; a.Z = 20.987654321;
        Steps(w, WorldAuthority.PopulationEvery);
        var cultist = Summon(w, false, a.X + 3, a.Z, faction: Population.Hostile);
        w.State.Named.Add(new NamedNpc { Index = cultist.Index, Title = "the Butcher" });
        var snapshot = w.Snapshot(2);
        var bytes = Wire.EncodeActors(snapshot.Sequence, snapshot.Tick, snapshot.Actors, i => w.State.Named.Any(n => n.Index == i && n.Title.Length > 0));
        var (sequence, tick, actors) = Wire.DecodeActors(bytes, w.State.WorldSeed);
        var player = actors.Single(x => x.Kind == 0);
        var original = snapshot.Actors.Single(x => x.Kind == 0);
        int npcs = actors.Count(x => x.Kind != 0);
        check(sequence == snapshot.Sequence && tick == snapshot.Tick && player == original && npcs == snapshot.Actors.Length - 1 &&
              actors.Zip(snapshot.Actors).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.Kind == p.Second.Kind &&
                  p.First.Faction == p.Second.Faction && p.First.Health == p.Second.Health && Math.Abs(p.First.X - p.Second.X) < .01 && Math.Abs(p.First.Z - p.Second.Z) < .01) &&
              actors.Any(x => x.Name.EndsWith("the Butcher")),
            $"binary actor packets round-trip: players exact, NPCs within 1 cm, names regenerated from the seed, titles sent ({npcs} NPCs in {bytes.Length:N0} bytes, ~{(bytes.Length - 60.0) / npcs:F1} B/NPC)");
        var crowd = CrowdWire.Decode(w.CrowdPackets[2]);
        check(crowd.Length > 0 && crowd.All(f => !w.Npcs.ContainsKey(f.Index)) &&
              crowd.All(f => Math.Sqrt((f.X - a.X) * (f.X - a.X) + (f.Z - a.Z) * (f.Z - a.Z)) <= WorldAuthority.CrowdRadius + .1) &&
              crowd.All(f => Math.Abs(Population.ToMetres(w.Pop!.PosX[f.Index]) - f.X) < 1.5 && Population.IsZombie(w.Pop.Info[f.Index]) == f.Zombie),
            $"crowd placeholders carry nearby background people only, never ones already in detail ({crowd.Length} figures in {w.CrowdPackets[2].Length:N0} bytes)");
    }
}
