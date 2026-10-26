using Godot;
using QuestSim.Domain;
using System.Text.Json;
using System.Text.RegularExpressions;

// Same node path/RPC declarations in separate server and client processes.
public partial class Session : Node
{
    private readonly Dictionary<string, string> options = new();
    private ENetMultiplayerPeer network = null!;
    private WorldAuthority? authority;
    private RoomView? view;
    private WorldSnapshot? snapshot;
    private CharacterRecord? own;
    private string profileId = "", profileName = "", savePath = "", statusPath = "", intentPath = "", shutdownPath = "";
    private string connection = "Connecting";
    private bool server, connected, serverStarted, failed;
    private volatile bool shutdownRequested;
    private FileStream? saveLock;
    private int frame;
    private double saveElapsed, statusElapsed, desiredX, desiredZ, desiredHeading = -Math.PI / 2;
    private long sequence, commandSerial;
    private long? testSequence;
    private Profile profile = null!;
    private string profilePath = "";
    private PickupReceipt? lastPickup;
    private string itemMessage = "F takes a bandage from the green crate; T retries your last attempt.";
    private static string Workspace => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
    private string Option(string key, string fallback = "") => options.GetValueOrDefault(key, fallback);
    private string PathOption(string key, string fallback = "") => Option(key, fallback) is var value && value.Length > 0 ? Path.GetFullPath(value, Workspace) : "";
    private sealed record Profile(string Id, string Name, string LastPickupRequestId = "", string LastPickupContainerId = "");
    private sealed class TestIntent
    {
        public long Serial { get; set; }
        public double X { get; set; }
        public double Z { get; set; }
        public double Heading { get; set; } = -Math.PI / 2;
        public long? Sequence { get; set; }
        public bool Quit { get; set; }
        public string PickupRequestId { get; set; } = "";
        public string ContainerId { get; set; } = "";
        public bool RetryPickup { get; set; }
    }

    public override void _Ready()
    {
        try
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                var pair = arg.TrimStart('-').Split('=', 2);
                options[pair[0]] = pair.Length == 2 ? pair[1] : "true";
            }
            server = options.ContainsKey("server");
            Engine.PhysicsTicksPerSecond = WorldAuthority.TickRate;
            Engine.MaxFps = 60;
            statusPath = PathOption("status");
            shutdownPath = PathOption("shutdown-file");
            if (Multiplayer is SceneMultiplayer sceneMultiplayer) sceneMultiplayer.AllowObjectDecoding = false;
            network = new ENetMultiplayerPeer();
            int port = int.Parse(Option("port", "27840"));
            Error error;
            if (server)
            {
                if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Run the server with --headless");
                savePath = PathOption("save", ".artifacts/play/world.json");
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                saveLock = new FileStream(savePath + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
                authority = new WorldAuthority(WorldStorage.Load(savePath));
                network.SetBindIP(Option("bind", "127.0.0.1"));
                error = network.CreateServer(port, 8);
                Multiplayer.PeerDisconnected += OnPeerLeft;
                Multiplayer.PeerConnected += peer => network.GetPeer((int)peer).SetTimeout(8, 1000, 3000);
                connection = "Server ready";
            }
            else
            {
                profileName = Option("profile", "player");
                if (!Regex.IsMatch(profileName, "^[a-zA-Z0-9_-]{1,24}$")) throw new ArgumentException("Profile must be 1–24 letters, digits, _ or -");
                profilePath = Path.Combine(PathOption("profiles-dir", ".artifacts/play/profiles"), profileName + ".json");
                if (File.Exists(profilePath)) profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(profilePath), WorldStorage.JsonOptions) ?? throw new InvalidDataException("Empty profile");
                else
                {
                    profile = new Profile(Guid.NewGuid().ToString("N"), profileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
                    using var profileFile = new FileStream(profilePath, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(profileFile, profile);
                    profileFile.Flush(flushToDisk: true);
                }
                if (!Guid.TryParseExact(profile.Id, "N", out _) || profile.LastPickupRequestId is null ||
                    (profile.LastPickupRequestId.Length > 0 && (!Guid.TryParseExact(profile.LastPickupRequestId, "N", out _) ||
                    string.IsNullOrWhiteSpace(profile.LastPickupContainerId) || profile.LastPickupContainerId.Length > 128))) throw new InvalidDataException("Invalid local profile identity");
                profileId = profile.Id;
                intentPath = PathOption("intent-file"); // Explicit opt-in for headless acceptance, never server state injection.
                Multiplayer.ConnectedToServer += OnConnected;
                Multiplayer.ConnectionFailed += () => Disconnected("Connection failed; restart client to retry");
                Multiplayer.ServerDisconnected += () => { if (connected) Disconnected("Server disconnected; restart client to reconnect"); };
                error = network.CreateClient(Option("host", "127.0.0.1"), port);
                if (DisplayServer.GetName() != "headless")
                {
                    DisplayServer.WindowSetTitle("QuestSim Shared Room");
                    view = new RoomView();
                    AddChild(view);
                    view.PickupRequested += retry => RequestPickup(retry);
                }
            }
            if (error != Error.Ok) throw new InvalidOperationException($"ENet startup failed: {error}");
            Multiplayer.MultiplayerPeer = network;
            WriteStatus();
            serverStarted = server;
            if (server) Console.CancelKeyPress += OnCancelKeyPress;
            GD.Print($"SESSION_READY role={(server ? "server" : "client")} profile={profileName} display={DisplayServer.GetName()} protocol={WorldAuthority.ProtocolVersion} port={port}");
        }
        catch (Exception e) { Fail(e); }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        shutdownRequested = true; // The signal callback never touches Godot objects.
    }

