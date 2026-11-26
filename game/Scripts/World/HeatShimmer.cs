using Godot;

namespace I76.Game.World;

/// <summary>A column of hot air: its foot, its radius there, the flames' top over the foot, the column's height, and how strong (0..1).</summary>
public readonly record struct HeatSource(Vector3 Base, float Radius, float FlameTop, float Height, float Strength);

/// <summary>
/// The fires Heat Shimmer bends the air over: the burning scenery wrecks (StaticFire) and cars (WreckFire) register themselves with a
/// function saying where and how hot they are now (null: not burning, or hidden); the flame streams add their points frame by frame.
/// Nothing is kept or asked for while the setting is off (<see cref="Wanted"/>).
/// </summary>
public static class HeatSources
{
    private static readonly List<(Node3D Node, Func<HeatSource?> Now)> Kept = [];
    private static readonly List<HeatSource> Points = [];

    /// <summary>Heat Shimmer draws fires: the streams' points are worth adding.</summary>
    public static bool Wanted => HeatShimmer.FiresOn;

    public static void Add(Node3D node, Func<HeatSource?> now) => Kept.Add((node, now));

    /// <summary>A source for this frame only (a flame stream's point).</summary>
    public static void Point(HeatSource s) { if (Wanted && Points.Count < 64) Points.Add(s); }

    public static void DropPoints() => Points.Clear();

    /// <summary>This frame's sources (the kept ones burning, the frame's points), the frame's points then dropped.</summary>
    public static void Collect(List<HeatSource> into)
    {
        into.Clear();
        for (int i = Kept.Count - 1; i >= 0; i--)
        {
            var (n, now) = Kept[i];
            if (!GodotObject.IsInstanceValid(n)) { Kept.RemoveAt(i); continue; }
            if (!n.IsInsideTree() || !n.IsVisibleInTree()) continue;
            if (now() is { Strength: > 0.01f } s) into.Add(s);
        }
        into.AddRange(Points);
        Points.Clear();
    }
}

/// <summary>
/// The hot day's ripple (Fires + Road): how much the air low over the ground bends a ray, by the ray's angle to level and how far it goes
/// before it meets something, where section IJ put it: low over the road and the ground beside it, far down the road. Nothing nearer than
/// <see cref="DefaultNear"/> m, full from <see cref="FullAt"/> times that and 1.6 times by 2 km; rays within <see cref="DefaultBelowDeg"/>
/// degrees under level and <see cref="DefaultAboveDeg"/> over it, full within a fifth of each (the over side a separate number only for the
/// Developer panel; the default is the same band either side, as IJ's). The kernel does the same sums with the same numbers (HeatTune's,
/// handed over each frame). The nudge is now an angle, <see cref="AmplitudeRad"/> at full, never under <see cref="MinPixels"/> pixels: IJ's
/// was 1.8 pixels of the 3D view at any size, so at Render Scale 200 % or a big window it shrank to a pixel on the screen. Pure, so the
/// tests check the rule.
/// </summary>
public static class HeatBand
{
    public const float DefaultNear = 75f, FullAt = 2.6f, DefaultAboveDeg = 1.72f, DefaultBelowDeg = 1.72f, FarGain = 0.6f, FarFrom = 400f, FarTo = 2000f;
    /// <summary>The ripple's nudge at full strength, an angle (radians: about 0.15 degrees, about 2.4 px at 1080p), and its least, in pixels.</summary>
    public const float AmplitudeRad = 0.0026f, MinPixels = 1.8f;
    /// <summary>The ripple's cells: at most this many a radian up and down (about 0.2 degrees each; five times as wide), never under
    /// <see cref="CellPixels"/> pixels; the mirage's wavering at most <see cref="MirageCellsPerRad"/>. IJ's: 950, 2 and 700, a pixel or two.</summary>
    public const float CellsPerRad = 300f, CellPixels = 3f, MirageCellsPerRad = 300f;
    /// <summary>The share of each band at full strength.</summary>
    public const float Core = 0.2f;

    public static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>By how far the ray goes (m; the sky: <see cref="float.PositiveInfinity"/>): 0 inside <paramref name="near"/>, 1 from FullAt times it, 1.6 from 2 km.</summary>
    public static float Distance(float dist, float near = DefaultNear) => Smooth(near, near * FullAt, dist) * (1f + FarGain * Smooth(FarFrom, FarTo, dist));

    /// <summary>By the ray's angle to level (degrees, up positive): 1 near level, 0 past the band's edge.</summary>
    public static float Angle(float elevDeg, float aboveDeg = DefaultAboveDeg, float belowDeg = DefaultBelowDeg) =>
        elevDeg >= 0 ? 1f - Smooth(Core * aboveDeg, aboveDeg, elevDeg) : 1f - Smooth(Core * belowDeg, belowDeg, -elevDeg);

    public static float Weight(float elevDeg, float dist) => Angle(elevDeg) * Distance(dist);

    /// <summary>
    /// By the ground (HeatTune.ByGround, the default): the ripple on ground that runs nearly flat to the line of sight, by the angle
    /// between the view ray and the surface (degrees; a level road or flat far ahead a fraction of a degree, a rise ahead or ground
    /// seen from a hill several), full within a fifth of <paramref name="bandDeg"/> and gone past it, by distance as <see cref="Distance"/>,
    /// and by the surface: <see cref="PavedShare"/> on paved road (<paramref name="paved"/>, HeatRoadMask), <see cref="GroundShare"/> on
    /// open ground, less on rough or rocky ground (its normal from the depths leaning: full from 0.99 up, none under 0.9), and nothing
    /// on what is not ground at all (a plant, a car's side, a wall: <paramref name="upY"/> under 0.85).
    /// </summary>
    public static float Surface(float grazeDeg, float dist, float paved, float upY = 1f, float pavedShare = PavedShare, float groundShare = GroundShare,
        float bandDeg = DefaultBelowDeg, float near = DefaultNear) =>
        (1f - Smooth(Core * bandDeg, bandDeg, grazeDeg)) * Smooth(0.85f, 0.95f, upY) * Share(paved, upY, pavedShare, groundShare) * Distance(dist, near);

    /// <summary>The surface's share: paved road's, open ground's (less as the ground's normal leans, rough or rocky), mixed by the paved part.</summary>
    public static float Share(float paved, float upY, float pavedShare = PavedShare, float groundShare = GroundShare)
    {
        float p = Math.Clamp(paved, 0f, 1f);
        return pavedShare * p + groundShare * Smooth(0.9f, 0.99f, upY) * (1f - p);
    }

    /// <summary>The surfaces' shares as shipped: paved road full, open flat ground half; the mirage off the road a quarter of the road's.</summary>
    public const float PavedShare = 1f, GroundShare = 0.5f, MirageGroundShare = 0.25f;

    /// <summary>The angle (degrees) between a view ray (unit) and a surface (unit normal): 0 skimming it, 90 straight on.</summary>
    public static float Graze(System.Numerics.Vector3 ray, System.Numerics.Vector3 normal) =>
        MathF.Asin(Math.Clamp(MathF.Abs(System.Numerics.Vector3.Dot(ray, normal)), 0f, 1f)) * 180f / MathF.PI;
}

