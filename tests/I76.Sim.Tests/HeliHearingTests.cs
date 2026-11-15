using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Sound;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// A helicopter's rotor loop heard far ([port] helicopter_hearing=far, docs/deviations.md): the three bands' levels, the shelf that
/// leaves a far one its thump, the range the loop is parked at; the original's car-engine voice unchanged; and Nitro P13's hidden
/// helicopter, whose loop the script stops at 0 s, silent for good.
/// </summary>
public class HeliHearingTests
{
    private static readonly HeliHearing H = HeliHearing.Default;

    [Fact]
    public void Overhead_is_loud_medium_is_clear_and_far_is_faint()
    {
        // overhead: the helicopters fly 10 to 115 m over the player; full inside 50 m, 10 dB over the original's there
        Assert.Equal(-4f, H.Db(0f));
        Assert.Equal(-4f, H.Db(50f));
        Assert.True(H.Db(50f) - Ds3d.AttenuationDb(50f) >= 9.9f);
        // medium: an attack run's 100 to 600 m, 3 dB and more over the original's, -14 dB at 150 m
        Assert.InRange(H.Db(150f), -14.5f, -13f);
        Assert.InRange(H.Db(400f), -24f, -22f);
        Assert.InRange(H.Db(600f), -28f, -26f);       // the original parks the loop here
        Assert.False(Ds3d.LoopStarts(600f));
        // far: the helicopters waiting 1 to 2 km off, faint
        Assert.InRange(H.Db(1000f), -34f, -32f);
        Assert.InRange(H.Db(1800f), -41f, -40f);
        // beyond: silent, and parked at the range
        Assert.True(H.Db(1999f) < -80f);
        Assert.Equal(float.NegativeInfinity, H.Db(2000f));
        Assert.Equal(float.NegativeInfinity, H.Db(5000f));
        Assert.Equal(float.NegativeInfinity, H.Db(float.NaN));
        Assert.Equal(0f, H.Gain(2500f));
        Assert.True(H.Plays(1999f));
        Assert.False(H.Plays(2000f));
    }

    [Fact]
    public void The_curve_falls_smoothly_all_the_way()
    {
        float last = H.Db(0f);
        for (float d = 1f; d < 1800f; d += 1f)
        {
            float db = H.Db(d);
            Assert.True(db <= last, $"{d} m");
            Assert.True(last - db < 0.5f, $"a step at {d} m");   // no kink a pass would hear as a jump
            last = db;
        }
    }

    [Fact]
    public void The_highs_go_first_with_distance()
    {
        Assert.Equal(0f, H.ShelfDb(40f));
        Assert.Equal(0f, H.ShelfDb(150f));
        Assert.InRange(H.ShelfDb(400f), -8f, -6f);
        Assert.Equal(-18f, H.ShelfDb(1800f), 3);
        Assert.Equal(-18f, H.ShelfDb(3000f), 3);
        Assert.Equal(500f, H.ShelfHz);
    }

    /// <summary>Godot scales its filter's dB by one less the voice's linear volume and squares the shelf's A: the value set gives the cut asked for.</summary>
    [Theory]
    [InlineData(-7f, -30f)]
    [InlineData(-18f, -46f)]
    [InlineData(-3f, -12f)]
    public void Godots_filter_is_set_for_the_cut_asked(float cut, float volumeDb)
    {
        float set = HeliHearing.GodotFilterDb(cut, volumeDb);
        float lin = MathF.Pow(10f, volumeDb / 20f);
        float dbAtt = (1f - lin) * set;                       // audio_stream_player_3d.cpp
        Assert.Equal(cut, 2f * dbAtt, 3);                     // the high shelf's gain at the top is A²
        Assert.Equal(0f, HeliHearing.GodotFilterDb(0f, -20f));
        Assert.Equal(0f, HeliHearing.GodotFilterDb(-10f, 0f));   // a voice at full volume: Godot applies none
    }

    [Fact]
    public void Only_a_helicopters_rotor_loop_is_heard_far()
    {
        Assert.True(HeliHearing.IsRotorLoop(true, "aheli.wav"));
        Assert.True(HeliHearing.IsRotorLoop(true, "AHELI.WAV"));
        Assert.False(HeliHearing.IsRotorLoop(true, "vballoon.wav"));   // the balloon's burner
        Assert.False(HeliHearing.IsRotorLoop(true, "vufo.wav"));
        Assert.False(HeliHearing.IsRotorLoop(false, "aheli.wav"));     // a car given the helicopter's row
        Assert.False(HeliHearing.IsRotorLoop(false, "eitank.wav"));
        Assert.False(HeliHearing.IsRotorLoop(true, null));
    }

    [Fact]
    public void The_original_rules_are_unchanged()
    {
        Assert.Equal(0f, Ds3d.AttenuationDb(10f));
        Assert.Equal(-20f, Ds3d.AttenuationDb(100f), 3);
        Assert.Equal(-32f, Ds3d.AttenuationDb(400f), 1);
        Assert.Equal(-32f, Ds3d.AttenuationDb(2000f), 1);
        Assert.True(Ds3d.LoopStarts(599f));
        Assert.False(Ds3d.LoopStarts(600f));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("nonsense", null)]
    [InlineData(",,,,2800,3000", 3000f)]
    [InlineData("40,-2", 2000f)]
    [InlineData(",,,,2500,2000", null)]   // the fade past the range
    [InlineData("1,2,3,4,5,6,7,8,9,10", null)]
    public void The_env_hook_reads_its_fields(string text, float? range)
    {
        var h = HeliHearing.Parse(text);
        Assert.Equal(range, h?.RangeMetres);
        if (text == "40,-2") { Assert.Equal(40f, h!.NearMetres); Assert.Equal(-2f, h.NearDb); Assert.Equal(H.Rolloff, h.Rolloff); }
    }

    /// <summary>
    /// Nitro P13: the script hides its helicopter (vuchuey2) at 0 s, which stops its engine, and nothing turns it on again (the
    /// helicopter's tick runs no ignition): no rotor loop to hear at any range, nor any thump. Needs I76_NITRO_INSTALL.
    /// </summary>
    [Fact]
    public void P13s_hidden_helicopter_stays_silent()
    {
        if (Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is not { Length: > 0 } dir || !Directory.Exists(dir)) return;
        var install = new GameInstall(dir);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var w = new MissionWorld(vfs, asm, install.FindMission("p13")!);
        var setup = MissionSetup.Build(vfs, w, asm, null, melee: null, rand: new I76.Sim.Rng.CrtRand(1));
        var session = new SimSession(setup);
        if (setup.World.Player is { } p) session.AddDriver(p);
        var heli = setup.Entities.Single(e => e.Vehicle is { IsHeli: true });
        Assert.Equal("vuchuey2", heli.Name);
        for (int f = 0; f < (int)(120 / session.TickSeconds); f++)
        {
            session.Tick([new CarInput()]);
            Assert.True((heli.Vehicle!.Flags & I76.Sim.Vehicles.VehicleFlags.EngineOn) == 0, $"engine on at {f * session.TickSeconds:0.0} s");
        }
    }
}
