using Godot;
using QuestSim.Domain;

// Client-only scene objects. The server never constructs this view.
public partial class RoomView : Node3D
{
    public float Yaw { get; private set; } = -Mathf.Pi / 2;
    private float elevation = .65f;
    private bool firstPerson;
    private readonly Camera3D camera = new();
    private readonly Label hud = new();
    private readonly Dictionary<string, Node3D> actors = new();
    private bool roomBuilt;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.12f, .17f, .23f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .65f
        } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -35, 0), ShadowEnabled = true });
        AddChild(camera); camera.Current = true; camera.Fov = 75;
        camera.Position = new Vector3(-8, 6, 0); camera.LookAt(new Vector3(0, 1, 0));
        var layer = new CanvasLayer(); AddChild(layer);
        layer.AddChild(new ColorRect { Color = new Color(.03f, .04f, .06f, .9f), Size = new Vector2(960, 100), MouseFilter = Control.MouseFilterEnum.Ignore });
        hud.Position = new Vector2(12, 8); hud.AddThemeFontSizeOverride("font_size", 17); layer.AddChild(hud);
    }
    private static MeshInstance3D Box(Node3D parent, Vector3 p, Vector3 size, Color color)
    {
        var box = new MeshInstance3D { Position = p, Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .9f } };
        parent.AddChild(box); return box;
    }
    private void BuildRoom(WorldSnapshot state)
    {
        float side = (float)state.HalfExtent * 2;
        Box(this, new Vector3(0, -.1f, 0), new Vector3(side, .2f, side), new Color(.25f, .28f, .3f));
        foreach (var block in state.Blocks)
            Box(this, new Vector3((float)block.X, (float)block.Height / 2, (float)block.Z),
                new Vector3((float)block.Width, (float)block.Height, (float)block.Depth),
                block.Id == "questsim:module" ? new Color(1, .8f, .15f) : new Color(.55f, .56f, .6f));
        roomBuilt = true;
    }
    private Node3D CreateActor(ActorState state, bool local)
    {
        var actor = new Node3D { Position = new Vector3((float)state.X, 0, (float)state.Z) };
        AddChild(actor);
        Color shirt = local ? new Color(.3f, .65f, 1) : new Color(.95f, .6f, .35f);
        Box(actor, new Vector3(0, 1.25f, 0), new Vector3(.48f, .6f, .28f), shirt);
        Box(actor, new Vector3(0, 1.75f, 0), new Vector3(.36f, .36f, .32f), new Color(1, .8f, .6f));
        Box(actor, new Vector3(0, 1.75f, -.2f), new Vector3(.12f, .12f, .1f), Colors.Black);
        foreach (int side in new[] { -1, 1 })
        {
            Box(actor, new Vector3(side * .34f, 1.2f, 0), new Vector3(.15f, .6f, .16f), shirt);
            Box(actor, new Vector3(side * .14f, .5f, 0), new Vector3(.18f, .9f, .2f), new Color(.18f, .2f, .25f));
        }
        actor.AddChild(new Label3D { Text = state.Name, Position = new Vector3(0, 2.2f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = .005f });
        actors.Add(state.Id, actor);
        return actor;
    }
    public void ClearActors()
    {
        foreach (var actor in actors.Values) actor.QueueFree();
        actors.Clear();
    }
    public void UpdateWorld(WorldSnapshot? state, string ownId, string profile, string connection, double delta)
    {
        Yaw += ((Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0)) * (float)delta;
        elevation = Mathf.Clamp(elevation + ((Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0)) * (float)delta, -.4f, 1.2f);
        hud.Text = $"{profile} — {connection} | {(firstPerson ? "first" : "third")} person\n" +
            "WASD move | Q/E turn/orbit | Up/Down tilt | C camera | R reset camera\n" +
            $"Present: {state?.Actors.Length ?? 0} | World time: {(state?.Tick ?? 0) / (double)WorldAuthority.TickRate:0.0}s\n" +
            "Blue: you. Orange: another player. Close window to disconnect; relaunch same profile to return.";
        if (state is null) return;
        if (!roomBuilt) BuildRoom(state);
        foreach (string id in actors.Keys.Except(state.Actors.Select(a => a.Id)).ToArray()) { actors[id].QueueFree(); actors.Remove(id); }
        foreach (var a in state.Actors)
        {
            bool local = a.Id == ownId;
            if (!actors.TryGetValue(a.Id, out var actor)) actor = CreateActor(a, local);
            actor.Position = actor.Position.Lerp(new Vector3((float)a.X, 0, (float)a.Z), 1 - Mathf.Exp(-15 * (float)delta));
            actor.Rotation = new Vector3(0, (float)a.Heading, 0);
            actor.Visible = !local || !firstPerson;
            if (!local) continue;
            Vector3 eye = actor.Position + new Vector3(0, 1.65f, 0);
            if (firstPerson) { camera.Position = eye; camera.Rotation = new Vector3(.65f - elevation, Yaw, 0); }
            else
            {
                camera.Position = eye + new Vector3(Mathf.Sin(Yaw) * 6 * Mathf.Cos(elevation), 6 * Mathf.Sin(elevation), Mathf.Cos(Yaw) * 6 * Mathf.Cos(elevation));
                camera.LookAt(eye);
            }
        }
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.C) firstPerson = !firstPerson;
        if (key.Keycode == Key.R) { Yaw = -Mathf.Pi / 2; elevation = .65f; }
    }
}
