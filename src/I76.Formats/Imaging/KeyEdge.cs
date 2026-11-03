namespace I76.Formats.Imaging;

/// <summary>
/// A keyed texture's outline as smooth curves rather than the pixel staircase (the modern look's cut-out edges). The art's
/// opaque mask is traced along its pixel edges; corners are kept where two straight runs meet (both of three pixels or more,
/// or a two-pixel tab or notch, or two straight four-pixel arms at 45 degrees or more); between them each edge's midpoint is
/// moved to the widest local fit (a Gaussian-weighted line through its neighbours, or a parabola on a curve; 8 down to 1 pixel) that stays within
/// <see cref="Tolerance"/> of the art's edge, and never past half the art's thickness or gap there. A staircase of any slope
/// comes out straight, an uneven curve smooth; a single-pixel feature keeps its pixels. <see cref="Field"/> turns the outlines
/// into a signed distance field, which a shader cuts at half over one screen pixel.
/// </summary>
public static class KeyEdge
{
    /// <summary>How far the smooth outline may stray from the art's pixel edge, in pixels.</summary>
    public const float Tolerance = 0.6f;

    /// <summary>The field's reach each way, in pixels: 0 is this far outside, 255 this far inside, 128 the edge.</summary>
    public const float Range = 2f;

    private static readonly float[] Scales = [8f, 6f, 4f, 3f, 2f, 1.5f, 1f];
    /// <summary>Each scale's Gaussian weights, at 0, 1, 2 ... points away, to 2.5 sigmas.</summary>
    private static readonly double[][] Weights = [.. Scales.Select(s => Enumerable.Range(0, (int)MathF.Ceiling(2.5f * s) + 1).Select(d => Math.Exp(-0.5 * (d / s) * (d / s))).ToArray())];
    private const int Arm = 4, Window = 12;
    private const float ArmStraight = 0.8f, CornerAngle = 45f * MathF.PI / 180f;
    private static readonly (int X, int Y)[] Normals = [(0, -1), (0, 1), (-1, 0), (1, 0)];

