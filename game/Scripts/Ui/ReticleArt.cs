using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The cockpit's gunsight ring drawn as vector geometry at the window's resolution (the modern look; shell.cfg [port] crosshair_drawn,
/// on by default), in place of the sprite the game ships (zretc_1, 32x32, and its fine twin zretc_6, 128x128, which the binoculars show).
/// The design is measured from the sprites' texels: a ring from 0.852 to 1.0 of the half tile (the outer edge is the tile's edge) with a
/// tick at each of the four compass points, reaching in to 0.6875 (the 32x32's, two texels wide) or 0.734 (the 128x128's, three texels
/// wide) of the half tile. The sprite's ring is a polished tube, not a flat band: across its width a lit rim at each edge and a darker
/// core, darkest on the upper left and lower right, with a bright glint across the whole width at the lower left and the upper right; the
/// ticks are bevelled, light on the left of the upright ones and the lower side of the level ones, shadowed on the other. That shading is
/// measured from the 128x128's texels (it matches the 32x32's as closely as a fit to the 32x32 itself does) and drawn as a shaded mesh
/// with antialiased edges. One state only: the original's ring has no locked, in-range or weapon colour and does not animate. The shape
/// is in half-tile units, y down, centred on the tile.
/// </summary>
public static class ReticleArt
{
    /// <summary>[port] crosshair_drawn: the modern look draws the ring (the 1997 look keeps the sprite either way).</summary>
    public static bool Drawn = true;

    /// <summary>
    /// A ring and its four ticks, in half-tile units: the ring between RingIn and RingOut, the ticks of half-width TickHalf from TickIn out to the ring.
    /// Ring is the ring's grey (0..255) at Across points from its inner edge to its outer for each of its equal angular steps from the +x axis towards
    /// +y (y down), step by step; Upright is the upright ticks' grey across them from left to right, Level the level ticks' from top to bottom.
    /// </summary>
    public sealed record Shape(float RingIn, float RingOut, float TickHalf, float TickIn, int Across, byte[] Ring, byte[] Upright, byte[] Level)
    {
        public int Steps => Ring.Length / Across;

        /// <summary>True when the point (half-tile units from the centre, y down) lies on the ring or a tick.</summary>
        public bool Covers(float x, float y)
        {
            float r = MathF.Sqrt(x * x + y * y);
            if (r >= RingIn && r <= RingOut) return true;
            return r >= TickIn && r < RingIn && (MathF.Abs(x) <= TickHalf || MathF.Abs(y) <= TickHalf);
        }

        /// <summary>
        /// The ring's grey, 0..1, at <paramref name="angle"/> (radians from +x towards +y) and <paramref name="t"/> across it (0 the inner edge,
        /// 1 the outer), interpolated between the measured points.
        /// </summary>
        public float GreyAt(float angle, float t)
        {
            int n = Steps;
            float a = angle / Mathf.Tau * n;
            a -= MathF.Floor(a / n) * n;
            int i = (int)a % n, i1 = (i + 1) % n;
            float u = a - MathF.Floor(a);
            float x = Math.Clamp(t, 0f, 1f) * (Across - 1);
            int j = Math.Min((int)x, Across - 2);
            float v = x - j;
            float lo = Ring[i * Across + j] * (1 - u) + Ring[i1 * Across + j] * u;
            float hi = Ring[i * Across + j + 1] * (1 - u) + Ring[i1 * Across + j + 1] * u;
            return (lo * (1 - v) + hi * v) / 255f;
        }

        /// <summary>A tick's grey, 0..1, at <paramref name="s"/> across it (0 its left or top side, 1 its right or bottom).</summary>
        public float TickGreyAt(bool upright, float s)
        {
            var p = upright ? Upright : Level;
            float x = Math.Clamp(s, 0f, 1f) * (p.Length - 1);
            int j = Math.Min((int)x, p.Length - 2);
            return (p[j] + (p[j + 1] - p[j]) * (x - j)) / 255f;
        }
    }

    /// <summary>
    /// The ring's shading: 48 angular steps (four to a line) of 7 points from the inner edge to the outer, fitted by least squares to zretc_6's
    /// texels clear of the sprite's keyed edge, held smooth round the ring.
    /// </summary>
    private static readonly byte[] Tube =
    [
        169,  81,  64,  60,  35, 129, 127,  165,  84,  63,  69,  40, 128, 131,  164,  97,  62,  68,  42, 132, 111,  145, 107,  55,  58,  43, 142,  74,
        104, 119,  43,  43,  35, 139,  24,   58, 129,  33,  29,  53, 132,   7,   25, 131,  40,  21,  61, 126,  28,   34, 114,  11,  13,  28, 127,  41,
         52,  98,   1,  19,  19, 152,  93,   83,  85,   1,  19,   7, 125, 146,  115,  83,  14,  36,  29, 108, 175,  139, 102,  63,  88,  77, 114, 188,
        160, 135,  74,  63,  57, 108, 192,  136, 142,  49,  29,  56, 122, 202,  100, 157,  68,  97, 122, 151, 200,   54, 169,  89, 131, 130, 162, 175,
         17, 178, 140, 191, 171, 189, 122,    0, 161, 209, 245, 228, 201,  42,    0, 121, 229, 240, 232, 166,   0,    0, 162, 222, 245, 233, 201,  36,
         11, 182, 165, 221, 201, 204, 117,   49, 178,  99, 150, 145, 171, 176,   98, 167,  73, 107, 120, 150, 206,  143, 151,  48,  37,  66, 133, 218,
        168, 148,  34,   6,  29, 110, 215,  150, 141,  50,  26,  53, 134, 221,  129, 160,  59,  45,  65, 168, 208,   85, 169,  65,  50,  86, 199, 166,
         40, 171,  75,  72, 107, 190,  95,   11, 159,  80,  56, 109, 150,  36,    7, 158,  76,  39, 129, 148,  20,   40, 167,  63,  40, 127, 168,   0,
        110, 158,  54,  43, 103, 195,  18,  164, 142,  51,  39,  76, 195,  79,  196, 123,  31,  14,  48, 175, 121,  204, 105,  22,   8,  41, 168, 142,
        200, 111,  41,  14,  43, 165, 137,  204, 132,  84,  52,  63, 173, 124,  200, 143, 128, 103,  82, 180,  83,  185, 150, 150, 148, 113, 172,  30,
        164, 181, 202, 208, 173, 147,   0,  117, 194, 223, 234, 210,  99,   0,   76, 183, 222, 228, 209,  44,   0,  116, 194, 224, 233, 209, 102,   0,
        164, 179, 201, 202, 163, 149,   0,  184, 147, 152, 154, 115, 167,  34,  187, 130, 138, 135,  68, 145,  81,  176, 101,  93,  77,  35, 130, 115,
    ];

    /// <summary>zretc_1 (the hood's ring and the 2D dash's), 32x32: the ticks two texels wide, light on the left and below.</summary>
    public static readonly Shape Coarse = new(0.852f, 1f, 1f / 16f, 11f / 16f, 7, Tube, [95, 64], [56, 95]);
    /// <summary>zretc_6 (the binoculars' ring), 128x128: the ticks three texels wide, the level ones with a dark line along the top.</summary>
    public static readonly Shape Fine = new(0.852f, 1f, 1.5f / 64f, 47f / 64f, 7, Tube, [186, 132, 67], [33, 101, 187, 136]);

    /// <summary>
    /// Draws <paramref name="s"/> on <paramref name="ci"/> where <paramref name="map"/> carries the half-tile units (the tile's centre at 0, its edge at 1)
    /// to canvas pixels: the ticks, then the ring over their outer ends, each a mesh shaded across its width (and the ring along its angle), its edges
    /// feathered over a screen pixel for antialiasing. <paramref name="map"/> may be a perspective projection.
    /// </summary>
    public static void Draw(CanvasItem ci, Shape s, Func<Vector2, Vector2?> map)
    {
        var c = map(Vector2.Zero); var ex = map(Vector2.Right); var ey = map(Vector2.Down);
        if (c is not { } c0 || ex is not { } x0 || ey is not { } y0) return;
        float unit = ((x0 - c0).Length() + (y0 - c0).Length()) * 0.5f;   // screen pixels to a half tile
        if (unit < 2f) return;
        var mesh = new Mesh();
        float band = s.RingOut - s.RingIn;
        for (int k = 0; k < 4; k++)
        {
            var d = new Vector2(k == 0 ? 1 : k == 2 ? -1 : 0, k == 1 ? 1 : k == 3 ? -1 : 0);
            bool upright = d.X == 0;
            var side = upright ? Vector2.Right : Vector2.Down;   // from the tick's left or top side to its right or bottom
            int m = (upright ? s.Upright : s.Level).Length;
            // rows: the inner end, and under the ring's middle; columns: the measured points over the tick's width
            if (!mesh.Grid(2, m, false, (i, j) => map(d * (i == 0 ? s.TickIn : s.RingIn + band * 0.5f) + side * s.TickHalf * (2f * j / (m - 1) - 1f)),
                    (i, j) => s.TickGreyAt(upright, j / (m - 1f)))) return;
        }
        int n = Math.Clamp((int)(unit * 1.2f), 2 * s.Steps, 360);
        if (!mesh.Grid(n, s.Across, true, (i, j) =>
            {
                float a = Mathf.Tau * i / n;
                return map(new Vector2(MathF.Cos(a), MathF.Sin(a)) * (s.RingIn + band * j / (s.Across - 1)));
            }, (i, j) => s.GreyAt(Mathf.Tau * i / n, j / (s.Across - 1f)))) return;
        RenderingServer.CanvasItemAddTriangleArray(ci.GetCanvasItem(), [.. mesh.Index], [.. mesh.Point], [.. mesh.Colour]);
    }

    /// <summary>Triangles with a grey at each vertex, built as grids whose edges fade out over a screen pixel.</summary>
    private sealed class Mesh
    {
        public readonly List<Vector2> Point = [];
        public readonly List<Color> Colour = [];
        public readonly List<int> Index = [];

        /// <summary>
        /// A grid of <paramref name="rows"/> by <paramref name="cols"/> points (<paramref name="at"/>, in canvas pixels; a null fails the grid) shaded
        /// <paramref name="grey"/>. Closed joins the last row to the first (the ring); open (a tick) fades its first row out too. The edge points are pulled
        /// in by half a pixel and a transparent point set half a pixel outside each, so an edge covers its share of the pixel it crosses.
        /// </summary>
        public bool Grid(int rows, int cols, bool closed, Func<int, int, Vector2?> at, Func<int, int, float> grey)
        {
            var p = new Vector2[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    if (at(i, j) is not { } q) return false;
                    p[i, j] = q;
                }
            int cap = closed ? 0 : 1, nc = cols + 2, b = Point.Count;
            var along = closed ? Vector2.Zero : (p[1, 0] - p[0, 0]).Normalized();
            for (int i = -cap; i < rows; i++)
            {
                int r = Math.Max(i, 0);
                var across = p[r, cols - 1] - p[r, 0];
                float w = across.Length();
                var dir = w > 1e-4f ? across / w : Vector2.Zero;
                float pull = Math.Min(0.5f, w * 0.25f);
                for (int j = -1; j <= cols; j++)
                {
                    int jj = Math.Clamp(j, 0, cols - 1);
                    var q = p[r, jj];
                    if (j == 0) q += dir * pull; else if (j == cols - 1) q -= dir * pull;
                    if (j < 0) q -= dir * 0.5f; else if (j >= cols) q += dir * 0.5f;
                    float alpha = j < 0 || j >= cols ? 0f : 1f;
                    if (!closed && i < 0) { q -= along * 0.5f; alpha = 0f; }
                    else if (!closed && i == 0) q += along * 0.5f;
                    float g = grey(r, jj);
                    Point.Add(q);
                    Colour.Add(new Color(g, g, g, alpha));
                }
            }
            int total = rows + cap, strips = closed ? total : total - 1;
            for (int i = 0; i < strips; i++)
            {
                int r0 = b + i * nc, r1 = b + (i + 1) % total * nc;
                for (int j = 0; j < nc - 1; j++)
                {
                    Index.Add(r0 + j); Index.Add(r1 + j); Index.Add(r1 + j + 1);
                    Index.Add(r0 + j); Index.Add(r1 + j + 1); Index.Add(r0 + j + 1);
                }
            }
            return true;
        }
    }

    /// <summary>The ring as the 2D dash draws the sprite: the 32x32 tile at <paramref name="topLeft"/>, <paramref name="scale"/> pixels a texel.</summary>
    public static void DrawFlat(CanvasItem ci, Vector2 topLeft, float scale)
        => Draw(ci, Coarse, u => topLeft + (u + Vector2.One) * 16f * scale);
}
