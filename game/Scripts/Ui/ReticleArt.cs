using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The cockpit's gunsight ring drawn as vector geometry at the window's resolution (the modern look; shell.cfg [port] crosshair_drawn,
/// on by default), in place of the sprite the game ships (zretc_1, 32x32, and its fine twin zretc_6, 128x128, which the binoculars show).
/// The design is measured from the sprites' texels: a ring from 0.852 to 1.0 of the half tile (the outer edge is the tile's edge) with a
/// tick at each of the four compass points, reaching in to 0.6875 (the 32x32's, two texels wide) or 0.734 (the 128x128's, three texels
/// wide) of the half tile; one near-white colour (texel value 249 of 255), no other state: the original's ring has no locked, in-range or
/// weapon colour, and does not animate. The shape is in half-tile units, y down, centred on the tile.
/// </summary>
public static class ReticleArt
{
    /// <summary>[port] crosshair_drawn: the modern look draws the ring (the 1997 look keeps the sprite either way).</summary>
    public static bool Drawn = true;

    /// <summary>
    /// A ring and its four ticks, in half-tile units: the ring between RingIn and RingOut, the ticks of half-width TickHalf from TickIn out to the ring.
    /// Shade is the ring's grey (0..255) at each of its equal angular steps from the +x axis towards +y (y down), TickGrey the ticks', and Middle how
    /// much of the ring's middle third shows through (1 is flat, less is the tube's darker core).
    /// </summary>
    public sealed record Shape(float RingIn, float RingOut, float TickHalf, float TickIn, byte[] Shade, byte TickGrey, float Middle)
    {
        /// <summary>True when the point (half-tile units from the centre, y down) lies on the ring or a tick.</summary>
        public bool Covers(float x, float y)
        {
            float r = MathF.Sqrt(x * x + y * y);
            if (r >= RingIn && r <= RingOut) return true;
            return r >= TickIn && r < RingIn && (MathF.Abs(x) <= TickHalf || MathF.Abs(y) <= TickHalf);
        }

        /// <summary>The ring's grey at <paramref name="angle"/> (radians from +x towards +y), 0..1, interpolated between the steps' centres.</summary>
        public float GreyAt(float angle)
        {
            int n = Shade.Length;
            float t = angle / Mathf.Tau * n - 0.5f;
            t -= MathF.Floor(t / n) * n;
            int i = (int)t % n, j = (i + 1) % n;
            float f = t - MathF.Floor(t);
            return (Shade[i] * (1f - f) + Shade[j] * f) / 255f;
        }
    }

    /// <summary>zretc_1 (the hood's ring and the 2D dash's), measured at 32x32 against its texels: 48 angular steps.</summary>
    public static readonly Shape Coarse = new(0.852f, 1f, 1f / 16f, 11f / 16f, [74, 72, 66, 62, 58, 53, 46, 40, 40, 47, 71, 89, 90, 98, 108, 118, 143, 172, 179, 154, 126, 112, 97, 82, 78, 84, 90, 91, 87, 85, 85, 82, 81, 76, 70, 70, 82, 98, 112, 125, 145, 168, 170, 146, 124, 112, 93, 77], 66, 1f);
    /// <summary>zretc_6 (the binoculars' ring), measured at 128x128 against its texels: 96 angular steps.</summary>
    public static readonly Shape Fine = new(0.852f, 1f, 1.5f / 64f, 47f / 64f, [82, 84, 84, 83, 84, 83, 80, 76, 73, 71, 71, 69, 64, 57, 54, 56, 56, 55, 55, 59, 72, 88, 97, 97, 92, 88, 96, 110, 122, 129, 134, 148, 171, 187, 193, 194, 192, 193, 193, 185, 167, 149, 138, 127, 116, 104, 90, 83, 82, 87, 95, 101, 105, 109, 112, 113, 110, 105, 102, 101, 102, 104, 105, 105, 104, 102, 97, 88, 84, 82, 80, 81, 88, 99, 111, 121, 131, 140, 153, 168, 181, 187, 183, 178, 179, 183, 188, 181, 166, 152, 140, 128, 114, 99, 88, 83], 120, 0.8f);

    private static Color Grey(float g) => new(g, g, g);

    /// <summary>
    /// Draws <paramref name="s"/> on <paramref name="ci"/> where <paramref name="map"/> carries the half-tile units (the tile's centre at 0, its edge at 1)
    /// to canvas pixels: the ring a smooth antialiased polyline shaded round its angle, the ticks lines. <paramref name="map"/> may be a perspective projection.
    /// </summary>
    public static void Draw(CanvasItem ci, Shape s, Func<Vector2, Vector2?> map)
    {
        var c = map(Vector2.Zero); var ex = map(Vector2.Right); var ey = map(Vector2.Down);
        if (c is not { } c0 || ex is not { } x0 || ey is not { } y0) return;
        float px = (x0 - c0).Length(), py = (y0 - c0).Length();
        float unit = (px + py) * 0.5f;   // screen pixels to a half tile
        if (unit < 2f) return;
        float mid = (s.RingIn + s.RingOut) * 0.5f, band = s.RingOut - s.RingIn;
        int n = Math.Clamp((int)(unit * 1.2f), 48, 360);
        var pts = new Vector2[n + 1];
        var cols = new Color[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Tau * (i % n) / n;
            if (map(new Vector2(MathF.Cos(a), MathF.Sin(a)) * mid) is not { } p) return;
            pts[i] = p;
            cols[i] = Grey(s.GreyAt(a));
        }
        ci.DrawPolylineColors(pts, cols, band * unit, true);
        if (s.Middle < 1f)
        {
            // the tube's darker core: the middle third drawn again, shaded
            var core = new Color(0f, 0f, 0f, 1f - s.Middle);
            ci.DrawPolyline(pts, core, band * unit / 3f, true);
        }
        var tick = Grey(s.TickGrey / 255f);
        for (int k = 0; k < 4; k++)
        {
            var d = new Vector2(k == 0 ? 1 : k == 2 ? -1 : 0, k == 1 ? 1 : k == 3 ? -1 : 0);
            if (map(d * s.TickIn) is { } a && map(d * mid) is { } b) ci.DrawLine(a, b, tick, 2f * s.TickHalf * unit, true);
        }
    }

    /// <summary>The ring as the 2D dash draws the sprite: the 32x32 tile at <paramref name="topLeft"/>, <paramref name="scale"/> pixels a texel.</summary>
    public static void DrawFlat(CanvasItem ci, Vector2 topLeft, float scale)
        => Draw(ci, Coarse, u => topLeft + (u + Vector2.One) * 16f * scale);
}
