namespace I76.Game.Ui;

/// <summary>
/// How far a mission's load is, 0 to 1, for the loading card's wheel (the port's: the original's card is a still picture,
/// docs/deviations.md "Loading progress"). The load's own stamps (MissionBuilder.Trace) are its milestones, each worth the share of
/// the load measured to end there (T01 and T05 on the desktop profile, first loads and restarts: <see cref="First"/>,
/// <see cref="Restart"/>); the cars' loop moves it a car at a time (<see cref="Part"/>). It only goes up: a stamp the path does
/// not make (the 1997 look's, a network game's, a threaded build's) is passed over, one out of order changes nothing. Engine-free.
/// </summary>
public sealed class LoadProgressModel
{
    /// <summary>A milestone: the stamp that ends it, the word the card shows while it is the next one, and its measured share of the load.</summary>
    public readonly record struct Stage(string Stamp, string Word, double Share);

    private readonly object _lock = new();
    private Stage[] _stages;
    private double[] _end;
    private double _value;
    private int _at = -1;

    public LoadProgressModel(Stage[] stages) { _stages = stages; _end = Ends(stages); }

    /// <summary>Each milestone's end, the running sum of the shares over their total.</summary>
    public static double[] Ends(Stage[] stages)
    {
        double total = stages.Sum(s => s.Share), sum = 0;
        var end = new double[stages.Length];
        for (int i = 0; i < stages.Length; i++) { sum += stages[i].Share; end[i] = total > 0 ? sum / total : (i + 1.0) / stages.Length; }
        if (end.Length > 0) end[^1] = 1;
        return end;
    }

    /// <summary>0 to 1, never less than it was.</summary>
    public double Value { get { lock (_lock) return _value; } }

    /// <summary>The word of the stage under way (the next milestone's), or the first.</summary>
    public string Word { get { lock (_lock) return _stages.Length == 0 ? "" : _stages[Math.Min(_at + 1, _stages.Length - 1)].Word; } }

    /// <summary>Another table from here on (a restart's, once the build finds the last mission's kit): the value stays where it is until the new table passes it.</summary>
    public void Use(Stage[] stages) { lock (_lock) { _stages = stages; _end = Ends(stages); _at = Math.Min(_at, stages.Length - 1); } }

    /// <summary>A stamp of the load: past its milestone, if it is one.</summary>
    public void Mark(string stamp)
    {
        lock (_lock)
        {
            int i = Array.FindIndex(_stages, s => s.Stamp == stamp);
            if (i < 0) return;
            if (i > _at) _at = i;
            Raise(_end[i]);
        }
    }

    /// <summary><paramref name="done"/> of <paramref name="of"/> of the stage that ends at <paramref name="stamp"/> (the cars' loop, a car at a time), once the milestone before it is past.</summary>
    public void Part(string stamp, int done, int of)
    {
        if (of <= 0) return;
        lock (_lock)
        {
            int i = Array.FindIndex(_stages, s => s.Stamp == stamp);
            if (i < 0 || _at < i - 1) return;   // only once the stage before it is past (the player's car, set up before the others)
            double from = i == 0 ? 0 : _end[i - 1];
            Raise(from + (_end[i] - from) * Math.Clamp(done, 0, of) / of);
        }
    }

    /// <summary>
    /// The stage that ends at <paramref name="stamp"/> takes <paramref name="share"/> from here on (a road bake the bake cache did not have:
    /// twenty times its share from the cache); the value stays, the rest of the load is spread over what is left.
    /// </summary>
    public void Weigh(string stamp, double share)
    {
        lock (_lock)
        {
            int i = Array.FindIndex(_stages, s => s.Stamp == stamp);
            if (i < 0 || _stages[i].Share == share) return;
            var stages = (Stage[])_stages.Clone();
            stages[i] = stages[i] with { Share = share };
            _stages = stages; _end = Ends(stages);
        }
    }

    /// <summary>The load is over: 1.</summary>
    public void Finish() { lock (_lock) { _at = _stages.Length - 1; Raise(1); } }

    private void Raise(double v) { if (v > _value) _value = Math.Min(1, v); }

