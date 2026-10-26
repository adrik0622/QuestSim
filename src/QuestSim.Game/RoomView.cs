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
    private readonly DirectionalLight3D sun = new() { RotationDegrees = new Vector3(-55, -35, 0), ShadowEnabled = true };
    private Godot.Environment? environment;
    private readonly ColorRect hudBack = new() { Color = new Color(.03f, .04f, .06f, .9f), Size = new Vector2(960, 100), MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Dictionary<string, Node3D> actors = new();
    private bool roomBuilt;
    private readonly Dictionary<string, (MeshInstance3D Box, Label3D Label)> supplies = new();
    private readonly HashSet<string> blocks = new();
    private CityGen? city;
    private readonly Dictionary<(int, int), Node3D> chunks = new();
    private (int, int)? centreChunk;
    private const int ChunkRadius = 4;
    public event Action? PickupRequested, BuildRequested, RetryRequested;
    public event Action<string>? AttackRequested;
    public event Action? AskRequested, UseRequested, InspectRequested;
    private readonly Label inspector = new() { Position = new Vector2(12, 430), Visible = false };
    private double inspectorSeconds;
    public void ShowInspection(string text) { inspector.Text = text; inspector.Visible = true; inspectorSeconds = 8; }

    public override void _Ready()
    {
        environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.12f, .17f, .23f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .65f
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(sun);
        AddChild(camera); camera.Current = true; camera.Fov = 75; camera.Near = .1f; camera.Far = 2500;
        camera.Position = new Vector3(-8, 6, 0); camera.LookAt(new Vector3(0, 1, 0));
        var layer = new CanvasLayer(); AddChild(layer);
        layer.AddChild(hudBack);
        hud.Position = new Vector2(12, 8); hud.AddThemeFontSizeOverride("font_size", 17); layer.AddChild(hud);
        inspector.AddThemeFontSizeOverride("font_size", 15); inspector.AddThemeColorOverride("font_color", new Color(1, .95f, .6f)); layer.AddChild(inspector);
    }
    private static MeshInstance3D Box(Node3D parent, Vector3 p, Vector3 size, Color color)
    {
        var box = new MeshInstance3D { Position = p, Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .9f } };
        parent.AddChild(box); return box;
    }
    private void BuildRoom(WorldSnapshot state)
    {
        Box(this, new Vector3(0, -.1f, 0), new Vector3(12, .2f, 12), new Color(.25f, .28f, .3f));
        city = new CityGen(state.WorldSeed);
        var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(CityGen.Half * 2, CityGen.Half * 2) }, Position = new Vector3(0, -.3f, 0),   // well below chunk ground: no z-fighting at a distance
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.35f, .5f, .25f), Roughness = 1 } };
        AddChild(ground);
        foreach (var road in city.Roads)
        {
            var from = new Vector3(road.X1, 0, road.Z1); var to = new Vector3(road.X2, 0, road.Z2);
            var strip = Box(this, (from + to) / 2 + new Vector3(0, -.06f, 0), new Vector3(CityGen.RoadHalfWidth * 2, .12f, from.DistanceTo(to)), new Color(.2f, .2f, .22f));
            strip.Rotation = new Vector3(0, Mathf.Atan2(to.X - from.X, to.Z - from.Z), 0);
        }
        var site = WorldSave.BuildSite; // Floor marker for the one build cell.
        Box(this, new Vector3((float)site.X, .01f, (float)site.Z), new Vector3((float)site.Width, .02f, (float)site.Depth + .3f), new Color(.85f, .55f, .3f));
        foreach (var supply in state.Supplies)
        {
            var label = new Label3D { Position = new Vector3((float)supply.X, .9f, (float)supply.Z), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, PixelSize = .005f };
            AddChild(label);
            supplies[supply.Id] = (Box(this, new Vector3((float)supply.X, .3f, (float)supply.Z), new Vector3(.6f, .6f, .6f), Colors.White), label);
        }
        roomBuilt = true;
    }
    // Keep the chunks around the local player built; generated identically to the server from the world seed.
    private void StreamChunks(Vector3 position)
    {
        var centre = ((int)Mathf.Floor(position.X / CityGen.ChunkSize), (int)Mathf.Floor(position.Z / CityGen.ChunkSize));
        if (city is null || centreChunk == centre) return;
        centreChunk = centre;
        var wanted = new HashSet<(int, int)>();
        for (int dx = -ChunkRadius; dx <= ChunkRadius; dx++)
            for (int dz = -ChunkRadius; dz <= ChunkRadius; dz++) wanted.Add((centre.Item1 + dx, centre.Item2 + dz));
        foreach (var key in chunks.Keys.Except(wanted).ToArray()) { chunks[key].QueueFree(); chunks.Remove(key); }
        foreach (var key in wanted.Except(chunks.Keys).ToArray()) chunks[key] = BuildChunk(key.Item1, key.Item2);
    }

    private Node3D BuildChunk(int cx, int cz)
    {
        var node = new Node3D { Name = $"Chunk_{cx}_{cz}" };
        AddChild(node);
        int x0 = cx * CityGen.ChunkSize, z0 = cz * CityGen.ChunkSize, half = CityGen.ChunkSize / 2;
        var biome = city!.BiomeAt(x0 + half, z0 + half);
        if (biome != Biome.Field)
        {
            var colour = biome switch { Biome.Urban => new Color(.3f, .31f, .33f), Biome.Town => new Color(.42f, .4f, .35f), _ => new Color(.17f, .33f, .17f) };
            node.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(CityGen.ChunkSize, CityGen.ChunkSize) }, Position = new Vector3(x0 + half, -.02f, z0 + half),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 1 } });
        }
        var buildings = city.Chunk(cx, cz);
        if (buildings.Length == 0) return node;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One } };
        multi.InstanceCount = buildings.Length;
        for (int i = 0; i < buildings.Length; i++)
        {
            var b = buildings[i];
            multi.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(b.Width, b.Height, b.Depth)), new Vector3(b.X, b.Height / 2f, b.Z)));
            float shade = .45f + (CityGen.Mix((uint)(b.X * 73856093 ^ b.Z * 19349663)) % 100) / 400f;
            multi.SetInstanceColor(i, b.Height > 18 ? new Color(shade * .8f, shade * .85f, shade) : new Color(shade, shade * .9f, shade * .75f));
        }
        node.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = .9f } });
        return node;
    }

    // Pull the third-person camera in front of the first wall or building between the eye and the camera.
    private float CameraDistance(WorldSnapshot state, Vector3 eye, Vector3 direction, float wanted)
    {
        for (float t = .5f; t <= wanted; t += .25f)
        {
            var p = eye + direction * t;
            bool blocked = state.Blocks.Any(b => Mathf.Abs(p.X - (float)b.X) < b.Width / 2 && Mathf.Abs(p.Z - (float)b.Z) < b.Depth / 2 && p.Y < b.Height);
            if (!blocked && city is not null)
            {
                int cx = (int)Mathf.Floor(p.X / CityGen.ChunkSize), cz = (int)Mathf.Floor(p.Z / CityGen.ChunkSize);
                for (int dx = -1; dx <= 1 && !blocked; dx++)
                    for (int dz = -1; dz <= 1 && !blocked; dz++)
                        blocked = city.Chunk(cx + dx, cz + dz).Any(b => Mathf.Abs(p.X - b.X) < b.Width / 2f && Mathf.Abs(p.Z - b.Z) < b.Depth / 2f && p.Y < b.Height);
            }
            if (blocked) return Mathf.Max(.3f, t - .3f);
        }
        return wanted;
    }

    // World ticks are 20 per real second; at 48:1 each tick is 2.4 game seconds.
    private void ApplyTimeOfDay(long tick)
    {
        double hour = Population.GameHour(tick / (double)WorldAuthority.TickRate * Population.GameSecondsPerTick);
        float daylight = (float)Math.Clamp(Math.Sin((hour - 6) / 12 * Math.PI) * 1.4, 0, 1);
        sun.LightEnergy = .15f + .85f * daylight;
        sun.RotationDegrees = new Vector3(-10 - 60 * daylight, (float)(hour / 24 * 360), 0);
        environment!.BackgroundColor = new Color(.02f, .03f, .06f).Lerp(new Color(.45f, .62f, .85f), daylight);
        environment.AmbientLightEnergy = .25f + .45f * daylight;
        clock = $"{(int)hour:00}:{(int)(hour % 1 * 60):00}";
    }

    private string clock = "";
    private readonly Dictionary<int, Node3D> caches = new();
    private static string Condition(WorldSnapshot? state, string ownId) =>
        state?.Actors.FirstOrDefault(a => a.Id == ownId)?.Condition is { Length: > 0 } c ? $" ({c})" : "";

    private void UpdateCaches(WorldSnapshot state)
    {
        var present = (state.Caches ?? []).ToDictionary(k => k.Id);
        foreach (var id in caches.Keys.Except(present.Keys).ToArray()) { caches[id].QueueFree(); caches.Remove(id); }
        foreach (var (id, cache) in present.Where(p => !caches.ContainsKey(p.Key)))
        {
            var node = new Node3D { Position = new Vector3((float)cache.X, 0, (float)cache.Z) };
            AddChild(node);
            Box(node, new Vector3(0, .25f, 0), new Vector3(.7f, .5f, .5f), new Color(.45f, .3f, .15f));
            node.AddChild(new Label3D { Text = cache.Label, Position = new Vector3(0, .9f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, PixelSize = .005f });
            caches[id] = node;
        }
    }

    private Node3D CreateActor(ActorState state, bool local)
    {
        var actor = new Node3D { Position = new Vector3((float)state.X, 0, (float)state.Z) };
        AddChild(actor);
        // Players: blue (you) / orange (others). NPC humans by faction; zombies grey-green.
        Color shirt = state.Kind switch
        {
            0 => local ? new Color(.3f, .65f, 1) : new Color(.95f, .6f, .35f),
            2 => new Color(.3f, .33f, .28f),
            _ => state.Faction switch { 0 => new Color(.45f, .55f, .7f), 1 => new Color(.75f, .65f, .4f), _ => new Color(.6f, .12f, .15f) }
        };
        Color skin = state.Kind == 2 ? new Color(.45f, .6f, .4f) : new Color(1, .8f, .6f);
        Box(actor, new Vector3(0, 1.25f, 0), new Vector3(.48f, .6f, .28f), shirt);
        Box(actor, new Vector3(0, 1.75f, 0), new Vector3(.36f, .36f, .32f), skin);
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
    public void UpdateWorld(WorldSnapshot? state, string ownId, string profile, string connection, Dictionary<string, int>? inventory, string itemMessage, double delta)
    {
        Yaw += ((Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0)) * (float)delta;
        elevation = Mathf.Clamp(elevation + ((Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0)) * (float)delta, -.4f, 1.2f);
        hud.Text = $"{profile} — {connection} | {(firstPerson ? "first" : "third")} person\n" +
            "WASD move (Shift sprint) | Q/E turn | Up/Down tilt | C/R camera | F use | B build | T retry | V melee | G shoot | H ask | U heal | I inspect\n" +
            $"Present: {state?.Actors.Length ?? 0} | {clock}" +
            string.Concat((state?.Factions ?? []).Where(f => f.Id < 3).Select(f => $" | {f.Name} {f.Standing:+0;-0}")) + "\n" +
            $"Health: {state?.Actors.FirstOrDefault(a => a.Id == ownId)?.Health ?? 0}{Condition(state, ownId)} | Ammo: {inventory?.GetValueOrDefault(WorldAuthority.Ammo) ?? 0} | " +
            $"Bandages: {inventory?.GetValueOrDefault("questsim:bandage") ?? 0} | Panels: {inventory?.GetValueOrDefault(WorldSave.Panel) ?? 0} | {itemMessage}" +
            string.Concat((state?.Opportunities ?? []).Take(3).Select(o => $"\n• {o.Text}"));
        hudBack.Size = new Vector2(960, 100 + 22 * Math.Min(3, state?.Opportunities?.Length ?? 0));
        if ((inspectorSeconds -= delta) <= 0) inspector.Visible = false;
        if (state is null) return;
        ApplyTimeOfDay(state.Tick);
        if (!roomBuilt) BuildRoom(state);
        UpdateCaches(state);
        // Blocks only appear (no demolition yet); draw any the server added since the last snapshot.
        foreach (var block in state.Blocks.Where(b => blocks.Add(b.Id)))
            Box(this, new Vector3((float)block.X, (float)block.Height / 2, (float)block.Z),
                new Vector3((float)block.Width, (float)block.Height, (float)block.Depth),
                block.Id == "questsim:module" ? new Color(1, .8f, .15f) : block.Id == WorldSave.BuildSite.Id ? new Color(.75f, .45f, .25f) : new Color(.55f, .56f, .6f));
        foreach (var supply in state.Supplies)
        {
            var (box, label) = supplies[supply.Id];
            bool panels = supply.ItemId == WorldSave.Panel;
            ((StandardMaterial3D)box.MaterialOverride).AlbedoColor = supply.Count == 0 ? new Color(.3f, .35f, .3f) : panels ? new Color(.25f, .45f, .95f) : new Color(.2f, .8f, .3f);
            label.Text = $"{(panels ? "Panel" : "Bandage")} crate ({supply.Count})";
        }
        foreach (string id in actors.Keys.Except(state.Actors.Select(a => a.Id)).ToArray()) { actors[id].QueueFree(); actors.Remove(id); }
        foreach (var a in state.Actors)
        {
            bool local = a.Id == ownId;
            if (!actors.TryGetValue(a.Id, out var actor)) actor = CreateActor(a, local);
            actor.Position = actor.Position.Lerp(new Vector3((float)a.X, 0, (float)a.Z), 1 - Mathf.Exp(-15 * (float)delta));
            actor.Rotation = new Vector3(0, (float)a.Heading, 0);
            actor.Visible = !local || !firstPerson;
            if (!local) continue;
            StreamChunks(actor.Position);
            Vector3 eye = actor.Position + new Vector3(0, 1.65f, 0);
            if (firstPerson) { camera.Position = eye; camera.Rotation = new Vector3(.65f - elevation, Yaw, 0); }
            else
            {
                var offset = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(Yaw) * Mathf.Cos(elevation));
                camera.Position = eye + offset * CameraDistance(state, eye, offset, 6);
                camera.LookAt(eye);
            }
        }
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F) PickupRequested?.Invoke();
        if (key.Keycode == Key.B) BuildRequested?.Invoke();
        if (key.Keycode == Key.T) RetryRequested?.Invoke();
        if (key.Keycode == Key.V) AttackRequested?.Invoke("melee");
        if (key.Keycode == Key.G) AttackRequested?.Invoke("shoot");
        if (key.Keycode == Key.H) AskRequested?.Invoke();
        if (key.Keycode == Key.U) UseRequested?.Invoke();
        if (key.Keycode == Key.I) InspectRequested?.Invoke();
        if (key.Keycode == Key.C) firstPerson = !firstPerson;
        if (key.Keycode == Key.R) { Yaw = -Mathf.Pi / 2; elevation = .65f; }
    }
}
