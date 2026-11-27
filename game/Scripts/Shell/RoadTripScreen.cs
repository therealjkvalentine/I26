using Godot;
using I76.Formats.Remix;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// ROAD TRIP (the port's; docs/remix-missions.md "Road trip"): a run of four generated jobs across neighbouring maps of the region.
/// One screen in three moods, on the port's paper sheet (PaperSheet, record 16's paper) in the shell's fonts: the choice of the next job
/// (two or three offers: the map, the job, its challenge, a line naming the gang), the pick a won job grants (full repair, full
/// ammunition, or nothing for a bonus mark), and the run's end (the jobs won, the kills, the run's number). The run lives in
/// user://remix/run.yaml between jobs, so quitting the game keeps it; ROAD TRIP on the scenario form takes it up again. Single player.
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

    private string Mood => Run.Phase switch { "reward" => "reward", "over" or "done" => "end", _ => "choose" };

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

    /// <summary>Back from a job: the result goes into the run (won: the pick, or the run done; not: the run over) and the screen comes up.</summary>
    public static ShellScreen Back(RunResult? result, string? loadError)
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
            run.Finish(result.Won, result.Kills, result.Car);
            GD.Print($"remix: ROAD TRIP {run.Number}: job {run.Legs.Count} {(result.Won ? "won" : "lost")}, {result.Kills} kills; {run.Phase}");
            Save(run);
        }
        return new RoadTripScreen { Run = run };
    }

    // ---- the screen ----

    public override void Enter()
    {
        _hand = Shell.Db.Font(0x45);
        _small = Shell.Db.Font(0x44);
        _title = Shell.Db.Font(0x43);
        _caps = Shell.Db.Font(0x47);
        if (Mood == "choose") _offers = Run.Offers(RemixNodes.Builtin(), Shell.Install.IsNitro);
        _sel = 0;
        Repaint();
        if (Notice is { Length: > 0 } n) Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = n, Closed = Repaint });
    }

    private int Count => Mood switch { "choose" => _offers.Count, "reward" => Picks.Length, _ => 0 };

    private Rect2I Box(int i) => new(Left, Top + i * (BoxH + BoxGap), Right - Left, BoxH);

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
                Centre(_small, 48, $"Run {num}: job {Run.Legs.Count + 1} of {RemixRun.Jobs}. {Run.Model}: {condition}.");
                Centre(_small, 66, Run.Map.Length == 0 ? "Pick where the run starts." : $"Leaving {MapName(Run.Map)}. Pick the next job down the road.");
                for (int i = 0; i < _offers.Count; i++)
                {
                    var o = _offers[i];
                    var b = Box(i);
                    if (i == _sel) Mark(b);
                    Line(_hand, b.Position.X + 12, b.Position.Y + 8, $"{i + 1}. {o.Name.ToUpperInvariant()}: {RemixRun.JobWord(o.Template)}");
                    Line(_small, b.Position.X + 30, b.Position.Y + 34, $"Challenge {o.Challenge:0.0} (Back to Spanner 1.0)");
                    var words = Wrap(_small, o.Flavour, b.Size.X - 50);
                    for (int k = 0; k < Math.Min(3, words.Count); k++) Line(_small, b.Position.X + 30, b.Position.Y + 52 + k * 16, words[k]);
                }
                if (_offers.Count == 0) Centre(_hand, 200, "No jobs on the road from here.");
                Centre(_small, 410, "Up and Down choose, Enter takes the job; Esc goes back (the run is kept).");
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
            default:
                bool done = Run.Phase == "done";
                Title(done ? "ROAD TRIP DONE" : "ROAD TRIP OVER");
                Centre(_hand, 70, done ? "Four jobs, four wins. The road's yours." : "The run ends here.");
                Centre(_small, 104, $"Run {num}: {Run.Won} of {RemixRun.Jobs} jobs won, {Run.Kills} kills, {Run.Bonus} bonus marks.");
                int y = 136;
                foreach (var l in Run.Legs)
                {
                    Centre(_small, y, $"{MapName(l.Map)}: {RemixRun.JobWord(l.Template).ToLowerInvariant()} at {l.Challenge:0.0}, {(l.Won ? "won" : "lost")}, {l.Kills} kills");
                    y += 20;
                }
                Centre(_small, 390, $"Run number {num} offers the same jobs again: type it on ROAD TRIP.");
                Button(Later, "DONE");
                break;
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
        [.. Enumerable.Range(0, Count).Select(i => PadRegion.Of(Box(i))), PadRegion.Of(Take), PadRegion.Of(Later)];

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
        if (Mood == "end") Close(Run);
        Shell.Go(new MeleeScreen { Mode = 1 });
    }

    private void Act()
    {
        Shell.Sound(82);
        switch (Mood)
        {
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
        try
        {
            GameInstall.UserMissionDir = Dir;
            var spec = Run.Spec(offer);
            RemixBuilder.Build(new GameInstall(Shell.Install.Root!), spec).WriteTo(Dir);
            File.WriteAllText(Path.Combine(Dir, RemixRun.Stem + ".job.yaml"), RemixDirector.ToYaml(spec));
        }
        catch (Exception ex) when (ex is RemixError or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PrintErr($"remix: ROAD TRIP {Run.Number}: {ex.Message}");
            Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = "That job could not be built.", Closed = Repaint });
            return;
        }
        Run.Take(offer);
        Save(Run);
        GD.Print($"remix: ROAD TRIP {Run.Number}: job {Run.Legs.Count + 1}, {offer.Template} on {offer.Map} (seed {offer.Seed}, challenge {offer.Challenge:0.0})");
        Shell.Launch(new ShellLaunch(RemixRun.Stem, GameKind.Scenario, Run.Car)
        {
            Driver = Run.Driver, Model = Run.Model, ModelIndex = Run.ModelIndex, RoadTrip = true, Carry = Run.State,
        });
    }
}
