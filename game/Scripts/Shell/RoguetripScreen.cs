using Godot;
using I76.Formats.Remix;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// ROGUETRIP (the port's; docs/remix-missions.md "Roguetrip"): a run of four generated jobs across neighbouring maps of the region.
/// One screen in three moods, on the port's paper sheet (PaperSheet, record 16's paper) in the shell's fonts: the choice of the next job
/// (two or three offers: the map, the job, its challenge, a line naming the gang), a job's debrief page (how it ended, the line it
/// ended on, its objectives, what follows: the salvage, a bust's penalty, the run's end; RETRY JOB with the Developer setting), and
/// the run's end (the jobs won and busted, the kills, the run's number). A won job goes on to the trip's own FIELD SALVAGE and
/// garage with the run's inventory. The run lives in user://remix/run.yaml between jobs (the file name of rounds four and five, when
/// the mode was called ROAD TRIP: a run saved then resumes), so quitting the game keeps it; the main menu's MELEE > ROGUETRIP takes
/// it up again. Single player.
/// </summary>
public sealed partial class RoguetripScreen : ShellScreen
{
    public required RemixRun Run { get; set; }
    /// <summary>Said over the sheet on entry (a job that could not be built or loaded).</summary>
    public string? Notice { get; init; }
    /// <summary>A new run just begun on the car pick: Esc (or BACK) before its first job drops it and goes back to the car pick.</summary>
    public bool FromCarPick { get; init; }
    public override bool EscapeOpensOptions => false;

    private List<RunOffer> _offers = [];
    private int _sel;
    private ShellFont _hand = null!, _small = null!, _title = null!, _caps = null!;
    private const int Left = 36, Right = 604, Top = 92, BoxH = 104, BoxGap = 8;
    private static readonly Rect2I Take = new(330, 451, 142, 20), Later = new(490, 451, 142, 20);
    private static readonly string[] Picks = ["repair", "rearm", "bonus"];

    private string Mood => Run.Phase switch { "reward" => "reward", "debrief" => "debrief", "over" or "done" => "end", _ => "choose" };

    /// <summary>The Developer setting (shell.cfg's developer=, or I76_DEV=1): the debrief page offers RETRY JOB.</summary>
    private static bool Developer => ShellSettings.Load().DeveloperTools || System.Environment.GetEnvironmentVariable("I76_DEV") == "1";

    // ---- the run's file ----

    public static string Dir => RemixScenarios.Dir;

    /// <summary>The run in progress in the user folder (a job the game closed during comes back to its choice), or null.</summary>
    public static RemixRun? InProgress()
    {
        var r = RemixRun.Load(Dir);
        if (r is null) return null;
        if (r.Phase == "playing") { r.Resume(); Save(r); }
        return r;
    }

    public static void Save(RemixRun r)
    {
        try { r.Save(Dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"roguetrip: not saved: {ex.Message}"); }
    }

