using I76.Game.Shell;
using I76.Game.Sim;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Radar Sweep (RadarSweepRow, RadarSweepLaw, RadarSweepParams): the setting, the smooth beam's clock against the stepped frames', the cone's bearing, zrads.json. Without the engine.</summary>
public class RadarSweepTests
{
    /// <summary>zrads.json as tools/radar-redraw.py writes it for an 8x pack (the fitted numbers of the original's frames).</summary>
    public const string Json8x = """
        {"format": 1, "what": "the live radar sweep's numbers", "style": "B", "scale": 8, "size": [2048, 1024],
         "layers": {"face": "zrads_face", "grid": "zrads_grid", "off": "zradf030", "grid_box": [336, 80, 1232, 984]},
         "face": {"centre": [97.852, 66.546], "radius": 54.293, "colour": [0, 69, 0]},
         "pivot": [98.897, 65.0],
         "sweep": {"frames": 30, "step_deg": 12.0, "step_s": 0.2, "deg_per_s": 60.0, "offset_deg": 1.078, "direction": "clockwise on screen"},
         "beam": {"width": 1.5, "core_width": 0.7, "edge_colour": [120, 240, 120], "core_colour": [225, 255, 225], "length": 55.293},
         "afterglow": {"span_deg": 150, "peak": 0.62, "decay_deg": 32, "decay_s": 0.5333, "colour": [40, 200, 40]},
         "cone": {"half_angle": 5.935, "apex_behind_pivot": 13.43, "fill": [72, 175, 72], "soft": 2.2, "axis_colour": [150, 245, 150], "axis_alpha": 0.45}}
        """;

