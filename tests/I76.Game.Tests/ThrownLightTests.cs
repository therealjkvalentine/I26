using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The thrown light ([port] thrown_light, ThrownLight): Original, the default, throws light as the original does: only in a night period (the
/// dawn and dusk presets are daylight periods), the rear lamps none, and the flashes, explosions and fires on objects, not the ground;
/// by day a flash only a trace, alike under every Lighting choice. Bright is the light as before the key. Without the engine.
/// </summary>
public class ThrownLightTests
{
    // the presets' daylight (Daylight.Of): full sun (M01, T10's 21:00), dawn (T16's 06:00, intensity 0.625), night
    private const float Day = 1f, Dawn = 0.625f, Night = 0f;
    // exposures as the game logged them by day: Sunlit at M01, T10 and T16; Lighting 1997 at M01; Off
    private static readonly float[] DayExposures = [0.297f, 0.102f, 0.324f, 0.601f, 1f];

    [Fact]
    public void OriginalIsTheDefaultAndBrightIsWrittenOnlyWhenChosen()
    {
        var s = new ShellSettings();
        Assert.False(s.ThrownLightBright);
        Assert.False(s.Write(null).HasSectionKey("port", "thrown_light"));
        var file = new ShellSettings { ThrownLightBright = true }.Write(null);
        Assert.Equal("bright", file.GetValue("port", "thrown_light").ToString());
        Assert.True(ShellSettings.Read(file).ThrownLightBright);
        Assert.Equal("thrown_light", Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "thrown_light").Key);
        Assert.False(string.IsNullOrEmpty(I76.Game.Ui.DevTips.Of("thrown_light")));
    }

    [Fact]
    public void ByDayTheLampsThrowNothingAndAtNightAsBefore()
    {
        foreach (float d in new[] { Day, Dawn, 0.01f })
        {
            Assert.Equal(0f, ThrownLight.Lamp(d, LampStrength.HeadDayFloor, bright: false));
            Assert.False(ThrownLight.Rear(bright: false));
        }
        Assert.Equal(1f, ThrownLight.Lamp(Night, LampStrength.HeadDayFloor, bright: false));
        // Bright: as before the row (a fifth in full sun, between by the daylight)
        foreach (float d in new[] { Day, Dawn, Night })
            Assert.Equal(Daylight.Scale(d, LampStrength.HeadDayFloor), ThrownLight.Lamp(d, LampStrength.HeadDayFloor, bright: true));
        Assert.True(ThrownLight.Rear(bright: true));
    }

    [Fact]
    public void ByDayAFlashIsATraceAlikeUnderEveryLightingChoice()
    {
        // on the screen a light is its energy times the exposure: by day the same trace under every choice
        float want = ThrownLight.DayTrace * LampStrength.ReferenceExposure;
        foreach (float e in DayExposures)
            Assert.Equal(want, ThrownLight.Effect(Day, e, bright: false) * e, 4);
        Assert.True(want < 0.1f);   // barely visible: a tenth of its night strength on the screen, on objects only
        // at night every choice's exposure is 1: full strength
        Assert.Equal(1f, ThrownLight.Effect(Night, 1f, bright: false));
        // Bright: as before, the energy as designed
        foreach (float e in DayExposures) Assert.Equal(1f, ThrownLight.Effect(Day, e, bright: true));
        // the muzzle flash: Bright keeps its day floor through the daylight; Original as the other flashes
        Assert.Equal(Daylight.Scale(Dawn, MuzzleReach.DayFloor), ThrownLight.Muzzle(Dawn, MuzzleReach.DayFloor, 0.3f, bright: true));
        Assert.Equal(ThrownLight.Effect(Dawn, 0.3f, false), ThrownLight.Muzzle(Dawn, MuzzleReach.DayFloor, 0.3f, bright: false));
    }

    [Fact]
    public void OriginalsFlashesLightNoGround()
    {
        uint ground = I76.Game.World.TerrainNode.TerrainLayer, all = 0xFFFFFu;
        Assert.Equal(0u, ThrownLight.Mask(all, bright: false) & ground);
        Assert.Equal(all & ~ground, ThrownLight.Mask(all, bright: false));
        Assert.Equal(all, ThrownLight.Mask(all, bright: true));
    }

    [Fact]
    public void EveryEffectLightGoesThroughTheRule()
    {
        static string Src(params string[] p)
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine([dir, "game", "Scripts", .. p]))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine([dir!, "game", "Scripts", .. p]));
        }
        foreach (var f in new[] { "WreckFire.cs", "StaticFire.cs", "FireDrops.cs", "NapalmModern.cs", "ModernFx.cs", "CombatView.cs" })
            Assert.Contains("ThrownLight.", Src("Sim", f));
        Assert.Contains("ThrownLight.", Src("World", "BrushImpacts.cs"));
        string car = Src("Sim", "CarNode.cs");
        Assert.Contains("ThrownLight.Lamp(", car);
        Assert.Contains("ThrownLight.Rear(", car);
    }
}
