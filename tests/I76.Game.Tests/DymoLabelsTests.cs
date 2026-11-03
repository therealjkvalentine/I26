using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's weapon labels: the painted spelling of each plate, and the label set in the font on the plate's tape.</summary>
public class DymoLabelsTests
{
    [Theory]
    [InlineData("fr_rocket_on", "FireRite Rkt", "FIRE-RITE RKT")]
    [InlineData("50cal_mg_off", "50cal MG", "50 CAL MG")]
    [InlineData("dr_missile_on", "DrRadar Msl", "DR RADAR MSL")]
    [InlineData("flame_off", "FlameThrower", "FLAMETHROWER")]
    [InlineData("762_mg_on", "7.62mm MG", "7.62MM MG")]
    [InlineData("30mm_can_on", "30mm Cannon", "30MM CANNON")]
    [InlineData("20mm_trt_off", "20mm Turret", "20MM TURRET")]
    // the tank cannons carry the original's EMPTY plate
    [InlineData("empty_on", "Tank Cannon", "EMPTY")]
    [InlineData("empty_off", "Police Tank Cann", "EMPTY")]
    // the specials panel, by its sprite names
    [InlineData("nitrous_on", null, "NITROUS OXIDE")]
    [InlineData("dash_mary_off", null, "DASHBD MARY")]
    [InlineData("sp_empty_on", null, "EMPTY")]
    public void APlateSaysWhatTheOriginalsPaintedPlateSays(string plate, string? table, string expected)
        => Assert.Equal(expected, DymoLabels.Text(plate, table));

    [Fact]
    public void APlateTheOriginalHasNotGotTakesTheTablesNameUpperCased()
    {
        Assert.Equal("MEGA GUN", DymoLabels.Text("megagun_on", " Mega Gun "));
        Assert.Equal("NEW THING", DymoLabels.Text("new_thing_off", null));
    }

    [Fact]
    public void TheFontIsInTheGameAssembly()
    {
        var f = DymoLabels.Font;
        Assert.NotNull(f);
        Assert.Equal(1000, f!.Em);
        Assert.InRange(f.CapHeight, 500, 560);
        Assert.NotEqual(0, f.Glyph('A'));
        var (contours, left, right) = f.Layout("EMPTY");
        Assert.NotEmpty(contours);
        Assert.True(right - left > 2000);
    }

    private static readonly (byte, byte, byte) Olive = (66, 72, 32), Tape = (209, 0, 41), Grey = (170, 170, 170);

    /// <summary>A lit plate as the original's: olive above and below, the tape on rows 3..18, grey letters on rows 7..14.</summary>
    private static byte[] Plate(int s)
    {
        int w = DymoLabels.PlateW * s, h = DymoLabels.PlateH * s;
        var b = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int py = y / s, px = x / s;
                var c = py < 3 || py > 18 ? Olive : py is >= 7 and <= 14 && px is >= 10 and <= 80 && px % 3 == 0 ? Grey : Tape;
                int i = ((h - 1 - y) * w + x) * 4;
                (b[i], b[i + 1], b[i + 2], b[i + 3]) = (c.Item1, c.Item2, c.Item3, 255);
            }
        return b;
    }

    private static (byte, byte, byte) At(byte[] b, int w, int h, int x, int y) { int i = ((h - 1 - y) * w + x) * 4; return (b[i], b[i + 1], b[i + 2]); }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheLabelIsSetAtThePaintedLettersHeightOnTheTape(int s)
    {
        int w = DymoLabels.PlateW * s, h = DymoLabels.PlateH * s;
        var b = Plate(s);
        DymoLabels.Paint(b, DymoLabels.PlateW, DymoLabels.PlateH, s, DymoLabels.Font!, "NITROUS OXIDE", lit: true);
        int top = h, bottom = -1, left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = At(b, w, h, x, y);
                // the painted letters' grey is gone: every texel is the olive, the tape, the shadow or the new face
                Assert.NotEqual(Grey, c);
                if (c.Item1 > 220 && c.Item2 > 220) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
            }
        // the face's rows: the painted letters' 7..14, give or take the font's rough edges; its width within the tape's 84
        Assert.InRange(top, 6 * s, 8 * s);
        Assert.InRange(bottom, 14 * s, 16 * s);
        Assert.True(right - left + 1 <= 84 * s, $"{right - left + 1} wide");
        Assert.True(right - left + 1 >= 80 * s, $"{right - left + 1} wide");
        // the olive above and below the tape is untouched
        Assert.Equal(Olive, At(b, w, h, 0, 0));
        Assert.Equal(Olive, At(b, w, h, w - 1, h - 1));
    }

    [Fact]
    public void AShortNameKeepsTheFontsProportions()
    {
        const int s = 2;
        int w = DymoLabels.PlateW * s, h = DymoLabels.PlateH * s;
        var b = Plate(s);
        DymoLabels.Paint(b, DymoLabels.PlateW, DymoLabels.PlateH, s, DymoLabels.Font!, "EMPTY", lit: false);
        int left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (At(b, w, h, x, y) is var c && c.Item1 > 150 && c.Item2 > 150) { left = Math.Min(left, x); right = Math.Max(right, x); }
        // five letters at 8 px a capital, about 0.6 em apart: far narrower than the tape, and centred on it
        Assert.InRange(right - left + 1, 30 * s, 60 * s);
        Assert.InRange((left + right) / 2, 44 * s, 48 * s);
    }
}
