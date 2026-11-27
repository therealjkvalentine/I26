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
public sealed class RoguetripScreen : ShellScreen
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
    private RoguetripLayout.ColumnPlan? _column;

    public override void Enter()
    {
        _hand = Shell.Db.Font(0x45);
        _small = Shell.Db.Font(RoguetripLayout.TextFont);
        _title = Shell.Db.Font(0x43);
        _caps = Shell.Db.Font(0x47);
        _map = Shell.Db.Font(RoguetripLayout.MapFont);
        // a won job's salvage and garage: the trip's own screens on the run's inventory
        if (Run.Phase == "salvage") { Salvage(); return; }
        if (Mood == "choose") _offers = Run.Offers(RemixNodes.Builtin(), Shell.Install.IsNitro);
        _sel = 0;
        // the modern look: the region's lines and rings at the window's resolution (the 1997 look: the frame's pixels)
        if (!Shell.Faithful && Mood == "choose")
        {
            _layer = new RoguetripMapLayer
            {
                Plan = () => Mood == "choose" ? _plan : null, Covered = () => Shell.ModalOpen || Shell.OverlayOpen,
                Backdrop = () => RoguetripAtlas.Texture(Dir, Repaint),
            };
            Shell.AddLayerOverFrame(_layer);
        }
        Repaint();
        // a run made by another version of the generator rolls other jobs from the same number: say so
        if (Notice is null && Run.Builder != RemixBuilder.Version && Mood == "choose")
            Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = Repaint, Text = $"This run was made by builder v{Run.Builder}; this game's is v{RemixBuilder.Version}, so its jobs differ." });
        if (Notice is { Length: > 0 } n) Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = n, Closed = Repaint });
    }

    public override void Leave()
    {
        _layer?.QueueFree();
        _layer = null;
    }

    private int Count => Mood switch { "choose" => _offers.Count, "reward" => Picks.Length, _ => 0 };

    /// <summary>The debrief page's second button: RETRY JOB with the Developer setting on.</summary>
    private static readonly Rect2I Retry = Take;   // (the sheet's middle button: TAKE THE JOB's)

    private Rect2I Box(int i)
    {
        if (Mood == "choose" && _column is { } c && i < c.Offers.Count) { var b = c.Offers[i].Box; return new((int)b.X, (int)b.Y, (int)b.W, (int)b.H); }
        return new(Left, Top + i * (BoxH + BoxGap), Right - Left, BoxH);
    }

    // ---- the choice map (docs/remix-missions.md "The choice map"): the region with the offers as points of interest ----

    private static readonly RoguetripLayout.Box RegionBox = RoguetripLayout.RegionBox;

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
    /// its words in the small sans either way (the text layer sets them at the window's resolution in the modern look).
    /// </summary>
    private void DrawRegion()
    {
        var reg = RemixRegion.Builtin();
        bool atlas = RoguetripAtlas.Files(Dir) is not null;
        _plan = RoguetripLayout.Region(reg, RegionBox, _offers.Select(o => o.Map).ToList(), _sel, Run.Legs.Select(l => l.Map).ToList(), Run.Map,
            t => _map.Measure(t), _map.Height, atlas);
        var plan = _plan;
        byte ink = Frame.Nearest(RoguetripMapLayer.Ink), road = Frame.Nearest(RoguetripMapLayer.Road), pencil = Frame.Nearest(RoguetripMapLayer.Pencil),
            red = Frame.Nearest(RoguetripMapLayer.Marker), redPencil = Frame.Nearest(RoguetripMapLayer.RedPencil);
        if (Shell.Faithful)
        {
            if (atlas && RoguetripAtlas.Small(Dir, Repaint) is { } bg)
            {
                // the player's atlas matched to the palette, under the drawing
                var near = new Dictionary<int, byte>();
                for (int y = 0; y < bg.H; y++)
                    for (int x = 0; x < bg.W; x++)
                    {
                        int o = (y * bg.W + x) * 4;
                        if (bg.Rgba[o + 3] < 128) continue;
                        // laid on the sheet as ink is (multiplied: the atlas's own paper becomes the sheet's; the palette has no cream)
                        int fx = (int)RegionBox.X + x, fy = (int)RegionBox.Y + y, under = Frame.At(fx, fy) * 3;
                        int r = Math.Min(255, Frame.Palette[under] * bg.Rgba[o] / AtlasPaper.R), g = Math.Min(255, Frame.Palette[under + 1] * bg.Rgba[o + 1] / AtlasPaper.G),
                            b = Math.Min(255, Frame.Palette[under + 2] * bg.Rgba[o + 2] / AtlasPaper.B);
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
                else Ring(R(d.X), R(d.Y), 2, pencil);
            }
            foreach (var o in plan.Offers)
            {
                Ring(R(o.X), R(o.Y), 8, o.Selected ? red : ink, o.Selected ? 2 : 1);
                if (o.Selected) Ring(R(o.X), R(o.Y), 12, red);
            }
        }
        foreach (var l in plan.Labels)
        {
            byte c = l.Kind switch { "road" => redPencil, "state" => pencil, _ => ink };
            Frame.Text(_map, R(l.Rect.X), R(l.Rect.Y), l.Text, Solid(_map, c));
        }
        foreach (var o in plan.Offers)
        {
            string n = (o.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Frame.Text(_small, R(o.X) - _small.Measure(n) / 2, R(o.Y) - _small.Height / 2 - 1, n, Solid(_small, ink));
            _hits[o.Index] = new Rect2I(R(o.X) - 12, R(o.Y) - 12, 24, 24);
        }
    }

    private readonly Dictionary<int, Rect2I> _hits = [];
    /// <summary>A scanned road atlas's paper, about (the 1997 look lays the atlas on the sheet relative to it).</summary>
    private static readonly (int R, int G, int B) AtlasPaper = (246, 236, 200);
    private readonly Dictionary<(int Seed, int W, int H), (I76.Formats.Mission.ArenaSketch Sketch, RoguetripLayout.MarkPlan Marks)?> _jobMaps = [];

    /// <summary>The paper the job map's scrap is cut from: a plain part of the sheet (under the region, which is drawn later).</summary>
    private static readonly (int X, int Y) ScrapPaperAt = (40, 120);
    private const int ScrapPad = 5, LegendCap = 7;

    /// <summary>
    /// The chosen offer's own map on a scrap of paper laid on the sheet, as the host form lays its arena's (ArenaSketches.DrawScrap:
    /// the sheet's paper lightened, torn edges, turned a degree or two, a shadow): the whole map's sketch, the job's marks ringed in
    /// red with their words placed clear of each other, the map's name under it in the hand. Made once an offer and size.
    /// </summary>
    private void DrawJobMap(RunOffer offer, RoguetripLayout.Box box)
    {
        int w = (int)box.W - 2 * ScrapPad - 2, h = (int)box.H - 2 * ScrapPad - LegendCap - 6;
        if (w < 40 || h < 30 || _sheet is null) return;
        if (!_jobMaps.TryGetValue((offer.Seed, w, h), out var made))
        {
            made = null;
            try { made = RoguetripLayout.JobMap(new GameInstall(Shell.Install.Root!), Run.Spec(offer), w, h, t => _map.Measure(t), _map.Height); }
            catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException) { GD.PrintErr($"roguetrip: the job's map: {ex.Message}"); }
            _jobMaps[(offer.Seed, w, h)] = made;
        }
        if (made is not { } m) return;
        var sk = m.Sketch;
        // the scrap: the ink, the rings and the words, a margin of paper, the legend under them
        var (ix0, iy0, ix1, iy1) = sk.InkBounds() ?? (0, 0, w - 1, h - 1);
        float x0 = ix0, y0 = iy0, x1 = ix1 + 1, y1 = iy1 + 1;
        foreach (var (rx, ry, _) in m.Marks.Rings) { x0 = Math.Min(x0, rx - 6); y0 = Math.Min(y0, ry - 6); x1 = Math.Max(x1, rx + 6); y1 = Math.Max(y1, ry + 6); }
        foreach (var l in m.Marks.Labels) { x0 = Math.Min(x0, l.Rect.X); y0 = Math.Min(y0, l.Rect.Y); x1 = Math.Max(x1, l.Rect.Right); y1 = Math.Max(y1, l.Rect.Bottom); }
        string legend = MapName(offer.Map);
        PrintedType.LeePath ??= Shell.Install.FindFile("Lee_____.ttf");
        float lw = PrintedType.Measure(Shell.Db, legend, LegendCap, PrintedType.Face.Hand);
        int lx = R((x0 + x1 - lw) / 2), ly = R(y1) + 3 + LegendCap;
        var scrap = new Rect2I(R(Math.Min(x0, lx)) - ScrapPad, R(y0) - ScrapPad, 0, 0);
        scrap = scrap with { Size = new Vector2I(R(Math.Max(x1, lx + lw)) + ScrapPad - scrap.Position.X, ly + 2 + ScrapPad - scrap.Position.Y) };
        int bx = R(box.X + (box.W - scrap.Size.X) / 2) - scrap.Position.X, by = R(box.Y + (box.H - scrap.Size.Y) / 2) - scrap.Position.Y;
        ArenaSketches.DrawScrap(Frame, _sheet.Pixels, ScrapPaperAt, $"roguetrip:{offer.Seed}", sk, bx, by, scrap, legend, lx, ly, LegendCap, RoguetripMapLayer.Ink,
            (k, aliased) => JobInk(sk, m.Marks, k, aliased));
    }

    /// <summary>The atlas of the map font's glyphs (the modern look's letters, 8 texels a pixel), as an image, once.</summary>
    private static Image? _glyphs;

    /// <summary>The job map's ink: the sketch's roads and edges (its hatching left out: at this size it fills the high ground solid), the marks' rings in red marker and their words in red pencil.</summary>
    private byte[] JobInk(I76.Formats.Mission.ArenaSketch sk, RoguetripLayout.MarkPlan marks, int k, bool aliased)
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
            float cx = rx * k, cy = ry * k, rr = 4 * k, half = (aliased ? 0.5f : 0.75f) * k;
            for (int y = (int)(cy - rr - half - 1); y <= cy + rr + half + 1; y++)
                for (int x = (int)(cx - rr - half - 1); x <= cx + rr + half + 1; x++)
                {
                    float d = MathF.Abs(MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - rr);
                    float a = aliased ? (d <= half ? 1 : 0) : Math.Clamp(half - d + 0.5f, 0, 1);
                    Put(x, y, RoguetripMapLayer.Marker, a);
                }
        }
        // the words: the map font's own pixels (1997), or its letters from the text layer's atlas (modern)
        ShellGlyphs.Atlas? atlas = aliased ? null : ShellGlyphs.For(_map);
        if (atlas is not null) _glyphs ??= atlas.Texture.GetImage();
        foreach (var l in marks.Labels)
        {
            float pen = l.Rect.X;
            foreach (char ch in l.Text)
            {
                if (_map.Glyph(ch) is not { } g) continue;
                int gx = (int)MathF.Round(pen * k), gy = (int)MathF.Round(l.Rect.Y * k);
                if (atlas is not null && _glyphs is not null && atlas.Cells.TryGetValue(ch, out var cell))
                {
                    int step = ShellGlyphs.Scale / k;
                    for (int y = 0; y < _map.Height * k; y++)
                        for (int x = 0; x < g.Width * k; x++)
                        {
                            float acc = 0;
                            for (int j = 0; j < step; j++)
                                for (int i = 0; i < step; i++)
                                {
                                    int sx = (int)cell.Position.X + x * step + i, sy = (int)cell.Position.Y + y * step + j;
                                    if (sx < _glyphs.GetWidth() && sy < _glyphs.GetHeight()) acc += _glyphs.GetPixel(sx, sy).A;
                                }
                            Put(gx + x, gy + y, RoguetripMapLayer.RedPencil, acc / (step * step));
                        }
                }
                else
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
        if (Mood == "choose")
        {
            _column = RoguetripLayout.Column(RoguetripLayout.ColumnBox, _offers.Select((o, i) => (OfferTitle(o, i), o.Flavour)).ToList(), Notes(),
                t => _small.Measure(t), RoguetripLayout.OfferLine, RoguetripLayout.NoteLine, t => _map.Measure(t), RoguetripLayout.NoteLine);
            if (_column.Overflow.Count > 0) GD.PrintErr($"roguetrip: the column overflows: {string.Join(" | ", _column.Overflow)}");
        }
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
                Centre(_small, 44, $"Run {num}: job {Run.Legs.Count + 1} of {RemixRun.Jobs}{(Run.Busts > 0 ? $" ({Run.Busts} busted)" : "")}. {Run.Model}: {condition}.");
                Centre(_small, 58, Run.Map.Length == 0 ? "Pick where the run starts." : $"Leaving {MapName(Run.Map)}. Pick the next job down the road.");
                // the region with the offers on it (left), the offers and the chosen one's own map and notes (right)
                _hits.Clear();
                DrawRegion();
                var col = _column!;
                for (int i = 0; i < col.Offers.Count; i++)
                {
                    var blk = col.Offers[i];
                    if (i == _sel) Mark(Box(i), blk.TitleLines);
                    for (int k = 0; k < blk.Lines.Count; k++)
                    {
                        var (t, x, y) = blk.Lines[k];
                        if (k < blk.TitleLines) Frame.Text(_small, R(x), R(y), t, ShellFrame.OverlayInk);
                        else Frame.Text(_map, R(x), R(y), t, Solid(_map, Frame.Nearest(RoguetripMapLayer.Ink)));
                    }
                }
                if (_offers.Count == 0) Centre(_hand, 200, "No jobs on the road from here.");
                else DrawJobMap(_offers[Math.Clamp(_sel, 0, _offers.Count - 1)], col.Map);
                foreach (var (t, x, y) in col.Notes) Frame.Text(_map, R(x), R(y), t, Solid(_map, Frame.Nearest(RoguetripMapLayer.Ink)));
                Centre(_small, 428, "Up, Down or the map's rings choose; Enter takes the job; Esc: back.");
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
                    RemixRun.Outcome.Won => Run.Legs.Count >= RemixRun.Jobs ? "That was the last job of the run." : "Next: what's left of their cars, in FIELD SALVAGE, then the garage.",
                    RemixRun.Outcome.Bust => Run.Legs.Count >= RemixRun.Jobs ? "A bust, and the last job of the run." : $"A bust: no salvage from this one, and word gets round: the next job is a step harder ({Run.NextChallenge:0.0}).",
                    _ => "The run ends here.",
                };
                foreach (var w in Wrap(_small, next, Right - Left - 40)) { y += 18; Centre(_small, y, w); }
                Button(Later, "CONTINUE");
                if (Developer) Button(Retry, "RETRY JOB");
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
        try { return Run.Spec(_offers[Math.Clamp(_sel, 0, _offers.Count - 1)]).Text.Notes.Take(2).ToList(); }
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

    public override void Tick()
    {
        foreach (var k in Shell.Keys)
        {
            if (k is Key.Up && Count > 0) { _sel = (_sel + Count - 1) % Count; Shell.Sound(82); Repaint(); }
            else if (k is Key.Down && Count > 0) { _sel = (_sel + 1) % Count; Shell.Sound(82); Repaint(); }
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
