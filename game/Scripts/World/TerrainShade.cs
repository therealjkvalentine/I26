using Godot;
using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// The modern look's baked terrain shading (docs/deviations.md "Terrain shading"): the sun's share and the sky's per texel of the
/// map (TerrainShadeBake), made on a worker while the mission loads and read by the terrain shader (terrain_modern.gdshaderinc) as
/// the globals i76_terrain_shade (RG8, mipmapped), i76_terrain_shade_rect (its corner in the game frame and 1 / its size) and
/// i76_terrain_shade_on (x its strength, 0 off; y the shadow map's reach, past which the bake alone shades; z the sky term's
/// strength). Day presets only: by night the original's own ground light carries the land (terrain_night.gdshaderinc) and the moon
/// is a tenth of the sun, so neither term would show. With Shadow Detail Low or High and [port] terrain_shading (on unless
/// the player turned it off); I76_TERRAIN_SHADE=0/1 overrides both for a capture.
/// </summary>
public static class TerrainShade
{
    private static readonly StringName ShadeName = "i76_terrain_shade", RectName = "i76_terrain_shade_rect", OnName = "i76_terrain_shade_on";
    /// <summary>The sky term's strength: 1 the whole cos² horizon visibility on the sky's light.</summary>
    public const float SkyStrength = 1f;
    private static readonly string? Hook = System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE");
    /// <summary>I76_TERRAIN_SHADE_DEBUG=sun|sky: the ground drawn grey by that term alone, to see the bake.</summary>
    private static readonly float DebugView = System.Environment.GetEnvironmentVariable("I76_TERRAIN_SHADE_DEBUG") switch { "sun" => 1, "sky" => 2, _ => 0 };

    private static ImageTexture? _slot;
    private static System.Threading.Tasks.Task<(Image Img, string Note)>? _job;
    private static bool _ready, _wanted, _look1997;
    private static float _reach, _strength = -1;
    private static int _generation;

    /// <summary>The last bake's time on its worker, milliseconds (0 none).</summary>
    public static long BakeMs { get; private set; }

    /// <summary>Wanted by the settings: Shadow Detail Low or High with [port] terrain_shading on (the hook over both).</summary>
    public static bool Wanted(bool setting, int shadowDetail) => Hook switch { "0" => false, "1" => true, _ => setting && shadowDetail > 0 };

    /// <summary>
    /// On the build's thread, before the terrain's materials are made: the texture at its size set on the global, blank (a texture set
    /// later would have every chunk's material made again, as the sward's: Vegetation.ReserveSward), and the bake started on a worker.
    /// Nothing by night, on a map with no tiles or without <paramref name="bake"/> (the last mission's then put away).
    /// </summary>
    public static void Start(MissionWorld world, LightPreset light, bool bake = true)
    {
        int gen = ++_generation;
        _job = null; _ready = false; _strength = -1; BakeMs = 0;
        RenderingServer.GlobalShaderParameterSet(OnName, Vector4.Zero);
        if (!bake || !light.IsDay) return;
        var p = TerrainShadeBake.PlacementFor(world.Terrain);
        if (p.W < 2 || p.D < 2) return;
        if (_slot is null || _slot.GetWidth() != p.W || _slot.GetHeight() != p.D)
        {
            _slot = ImageTexture.CreateFromImage(Image.CreateEmpty(p.W, p.D, true, Image.Format.Rg8));
            RenderingServer.GlobalShaderParameterSet(ShadeName, _slot);
        }
        RenderingServer.GlobalShaderParameterSet(RectName, new Vector4(p.X0, p.Z0, 1f / p.SizeX, 1f / p.SizeZ));
        var dir = light.Direction;
        _job = System.Threading.Tasks.Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = TerrainShadeBake.HeightsFor(world, p);
            long th = sw.ElapsedMilliseconds;
            var sun = TerrainShadeBake.Sun(g, dir);
            long ts = sw.ElapsedMilliseconds;
            var sky = TerrainShadeBake.SkyCoarse(g, 2);
            long tk = sw.ElapsedMilliseconds;
            var img = Image.CreateFromData(p.W, p.D, false, Image.Format.Rg8, TerrainShadeBake.Pack(sun, sky));
            img.GenerateMipmaps();
            long tm = sw.ElapsedMilliseconds;
            if (gen == _generation) BakeMs = tm;
            float lit = sun.Average(), open = sky.Average();
            return (img, $"terrain shade: {p.W} x {p.D} texels of {p.SizeX / p.W:0.#} m, heights {th} ms, sun {ts - th} ms, sky {tk - ts} ms, mipmaps {tm - tk} ms, total {tm} ms; sun elevation {MathF.Atan2(dir.Y, MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z)) * 180 / MathF.PI:0.#} deg, mean sun {lit:0.000}, mean sky {open:0.000}, {p.W * p.D * 2 * 4 / 3 / 1024} KiB");
        });
    }

    /// <summary>Each frame on the main thread: the finished bake into the texture, then its strength eased in over half a second.</summary>
    public static void Poll(double delta)
    {
        if (_job is { IsCompleted: true } job)
        {
            _job = null;
            if (job.IsCompletedSuccessfully && _slot is not null)
            {
                var (img, note) = job.Result;
                _slot.Update(img);
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

    /// <summary>The settings and the look (Main's ApplyModernQuality and look switch); <paramref name="reach"/> the shadow map's, 0 with none.</summary>
    public static void Set(bool wanted, bool look1997, float reach)
    {
        bool snap = _look1997 != look1997;
        _wanted = wanted; _look1997 = look1997; _reach = reach;
        // a look change shows at once (it is a picture swap anyway); the setting eases in Poll
        if (snap) _strength = _ready && _wanted && !_look1997 ? 1f : 0f;
        Push();
    }

    private static void Push() => RenderingServer.GlobalShaderParameterSet(OnName, new Vector4(Math.Max(_strength, 0), _reach, SkyStrength, DebugView));
}
