namespace I76.Sim.Views;

/// <summary>
/// The port's Field of View (Graphic Detail): the main 3D views' projection. The original draws 90 degrees across its 4:3
/// view, 73.7 high. The choices: Monitor (the default) keeps that height and takes the width from the window's shape, so
/// a 4:3 window is exactly the original's and a wider one sees more to the sides; a number is exact degrees across the
/// window whatever its shape (90 is the original's width, and in a wide window less height than the original's). The
/// cameras keep the height (Godot's KeepAspect Height), so every choice is a vertical angle here. The 2D dash, the
/// radar, the mirror and the binoculars keep their own framing.
/// </summary>
public static class ViewFov
{
    /// <summary>Degrees across the window per choice; 0 is Monitor (no fixed width), then 60 to 110 by 5 with the original's 90 among them (unmarked in <see cref="Names"/>: the pads mark every value the original does not have with *).</summary>
    public static readonly int[] Across = [0, 60, 65, 70, 75, 80, 85, 90, 95, 100, 105, 110];
    public static readonly string[] Names = ["Monitor*", "60*", "65*", "70*", "75*", "80*", "85*", "90", "95*", "100*", "105*", "110*"];
    public const int Monitor = 0;
    /// <summary>The original's: 90 across, which is choice 7.</summary>
    public static readonly int Original = System.Array.IndexOf(Across, 90);
    public const float OriginalVertical = 73.74f;
    public const float FourByThree = 4f / 3;

    /// <summary>The choice in force (view only; <see cref="Apply"/> sets it from the settings).</summary>
    public static int Choice { get; private set; }

    /// <summary>The window's width over its height, which the game layer keeps current; the original's 4:3 until it does.</summary>
    public static float Aspect { get; set; } = FourByThree;

    public static void Apply(int choice) => Choice = System.Math.Clamp(choice, 0, Across.Length - 1);

    /// <summary>The choice for a shell.cfg [port] fov_window value (degrees across the window); absent, 0 or anything not offered is Monitor.</summary>
    public static int Parse(int? across) => across is { } a && a != 0 && System.Array.IndexOf(Across, a) is var i and >= 0 ? i : Monitor;

    /// <summary>
    /// shell.cfg [port] fov as upstream writes it: degrees across a 4:3 view, the values its Field of View offers, 90 the
    /// original's (its height in any window, as Monitor). The port's own choice above is [port] fov_window.
    /// </summary>
    public static readonly int[] FourByThreeAcross = [90, 80, 100, 110];

    /// <summary>The vertical field of view (degrees) of <paramref name="across"/> degrees across a 4:3 view: the original's 73.74 at 90.</summary>
    public static float VerticalFourByThree(int across) => across == 90 ? OriginalVertical : Vertical(across, FourByThree);

    /// <summary>
    /// The choice for a [port] fov value in the 4:3 meaning: 90, absent or a value not offered is Monitor (the original's
    /// height); 80, 100 and 110 are the same degrees across the window, which is the same view in a 4:3 window.
    /// </summary>
    public static int ParseFourByThree(int? across) =>
        across is { } a && a != 90 && System.Array.IndexOf(FourByThreeAcross, a) >= 0 ? System.Array.IndexOf(Across, a) : Monitor;

    /// <summary>
    /// The [port] fov value in the 4:3 meaning for a choice in a window of that shape: the offered value whose height is
    /// nearest the choice's there (Monitor is 90).
    /// </summary>
    public static int ToFourByThree(int choice, float aspect)
    {
        float v = VerticalOf(choice, aspect);
        int best = FourByThreeAcross[0];
        foreach (int a in FourByThreeAcross)
            if (System.Math.Abs(VerticalFourByThree(a) - v) < System.Math.Abs(VerticalFourByThree(best) - v)) best = a;
        return best;
    }

    /// <summary>The vertical field of view (degrees) that puts <paramref name="across"/> degrees across a window of that shape.</summary>
    public static float Vertical(int across, float aspect) =>
        (float)(System.Math.Atan(System.Math.Tan(across * System.Math.PI / 360) / aspect) * 360 / System.Math.PI);

    /// <summary>The degrees across a window of that shape that the original's height gives (Monitor): 90 at 4:3.</summary>
    public static float MonitorAcross(float aspect) =>
        (float)(System.Math.Atan(System.Math.Tan(OriginalVertical * System.Math.PI / 360) * aspect) * 360 / System.Math.PI);

    /// <summary>The vertical field of view of a choice in a window of that shape.</summary>
    public static float VerticalOf(int choice, float aspect) =>
        System.Math.Clamp(choice, 0, Across.Length - 1) is var c && c == Monitor ? OriginalVertical : Vertical(Across[c], aspect);

    /// <summary>The main views' vertical field of view in force.</summary>
    public static float Main => VerticalOf(Choice, Aspect);

    /// <summary>
    /// A point of the 640x480 screen placed for the original's view (the 2D dash's gun reticles) where the same direction lands
    /// with the vertical field of view <paramref name="vertical"/> (degrees): its offset from the centre scaled by the ratio of the
    /// two focal lengths. The view keeps its height, so one scale serves both axes.
    /// </summary>
    public static (float X, float Y) ScreenPoint(float x, float y, float vertical)
    {
        double r = System.Math.Tan(OriginalVertical * System.Math.PI / 360) / System.Math.Tan(vertical * System.Math.PI / 360);
        return ((float)(320 + (x - 320) * r), (float)(240 + (y - 240) * r));
    }
}
