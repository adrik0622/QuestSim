using QuestSim.Domain;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS {description}");
}

var scenario = new PresentationScenario();
var initial = scenario.GetActors();
scenario.Step(0);
Check(initial.SequenceEqual(scenario.GetActors()), "zero step preserves fixture");
foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
{
    bool rejected = false;
    try { scenario.Step(invalid); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected && scenario.ElapsedSeconds == 0, $"invalid delta {invalid} rejected without mutation");
}
var crossed = new HashSet<string>();
var previous = initial;
for (int i = 0; i < 1200; i++)
{
    scenario.Step(1.0 / 60);
    var current = scenario.GetActors();
    for (int j = 0; j < current.Length; j++)
        if (Math.Sign(previous[j].X) != Math.Sign(current[j].X)) crossed.Add(current[j].Id);
    previous = current;
}
Check(crossed.Count == 4 && previous.Select(a => a.Id).SequenceEqual(initial.Select(a => a.Id)),
    "four stable actor IDs cross doorway during 20 simulated seconds");
Check(previous.All(a => double.IsFinite(a.X) && Math.Abs(a.X) <= 4 && Math.Abs(a.Y) < 1),
    "fixture remains within shared room and doorway bounds");
var replay = new PresentationScenario();
for (int i = 0; i < 1200; i++) replay.Step(1.0 / 60);
Check(previous.SequenceEqual(replay.GetActors()), "same steps reproduce identical fixture poses");
Console.WriteLine("Offline domain fixture only; no engine, collision, networking or persistence verified.");
AuthorityChecks.Run(Check);
PickupChecks.Run(Check);
BuildChecks.Run(Check);
CityChecks.Run(Check);
LivingChecks.Run(Check);
DefeatChecks.Run(Check);
RumorChecks.Run(Check);
ContentChecks.Run(Check);
AcceptanceChecks.Run(Check);
WireChecks.Run(Check);
EconomyChecks.Run(Check);
PoliticsChecks.Run(Check);