/// <summary>
/// Heat Shimmer's numbers, live: the shipped values (twice the first version's), the I76_HEAT_ hooks over them, and the Developer panel's
/// Heat shimmer group (Ui/DevHeat.cs) over both while a mission runs. Never saved: a new run starts from the shipped values. Read each
/// frame (the pass's strengths and reach) and at a hot day's test; nothing else.
/// </summary>
public static class HeatTune
{
    public const float DefaultRoadNear = HeatBand.DefaultNear, DefaultRippleDeg = HeatBand.DefaultBelowDeg, DefaultRippleAboveDeg = HeatBand.DefaultAboveDeg,
        DefaultMirageDeg = 2.29f, DefaultFireFull = 60f, DefaultFireFar = 260f;

    /// <summary>The strengths (1 the shipped; I76_HEAT_SCALE times I76_HEAT_FIRE / ROAD / MIRAGE).</summary>
    public static float Fire = HeatShimmer.EnvGain("I76_HEAT_FIRE"), Road = HeatShimmer.EnvGain("I76_HEAT_ROAD"), Mirage = HeatShimmer.EnvGain("I76_HEAT_MIRAGE");
    /// <summary>Where the road's ripple and the mirage start (m; the ripple full at HeatBand.FullAt times it, the mirage at 2).</summary>
    public static float RoadNear = DefaultRoadNear;
    /// <summary>The ripple's band (HeatBand): rays down to this many degrees under level and up to RippleAboveDeg over it (full within 0.3 of each).</summary>
    public static float RippleDeg = DefaultRippleDeg, RippleAboveDeg = DefaultRippleAboveDeg;
    /// <summary>The mirage's band: level ground seen under this many degrees (full under 0.3 of it).</summary>
    public static float MirageDeg = DefaultMirageDeg;
    /// <summary>The fires' bend full out to FireFull m and gone at FireFar; the columns' size (radius and height) times FireColumn.</summary>
    public static float FireFull = DefaultFireFull, FireFar = DefaultFireFar, FireColumn = 1f;
    /// <summary>The hot day forced (true or false) or found (null; I76_HEAT_HOT=1|0 sets it).</summary>
    public static bool? ForceHot = EnvHot();
    /// <summary>The ripple by the ground's grazing angle and its surface (HeatBand.Surface; the shipped), or, off, IJ's band round level
    /// over any ground (to compare; I76_HEAT_BY_GROUND=0 starts so).</summary>
    public static bool ByGround = EnvByGround();
    private static bool EnvByGround() => System.Environment.GetEnvironmentVariable("I76_HEAT_BY_GROUND") != "0";
    /// <summary>The surfaces' shares (HeatBand): the ripple on paved road and on open ground, the mirage off the road (a share of the road's).</summary>
    public static float PavedShare = HeatBand.PavedShare, GroundShare = HeatBand.GroundShare, MirageGround = HeatBand.MirageGroundShare;
    /// <summary>The Developer panel's group is up: the pass runs while the game is paused, so a change shows at once.</summary>
    public static bool Live { get => _live; set { _live = value; HeatShimmer.Live(value); } }
    private static bool _live;

    private static string HeatStrengthName() => I76.Game.Shell.HeatStrengthRow.NameOf(HeatShimmer.Strength);

    private static bool? EnvHot() => System.Environment.GetEnvironmentVariable("I76_HEAT_HOT") switch { "1" => true, "0" => false, _ => null };

    /// <summary>The numbers in one line, to hand back ("heat tune: ...").</summary>
    public static string Line() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"heat tune: strength {HeatShimmer.Strength:0.##} ({HeatStrengthName()}); fire {Fire:0.##} road {Road:0.##} mirage {Mirage:0.##}; road from {RoadNear:0} m, ripple band {RippleDeg:0.##} deg under level and {RippleAboveDeg:0.##} over, mirage band {MirageDeg:0.##} deg; fire full to {FireFull:0} m, gone at {FireFar:0} m, column x{FireColumn:0.##}; {(ByGround ? $"by the ground: paved x{PavedShare:0.##}, open ground x{GroundShare:0.##}, mirage off road x{MirageGround:0.##}" : "band round level (IJ)")}; hot day: sun >= {HotDay.MinElevation:0} deg, warmth >= {HotDay.MinWarmth:0.###}, {(ForceHot is { } f ? (f ? "forced hot" : "forced not hot") : "found")}");

    /// <summary>Back to the shipped values (and the hooks').</summary>
    public static void Reset()
    {
        Fire = HeatShimmer.EnvGain("I76_HEAT_FIRE"); Road = HeatShimmer.EnvGain("I76_HEAT_ROAD"); Mirage = HeatShimmer.EnvGain("I76_HEAT_MIRAGE");
        RoadNear = DefaultRoadNear; RippleDeg = DefaultRippleDeg; RippleAboveDeg = DefaultRippleAboveDeg; MirageDeg = DefaultMirageDeg;
        FireFull = DefaultFireFull; FireFar = DefaultFireFar; FireColumn = 1f;
        HotDay.MinElevation = HotDay.DefaultMinElevation; HotDay.MinWarmth = HotDay.DefaultMinWarmth;
        ForceHot = EnvHot();
        ByGround = EnvByGround(); PavedShare = HeatBand.PavedShare; GroundShare = HeatBand.GroundShare; MirageGround = HeatBand.MirageGroundShare;
        HeatShimmer.Reevaluate();
    }
}

/// <summary>
/// Graphic Detail's Heat Shimmer ([port] heat_shimmer, HeatShimmerRow), the modern look's, not the original's: the air over a fire bends
/// what is behind it, and on a hot day (HotDay, Fires + Road) the air low over the far ground does too, with the road's mirage. One compute
/// pass after the scene's transparent pass (a CompositorEffect on the main camera, beside the smoke layer's), over the screen's rectangle
/// that holds something to bend: the colour there copied aside, then each pixel read back from a place nudged by scrolling noise.
/// <list type="bullet">
/// <item>Fires: each source a column rising from its flames; the ray's nearest point to the column's axis says how deep in it the pixel
/// looks. The nudge is an angle (the same look at any resolution or render scale), fading out from 60 to 260 m, and only where the
/// scene lies behind the column: the cockpit, a car in front, anything nearer is never bent, nor pulled into the bend.</item>
/// <item>Road: by a hot day only, ground seen at a grazing angle, within about 1.7 degrees of its surface, from 75 m out, gets a fine
/// vertical ripple (HeatBand.Surface): full on paved road (HeatRoadMask), half on open flat ground, less on rough ground, none on
/// plants, rock faces or ground seen steeply; level ground under about 2 degrees and from 75 m (full from 150 m), the mirage: the sky
/// at the mirrored angle (the sky above whatever stands there) shows through, wavering, on paved road and a quarter as much off it.
/// HeatTune.ByGround off gives back section IJ's band round level over any ground.</item>
/// </list>
/// The 3D view's own pass: the 2D dash and HUD are drawn after it, the mirrors and the weapon camera are other cameras and stay
/// unbent. Forward+ only (Compatibility: none). Off, nothing is gathered and the effect is not in the frame.
/// The strengths are twice the first version's (the owner could barely see it: the road's band was a few pixels at the road's far end);
/// I76_HEAT_SCALE=K scales all three (0.5 the first version's), I76_HEAT_ROAD/MIRAGE/FIRE each. I76_HEAT_STATS=1 prints the
/// sources and the rectangle every 2 s.
/// </summary>
public partial class HeatShimmer : Node3D
{
    /// <summary>The setting (HeatShimmerRow: 0 Off, 1 Fires, 2 Fires + Road).</summary>
    public static int Setting { get; private set; }
    public static bool Look1997 { get; set; }
    /// <summary>The mission is a hot day (HotDay).</summary>
    public static bool HotDay { get; private set; }
    public static bool FiresOn => Setting >= 1 && !Look1997 && _supported;
    public static bool RoadOn => Setting >= 2 && !Look1997 && HotDay && _supported;

