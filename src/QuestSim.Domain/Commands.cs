namespace QuestSim.Domain;

// The in-game debug console. Clients send text; the server runs it here and replies with lines. Admins are character IDs
// listed one per line in a host-edited file next to the save (<save>.admins); no file, or an empty one, means no admins.
public sealed partial class WorldAuthority
{
    public static readonly int[] Speeds = [1, 2, 4, 8, 16];
    public const int MaxCommandLength = 200;
    public int WorldSpeed { get; private set; } = 1;
    public bool AlwaysOn { get; set; }   // keep world time running with nobody connected (default: paused)

    sealed record Command(string Name, bool Admin, string Usage, Func<int, string[], IEnumerable<string>> Run);

    Command[]? commands;
    Command[] Commands => commands ??=
    [
        new("help", false, "help — the commands you may run", (peer, _) => HelpFor(peer)),
        new("time", false, "time — game day, hour and speed", (_, _) => [TimeLine()]),
        new("who", false, "who — connected players and their character IDs", (_, _) =>
            active.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => $"{c.Name} {c.Id}{(admins.Contains(c.Id) ? " (admin)" : "")}")),
        new("speed", true, "speed <1|2|4|8|16> — world speed for everyone", (_, args) => SetSpeed(args)),
        new("alwayson", true, "alwayson <on|off> — keep time running with nobody connected", (_, args) => SetAlwaysOn(args)),
    ];

    HashSet<string> admins = [];
    public IReadOnlySet<string> Admins => admins;

    // One character ID per line; blank lines and # comments are ignored. A missing file means nobody is admin.
    public static HashSet<string> ReadAdmins(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal) : [];

    // Runs one console line for a joined peer. The admin list is passed in fresh each time so host edits apply at once.
    public string[] RunCommand(int peer, string text, IEnumerable<string> adminIds)
    {
        var character = CharacterFor(peer);
        admins = adminIds.ToHashSet(StringComparer.Ordinal);
        var words = (text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        if (text!.Length > MaxCommandLength) return ["Command too long."];
        var command = Commands.FirstOrDefault(c => c.Name == words[0].ToLowerInvariant());
        if (command is null) return [$"Unknown command '{words[0]}'. Type help."];
        if (command.Admin && words.Length > 1 && !admins.Contains(character.Id)) return [$"'{command.Name}' needs admin rights."];
        return command.Run(peer, words[1..]).ToArray();
    }

    IEnumerable<string> HelpFor(int peer)
    {
        bool admin = admins.Contains(CharacterFor(peer).Id);
        return Commands.Where(c => admin || !c.Admin).Select(c => c.Usage)
            .Append(admin ? "You are an admin." : "Admin commands are hidden; the host lists admins in the server's .admins file.");
    }

    public string TimeLine()
    {
        double gameSeconds = State.Tick / (double)TickRate * Population.GameSecondsPerTick, hour = Population.GameHour(gameSeconds);
        long day = (long)((gameSeconds / 3600 + 8) / 24) + 1;
        return $"Day {day}, {(int)hour:00}:{(int)(hour % 1 * 60):00} | speed {WorldSpeed}x{(frames > 0 && HeldSpeed < WorldSpeed - .5 ? $" (holding {HeldSpeed:0.#}x)" : "")} | always-on {(AlwaysOn ? "on" : "off")}";
    }

    IEnumerable<string> SetSpeed(string[] args)
    {
        if (args.Length == 0) return [$"Speed is {WorldSpeed}x."];
        if (!int.TryParse(args[0].TrimEnd('x', 'X'), out int speed) || !Speeds.Contains(speed)) return ["Speed must be 1, 2, 4, 8 or 16."];
        WorldSpeed = speed;
        return [$"Speed set to {WorldSpeed}x."];
    }

    IEnumerable<string> SetAlwaysOn(string[] args)
    {
        if (args.Length == 0) return [$"Always-on is {(AlwaysOn ? "on" : "off")}."];
        if (args[0] is not ("on" or "off")) return ["Use alwayson on or alwayson off."];
        AlwaysOn = args[0] == "on";
        return [$"Always-on {(AlwaysOn ? "on: the world keeps running with nobody connected" : "off: the world pauses when everyone leaves")}."];
    }

    // Up to WorldSpeed steps for one server frame. Above 1x it does fewer steps this frame rather than skip a coarse tick still
    // running in the background, or overrun the frame budget (an overrun frame makes the engine run catch-up frames, which
    // starves the network), so the world runs as fast as the machine allows and history is never thinned. Timing only decides
    // how many steps run now, never what a step does. At 1x nothing changes: a late coarse tick is skipped and counted in
    // PopulationLagged, as before.
    public int StepFrame(double budgetMs = 35)
    {
        int done = 0;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (; done < WorldSpeed; done++)
        {
            double left = budgetMs - System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (done > 0 && left <= 0) break;
            // A coarse tick still running: wait for it with whatever this frame's budget has left.
            if ((done > 0 || WorldSpeed > 1) && Pop is not null && (State.Tick + 1) % PopulationEvery == 0 && populationTask is { IsCompleted: false } task &&
                !task.Wait(TimeSpan.FromMilliseconds(Math.Clamp(left, 0, budgetMs)))) break;
            Step();
        }
        recentSteps[frames++ % recentSteps.Length] = done;
        return done;
    }

    readonly int[] recentSteps = new int[TickRate];
    long frames;
    // Steps per frame over the last second: the speed the server is actually holding.
    public double HeldSpeed => frames == 0 ? 0 : recentSteps.Take((int)Math.Min(frames, recentSteps.Length)).Average();
}
