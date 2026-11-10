using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The target box's size rule, as the original's bracket drawer has it, shared by both looks. Everything is in the 640x480
/// frame's pixels: the window's height is 480 of them (k = height / 480 window pixels each), so a wider window has more of
/// them across, never bigger ones. The box is centred on the target's collider centre and sized
/// S = clamp(|trunc(R·sx/z)|, 23, 150) by the collider radius R over its depth z, sx the view's focal length in the frame's
/// pixels (320 for the original's 73.74° high view); it spans 2·trunc(0.6S) across and 2·trunc(0.4S) + 16 down (the
/// corner sprites are 8 high, the lower pair drawn one sprite lower), from trunc(0.4S)·(1 + 1/3) over the centre. So a car
/// is held at the 23-pixel floor (a 26x34 box) from about 35–40 m out and the box only grows near, to 180x136 at most.
/// </summary>
public static class TargetBox
{
    public const int MinSize = 23, MaxSize = 150;   // 0x17, 0x96
    /// <summary>The corner sprites' size (ztarg&lt;T&gt;01..04), in the frame's pixels.</summary>
    public const int Corner = 8;

    /// <summary>Window pixels to one frame pixel: the window's height over 480.</summary>
    public static float Scale(Vector2 window) => window.Y / 480f;

    /// <summary>
    /// The view's focal length in the frame's pixels, for a frame <paramref name="frameWidth"/> by <paramref name="frameHeight"/>
    /// of them and a field of view of <paramref name="fovDegrees"/>: high (Godot's KeepAspect Height, the main views), or across
    /// with <paramref name="keepWidth"/> (the mirror's camera). The main view's frame is 480 high: 320 at the original's 73.74°.
    /// </summary>
    public static float Focal(float frameWidth, float frameHeight, float fovDegrees, bool keepWidth)
    {
        float half = Mathf.DegToRad(fovDegrees) / 2;
        return (keepWidth ? frameWidth : frameHeight) / 2 / Mathf.Tan(half);
    }

    /// <summary>S: clamp(|trunc(R·f/z)|, 23, 150); a target at depth 0 takes the largest.</summary>
    public static int Size(float radius, float depth, float focal)
    {
        double raw = depth != 0 ? Math.Abs(Math.Truncate((double)radius * focal / depth)) : MaxSize;
        return (int)Math.Clamp(raw, MinSize, MaxSize);
    }

    /// <summary>The box round the projected centre (x, y) for size S, in the frame's pixels (the four corner sprites' outer edges).</summary>
    public static Rect2I Rect(int x, int y, int size)
    {
        int a = (int)(size * (double)0.6f), c = (int)(size * (double)0.4f);
        int y2 = y + (int)(c * (double)(-1f / 3f));
        return new Rect2I(x - a, y2 - c, 2 * a, 2 * c + 2 * Corner);
    }
}