    /// <summary>A run over: its file kept as run-last.yaml (the summary's), the way clear for a new one.</summary>
    private static void Close(RemixRun r)
    {
        try
        {
            File.WriteAllText(Path.Combine(Dir, "run-last.yaml"), r.ToYaml());
            File.Delete(Path.Combine(Dir, RemixRun.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"roguetrip: {ex.Message}"); }
    }

    private static string NumbersFile => Path.Combine(Dir, "runs.txt");

    /// <summary>The last ten run numbers, newest first.</summary>
    public static List<int> Numbers()
    {
        try
        {
            return File.Exists(NumbersFile) ? [.. File.ReadAllLines(NumbersFile).Select(l => int.TryParse(l.Trim(), out int n) ? n : 0).Where(n => n > 0).Distinct().Take(10)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public static void RememberNumber(int n)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(NumbersFile, new[] { n }.Concat(Numbers().Where(x => x != n)).Take(10).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"roguetrip: {ex.Message}"); }
    }

    /// <summary>
    /// Back from a job: the result goes into the run (docs/remix-missions.md "Busts": won, a bust with the player alive, or the
    /// player's death) and its debrief page comes up. A won job's salvage goes into the run's inventory at once (its field), so a
    /// game closed on the salvage screens finds it there again.
    /// </summary>
    public static ShellScreen Back(Shell shell, RunResult? result, string? loadError)
    {
        var run = RemixRun.Load(Dir);
        if (run is null) return new MeleeScreen { Mode = 1, Roguetrip = true };
        if (run.Phase == "playing")
        {
            if (result is null)
            {
                // the job never ran (it could not be built or loaded): the same choice again, saying why
                run.Resume();
                Save(run);
                return new RoguetripScreen { Run = run, Notice = loadError ?? "The job could not be started." };
            }
            int failures = 0;
            try { if (run.Current is { } cur) failures = run.Spec(cur).Text.Failures.Count; } catch (RemixError) { }
            var outcome = RemixRun.Classify(result.Won, result.FailMessage, failures, result.PlayerDead);
            string why = result.Won ? "" : result.Why.Length > 0 ? result.Why : outcome == RemixRun.Outcome.Dead ? "You were killed." : "The job went wrong.";
            run.Finish(outcome, result.Kills, result.Car, why);
            _lastObjectives = result.Objectives;
            if (outcome == RemixRun.Outcome.Won)
            {
                // the trip's own hand-back: the parts' state, the last repair order finished, the job's wrecks in the field
                try
                {
                    var inv = Inventory(run, shell);
                    inv.MissionWon(result.Parts, result.Salvage, shell.Parts, shell.Rand);
                    run.Inventory = inv.Save();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException) { GD.PrintErr($"roguetrip: salvage not taken: {ex.Message}"); }
            }
            GD.Print($"remix: ROGUETRIP {run.Number:000000}: job {run.Legs.Count} {outcome.ToString().ToLowerInvariant()} ({(why.Length > 0 ? why : "done")}), {result.Kills} kills; busts {run.Busts}; next challenge {run.NextChallenge:0.0}");
            Save(run);
        }
        return new RoguetripScreen { Run = run };
    }

    /// <summary>The objectives of the job just ended as they stood (the debrief page's; not kept across a restart of the game).</summary>
    private static IReadOnlyList<(string Text, bool Done, bool Failed)> _lastObjectives = [];

    /// <summary>The run's inventory (the trip's: the car's parts, the van, the repair order), from its file or, the first time, the run's car.</summary>
    public static I76.Sim.Trip.TripInventory Inventory(RemixRun run, Shell shell)
    {
        if (run.Inventory is { Length: > 0 } json && I76.Sim.Trip.TripInventory.Load(json, shell.Parts, shell.Vfs) is { } kept) return kept;
        var vfs = shell.Vfs;
        var car = I76.Sim.Trip.TripCar.FromVcf(vfs.Read(run.Car), n => I76.Sim.Vehicles.VehicleFactory.EngineCompId(vfs, n));
        return I76.Sim.Trip.TripInventory.FromCar(car, shell.Parts, vfs);
    }

    // ---- the screen ----

    /// <summary>The paper sheet as loaded (the job map's scrap takes its paper from it).</summary>
    private Pcx? _sheet;
    private ShellFont _map = null!;
    private RoguetripMapLayer? _layer;
    private RoguetripLayout.RegionPlan? _plan;
    private RoguetripLayout.PagePlan? _page;
    /// <summary>I76_ROGUE_NOTEPAD=beside: the slip always in a band of its own under a smaller job map (the alternative layout, for comparing).</summary>
    private static bool NotepadBeside => System.Environment.GetEnvironmentVariable("I76_ROGUE_NOTEPAD") == "beside";

    public override void Enter()
    {
        _hand = Shell.Db.Font(0x45);
        _small = Shell.Db.Font(RoguetripLayout.TextFont);
        _title = Shell.Db.Font(0x43);
        _caps = Shell.Db.Font(0x47);
        _map = Shell.Db.Font(RoguetripLayout.MapFont);
        // a co-op roguetrip's route map and debrief, read from the host's run (RoguetripScreen.Coop.cs); its sheet goes from
        // the choice to the debrief and back on one screen, so the map's layers are made whatever it shows first
        if (CoopView is not null && !Shell.Faithful) MapLayers();
        if (CoopEnter()) return;
        // a won job's salvage and garage: the trip's own screens on the run's inventory
        if (Run.Phase == "salvage") { Salvage(); return; }
        if (Mood == "choose") _offers = Run.Offers(RemixNodes.Builtin(), Shell.Install.IsNitro);
        _sel = 0;
        // the modern look: the region's lines and rings at the window's resolution (the 1997 look: the frame's pixels)
        if (!Shell.Faithful && Mood == "choose") MapLayers();
        Repaint();
        // a run made by another version of the generator rolls other jobs from the same number: say so
        if (Notice is null && Run.Builder != RemixBuilder.Version && Mood == "choose")
            Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = Repaint, Text = $"This run was made by builder v{Run.Builder}; this game's is v{RemixBuilder.Version}, so its jobs differ." });
        if (Notice is { Length: > 0 } n) Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = n, Closed = Repaint });
    }

    /// <summary>The region's drawing and the atlas under it, as layers over the frame (the modern look); they show only on the choice.</summary>
    private void MapLayers()
    {
        if (_layer is not null) return;
        _layer = new RoguetripMapLayer
        {
            Plan = () => Mood == "choose" ? _plan : null, Covered = () => Shell.ModalOpen || Shell.OverlayOpen, OnAtlas = () => _atlasShown,
        };
        Shell.AddLayerOverFrame(_layer);
        // (added second, so it lies under the drawing: each layer goes in just over the frame)
        _atlasLayer = new RoguetripAtlasLayer { Picture = () => Mood == "choose" && _atlasShown ? RoguetripAtlas.Texture(Dir) : null, Covered = () => Shell.ModalOpen || Shell.OverlayOpen };
        Shell.AddLayerOverFrame(_atlasLayer);
    }

    public override void Leave()
    {
        _layer?.QueueFree();
        _layer = null;
        _atlasLayer?.QueueFree();
        _atlasLayer = null;
    }

    private RoguetripAtlasLayer? _atlasLayer;
    /// <summary>The player's atlas is drawn under the region (it is there and read: until then, and without it, the drawn map).</summary>
    private bool _atlasShown;

    private int Count => Mood switch { "choose" => _offers.Count, "reward" => Picks.Length, _ => 0 };

    /// <summary>The debrief page's second button: RETRY JOB with the Developer setting on.</summary>
    private static readonly Rect2I Retry = Take;   // (the sheet's middle button: TAKE THE JOB's)

    /// <summary>The choice's selectable places (the offers' rings), or the reward page's boxes.</summary>
    private Rect2I Box(int i)
    {
        if (Mood == "choose") return _hits.TryGetValue(i, out var r) ? r : default;
        return new(Left, Top + i * (BoxH + BoxGap), Right - Left, BoxH);
    }

    // ---- the choice map (docs/remix-missions.md "The choice map"): the region with the offers as points of interest ----

    /// <summary>A pen line on the frame (Bresenham), solid or dashed (on, off pixels): the 1997 look's.</summary>
    private void Pen(int x0, int y0, int x1, int y1, byte ink, int on = 0, int off = 0, int width = 1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy, n = 0;
        while (true)
        {
            if (on == 0 || n % (on + off) < on) Frame.Fill(x0, y0, width, width, ink);
            n++;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void Ring(int cx, int cy, int r, byte ink, int width = 1)
    {
        int steps = Math.Max(16, r * 8);
        for (int i = 0; i < steps; i++)
        {
            double a = 2 * Math.PI * i / steps;
            Frame.Fill(cx + (int)Math.Round(r * Math.Cos(a)), cy + (int)Math.Round(r * Math.Sin(a)), width, width, ink);
        }
    }

    private static int R(float v) => (int)MathF.Round(v);

    /// <summary>
    /// The region: its plan (RoguetripLayout.Region: the lines, the towns, the trip's maps, the run's road, the offers, the words
    /// placed clear of each other), its lines and rings drawn by the modern look's layer or here into the pixels (the 1997 look),
    /// its words printed as the stock forms print their labels (Arial Bold capitals; Print), at the window's resolution in the modern look.
    /// </summary>
    private void DrawRegion()
    {
        var reg = RemixRegion.Builtin();
        // the drawn map unless the atlas is ready: absent, still being read (a second or two the first time) or unreadable, it stands alone
        _atlasState = RoguetripAtlas.State(Dir);
        bool atlas = _atlasShown = RoguetripLayout.AtlasShown(_atlasState);
        // the map fills the sheet; the shown offer's scrap lies over its east side (no word goes under it)
        _plan = RoguetripLayout.Region(reg, RoguetripLayout.View, _offers.Select(o => o.Map).ToList(), _sel, Run.Legs.Select(l => l.Map).ToList(), Run.Map,
            t => _map.Measure(t) * RoguetripLayout.MapScale, RoguetripLayout.MapCell, atlas, _offers.Count > 0 ? RoguetripLayout.ScrapBox : null);
        var plan = _plan;
        byte ink = Frame.Nearest(RoguetripMapLayer.Ink), road = Frame.Nearest(RoguetripMapLayer.Road), pencil = Frame.Nearest(RoguetripMapLayer.Pencil),
            red = Frame.Nearest(RoguetripMapLayer.Marker), redPencil = Frame.Nearest(RoguetripMapLayer.RedPencil);
        if (Shell.Faithful)
        {
            if (atlas && RoguetripAtlas.Small(Dir) is { } bg)
            {
                // the player's atlas matched to the palette, under the drawing
                var near = new Dictionary<int, byte>();
                for (int y = 0; y < bg.H; y++)
                    for (int x = 0; x < bg.W; x++)
                    {
                        int o = (y * bg.W + x) * 4;
                        if (bg.Rgba[o + 3] < 128) continue;
                        // laid on the sheet as print is (multiplied; its paper was made white, so the sheet's shows; the palette has no cream)
                        int fx = (int)RoguetripLayout.MapBox.X + x, fy = (int)RoguetripLayout.MapBox.Y + y, under = Frame.At(fx, fy) * 3;
                        int r = Frame.Palette[under] * bg.Rgba[o] / 255, g = Frame.Palette[under + 1] * bg.Rgba[o + 1] / 255, b = Frame.Palette[under + 2] * bg.Rgba[o + 2] / 255;
                        int key = r >> 3 << 10 | g >> 3 << 5 | b >> 3;
                        if (!near.TryGetValue(key, out byte v)) near[key] = v = Frame.Nearest(Color.Color8((byte)r, (byte)g, (byte)b));
                        Frame.Fill(fx, fy, 1, 1, v);
                    }
            }
            foreach (var s in plan.Segments)
                switch (s.Kind)
                {
                    case RoguetripLayout.Ink.State: Pen(R(s.X0), R(s.Y0), R(s.X1), R(s.Y1), pencil, 5, 3); break;
                    case RoguetripLayout.Ink.Run: Pen(R(s.X0), R(s.Y0), R(s.X1), R(s.Y1), red, 3, 2, 2); break;
                    case RoguetripLayout.Ink.Approach: Pen(R(s.X0), R(s.Y0), R(s.X1), R(s.Y1), red, 4, 3, 2); break;
                    default: Pen(R(s.X0), R(s.Y0), R(s.X1), R(s.Y1), road); break;
                }
            foreach (var d in plan.Dots)
            {
                if (d.Kind == "town") Frame.Fill(R(d.X) - 1, R(d.Y) - 1, 3, 3, ink);
                else if (d.Kind == "visited") Frame.Fill(R(d.X) - 2, R(d.Y) - 2, 5, 5, ink);
                else Ring(R(d.X), R(d.Y), 2, atlas ? ink : pencil);
            }
            foreach (var o in plan.Offers)
            {
                // on the atlas: the ring's inside cleared to paper, so its number reads
                int ri = (int)RoguetripLayout.Ring, cl = ri - 1;
                if (atlas) for (int dy = -cl; dy <= cl; dy++) for (int dx = -cl; dx <= cl; dx++) if (dx * dx + dy * dy <= cl * cl) Frame.Fill(R(o.X) + dx, R(o.Y) + dy, 1, 1, _sheet!.Pixels[(R(o.Y) + dy) * ShellFrame.W + R(o.X) + dx]);
                Ring(R(o.X), R(o.Y), ri, o.Selected ? red : ink, o.Selected ? 2 : 1);
                if (o.Selected) Ring(R(o.X), R(o.Y), (int)RoguetripLayout.OuterRing, red);
            }
        }
        foreach (var l in plan.Labels)
        {
            byte c = l.Kind switch { "road" => redPencil, "state" => pencil, _ => ink };
            Print(l.Rect, l.Text, c);
        }
        foreach (var o in plan.Offers)
        {
            string n = (o.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Frame.Text(_small, R(o.X) - _small.Measure(n) / 2, R(o.Y) - _small.Height / 2 - 1, n, Solid(_small, ink));
            int hr = (int)RoguetripLayout.OuterRing;
            _hits[o.Index] = new Rect2I(R(o.X) - hr, R(o.Y) - hr, 2 * hr, 2 * hr);
        }
    }

    private readonly Dictionary<int, Rect2I> _hits = [];

    /// <summary>
    /// A word the map prints (a town, a road number, a state): Arial Bold as the stock forms print their labels, its capitals
    /// <see cref="RoguetripLayout.MapCap"/> px high inside the word's cell (the cell is the small sans's measure, which the layout
    /// places); at the window's resolution in the modern look, printed at the pixel size that fits in the 1997 look. Without Arial,
    /// the small sans.
    /// </summary>
    private void Print(RoguetripLayout.Box cell, string text, byte ink)
    {
        int cap = RoguetripLayout.MapCap;
        var box = new Rect2I(R(cell.X), R(cell.Y + (cell.H - cap) / 2), Math.Max(1, (int)MathF.Floor(cell.W)), cap);
        if (Frame.Modern ? Frame.Label(box, text, ink, 0) : PrintPx(text, box.Size) is int px && Frame.Label(box, text, ink, px)) return;
        Frame.Text(_map, R(cell.X), R(cell.Y), text, Solid(_map, ink));
    }

    private static readonly Dictionary<(string, Vector2I), int?> PrintSizes = [];

    /// <summary>The largest pixel size at which Arial Bold prints the words inside the box (the 1997 look), or null.</summary>
    private static int? PrintPx(string text, Vector2I box)
    {
        if (PrintSizes.TryGetValue((text, box), out var have)) return have;
        // one size for every word whose capitals are the box's height (a word wider than its box a size or two down)
        int? best = null;
        for (int px = CapPx(box.Y); px >= 5 && best is null; px--)
            if (ShellGlyphs.LabelPixels(text, px) is { } r && r.W <= box.X + 2) best = px;
        return PrintSizes[(text, box)] = best;
    }

    private static readonly Dictionary<int, int> CapSizes = [];

    /// <summary>The pixel size at which Arial Bold's capitals are <paramref name="cap"/> px high (the 1997 look's print).</summary>
    private static int CapPx(int cap)
    {
        if (CapSizes.TryGetValue(cap, out int px)) return px;
        int best = 5;
        for (int p = 5; p <= 24; p++)
            if (ShellGlyphs.LabelPixels("H", p) is { } r && r.H <= cap) best = p;
        return CapSizes[cap] = best;
    }
    private readonly Dictionary<(int Seed, int W, int H), (I76.Formats.Mission.ArenaSketch Sketch, RoguetripLayout.MarkPlan Marks)?> _jobMaps = [];

    /// <summary>The paper the job map's scrap is cut from: a plain part of the sheet (under the region, which is drawn later).</summary>
    private static readonly (int X, int Y) ScrapPaperAt = (40, 120);
    private const int ScrapPad = 5, LegendCap = 7;

    /// <summary>The job's map at a size, made once (null: the map could not be drawn).</summary>
    private (I76.Formats.Mission.ArenaSketch Sketch, RoguetripLayout.MarkPlan Marks)? JobMapAt(RunOffer offer, int w, int h)
    {
        if (w < 40 || h < 30) return null;
        if (_jobMaps.TryGetValue((offer.Seed, w, h), out var made)) return made;
        made = null;
        try
        {
            made = RoguetripLayout.JobMap(new GameInstall(Shell.Install.Root!), Run.Spec(offer), w, h, t => _map.Measure(t) * RoguetripLayout.JobScale, RoguetripLayout.JobCell,
                RoguetripLayout.JobRing);
        }
        catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException) { GD.PrintErr($"roguetrip: the job's map: {ex.Message}"); }
        return _jobMaps[(offer.Seed, w, h)] = made;
    }

    /// <summary>
    /// The shown offer's page over the map's east side: a scrap of paper as the host form lays its arena's sketch (ArenaSketches.DrawScrap:
    /// the sheet's paper lightened, torn edges, turned a little, a shadow) carrying the job's own map as large as the half allows (the
    /// whole map's sketch, its marks ringed in red with their words printed clear of each other and of its routes), and on it a notepad
    /// slip (whiter paper, ruled in blue with a red margin, as the notepad the objectives are read on) where the map carries nothing that
    /// matters (RoguetripLayout.Page places it). Both kept whole under the words.
    /// </summary>
    private void DrawOfferPage(RunOffer offer, RoguetripLayout.PagePlan page)
    {
        if (_sheet is null) return;
        var sb = page.SketchBox;
        int bx = R(sb.X), by = R(sb.Y);
        var box = RoguetripLayout.ScrapBox;
        var scrap = new Rect2I(R(box.X) - bx, R(box.Y) - by, R(box.W), R(box.H));
        if (JobMapAt(offer, (int)sb.W, (int)sb.H) is { } m)
            ArenaSketches.DrawScrap(Frame, _sheet.Pixels, ScrapPaperAt, $"roguetrip-page:{offer.Seed}:{page.Band}", m.Sketch, bx, by, scrap, "", 0, 0, LegendCap, RoguetripMapLayer.Ink,
                (k, aliased) => JobInk(m.Sketch, m.Marks, k, aliased, RoguetripLayout.JobCap), turn: 0.3f, keep: true);
        else if ((_blank ??= BlankSketch()) is { } blank)
            ArenaSketches.DrawScrap(Frame, _sheet.Pixels, ScrapPaperAt, "roguetrip-page:blank", blank, bx, by, scrap, "", 0, 0, LegendCap, RoguetripMapLayer.Ink, turn: 0.3f, keep: true);
        // the notepad slip
        var sl = page.Slip;
        int w = (int)MathF.Ceiling(sl.W), h = (int)MathF.Ceiling(sl.H);
        if (I76.Formats.Mission.ArenaSketch.Build((_, _) => null, [], [System.Numerics.Vector2.Zero], [], w, h) is not { } pad) return;   // (a box of the slip's size; its ink is SlipInk)
        var keepPaper = ArenaSketches.ScrapPaper;
        // whiter paper in the modern look (the 1997 look's palette has no such colours: there it stays the sheet's, lightened)
        if (Frame.Modern) ArenaSketches.ScrapPaper = ArenaSketches.Paper.Whiter;
        var rules = page.Lines.Select(l => l.Y - sl.Y + (l.Big ? RoguetripLayout.TitleLine : RoguetripLayout.SlipLineH) - 1).ToList();
        ArenaSketches.DrawScrap(Frame, _sheet.Pixels, ScrapPaperAt, $"roguetrip-slip:{offer.Seed}:{w}x{h}:{page.Band}", pad, R(sl.X), R(sl.Y), new Rect2I(0, 0, w, h), "", 0, 0, LegendCap,
            RoguetripMapLayer.Ink, (k, aliased) => SlipInk(w, h, rules, k, aliased), turn: 0.15f, keep: true);
        ArenaSketches.ScrapPaper = keepPaper;
    }

    /// <summary>The slip's ruling: a pale blue line under each line of words, a red margin down its left.</summary>
    private static byte[] SlipInk(int w, int h, List<float> rules, int k, bool aliased)
    {
        int W = w * k, H = h * k;
        var px = new byte[W * H * 4];
        void Put(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int o = (y * W + x) * 4;
            (px[o], px[o + 1], px[o + 2], px[o + 3]) = ((byte)c.R8, (byte)c.G8, (byte)c.B8, (byte)Math.Clamp(a * 255, 0, 255));
        }
        var blue = Color.Color8(120, 150, 196);
        foreach (float r in rules)
        {
            int y = (int)MathF.Round(r * k);
            for (int x = 3 * k; x < W - 3 * k; x++) for (int t = 0; t < Math.Max(1, k / 3); t++) Put(x, y + t, blue, aliased ? 1 : 0.55f);
        }
        int mx = (int)MathF.Round(5f * k);
        for (int y = 2 * k; y < H - 2 * k; y++) for (int t = 0; t < Math.Max(1, k / 3); t++) Put(mx + t, y, RoguetripMapLayer.Marker, aliased ? 1 : 0.5f);
        return px;
    }

    /// <summary>A sketch with no ink (a job whose map could not be drawn still gets its scrap).</summary>
    private static I76.Formats.Mission.ArenaSketch? _blank;
    private static I76.Formats.Mission.ArenaSketch? BlankSketch() => I76.Formats.Mission.ArenaSketch.Build((_, _) => null, [], [System.Numerics.Vector2.Zero], [], 8, 8);

    /// <summary>The job map's ink: the sketch's roads and edges (its hatching left out: at this size it fills the high ground solid), the marks' rings in red marker and their words in red pencil.</summary>
    private byte[] JobInk(I76.Formats.Mission.ArenaSketch sk, RoguetripLayout.MarkPlan marks, int k, bool aliased, int cap = RoguetripLayout.MapCap)
    {
        var px = (byte[])sk.Render(k, aliased).Clone();
        int W = sk.BoxWidth * k, H = sk.BoxHeight * k;
        for (int i = 0; i < px.Length; i += 4)
        {
            int r0 = px[i], g0 = px[i + 1], b0 = px[i + 2];
            int dSep = (r0 - 92) * (r0 - 92) + (g0 - 58) * (g0 - 58) + (b0 - 26) * (b0 - 26), dInk = (r0 - 40) * (r0 - 40) + (g0 - 30) * (g0 - 30) + (b0 - 18) * (b0 - 18);
            int dGra = (r0 - 58) * (r0 - 58) + (g0 - 50) * (g0 - 50) + (b0 - 44) * (b0 - 44);
            if (dGra < dSep && dGra < dInk) px[i + 3] = 0;
        }
        void Put(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || a <= 0) return;
            int o = (y * W + x) * 4;
            float have = px[o + 3] / 255f, na = a + have * (1 - a);
            for (int ch = 0; ch < 3; ch++)
            {
                float v = ch == 0 ? c.R8 : ch == 1 ? c.G8 : c.B8;
                px[o + ch] = (byte)Math.Clamp((v * a + px[o + ch] * have * (1 - a)) / Math.Max(na, 1e-4f), 0, 255);
            }
            px[o + 3] = (byte)Math.Clamp(na * 255, 0, 255);
        }
        // the rings: 4 px across, a pixel and a half thick (hard in the 1997 look)
        foreach (var (rx, ry, _) in marks.Rings)
        {
            float cx = rx * k, cy = ry * k, rr = marks.Ring * k, half = (aliased ? 0.5f : 0.75f) * k;
            for (int y = (int)(cy - rr - half - 1); y <= cy + rr + half + 1; y++)
                for (int x = (int)(cx - rr - half - 1); x <= cx + rr + half + 1; x++)
                {
                    float d = MathF.Abs(MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - rr);
                    float a = aliased ? (d <= half ? 1 : 0) : Math.Clamp(half - d + 0.5f, 0, 1);
                    Put(x, y, RoguetripMapLayer.Marker, a);
                }
        }
        // a word set away from its ring: a fine red leader to it
        foreach (var l in marks.Labels)
            if (RoguetripLayout.Leader(l, marks.Ring) is var (lx0, ly0, lx1, ly1))
            {
                float len = MathF.Max(1, MathF.Sqrt((lx1 - lx0) * (lx1 - lx0) + (ly1 - ly0) * (ly1 - ly0)));
                for (float t = 0; t <= len; t += 0.5f / k)
                    Put((int)((lx0 + (lx1 - lx0) * t / len) * k), (int)((ly0 + (ly1 - ly0) * t / len) * k), RoguetripMapLayer.RedPencil, aliased ? 1 : 0.8f);
            }
        // the words printed as the map's others are: Arial Bold capitals, MapCap high in the word's cell (the small sans without Arial)
        // (the marks' words at the cap the plan was made for)
        foreach (var l in marks.Labels)
        {
            int bw = Math.Max(1, (int)MathF.Floor(l.Rect.W)) * k, bh = cap * k;
            int bx = (int)MathF.Round(l.Rect.X * k), by = (int)MathF.Round((l.Rect.Y + (l.Rect.H - cap) / 2) * k);
            byte[]? cov = null;
            int cw = 0, chh = 0;
            if (aliased)
            {
                if (PrintPx(l.Text, new Vector2I(bw, bh)) is int p && ShellGlyphs.LabelPixels(l.Text, p) is { } r) { cov = r.A; cw = r.W; chh = r.H; bx += (bw - cw) / 2; by += (bh - chh + 1) / 2; }
            }
            else if (ShellGlyphs.LabelPixels(l.Text, 64) is { } r)
            {
                var img = Image.CreateFromData(r.W, r.H, false, Image.Format.L8, r.A);
                img.Resize(bw, bh, Image.Interpolation.Lanczos);
                cov = img.GetData(); cw = bw; chh = bh;
            }
            if (cov is not null)
            {
                for (int y = 0; y < chh; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        float a = cov[y * cw + x] / 255f;
                        Put(bx + x, by + y, RoguetripMapLayer.RedPencil, aliased ? (a >= 0.5f ? 1 : 0) : a);
                    }
                continue;
            }
            float pen = l.Rect.X;
            foreach (char ch in l.Text)
            {
                if (_map.Glyph(ch) is not { } g) continue;
                int gx = (int)MathF.Round(pen * k), gy = (int)MathF.Round(l.Rect.Y * k);
                for (int y = 0; y < _map.Height; y++)
                    for (int x = 0; x < g.Width; x++)
                        if (g.Px[y * g.Width + x] != _map.Key)
                            for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) Put(gx + x * k + i, gy + y * k + j, RoguetripMapLayer.RedPencil, 1);
                pen += g.Width;
            }
        }
        return px;
    }

    // ---- the sheet's title and buttons: lettered as the stock forms' (the scenario form's art, record 25) ----

    /// <summary>The scenario form's art in this sheet's palette, its header's and buttons' boxes (measured off the art).</summary>
    private Func<int, int, byte>? _form;
    private static readonly Rect2I FormHeader = new(225, 4, 402, 30), FormButton = new(229, 451, 142, 20), FormLabel = new(233, 454, 134, 14);
    private const string FormHeaderText = "AUTO MELEE•DRIVER ENTRY FORM", FormButtonText = "ENTER AREA";

    private Func<int, int, byte> Form()
    {
        if (_form is not null) return _form;
        var art = Shell.Db.Background(25);
        var map = new byte[256];
        for (int i = 0; i < 256; i++) map[i] = Frame.Nearest(Color.Color8(art.Palette[i * 3], art.Palette[i * 3 + 1], art.Palette[i * 3 + 2]));
        return _form = (x, y) => map[art.Pixels[Math.Clamp(y, 0, art.Height - 1) * art.Width + Math.Clamp(x, 0, art.Width - 1)]];
    }

    /// <summary>The title in the stock forms' heavy printed sans (as AUTO MELEE · DRIVER ENTRY FORM is), centred on the sheet.</summary>
    private void Title(string t)
    {
        if (ArtButtons.Label(Form(), Frame.Palette, FormHeader) is { } like)
        {
            ArtButtons.Letter(Frame, _caps, t, like, FormHeaderText, new Rect2I(0, like.Box.Position.Y, 640, like.Box.Size.Y));
            return;
        }
        var ink = Solid(_title, Frame.Nearest(Color.Color8(82, 66, 41)));
        int x = 320 - _title.Measure(t) / 2;
        Frame.Text(_title, x, 12, t, ink);
        Frame.Text(_title, x + 1, 12, t, ink);
    }

    /// <summary>A button made from the scenario form's ENTER AREA (its face, resized) and lettered as its label is.</summary>
    private void Button(Rect2I s, string text)
    {
        var px = Form();
        var like = ArtButtons.Label(px, Frame.Palette, FormLabel);
        ArtButtons.Face(Frame, px, FormButton, like?.Box.Position.X ?? FormLabel.Position.X, s, 4);
        if (like is not null) ArtButtons.Letter(Frame, _caps, text, like with { Box = like.Box with { Position = new Vector2I(like.Box.Position.X, s.Position.Y + like.Box.Position.Y - FormButton.Position.Y) } }, FormButtonText, s);
        else
        {
            var dark = Solid(_caps, Frame.Nearest(Color.Color8(82, 66, 41)));
            int tx = s.Position.X + (s.Size.X - _caps.Measure(text)) / 2, ty = s.Position.Y + (s.Size.Y - _caps.Height) / 2 + 1;
            Frame.Text(_caps, tx, ty, text, dark);
            Frame.Text(_caps, tx + 1, ty, text, dark);
        }
    }

    public override void Repaint()
    {
        var lines = new List<(int X, int Y, int W, int H)>();
        // the frame's double rule round the sheet, as Control Configuration's
        lines.AddRange([(20, 37, 600, 2), (20, 437, 600, 2), (20, 37, 2, 402), (618, 37, 2, 402), (24, 41, 592, 1), (24, 434, 592, 1), (24, 41, 1, 394), (615, 41, 1, 394)]);
        string num = Run.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Mood != "choose")
            for (int i = 0; i < Count; i++) { var b = Box(i); lines.AddRange([(b.Position.X, b.Position.Y, b.Size.X, 1), (b.Position.X, b.End.Y, b.Size.X, 1)]); }
        // (no button bars of the sheet's own: the buttons are the scenario form's)
        _sheet = PaperSheet.Make(Shell.Db.Background(16), true, ControllerScreen.Slots.Length, false, lines);
        Frame.LoadBackground(_sheet, 0, 0, black: false);
        void Line(ShellFont f, int x, int y, string t) => Frame.Text(f, x, y, Fit(f, t, Right - x - 8), ShellFrame.OverlayInk);
        void Centre(ShellFont f, int y, string t) => Frame.Text(f, 320 - f.Measure(t) / 2, y, t, ShellFrame.OverlayInk);
        var car = Run.State;
        string condition = car is null ? "the car as built" : $"armour {car.ArmourLeft}%, chassis {car.ChassisLeft}%, ammo {car.AmmoLeft}%";
        switch (Mood)
        {
            case "choose":
                Title("ROGUETRIP");
                // the map fills the sheet, the offers' rings on it; the shown offer on its scrap over the map's east side
                _hits.Clear();
                DrawRegion();
                if (_offers.Count == 0) { _page = null; Centre(_hand, 200, "No jobs on the road from here."); }
                else
                {
                    int sel = Math.Clamp(_sel, 0, _offers.Count - 1);
                    var offer = _offers[sel];
                    string run = $"Run {num}, job {Run.Legs.Count + 1} of {RemixRun.Jobs}{(Run.Busts > 0 ? $" ({Run.Busts} busted)" : "")}. {Run.Model}: {condition}. "
                        + (Run.Map.Length == 0 ? "Pick where the run starts." : $"Leaving {MapName(Run.Map)}.");
                    string count = $"{sel + 1} of {_offers.Count}";
                    // the right half: the job's own map as large as it allows, the offer's words on a notepad slip laid where the map
                    // carries nothing (RoguetripLayout.Page)
                    (I76.Formats.Mission.ArenaSketch Sketch, RoguetripLayout.MarkPlan Marks)? Map(int w, int h) => JobMapAt(offer, w, h);
                    _page = RoguetripLayout.Page(RoguetripLayout.PageArea, Map, OfferTitle(offer, sel), count, offer.Flavour, Notes(), run,
                        t => _hand.Measure(t), t => _small.Measure(t), NotepadBeside);
                    if (_page.Overflow.Count > 0) GD.PrintErr($"roguetrip: the offer's page overflows: {string.Join(" | ", _page.Overflow)}");
                    DrawOfferPage(offer, _page);
                    // written on the slip: in the form's hand, the title in its big hand (over the pictures, kept whole under them)
                    foreach (var l in _page.Lines) Frame.Text(l.Big ? _hand : _small, R(l.X), R(l.Y), l.Text, ShellFrame.OverlayInk);
                    var (ct, cx, cy) = _page.Count;
                    Frame.Text(_small, R(cx), R(cy), ct, Solid(_small, Frame.Nearest(RoguetripMapLayer.RedPencil)));
                }
                // the help in the sheet's foot, left of the buttons (as round six had it under the sheet)
                int hy = 444;
                foreach (var hl in Wrap(_small, CoopHelp ?? "Up, Down, 1-3 or the map's rings choose; Enter takes the job; Esc: back.", Take.Position.X - 30))
                { Frame.Text(_small, 26, hy, hl, ShellFrame.OverlayInk); hy += 12; }
                Button(Take, "TAKE THE JOB");
                Button(Later, "BACK");
                break;
            case "reward":
                Title("JOB WON");
                var last = Run.Legs[^1];
                Centre(_small, 48, $"Run {num}: {Run.Won} of {RemixRun.Jobs} won. {MapName(last.Map)}, {RemixRun.JobWord(last.Template).ToLowerInvariant()}: {last.Kills} kills.");
                Centre(_small, 66, $"The car: {condition}. Pick one before the next job.");
                string[] words2 = ["FULL REPAIR: armour, chassis and parts as new", "FULL AMMO: every gun loaded", "PUSH ON: nothing now, a bonus mark for the run"];
                for (int i = 0; i < Picks.Length; i++)
                {
                    var b = Box(i);
                    if (i == _sel) Mark(b, 0);
                    Line(_hand, b.Position.X + 12, b.Position.Y + 8, $"{i + 1}. {words2[i].Split(':')[0]}");
                    Line(_small, b.Position.X + 30, b.Position.Y + 40, words2[i].Split(':')[1].Trim());
                }
                Centre(_small, 410, "Up and Down choose, Enter takes it; Esc leaves it for later (the run is kept).");
                Button(Take, "TAKE IT");
                Button(Later, "LATER");
                break;
            case "debrief":
            {
                // the job's debrief page: how it ended, the line it ended on, its objectives as they stood, what follows
                var l = Run.Legs[^1];
                var how = Run.Last;
                Title(how == RemixRun.Outcome.Won ? "JOB DONE" : how == RemixRun.Outcome.Bust ? "JOB BUSTED" : "WRECKED");
                Centre(_small, 48, $"Run {num}, job {Run.Legs.Count} of {RemixRun.Jobs}: {MapName(l.Map)}, {RemixRun.JobWord(l.Template).ToLowerInvariant()}, job #{l.Seed}, challenge {l.Challenge:0.0}.");
                int y = 74;
                if (l.Why.Length > 0) foreach (var w in Wrap(_hand, l.Why, Right - Left - 40)) { Centre(_hand, y, w); y += 26; }
                else { Centre(_hand, y, "The job's done."); y += 26; }
                y += 8;
                foreach (var (text, ok, failed) in _lastObjectives)
                {
                    var wl = Wrap(_small, text, Right - Left - 110);
                    Line(_small, Left + 20, y, ok ? "DONE" : failed ? "FAILED" : "--");
                    foreach (var w in wl.Take(2)) { Line(_small, Left + 90, y, w); y += 16; }
                    y += 4;
                    if (y > 300) break;
                }
                y = Math.Max(y + 10, 318);
                Centre(_small, y, $"{l.Kills} kills. The car: {condition}.");
                string next = how switch
                {
                    RemixRun.Outcome.Won => Run.Legs.Count >= RemixRun.Jobs ? "That was the last job of the run." : CoopView is not null ? CoopWonNext : "Next: what's left of their cars, in FIELD SALVAGE, then the garage.",
                    RemixRun.Outcome.Bust => Run.Legs.Count >= RemixRun.Jobs ? "A bust, and the last job of the run." : $"A bust: no salvage from this one, and word gets round: the next job is a step harder ({Run.NextChallenge:0.0}).",
                    _ => "The run ends here.",
                };
                foreach (var w in Wrap(_small, next, Right - Left - 40)) { y += 18; Centre(_small, y, w); }
                Button(Later, "CONTINUE");
                if (Developer && CoopView is null) Button(Retry, "RETRY JOB");
                break;
            }
            default:
            {
                bool done = Run.Phase == "done";
                Title(done ? "ROGUETRIP DONE" : "ROGUETRIP OVER");
                Centre(_hand, 70, done ? (Run.Busts == 0 ? "Four jobs, four wins. The road's yours." : $"Four jobs done, {Run.Busts} of them busts.") : "The run ends here.");
                Centre(_small, 104, $"Run {num}: {Run.Won} of {RemixRun.Jobs} jobs won, {Run.Busts} busted, {Run.Kills} kills.");
                int y = 136;
                foreach (var l in Run.Legs)
                {
                    string res = l.Outcome switch { "bust" => "busted", "dead" => "killed", "won" => "won", _ => l.Won ? "won" : "lost" };
                    Centre(_small, y, $"{MapName(l.Map)}: {RemixRun.JobWord(l.Template).ToLowerInvariant()} at {l.Challenge:0.0}, {res}, {l.Kills} kills");
                    y += 20;
                }
                Centre(_small, 390, $"Run number {num} offers the same jobs again: type it on ROGUETRIP.");
                Button(Later, "DONE");
                break;
            }
        }
    }

    /// <summary>An offer's first line: its number, the map, the job and its challenge (wrapped whole by the column).</summary>
    public static string OfferTitle(RunOffer o, int i) => $"{i + 1}. {o.Name.ToUpperInvariant()}: {RemixRun.JobWord(o.Template)}, {o.Challenge:0.0}";

    /// <summary>The chosen offer's notes (where its places are, in words): the first two, whole.</summary>
    private List<string> Notes()
    {
        if (_offers.Count == 0) return [];
        try
        {
            // the job's other objectives shown from the start (a defence's "Keep the farms standing."), then where its places are
            var t = Run.Spec(_offers[Math.Clamp(_sel, 0, _offers.Count - 1)]).Text;
            return [.. t.Objectives.Skip(1).Where(o => !o.StartsWith("(hidden)", StringComparison.Ordinal)), .. t.Notes.Take(2)];
        }
        catch (RemixError) { return []; }
    }

    private static string MapName(string map) => RemixNodes.Builtin().FirstOrDefault(t => t.Base.Equals(map, StringComparison.OrdinalIgnoreCase)) is { Name.Length: > 0 } t ? t.Name : map.ToUpperInvariant();

    /// <summary>The selected box: a marker's rule down its left side and under its first line (its title's lines on the choice).</summary>
    private void Mark(Rect2I b, int titleLines)
    {
        byte m = Frame.Nearest(Color.Color8(222, 96, 38));
        Frame.Fill(b.Position.X + 2, b.Position.Y + 4, 4, b.Size.Y - 8, m);
        if (Mood == "choose") Frame.Fill(b.Position.X + 12, b.Position.Y + 4 + R(titleLines * RoguetripLayout.OfferLine), b.Size.X - 24, 1, m);
        else Frame.Fill(b.Position.X + 12, b.Position.Y + 28, b.Size.X - 24, 2, m);
    }

    private static byte[] Solid(ShellFont f, byte ink)
    {
        var r = new byte[256];
        Array.Fill(r, ink);
        r[f.Key & 0xff] = 0xff;
        return r;
    }

    private static string Fit(ShellFont f, string t, int w)
    {
        while (t.Length > 0 && f.Measure(t) > w) t = t[..^1];
        return t;
    }

    private static List<string> Wrap(ShellFont f, string text, int w) => RoguetripLayout.Wrap(text, w, t => f.Measure(t));

    public override IReadOnlyList<PadRegion> PadRegions =>
        Mood == "debrief" ? [PadRegion.Of(Later), .. Developer ? new[] { PadRegion.Of(Retry) } : []]
        : [.. Enumerable.Range(0, Count).Select(i => PadRegion.Of(Box(i))), PadRegion.Of(Take), PadRegion.Of(Later)];

    /// <summary>Where the atlas was when the region was last drawn: once it is read, the region is drawn again with it.</summary>
    private RoguetripLayout.AtlasState _atlasState;

    public override void Tick()
    {
        if (CoopTick()) return;
        if (Mood == "choose" && _atlasState == RoguetripLayout.AtlasState.Loading && RoguetripAtlas.State(Dir) != RoguetripLayout.AtlasState.Loading) Repaint();
        foreach (var k in Shell.Keys)
        {
            if (k is Key.Up or Key.Left && Count > 0) { _sel = (_sel + Count - 1) % Count; Shell.Sound(82); Repaint(); }
            else if (k is Key.Down or Key.Right && Count > 0) { _sel = (_sel + 1) % Count; Shell.Sound(82); Repaint(); }
            else if (k is >= Key.Key1 and <= Key.Key3 && (int)(k - Key.Key1) < Count) { _sel = (int)(k - Key.Key1); Shell.Sound(82); Repaint(); }
            else if (k is Key.Enter or Key.KpEnter or Key.Space) { Act(); return; }
            else if (k is Key.Escape) { GoBack(); return; }
        }
        if (!Shell.Clicked) return;
        var m = Shell.Mouse;
        if (Mood == "debrief")
        {
            if (Later.HasPoint(m)) { Act(); return; }
            if (Developer && Retry.HasPoint(m)) { RetryJob(); return; }
            return;
        }
        if (Mood == "choose")
            foreach (var (i, r) in _hits)
                if (r.HasPoint(m) && i < Count)
                {
                    if (_sel == i) { Act(); return; }
                    _sel = i; Shell.Sound(82); Repaint(); return;
                }
        for (int i = 0; i < Count; i++)
            if (Box(i).HasPoint(m))
            {
                if (_sel == i) { Act(); return; }   // a second click on the chosen box takes it
                _sel = i; Shell.Sound(82); Repaint(); return;
            }
        if (Take.HasPoint(m) && Mood != "end") { Act(); return; }
        if (Later.HasPoint(m)) { GoBack(); return; }
    }

    private void GoBack()
    {
        Shell.Sound(82);
        // the debrief page has no way out but on: Esc is CONTINUE
        if (Mood == "debrief") { Continue(); return; }
        if (Mood == "end") Close(Run);
        // one step back: a run begun on the car pick and no job taken yet is dropped (its number is kept with the recent ones), and the
        // car pick comes back; a run under way stays saved and the main menu comes back with MELEE open
        if (BackToCarPick(FromCarPick, Run))
        {
            Drop();
            Shell.Go(new MeleeScreen { Mode = 1, Roguetrip = true });
            return;
        }
        Shell.Go(new MainMenuScreen { OpenMelee = true });
    }

    /// <summary>Where Esc on the choice goes: back to the car pick only for a run just begun there with no job taken.</summary>
    public static bool BackToCarPick(bool fromCarPick, RemixRun run) => fromCarPick && run.Phase == "choose" && run.Legs.Count == 0 && run.Log.Count == 0;

    /// <summary>The run's file removed (a run dropped before its first job).</summary>
    private static void Drop()
    {
        try { File.Delete(Path.Combine(Dir, RemixRun.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"roguetrip: {ex.Message}"); }
    }

    /// <summary>Past the debrief page: a won job's salvage, the next choice, or the run's end.</summary>
    private void Continue()
    {
        Run.Debriefed();
        Save(Run);
        if (Run.Phase == "salvage") { Salvage(); return; }
        Shell.Go(new RoguetripScreen { Run = Run });
    }

    /// <summary>
    /// A won job's FIELD SALVAGE and garage: the trip's own screens on the run's inventory (its field holds the job's wrecks);
    /// DONE comes back here: the parts left in the field are scrapped for armour (RemixRun.Patch, the roguetrip's own), and the
    /// inventory kept for the next job.
    /// </summary>
    private void Salvage()
    {
        I76.Sim.Trip.TripInventory inv;
        try { inv = Inventory(Run, Shell); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
        {
            GD.PrintErr($"roguetrip: no inventory: {ex.Message}");
            Run.Salvaged(Run.Inventory, Run.State);
            Save(Run);
            Shell.Go(new RoguetripScreen { Run = Run });
            return;
        }
        var run = Run;
        Shell.Go(new FieldSalvageScreen
        {
            Own = inv,
            Finished = (done, scrapped) =>
            {
                var car = run.State;
                if (car is { ArmourMax.Length: 4, ChassisMax.Length: 4 })
                    car = RemixRun.Patch(car, car.ArmourMax, car.ChassisMax, scrapped);
                // the parts are the inventory's from here on (its state at the launch); the guns are reloaded, as the trip's are
                if (car is not null) car = new RunCar { Armour = car.Armour, Chassis = car.Chassis, ArmourMax = car.ArmourMax, ChassisMax = car.ChassisMax, ArmourLeft = car.ArmourLeft, ChassisLeft = car.ChassisLeft, AmmoLeft = 100 };
                run.Salvaged(done.Save(), car);
                GD.Print($"remix: ROGUETRIP {run.Number:000000}: salvage done, {scrapped} parts scrapped for plate; armour {car?.ArmourLeft ?? 100}%, chassis {car?.ChassisLeft ?? 100}%");
                Save(run);
                Shell.Go(new RoguetripScreen { Run = run });
            },
        });
    }

    /// <summary>The developer's RETRY JOB: the same job again, the car as it began it.</summary>
    private void RetryJob()
    {
        Shell.Sound(82);
        if (Run.Retry() is not { } offer) return;
        Save(Run);
        GD.Print($"remix: ROGUETRIP {Run.Number:000000}: RETRY JOB {Run.JobIndex} (developer)");
        Launch(offer);
    }

    private void Act()
    {
        Shell.Sound(82);
        switch (Mood)
        {
            case "debrief":
                Continue();
                return;
            case "reward":
                Run.Reward(Picks[_sel]);
                Save(Run);
                Shell.Go(new RoguetripScreen { Run = Run });
                return;
            case "end":
                GoBack();
                return;
        }
        if (_offers.Count == 0) return;
        var offer = _offers[_sel];
        Run.Take(offer);
        Launch(offer);
    }

    /// <summary>The job built as xrun and launched in the run's car (its parts from the run's garage, its damage carried).</summary>
    private void Launch(RunOffer offer)
    {
        RemixSpec spec;
        try
        {
            GameInstall.UserMissionDir = Dir;
            spec = Run.Spec(offer);
            RemixBuilder.Build(new GameInstall(Shell.Install.Root!), spec).WriteTo(Dir);
            File.WriteAllText(Path.Combine(Dir, RemixRun.Stem + ".job.yaml"), RemixDirector.ToYaml(spec));
        }
        catch (Exception ex) when (ex is RemixError or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PrintErr($"remix: ROGUETRIP {Run.Number}: {ex.Message}");
            Run.Resume();
            Save(Run);
            Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = "That job could not be built.", Closed = Repaint });
            return;
        }
        // the reproduction line (docs/remix-missions.md "Reproducing a job"): the game log and the run file, and the pause pad's rows
        var line = RemixRun.LaunchLine(Run, offer, spec, Run.Car, I76.Game.Boot.BuildInfo.Commit);
        Run.Log.Add(line);
        Save(Run);
        GD.Print(line);
        // the car: the run's garage's (its parts and their state) once there is one, else the car picked on the form
        string car = Run.Car;
        I76.Sim.Trip.CarState? parts = null;
        if (Run.Inventory is { Length: > 0 })
            try
            {
                var inv = Inventory(Run, Shell);
                AssetVfs.Addon[RunCarFile] = inv.ToCar().ToVcf();
                car = RunCarFile;
                parts = inv.State();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException) { GD.PrintErr($"roguetrip: the garage's car not used: {ex.Message}"); }
        Shell.Launch(new ShellLaunch(RemixRun.Stem, GameKind.Scenario, car)
        {
            Driver = Run.Driver, Model = Run.Model, ModelIndex = Run.ModelIndex, Roguetrip = true, Carry = Run.State, State = parts, RunBase = offer.Map,
            RemixLabel = RemixRun.Label(Run, offer, spec) is var lab ? lab.Top + "\n" + lab.Bottom : null,
        });
    }

    /// <summary>The run's car as its garage left it, served from memory as the trip's vehscn.vcf is.</summary>
    public const string RunCarFile = "vehrun.vcf";
}
