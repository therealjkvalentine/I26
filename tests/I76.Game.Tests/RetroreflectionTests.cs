using System.Globalization;
using System.Text.RegularExpressions;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's road signs at night: retroreflective sheeting (Retroreflection, object.gdshaderinc's retro_light). A sign face
/// is bright only while a lit headlamp's beam reaches it (inside the beam's cone and range, the face turned toward the lamp), most
/// to an eye beside that lamp; next to nothing from another car's lamps; and otherwise lit as its neighbours, never glowing of its
/// own (no night_glow), as the owner asked ("reflective signs should only be reflective when some headlights in path").
/// </summary>
public class RetroreflectionTests
{
    private static readonly float Edge = MathF.Cos(42f * MathF.PI / 180f);   // the modern look's headlamp cone (CarNode.HeadEdgeCos)
    private static readonly float Range = LampStrength.HeadlampRange(HeadlampThrow.Default);    // the default throw's range, 147 m

    // a sign straight down the beam at d metres, the eye `offset` metres from the lamp across the line of sight
    private static float Ahead(float d, float offset = 1f, float? range = null) => Retroreflection.Factor(d, MathF.Atan2(offset, d), 1f, 1f, Edge, range ?? Range);

    [Fact]
    public void InTheBeamTheDriversOwnLampsMakeItBright()
    {
        // the driver's eye a metre or so from the lamps: close in the sheeting returns most of its best, and still a good share near the range
        Assert.InRange(Ahead(20), 0.55f, 0.7f);
        Assert.InRange(Ahead(50), 0.7f, 0.8f);
        Assert.True(Ahead(100) > 0.5f);
        // far less falloff than a lamp's light on a diffuse face (1/d², 25 times from 20 to 100 m): not twice
        Assert.True(Ahead(100) / Ahead(20) > 0.5f);
    }

    [Fact]
    public void PastTheBeamsRangeNothing()
    {
        // the same range the headlamp's light on the road ends at (LampStrength.HeadlampRange), fading over its last fifth
        Assert.Equal(0f, Ahead(Range));
        Assert.Equal(0f, Ahead(Range + 50));
        Assert.Equal(0f, Ahead(300));
        Assert.True(Ahead(Range * 0.9f) < 0.6f * Ahead(Range * 0.75f));
        // a longer throw reaches further
        float far = LampStrength.HeadlampRange(150);
        Assert.Equal(0f, Ahead(170));
        Assert.True(Ahead(170, range: far) > 0.3f);
    }

    [Fact]
    public void OutsideTheBeamsConeNothingAndOffItsHotSpotDimmer()
    {
        float Off(float deg) => Retroreflection.Factor(60, 0.005f, 1f, MathF.Cos(deg * MathF.PI / 180f), Edge, Range);
        Assert.True(Off(12) < 0.6f * Off(0));
        Assert.True(Off(20) < 0.2f * Off(0));
        Assert.Equal(0f, Off(45));
        // behind the car (the lamps facing away from the sign)
        Assert.Equal(0f, Off(180));
        Assert.Equal(0f, Off(90));
    }

    [Fact]
    public void AFaceTurnedFromTheLampSendsNothingAndTheEntranceAngleMattersMildly()
    {
        float At(float deg) => Retroreflection.Factor(60, 0.005f, MathF.Cos(deg * MathF.PI / 180f), 1f, Edge, Range);
        Assert.True(At(45) > 0.8f * At(0));
        Assert.True(At(70) > 0.5f * At(0));
        Assert.Equal(0f, At(90));
        Assert.Equal(0f, Retroreflection.Factor(60, 0.005f, -0.5f, 1f, Edge, Range));
    }

    [Fact]
    public void AnotherCarsLampsHardlyBrightenItForUs()
    {
        // the lamp 10 m across from our eye (a car in the next lane, or oncoming) lights a sign 50 m off: its return goes back to it
        Assert.True(Ahead(50, 10f) < 0.01f * Ahead(50));
        // the chase camera, a few metres behind and above the lamps, still sees most of it far off
        Assert.True(Ahead(100, 4f) > 0.6f * Ahead(100));
    }

