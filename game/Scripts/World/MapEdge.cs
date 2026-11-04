using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// Graphic Detail's Map Edge (the port's own, both looks). A level's boundary is its heightfield's own outer tiles:
/// most missions are bowls whose rim rises 100-170 m to a cliff band along the last tiles, and the modern look's ring
/// (HorizonTerrain) carries on from the rim's height. Off (the original's) draws that as it is. On draws the walls,
/// and the plateau behind them, down at the plain of their foot (EdgeWalls: the ground no car reaches that is joined to the
/// map's edge and lies behind a cliff, the slopes below the cliffs kept; the terrain's vertex shader holds it to the foot grid, the ring beyond starts from the plain), so the ground
/// runs on level to the ring's ranges (On is marked as the port's). The 1997 look's terrain is held down the same way, lit by the
/// original's light through the mission's ramp as level ground; it has no ring, so past the walls' plain the sky's horizon
/// panels stand where they stand at Off. Drawing only (map_edge.gdshaderinc): the heightfield, its collision and the
/// simulation are unchanged, and the playable interior away from the edge is untouched.
/// </summary>
public static class MapEdge
{
    public const int Off = 0, On = 1;
    public static readonly string[] Names = ["Off", "On" + I76.Game.Shell.ShellSettings.PortMark];

    /// <summary>
    /// shell.cfg's [port] map_edge: "on" or "off"; a number is the four-value row's (0 Plain, 1 Haze, 2 Fade Out, 3 Hide), whose
    /// Hide is On and the rest Off. Absent or anything else, Off.
    /// </summary>
    public static int Parse(object? value) => value switch
    {
        string s => s == "on" ? On : Off,
        int n => n == 3 ? On : Off,
        _ => Off,
    };

    /// <summary>The value shell.cfg keeps.</summary>
    public static string Key(int value) => value == On ? "on" : "off";

    // On's walls per mission, made once for the terrain's chunks and the ring (either may ask first, on its own thread)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MissionWorld, Lazy<EdgeWalls?>> _walls = new();

    /// <summary>
    /// On's walls of a mission (<see cref="EdgeWalls"/>) from the surface the modern look draws, made on the first call and
    /// kept with the world; null for a map too big for one surface (it has no ring either). I76_MAP_EDGE_RIDGES=0 hides the
    /// ridges between drivable regions as well, I76_MAP_EDGE_THIN=0 keeps the thin barriers among them, and I76_MAP_EDGE_ROADS=0
    /// hides the thin barriers with a road at their foot too, for comparison; I76_MAP_EDGE_KEEP=0 hides ground the mission puts
    /// something on as well, I76_MAP_EDGE_ROADSIDE=0 the ground beside the roads, and I76_MAP_EDGE_SLOPES=0 the slopes under the cliffs.
    /// </summary>
    public static EdgeWalls? WallsFor(MissionWorld world, Func<ITerrainSurface> surface) =>
        _walls.GetValue(world, w => new Lazy<EdgeWalls?>(() => w.Terrain.Huge ? null : EdgeWalls.Build(w.Terrain, surface(), keepRidges: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_RIDGES") != "0", thinBarriers: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_THIN") != "0",
            roads: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_ROADS") == "0" ? null : EdgeWalls.Roads(w),
            keep: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_KEEP") == "0" ? null : EdgeWalls.Keep(w),
            roadside: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_ROADSIDE") != "0",
            slopes: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_SLOPES") != "0"))).Value;

    /// <summary>On's foot grid of <paramref name="walls"/> into the shader globals i76_edge_foot and i76_edge_foot_rect (main thread).</summary>
    public static void Upload(EdgeWalls walls)
    {
        // the plain's height and the hidden flag a sample, texel centres on the samples
        var img = Godot.Image.CreateFromData(walls.W, walls.H, false, Godot.Image.Format.Rgf, System.Runtime.InteropServices.MemoryMarshal.AsBytes(walls.Texels().AsSpan()).ToArray());
        Godot.RenderingServer.GlobalShaderParameterSet("i76_edge_foot", Godot.ImageTexture.CreateFromImage(img));
        float ss = TerrainNode.SampleSize;
        Godot.RenderingServer.GlobalShaderParameterSet("i76_edge_foot_rect", new Godot.Vector4(walls.X0 * ss - ss / 2, walls.Z0 * ss - ss / 2, walls.W * ss, walls.H * ss));
        MissionBuilder.Trace($"map edge: {walls.HiddenCount} hidden samples of {walls.W}x{walls.H}");
    }
}