    /// <summary>All of it (I76_HEAT_SCALE, 0..4; 1 the shipped, 0.5 the first version's), times the road shimmer, mirage and fires each (I76_HEAT_ROAD, I76_HEAT_MIRAGE, I76_HEAT_FIRE): HeatTune's starting strengths.</summary>
    public static readonly float Scale = Env("I76_HEAT_SCALE", 1f);
    public static float EnvGain(string name) => Env(name, 1f) * Scale;
    private static readonly bool Stats = System.Environment.GetEnvironmentVariable("I76_HEAT_STATS") == "1";
    /// <summary>I76_HEAT_DEBUG=1: the bend painted (red: how far a pixel is nudged, 4 px full; green: the mirage), to tune and check the masks;
    /// 2 the mirage's masks; 3 how far the bend can reach, steady (red: the envelope of the nudge, 6 px full; green: the mirage); 4 what the
    /// pass changed this frame (the colour's difference, 8 times, black where nothing is bent: a measure of how visible it is).</summary>
    private static readonly float DebugMode = System.Environment.GetEnvironmentVariable("I76_HEAT_DEBUG") switch { "1" => 1f, "2" => 2f, "3" => 3f, "4" => 4f, _ => 0f };

    /// <summary>
    /// The ripple's look (HeatBand's): its nudge at full (radians) and least (px), its cells' most per radian and fewest pixels, the
    /// mirage's most per radian. I76_HEAT_RIPPLE="a,p,k,c,m" sets them (section IJ's: "0,1.8,950,2,700"), for before-and-after captures.
    /// </summary>
    private static readonly float[] Ripple = RippleLook();

    private static float[] RippleLook()
    {
        float[] v = [HeatBand.AmplitudeRad, HeatBand.MinPixels, HeatBand.CellsPerRad, HeatBand.CellPixels, HeatBand.MirageCellsPerRad];
        var parts = System.Environment.GetEnvironmentVariable("I76_HEAT_RIPPLE")?.Split(',');
        for (int i = 0; parts is not null && i < Math.Min(parts.Length, v.Length); i++)
            if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 0) v[i] = f;
        v[3] = Math.Max(0.5f, v[3]);
        return v;
    }

    private static float Env(string name, float d) =>
        float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? Math.Clamp(v, 0f, 4f) : d;

    /// <summary>Shimmer Strength (HeatStrengthRow's factor, 1 Medium): fire, ripple and mirage together, under HeatTune's.</summary>
    public static float Strength { get; set; } = 1f;

    public static void Apply(int setting, bool look1997, float strength = 1f) { Setting = setting; Look1997 = look1997; Strength = strength; }

    /// <summary>The running mission's sun elevation (degrees), day or night, palette warmth (null: none read), ground family.</summary>
    public static (float Sun, bool Day, float? Warmth, string? Ground) Mission { get; private set; }

    public static void SetMission(I76.Formats.Mission.LightPreset light, I76.Formats.Imaging.Palette? palette, string? ground = null)
    {
        Know(light, palette, ground);
        GD.Print($"heat shimmer: {Readout()}");
    }

    /// <summary>The mission's numbers kept and its hot day found (SetMission without the log line).</summary>
    public static void Know(I76.Formats.Mission.LightPreset light, I76.Formats.Imaging.Palette? palette, string? ground = null)
    {
        Mission = (I76.Game.Sim.SmokeLight.Elevation(light), light.IsDay, palette is null ? null : World.HotDay.Warmth(palette.Rgb), ground);
        Reevaluate();
    }

    /// <summary>The hot day again from the mission's numbers (the Developer panel changed the rule).</summary>
    public static void Reevaluate()
    {
        var m = Mission;
        HotDay = HeatTune.ForceHot ?? (!World.HotDay.Snow(m.Ground) && World.HotDay.Is(m.Day, m.Sun, m.Warmth ?? World.HotDay.MinWarmth));
    }

    /// <summary>"a hot day (sun 35 deg, day, palette warmth 0.493)".</summary>
    public static string Readout()
    {
        var m = Mission;
        return $"{(HotDay ? "a hot day" : "not a hot day")}{(HeatTune.ForceHot is null ? "" : " (forced)")} (sun {m.Sun:0} deg, {(m.Day ? "day" : "night")}, palette warmth {(m.Warmth is { } w ? w.ToString("0.000") : "none")}{(World.HotDay.Snow(m.Ground) ? ", snow" : "")})";
    }

    private const int MaxSources = 16, Header = 92, UboFloats = Header + MaxSources * 8;
    /// <summary>Where the fires' bend is gone (it fades from FireFull), and where the road's ripple and mirage start (metres): the shipped, HeatTune's defaults.</summary>
    public const float FireFull = HeatTune.DefaultFireFull, FireFar = HeatTune.DefaultFireFar, RoadNear = HeatTune.DefaultRoadNear;
    /// <summary>The least angle either side of level the road's band takes (degrees): the mirage's mirrored sky, the nudge's reach.</summary>
    public const float BandDegrees = 2.5f;
    private static bool _supported = true;
    private readonly ShimmerEffect _effect = new();
    private readonly List<HeatSource> _all = [];
    private double _time;

    // handed to the render thread under the lock
    private static readonly object Lock = new();
    private static readonly float[] Src = new float[MaxSources * 8];
    private static int _count;
    private static Rect2 _rect;   // normalised to the view
    private static float _road, _mirage, _clock;
    private static Vector3 _right;
    private static Vector4 _roadMask, _shares;   // the mask's corner from the floating origin (game X, Z), its size, by the ground (1); the surfaces' shares
    /// <summary>The mission's sky, for a mirage whose mirrored direction meets no sky on the screen (MissionBuilder.SkyAverage).</summary>
    public static Color Sky { get; set; } = new(0.7f, 0.7f, 0.75f);
    private static Rid _maskRd;

    /// <summary>The world's floating origin (game frame; DamageView's): a Godot position plus it is the game's.</summary>
    public Func<System.Numerics.Vector3>? Origin { get; init; }

    // the mission's roads (SetRoads) and the paved mask baked round the camera (HeatRoadMask)
    private static IReadOnlyList<I76.Formats.Assembly.RoadRaster.FlatQuad>? _quads;
    private static IReadOnlyList<I76.Formats.Assembly.RoadRaster.FlatTri>? _tris;
    private static Func<int, I76.Formats.Assembly.RoadRaster.Tex?>? _paved;
    private static System.Numerics.Vector2? _maskCorner;   // the baked square's, null: none yet (or no paved road in it)
    private static System.Threading.Tasks.Task<(System.Numerics.Vector2 Corner, byte[]? Mask)>? _baking;
    private ImageTexture? _maskTex, _blankTex;
    private Rid _maskTexRd, _blankRd;