    [Fact]
    public void SteppedIsTheDefaultAndSmoothIsSavedAndReadBack()
    {
        Assert.Equal(["Stepped", "Smooth*"], RadarSweepRow.Names);
        Assert.Equal(RadarSweepRow.Stepped, new ShellSettings().RadarSweep);
        Assert.Null(RadarSweepRow.Word(RadarSweepRow.Stepped));   // the default is not written: a file from before reads Stepped
        Assert.Equal("smooth", RadarSweepRow.Word(RadarSweepRow.Smooth));
        Assert.Equal(RadarSweepRow.Stepped, RadarSweepRow.Parse(""));
        Assert.Equal(RadarSweepRow.Stepped, RadarSweepRow.Parse("nonsense"));
        Assert.Equal(RadarSweepRow.Smooth, RadarSweepRow.Parse(" Smooth "));
        var old = ShellSettings.Read(new ShellSettings().Write(null));
        Assert.Equal(RadarSweepRow.Stepped, old.RadarSweep);
        Assert.False(new ShellSettings().Write(null).HasSectionKey("port", "radar_sweep"));
        var on = new ShellSettings { RadarSweep = RadarSweepRow.Smooth };
        var f = on.Write(null);
        Assert.Equal("smooth", f.GetValue("port", "radar_sweep").ToString());
        Assert.Equal(RadarSweepRow.Smooth, ShellSettings.Read(f).RadarSweep);
        // a later save keeps it, and a click goes round
        Assert.Equal(RadarSweepRow.Smooth, ShellSettings.Read(ShellSettings.Read(f).Write(f)).RadarSweep);
        RadarSweepRow.Step(on);
        Assert.Equal(RadarSweepRow.Stepped, on.RadarSweep);
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.RadarSweep, new ShellSettings { RadarSweep = RadarSweepRow.Smooth }, to);
        Assert.Equal(RadarSweepRow.Smooth, to.RadarSweep);
    }

    [Fact]
    public void TheRowIsOnCarAndCockpitShownWithAPackWithHelpInBothLooks()
    {
        Assert.Contains(GraphicsRow.RadarSweep, GraphicsPages.Rows[GraphicsPages.CarCockpit]);
        Assert.Contains(GraphicsRow.RadarSweep, GraphicsPages.PackRows);
        Assert.Contains("Stepped (the default)", GraphicsHelp.Text(GraphicsRow.RadarSweep, false));
        Assert.Contains("1997 look", GraphicsHelp.Text(GraphicsRow.RadarSweep, true));
    }

    [Fact]
    public void AtEachStepsInstantTheSmoothBeamIsTheSteppedFrames()
    {
        var p = RadarSweepParams.Parse(Json8x)!;
        for (int f = 0; f < RadarScope.SweepFrames; f++)
        {
            // the step's instant: the frame shown, the next due a step later
            double t = 3.7 + f * 0.2, next = t + RadarSweepLaw.StepSeconds;
            Assert.Equal(f, RadarSweepLaw.Phase(f, t, next), 9);
            // the art's law (tools/radar-redraw.py frame_angle + the fitted offset): file angle 90 - 12 f + offset
            Assert.Equal(90.0 - 12.0 * f + p.OffsetDeg, RadarSweepLaw.FileAngle(RadarSweepLaw.Phase(f, t, next), p.OffsetDeg), 9);
            Assert.Equal(12.0 * f - p.OffsetDeg, RadarSweepLaw.Bearing(f, p.OffsetDeg), 9);
            // half a step on: half way to the next frame's angle; just before the next step: all but there
            Assert.Equal(90.0 - 12.0 * (f + 0.5) + p.OffsetDeg, RadarSweepLaw.FileAngle(RadarSweepLaw.Phase(f, t + 0.1, next), p.OffsetDeg), 6);
            Assert.True(RadarSweepLaw.Phase(f, next - 1e-9, next) < f + 1);
        }
    }

    /// <summary>The cockpit's stepping (Cockpit.RadarDraw) run over frames of <paramref name="dt"/>: the step's frame, and the smooth phase read from it.</summary>
    private static IEnumerable<(double T, int Cur, bool Stepped, double Phase)> Run(Func<int, double> dt, int frames)
    {
        double t = 0, next = 0;
        int cur = 0;
        for (int i = 0; i < frames; i++)
        {
            t += dt(i);
            bool stepped = false;
            if (!(t < next))
                while (next < t) { next += 0.2; cur = RadarScope.NextSweep(cur); stepped = true; }
            yield return (t, cur, stepped, RadarSweepLaw.Phase(cur, t, next));
        }
    }

    [Theory]
    [InlineData(60.0, 0.0)]
    [InlineData(144.0, 0.0)]
    [InlineData(60.0, 0.4)]
    [InlineData(24.0, 0.3)]
    public void TheSmoothBeamTurnsAtTheStepsAverageSpeedAndMeetsEveryStep(double fps, double jitter)
    {
        var rng = new Random(7);
        double Dt(int i) => 1.0 / fps * (1 + jitter * (rng.NextDouble() - 0.5));
        var run = Run(Dt, (int)(fps * 60)).ToList();
        double turned = 0;
        for (int i = 1; i < run.Count; i++)
        {
            var (t, cur, stepped, phase) = run[i];
            double frameDt = t - run[i - 1].T;
            // a step shown this frame: the smooth beam is at that frame's angle, or on from it by what this frame's time took it past the step
            Assert.InRange(phase - cur, 0, 1);
            if (stepped) Assert.True(phase - cur < frameDt / RadarSweepLaw.StepSeconds + 1e-9);
            // never backwards, never more than a frame's worth at 60 degrees a second (plus a step where the stepping catches up)
            double d = ((phase - run[i - 1].Phase) % 30 + 30) % 30;
            Assert.InRange(d * 12, 0, 60 * frameDt + 12 + 1e-6);
            turned += d;
        }
        double secs = run[^1].T - run[0].T;
        Assert.InRange(turned * 12 / secs, 59.5, 60.5);   // 60 degrees a second: a turn in 6 s, the original's 30 steps of 0.2 s
    }

    [Fact]
    public void PausedTheBeamStaysPut()
    {
        // the cockpit's clock is its process time: paused, no frame is processed and the phase is read from the same clock
        double t = 12.34, next = 12.4;
        Assert.Equal(RadarSweepLaw.Phase(7, t, next), RadarSweepLaw.Phase(7, t, next));
        Assert.Equal(7 + (12.34 - 12.2) / 0.2, RadarSweepLaw.Phase(7, t, next), 9);
    }

    [Fact]
    public void TheConesBearingIsTheOneTheSteppedFrameTruncates()
    {
        var rng = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            float x = (float)(rng.NextDouble() * 1200 - 600), z = (float)(rng.NextDouble() * 1200 - 600);
            double b = RadarSweepLaw.TargetBearing(x, z);
            Assert.InRange(b, 0, 360);
            int frame = RadarScope.LockedFrame(x, z);
            // the stepped cone points at 12 f, the smooth one at the bearing itself, which lies within that frame's 12 degrees
            Assert.InRange(b - 12 * frame, -1e-3, 12 + 1e-3);
            Assert.Equal(90 - b, RadarSweepLaw.ConeFileAngle(b), 9);
        }
        Assert.Equal(90, RadarSweepLaw.TargetBearing(10, 0), 6);   // dead right: a quarter turn clockwise
        Assert.Equal(0, RadarSweepLaw.TargetBearing(0, 10), 6);
    }

    [Fact]
    public void TheConeEasesTheShortWayRoundAndSettles()
    {
        double b = 350;
        for (int i = 0; i < 6; i++) b = RadarSweepLaw.Ease(b, 10, 1.0 / 60);
        Assert.True(b > 350 || b < 10, $"{b}");                // across north, not back round through 180
        for (int i = 0; i < 60; i++) b = RadarSweepLaw.Ease(b, 10, 1.0 / 60);
        Assert.Equal(10, b, 3);                                  // settled within a second
        Assert.Equal(100, RadarSweepLaw.Ease(100, 200, 0), 9);   // no time: no move
    }

    [Fact]
    public void ZradsJsonIsReadAndAFileThisBuildCannotDrawIsRefused()
    {
        var p = RadarSweepParams.Parse(Json8x)!;
        Assert.Equal((8, 2048, 1024), (p.Scale, p.W, p.H));
        Assert.Equal(new Godot.Rect2I(336, 80, 896, 904), p.Box);
        Assert.Equal(1.078f, p.OffsetDeg);
        Assert.Equal(54.293f, p.FaceRadius);
        Assert.Equal(new Godot.Vector3(150, 245, 150), p.ConeAxisColour);
        Assert.Null(p.ConeRimColour);
        Assert.Null(RadarSweepParams.Parse(Json8x.Replace("\"format\": 1", "\"format\": 2")));
        Assert.Null(RadarSweepParams.Parse(Json8x.Replace("\"step_s\": 0.2", "\"step_s\": 0.25")));   // not the frames' clock
        Assert.Null(RadarSweepParams.Parse(Json8x.Replace("[2048, 1024]", "[2048, 1000]")));
        Assert.Null(RadarSweepParams.Parse(Json8x.Replace("\"offset_deg\": 1.078, ", "")));
        Assert.Null(RadarSweepParams.Parse("{"));
        // the kernel's source carries the numbers, every placeholder filled
        var k = p.Kernel();
        Assert.Contains("const float S = 8.0;", k);
        Assert.Contains("const float FR = 54.293;", k);
        Assert.DoesNotContain("{{", k);
        Assert.Contains("smooth3(", k);   // the cone's soft edge (style B)
    }
}
