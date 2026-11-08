using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The wheel faces are painted lit from one side, and the engine stepped hubcap frames under that still light; the modern look
/// turns the face itself, so object.gdshaderinc's wheel_still_light reads each ring's broad light (its first two harmonics) at the
/// angle the texel had at rest. Checked on the shader's text and on a model of its sum: a disc lit from one side with six holes,
/// turned, keeps its light where it was and turns its holes; at rest it is the texture as it is.
/// </summary>
public class WheelStillLightTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    [Fact]
    public void TheModernWheelFaceHoldsItsPaintedLightStill()
    {
        var shader = Repo("game", "Shaders", "object.gdshaderinc");
        var modern = shader.IndexOf("#ifndef FAITHFUL", StringComparison.Ordinal);
        var decl = shader.IndexOf("instance uniform vec3 wheel_turn", StringComparison.Ordinal);
        Assert.True(modern >= 0 && decl > modern, "wheel_turn is the modern look's");
        Assert.Matches(new Regex(@"if \(wheel_face && use_texture && wheel_turn\.z != 0\.0\) c\.rgb = wheel_still_light\("), shader);
        var car = Repo("game", "Scripts", "Sim", "CarNode.cs");
        Assert.Contains("SetInstanceShaderParameter(\"wheel_turn\"", car);
    }

    // a 64-texel disc: rubber, a dish lit from below (+v), six holes at mid radius
    private static float Paint(float u, float v)
    {
        float du = u - 0.5f, dv = v - 0.5f, r = MathF.Sqrt(du * du + dv * dv), th = MathF.Atan2(dv, du);
        if (r > 0.47f) return 0f;
        if (r > 0.36f) return 0.08f;
        float lit = 0.55f + 0.25f * MathF.Sin(th) * (r / 0.36f);
        bool hole = r > 0.18f && r < 0.26f && MathF.Cos(6 * th) > 0.7f;
        return hole ? 0.02f : lit;
    }

    // the shader's wheel_still_light on that paint (one channel)
    private static float StillLight(float col, float u, float v, float turn)
    {
        float du = u - 0.5f, dv = v - 0.5f, r = MathF.Sqrt(du * du + dv * dv);
        if (r < 1e-4f) return col;
        float a0 = 0, a1 = 0, b1 = 0, a2 = 0, b2 = 0;
        for (int i = 0; i < 32; i++)
        {
            float ph = i * (MathF.Tau / 32);
            float s = Paint(0.5f + r * MathF.Cos(ph), 0.5f + r * MathF.Sin(ph));
            a0 += s; a1 += s * MathF.Cos(ph); b1 += s * MathF.Sin(ph); a2 += s * MathF.Cos(2 * ph); b2 += s * MathF.Sin(2 * ph);
        }
        float th = MathF.Atan2(dv, du), tw = th + turn;
        float now = a1 * MathF.Cos(th) + b1 * MathF.Sin(th) + a2 * MathF.Cos(2 * th) + b2 * MathF.Sin(2 * th);
        float rest = a1 * MathF.Cos(tw) + b1 * MathF.Sin(tw) + a2 * MathF.Cos(2 * tw) + b2 * MathF.Sin(2 * tw);
        a0 /= 32; now = MathF.Max(a0 + now * 2 / 32, 0); rest = MathF.Max(a0 + rest * 2 / 32, 0);
        float k = Math.Clamp((a0 - 0.004f) / 0.016f, 0, 1); k = k * k * (3 - 2 * k);
        return col * (1 + (MathF.Min(rest / MathF.Max(now, 1e-4f), 4f) - 1) * k);
    }

    // what the face shows at (u, v) with its picture turned by turn: the texel that was at the angle turn behind
    private static float Shown(float u, float v, float turn, bool still)
    {
        float du = u - 0.5f, dv = v - 0.5f, c = MathF.Cos(-turn), s = MathF.Sin(-turn);
        float tu = 0.5f + c * du - s * dv, tv = 0.5f + s * du + c * dv;
        float col = Paint(tu, tv);
        return still ? StillLight(col, tu, tv, turn) : col;
    }

    [Fact]
    public void ATurnedWheelKeepsItsLightAndTurnsItsHoles()
    {
        // the dish's lit side (+v) against its shaded side (-v), between the holes' ring and the lip, and a hole's place
        float Lit(float turn, bool still) => Shown(0.5f, 0.5f + 0.31f, turn, still) - Shown(0.5f, 0.5f - 0.31f, turn, still);
        float rest = Lit(0, still: true);
        Assert.Equal(rest, Lit(0, still: false), 5);
        Assert.True(rest > 0.3f, $"the model's dish is lit from +v ({rest:F2})");
        // half a turn: as the face turns, its light goes round with it; held still, it stays
        Assert.True(Lit(MathF.PI, still: false) < -0.3f);
        Assert.InRange(Lit(MathF.PI, still: true), rest - 0.05f, rest + 0.05f);
        Assert.InRange(Lit(1.1f, still: true), rest - 0.05f, rest + 0.05f);
        // the holes turn with the face: at a twelfth of a turn a hole stands where the dish was
        float hu = 0.5f + 0.22f, hv = 0.5f;
        Assert.True(Shown(hu, hv, 0, true) < 0.1f, "a hole at rest");
        Assert.True(Shown(hu, hv, MathF.Tau / 12, true) > 0.3f, "the dish there a twelfth of a turn on");
        // and a hole stays dark wherever it is turned to (the light is a ratio, not added)
        for (int i = 0; i < 6; i++)
        {
            float t = MathF.Tau * i / 6 + 0.9f, c = MathF.Cos(t), sn = MathF.Sin(t);   // the six holes, turned by 0.9
            Assert.True(Shown(0.5f + 0.22f * c, 0.5f + 0.22f * sn, 0.9f, true) < 0.05f, $"a hole at {t:F2} greyed");
        }
    }
}
