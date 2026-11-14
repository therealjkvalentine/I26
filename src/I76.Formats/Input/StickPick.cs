namespace I76.Formats.Input;

/// <summary>
/// Which axis of a pad's stick a push means, for picking it on the controller screen: past half its travel, the axis pushed further. A
/// push straight up is never quite straight, and an axis with nothing bound has no row of its own (a stick's two camera axes share one,
/// the X's), so the row alone could only ever give a stick's left / right.
/// </summary>
public static class StickPick
{
    /// <summary>How far from the middle a stick is pushed before it picks (as the joystick screen's axes and the menus' sticks).</summary>
    public const float Engage = 0.5f;

    /// <returns>Null with the stick near the middle; else whether it is the up / down axis (the larger of the two; up / down on a tie).</returns>
    public static bool? IsY(float x, float y) => x * x + y * y < Engage * Engage ? null : MathF.Abs(y) >= MathF.Abs(x);

    /// <summary>The pushed stick's axis as the controller screen names its inputs: LSX, LSY, RSX or RSY; null with neither stick pushed (the left one first).</summary>
    public static string? Pushed(PadRaw p) =>
        IsY(p.LeftX, p.LeftY) is { } l ? (l ? "LSY" : "LSX") : IsY(p.RightX, p.RightY) is { } r ? (r ? "RSY" : "RSX") : null;
}