    /// <summary>The mission's roads, as the terrain's road layers are baked from them (MissionBuilder): the mask is baked from these when the road shimmer first wants it.</summary>
    public static void SetRoads(IReadOnlyList<I76.Formats.Assembly.RoadRaster.FlatQuad>? quads, IReadOnlyList<I76.Formats.Assembly.RoadRaster.FlatTri>? tris,
        IReadOnlyDictionary<int, I76.Formats.Assembly.RoadRaster.Tex?>? textures)
    {
        _quads = quads; _tris = tris; _maskCorner = null; _baking = null;
        if (quads is null || textures is null) { _paved = null; return; }
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<int, I76.Formats.Assembly.RoadRaster.Tex?>();
        _paved = i => cache.GetOrAdd(i, k => textures.GetValueOrDefault(k) is { } t ? HeatRoadMask.PavedTexture(t) : null);
    }

    /// <summary>The paved mask round the camera (game X, Z): baked off the main thread when there is none or the camera has left its middle; true while one is up.</summary>
    private bool MaskAround(float gx, float gz)
    {
        if (_baking is { IsCompleted: true } done)
        {
            _baking = null;
            var (corner, mask) = done.Result;
            if (mask is null) _maskCorner = null;
            else
            {
                var img = Image.CreateFromData(HeatRoadMask.Res, HeatRoadMask.Res, false, Image.Format.R8, mask);
                if (_maskTex is null) _maskTex = ImageTexture.CreateFromImage(img); else _maskTex.Update(img);
                _maskTexRd = RenderingServer.TextureGetRdTexture(_maskTex.GetRid());
                _maskCorner = corner;
            }
            _maskAt = corner;
        }
        var want = HeatRoadMask.CornerFor(gx, gz);
        if (_baking is null && _quads is not null && _paved is not null && (_maskAt is not { } at || System.Numerics.Vector2.Distance(at, want) > HeatRoadMask.Recentre))
        {
            var (q, t, f) = (_quads, _tris, _paved);
            _maskAt = want;   // not asked again while it bakes
            _baking = System.Threading.Tasks.Task.Run(() => (want, HeatRoadMask.Bake(q, t, f, want)));
        }
        return _maskCorner is not null && _maskTex is not null;
    }
    private static System.Numerics.Vector2? _maskAt;

