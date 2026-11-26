namespace I76.Game.Shell;

/// <summary>
/// Smoke Linger ([port] smoke_haze, Graphic Detail > Effects, the modern look's): Normal (the default) ends each smoke puff with its
/// life, as before; Long lets the damaged cars' and the burning wrecks' smoke spread on after it as a thinning haze that drifts with the
/// wind and fades away (SmokeHaze). The original's puff lives 20 frames; the 1997 look is unchanged either way.
/// </summary>
public static class SmokeLingerRow
{
    public static readonly string[] Names = ["Normal", "Long" + ShellSettings.PortMark];
    public const string Label = "Smoke Linger";
    public static int Index(ShellSettings s) => s.SmokeHaze ? 1 : 0;
    public static void Put(ShellSettings s, int v) => s.SmokeHaze = v != 0;
    public static void Step(ShellSettings s) => s.SmokeHaze = !s.SmokeHaze;
}