    /// <summary>
    /// The smooth outlines of a w × h mask (true opaque, rows from the top), closed polygons in pixels (pixel (x, y) covers
    /// x..x+1, y..y+1), the opaque side on the left going round. Outside the picture the edge row or column carries on a
    /// pixel (a tiling texture's), then all is clear; points out there stay where they are.
    /// </summary>
    public static List<(float X, float Y)[]> Outlines(bool[] mask, int w, int h)
    {
        int W = w + 4, H = h + 4, VW = W + 1;
        bool Px(int x, int y)
        {
            x -= 2; y -= 2;
            return x >= -1 && y >= -1 && x <= w && y <= h && mask[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        }
        var ea = new List<int>(); var eb = new List<int>(); var en = new List<byte>();
        var out0 = new int[VW * (H + 1)]; var out1 = new int[out0.Length];
        Array.Fill(out0, -1); Array.Fill(out1, -1);
        void Add(int ax, int ay, int bx, int by, byte n)
        {
            int a = ay * VW + ax, e = ea.Count;
            ea.Add(a); eb.Add(by * VW + bx); en.Add(n);
            if (out0[a] < 0) out0[a] = e; else out1[a] = e;
        }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!Px(x, y)) continue;
                if (!Px(x, y - 1)) Add(x + 1, y, x, y, 0);
                if (!Px(x, y + 1)) Add(x, y + 1, x + 1, y + 1, 1);
                if (!Px(x - 1, y)) Add(x, y, x, y + 1, 2);
                if (!Px(x + 1, y)) Add(x + 1, y + 1, x + 1, y, 3);
            }
        (int X, int Y) Dir(int e) => (eb[e] % VW - ea[e] % VW, eb[e] / VW - ea[e] / VW);
        // where two clear and two opaque pixels meet corner to corner the outline turns toward the opaque one across: the
        // art's one-pixel diagonals stay joined
        int Next(int e)
        {
            int a = out0[eb[e]], b = out1[eb[e]];
            if (b < 0) return a;
            var d = Dir(e);
            var da = Dir(a); var db = Dir(b);
            return d.X * da.Y - d.Y * da.X > d.X * db.Y - d.Y * db.X ? a : b;
        }
        var used = new bool[ea.Count];
        var result = new List<(float X, float Y)[]>();
        var loop = new List<int>();
        for (int s = 0; s < ea.Count; s++)
        {
            if (used[s]) continue;
            loop.Clear();
            for (int e = s; e >= 0 && !used[e]; e = Next(e)) { used[e] = true; loop.Add(e); }
            var vx = new int[loop.Count]; var vy = new int[loop.Count]; var nn = new byte[loop.Count];
            for (int i = 0; i < loop.Count; i++) { vx[i] = ea[loop[i]] % VW - 2; vy[i] = ea[loop[i]] / VW - 2; nn[i] = en[loop[i]]; }
            result.Add(Fit(vx, vy, nn, mask, w, h));
        }
        return result;
    }

    /// <summary>One traced loop (its vertices, unit steps, each edge's outward normal) as a smooth polygon.</summary>
    private static (float X, float Y)[] Fit(int[] vx, int[] vy, byte[] nn, bool[] mask, int w, int h)
    {
        int n = vx.Length;
        // a loop of a pixel or two keeps its pixels
        if (n < 12) return [.. Enumerable.Range(0, n).Select(i => ((float)vx[i], (float)vy[i]))];
        int M(int i) => ((i % n) + n) % n;
        (int X, int Y) D(int i) { int j = M(i); return (vx[M(j + 1)] - vx[j], vy[M(j + 1)] - vy[j]); }

        // corners, at vertex i (between edges i-1 and i): two straight arms of Arm edges at CornerAngle or more, the
        // strongest of five; or a turn between straight runs of 3 and more, or into a run of 2 that is a tab or notch
        var corner = new bool[n];
        var score = new float[n];
        (float X, float Y)? Chord(int i, int j)
        {
            float ax = vx[M(i)], ay = vy[M(i)], dx = vx[M(j)] - ax, dy = vy[M(j)] - ay, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len == 0) return null;
            for (int q = i + 1; q < j; q++)
                if (MathF.Abs((vx[M(q)] - ax) * dy - (vy[M(q)] - ay) * dx) / len > ArmStraight) return null;
            return (dx / len, dy / len);
        }
        static float Angle((float X, float Y) a, (float X, float Y) b) => MathF.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y, -1f, 1f));
        for (int i = 0; i < n; i++)
            if (Chord(i - Arm, i) is { } u && Chord(i, i + Arm) is { } v)
            {
                float ang = Angle(u, v);
                // the arms turn less than half as much beyond (where they can be read): a curve turns all along, a corner at one place
                if (ang < CornerAngle || (Chord(i - 2 * Arm, i - Arm) is { } u0 && 2 * Angle(u0, u) > ang) || (Chord(i + Arm, i + 2 * Arm) is { } v1 && 2 * Angle(v, v1) > ang)) continue;
                score[i] = ang;
            }
        for (int i = 0; i < n; i++)
        {
            if (score[i] <= 0) continue;
            bool best = true;
            for (int d = -2; d <= 2 && best; d++)
                if (d != 0) best = d < 0 ? score[i] > score[M(i + d)] : score[i] >= score[M(i + d)];
            if (best) corner[i] = true;
        }
        int start = 0;
        while (start < n && D(start - 1) == D(start)) start++;
        var runs = new List<(int First, int Length, (int X, int Y) Dir)>();
        for (int i = 0; i < n;)
        {
            int j = i;
            while (j + 1 < n && D(start + j + 1) == D(start + i)) j++;
            runs.Add((M(start + i), j - i + 1, D(start + i)));
            i = j + 1;
        }
        static int Cross((int X, int Y) a, (int X, int Y) b) => a.X * b.Y - a.Y * b.X;
        int nr = runs.Count;
        if (nr >= 4)
            for (int r = 0; r < nr; r++)
            {
                var (_, a, d0) = runs[r];
                var (k1, b, d1) = runs[(r + 1) % nr];
                int t = Cross(d0, d1);
                // a two-pixel run is a tab or notch where both its ends turn the same way (on a curve or a staircase they alternate or it is longer)
                bool ok = Math.Min(a, b) >= 3 || (Math.Min(a, b) == 2
                    && ((a == 2 && Cross(runs[(r + nr - 1) % nr].Dir, d0) == t) || (b == 2 && Cross(d1, runs[(r + 2) % nr].Dir) == t)));
                if (ok) corner[k1] = true;
            }

        // the edges, unpadded, and how far the outline may go inside and outside each: half the art's thickness or gap
        // along its normal (counted to 8 pixels), at most Tolerance
        var tin = new float[n]; var tout = new float[n];
        int Count(int x, int y, int dx, int dy, bool val)
        {
            int c = 0;
            while (c < 8)
            {
                if (mask[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)] != val) break;
                c++; x += dx; y += dy;
                if (x < -1 || y < -1 || x > w || y > h) return 8;
            }
            return c;
        }
        for (int i = 0; i < n; i++)
        {
            var (nx, ny) = Normals[nn[i]];
            var d = D(i);
            // the opaque pixel beside the edge
            int ax = Math.Min(vx[i], vx[i] + d.X) - (nx > 0 ? 1 : 0), ay = Math.Min(vy[i], vy[i] + d.Y) - (ny > 0 ? 1 : 0);
            tin[i] = MathF.Min(Tolerance, Count(ax, ay, -nx, -ny, true) / 2f - 0.05f);
            tout[i] = MathF.Min(Tolerance, Count(ax + nx, ay + ny, nx, ny, false) / 2f - 0.05f);
        }

        // the points: each edge's midpoint, a corner's vertex before its edge; fixed: corners and points outside the picture
        var px = new List<float>(n + 16); var py = new List<float>(n + 16); var edge = new List<int>(n + 16); var fix = new List<bool>(n + 16);
        for (int i = 0; i < n; i++)
        {
            if (corner[i]) { px.Add(vx[i]); py.Add(vy[i]); edge.Add(i); fix.Add(true); }
            var d = D(i);
            float mx = vx[i] + d.X * 0.5f, my = vy[i] + d.Y * 0.5f;
            px.Add(mx); py.Add(my); edge.Add(i); fix.Add(mx < 0 || my < 0 || mx > w || my > h);
        }
        int m = px.Count;
        int P(int k) => ((k % m) + m) % m;

        // the nearest point of the art's edge (its edges near this point's own); the point pulled back within the edge's limit
        (float X, float Y) Project(int k, float x, float y)
        {
            float best = float.MaxValue, qx = 0, qy = 0; int bj = 0;
            int reach = Math.Min(Window, (n - 1) / 2);
            for (int dd = -reach; dd <= reach; dd++)
            {
                int j = M(edge[k] + dd);
                var d = D(j);
                float u = Math.Clamp((x - vx[j]) * d.X + (y - vy[j]) * d.Y, 0f, 1f);
                float cx = vx[j] + u * d.X, cy = vy[j] + u * d.Y, dist = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (dist < best) { best = dist; qx = cx; qy = cy; bj = j; }
            }
            var (nx, ny) = Normals[nn[bj]];
            float lim = (x - qx) * nx + (y - qy) * ny > 0 ? tout[bj] : tin[bj], r = MathF.Sqrt(best);
            return r > lim && r > 0 ? (qx + (x - qx) * lim / r, qy + (y - qy) * lim / r) : (x, y);
        }
        // a Gaussian-weighted line through the points about k (in their order), read at k; a parabola instead where it fits
        // them much closer (a curve's own bend; on a staircase it would follow the steps). The window stops at a fixed point,
        // taking it in
        var win = new List<(double T, double W, int Q)>();
        (float X, float Y) Line(List<float> sx, List<float> sy, int k, int scale)
        {
            var wt = Weights[scale];
            int r = wt.Length - 1;
            win.Clear();
            win.Add((0, 1, k));
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int d = 1; d <= r; d++)
                {
                    int q = P(k + sgn * d);
                    win.Add((sgn * d, wt[d], q));
                    if (fix[q]) break;
                }
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, x0 = 0, x1 = 0, x2 = 0, y0 = 0, y1 = 0, y2 = 0;
            foreach (var (t, wq, q) in win)
            {
                double t2 = t * t;
                s0 += wq; s1 += wq * t; s2 += wq * t2; s3 += wq * t2 * t; s4 += wq * t2 * t2;
                x0 += wq * sx[q]; x1 += wq * t * sx[q]; x2 += wq * t2 * sx[q];
                y0 += wq * sy[q]; y1 += wq * t * sy[q]; y2 += wq * t2 * sy[q];
            }
            double dl = s0 * s2 - s1 * s1;
            if (dl < 1e-9) return ((float)(x0 / s0), (float)(y0 / s0));
            double lxa = (s2 * x0 - s1 * x1) / dl, lxb = (s0 * x1 - s1 * x0) / dl, lya = (s2 * y0 - s1 * y1) / dl, lyb = (s0 * y1 - s1 * y0) / dl;
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (win.Count < 5 || Math.Abs(det) < 1e-9) return ((float)lxa, (float)lya);
            // the parabola's three terms (Cramer's rule on the normal equations)
            (double A, double B, double C) Q(double v0, double v1, double v2) => (
                (v0 * (s2 * s4 - s3 * s3) - s1 * (v1 * s4 - s3 * v2) + s2 * (v1 * s3 - s2 * v2)) / det,
                (s0 * (v1 * s4 - s3 * v2) - v0 * (s1 * s4 - s3 * s2) + s2 * (s1 * v2 - v1 * s2)) / det,
                (s0 * (s2 * v2 - v1 * s3) - s1 * (s1 * v2 - v1 * s2) + v0 * (s1 * s3 - s2 * s2)) / det);
            var qx = Q(x0, x1, x2); var qy = Q(y0, y1, y2);
            double el = 0, eq = 0;
            foreach (var (t, wq, q) in win)
            {
                double ex = sx[q] - lxa - lxb * t, ey = sy[q] - lya - lyb * t;
                el += wq * (ex * ex + ey * ey);
                ex = sx[q] - qx.A - (qx.B + qx.C * t) * t; ey = sy[q] - qy.A - (qy.B + qy.C * t) * t;
                eq += wq * (ex * ex + ey * ey);
            }
            return eq < 0.5 * el ? ((float)qx.A, (float)qy.A) : ((float)lxa, (float)lya);
        }
        var ox = new List<float>(px); var oy = new List<float>(py);
        for (int k = 0; k < m; k++)
        {
            if (fix[k]) continue;
            for (int sc = 0; sc < Scales.Length; sc++)
            {
                var (x, y) = Line(px, py, k, sc);
                var (cx, cy) = Project(k, x, y);
                if ((cx == x && cy == y) || sc == Scales.Length - 1) { ox[k] = cx; oy[k] = cy; break; }
            }
        }
        // one light pass evens out where neighbours took different widths
        var res = new (float X, float Y)[m];
        for (int k = 0; k < m; k++)
        {
            if (fix[k]) { res[k] = (ox[k], oy[k]); continue; }
            var (x, y) = Line(ox, oy, k, Scales.Length - 1);
            res[k] = Project(k, x, y);
        }
        return res;
    }

    /// <summary>
    /// The mask's smooth outlines as a signed distance field sampled at <paramref name="scale"/> texels a pixel (w·scale ×
    /// h·scale, one byte a texel, rows from the top): 128 on the outline, rising inside to 255 at <see cref="Range"/> pixels.
    /// </summary>
    public static byte[] Field(bool[] mask, int w, int h, int scale) => Field(Outlines(mask, w, h), w, h, scale);

    /// <summary>Outlines (as <see cref="Outlines"/>) as a signed distance field; inside by the even-odd rule.</summary>
    public static byte[] Field(List<(float X, float Y)[]> outlines, int w, int h, int scale)
    {
        int fw = w * scale, fh = h * scale;
        var dist = new float[fw * fh];
        Array.Fill(dist, Range);
        var rows = new List<float>?[fh];
        foreach (var poly in outlines)
            for (int i = 0; i < poly.Length; i++)
            {
                var (x0, y0) = poly[i];
                var (x1, y1) = poly[(i + 1) % poly.Length];
                // the rows whose centre the segment crosses (half-open, so a shared vertex counts once)
                if (y0 != y1)
                {
                    float lo = MathF.Min(y0, y1), hi = MathF.Max(y0, y1);
                    for (int Y = Math.Max(0, (int)MathF.Ceiling(lo * scale - 0.5f)); Y < fh; Y++)
                    {
                        float yc = (Y + 0.5f) / scale;
                        if (yc >= hi) break;
                        if (yc < lo) continue;
                        (rows[Y] ??= []).Add(x0 + (yc - y0) / (y1 - y0) * (x1 - x0));
                    }
                }
                // the frame round the picture's carried-on row and column is no edge of the art
                if ((x0 < 0 && x1 < 0) || (y0 < 0 && y1 < 0) || (x0 > w && x1 > w) || (y0 > h && y1 > h)) continue;
                float dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
                int X0 = Math.Max(0, (int)MathF.Floor((MathF.Min(x0, x1) - Range) * scale)), X1 = Math.Min(fw - 1, (int)MathF.Ceiling((MathF.Max(x0, x1) + Range) * scale));
                int Y0 = Math.Max(0, (int)MathF.Floor((MathF.Min(y0, y1) - Range) * scale)), Y1 = Math.Min(fh - 1, (int)MathF.Ceiling((MathF.Max(y0, y1) + Range) * scale));
                for (int Y = Y0; Y <= Y1; Y++)
                {
                    float yc = (Y + 0.5f) / scale;
                    for (int X = X0; X <= X1; X++)
                    {
                        float xc = (X + 0.5f) / scale;
                        float u = len2 > 0 ? Math.Clamp(((xc - x0) * dx + (yc - y0) * dy) / len2, 0f, 1f) : 0f;
                        float ex = xc - x0 - u * dx, ey = yc - y0 - u * dy, d = MathF.Sqrt(ex * ex + ey * ey);
                        if (d < dist[Y * fw + X]) dist[Y * fw + X] = d;
                    }
                }
            }
        var o = new byte[fw * fh];
        for (int Y = 0; Y < fh; Y++)
        {
            var xs = rows[Y];
            xs?.Sort();
            int c = 0;
            for (int X = 0; X < fw; X++)
            {
                float xc = (X + 0.5f) / scale;
                while (xs is not null && c < xs.Count && xs[c] < xc) c++;
                float sd = (c & 1) == 1 ? dist[Y * fw + X] : -dist[Y * fw + X];
                o[Y * fw + X] = (byte)Math.Clamp((int)((0.5f + sd / (2 * Range)) * 255f + 0.5f), 0, 255);
            }
        }
        return o;
    }

    /// <summary>The opaque mask of an RGBA picture (alpha 128 and over).</summary>
    public static bool[] Opaque(byte[] rgba, int w, int h)
    {
        var m = new bool[w * h];
        for (int i = 0; i < m.Length; i++) m[i] = rgba[i * 4 + 3] >= 128;
        return m;
    }
}
