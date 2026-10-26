using Godot;
using QuestSim.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

// Disposable comparison scene. Branches deliberately use native 2D/3D APIs directly.
public partial class PresentationLab : Node
{
    private readonly PresentationScenario scenario = new();
    private string mode = "2d", view = "overhead", report = "";
    private Node3D space = null!;
    private Camera3D camera = null!;
    private Node3D[] actors = new Node3D[5];
    private readonly List<(Node3D pivot, int actor, float phase)> limbs = new();
    private ImageTexture sprite = null!, topSprite = null!;
    private OverheadRoom overhead = null!;
    private StaticBody3D pieceBody = null!;
    private MeshInstance3D ghost = null!;
    private readonly Label hud = new();
    private Vector2 player = new(-3, 0), piece = new(-2, 2), candidate;
    private float yaw = -Mathf.Pi / 2, elevation = .65f;
    private bool selected, benchmark;
    private double elapsed;
    private int selectedCount, placedCount, rejectedCount, blockedMoves;
    private readonly List<double> frames = new();
    private string message = "Click the yellow construction piece to select it.";
    private ActorPose[] poses = Array.Empty<ActorPose>();
    private const float Scale = 32;
    private static readonly Vector2 Origin = new(480, 330);

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--mode=")) mode = arg[7..];
            if (arg.StartsWith("--camera=")) view = arg[9..];
            if (arg.StartsWith("--report=")) report = arg[9..];
            if (arg == "--benchmark") benchmark = true;
        }
        if (mode is not ("2d" or "sprite3d" or "poly3d")) throw new ArgumentException("Unknown mode");
        if (mode == "2d") view = "overhead";
        else if (view is not ("first" or "third")) view = "third";
        DisplayServer.WindowSetTitle("QuestSim Presentation Lab");
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        sprite = MakeSprite(false);
        topSprite = MakeSprite(true);
        poses = scenario.GetActors();
        if (mode == "2d")
        {
            overhead = new OverheadRoom { Lab = this };
            AddChild(overhead);
        }
        else Build3D();
        var layer = new CanvasLayer();
        AddChild(layer);
        var background = new ColorRect { Color = new Color(.03f, .04f, .06f, .9f), Size = new Vector2(960, 104) };
        background.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(background);
        hud.Position = new Vector2(12, 8);
        hud.AddThemeFontSizeOverride("font_size", 16);
        layer.AddChild(hud);
        UpdateVisuals();
        GD.Print($"QUESTSIM_LAB_READY mode={mode} camera={view}");
        PrintState("ready");
    }

    private static ImageTexture MakeSprite(bool top)
    {
        int h = top ? 32 : 48;
        var image = Image.CreateEmpty(64, h, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (int f = 0; f < 2; f++)
        {
            int x = 32 * f;
            if (top)
            {
                image.FillRect(new Rect2I(x + 5, 10, 22, 12), Colors.White);
                image.FillRect(new Rect2I(x + 11, 7, 10, 10), new Color(1, .8f, .6f));
                image.FillRect(new Rect2I(x + 22, 3 + f * 3, 4, 12), new Color(.3f, .2f, .12f));
            }
            else
            {
                image.FillRect(new Rect2I(x + 10, 3, 12, 12), new Color(1, .8f, .6f));
                image.FillRect(new Rect2I(x + 12, 7, 2, 2), Colors.Black);
                image.FillRect(new Rect2I(x + 19, 7, 2, 2), Colors.Black);
                image.FillRect(new Rect2I(x + 8, 15, 16, 19), Colors.White);
                image.FillRect(new Rect2I(x + 3, 17 + f * 2, 5, 17), Colors.White);
                image.FillRect(new Rect2I(x + 24, 17 - f * 2, 5, 17), Colors.White);
                image.FillRect(new Rect2I(x + 9, 34, 6, 11 - f * 3), new Color(.3f, .3f, .4f));
                image.FillRect(new Rect2I(x + 18, 34, 6, 8 + f * 3), new Color(.3f, .3f, .4f));
                image.FillRect(new Rect2I(x + 27, 20 - f * 2, 3, 18), new Color(.3f, .2f, .12f));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Color ActorColor(int i) => i == 4 ? new Color(.3f, .65f, 1) :
        i < 2 ? new Color(.45f, .85f, .45f) : new Color(.95f, .6f, .35f);

    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = .9f };
    private static MeshInstance3D Box(Node3D parent, Vector3 position, Vector3 size, Color color)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = Material(color) };
        parent.AddChild(mesh);
        return mesh;
    }
    private StaticBody3D Solid(Vector3 position, Vector3 size, Color color)
    {
        var body = new StaticBody3D { Position = position };
        space.AddChild(body);
        Box(body, Vector3.Zero, size, color);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return body;
    }
    private void Build3D()
    {
        space = new Node3D();
        camera = new Camera3D();
        AddChild(space);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.12f, .17f, .23f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .65f
        };
        space.AddChild(new WorldEnvironment { Environment = environment });
        space.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -35, 0), ShadowEnabled = true });
        Solid(new Vector3(0, -.1f, 0), new Vector3(12, .2f, 12), new Color(.25f, .28f, .3f));
        foreach (float z in new[] { -3.5f, 3.5f })
            Solid(new Vector3(0, 1.4f, z), new Vector3(.25f, 2.8f, 5), new Color(.55f, .56f, .6f));
        Solid(new Vector3(0, 2.65f, 0), new Vector3(.25f, .3f, 2), new Color(.55f, .56f, .6f));
        for (int i = 0; i < 3; i++)
        {
            float h = .25f * (i + 1);
            Solid(new Vector3(1.5f + i, h / 2, -3), new Vector3(1, h, 2), new Color(.38f, .42f, .49f));
        }
        // Grid lines aid spatial/placement comparisons without adding art assets.
        for (int i = -6; i <= 6; i++)
        {
            Box(space, new Vector3(i, .005f, 0), new Vector3(.015f, .01f, 12), new Color(.36f, .38f, .4f));
            Box(space, new Vector3(0, .005f, i), new Vector3(12, .01f, .015f), new Color(.36f, .38f, .4f));
        }
        for (int i = 0; i < 5; i++)
        {
            actors[i] = new Node3D();
            space.AddChild(actors[i]);
            if (mode == "sprite3d") actors[i].AddChild(new Sprite3D
            {
                Texture = sprite, Hframes = 2, PixelSize = .04f, Position = new Vector3(0, .96f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Shaded = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Modulate = ActorColor(i)
            });
            else
            {
                Box(actors[i], new Vector3(0, 1.25f, 0), new Vector3(.48f, .6f, .28f), ActorColor(i));
                Box(actors[i], new Vector3(0, 1.75f, 0), new Vector3(.36f, .36f, .32f), new Color(1, .8f, .6f));
                // Dark nose is a cheap facing cue; limbs use simple pivot rotations.
                Box(actors[i], new Vector3(0, 1.75f, -.2f), new Vector3(.12f, .12f, .1f), Colors.Black);
                foreach (int side in new[] { -1, 1 })
                {
                    var arm = new Node3D { Position = new Vector3(side * .34f, 1.5f, 0) };
                    actors[i].AddChild(arm);
                    Box(arm, new Vector3(0, -.3f, 0), new Vector3(.15f, .6f, .16f), ActorColor(i));
                    if (side == 1) Box(arm, new Vector3(0, -.5f, -.3f), new Vector3(.08f, .08f, .65f), new Color(.3f, .2f, .12f));
                    limbs.Add((arm, i, side));
                    var leg = new Node3D { Position = new Vector3(side * .14f, .95f, 0) };
                    actors[i].AddChild(leg);
                    Box(leg, new Vector3(0, -.45f, 0), new Vector3(.18f, .9f, .2f), new Color(.18f, .2f, .25f));
                    limbs.Add((leg, i, -side));
                }
            }
        }
        pieceBody = Solid(new Vector3(piece.X, .5f, piece.Y), Vector3.One, new Color(1, .8f, .15f));
        ghost = Box(space, Vector3.Zero, Vector3.One, new Color(.2f, 1, 1, .5f));
        ((StandardMaterial3D)ghost.MaterialOverride).Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        ghost.Visible = false;
        space.AddChild(camera);
        camera.Current = true;
        camera.Fov = 75;
    }

    private static float Height(Vector2 p) => p.Y is >= -4 and <= -2 && p.X is >= 1 and < 4 ?
        .25f * (1 + Mathf.Floor(p.X - 1)) : 0;
    private bool Walkable(Vector2 p) => Mathf.Abs(p.X) <= 5.7f && Mathf.Abs(p.Y) <= 5.7f &&
        !(Mathf.Abs(p.X) < .4f && Mathf.Abs(p.Y) > .7f) &&
        !(Mathf.Abs(p.X - piece.X) < .75f && Mathf.Abs(p.Y - piece.Y) < .75f);
    private bool CanPlace(Vector2 p) => Mathf.Abs(p.X) <= 5 && Mathf.Abs(p.Y) <= 5 &&
        !(Mathf.Abs(p.X) < .7f && Mathf.Abs(p.Y) > .5f) &&
        p.DistanceTo(player) > 1 && poses.All(a => p.DistanceTo(new Vector2((float)a.X, (float)a.Y)) > .9f) && Height(p) == 0;

    public override void _Process(double delta)
    {
        elapsed += delta;
        scenario.Step(delta);
        poses = scenario.GetActors();
        // WASD leaves arrows available for elevation when testing 3D cameras.
        var input = new Vector2((Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                            (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
        if (input.LengthSquared() > 1) input = input.Normalized();
        if (mode != "2d")
        {
            yaw += ((Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0)) * (float)delta;
            elevation = Mathf.Clamp(elevation + ((Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0)) * (float)delta, -.4f, 1.2f);
            var forward = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
            input = forward * -input.Y + new Vector2(-forward.Y, forward.X) * input.X;
        }
        Vector2 step = input * (float)delta * 2.5f;
        // Axis separation permits sliding along the doorway. No NPC collision/avoidance.
        foreach (var axis in new[] { new Vector2(step.X, 0), new Vector2(0, step.Y) })
            if (Walkable(player + axis)) player += axis;
            else if (axis.LengthSquared() > 0) blockedMoves++;
        UpdateVisuals();
        hud.Text = $"{mode} / {view} — four moving actors; 2m doorway; yellow 1m module\n" +
            (mode == "2d"
                ? "Fixed overhead | WASD move | R reset | click select/place | Esc cancel\n"
                : "WASD move | Q/E turn | Up/Down tilt | C first/third | R reset | click select/place | Esc cancel\n") +
            $"Player ({player.X:0.0}, {player.Y:0.0}) height {Height(player):0.00} | selected {selected} | {message}\n" +
            "Disposable control/animation proxies. NPCs do not collide; no multiplayer or production art.";
        if (benchmark && elapsed >= 10 && elapsed < 40) frames.Add(delta * 1000);
        if (benchmark && elapsed >= 40) { WriteReport(); GetTree().Quit(); }
    }

    private void UpdateVisuals()
    {
        if (mode == "2d") { overhead.QueueRedraw(); return; }
        for (int i = 0; i < 5; i++)
        {
            Vector2 p = i == 4 ? player : new Vector2((float)poses[i].X, (float)poses[i].Y);
            actors[i].Position = new Vector3(p.X, Height(p), p.Y);
            actors[i].Visible = i != 4 || view != "first";
            if (mode == "sprite3d") ((Sprite3D)actors[i].GetChild(0)).Frame = (int)(elapsed * 5 + i) % 2;
            else actors[i].Rotation = new Vector3(0, i == 4 ? yaw : (Math.Cos(scenario.ElapsedSeconds * .5 + i * .6) > 0 ? -Mathf.Pi / 2 : Mathf.Pi / 2), 0);
        }
        foreach (var (pivot, actor, phase) in limbs)
            pivot.Rotation = new Vector3(Mathf.Sin((float)elapsed * 6 + actor) * .5f * phase, 0, 0);
        var eye = new Vector3(player.X, Height(player) + 1.65f, player.Y);
        if (view == "first") { camera.Position = eye; camera.Rotation = new Vector3(-elevation + .65f, yaw, 0); }
        else
        {
            float distance = 6;
            camera.Position = eye + new Vector3(Mathf.Sin(yaw) * distance * Mathf.Cos(elevation), distance * Mathf.Sin(elevation), Mathf.Cos(yaw) * distance * Mathf.Cos(elevation));
            camera.LookAt(eye);
        }
        ghost.Visible = selected;
        if (selected)
        {
            candidate = PointerGround();
            ghost.Position = new Vector3(candidate.X, .5f, candidate.Y);
            ((StandardMaterial3D)ghost.MaterialOverride).AlbedoColor = CanPlace(candidate) ? new Color(.1f, 1, .7f, .5f) : new Color(1, .15f, .1f, .5f);
        }
    }

    private Vector2 PointerGround()
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        if (mode == "2d") return ((mouse - Origin) / Scale).Round();
        Vector3? point = new Plane(Vector3.Up, 0).IntersectsRay(camera.ProjectRayOrigin(mouse), camera.ProjectRayNormal(mouse));
        return point.HasValue ? new Vector2(point.Value.X, point.Value.Z).Round() : new Vector2(100, 100);
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.C && mode != "2d") { view = view == "first" ? "third" : "first"; GD.Print($"CAMERA {view}"); }
            if (key.Keycode == Key.Escape) selected = false;
            if (key.Keycode == Key.R) { player = new Vector2(-3, 0); yaw = -Mathf.Pi / 2; elevation = .65f; selected = false; }
            if (key.Keycode == Key.Space) PrintState("probe");
        }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || click.Position.Y < 104) return;
        if (!selected)
        {
            bool hit;
            if (mode == "2d") hit = ((click.Position - Origin) / Scale - piece).Abs().X <= .5f && ((click.Position - Origin) / Scale - piece).Abs().Y <= .5f;
            else
            {
                Vector3 origin = camera.ProjectRayOrigin(click.Position);
                var query = PhysicsRayQueryParameters3D.Create(origin, origin + camera.ProjectRayNormal(click.Position) * 100);
                var result = space.GetWorld3D().DirectSpaceState.IntersectRay(query);
                hit = result.Count > 0 && result["collider"].AsGodotObject() == pieceBody;
            }
            if (hit) { selected = true; selectedCount++; message = "Selected: click a valid green grid cell."; GD.Print("PIECE_SELECTED"); }
        }
        else
        {
            candidate = PointerGround();
            if (CanPlace(candidate))
            {
                piece = candidate; selected = false; placedCount++; message = $"Placed at {piece}";
                if (mode != "2d") pieceBody.Position = new Vector3(piece.X, .5f, piece.Y);
                GD.Print($"PIECE_PLACED {piece}");
            }
            else { rejectedCount++; message = "Placement rejected: wall, actor, platform or bounds."; GD.Print($"PIECE_REJECTED {candidate}"); }
        }
    }

    private void PrintState(string reason)
    {
        Vector2 Screen(Vector2 p, float height) => mode == "2d" ? Origin + p * Scale : camera.UnprojectPosition(new Vector3(p.X, height, p.Y));
        var pieceScreen = Screen(piece, .5f);
        var validScreen = Screen(piece == new Vector2(-1, 3) ? new Vector2(-2, 3) : new Vector2(-1, 3), 0);
        var wallScreen = Screen(new Vector2(0, 3), 0);
        GD.Print("STATE ", JsonSerializer.Serialize(new {
            reason, mode, camera = view, player_x = player.X, player_z = player.Y, height = Height(player),
            piece_screen_x = pieceScreen.X, piece_screen_y = pieceScreen.Y,
            valid_screen_x = validScreen.X, valid_screen_y = validScreen.Y,
            wall_screen_x = wallScreen.X, wall_screen_y = wallScreen.Y,
            selected, placedCount, rejectedCount, blockedMoves
        }));
    }
    private void WriteReport()
    {
        frames.Sort();
        var result = new
        {
            mode, camera = view, engine = Engine.GetVersionInfo()["string"].AsString(), runtime = System.Environment.Version.ToString(),
            build = OS.IsDebugBuild() ? "debug" : "release", warmup_seconds = 10, sample_seconds = 30, sample_frames = frames.Count,
            median_frame_ms = frames[frames.Count / 2], p95_frame_ms = frames[(int)(frames.Count * .95)],
            rss_bytes = Process.GetCurrentProcess().WorkingSet64,
            engine_static_bytes = Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            video_memory_bytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed),
            renderer = "gl_compatibility", viewport = "960x540", vsync = "disabled", max_fps = Engine.MaxFps,
            actor_count = 5, selectedCount, placedCount, rejectedCount, blockedMoves
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        if (report.Length > 0) File.WriteAllText(report, json + "\n");
        GD.Print("BENCHMARK ", json);
    }

    public void DrawRoom(Node2D canvas)
    {
        Vector2 Screen(Vector2 p) => Origin + p * Scale;
        void Rect(Vector2 p, Vector2 size, Color c) => canvas.DrawRect(new Rect2(Screen(p), size * Scale), c);
        Rect(new Vector2(-6, -6), new Vector2(12, 12), new Color(.25f, .28f, .3f));
        for (int i = -6; i <= 6; i++)
        {
            canvas.DrawLine(Screen(new Vector2(i, -6)), Screen(new Vector2(i, 6)), new Color(.36f, .38f, .4f));
            canvas.DrawLine(Screen(new Vector2(-6, i)), Screen(new Vector2(6, i)), new Color(.36f, .38f, .4f));
        }
        foreach (float z in new[] { -6f, 1f }) Rect(new Vector2(-.125f, z), new Vector2(.25f, 5), new Color(.6f, .6f, .65f));
        for (int i = 0; i < 3; i++) Rect(new Vector2(1 + i, -4), new Vector2(1, 2), new Color(.35f + i * .05f, .4f + i * .05f, .5f));
        Rect(piece - Vector2.One * .5f, Vector2.One, new Color(1, .8f, .15f));
        if (selected)
        {
            candidate = PointerGround();
            Rect(candidate - Vector2.One * .5f, Vector2.One, CanPlace(candidate) ? new Color(.1f, 1, .7f, .6f) : new Color(1, .1f, .1f, .6f));
        }
        for (int i = 0; i < 5; i++)
        {
            Vector2 p = i == 4 ? player : new Vector2((float)poses[i].X, (float)poses[i].Y);
            int frame = (int)(elapsed * 5 + i) % 2;
            canvas.DrawTextureRectRegion(topSprite, new Rect2(Screen(p) - Vector2.One * 12, Vector2.One * 24), new Rect2(frame * 32, 0, 32, 32), ActorColor(i));
        }
    }
}

public partial class OverheadRoom : Node2D
{
    public PresentationLab Lab { get; set; } = null!;
    public override void _Draw() => Lab.DrawRoom(this);
}
