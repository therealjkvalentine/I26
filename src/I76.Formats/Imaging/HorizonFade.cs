namespace I76.Formats.Imaging;

/// <summary>
/// The colour the 1997 look's ground fades into at the Faded draw distance: the mean of the horizon panels' foot rows (the rows
/// the sky shader stands on the horizon, texture rows 0 to 3), over every panel, the opaque texels only. The patch kit
/// i76-everywhere fades the original's ground into the same colour (I76_GLIDE_FOG's "hzd": the mean of the bottom 4 rows of every
/// horizon strip through the mission's palette).
/// </summary>
public static class HorizonFade
{
    public const int Rows = 4;

    /// <summary>The mean (r, g, b) in 0..1 of the panels' first <see cref="Rows"/> rows (RGBA8, rows from the top of the data); null with no opaque texel.</summary>
    public static (float R, float G, float B)? Colour(IEnumerable<(byte[] Rgba, int Width, int Height)> panels)
    {
        double r = 0, g = 0, b = 0; long n = 0;
        foreach (var (px, w, h) in panels)
            for (int y = 0; y < Math.Min(Rows, h); y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (i + 3 >= px.Length || px[i + 3] < 128) continue;
                    r += px[i]; g += px[i + 1]; b += px[i + 2]; n++;
                }
        return n == 0 ? null : ((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255));
    }
}