    // The tables: the main thread's milestones in the order a single-player start in the modern look stamps them, each worth the
    // milliseconds measured to it from the one before (I76_LOAD_PROFILE at 3440x1440, the 4x world and 8x cars, no briefing: the mean
    // of T01's median over four runs and T05's over three, first loads and their restarts).
    // The words are the card's: TERRAIN for the world's build, SCENERY for the sky and the look, VEHICLES for the cars and their cockpit,
    // EFFECTS for the first frames drawn.
    public const string Terrain = "TERRAIN", Scenery = "SCENERY", Vehicles = "VEHICLES", Cockpit = "COCKPIT", Effects = "EFFECTS";

    /// <summary>The stamp the cars' loop's parts belong to (<see cref="Part"/>).</summary>
    public const string CarsMade = "spawn: cars made";
    /// <summary>The stamp the road layers' bake's chunks belong to, and its share when the bake cache has none of them (T05's, measured cold).</summary>
    public const string RoadBake = "road bake done";
    public const double ColdRoadBakeMs = 7500;   // (T05's first load in a new user folder: 7.5 s, 0.37 s from the cache)

    private static Stage[] Table(params (string Stamp, string Word, double Ms)[] rows) => [.. rows.Select(r => new Stage(r.Stamp, r.Word, r.Ms))];

    /// <summary>A first load of the mission in this run (the bake cache holding the map's road layers and ground).</summary>
    public static readonly Stage[] First = Table(
        ("build start", Terrain, 251), ("textures, materials, meshes", Terrain, 106), ("ground, sky, horizon", Terrain, 182),
        ("roads flattened", Terrain, 84), ("road bake", Terrain, 134), (RoadBake, Terrain, 355), ("ground materials", Terrain, 1875),
        ("terrain: 1997 arrays", Terrain, 660), ("terrain: chunks added", Terrain, 562), ("terrain built", Terrain, 56),
        ("scenery spawn", Terrain, 215), ("done", Terrain, 711),
        ("horizon built", Scenery, 157), ("sky", Scenery, 115), ("mission loaded", Scenery, 42), ("drop shadows", Scenery, 308),
        ("look: shadows, draw distance", Scenery, 522), ("veg: meshes", Scenery, 154), ("look: modern quality", Scenery, 211), ("look set", Scenery, 1),
        ("spawn: sim world set up", Vehicles, 228), ("spawn: combat view", Vehicles, 506), ("spawn: cockpit assembled", Cockpit, 386),
        ("spawn: cockpit meshes", Cockpit, 128), ("spawn: the player's car set up", Cockpit, 1666), (CarsMade, Vehicles, 1027),
        ("spawn: car prewarmed", Vehicles, 297), ("spawn: wrecks prewarmed", Vehicles, 262), ("car spawned", Vehicles, 121),
        ("first tick done", Vehicles, 145), ("vegetation prewarmed", Vehicles, 102),
        ("rehearsal: explosions", Effects, 296), ("rehearsal: made", Effects, 93), ("rehearsal: frame 0", Effects, 23),
        ("rehearsal: frame 1", Effects, 1889), ("effects rehearsal", Effects, 72));

    /// <summary>A restart of the same mission (the build finds the last one's kit: World/RestartCache.cs).</summary>
    public static readonly Stage[] Restart = Table(
        ("build start", Terrain, 270), ("textures, materials, meshes", Terrain, 11), ("ground, sky, horizon", Terrain, 59),
        ("roads flattened", Terrain, 12), ("road bake", Terrain, 24), (RoadBake, Terrain, 108), ("ground materials", Terrain, 113),
        ("terrain: 1997 arrays", Terrain, 3), ("terrain: chunks added", Terrain, 575), ("terrain built", Terrain, 52),
        ("scenery spawn", Terrain, 68), ("done", Terrain, 55),
        ("horizon built", Scenery, 188), ("sky", Scenery, 45), ("mission loaded", Scenery, 14), ("drop shadows", Scenery, 2),
        ("look: shadows, draw distance", Scenery, 90), ("veg: meshes", Scenery, 78), ("look: modern quality", Scenery, 146), ("look set", Scenery, 1),
        ("spawn: sim world set up", Vehicles, 83), ("spawn: combat view", Vehicles, 160), ("spawn: cockpit assembled", Cockpit, 8),
        ("spawn: cockpit meshes", Cockpit, 6), ("spawn: the player's car set up", Cockpit, 719), (CarsMade, Vehicles, 33),
        ("spawn: car prewarmed", Vehicles, 109), ("spawn: wrecks prewarmed", Vehicles, 130), ("car spawned", Vehicles, 101),
        ("first tick done", Vehicles, 8), ("vegetation prewarmed", Vehicles, 66),
        ("rehearsal: explosions", Effects, 7), ("rehearsal: made", Effects, 50), ("rehearsal: frame 0", Effects, 4),
        ("rehearsal: frame 1", Effects, 884), ("effects rehearsal", Effects, 27));
}

