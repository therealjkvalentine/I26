namespace I76.Game.Shell;

/// <summary>
/// Road Detail ([port] road_detail, and road_detail_strong for Strong; Graphic Detail > World, the modern look's): On gives the paved roads a
/// surface of their own, made in the terrain shader from the baked noise (World/RoadDetail, terrain_modern.gdshaderinc road_detail()):
/// aggregate grain and stones with their relief, cracks, sealed patches, the markings' edges steepened and chipped, broad variation down a
/// straight, the dirt verge blended into the ground, soft wheel-track and oil bands along the lanes and sealant lines, and at night the
/// markings sending the headlamps' light back. Strong is the same with larger, rougher aggregate close up. Off (the default) draws the
/// road layer's colour as before. The 1997 look is unchanged.
/// </summary>
public static class RoadDetailRow
{
    public const int Off = 0, On = 1, Strong = 2;
    public static readonly string[] Names = ["Off", "On" + ShellSettings.PortMark, "Strong" + ShellSettings.PortMark];
    public const string Label = "Road Detail";
    public static int Index(ShellSettings s) => !s.RoadDetail ? Off : s.RoadDetailStrong ? Strong : On;
    public static void Put(ShellSettings s, int v) { s.RoadDetail = v != Off; s.RoadDetailStrong = v == Strong; }
    public static void Step(ShellSettings s) => Put(s, (Index(s) + 1) % Names.Length);
}
