namespace I76.Game.Ui;

/// <summary>
/// How far a mission's load is, 0 to 1, for the line under PLEASE STAND BY (StandByTag; the port's: the original's card is a still
/// picture, docs/deviations.md "Loading progress"). The load's own stamps (MissionBuilder.Trace) are its milestones, each worth the share of
/// the load measured to end there (T01 and T05 on the desktop profile, first loads and restarts: <see cref="First"/>,
/// <see cref="Restart"/>); the cars' loop moves it a car at a time (<see cref="Part"/>). It only goes up: a stamp the path does
/// not make (the 1997 look's, a network game's, a threaded build's) is passed over, one out of order changes nothing. Engine-free.
/// </summary>
public sealed class LoadProgressModel
{
    /// <summary>A milestone: the stamp that ends it and its measured share of the load.</summary>
    public readonly record struct Stage(string Stamp, double Share);

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

    /// <summary>The stamp the cars' loop's parts belong to (<see cref="Part"/>).</summary>
    public const string CarsMade = "spawn: cars made";
    /// <summary>The stamp the road layers' bake's chunks belong to, and its share when the bake cache has none of them (T05's, measured cold).</summary>
    public const string RoadBake = "road bake done";
    public const double ColdRoadBakeMs = 7500;   // (T05's first load in a new user folder: 7.5 s, 0.37 s from the cache)

    private static Stage[] Table(params (string Stamp, double Ms)[] rows) => [.. rows.Select(r => new Stage(r.Stamp, r.Ms))];

    /// <summary>A first load of the mission in this run (the bake cache holding the map's road layers and ground).</summary>
    public static readonly Stage[] First = Table(
        ("build start", 251), ("textures, materials, meshes", 106), ("ground, sky, horizon", 182),
        ("roads flattened", 84), ("road bake", 134), (RoadBake, 355), ("ground materials", 1875),
        ("terrain: 1997 arrays", 660), ("terrain: chunks added", 562), ("terrain built", 56),
        ("scenery spawn", 215), ("done", 711),
        ("horizon built", 157), ("sky", 115), ("mission loaded", 42), ("drop shadows", 308),
        ("look: shadows, draw distance", 522), ("veg: meshes", 154), ("look: modern quality", 211), ("look set", 1),
        ("spawn: sim world set up", 228), ("spawn: combat view", 506), ("spawn: cockpit assembled", 386),
        ("spawn: cockpit meshes", 128), ("spawn: the player's car set up", 1666), (CarsMade, 1027),
        ("spawn: car prewarmed", 297), ("spawn: wrecks prewarmed", 262), ("car spawned", 121),
        ("first tick done", 145), ("vegetation prewarmed", 102),
        ("rehearsal: explosions", 296), ("rehearsal: made", 93), ("rehearsal: frame 0", 23),
        ("rehearsal: frame 1", 1889), ("effects rehearsal", 72));

    /// <summary>A restart of the same mission (the build finds the last one's kit: World/RestartCache.cs).</summary>
    public static readonly Stage[] Restart = Table(
        ("build start", 270), ("textures, materials, meshes", 11), ("ground, sky, horizon", 59),
        ("roads flattened", 12), ("road bake", 24), (RoadBake, 108), ("ground materials", 113),
        ("terrain: 1997 arrays", 3), ("terrain: chunks added", 575), ("terrain built", 52),
        ("scenery spawn", 68), ("done", 55),
        ("horizon built", 188), ("sky", 45), ("mission loaded", 14), ("drop shadows", 2),
        ("look: shadows, draw distance", 90), ("veg: meshes", 78), ("look: modern quality", 146), ("look set", 1),
        ("spawn: sim world set up", 83), ("spawn: combat view", 160), ("spawn: cockpit assembled", 8),
        ("spawn: cockpit meshes", 6), ("spawn: the player's car set up", 719), (CarsMade, 33),
        ("spawn: car prewarmed", 109), ("spawn: wrecks prewarmed", 130), ("car spawned", 101),
        ("first tick done", 8), ("vegetation prewarmed", 66),
        ("rehearsal: explosions", 7), ("rehearsal: made", 50), ("rehearsal: frame 0", 4),
        ("rehearsal: frame 1", 884), ("effects rehearsal", 27));
}

/// <summary>
/// The load under way (<see cref="LoadProgressModel"/>): stamped from the build's thread and the main thread, read by the line under
/// PLEASE STAND BY (StandByTag) in the frames drawn while it is up.
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
    public static void Mark(string stamp) { _model.Mark(stamp); Present?.Invoke(); }

    /// <summary><paramref name="done"/> of <paramref name="of"/> of the stage ending at <paramref name="stamp"/> (the cars made, the road layers' chunks baked; any thread).</summary>
    public static void Part(string stamp, int done, int of) { _model.Part(stamp, done, of); Present?.Invoke(); }

    /// <summary>The stage ending at <paramref name="stamp"/> is worth <paramref name="ms"/> this time (the bake cache had nothing for it).</summary>
    public static void Weigh(string stamp, double ms) => _model.Weigh(stamp, ms);

    public static void Finish() => _model.Finish();

    /// <summary>The main thread loads without letting a frame through (Main's straight-through load: no briefing to build under).</summary>
    public static volatile bool Blocking;

    /// <summary>The loading card's chance to draw its line at a stamp while <see cref="Blocking"/> (LoadingScreen; it checks the thread and the time).</summary>
    public static System.Action? Present;
}