/// <summary>
/// The load under way (<see cref="LoadProgressModel"/>) and the card's wheel that shows it: stamped from the build's thread and the
/// main thread, read by the wheel (LoadProgressView) every frame drawn. While the main thread loads without letting a frame through
/// (a start with nothing to build under, a restart), <see cref="Blocking"/>: the wheel is drawn at its stamps instead, a few times a second.
/// </summary>
public static class LoadProgress
{
    private static LoadProgressModel _model = new(LoadProgressModel.First);
    public static LoadProgressModel Model => _model;

    /// <summary>A load begins (the card up): back to 0 on the first load's table.</summary>
    public static void Begin() { _model = new LoadProgressModel(LoadProgressModel.First); Active = true; }

    /// <summary>A load is under way: from <see cref="Begin"/> until its card goes (<see cref="End"/>). A network game's card is up before it.</summary>
    public static bool Active { get; private set; }

    public static void End() => Active = false;

    /// <summary>The build took the last mission's kit: the restart's table.</summary>
    public static void Restarted() => _model.Use(LoadProgressModel.Restart);

    /// <summary>A stamp (MissionBuilder.Trace, any thread).</summary>
    public static void Mark(string stamp)
    {
        _model.Mark(stamp);
        Present?.Invoke();
    }

    /// <summary><paramref name="done"/> of <paramref name="of"/> of the stage ending at <paramref name="stamp"/> (the cars made, the road layers' chunks baked; any thread).</summary>
    public static void Part(string stamp, int done, int of)
    {
        _model.Part(stamp, done, of);
        Present?.Invoke();
    }

    /// <summary>The stage ending at <paramref name="stamp"/> is worth <paramref name="ms"/> this time (the bake cache had nothing for it).</summary>
    public static void Weigh(string stamp, double ms) => _model.Weigh(stamp, ms);

    public static void Finish() => _model.Finish();

    /// <summary>The main thread is loading without frames between its steps (Main's straight-through load).</summary>
    public static volatile bool Blocking;

    /// <summary>The wheel's chance to draw itself at a stamp (set while it is up; it checks the thread and the time itself).</summary>
    public static System.Action? Present;
}

/// <summary>
/// The wheel's pie (load_wheel.gdshader draws it; this is its rule, engine-free): the fraction shown in whole per cents, filled
/// clockwise from 12 o'clock, a point of the wheel loaded when its angle from 12 o'clock, clockwise, is under the fill's.
/// </summary>
public static class LoadWheel
{
    /// <summary>The per cent shown: floor of the fraction, 0 to 100 (−1 for nothing shown yet).</summary>
    public static int Percent(double f) => f < 0 ? -1 : (int)Math.Floor(Math.Clamp(f, 0, 1) * 100 + 1e-9);

    /// <summary>The pie's angle in degrees, clockwise from 12 o'clock, for <paramref name="percent"/>.</summary>
    public static double FillDegrees(int percent) => Math.Clamp(percent, 0, 100) * 3.6;

    /// <summary>A point's angle in degrees from 12 o'clock, clockwise, the wheel's centre at 0, x right and y down (the screen's).</summary>
    public static double Angle(double x, double y)
    {
        double a = Math.Atan2(x, -y) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    /// <summary>Whether the point is in the loaded part at <paramref name="percent"/> (the shader's step, without its smoothing).</summary>
    public static bool Loaded(int percent, double x, double y) => percent >= 100 || (percent > 0 && Angle(x, y) < FillDegrees(percent));
}
