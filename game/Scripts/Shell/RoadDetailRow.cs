namespace I76.Game.Shell;

/// <summary>
/// Road Detail ([port] road_detail, Graphic Detail > World, the modern look's): On gives the paved roads a surface of their own near the
/// camera, made in the terrain shader from the baked noise (World/RoadDetail, terrain_modern.gdshaderinc road_detail()): aggregate grain and
/// stones with their relief, cracks, sealed patches, the light bands' and yellow dashes' edges steepened and chipped, broad variation down a
/// straight, the dirt verge blended into the ground. Off (the default) draws the road layer's colour as before. The 1997 look is unchanged.
/// </summary>
public static class RoadDetailRow
{
    public static readonly string[] Names = ["Off", "On" + ShellSettings.PortMark];
    public const string Label = "Road Detail";
    public static int Index(ShellSettings s) => s.RoadDetail ? 1 : 0;
    public static void Put(ShellSettings s, int v) => s.RoadDetail = v != 0;
    public static void Step(ShellSettings s) => s.RoadDetail = !s.RoadDetail;
}
