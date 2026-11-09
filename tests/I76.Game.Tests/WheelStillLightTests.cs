using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The wheel faces are painted under one still light, and the engine stepped hubcap frames under it; the modern look turns the
/// face itself, so object.gdshaderinc's wheel_still_light reads the light at the place the texel had at rest: the light is the
/// strip's frames averaged texel by texel (SmoothWheel.StillLight), where the hubcap averages into rings. Checked on the shader's
/// text and on a model of its rule: a disc with a broad light from one side and a sharp horizon across it (a chrome dome's sky
/// and ground), six holes turning through a strip; turned, it keeps the light and the horizon where they were and turns its holes.
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
        Assert.Contains("uniform sampler2D wheel_light", shader);
        Assert.Matches(new Regex(@"if \(wheel_face && use_texture && wheel_turn\.z != 0\.0\) c\.rgb = wheel_still_light\(c\.rgb, tuv, wheel_turn\)"), shader);
        // the light at rest is read at the texel's place turned on by wt.z (from +u toward +v), over the light where it is
        Assert.Contains("wt.xy + vec2(c * d.x - s * d.y, s * d.x + c * d.y)", shader);
        var car = Repo("game", "Scripts", "Sim", "CarNode.cs");
        Assert.Contains("SetInstanceShaderParameter(\"wheel_turn\"", car);
        Assert.Contains("I76_WHEEL_LIGHT", car);
        var mesh = Repo("game", "Scripts", "Assets", "MeshBuilder.cs");
        Assert.Contains("SetShaderParameter(\"wheel_light\"", mesh);
        Assert.Contains("SmoothWheel.StillLight(", mesh);
        // and the face's picture the way up the original shows it: the wheel textures are stored the other way up from the bodies',
        // so the assembler turns the .wdf faces' v over (SmoothWheelTests.EveryWheelFaceShowsItsPictureTheWayUpTheOriginalDoes)
        var assembler = Repo("src", "I76.Formats", "Assembly", "ModelAssembler.cs");
        Assert.Contains("geo.WithVFlipped(), blown?.WithVFlipped()", assembler);
    }

    // a disc: rubber, a dish lit from +v (a broad gradient) with a sharp horizon across its middle (bright above, dark below, as
    // a chrome dome reflects sky and ground), six holes at mid radius turned by phase
    private static float Paint(float u, float v, float phase)
    {
        float du = u - 0.5f, dv = v - 0.5f, r = MathF.Sqrt(du * du + dv * dv), th = MathF.Atan2(dv, du);
        if (r > 0.47f) return 0f;
        if (r > 0.36f) return 0.08f;
        float lit = 0.45f + 0.2f * MathF.Sin(th) * (r / 0.36f) + (dv > 0 ? 0.2f : -0.1f);
        bool hole = r > 0.18f && r < 0.26f && MathF.Cos(6 * (th - phase)) > 0.7f;
        return hole ? 0.02f : lit;
    }

    private const int Frames = 6;
    // the strip: the holes stepped through their period, the light still; its mean is the shader's wheel_light
    private static float Light(float u, float v)
    {
        float s = 0;
        for (int k = 0; k < Frames; k++) s += Paint(u, v, MathF.Tau / 6 * k / Frames);
        return s / Frames;
    }

    // the shader's wheel_still_light on frame 0 (one channel): the texel's colour times the light at its place at rest over the light at its own place
    private static float StillLight(float col, float tu, float tv, float turn)
    {
        float du = tu - 0.5f, dv = tv - 0.5f, c = MathF.Cos(turn), s = MathF.Sin(turn);
        float now = Light(tu, tv), rest = Light(0.5f + c * du - s * dv, 0.5f + s * du + c * dv);
        float k = Math.Clamp((now - 0.004f) / 0.016f, 0, 1); k = k * k * (3 - 2 * k);
        return col * (1 + (MathF.Min(rest / MathF.Max(now, 1e-3f), 8f) - 1) * k);
    }

    // what the face shows at (u, v) with its picture turned by turn: the texel that was at the angle turn behind
    private static float Shown(float u, float v, float turn, bool still)
    {
        float du = u - 0.5f, dv = v - 0.5f, c = MathF.Cos(-turn), s = MathF.Sin(-turn);
        float tu = 0.5f + c * du - s * dv, tv = 0.5f + s * du + c * dv;
        float col = Paint(tu, tv, 0);
        return still ? StillLight(col, tu, tv, turn) : col;
    }

    [Fact]
    public void ATurnedWheelKeepsItsLightAndTurnsItsHoles()
    {
        // the dish's lit side (+v) against its shaded side (-v), between the holes' ring and the lip
        float Lit(float turn, bool still) => Shown(0.5f, 0.5f + 0.31f, turn, still) - Shown(0.5f, 0.5f - 0.31f, turn, still);
        float rest = Lit(0, still: true);
        Assert.Equal(rest, Lit(0, still: false), 5);
        Assert.True(rest > 0.4f, $"the model's dish is lit from +v ({rest:F2})");
        // half a turn: as the face turns, its light goes round with it; held still, it stays, at any turn
        Assert.True(Lit(MathF.PI, still: false) < -0.4f);
        foreach (var t in new[] { 0.4f, 1.1f, MathF.PI / 2, MathF.PI, 4.0f, 5.5f })
            Assert.InRange(Lit(t, still: true), rest - 0.03f, rest + 0.03f);
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

    /// <summary>
    /// The sharp horizon across the dish (a dome's sky over its ground) is part of the painted light: turned half round it would show
    /// the ground above; held still it stays the right way up, at the texel's own place (a fit of two harmonics round the hub left
    /// its step turning, and the shadow read as upside down).
    /// </summary>
    [Fact]
    public void AStepInTheLightStaysTheRightWayUp()
    {
        // just above and just below the horizon, inside the holes' ring: the step is the painted light's
        float Step(float turn, bool still) => Shown(0.5f + 0.1f, 0.5f + 0.03f, turn, still) - Shown(0.5f + 0.1f, 0.5f - 0.03f, turn, still);
        float rest = Step(0, true);
        Assert.InRange(rest, 0.25f, 0.35f);
        Assert.True(Step(MathF.PI, still: false) < -0.25f, "turned as is, the ground comes up over the sky");
        foreach (var t in new[] { 0.7f, MathF.PI / 2, MathF.PI, 4.5f })
            Assert.InRange(Step(t, still: true), rest - 0.03f, rest + 0.03f);
    }
}
