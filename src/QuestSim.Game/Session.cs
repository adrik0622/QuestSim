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
    private string profilePath = "", capturePath = "";
    private Key[] heldKeys = [];
    private double populationSaveSeconds = 60, populationElapsed;
    private Task<(string Name, string Hash)>? populationWrite;
    private PickupReceipt? lastPickup;
    private BuildReceipt? lastBuild;
    private AttackResult? lastAttack;
    private string lastInteract = "";
    private AskResult? lastAsk;
    private string itemMessage = "F takes from the nearest crate; B builds a panel wall; T retries your last attempt.";
    private static string Workspace => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
    private string Option(string key, string fallback = "") => options.GetValueOrDefault(key, fallback);
    private string PathOption(string key, string fallback = "") => Option(key, fallback) is var value && value.Length > 0 ? Path.GetFullPath(value, Workspace) : "";
    private sealed record Profile(string Id, string Name, string LastPickupRequestId = "", string LastPickupContainerId = "",
        string LastBuildRequestId = "", string LastBuildSiteId = "", string LastAction = "");
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
        public string BuildRequestId { get; set; } = "";
        public string SiteId { get; set; } = "";
        public bool RetryBuild { get; set; }
        public string[] Keys { get; set; } = [];
        public string[] HoldKeys { get; set; } = [];
        public string Capture { get; set; } = "";
        public string Attack { get; set; } = "";
        public string Interact { get; set; } = "";
        public bool Ask { get; set; }
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
                var save = WorldStorage.Load(savePath);
                if (!File.Exists(savePath))
                {
                    // New worlds: a seeded million-agent population (configurable) and a basic starting kit.
                    save.Population = int.Parse(Option("population", "1000000"));
                    save.StartingKit = new() { [WorldAuthority.Pistol] = 1, [WorldAuthority.Ammo] = 24 };
                }
                authority = new WorldAuthority(save);
                authority.LoadPopulation(savePath);
                populationSaveSeconds = double.Parse(Option("population-save-seconds", "60"), System.Globalization.CultureInfo.InvariantCulture);
                GD.Print($"POPULATION agents={save.Population} load={authority.PopulationLoad}");
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
                static bool ValidLast(string? id, string? target) => id is not null && (id.Length == 0 ||
                    (Guid.TryParseExact(id, "N", out _) && !string.IsNullOrWhiteSpace(target) && target.Length <= 128));
                if (!Guid.TryParseExact(profile.Id, "N", out _) || !ValidLast(profile.LastPickupRequestId, profile.LastPickupContainerId) ||
                    !ValidLast(profile.LastBuildRequestId, profile.LastBuildSiteId) || profile.LastAction is not ("" or "pickup" or "build"))
                    throw new InvalidDataException("Invalid local profile identity");
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
                    view.PickupRequested += Interact;
                    view.BuildRequested += () => RequestBuild(false);
                    view.AttackRequested += kind => RequestAttack(kind);
                    view.AskRequested += () => { if (connected && own is not null) RpcId(1, nameof(RequestAsk)); };
                    view.RetryRequested += () => { if (profile.LastAction == "build") RequestBuild(true); else RequestPickup(true); };
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
            if (containerId.Length == 0) containerId = NearestCrate().Id;
            if (containerId.Length > 128) { itemMessage = "Invalid crate."; return; }
            if (!SaveProfile(profile with { LastPickupRequestId = requestId, LastPickupContainerId = containerId, LastAction = "pickup" }, "pickup", "no item taken")) return;
        }
        itemMessage = retry ? "Retrying pickup..." : "Trying to take from crate...";
        RpcId(1, nameof(TakeItem), requestId, containerId);
    }

    // Prefer a stocked crate within reach, then the nearest crate; the server still validates both.
    private SupplyState NearestCrate()
    {
        var me = snapshot!.Actors.FirstOrDefault(a => a.Id == own!.Id);
        double Distance(SupplyState s) => me is null ? 0 : Math.Pow(me.X - s.X, 2) + Math.Pow(me.Z - s.Z, 2);
        return snapshot.Supplies.OrderBy(s => s.Count > 0 && Distance(s) <= 4 ? 0 : 1).ThenBy(Distance).First();
    }

    private bool SaveProfile(Profile pending, string kind, string consequence)
    {
        try { WorldStorage.WriteAtomic(profilePath, JsonSerializer.Serialize(pending)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            itemMessage = $"Could not save {kind} request; {consequence}.";
            GD.Print("LOCAL_REQUEST_SAVE_FAILED ", e.Message);
            return false;
        }
        profile = pending;
        return true;
    }

    private void RequestBuild(bool retry, string requestId = "", string siteId = "")
    {
        if (!connected || own is null || snapshot is null) return;
        if (retry)
        {
            requestId = profile.LastBuildRequestId;
            siteId = profile.LastBuildSiteId;
            if (requestId.Length == 0) { itemMessage = "No build to retry yet."; return; }
        }
        else
        {
            if (requestId.Length == 0) requestId = Guid.NewGuid().ToString("N");
            if (!Guid.TryParseExact(requestId, "N", out var id)) { itemMessage = "Invalid build request."; return; }
            requestId = id.ToString("N");
            if (siteId.Length == 0) siteId = WorldSave.BuildSite.Id;
            if (siteId.Length > 128) { itemMessage = "Invalid build site."; return; }
            if (!SaveProfile(profile with { LastBuildRequestId = requestId, LastBuildSiteId = siteId, LastAction = "build" }, "build", "nothing built")) return;
        }
        itemMessage = retry ? "Retrying build..." : "Placing wall panel...";
        RpcId(1, nameof(PlaceWall), requestId, siteId);
    }

    // F: free a captive teammate in reach, else loot a cache in reach, else take from the nearest crate.
    private void Interact()
    {
        if (!connected || own is null || snapshot is null) return;
        var me = snapshot.Actors.FirstOrDefault(a => a.Id == own.Id);
        if (me is null) { RequestPickup(false); return; }
        double Reach(double x, double z) => Math.Sqrt((x - me.X) * (x - me.X) + (z - me.Z) * (z - me.Z));
        if (snapshot.Actors.Any(a => a.Id != own.Id && a.Condition == "captive" && Reach(a.X, a.Z) <= WorldAuthority.RescueReach))
            RpcId(1, nameof(RequestInteract), "rescue");
        else if ((snapshot.Caches ?? []).Any(k => Reach(k.X, k.Z) <= WorldAuthority.LootReach))
            RpcId(1, nameof(RequestInteract), "loot");
        else RequestPickup(false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestInteract(string kind)
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        try
        {
            string result = kind == "rescue" ? authority.Rescue(peer) : kind == "loot" ? authority.Loot(peer) : "unknown";
            WorldStorage.Save(savePath, authority.State);   // rescues and loot move items and claim rewards: save before replying
            RpcId(peer, nameof(InteractReply), kind, result, JsonSerializer.Serialize(authority.CharacterFor(peer).Inventory));
            Publish();
            GD.Print($"INTERACT_RESULT peer={peer} kind={kind} result={result}");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { GD.Print("INTERACT_REJECTED ", e.Message); }
        catch (Exception e) { Fail(e); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void InteractReply(string kind, string result, string inventoryJson)
    {
        if (server || own is null) return;
        own.Inventory = JsonSerializer.Deserialize<Dictionary<string, int>>(inventoryJson) ?? own.Inventory;
        lastInteract = result;
        itemMessage = (kind, result) switch
        {
            ("rescue", "rescued") => "You freed your teammate.", ("loot", "looted") => "You took everything in the cache.",
            ("rescue", _) => "Nobody to free here.", _ => "Nothing to take here."
        };
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Retired(string reason)
    {
        if (server) return;
        GD.Print("RETIRED ", reason);
        Disconnected(reason);
        network.Close();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestAsk()
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        try { RpcId(peer, nameof(AskReply), JsonSerializer.Serialize(authority.Ask(peer))); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { GD.Print("ASK_REJECTED ", e.Message); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void AskReply(string json)
    {
        if (server) return;
        lastAsk = JsonSerializer.Deserialize<AskResult>(json, WorldStorage.JsonOptions);
        itemMessage = lastAsk is null || lastAsk.Reason == "nobody" ? "Nobody close enough to ask." : $"{lastAsk.Speaker}: \"{string.Join(" ", lastAsk.Lines.Take(2))}\"";
    }

    private void RequestAttack(string kind)
    {
        if (connected && own is not null) RpcId(1, nameof(Attack), kind);
    }

    // Attacks are not durable transactions: their effects (ammo, damage, deaths) persist with the next autosave.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Attack(string kind)
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        try
        {
            var result = authority.Attack(peer, kind);
            RpcId(peer, nameof(AttackReply), JsonSerializer.Serialize(result), JsonSerializer.Serialize(authority.CharacterFor(peer).Inventory));
            GD.Print($"ATTACK_RESULT peer={peer} kind={kind} result={result.Reason} target={result.TargetId}");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { GD.Print("ATTACK_REJECTED ", e.Message); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void AttackReply(string resultJson, string inventoryJson)
    {
        if (server || own is null) return;
        lastAttack = JsonSerializer.Deserialize<AttackResult>(resultJson, WorldStorage.JsonOptions);
        own.Inventory = JsonSerializer.Deserialize<Dictionary<string, int>>(inventoryJson) ?? own.Inventory;
        itemMessage = lastAttack?.Reason switch
        {
            "killed" => "Target down.", "hit" => $"Hit for {lastAttack.Damage}.", "miss" => "Missed.", "blocked" => "The shot hit a wall.",
            "no_ammo" => "Out of ammo.", "no_weapon" => "No pistol.", "cooldown" => "Not ready yet.", _ => itemMessage
        };
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void PlaceWall(string requestId, string siteId)
    {
        if (!server || authority is null || failed) return;
        int peer = Multiplayer.GetRemoteSenderId();
        BuildReceipt receipt;
        try { receipt = authority.PlaceWall(peer, requestId, siteId); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            var invalid = new BuildReceipt(requestId, "", siteId, false, "invalid_request", 0);
            RpcId(peer, nameof(BuildReply), JsonSerializer.Serialize(invalid), "{}");
            return;
        }
        try
        {
            // Wall, spent panel and receipt share one atomic save. Never acknowledge before it succeeds.
            WorldStorage.Save(savePath, authority.State);
            RpcId(peer, nameof(BuildReply), JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(authority.CharacterFor(peer).Inventory));
            Publish();
            WriteStatus();
            GD.Print($"BUILD_RESULT actor={receipt.ActorId} request={receipt.RequestId} result={receipt.Reason}");
        }
        catch (Exception e) { Fail(e); }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void BuildReply(string receiptJson, string inventoryJson)
    {
        if (server || own is null) return;
        lastBuild = JsonSerializer.Deserialize<BuildReceipt>(receiptJson, WorldStorage.JsonOptions) ?? throw new InvalidDataException("Empty build receipt");
        if (lastBuild.ActorId == own.Id)
            own.Inventory = JsonSerializer.Deserialize<Dictionary<string, int>>(inventoryJson) ?? throw new InvalidDataException("Missing inventory");
        itemMessage = lastBuild.Reason switch
        {
            "placed" => "Wall panel placed.", "occupied" => "Someone already built there.",
            "no_material" => "You need a wall panel from the blue crate.", "out_of_reach" => "Move closer to the build cell.",
            "blocked" => "A player is standing in the build cell.", "unknown_site" => "That build site is unavailable.",
            _ => "Build request rejected."
        };
        GD.Print($"BUILD_REPLY request={lastBuild.RequestId} result={lastBuild.Reason}");
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
            var invalid = new PickupReceipt(requestId, "", containerId, "", false, "invalid_request", 0);
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
            "taken" => lastPickup.ItemId == WorldSave.Panel ? "Wall panel taken." : "Bandage taken.", "empty" => "The crate is empty.",
            "out_of_reach" => "Move closer to the crate.", "unknown_container" => "That crate is unavailable.",
            "inventory_full" => "Cannot carry more bandages.", _ => "Pickup request rejected."
        };
        GD.Print($"ITEM_REPLY request={lastPickup.RequestId} result={lastPickup.Reason}");
    }

    // Each joined player gets its own snapshot: every player plus only the NPCs near them.
    private void Publish()
    {
        snapshot = authority!.Snapshot();
        foreach (var (peer, _) in authority.ActivePeers)
            RpcId(peer, nameof(ReceiveSnapshot), JsonSerializer.Serialize(authority.Snapshot(peer)));
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
                foreach (var (peer, reason) in authority.PendingKicks)
                {
                    WorldStorage.Save(savePath, authority.State);
                    RpcId(peer, nameof(Retired), reason);
                    network.GetPeer(peer)?.PeerDisconnectLater();
                }
                authority.PendingKicks.Clear();
                if (frame % 2 == 0) Publish();
                SavePopulationInBackground(delta);
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
    // Capture between coarse ticks, write off the main thread, then reference it from the next world save.
    private void SavePopulationInBackground(double delta)
    {
        if (authority?.Pop is null) return;
        populationElapsed += delta;
        if (populationWrite is { IsCompleted: true })
        {
            var (name, hash) = populationWrite.GetAwaiter().GetResult();
            populationWrite = null;
            authority.CommitPopulation(name, hash);
            WorldStorage.Save(savePath, authority.State);
            WorldAuthority.PrunePopulationFiles(savePath, name);
            GD.Print($"POPULATION_SAVED file={name}");
        }
        if (populationWrite is null && authority.CapturedPopulation is not null)
        {
            var bytes = authority.TakeCapturedPopulation()!;
            uint tick = authority.CapturedPopulationTick;
            string path = savePath;
            populationWrite = Task.Run(() => (WorldAuthority.WritePopulationFile(path, tick, bytes), WorldAuthority.Hash(bytes)));
        }
        else if (populationWrite is null && populationElapsed >= populationSaveSeconds && authority.ActiveCount > 0)
        {
            authority.PopulationSaveRequested = true;
            populationElapsed = 0;
        }
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
        if (command.Attack.Length > 0) RequestAttack(command.Attack);
        if (command.Interact.Length > 0) RpcId(1, nameof(RequestInteract), command.Interact);
        if (command.Ask) RpcId(1, nameof(RequestAsk));
        if (command.BuildRequestId.Length > 0 || command.RetryBuild)
            RequestBuild(command.RetryBuild, command.BuildRequestId, command.SiteId);
        // Real key events through Godot's input pipeline; only graphical clients have a view to receive them.
        foreach (string name in command.Keys)
        {
            var key = Enum.Parse<Key>(name, ignoreCase: true);
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        }
        // Held keys stay down until the next command, for continuous inputs such as camera tilt.
        foreach (var key in heldKeys) Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        heldKeys = command.HoldKeys.Select(name => Enum.Parse<Key>(name, ignoreCase: true)).ToArray();
        foreach (var key in heldKeys) Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        if (command.Capture.Length > 0) capturePath = Path.GetFullPath(command.Capture, Workspace);
        if (command.Quit) GetTree().Quit();
    }
    public override void _Process(double delta)
    {
        view?.UpdateWorld(own is null ? null : snapshot, own?.Id ?? "", profileName, connection, own?.Inventory, itemMessage, delta);
        if (capturePath.Length == 0 || view is null) return;
        // Viewport capture for Xvfb screen tests; written beside, then renamed so readers never see a partial PNG.
        GetViewport().GetTexture().GetImage().SavePng(capturePath + ".tmp.png");
        File.Move(capturePath + ".tmp.png", capturePath, overwrite: true);
        capturePath = "";
    }

    private void WriteStatus()
    {
        if (statusPath.Length == 0) return;
        WorldStorage.WriteAtomic(statusPath, JsonSerializer.Serialize(new
        {
            role = server ? "server" : "client", display = DisplayServer.GetName(), connection, profile = profileName,
            ownId = own?.Id, own, active = authority?.ActiveCount ?? snapshot?.Actors.Length ?? 0,
            tick = authority?.State.Tick ?? snapshot?.Tick ?? 0, snapshot,
            characters = authority?.State.Characters, receipts = authority?.State.Receipts, buildReceipts = authority?.State.BuildReceipts, lastPickup, lastBuild, lastAttack, lastInteract, lastAsk,
            opportunities = authority?.State.Opportunities, facts = authority?.State.Facts.Count,
            npcs = authority?.Npcs.Count, populationLagged = authority?.PopulationLagged, agentDeltas = authority?.State.AgentDeltas.Count,
            populationLoad = authority?.PopulationLoad, populationTick = authority?.Pop?.Tick, populationFile = authority?.State.PopulationFile, itemMessage, fps = Engine.GetFramesPerSecond(),
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
        try
        {
            if (serverStarted && authority is not null && savePath.Length > 0)
            {
                populationWrite?.GetAwaiter().GetResult();
                if (authority.CapturePopulationNow() is { } bytes)
                {
                    string name = WorldAuthority.WritePopulationFile(savePath, authority.Pop!.Tick, bytes);
                    authority.CommitPopulation(name, WorldAuthority.Hash(bytes));
                    WorldStorage.Save(savePath, authority.State);
                    WorldAuthority.PrunePopulationFiles(savePath, name);
                }
                WorldStorage.Save(savePath, authority.State);
            }
        }
        catch (Exception e) { GD.PushError("Final save failed: " + e.Message); GetTree().Quit(1); }
        if (server) Console.CancelKeyPress -= OnCancelKeyPress;
        network?.Close();
        saveLock?.Dispose();
    }
}
