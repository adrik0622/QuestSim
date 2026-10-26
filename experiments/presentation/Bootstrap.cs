using Godot;
using QuestSim.Domain;

public partial class Bootstrap : Node
{
    private readonly PresentationScenario scenario = new();
    private readonly Label status = new();
    private int presses;

    public override void _Ready()
    {
        DisplayServer.WindowSetTitle("QuestSim Presentation Lab");
        status.Position = new Vector2(32, 32);
        AddChild(status);
        UpdateStatus();
        GD.Print("QUESTSIM_BOOTSTRAP_READY actors=", scenario.GetActors().Length);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
        {
            scenario.Step(1);
            presses++;
            UpdateStatus();
            GD.Print("QUESTSIM_PROBE_SPACE ", presses);
        }
    }

    private void UpdateStatus() => status.Text =
        "QuestSim presentation lab — launch scaffold\n" +
        "No presentation selected or compared yet.\n" +
        $"Space: controlled input probe ({presses}); fixture time {scenario.ElapsedSeconds:0}s";
}
