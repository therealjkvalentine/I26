namespace I76.Sim.Views;

/// <summary>
/// The port's Field of View (Graphic Detail): the main 3D views' projection. The original's is 90 degrees across a 4:3 view, 73.7
/// degrees high; the port keeps that height (so a wide window sees more to the sides) and a choice widens or narrows it as if
/// the 4:3 view were 80 / 100 / 110 degrees across. The 2D dash, the radar, the mirror and the binoculars keep their own framing.
/// </summary>
public static class ViewFov
{
    /// <summary>Degrees across a 4:3 view, per choice; the first is the original's 90.</summary>
    public static readonly int[] Across = [90, 80, 100, 110];
    public static readonly string[] Names = ["Original", "80", "100", "110"];
    public const float OriginalVertical = 73.74f;

    /// <summary>The choice in force (view only; <see cref="Apply"/> sets it from the settings).</summary>
    public static int Choice { get; private set; }

    public static void Apply(int choice) => Choice = System.Math.Clamp(choice, 0, Across.Length - 1);

    /// <summary>The choice for a shell.cfg [port] fov value (degrees across 4:3); anything that is not one of the offered values is the original's.</summary>
    public static int Parse(int? across) => across is { } a && System.Array.IndexOf(Across, a) is var i and >= 0 ? i : 0;

    /// <summary>A vertical field of view (degrees) made for the original's 90 across 4:3, scaled to the choice: the tangent of the half angle times tan(across / 2).</summary>
    public static float Vertical(float originalVertical, int choice)
    {
        double k = System.Math.Tan(Across[System.Math.Clamp(choice, 0, Across.Length - 1)] * System.Math.PI / 360);
        double half = System.Math.Tan(originalVertical * System.Math.PI / 360) * k;
        return (float)(System.Math.Atan(half) * 360 / System.Math.PI);
    }

    /// <summary>The main views' vertical field of view in force.</summary>
    public static float Main => Choice == 0 ? OriginalVertical : Vertical(OriginalVertical, Choice);
}