    private static string Source(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Shaders", "object.gdshaderinc"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    [Fact]
    public void TheShaderUsesTheSameRule()
    {
        var shader = Source("game", "Shaders", "object.gdshaderinc");
        float Const(string name) => float.Parse(Regex.Match(shader, $@"const float {name} = ([0-9.]+);").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(Retroreflection.Strength, Const("RETRO_STRENGTH"));
        Assert.Equal(Retroreflection.Hot, Const("RETRO_HOT"));
        Assert.Equal(Retroreflection.Lobe, Const("RETRO_LOBE"));
        Assert.Equal(Retroreflection.Reach, Const("RETRO_REACH"));
        Assert.Equal(Retroreflection.Fade, Const("RETRO_FADE"));
        // the lamp's range is the beam's (i76_retro_at's w), and nothing past it
        Assert.Contains("if (d >= a.w) continue;", shader);
        Assert.Contains("smoothstep(a.w * RETRO_FADE, a.w, d)", shader);
        // the modern look's night only, front faces only; the 1997 look's shader (FAITHFUL) never reads it
        Assert.Contains("if (retro && i76_night && i76_retro_count > 0 && FRONT_FACING)", shader);
    }

    [Fact]
    public void OnlyLitHeadlampsAreSentAtTheirBeamsRange()
    {
        // lamps off (or a car without lamps lit): nothing sent, so nothing sent back; tail and brake lamps are not headlamps
        var car = Source("game", "Scripts", "Sim", "CarNode.cs");
        var lit = Regex.Match(car, @"LitHeadlamps\(\)\s*\{(.*?)\n    \}", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("if (!c._lampsOn || !c.IsInsideTree()) continue;", lit);
        Assert.Contains("c._lamps[i]", lit);
        Assert.Contains("_lampsOn = headLit;", car);
        var lamps = Source("game", "Scripts", "World", "RetroLamps.cs");
        Assert.Contains("float range = LampStrength.HeadlampRange();", lamps);
        // and the node is made at night (Main.AddDropShadows, beside NightLights)
        Assert.Contains("AddChild(new RetroLamps { Name = \"RetroLamps\", Lamps = CarNode.LitHeadlamps });", Source("game", "Scripts", "Boot", "Main.cs"));
    }

    [Fact]
    public void ASignsFaceNeverGlowsOfItsOwn()
    {
        // without a lamp on it the face is lit as its neighbours: a sign's material takes no night_glow (the station's lit faces keep it)
        var lib = Source("game", "Scripts", "Assets", "MaterialLibrary.cs");
        Assert.Contains("if (unlit && car is null && mode != FaceMode.Translucent && !retro) mat.SetShaderParameter(\"night_glow\", true);", lib);
        Assert.Contains("if (prop && !retro) mat.SetShaderParameter(\"night_glow\", true);", lib);
        Assert.Equal(2, Regex.Matches(lib, @"SetShaderParameter\(""night_glow"", true\)").Count);
        // every sign's parts (type-4 nodes, whatever the placement's class id) are marked, whatever the hook says; the hook only leaves
        // the sheeting out, and a sign part's faces never take the structures' own glow
        var spawner = Source("game", "Scripts", "World", "ScenerySpawner.cs");
        Assert.Contains("bool retro = !roadPiece;", spawner);
        Assert.Contains("signPart: signPart", spawner);
        Assert.Contains("bool shines = selfLit && !sign && SelfLit(f, faceN.Y);", Source("game", "Scripts", "Assets", "MeshBuilder.cs"));
    }

    [Fact]
    public void TheHookIsOnUnlessZero()
    {
        // I76_RETRO_SIGNS=0 for before and after captures; on otherwise
        Assert.Equal(System.Environment.GetEnvironmentVariable("I76_RETRO_SIGNS") != "0", Retroreflection.On);
    }
}
