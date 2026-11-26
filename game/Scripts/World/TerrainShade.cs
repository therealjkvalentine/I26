using Godot;
using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// The modern look's baked terrain shadows (docs/deviations.md "Terrain shadows"): the sun's margin over the terrain's horizon per
/// texel of the map and the sky's share (TerrainShadeBake), made on a worker from the drawn ground and read by the terrain shader
/// (terrain_modern.gdshaderinc) as the globals i76_terrain_shade (R8 margin, mipmapped), i76_terrain_sky (R8 share), i76_terrain_shade_rect
/// (their corner in the game frame and 1 / their size), i76_terrain_shade_on (x strength, 0 off; y the shadow map's reach; z the sky
/// term's strength; w a debug view) and i76_terrain_shade_edge (x the margin's range, y the sun's radius, z the texel in metres, w how
/// the shadow map's own terrain shadow is handed over: <see cref="Combine"/>).
///
/// The bake owns the terrain's shadows on the terrain at every distance; the shadow map keeps the cars', buildings' and plants' shadows
/// on it (and the terrain's on them). Day presets only: by night the original's own ground light carries the land (terrain_night.gdshaderinc)
/// and the moon is a tenth of the sun. Graphic Detail's Terrain Shadows row (TerrainShadowsRow; I76_TERRAIN_SHADE=0/1 over it) turns it on;
/// the textures are a 1x1 stand-in until a bake replaces their data (RenderingServer.TextureReplace keeps the RID the materials hold,
/// so turning it on mid-mission bakes then, and no material is made again).
/// </summary>
public static class TerrainShade
{
    private static readonly StringName ShadeName = "i76_terrain_shade", SkyName = "i76_terrain_sky", RectName = "i76_terrain_shade_rect",
        OnName = "i76_terrain_shade_on", EdgeName = "i76_terrain_shade_edge", BandName = "i76_terrain_shade_band";
    /// <summary>The sky term's strength: 1 the whole cos² horizon visibility on the sky's light.</summary>
    public const float SkyStrength = 1f;

    /// <summary>
    /// How the bake and the shadow map share the terrain's own shadow. Band (the default): the terrain stays a caster (the cars and buildings in a
    /// hill's shadow are shaded by the map), and on the terrain the map's darkening is let through only away from the bake's shadow edges
    /// (there it is the map's blurred or stepped copy of the same shadow): the bake's edge alone shows. Fade: the earlier bake's hand-over,
    /// the bake only from 0.4 to 0.8 of the map's reach. NoCast: the terrain cast no shadow into the map (cheaper; the cars in a hill's
    /// shadow are lit). I76_TERRAIN_SHADE_MODE=band|fade|nocast.
    /// </summary>
    public enum Combine { Band, Fade, NoCast }

