namespace QuestSim.Domain;

// Disposable, deterministic ground-plane fixture; no scene objects or server rules.
// Units are metres. It does not decide future world structure or verticality.
public readonly record struct ActorPose(string Id, double X, double Y);

public sealed class PresentationScenario
{
    public double ElapsedSeconds { get; private set; }

    public void Step(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 ||
            !double.IsFinite(ElapsedSeconds + seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        ElapsedSeconds += seconds;
    }

    // Four actors cross the same two-metre doorway at x=0, y=-1..1.
    // Deliberate overlap makes occlusion visible. No collision/AI claim.
    public ActorPose[] GetActors() => Enumerable.Range(0, 4)
        .Select(i => new ActorPose($"probe:actor-{i}",
            4 * Math.Sin(ElapsedSeconds * 0.5 + i * 0.6), (i - 1.5) * 0.4))
        .ToArray();
}
