using Godot;
using I76.Formats.Remix;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// ROAD TRIP (the port's; docs/remix-missions.md "Road trip"): a run of four generated jobs across neighbouring maps of the region.
/// One screen in three moods, on the port's paper sheet (PaperSheet, record 16's paper) in the shell's fonts: the choice of the next job
/// (two or three offers: the map, the job, its challenge, a line naming the gang), a job's debrief page (how it ended, the line it
/// ended on, its objectives, what follows: the salvage, a bust's penalty, the run's end; RETRY JOB with the Developer setting), and
/// the run's end (the jobs won and busted, the kills, the run's number). A won job goes on to the trip's own FIELD SALVAGE and
/// garage with the run's inventory. The run lives in user://remix/run.yaml between jobs, so quitting the game keeps it; ROAD TRIP
/// on the scenario form takes it up again. Single player.
/// </summary>
public sealed class RoadTripScreen : ShellScreen
{
    public required RemixRun Run { get; set; }
    /// <summary>Said over the sheet on entry (a job that could not be built or loaded).</summary>
    public string? Notice { get; init; }
    public override bool EscapeOpensOptions => false;

    private List<RunOffer> _offers = [];
    private int _sel;
    private ShellFont _hand = null!, _small = null!, _title = null!, _caps = null!;
    private const int Left = 36, Right = 604, Top = 92, BoxH = 104, BoxGap = 8;
    private static readonly Rect2I Take = new(330, 450, 140, 19), Later = new(490, 450, 140, 19);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"road trip: not saved: {ex.Message}"); }
    }

    /// <summary>A run over: its file kept as run-last.yaml (the summary's), the way clear for a new one.</summary>
    private static void Close(RemixRun r)
    {
        try
        {
            File.WriteAllText(Path.Combine(Dir, "run-last.yaml"), r.ToYaml());
            File.Delete(Path.Combine(Dir, RemixRun.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"road trip: {ex.Message}"); }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"road trip: {ex.Message}"); }
    }

    /// <summary>
    /// Back from a job: the result goes into the run (docs/remix-missions.md "Busts": won, a bust with the player alive, or the
    /// player's death) and its debrief page comes up. A won job's salvage goes into the run's inventory at once (its field), so a
    /// game closed on the salvage screens finds it there again.
    /// </summary>
    public static ShellScreen Back(Shell shell, RunResult? result, string? loadError)
    {
        var run = RemixRun.Load(Dir);
        if (run is null) return new MeleeScreen { Mode = 1 };
        if (run.Phase == "playing")
        {
            if (result is null)
            {
                // the job never ran (it could not be built or loaded): the same choice again, saying why
                run.Resume();
                Save(run);
                return new RoadTripScreen { Run = run, Notice = loadError ?? "The job could not be started." };
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
                catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException) { GD.PrintErr($"road trip: salvage not taken: {ex.Message}"); }
            }
            GD.Print($"remix: ROAD TRIP {run.Number:000000}: job {run.Legs.Count} {outcome.ToString().ToLowerInvariant()} ({(why.Length > 0 ? why : "done")}), {result.Kills} kills; busts {run.Busts}; next challenge {run.NextChallenge:0.0}");
            Save(run);
        }
        return new RoadTripScreen { Run = run };
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

    public override void Enter()
    {
        _hand = Shell.Db.Font(0x45);
        _small = Shell.Db.Font(0x44);
        _title = Shell.Db.Font(0x43);
        _caps = Shell.Db.Font(0x47);
        // a won job's salvage and garage: the trip's own screens on the run's inventory
        if (Run.Phase == "salvage") { Salvage(); return; }
        if (Mood == "choose") _offers = Run.Offers(RemixNodes.Builtin(), Shell.Install.IsNitro);
        _sel = 0;
        Repaint();
        // a run made by another version of the generator rolls other jobs from the same number: say so
        if (Notice is null && Run.Builder != RemixBuilder.Version && Mood == "choose")
            Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = $"This run was made by builder v{Run.Builder}; this game's is v{RemixBuilder.Version}, so its jobs differ.", Closed = Repaint });
        if (Notice is { Length: > 0 } n) Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = n, Closed = Repaint });
    }

    private int Count => Mood switch { "choose" => _offers.Count, "reward" => Picks.Length, _ => 0 };

    /// <summary>The debrief page's second button: RETRY JOB with the Developer setting on.</summary>
    private static readonly Rect2I Retry = new(170, 450, 140, 19);

    private Rect2I Box(int i) => Mood == "choose" ? new(OfferX, 80 + i * 58, Right - OfferX, 52) : new(Left, Top + i * (BoxH + BoxGap), Right - Left, BoxH);

    // ---- the choice map (docs/remix-missions.md "The choice map"): the region with the offers as points of interest ----

    private const int OfferX = 344;
    /// <summary>The region's box on the sheet, and the chosen job's own map under the offers.</summary>
    private static readonly Rect2I RegionBox = new(28, 76, 304, 334), JobMapBox = new(OfferX, 256, Right - OfferX, 132);

    private (int X, int Y) InRegion(double[] latLon)
    {
        var (x, y) = RemixRegion.Builtin().Project(latLon, RegionBox.Size.X, RegionBox.Size.Y);
        return (RegionBox.Position.X + (int)Math.Round(x), RegionBox.Position.Y + (int)Math.Round(y));
    }

    /// <summary>A pen line on the frame (Bresenham), solid or dashed (on, off pixels).</summary>
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

    /// <summary>The region drawn: the state line, the highways and their numbers, the towns, the trip's maps, the run's road so far and the offers.</summary>
    private void DrawRegion()
    {
        var reg = RemixRegion.Builtin();
        byte ink = Frame.Nearest(Color.Color8(40, 30, 18)), sepia = Frame.Nearest(Color.Color8(92, 58, 26)), pencil = Frame.Nearest(Color.Color8(120, 110, 96)), red = Frame.Nearest(Color.Color8(222, 96, 38));
        var b = RegionBox;
        foreach (var l in reg.Lines)
            for (int i = 0; i + 1 < l.Count; i++) { var p = InRegion(l[i]); var q = InRegion(l[i + 1]); Clip(ref p, ref q); Pen(p.X, p.Y, q.X, q.Y, pencil, 5, 3); }
        foreach (var h in reg.Highways)
        {
            var pts = reg.Route(h).Select(InRegion).ToList();
            for (int i = 0; i + 1 < pts.Count; i++) { var p = pts[i]; var q = pts[i + 1]; Clip(ref p, ref q); Pen(p.X, p.Y, q.X, q.Y, sepia); }
            if (pts.Count > 1)
            {
                var m = ((pts[0].X + pts[1].X) / 2, (pts[0].Y + pts[1].Y) / 2);
                if (b.HasPoint(new Vector2I(m.Item1, m.Item2))) Frame.Text(_small, m.Item1 + 3, m.Item2 - 12, h.Name, Solid(_small, sepia));
            }
        }
        foreach (var s in reg.States) { var p = InRegion(s.At); Frame.Text(_small, p.X - _small.Measure(s.Name) / 2, p.Y, s.Name, Solid(_small, pencil)); }
        foreach (var t in reg.Towns)
        {
            var p = InRegion(t.At);
            if (!b.HasPoint(new Vector2I(p.X, p.Y))) continue;
            Frame.Fill(p.X - 1, p.Y - 1, 3, 3, ink);
            int w = _small.Measure(t.Name);
            int tx = p.X + 4 + w > b.End.X ? p.X - 4 - w : p.X + 4;
            Frame.Text(_small, tx, p.Y - 6, t.Name, Solid(_small, ink));
        }
        // the trip's maps: a small ring; those the run has been to filled; the run's road so far in red pencil
        var visited = Run.Legs.Select(l => l.Map).ToList();
        foreach (var (map, at) in reg.Maps)
        {
            var p = InRegion(at);
            if (visited.Contains(map, StringComparer.OrdinalIgnoreCase)) Frame.Fill(p.X - 2, p.Y - 2, 5, 5, ink);
            else Ring(p.X, p.Y, 2, pencil);
        }
        for (int i = 0; i + 1 < visited.Count; i++)
            if (reg.Maps.TryGetValue(visited[i], out var a) && reg.Maps.TryGetValue(visited[i + 1], out var c)) { var p = InRegion(a); var q = InRegion(c); Pen(p.X, p.Y, q.X, q.Y, red, 3, 2, 2); }
        // the offers: numbered rings, the chosen one doubled in red pencil, a line from where the run is
        double[]? here = Run.Map.Length > 0 && reg.Maps.TryGetValue(Run.Map, out var hh) ? hh : null;
        for (int i = 0; i < _offers.Count; i++)
        {
            if (!reg.Maps.TryGetValue(_offers[i].Map, out var at)) continue;
            var p = InRegion(at);
            // offers on the same map side by side
            p = (p.X + (i - (_offers.Count - 1) / 2) * (_offers.Count(o => o.Map == _offers[i].Map) > 1 ? 14 : 0), p.Y);
            if (here is not null && i == _sel) { var h0 = InRegion(here); Pen(h0.X, h0.Y, p.X, p.Y, red, 4, 3, 2); }
            Ring(p.X, p.Y, 8, i == _sel ? red : ink, i == _sel ? 2 : 1);
            if (i == _sel) Ring(p.X, p.Y, 12, red);
            string n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Frame.Text(_small, p.X - _small.Measure(n) / 2, p.Y - 6, n, Solid(_small, i == _sel ? red : ink));
            _hits[i] = new Rect2I(p.X - 12, p.Y - 12, 24, 24);
        }
    }

    /// <summary>A segment kept to the region's box (the region's lines run off its edges).</summary>
    private static void Clip(ref (int X, int Y) p, ref (int X, int Y) q)
    {
        var b = RegionBox;
        p = (Math.Clamp(p.X, b.Position.X, b.End.X - 1), Math.Clamp(p.Y, b.Position.Y, b.End.Y - 1));
        q = (Math.Clamp(q.X, b.Position.X, b.End.X - 1), Math.Clamp(q.Y, b.Position.Y, b.End.Y - 1));
    }

    private readonly Dictionary<int, Rect2I> _hits = [];
    private readonly Dictionary<int, (byte[] Ink, List<(int X, int Y, string Label)> Marks, List<string> Notes)?> _jobMaps = [];

    /// <summary>
    /// The chosen offer's own map: its whole map's sketch (ArenaSketch.Whole: the roads by kind) with its marks, drawn in the frame's
    /// inks at the box's size, and its notes under it (where its places are, in words). Made once an offer.
    /// </summary>
    private void DrawJobMap(RunOffer offer)
    {
        if (!_jobMaps.TryGetValue(offer.Seed, out var made))
        {
            made = null;
            try
            {
                var spec = Run.Spec(offer);
                var install = new GameInstall(Shell.Install.Root!);
                if (install.FindMission(spec.Base) is { } path && spec.Start?.At is [var sx, var sz])
                {
                    var msn = new I76.Formats.Mission.MissionFile(path);
                    var marks = RemixMarks.Of(spec, new System.Numerics.Vector2((float)sx, (float)sz),
                        n => spec.Places.TryGetValue(n, out var pts) ? [.. pts.Select(q => new System.Numerics.Vector2((float)q[0], (float)q[1]))] : [],
                        n => spec.Cars.TryGetValue(n, out var c) && c.At is [var cx, var cz] ? new System.Numerics.Vector2((float)cx, (float)cz) : null);
                    var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Points).Select(p => new System.Numerics.Vector2(p.X, p.Z)).ToList();
                    if (I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) is { } ter
                        && I76.Formats.Mission.ArenaSketch.Whole(msn, I76.Formats.Mission.TerrainTiles.Load(msn, ter), [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], exits, JobMapBox.Size.X, JobMapBox.Size.Y) is { } sk)
                    {
                        var px = sk.Render(1, aliased: true);
                        var ms = marks.Points.Select(p => { var u = sk.InBox(p.At); return ((int)(u.X * JobMapBox.Size.X), (int)(u.Y * JobMapBox.Size.Y), p.Label); }).ToList();
                        made = (px, ms, spec.Text.Notes.Take(2).ToList());
                    }
                }
            }
            catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException) { GD.PrintErr($"road trip: the job's map: {ex.Message}"); }
            _jobMaps[offer.Seed] = made;
        }
        if (made is not { } m) return;
        byte ink = Frame.Nearest(Color.Color8(40, 30, 18)), sepia = Frame.Nearest(Color.Color8(92, 58, 26)), red = Frame.Nearest(Color.Color8(222, 96, 38));
        var b = JobMapBox;
        for (int y = 0; y < b.Size.Y; y++)
            for (int x = 0; x < b.Size.X; x++)
            {
                int i = (y * b.Size.X + x) * 4;
                if (m.Ink[i + 3] < 40) continue;
                // the roads in sepia, the ground's edges and hatching in ink
                bool road = m.Ink[i] > 70 && m.Ink[i + 2] < 40;
                Frame.Fill(b.Position.X + x, b.Position.Y + y, 1, 1, road ? sepia : ink);
            }
        foreach (var (x, y, label) in m.Marks)
        {
            Ring(b.Position.X + x, b.Position.Y + y, 5, red, 2);
            string word = label.Split(" / ")[^1];
            int w = _small.Measure(word);
            int tx = b.Position.X + x + 8 + w > b.End.X ? b.Position.X + x - 8 - w : b.Position.X + x + 8;
            Frame.Text(_small, tx, b.Position.Y + y - 6, word, Solid(_small, red));
        }
        int ny = b.End.Y + 4;
        foreach (var note in m.Notes)
            foreach (var w in Wrap(_small, note, b.Size.X).Take(2)) { if (ny > 436) break; Frame.Text(_small, b.Position.X, ny, Fit(_small, w, b.Size.X), ShellFrame.OverlayInk); ny += 14; }
    }

    public override void Repaint()
    {
        var lines = new List<(int X, int Y, int W, int H)>();
        // the frame's double rule round the sheet, as Control Configuration's
        lines.AddRange([(20, 37, 600, 2), (20, 437, 600, 2), (20, 37, 2, 402), (618, 37, 2, 402), (24, 41, 592, 1), (24, 434, 592, 1), (24, 41, 1, 394), (615, 41, 1, 394)]);
        for (int i = 0; i < Count; i++) { var b = Box(i); lines.AddRange([(b.Position.X, b.Position.Y, b.Size.X, 1), (b.Position.X, b.End.Y, b.Size.X, 1)]); }
        Frame.LoadBackground(PaperSheet.Make(Shell.Db.Background(16), true, Mood == "end" ? 3 : 2, false, lines), 0, 0, black: false);
        var ink = Solid(_title, Frame.Nearest(Color.Color8(82, 66, 41)));
        void Title(string t) { int x = 320 - _title.Measure(t) / 2; Frame.Text(_title, x, 12, t, ink); Frame.Text(_title, x + 1, 12, t, ink); }
        void Line(ShellFont f, int x, int y, string t) => Frame.Text(f, x, y, Fit(f, t, Right - x - 8), ShellFrame.OverlayInk);
        void Centre(ShellFont f, int y, string t) => Frame.Text(f, 320 - f.Measure(t) / 2, y, t, ShellFrame.OverlayInk);
        string num = Run.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var car = Run.State;
        string condition = car is null ? "the car as built" : $"armour {car.ArmourLeft}%, chassis {car.ChassisLeft}%, ammo {car.AmmoLeft}%";
        switch (Mood)
        {
            case "choose":
                Title("ROAD TRIP");
                Centre(_small, 44, $"Run {num}: job {Run.Legs.Count + 1} of {RemixRun.Jobs}{(Run.Busts > 0 ? $" ({Run.Busts} busted)" : "")}. {Run.Model}: {condition}.");
                Centre(_small, 58, Run.Map.Length == 0 ? "Pick where the run starts." : $"Leaving {MapName(Run.Map)}. Pick the next job down the road.");
                // the region with the offers on it (left), the offers and the chosen one's own map and notes (right)
                _hits.Clear();
                DrawRegion();
                for (int i = 0; i < _offers.Count; i++)
                {
                    var o = _offers[i];
                    var b = Box(i);
                    if (i == _sel) Mark(b);
                    Line(_small, b.Position.X + 12, b.Position.Y + 6, $"{i + 1}. {o.Name.ToUpperInvariant()}: {RemixRun.JobWord(o.Template)}, {o.Challenge:0.0}");
                    var words = Wrap(_small, o.Flavour, b.Size.X - 24);
                    for (int k = 0; k < Math.Min(2, words.Count); k++) Line(_small, b.Position.X + 12, b.Position.Y + 22 + k * 14, words[k]);
                }
                if (_offers.Count == 0) Centre(_hand, 200, "No jobs on the road from here.");
                else DrawJobMap(_offers[Math.Clamp(_sel, 0, _offers.Count - 1)]);
                Centre(_small, 424, "Up and Down (or a point on the map) choose, Enter takes the job; Esc goes back (the run is kept).");
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
                    if (i == _sel) Mark(b);
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
                Title(done ? "ROAD TRIP DONE" : "ROAD TRIP OVER");
                Centre(_hand, 70, done ? (Run.Busts == 0 ? "Four jobs, four wins. The road's yours." : $"Four jobs done, {Run.Busts} of them busts.") : "The run ends here.");
                Centre(_small, 104, $"Run {num}: {Run.Won} of {RemixRun.Jobs} jobs won, {Run.Busts} busted, {Run.Kills} kills.");
                int y = 136;
                foreach (var l in Run.Legs)
                {
                    string res = l.Outcome switch { "bust" => "busted", "dead" => "killed", "won" => "won", _ => l.Won ? "won" : "lost" };
                    Centre(_small, y, $"{MapName(l.Map)}: {RemixRun.JobWord(l.Template).ToLowerInvariant()} at {l.Challenge:0.0}, {res}, {l.Kills} kills");
                    y += 20;
                }
                Centre(_small, 390, $"Run number {num} offers the same jobs again: type it on ROAD TRIP.");
                Button(Later, "DONE");
                break;
            }
        }
    }

    private static string MapName(string map) => RemixNodes.Builtin().FirstOrDefault(t => t.Base.Equals(map, StringComparison.OrdinalIgnoreCase)) is { Name.Length: > 0 } t ? t.Name : map.ToUpperInvariant();

    /// <summary>The selected box: a marker's rule down its left side and under its first line.</summary>
    private void Mark(Rect2I b)
    {
        byte m = Frame.Nearest(Color.Color8(222, 96, 38));
        Frame.Fill(b.Position.X + 2, b.Position.Y + 4, 4, b.Size.Y - 8, m);
        Frame.Fill(b.Position.X + 12, b.Position.Y + 28, b.Size.X - 24, 2, m);
    }

    private void Button(Rect2I s, string text)
    {
        var dark = Solid(_caps, Frame.Nearest(Color.Color8(82, 66, 41)));
        int tx = s.Position.X + (s.Size.X - _caps.Measure(text)) / 2, ty = s.Position.Y + (s.Size.Y - _caps.Height) / 2 + 1;
        Frame.Text(_caps, tx, ty, text, dark);
        Frame.Text(_caps, tx + 1, ty, text, dark);
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

    private static List<string> Wrap(ShellFont f, string text, int w)
    {
        var o = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && f.Measure(next) > w) { o.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) o.Add(line);
        return o;
    }

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
            else if (k is Key.Escape) { Leave(); return; }
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
        if (Later.HasPoint(m)) { Leave(); return; }
    }

    private void Leave()
    {
        Shell.Sound(82);
        // the debrief page has no way out but on: Esc is CONTINUE
        if (Mood == "debrief") { Continue(); return; }
        if (Mood == "end") Close(Run);
        Shell.Go(new MeleeScreen { Mode = 1 });
    }

    /// <summary>Past the debrief page: a won job's salvage, the next choice, or the run's end.</summary>
    private void Continue()
    {
        Run.Debriefed();
        Save(Run);
        if (Run.Phase == "salvage") { Salvage(); return; }
        Shell.Go(new RoadTripScreen { Run = Run });
    }

    /// <summary>
    /// A won job's FIELD SALVAGE and garage: the trip's own screens on the run's inventory (its field holds the job's wrecks);
    /// DONE comes back here: the parts left in the field are scrapped for armour (RemixRun.Patch, the road trip's own), and the
    /// inventory kept for the next job.
    /// </summary>
    private void Salvage()
    {
        I76.Sim.Trip.TripInventory inv;
        try { inv = Inventory(Run, Shell); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
        {
            GD.PrintErr($"road trip: no inventory: {ex.Message}");
            Run.Salvaged(Run.Inventory, Run.State);
            Save(Run);
            Shell.Go(new RoadTripScreen { Run = Run });
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
                GD.Print($"remix: ROAD TRIP {run.Number:000000}: salvage done, {scrapped} parts scrapped for plate; armour {car?.ArmourLeft ?? 100}%, chassis {car?.ChassisLeft ?? 100}%");
                Save(run);
                Shell.Go(new RoadTripScreen { Run = run });
            },
        });
    }

    /// <summary>The developer's RETRY JOB: the same job again, the car as it began it.</summary>
    private void RetryJob()
    {
        Shell.Sound(82);
        if (Run.Retry() is not { } offer) return;
        Save(Run);
        GD.Print($"remix: ROAD TRIP {Run.Number:000000}: RETRY JOB {Run.JobIndex} (developer)");
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
                Shell.Go(new RoadTripScreen { Run = Run });
                return;
            case "end":
                Leave();
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
            GD.PrintErr($"remix: ROAD TRIP {Run.Number}: {ex.Message}");
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
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException) { GD.PrintErr($"road trip: the garage's car not used: {ex.Message}"); }
        Shell.Launch(new ShellLaunch(RemixRun.Stem, GameKind.Scenario, car)
        {
            Driver = Run.Driver, Model = Run.Model, ModelIndex = Run.ModelIndex, RoadTrip = true, Carry = Run.State, State = parts, RunBase = offer.Map,
            RemixLabel = RemixRun.Label(Run, offer, spec) is var lab ? lab.Top + "\n" + lab.Bottom : null,
        });
    }

    /// <summary>The run's car as its garage left it, served from memory as the trip's vehscn.vcf is.</summary>
    public const string RunCarFile = "vehrun.vcf";
}
