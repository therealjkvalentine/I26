using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Haze, Glow and Reflections (AirRows): Off by default and in a file without their keys, written only when on and
/// read back, clamped; each click the next value round to Off; Reflections only with Ambient Occlusion On; the added haze nothing
/// to 600 m and rising toward its share. Without the engine.
/// </summary>
public class AirRowsTests
{
    [Fact]
    public void AllThreeAreOffByDefault()
    {
        var s = new ShellSettings();
        Assert.Equal(0, s.Haze);
        Assert.Equal(0, s.Glow);
        Assert.False(s.Reflections);
        Assert.False(AirRows.ReflectionsActive(s));
        Assert.Equal("Off", AirRows.HazeNames[s.Haze]);
        Assert.Equal("Off", AirRows.GlowNames[s.Glow]);
    }

    /// <summary>A file from before the rows (no keys) reads Off, and Off writes no key: the file stays as it was.</summary>
    [Fact]
    public void OffWritesNoKeyAndAnOldFileReadsOff()
    {
        var f = new ShellSettings().Write(null);
        Assert.False(f.HasSectionKey("port", "haze"));
        Assert.False(f.HasSectionKey("port", "glow"));
        Assert.False(f.HasSectionKey("port", "reflections"));
        var back = ShellSettings.Read(f);
        Assert.Equal((0, 0, false), (back.Haze, back.Glow, back.Reflections));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 0, false)]
    [InlineData(0, 1, true)]
    public void AChoiceIsSavedAndReadBack(int haze, int glow, bool reflections)
    {
        var s = new ShellSettings { Haze = haze, Glow = glow, Reflections = reflections, AmbientOcclusion = true };
        var back = ShellSettings.Read(s.Write(null));
        Assert.Equal((haze, glow, reflections), (back.Haze, back.Glow, back.Reflections));
        // and again through a second save over the first (the shell saves over its own file)
        var again = ShellSettings.Read(back.Write(s.Write(null)));
        Assert.Equal((haze, glow, reflections), (again.Haze, again.Glow, again.Reflections));
    }

    [Fact]
    public void AValueOutOfRangeInTheFileIsClamped()
    {
        var f = new ShellSettings().Write(null);
        f.SetValue("port", "haze", 7);
        f.SetValue("port", "glow", -3);
        var s = ShellSettings.Read(f);
        Assert.Equal(2, s.Haze);
        Assert.Equal(0, s.Glow);
    }

    [Fact]
    public void ClicksCycleRoundToOff()
    {
        var s = new ShellSettings();
        var seen = new List<string>();
        for (int i = 0; i < 4; i++) { seen.Add(AirRows.HazeNames[s.Haze]); AirRows.StepHaze(s); }
        Assert.Equal(["Off", "Light*", "Southwest*", "Off"], seen);
        seen.Clear();
        for (int i = 0; i < 3; i++) { seen.Add(AirRows.GlowNames[s.Glow]); AirRows.StepGlow(s); }
        Assert.Equal(["Off", "Lamps*", "Off"], seen);
    }

    /// <summary>Reflections is offered only with Ambient Occlusion On: the row says so and a click does nothing; with it On the row cycles, and turning AO Off keeps the choice but draws none.</summary>
    [Fact]
    public void ReflectionsNeedAmbientOcclusion()
    {
        var s = new ShellSettings();
        Assert.Equal(AirRows.NeedsAo, AirRows.ReflectionWord(s));
        AirRows.StepReflections(s);
        Assert.False(s.Reflections);
        s.AmbientOcclusion = true;
        Assert.Equal("Off", AirRows.ReflectionWord(s));
        AirRows.StepReflections(s);
        Assert.True(s.Reflections);
        Assert.Equal("On*", AirRows.ReflectionWord(s));
        Assert.True(AirRows.ReflectionsActive(s));
        s.AmbientOcclusion = false;
        Assert.True(s.Reflections);
        Assert.False(AirRows.ReflectionsActive(s));
        Assert.Equal(AirRows.NeedsAo, AirRows.ReflectionWord(s));
        AirRows.StepReflections(s);
        Assert.True(s.Reflections);
    }

    /// <summary>The rows are Effects', Reflections under Ambient Occlusion; Reset on Effects puts all three back Off.</summary>
    [Fact]
    public void TheRowsAreEffectsAndResetTurnsThemOff()
    {
        var adv = GraphicsPages.Rows[GraphicsPages.Effects];
        foreach (var r in new[] { GraphicsRow.Haze, GraphicsRow.Glow, GraphicsRow.Reflections }) Assert.Equal(GraphicsPages.Effects, GraphicsPages.PageOf(r));
        Assert.Equal(Array.IndexOf(adv, GraphicsRow.AmbientOcclusion) + 1, Array.IndexOf(adv, GraphicsRow.Reflections));
        var s = new ShellSettings { Haze = 2, Glow = 1, Reflections = true, AmbientOcclusion = true };
        var snap = GraphicsPages.CopyAll(s, new ShellSettings());
        Assert.Equal((2, 1, true), (snap.Haze, snap.Glow, snap.Reflections));
        GraphicsPages.Reset(s, GraphicsPages.Effects);
        Assert.Equal((0, 0, false), (s.Haze, s.Glow, s.Reflections));
        GraphicsPages.Reset(s, GraphicsRow.Haze);
        Assert.Equal(0, s.Haze);
    }

    /// <summary>The added haze: none to 600 m (the cockpit, the car ahead, the near ground), then rising, Southwest over Light, never past its share.</summary>
    [Fact]
    public void TheHazeStartsAt600MetresAndRises()
    {
        Assert.Equal(0f, AirRows.HazeAt(0, 5000f));
        foreach (int level in new[] { 1, 2 })
        {
            Assert.Equal(0f, AirRows.HazeAt(level, 100f));
            Assert.Equal(0f, AirRows.HazeAt(level, AirRows.HazeStart));
            float prev = 0f;
            foreach (float d in new[] { 1000f, 2000f, 5000f, 10000f, 20000f })
            {
                float h = AirRows.HazeAt(level, d);
                Assert.True(h > prev, $"level {level} at {d} m");
                Assert.True(h < AirRows.Haze(level).Amount);
                prev = h;
            }
        }
        Assert.True(AirRows.HazeAt(2, 2000f) > AirRows.HazeAt(1, 2000f));
        // slight at the map's edge: under a sixth at 2 km on Southwest
        Assert.InRange(AirRows.HazeAt(2, 2000f), 0.05f, 0.17f);
    }

    [Fact]
    public void TheTooltipsSayOffByDefaultAndTheAoRule()
    {
        Assert.Contains("Ambient Occlusion", GraphicsHelp.Text(GraphicsRow.Reflections, false));
        Assert.Contains("600 m", GraphicsHelp.Text(GraphicsRow.Haze, false));
        Assert.Contains("dash", GraphicsHelp.Text(GraphicsRow.Glow, false));
        foreach (var r in new[] { GraphicsRow.Haze, GraphicsRow.Glow, GraphicsRow.Reflections })
            Assert.Contains("No cost in the 1997 look", GraphicsHelp.Text(r, true));
    }
}
