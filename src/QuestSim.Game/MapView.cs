using Godot;
using QuestSim.Domain;

// M: provisional full-transparency debug map of the settlement economy (director 2026-10-26; players will normally learn
// this through rumors). Settlements are discs by faction and size, darker when food is low, ringed red while reporting a
// shortage and marked with a bar for order; hovering one shows its stock and profile. Distance from the plaza is drawn
// on a square-root scale so the dense city and the far-off towns both fit.
public partial class MapView : Control
{
    private List<Settlement> settlements = [];
    private List<(int A, int B)> links = [];
    private SettlementInfo[] infos = [];
    private FactionInfo[] factions = [];
    private Vector2 player;
    private Rect2 bounds;
    private float reach = 1;
    private static readonly Color[] FactionColours = [new(.35f, .55f, .9f), new(.9f, .75f, .35f), new(.82f, .2f, .24f)];

    public void SetCity(CityGen city)
    {
        (settlements, links) = city.Settlements();
        reach = settlements.Max(s => new Vector2(s.X, s.Z).Length()) + 300;
        var points = settlements.Select(s => Compress(s.X, s.Z)).ToArray();
        float margin = reach * .04f;
        bounds = new Rect2(points.Min(p => p.X) - margin, points.Min(p => p.Y) - margin, 0, 0);
        bounds.End = new Vector2(points.Max(p => p.X) + margin, points.Max(p => p.Y) + margin);
    }

    public void UpdateMap(SettlementInfo[]? state, FactionInfo[]? factionState, Vector2 position)
    {
        infos = state ?? [];
        factions = factionState ?? [];
        player = position;
        if (Visible) QueueRedraw();
    }

    private (Vector2 Origin, float Scale) Frame()
    {
        var area = GetViewportRect().Size - new Vector2(40, 160);
        float scale = Math.Min(area.X / bounds.Size.X, area.Y / bounds.Size.Y);
        var origin = new Vector2(20, 120) + (area - bounds.Size * scale) / 2;
        return (origin, scale);
    }

    private Vector2 Compress(float x, float z)
    {
        var v = new Vector2(x, z);
        float d = v.Length();
        return d < 1 ? v : v / d * Mathf.Sqrt(d / reach) * reach;
    }

    private Vector2 ToScreen(float x, float z, (Vector2 Origin, float Scale) f) => f.Origin + (Compress(x, z) - bounds.Position) * f.Scale;

    private float Radius(SettlementInfo? info) => 2 + Mathf.Sqrt((info?.Residents ?? 0) / 90f);

    public override void _Process(double delta) { if (Visible) QueueRedraw(); }

    public override void _Draw()
    {
        if (settlements.Count == 0) return;
        var frame = Frame();
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), new Color(.04f, .05f, .07f, .93f));
        foreach (var (a, b) in links)
            DrawLine(ToScreen(settlements[a].X, settlements[a].Z, frame), ToScreen(settlements[b].X, settlements[b].Z, frame), new Color(.3f, .3f, .32f), 1);
        var byIndex = infos.ToDictionary(i => i.Index);
        SettlementInfo? hovered = null;
        float best = 14;
        var mouse = GetLocalMousePosition();
        foreach (var s in settlements)
        {
            byIndex.TryGetValue(s.Index, out var info);
            var at = ToScreen(s.X, s.Z, frame);
            float radius = Radius(info);
            var colour = info is null ? new Color(.4f, .4f, .4f) : FactionColours[Math.Clamp(info.Faction, 0, 2)];
            float food = info is null ? 1 : Mathf.Clamp(info.Days10[0] / 100f, .2f, 1);
            DrawCircle(at, radius, colour * new Color(food, food, food));
            if (info is { Short: not 0 }) DrawArc(at, radius + 3, 0, Mathf.Tau, 24, (info.Short & 1) != 0 ? new Color(1, .2f, .2f) : new Color(1, .6f, .2f), 2);
            if (info is not null) DrawRect(new Rect2(at + new Vector2(-radius, radius + 2), new Vector2(2 * radius * info.Order / 100f, 2)), new Color(.6f, .9f, .6f));
            if (at.DistanceTo(mouse) < best) { best = at.DistanceTo(mouse); hovered = info; }
        }
        // Factions: relations with the others (F20) and the last daily move.
        float y = 128;
        if (factions.Any(f => f.Relations is not null)) DrawRect(new Rect2(GetViewportRect().Size.X - 440, 108, 430, 186), new Color(0, 0, 0, .8f));
        foreach (var f in factions.Where(f => f.Id < 3 && f.Relations is not null))
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(GetViewportRect().Size.X - 430, y), f.Name, HorizontalAlignment.Left, -1, 15, FactionColours[f.Id]);
            DrawMultilineString(ThemeDB.FallbackFont, new Vector2(GetViewportRect().Size.X - 430, y + 18),
                string.Join("  ", Enumerable.Range(0, 3).Where(o => o != f.Id).Select(o => $"{factions[o].Name.Split(' ')[^1]} {f.Relations![o]:+0;-0}")) +
                (f.LastMove.Length > 0 ? $"\n{f.LastMove}" : ""), HorizontalAlignment.Left, 420, 13, -1, new Color(.8f, .8f, .8f));
            y += 58;
        }
        var me = ToScreen(player.X, player.Y, frame);
        DrawCircle(me, 5, Colors.White);
        DrawArc(me, 9, 0, Mathf.Tau, 20, Colors.White, 1.5f);
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(20, GetViewportRect().Size.Y - 32), "Debug map (M): blue Survivors' Union, gold Free Traders, red Hollow Choir; darker = less food.",
            HorizontalAlignment.Left, -1, 15, new Color(.8f, .8f, .8f));
        DrawString(font, new Vector2(20, GetViewportRect().Size.Y - 12), "Red ring = food shortage, orange = medicine; green bar = order. White = you. Hover a settlement for details.",
            HorizontalAlignment.Left, -1, 15, new Color(.8f, .8f, .8f));
        if (hovered is null) return;
        var h = hovered;
        string text = $"Settlement {h.Index} ({Economy.ProfileNames[h.Profile]}, {Population.FactionNames[Math.Clamp(h.Faction, 0, 4)]})\n" +
            $"{h.Residents} residents, order {h.Order}\n" +
            string.Join("\n", Enumerable.Range(0, Economy.Goods).Select(g => $"{Economy.GoodNames[g]}: {h.Days10[g] / 10.0:0.0} days" + ((h.Short >> g & 1) != 0 ? "  SHORT" : "")));
        var box = new Rect2(mouse + new Vector2(16, 8), new Vector2(300, 150));
        box.Position = new Vector2(Math.Min(box.Position.X, GetViewportRect().Size.X - box.Size.X - 4), Math.Min(box.Position.Y, GetViewportRect().Size.Y - box.Size.Y - 30));
        DrawRect(box, new Color(0, 0, 0, .85f));
        DrawMultilineString(font, box.Position + new Vector2(8, 20), text, HorizontalAlignment.Left, 290, 15, -1, Colors.White);
    }
}