    private void OnConnected()
    {
        connected = true;
        network.GetPeer(1).SetTimeout(8, 1000, 3000);
        RpcId(1, nameof(RequestJoin), WorldAuthority.ProtocolVersion, profileId, profileName);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestJoin(int version, string profile, string name)
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        try
        {
            if (version != WorldAuthority.ProtocolVersion) throw new InvalidOperationException("Incompatible protocol version");
            var character = authority.Join(peer, profile, name);
            WorldStorage.Save(savePath, authority.State);
            RpcId(peer, nameof(Welcome), JsonSerializer.Serialize(character));
            GD.Print($"PLAYER_JOIN id={character.Id} peer={peer} active={authority.ActiveCount}");
            Publish();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            RpcId(peer, nameof(RejectJoin), e.Message);
        }
        catch (Exception e) { Fail(e); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Welcome(string json)
    {
        if (server) return;
        own = JsonSerializer.Deserialize<CharacterRecord>(json, WorldStorage.JsonOptions) ?? throw new InvalidDataException("Missing welcome");
        connection = "Connected";
        GD.Print($"PLAYER_WELCOME id={own.Id} name={own.Name}");
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RejectJoin(string reason)
    {
        if (server) return;
        GD.Print("JOIN_REJECTED ", reason);
        Disconnected("Join rejected: " + reason);
        network.Close();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    public void MoveIntent(long inputSequence, double x, double z, double heading)
    {
        if (!server || authority is null || failed) return;
        if (!authority.SetIntent(Multiplayer.GetRemoteSenderId(), inputSequence, x, z, heading))
            GD.Print("INTENT_REJECTED peer=", Multiplayer.GetRemoteSenderId());
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    public void ReceiveSnapshot(string json)
    {
        if (server) return;
        var received = JsonSerializer.Deserialize<WorldSnapshot>(json, WorldStorage.JsonOptions) ?? throw new InvalidDataException("Empty snapshot");
        if (received.Version != WorldAuthority.ProtocolVersion) { Disconnected("Incompatible snapshot version"); return; }
        if (snapshot is not null && received.Sequence <= snapshot.Sequence) return;
        snapshot = received;
    }

    private void RequestPickup(bool retry, string requestId = "", string containerId = "")
    {
        if (!connected || own is null || snapshot is null) return;
        if (retry)
        {
            requestId = profile.LastPickupRequestId;
            containerId = profile.LastPickupContainerId;
            if (requestId.Length == 0) { itemMessage = "No pickup to retry yet."; return; }
        }
        else
        {
            if (requestId.Length == 0) requestId = Guid.NewGuid().ToString("N");
            if (!Guid.TryParseExact(requestId, "N", out var id)) { itemMessage = "Invalid pickup request."; return; }
            requestId = id.ToString("N");
            if (containerId.Length == 0) containerId = snapshot.Supply.Id;
            if (containerId.Length > 128) { itemMessage = "Invalid crate."; return; }
            var pending = profile with { LastPickupRequestId = requestId, LastPickupContainerId = containerId };
            try { WorldStorage.WriteAtomic(profilePath, JsonSerializer.Serialize(pending)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                itemMessage = "Could not save pickup request; no item taken.";
                GD.Print("LOCAL_REQUEST_SAVE_FAILED ", e.Message);
                return;
            }
            profile = pending;
        }
        itemMessage = retry ? "Retrying pickup..." : "Trying to take bandage...";
        RpcId(1, nameof(TakeItem), requestId, containerId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void TakeItem(string requestId, string containerId)
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        PickupReceipt receipt;
        try { receipt = authority.TakeItem(peer, requestId, containerId); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            var invalid = new PickupReceipt(requestId, "", containerId, "questsim:bandage", false, "invalid_request", 0);
            RpcId(peer, nameof(ItemReply), JsonSerializer.Serialize(invalid), "{}");
            return;
        }
        try
        {
            // Inventory, stock and receipt share one atomic save. Never acknowledge before it succeeds.
            WorldStorage.Save(savePath, authority.State);
            RpcId(peer, nameof(ItemReply), JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(authority.CharacterFor(peer).Inventory));
            Publish();
            WriteStatus();
            GD.Print($"PICKUP_RESULT actor={receipt.ActorId} request={receipt.RequestId} result={receipt.Reason}");
        }
        catch (Exception e) { Fail(e); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ItemReply(string receiptJson, string inventoryJson)
    {
        if (server || own is null) return;
        lastPickup = JsonSerializer.Deserialize<PickupReceipt>(receiptJson, WorldStorage.JsonOptions) ?? throw new InvalidDataException("Empty item receipt");
        if (lastPickup.ActorId == own.Id)
            own.Inventory = JsonSerializer.Deserialize<Dictionary<string, int>>(inventoryJson) ?? throw new InvalidDataException("Missing inventory");
        itemMessage = lastPickup.Reason switch
        {
            "taken" => "Bandage taken.", "empty" => "The crate is empty.",
            "out_of_reach" => "Move closer to the green crate.", "unknown_container" => "That crate is unavailable.",
            "inventory_full" => "Cannot carry more bandages.", _ => "Pickup request rejected."
        };
        GD.Print($"ITEM_REPLY request={lastPickup.RequestId} result={lastPickup.Reason}");
    }

    private void Publish()
    {
        snapshot = authority!.Snapshot();
        Rpc(nameof(ReceiveSnapshot), JsonSerializer.Serialize(snapshot));
    }
    private void OnPeerLeft(long peer)
    {
        if (authority is null || failed || !authority.Leave((int)peer)) return;
        try
        {
            WorldStorage.Save(savePath, authority.State);
            GD.Print($"PLAYER_LEAVE peer={peer} active={authority.ActiveCount} tick={authority.State.Tick}");
            Publish();
            WriteStatus();
        }
        catch (Exception e) { Fail(e); }
    }
    private void Disconnected(string message)
    {
        connected = false; connection = message; snapshot = null;
        view?.ClearActors();
        GD.Print("CONNECTION ", message);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (network is null) return;
        try
        {
            frame++;
            if (shutdownRequested || (shutdownPath.Length > 0 && File.Exists(shutdownPath))) { GetTree().Quit(); return; }
            if (server)
            {
                authority!.Step();
                if (frame % 2 == 0) Publish();
                saveElapsed += delta;
                if (saveElapsed >= 2 && authority.ActiveCount > 0) { WorldStorage.Save(savePath, authority.State); saveElapsed = 0; }
            }
            else if (connected && own is not null)
            {
                if (intentPath.Length > 0) ReadTestIntent();
                else
                {
                    Vector2 input = new((Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                                        (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
                    if (input.LengthSquared() > 1) input = input.Normalized();
                    double yaw = view?.Yaw ?? -Math.PI / 2;
                    desiredX = -Math.Sin(yaw) * -input.Y + Math.Cos(yaw) * input.X;
                    desiredZ = -Math.Cos(yaw) * -input.Y - Math.Sin(yaw) * input.X;
                    desiredHeading = yaw;
                }
                if (frame % 2 == 0) RpcId(1, nameof(MoveIntent), testSequence ?? ++sequence, desiredX, desiredZ, desiredHeading);
            }
            statusElapsed += delta;
            if (statusElapsed >= .2) { WriteStatus(); statusElapsed = 0; }
        }
        catch (Exception e) { Fail(e); }
    }
    private void ReadTestIntent()
    {
        if (!File.Exists(intentPath)) return;
        var command = JsonSerializer.Deserialize<TestIntent>(File.ReadAllText(intentPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (command is null || command.Serial <= commandSerial) return;
        commandSerial = command.Serial;
        desiredX = command.X; desiredZ = command.Z; desiredHeading = command.Heading; testSequence = command.Sequence;
        if (command.PickupRequestId.Length > 0 || command.RetryPickup)
            RequestPickup(command.RetryPickup, command.PickupRequestId, command.ContainerId);
        if (command.Quit) GetTree().Quit();
    }
    public override void _Process(double delta) => view?.UpdateWorld(own is null ? null : snapshot, own?.Id ?? "", profileName, connection, own?.Inventory.GetValueOrDefault("questsim:bandage") ?? 0, itemMessage, delta);

    private void WriteStatus()
    {
        if (statusPath.Length == 0) return;
        WorldStorage.WriteAtomic(statusPath, JsonSerializer.Serialize(new
        {
            role = server ? "server" : "client", display = DisplayServer.GetName(), connection, profile = profileName,
            ownId = own?.Id, own, active = authority?.ActiveCount ?? snapshot?.Actors.Length ?? 0,
            tick = authority?.State.Tick ?? snapshot?.Tick ?? 0, snapshot,
            characters = authority?.State.Characters, receipts = authority?.State.Receipts, lastPickup, itemMessage, fps = Engine.GetFramesPerSecond(),
            rssBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64
        }));
    }
    private void Fail(Exception e)
    {
        failed = true;
        GD.PushError(e.ToString());
        SetPhysicsProcess(false);
        GetTree().Quit(1);
    }
    public override void _ExitTree()
    {
        try { if (serverStarted && authority is not null && savePath.Length > 0) WorldStorage.Save(savePath, authority.State); }
        catch (Exception e) { GD.PushError("Final save failed: " + e.Message); GetTree().Quit(1); }
        if (server) Console.CancelKeyPress -= OnCancelKeyPress;
        network?.Close();
        saveLock?.Dispose();
    }
}