    private static readonly string? Hook = System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE");
    /// <summary>I76_TERRAIN_SHADE_DEBUG=sun|sky|band: the ground drawn grey by that term alone (band: white where the shadow map is let through), to see the bake.</summary>
    private static readonly float DebugView = System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_DEBUG") switch { "sun" => 1, "sky" => 2, "band" => 3, _ => 0 };
    public static readonly Combine Mode = System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_MODE") switch { "fade" => Combine.Fade, "nocast" => Combine.NoCast, _ => Combine.Band };
    /// <summary>I76_TERRAIN_SHADE_TEXEL=metres (5, 2.5, 1.25): the sun's grid over the row's.</summary>
    private static readonly float? TexelHook = float.TryParse(System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_TEXEL"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t) ? t : null;
    /// <summary>
    /// The band round the bake's shadow edges where the shadow map is not let through on the terrain, metres: <see cref="BandBase"/> plus
    /// <see cref="BandPerMetre"/> of the distance, over the sine of the sun's elevation (a low sun stretches the map's texels along the
    /// ground). I76_TERRAIN_SHADE_BAND=base,per-metre.
    /// </summary>
    public static readonly (float Base, float PerMetre) Band = ParseBand(System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_BAND")) ?? (0.5f, 0.004f);

    /// <summary>I76_TERRAIN_SHADE_PENUMBRA=k: the penumbra's width times k (1, the sun's disc; under 1 sharper than a real shadow).</summary>
    public static readonly float Penumbra = float.TryParse(System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_PENUMBRA"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pk) ? Math.Max(pk, 0.01f) : 1f;

    private static ImageTexture? _sunSlot, _skySlot;
    private static System.Threading.Tasks.Task<(Image Sun, Image Sky, TerrainShadeBake.Placement P, string Note)>? _job;
    private static bool _ready, _wanted, _look1997;
    private static float _reach, _strength = -1, _texel, _bakedTexel, _elevSin = 1;
    private static int _generation;
    private static MissionWorld? _world;
    private static LightPreset? _light;
    private static Func<ITerrainSurface?>? _surface;

    /// <summary>The last bake's time on its worker, milliseconds (0 none).</summary>
    public static long BakeMs { get; private set; }

    /// <summary>The sun's texels the row asks for, metres (the hook over it); 0 off.</summary>
    public static float TexelFor(int row) => Hook == "0" ? 0 : TexelHook ?? (row > 0 ? I76.Game.Shell.TerrainShadowsRow.Texel(row) : Hook == "1" ? TerrainShadeBake.DefaultTexel : 0);

    /// <summary>The terrain casts into the shadow map: always but with <see cref="Combine.NoCast"/> while the bake shows.</summary>
    public static bool TerrainCasts => !(Mode == Combine.NoCast && _ready && _wanted && !_look1997 && _strength > 0);

    /// <summary>
    /// On the build's thread, before the terrain's materials are made: the textures' stand-ins on the globals (once), and the mission kept
    /// for a bake. Nothing by night, on a map with no tiles or without <paramref name="bake"/> (the map editor: its ground changes under it).
    /// <paramref name="surface"/> gives the drawn surface on the bake's worker (it may wait for the terrain's preparation).
    /// </summary>
    public static void Start(MissionWorld world, LightPreset light, Func<ITerrainSurface?> surface, bool bake = true)
    {
        ++_generation;
        _job = null; _ready = false; _strength = -1; BakeMs = 0; _bakedTexel = 0;
        if (_sunSlot is null)
        {
            _sunSlot = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.R8));
            _skySlot = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.R8));
            RenderingServer.GlobalShaderParameterSet(ShadeName, _sunSlot);
            RenderingServer.GlobalShaderParameterSet(SkyName, _skySlot);
        }
        RenderingServer.GlobalShaderParameterSet(OnName, Vector4.Zero);
        bool ok = bake && light.IsDay && TerrainShadeBake.PlacementFor(world.Terrain) is { W: >= 2, D: >= 2 };
        _world = ok ? world : null; _light = ok ? light : null; _surface = ok ? surface : null;
        var d = light.Direction;
        _elevSin = MathF.Max(d.Y / d.Length(), 0.2f);
        Kick();
    }

    /// <summary>A bake for the texels wanted, if none is done or running for them.</summary>
    private static void Kick()
    {
        if (_world is not { } world || _light is not { } light || _surface is not { } surface || !_wanted || _look1997 || _texel <= 0) return;
        if (_bakedTexel == _texel && (_ready || _job is not null)) return;
        int gen = ++_generation;
        _bakedTexel = _texel;
        _ready = false;
        float texel = _texel;
        var dir = light.Direction;
        _job = System.Threading.Tasks.Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // a map too big for the drawn surface whole (BlockedSurface makes it in blocks as the camera nears them) takes the simulation's ground
            var surf = world.Terrain.Huge ? null : surface() ?? throw new InvalidOperationException("no surface");
            long tw = sw.ElapsedMilliseconds;
            var p = TerrainShadeBake.PlacementFor(world.Terrain, texel);
            var g = surf is not null ? TerrainShadeBake.HeightsFor(surf, p)
                : TerrainShadeBake.HeightsFor((fx, fz) => (float)world.BilinearHeight(fx / MissionWorld.SampleScale, fz / MissionWorld.SampleScale), p);
            long th = sw.ElapsedMilliseconds;
            var margin = TerrainShadeBake.Margin(g, dir);
            long ts = sw.ElapsedMilliseconds;
            int f = Math.Max(1, (int)MathF.Round(TerrainShadeBake.SkyTexel / p.Texel));
            var cg = TerrainShadeBake.Coarsen(g, f);
            var sky = TerrainShadeBake.Sky(cg);
            long tk = sw.ElapsedMilliseconds;
            var sunImg = Image.CreateFromData(p.W, p.D, false, Image.Format.R8, TerrainShadeBake.PackMargin(margin));
            sunImg.GenerateMipmaps();
            var skyImg = Image.CreateFromData(cg.W, cg.D, false, Image.Format.R8, TerrainShadeBake.Pack(sky));
            skyImg.GenerateMipmaps();
            long tm = sw.ElapsedMilliseconds;
            if (gen == _generation) BakeMs = tm - tw;
            long kib = ((long)p.W * p.D + (long)cg.W * cg.D) * 4 / 3 / 1024;
            float lit = 0;
            foreach (float m in margin) lit += TerrainShadeBake.Lit(m);
            return (sunImg, skyImg, p, $"terrain shade: {p.W} x {p.D} texels of {p.Texel:0.##} m, sky {cg.W} x {cg.D}; surface wait {tw} ms, heights {th - tw} ms, sun {ts - th} ms, sky {tk - ts} ms, textures {tm - tk} ms, bake {tm - tw} ms; sun elevation {MathF.Atan2(dir.Y, MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z)) * 180 / MathF.PI:0.#} deg, mean sun {lit / margin.Length:0.000}, mean sky {sky.Average():0.000}, {kib} KiB");
        });
        _jobGen = gen;
    }
    private static int _jobGen;

    /// <summary>Each frame on the main thread: the finished bake into the textures, then its strength eased in over half a second.</summary>
    public static void Poll(double delta)
    {
        if (_job is { IsCompleted: true } job)
        {
            _job = null;
            if (job.IsCompletedSuccessfully && _jobGen == _generation && _sunSlot is not null && _skySlot is not null)
            {
                var (sun, sky, p, note) = job.Result;
                RenderingServer.TextureReplace(_sunSlot.GetRid(), RenderingServer.Texture2DCreate(sun));
                RenderingServer.TextureReplace(_skySlot.GetRid(), RenderingServer.Texture2DCreate(sky));
                RenderingServer.GlobalShaderParameterSet(RectName, new Vector4(p.X0, p.Z0, 1f / p.SizeX, 1f / p.SizeZ));
                RenderingServer.GlobalShaderParameterSet(EdgeName, new Vector4(TerrainShadeBake.MarginRange, TerrainShadeBake.SunRadius * Penumbra, p.Texel, (int)Mode));
                RenderingServer.GlobalShaderParameterSet(BandName, new Vector4(Band.Base / _elevSin, Band.PerMetre / _elevSin, 0, 0));
                _ready = true;
                GD.Print(note);
            }
            else if (job.IsFaulted) GD.PrintErr($"terrain shade: {job.Exception?.GetBaseException().Message}");
        }
        float target = _ready && _wanted && !_look1997 ? 1f : 0f;
        float k = _strength < 0 ? target : Mathf.MoveToward(_strength, target, (float)delta * 2f);
        if (k == _strength) return;
        _strength = k;
        Push();
    }

    /// <summary>The settings and the look (Main's ApplyModernQuality and look switch): the row's texels (0 off); <paramref name="reach"/> the shadow map's, 0 with none.</summary>
    public static void Set(float texel, bool look1997, float reach)
    {
        bool snap = _look1997 != look1997;
        _texel = texel; _wanted = texel > 0; _look1997 = look1997; _reach = reach;
        // a look change shows at once (it is a picture swap anyway); the setting eases in Poll
        if (snap) _strength = _ready && _wanted && !_look1997 ? 1f : 0f;
        if (_wanted && _bakedTexel != _texel) { _ready = false; _strength = Math.Min(_strength, 0); }
        Kick();
        Push();
    }

    private static void Push() => RenderingServer.GlobalShaderParameterSet(OnName, new Vector4(Math.Max(_strength, 0), _reach, SkyStrength, DebugView));

    private static (float, float)? ParseBand(string? s)
    {
        if (s?.Split(',') is not [var a, var b]) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return float.TryParse(a, System.Globalization.NumberStyles.Float, ci, out float x) && float.TryParse(b, System.Globalization.NumberStyles.Float, ci, out float y) ? (x, y) : null;
    }
}