    public override void _Ready()
    {
        if (RenderingServer.GetCurrentRenderingMethod() != "forward_plus" || RenderingServer.GetRenderingDevice() is null) _supported = false;
        _blankTex = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.R8));
        _blankRd = RenderingServer.TextureGetRdTexture(_blankTex.GetRid());
        _maskAt = null; _maskCorner = null;
    }

    private static HeatShimmer? _node;

    /// <summary>The Developer panel's group up (HeatTune.Live): the pass on while the game is paused, so its changes show at once.</summary>
    internal static void Live(bool on)
    {
        if (_node is { } n && IsInstanceValid(n)) n.ProcessMode = on ? ProcessModeEnum.Always : ProcessModeEnum.Inherit;
    }

    public override void _EnterTree() { _node = this; if (HeatTune.Live) ProcessMode = ProcessModeEnum.Always; }

    public override void _ExitTree()
    {
        if (_node == this) _node = null;
        var cam = GetViewport()?.GetCamera3D();
        if (cam?.Compositor is { } c && c.CompositorEffects.Contains(_effect))
        {
            var list = c.CompositorEffects; list.Remove(_effect); c.CompositorEffects = list;
        }
    }

    public override void _Process(double delta)
    {
        bool fires = FiresOn, road = RoadOn;
        var camNow = GetViewport()?.GetCamera3D();
        bool masked = false;
        if (road && HeatTune.ByGround && camNow is not null)
        {
            var o = Origin?.Invoke() ?? default;
            var g = camNow.GlobalPosition;
            masked = MaskAround(g.X + o.X, -g.Z + o.Z);   // none (no paved road round here, or not yet baked): all of it open ground
        }
        if (!fires && !road) { if (_effect.Enabled) _effect.Enabled = false; HeatSources.DropPoints(); return; }
        _time += delta;
        var cam = GetViewport().GetCamera3D();
        if (cam is null) { _effect.Enabled = false; return; }
        Attach(cam);
        var view = GetViewport().GetVisibleRect().Size;
        var eye = cam.GlobalPosition;
        HeatSources.Collect(_all);
        if (HeatTune.FireColumn != 1f)
            for (int i = 0; i < _all.Count; i++) _all[i] = _all[i] with { Radius = _all[i].Radius * HeatTune.FireColumn, FlameTop = _all[i].FlameTop * HeatTune.FireColumn, Height = _all[i].Height * HeatTune.FireColumn };
        // the nearest in front within reach
        _all.RemoveAll(s => s.Base.DistanceTo(eye) > HeatTune.FireFar + s.Height || cam.IsPositionBehind(s.Base + Vector3.Up * s.Height * 0.5f) && s.Base.DistanceTo(eye) > s.Height + s.Radius * 3f);
        _all.Sort((a, b) => a.Base.DistanceSquaredTo(eye).CompareTo(b.Base.DistanceSquaredTo(eye)));
        int n = Math.Min(_all.Count, MaxSources);
        Rect2? rect = null;
        for (int i = 0; i < n; i++) rect = Union(rect, ColumnRect(cam, view, _all[i]));
        if (road) rect = Union(rect, BandRect(cam, view, HeatTune.ByGround));
        if (rect is not { } r || r.Size.X <= 0 || r.Size.Y <= 0) { _effect.Enabled = false; return; }
        lock (Lock)
        {
            for (int i = 0; i < n; i++)
            {
                var s = _all[i];
                int o = i * 4;
                Src[o] = s.Base.X; Src[o + 1] = s.Base.Y; Src[o + 2] = s.Base.Z; Src[o + 3] = s.Radius;
                o = MaxSources * 4 + i * 4;
                Src[o] = s.FlameTop; Src[o + 1] = s.Height; Src[o + 2] = Math.Clamp(s.Strength, 0f, 1.5f) * HeatTune.Fire * Strength; Src[o + 3] = (i * 0.618034f) % 1f;
            }
            _count = n;
            _rect = r;
            _road = road ? HeatTune.Road * Strength : 0f;
            _mirage = road ? HeatTune.Mirage * Strength : 0f;
            _clock = (float)(_time % 4096.0);
            _right = cam.GlobalTransform.Basis.X;
            var org = Origin?.Invoke() ?? default;
            _roadMask = masked && _maskCorner is { } mc ? new Vector4(org.X - mc.X, org.Z - mc.Y, HeatRoadMask.Size, HeatTune.ByGround ? 1f : 0f)
                : new Vector4(-1e7f, -1e7f, HeatRoadMask.Size, HeatTune.ByGround ? 1f : 0f);
            _shares = new Vector4(HeatTune.PavedShare, HeatTune.GroundShare, HeatTune.MirageGround, 0f);
            _maskRd = masked ? _maskTexRd : _blankRd;
        }
        _effect.Enabled = true;
        if (Stats) Report(n, r);
    }

    private ulong _statFrom;
    private void Report(int n, Rect2 r)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _statFrom < 2000) return;
        _statFrom = now;
        GD.Print($"heat shimmer: {n} sources (of {_all.Count}), road {(RoadOn ? "on" : "off")}{(HeatTune.ByGround ? $", paved mask {(_maskCorner is { } c ? $"at {c.X:0},{c.Y:0}" : "none")}" : "")}, rect {r.Position.X:0.00},{r.Position.Y:0.00} {r.Size.X:0.00}x{r.Size.Y:0.00}");
    }

    /// <summary>The effect in the camera's compositor, beside what is there (the smoke layer's); a camera without one gets one.</summary>
    private void Attach(Camera3D cam)
    {
        // as last frame: no array copied out of the engine, but looked at again each second (another effect may have set the list)
        if (cam == _attachedCam && cam.Compositor == _attachedTo && _attachedTo is not null && ++_attachAge % 60 != 0) return;
        if (cam.Compositor is null) cam.Compositor = new Compositor { CompositorEffects = [_effect] };
        else
        {
            var list = cam.Compositor.CompositorEffects;
            if (!list.Contains(_effect)) { list.Add(_effect); cam.Compositor.CompositorEffects = list; }
        }
        (_attachedCam, _attachedTo) = (cam, cam.Compositor);
    }

    private Camera3D? _attachedCam;
    private Compositor? _attachedTo;
    private int _attachAge;

    private static Rect2? Union(Rect2? a, Rect2? b) => a is null ? b : b is null ? a : a.Value.Merge(b.Value);

    /// <summary>The column's box on the view (normalised), the whole view when the eye is in or by it, none off the view.</summary>
    private static Rect2? ColumnRect(Camera3D cam, Vector2 view, HeatSource s)
    {
        float r = s.Radius * 1.9f;
        Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
        for (int k = 0; k < 8; k++)
        {
            var p = s.Base + new Vector3((k & 1) == 0 ? -r : r, (k & 2) == 0 ? 0 : s.Height, (k & 4) == 0 ? -r : r);
            if (cam.IsPositionBehind(p)) return new Rect2(0, 0, 1, 1);
            var q = cam.UnprojectPosition(p) / view;
            lo = lo.Min(q); hi = hi.Max(q);
        }
        lo = lo.Clamp(Vector2.Zero, Vector2.One); hi = hi.Clamp(Vector2.Zero, Vector2.One);
        return hi.X > lo.X && hi.Y > lo.Y ? new Rect2(lo, hi - lo) : null;
    }

    /// <summary>The band round the horizon the road shimmer and the mirage (and its mirrored sky) take, degrees over and under level: at least <see cref="BandDegrees"/>, across the view.</summary>
    private static float BandOver() => Math.Max(BandDegrees, Math.Max(HeatTune.RippleAboveDeg, HeatTune.MirageDeg) + 0.5f);
    private static float BandUnder() => Math.Max(BandDegrees, Math.Max(HeatTune.RippleDeg, HeatTune.MirageDeg) + 0.5f);
    /// <summary>By the ground: a road or a flat climbing ahead or falling away is seen well off level and still at a grazing angle; this much either side (degrees).</summary>
    public const float RoadBandDegrees = 6f;

    private static Rect2? BandRect(Camera3D cam, Vector2 view, bool roadOnly = false)
    {
        var fwd = -cam.GlobalTransform.Basis.Z;
        float yaw = Mathf.Atan2(fwd.X, fwd.Z);
        float lo = float.MaxValue, hi = float.MinValue;
        for (int a = -8; a <= 8; a++)
            foreach (float el in roadOnly ? (float[])[-Math.Max(BandUnder(), RoadBandDegrees), Math.Max(BandOver(), RoadBandDegrees)] : [-BandUnder(), BandOver()])
            {
                float az = yaw + a * 0.2f, e = Mathf.DegToRad(el);
                var d = new Vector3(Mathf.Sin(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(az) * Mathf.Cos(e));
                var p = cam.GlobalPosition + d * 1000f;
                if (cam.IsPositionBehind(p)) continue;
                var q = cam.UnprojectPosition(p) / view;
                if (q.X < -1 || q.X > 2) continue;
                lo = Math.Min(lo, q.Y); hi = Math.Max(hi, q.Y);
            }
        lo = Math.Clamp(lo, 0f, 1f); hi = Math.Clamp(hi, 0f, 1f);
        return hi > lo ? new Rect2(0, lo, 1, hi - lo) : null;
    }

    // ---- render thread ----

    private sealed partial class ShimmerEffect : CompositorEffect
    {
        public ShimmerEffect()
        {
            EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
            AccessResolvedColor = true;
            AccessResolvedDepth = true;
            Enabled = false;
        }

        public override void _RenderCallback(int effectCallbackType, RenderData renderData)
        {
            if (_failed || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent) return;
            try { Render(renderData); }
            catch (Exception e) { _failed = true; GD.PrintErr($"heat shimmer: {e}"); }
        }
    }

    private static bool _failed;
    private static Rid _copyShader, _copyPipeline, _copySet;
    private static Rid _shader, _pipeline, _linear, _nearest, _ubo, _tmp, _set, _setColor, _setDepth, _setMask;
    private static Vector2I _tmpSize;
    private static long _tmpFormat = -1;
    private static readonly float[] Ubo = new float[UboFloats];
    private static readonly byte[] UboBytes = new byte[UboFloats * 4];
    private static Godot.Collections.Array<RDUniform>? _uniforms;

    private static void Render(RenderData renderData)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers || renderData.GetRenderSceneData() is not RenderSceneDataRD scene) return;
        if (buffers.GetViewCount() != 1) return;   // one view (no XR)
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0) return;
        if (!_pipeline.IsValid && !Setup(rd)) return;
        var color = buffers.GetColorLayer(0, false);
        var depth = buffers.GetDepthLayer(0, false);
        if (!color.IsValid || !depth.IsValid) return;
        var fmt = rd.TextureGetFormat(color);
        if (fmt is null) return;
        if (fmt.Format != RenderingDevice.DataFormat.R16G16B16A16Sfloat) { _failed = true; GD.PrintErr($"heat shimmer: the scene's colour is {fmt.Format}, not RGBA16F; off"); return; }
        if (!_tmp.IsValid || _tmpSize != size || (long)fmt.Format != _tmpFormat) MakeTmp(rd, size, fmt.Format);

        Rect2 rn; int count; float road, mirage, clock; Vector3 right; Vector4 roadMask, shares; Rid maskRd;
        lock (Lock)
        {
            rn = _rect; count = _count; road = _road; mirage = _mirage; clock = _clock; right = _right; roadMask = _roadMask; shares = _shares; maskRd = _maskRd;
            Array.Copy(Src, 0, Ubo, Header, MaxSources * 8);
        }
        var camT = scene.GetCamTransform();
        var proj = scene.GetViewProjection(0);
        var view = new Projection(camT.AffineInverse());
        Write(proj.Inverse(), Ubo.AsSpan(0, 16));
        Write(new Projection(camT), Ubo.AsSpan(16, 16));
        Write(proj * view, Ubo.AsSpan(32, 16));
        // the pixels to bend, and round them the copy (as far as a nudge reaches)
        float focal = MathF.Abs(proj.Y.Y) * 0.5f * size.Y;
        float most = Math.Max(2f, size.Y * 0.012f * Math.Max(1f, Scale * Strength));
        int margin = (int)MathF.Ceiling(most) + 3;
        int x0 = Math.Clamp((int)MathF.Floor(rn.Position.X * size.X), 0, size.X), y0 = Math.Clamp((int)MathF.Floor(rn.Position.Y * size.Y), 0, size.Y);
        int x1 = Math.Clamp((int)MathF.Ceiling(rn.End.X * size.X), 0, size.X), y1 = Math.Clamp((int)MathF.Ceiling(rn.End.Y * size.Y), 0, size.Y);
        if (x1 <= x0 || y1 <= y0) return;
        Ubo[48] = x0; Ubo[49] = y0; Ubo[50] = x1; Ubo[51] = y1;
        Ubo[52] = size.X; Ubo[53] = size.Y; Ubo[54] = 1f / size.X; Ubo[55] = 1f / size.Y;
        Ubo[56] = clock; Ubo[57] = count; Ubo[58] = road; Ubo[59] = mirage;
        Ubo[60] = SmokeDepthReversed(proj) ? 1f : -1f; Ubo[61] = focal; Ubo[62] = HeatTune.RoadNear; Ubo[63] = most;
        Ubo[64] = right.X; Ubo[65] = right.Y; Ubo[66] = right.Z; Ubo[67] = DebugMode;
        Ubo[68] = 0f; Ubo[69] = MathF.Sin(Mathf.DegToRad(HeatTune.MirageDeg));
        Ubo[70] = HeatTune.FireFull; Ubo[71] = Math.Max(HeatTune.FireFar, HeatTune.FireFull + 1f);
        Ubo[72] = Math.Max(0.05f, HeatTune.RippleAboveDeg); Ubo[73] = Math.Max(0.05f, HeatTune.RippleDeg); Ubo[74] = Ripple[0]; Ubo[75] = Ripple[1];
        Ubo[76] = Ripple[2]; Ubo[77] = Ripple[3]; Ubo[78] = Ripple[4]; Ubo[79] = 0f;
        Ubo[80] = roadMask.X; Ubo[81] = roadMask.Y; Ubo[82] = roadMask.Z; Ubo[83] = roadMask.W;
        Ubo[84] = shares.X; Ubo[85] = shares.Y; Ubo[86] = shares.Z; Ubo[87] = shares.W;
        var sky = Sky; Ubo[88] = sky.R; Ubo[89] = sky.G; Ubo[90] = sky.B; Ubo[91] = 1f;
        if (!maskRd.IsValid) return;
        System.Buffer.BlockCopy(Ubo, 0, UboBytes, 0, UboFloats * 4);
        rd.BufferUpdate(_ubo, 0, UboFloats * 4, UboBytes);

        int cx0 = Math.Max(0, x0 - margin), cy0 = Math.Max(0, y0 - margin), cx1 = Math.Min(size.X, x1 + margin), cy1 = Math.Min(size.Y, y1 + margin);

        if (!_set.IsValid || !rd.UniformSetIsValid(_set) || !_copySet.IsValid || !rd.UniformSetIsValid(_copySet) || color != _setColor || depth != _setDepth || maskRd != _setMask)
        {
            if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
            if (_copySet.IsValid && rd.UniformSetIsValid(_copySet)) rd.FreeRid(_copySet);
            _setColor = color; _setDepth = depth; _setMask = maskRd;
            // the scene's colour cannot be copied from (no copy-from usage): a kernel copies the rectangle aside
            var cin = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 }; cin.AddId(color);
            var cout = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 }; cout.AddId(_tmp);
            _copySet = rd.UniformSetCreate([cin, cout], _copyShader, 0);
            var img = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 }; img.AddId(color);
            var src = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 }; src.AddId(_linear); src.AddId(_tmp);
            var dep = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 }; dep.AddId(_nearest); dep.AddId(depth);
            var ubo = new RDUniform { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 3 }; ubo.AddId(_ubo);
            var msk = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 4 }; msk.AddId(_linear); msk.AddId(maskRd);
            _uniforms = [img, src, dep, ubo, msk];
            _set = rd.UniformSetCreate(_uniforms, _shader, 0);
        }
        if (!_set.IsValid || !_copySet.IsValid) { _failed = true; GD.PrintErr("heat shimmer: no uniform set (the scene's colour cannot be written by a kernel?); off"); return; }
        long cl = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(cl, _copyPipeline);
        rd.ComputeListBindUniformSet(cl, _copySet, 0);
        Span<byte> rect = stackalloc byte[16];
        System.BitConverter.TryWriteBytes(rect, cx0); System.BitConverter.TryWriteBytes(rect[4..], cy0);
        System.BitConverter.TryWriteBytes(rect[8..], cx1); System.BitConverter.TryWriteBytes(rect[12..], cy1);
        rd.ComputeListSetPushConstant(cl, rect, 16);
        rd.ComputeListDispatch(cl, (uint)((cx1 - cx0 + 7) / 8), (uint)((cy1 - cy0 + 7) / 8), 1);
        rd.ComputeListAddBarrier(cl);
        rd.ComputeListBindComputePipeline(cl, _pipeline);
        rd.ComputeListBindUniformSet(cl, _set, 0);
        rd.ComputeListDispatch(cl, (uint)((x1 - x0 + 7) / 8), (uint)((y1 - y0 + 7) / 8), 1);
        rd.ComputeListEnd();
    }

    /// <summary>Whether a larger depth is nearer the eye (Godot's reversed z), as the smoke layer reads it.</summary>
    private static bool SmokeDepthReversed(Projection proj) => I76.Game.Sim.SmokeDepth.NearerIsLarger(proj);

    private static void MakeTmp(RenderingDevice rd, Vector2I size, RenderingDevice.DataFormat format)
    {
        if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
        if (_copySet.IsValid && rd.UniformSetIsValid(_copySet)) rd.FreeRid(_copySet);
        _set = default; _copySet = default;
        if (_tmp.IsValid) rd.FreeRid(_tmp);
        _tmp = rd.TextureCreate(new RDTextureFormat
        {
            Width = (uint)size.X, Height = (uint)size.Y, Format = format,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
        }, new RDTextureView());
        _tmpSize = size; _tmpFormat = (long)format;
    }

    private static bool Setup(RenderingDevice rd)
    {
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = Kernel });
        if (spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } err) { _failed = true; GD.PrintErr($"heat shimmer: the kernel did not compile: {err}"); return false; }
        _shader = rd.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) { _failed = true; GD.PrintErr("heat shimmer: the backend refused the kernel"); return false; }
        _pipeline = rd.ComputePipelineCreate(_shader);
        var copy = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = CopyKernel });
        if (copy.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } cerr) { _failed = true; GD.PrintErr($"heat shimmer: the copy kernel did not compile: {cerr}"); return false; }
        _copyShader = rd.ShaderCreateFromSpirV(copy);
        if (!_copyShader.IsValid) { _failed = true; return false; }
        _copyPipeline = rd.ComputePipelineCreate(_copyShader);
        _linear = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        _nearest = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Nearest, MagFilter = RenderingDevice.SamplerFilter.Nearest, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        _ubo = rd.UniformBufferCreate(UboFloats * 4);
        GD.Print("heat shimmer: kernel ready");
        return _pipeline.IsValid;
    }

    private static void Write(Projection p, Span<float> to)
    {
        to[0] = p.X.X; to[1] = p.X.Y; to[2] = p.X.Z; to[3] = p.X.W;
        to[4] = p.Y.X; to[5] = p.Y.Y; to[6] = p.Y.Z; to[7] = p.Y.W;
        to[8] = p.Z.X; to[9] = p.Z.Y; to[10] = p.Z.Z; to[11] = p.Z.W;
        to[12] = p.W.X; to[13] = p.W.Y; to[14] = p.W.Z; to[15] = p.W.W;
    }

    private const string CopyKernel = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(rgba16f, set = 0, binding = 0) uniform restrict readonly image2D color_in;
        layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D color_copy;
        layout(push_constant, std430) uniform P { ivec4 rect; } pc;
        void main() {
            ivec2 p = ivec2(gl_GlobalInvocationID.xy) + pc.rect.xy;
            if (p.x >= pc.rect.z || p.y >= pc.rect.w) return;
            imageStore(color_copy, p, imageLoad(color_in, p));
        }
        """;

    private const string Kernel = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(rgba16f, set = 0, binding = 0) uniform restrict writeonly image2D color_out;
        layout(set = 0, binding = 1) uniform sampler2D src;
        layout(set = 0, binding = 2) uniform sampler2D depth_tex;
        layout(set = 0, binding = 4) uniform sampler2D road_mask;
        layout(set = 0, binding = 3, std140) uniform U {
            mat4 invproj; mat4 invview; mat4 viewproj;
            vec4 rect;    // the pixels to bend: x0, y0, x1, y1
            vec4 size;    // w, h, 1/w, 1/h
            vec4 misc;    // clock (s), sources, road, mirage
            vec4 misc2;   // reversed z (+1), focal length (px), the road's near edge (m), the most a nudge moves (px)
            vec4 cam_right;
            vec4 tune;    // -, the sine of the mirage's band, the fires' full and gone (m): HeatTune
            vec4 band;    // the ripple's band over and under level (degrees), its nudge at full (radians), its least (px): HeatBand
            vec4 cells;   // the ripple's cells: the most per radian, the fewest pixels each; the mirage's most per radian: HeatBand
            vec4 road;    // the paved mask's corner from the floating origin (game X, Z), its size (m), by the ground (1): HeatRoadMask
            vec4 shares;  // the ripple's share on paved road and on open ground, the mirage's off the road: HeatBand
            vec4 skycol;  // the mission's sky (linear), when the mirrored direction meets no sky on the screen
            vec4 src_pos[16];   // foot xyz, radius
            vec4 src_par[16];   // flames' top, column height, strength, seed
        } u;

        float hash3(ivec3 p) {
            uvec3 v = uvec3(p) * 1664525u + 1013904223u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            v ^= v >> 16u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            return float(v.x & 0xffffffu) * (1.0 / 16777215.0);
        }
        float vnoise(vec3 p) {
            ivec3 i = ivec3(floor(p)); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
            float a = mix(hash3(i), hash3(i + ivec3(1, 0, 0)), f.x), b = mix(hash3(i + ivec3(0, 1, 0)), hash3(i + ivec3(1, 1, 0)), f.x);
            float c = mix(hash3(i + ivec3(0, 0, 1)), hash3(i + ivec3(1, 0, 1)), f.x), d = mix(hash3(i + ivec3(0, 1, 1)), hash3(i + ivec3(1, 1, 1)), f.x);
            return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
        }
        vec3 view_at(vec2 uv, float d) {
            vec4 v = u.invproj * vec4(uv * 2.0 - 1.0, d, 1.0);
            return v.xyz / v.w;
        }
        bool is_sky(float d) { return u.misc2.x > 0.0 ? d <= 0.0 : d >= 1.0; }
        vec3 view_px(ivec2 q) { return view_at((vec2(q) + 0.5) * u.size.zw, texelFetch(depth_tex, q, 0).r); }
        float dist_at(ivec2 p) {
            p = clamp(p, ivec2(0), ivec2(u.size.xy) - 1);
            float d = texelFetch(depth_tex, p, 0).r;
            return is_sky(d) ? 1e7 : length(view_at((vec2(p) + 0.5) * u.size.zw, d));
        }

        // HeatBand.Distance and Angle
        float band_dist(float d, float near) { return smoothstep(near, near * 2.6, d) * (1.0 + 0.6 * smoothstep(400.0, 2000.0, d)); }
        float band_angle(float e) { return e >= 0.0 ? 1.0 - smoothstep(0.2 * u.band.x, u.band.x, e) : 1.0 - smoothstep(0.2 * u.band.y, u.band.y, -e); }

        void main() {
            ivec2 p = ivec2(gl_GlobalInvocationID.xy) + ivec2(u.rect.xy);
            if (p.x >= int(u.rect.z) || p.y >= int(u.rect.w)) return;
            vec2 uv = (vec2(p) + 0.5) * u.size.zw;
            float dz = texelFetch(depth_tex, p, 0).r;
            bool sky = is_sky(dz);
            vec3 vpos = view_at(uv, dz);
            float dist = sky ? 1e7 : length(vpos);
            vec3 dirW = normalize(mat3(u.invview) * normalize(view_at(uv, 0.5)));
            vec3 cam = u.invview[3].xyz;
            float clock = u.misc.x;
            vec2 off = vec2(0.0);   // radians: x along the view's right, y up
            float t_near = 1e9;
            float env = 0.0;   // the most this pixel's nudge can reach (radians), for I76_HEAT_DEBUG=3

            // fires: the columns this pixel looks through in front of the scene
            int n = int(u.misc.y);
            for (int i = 0; i < n; i++) {
                vec4 a = u.src_pos[i], b = u.src_par[i];
                vec2 o = cam.xz - a.xz, d2 = dirW.xz;
                float dd = dot(d2, d2);
                float t = dd > 1e-8 ? max(-dot(o, d2) / dd, 0.0) : 0.0;
                if (t >= dist || t < 1.0) continue;
                vec3 q = cam + dirW * t;
                float y = q.y - a.y, H = b.y, top = b.x;
                if (y <= 0.0 || y >= H) continue;
                float rad = a.w * (1.0 + 0.8 * y / H);
                float m = 1.0 - smoothstep(0.3 * rad, rad, length(q.xz - a.xz));
                // up to half the column at full (the first version 0.4), out to 60 m at full and gone at 260 (HeatTune.FireFull, FireFar)
                m *= smoothstep(top * 0.35, top * 1.05, y) * (1.0 - smoothstep(H * 0.5, H, y));
                m *= b.z * (1.0 - smoothstep(u.tune.z, u.tune.w, t)) * smoothstep(1.5, 4.0, t);
                if (m <= 0.002) continue;
                float s = dot(q - a.xyz, u.cam_right.xyz);
                vec3 nc = vec3(s * 2.6, (y - clock * 2.4) * 2.0, clock * 1.1 + b.w * 37.0);
                vec2 w = vec2(vnoise(nc), vnoise(nc + vec3(19.7, 7.3, 3.1))) * 2.0 - 1.0;
                w += 0.5 * (vec2(vnoise(nc * 2.3 + 5.0), vnoise(nc * 2.3 + 11.0)) * 2.0 - 1.0);
                off += w * m * 0.0048;
                env += m * 0.0048 * 1.5;
                t_near = min(t_near, t);
            }

            // a hot day: the air low over the ground bends what is seen through it from 75 m out (the far road and the ground beside it:
            // rays within 1.7 degrees of level; HeatBand), and level ground seen at a grazing angle shows the mirage
            float road = u.misc.z, mir = u.misc.w, mirage = 0.0;
            vec3 mcol = vec3(0.0);
            if (road > 0.0 || mir > 0.0) {
                bool only = u.road.w > 0.5;   // by the ground (HeatBand.Surface), else IJ's band round level
                // the surface: its normal from the depths two texels either side (steadier far away than the next texel's), facing up
                vec3 nW = vec3(0.0, 1.0, 0.0);
                float sin_inc = 1.0, paved = only ? 0.0 : 1.0;
                if (!sky && dist > u.misc2.z * 0.8) {
                    ivec2 hi = ivec2(u.size.xy) - 1;
                    vec3 vl = view_px(clamp(p - ivec2(2, 0), ivec2(0), hi)), vr = view_px(clamp(p + ivec2(2, 0), ivec2(0), hi));
                    vec3 vd = view_px(clamp(p - ivec2(0, 2), ivec2(0), hi)), vu = view_px(clamp(p + ivec2(0, 2), ivec2(0), hi));
                    nW = normalize(mat3(u.invview) * cross(vr - vl, vu - vd));
                    if (nW.y < 0.0) nW = -nW;
                    sin_inc = abs(dot(nW, dirW));
                    if (only) {
                        // paved road there (HeatRoadMask, game frame: Godot's X, minus its Z, plus the floating origin)
                        vec3 wp = cam + dirW * dist;
                        vec2 muv = (vec2(wp.x, -wp.z) + u.road.xy) / u.road.z;
                        paved = all(greaterThan(muv, vec2(0.0))) && all(lessThan(muv, vec2(1.0))) ? textureLod(road_mask, muv, 0.0).r : 0.0;
                    }
                }
                float band;
                if (only) {
                    // HeatBand.Surface: by the angle between the ray and the ground's surface, and the surface (paved, open, rough)
                    float graze = degrees(asin(clamp(sin_inc, 0.0, 1.0)));
                    float share = u.shares.x * paved + u.shares.y * smoothstep(0.9, 0.99, nW.y) * (1.0 - paved);
                    band = (1.0 - smoothstep(0.2 * u.band.y, u.band.y, graze)) * smoothstep(0.85, 0.95, nW.y) * share * band_dist(dist, u.misc2.z);
                    if (u.cam_right.w == 2.0) {
                        if (!sky) imageStore(color_out, p, vec4(1.0 - smoothstep(0.2 * u.band.y, u.band.y, graze), paved, share * 0.8 * step(u.misc2.z * 0.8, dist), 1.0));
                        return;
                    }
                } else band = band_angle(degrees(asin(clamp(dirW.y, -1.0, 1.0)))) * band_dist(dist, u.misc2.z);
                if (u.cam_right.w == 2.0) band = 0.0;
                if (band > 0.0 && road > 0.0) {
                    // cells taller than wide, rising and drifting: their size an angle (about 0.2 by 1 degree), never under three pixels
                    float kv = min(u.cells.x, u.misc2.y / u.cells.y), kh = kv * 0.2;
                    vec3 a = vec3(dirW.x * kh + clock * 0.25, dirW.y * kv - clock * 2.2, dirW.z * kh + clock * 0.15);
                    float r1 = vnoise(a) * 2.0 - 1.0;
                    float r2 = vnoise(a * vec3(2.3, 2.1, 2.3) + vec3(3.7, -clock * 1.7, 1.9)) * 2.0 - 1.0;
                    float amp = max(u.band.z, u.band.w / u.misc2.y);
                    off += vec2(0.45 * r2, r1 + 0.5 * r2) * band * road * amp;
                    env += 1.5 * band * road * amp;
                    t_near = min(t_near, u.misc2.z * 0.8);
                }
                if (mir > 0.0 && !sky && dist > u.misc2.z) {
                    // level ground at a grazing angle (on paved road only, road only)
                    float level = smoothstep(0.95, 0.995, abs(nW.y));
                    float mw = (1.0 - smoothstep(0.3 * u.tune.y, u.tune.y, sin_inc)) * level * smoothstep(u.misc2.z, u.misc2.z * 2.0, dist) * (only ? mix(u.shares.z, 1.0, paved) : 1.0);
                    if (u.cam_right.w == 2.0) { imageStore(color_out, p, vec4(1.0 - smoothstep(0.012, 0.04, sin_inc), level, smoothstep(u.misc2.z, u.misc2.z * 2.0, dist), 1.0)); return; }
                    if (mw > 0.0) {
                        // the ray bent back up off the hot layer: what lies at the mirrored angle, if it is farther than the ground
                        // a real road mirage is the sky: at the mirrored direction, or, where a mesa or the land stands there, the sky
                        // straight above it on the screen (a few steps up), else the mission's sky
                        vec4 c = u.viewproj * vec4(dirW.x, -dirW.y, dirW.z, 0.0);
                        if (c.w > 0.0) {
                            vec2 muv = clamp(c.xy / c.w * 0.5 + 0.5, vec2(0.0), vec2(1.0));
                            mcol = u.skycol.rgb;
                            for (int k = 0; k < 10; k++) {
                                vec2 q = vec2(muv.x, muv.y - float(k * k) * 0.006);
                                if (q.y <= 0.0) break;
                                if (is_sky(texelFetch(depth_tex, clamp(ivec2(q * u.size.xy), ivec2(0), ivec2(u.size.xy) - 1), 0).r)) { mcol = texture(src, q).rgb; break; }
                            }
                            float kv = min(u.cells.z, u.misc2.y / 3.0);
                            float fl = vnoise(vec3(dirW.x * kv * 0.3 + clock * 0.3, dirW.y * kv - clock * 2.5, dirW.z * kv * 0.3 + 9.1));
                            // never quite a mirror: it wavers at any strength
                            mirage = clamp(mw * mir * (0.4 + 0.4 * fl), 0.0, 0.6 + 0.35 * fl);
                        }
                    }
                }
            }
            if (off == vec2(0.0) && mirage <= 0.0) { if (u.cam_right.w == 4.0) imageStore(color_out, p, vec4(0.0, 0.0, 0.0, 1.0)); return; }
            vec2 opx = vec2(off.x, -off.y) * u.misc2.y;
            float len = length(opx);
            if (len > u.misc2.w) opx *= u.misc2.w / len;
            vec2 suv = uv + opx * u.size.zw;
            // never pull in what is nearer than the heat bending it (the cockpit, a car in front)
            if (dist_at(ivec2(suv * u.size.xy)) < t_near) suv = uv;
            vec4 col = texture(src, suv);
            col.rgb = mix(col.rgb, mcol, mirage);
            if (u.cam_right.w == 4.0) col.rgb = clamp(abs(col.rgb - texture(src, uv).rgb) * 8.0, 0.0, 1.0);
            else if (u.cam_right.w == 3.0) col.rgb = mix(col.rgb, vec3(clamp(env * u.misc2.y / 6.0, 0.0, 1.0), mirage, 0.0), clamp(env * u.misc2.y / 6.0 + mirage * 2.0, 0.0, 0.85));
            else if (u.cam_right.w > 0.0) col.rgb = mix(col.rgb, vec3(clamp(len / 4.0, 0.0, 1.0), mirage, 0.0), clamp(len * 2.0 + mirage * 4.0, 0.0, 0.85));
            imageStore(color_out, p, col);
        }
        """;
}
