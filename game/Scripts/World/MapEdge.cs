using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// Graphic Detail's Map Edge (the port's own, modern look only). A level's boundary is its heightfield's own outer tiles:
/// most missions are bowls whose rim rises 100-170 m to a cliff band along the last tiles, and the modern look's ring
/// (HorizonTerrain) carries on from the rim's height. Off (the original's) draws that as it is. On draws the walls,
/// and the plateau behind them, down at the plain of their foot (EdgeWalls: the ground no car reaches that is joined to the
/// map's edge; the terrain's vertex shader holds it to the foot grid, the ring beyond starts from the plain), so the ground
/// runs on level to the ring's ranges (On is marked as the port's). Drawing only (map_edge.gdshaderinc): the heightfield, its collision and the
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
    /// ridges between drivable regions as well, for comparison.
    /// </summary>
    public static EdgeWalls? WallsFor(MissionWorld world, Func<ITerrainSurface> surface) =>
        _walls.GetValue(world, w => new Lazy<EdgeWalls?>(() => w.Terrain.Huge ? null : EdgeWalls.Build(w.Terrain, surface(), keepRidges: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_RIDGES") != "0"))).Value;
}
