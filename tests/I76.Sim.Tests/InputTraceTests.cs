using I76.Formats.Assembly;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The input traces of I76_INPUT_RECORD / I76_INPUT and <c>i76 run --record / --replay</c> (docs/dev-hooks.md).</summary>
public class InputTraceTests
{
    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private static InputTrace RoundTrip(InputTrace t)
    {
        var w = new StringWriter();
        t.Write(w);
        return InputTrace.Read(new StringReader(w.ToString()));
    }

    [Fact]
    public void ATraceRoundTripsThroughItsCsv()
    {
        var t = new InputTrace { Mission = "m01", Car = "vppirna1.vcf", Seed = 4000000000u, Rate = 30, Melee = ["valepre1.vcf", "vcgcour1.vcf"] };
        t.Add(default, 0x0123456789abcdefUL);
        t.Add(new CarInput { Buttons = CarButtons.ThrottleUp | CarButtons.Fire | CarButtons.Analog, Commands = CarCommands.ResetUpright | CarCommands.Skip, Steer = -32767, Throttle = 32767 });
        t.Add(new CarInput { Buttons = (CarButtons)0xffff, Commands = (CarCommands)0x0fffffff, Steer = short.MinValue, Throttle = -1 }, ulong.MaxValue);
        var back = RoundTrip(t);
        Assert.Equal((t.Mission, t.Car, t.Seed, t.Rate), (back.Mission, back.Car, back.Seed, back.Rate));
        Assert.Equal(t.Melee, back.Melee);
        Assert.Equal(t.Inputs, back.Inputs);
        Assert.Equal(t.Hashes, back.Hashes);
        Assert.Equal(default, back.At(3));   // past the end nothing is held
    }

    [Fact]
    public void TheHeaderNamesWhatElseReachesTheSimAndAReplaySaysWhatDiffers()
    {
        var t = new InputTrace
        {
            Mission = "t01", Seed = 7, Build = "0.11.0 play", Install = 0x00c0ffee12345678UL, Options = 0x1c, Difficulty = 2, ModernPhysics = true, Ai = "script",
        };
        var line = t.HeaderLine();
        Assert.Contains("build=0.11.0_play install=00c0ffee12345678 options=0x1c difficulty=2 physics=modern ai=script", line);
        var back = RoundTrip(t);
        Assert.Equal(("0.11.0_play", 0x00c0ffee12345678UL, 0x1c, 2, true, "script"), (back.Build, back.Install!.Value, back.Options!.Value, back.Difficulty!.Value, back.ModernPhysics!.Value, back.Ai));
        Assert.Empty(back.Differences(t));
        var now = new InputTrace { Mission = "t01", Seed = 7, Build = "0.11.0 play", Install = 0x00c0ffee12345678UL, Options = 0x1c, Difficulty = 1, ModernPhysics = false, Ai = "script" };
        Assert.Equal(["Difficulty 2 (now 1)", "physics modern (now original)"], back.Differences(now));
        // an older recording names none of them: nothing to compare, nothing said
        var old = InputTrace.Read(new StringReader("# I76_INPUT 1 mission=t01 car= seed=7 rate=20\ntick,buttons,commands,steer,throttle,hash\n0,0x0,0x0,0,0,\n"));
        Assert.Null(old.Build);
        Assert.Empty(old.Differences(now));
    }

    [Fact]
    public void ADeveloperPanelActionIsARowOfItsOwnThatAReplayKnows()
    {
        var t = new InputTrace { Mission = "t01" };
        t.Add(default); t.Add(default); t.Add(default);
        t.Dev.Add((2, "restored tick 300"));
        var w = new StringWriter();
        t.Write(w);
        var lines = w.ToString().Replace("\r", "").Split('\n');
        Assert.Equal("# dev restored tick 2", lines[4]);   // before tick 2's row
        Assert.StartsWith("2,", lines[5]);
        var back = InputTrace.Read(new StringReader(w.ToString()));
        Assert.Equal([(2, "restored")], back.Dev);
        Assert.Equal(3, back.Count);
    }

    /// <summary>A recording is flushed row by row: a run killed mid-mission leaves every tick it ran in the file.</summary>
    [Fact]
    public void TheWriterFlushesEachRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"i26-trace-{Guid.NewGuid():N}.csv");
        try
        {
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var writer = new InputTraceWriter(new StreamWriter(stream));
            writer.Start(new InputTrace { Mission = "t01" });
            writer.Tick(0, new CarInput { Buttons = CarButtons.Fire }, 0xabcUL);
            writer.Dev(1, "teleported to (0, 0, 0)");
            string ReadNow() { using var r = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return r.ReadToEnd(); }
            var text = ReadNow();     // the writer still open, nothing disposed
            var trace = InputTrace.Read(new StringReader(text));
            Assert.Equal(1, trace.Count);
            Assert.Equal(CarButtons.Fire, trace.At(0).Buttons);
            Assert.Equal([(1, "teleported")], trace.Dev);
            writer.Dispose();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheCsvIsAHeaderAColumnLineAndARowATick()
    {
        var t = new InputTrace { Mission = "t01", Seed = 7 };
        t.Add(new CarInput { Buttons = CarButtons.SteerLeft | CarButtons.ThrottleUp, Commands = CarCommands.Lights }, 0xabcUL);
        var w = new StringWriter();
        t.Write(w);
        var lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(["# I76_INPUT 1 mission=t01 car= seed=7 rate=20", "tick,buttons,commands,steer,throttle,hash", "0,0x5,0x2,0,0,0000000000000abc"], lines);
        // a missing tick holds nothing down; decimal fields and an empty hash are read too
        var r = InputTrace.Read(new StringReader("# I76_INPUT 1 mission=t01\ntick,buttons,commands,steer,throttle,hash\n2,1,0,0,0,\n"));
        Assert.Equal(3, r.Count);
        Assert.Equal([default, default, new CarInput { Buttons = CarButtons.ThrottleUp }], r.Inputs);
        Assert.Null(r.Car);
        Assert.Null(r.HashAt(2));
        Assert.Throws<InvalidDataException>(() => InputTrace.Read(new StringReader("# I76_INPUT 2\n")));
        Assert.Throws<InvalidDataException>(() => InputTrace.Read(new StringReader("tick,buttons\n")));
    }

    /// <summary>Scripted driving: throttle, turns, braking, the e-brake, fire, the stick and a few commands.</summary>
    private static CarInput Keys(int t)
    {
        var b = CarButtons.None;
        if (t % 120 < 90) b |= CarButtons.ThrottleUp;
        if (t % 120 is >= 100 and < 110) b |= CarButtons.ThrottleDown;
        if (t % 70 is > 20 and < 40) b |= CarButtons.SteerLeft;
        if (t % 90 is > 50 and < 60) b |= CarButtons.SteerRight;
        if (t % 150 is > 130 and < 135) b |= CarButtons.EBrake;
        if (t % 40 < 15) b |= CarButtons.Fire;
        var c = t % 50 == 25 ? CarCommands.TargetNearest : t == 77 ? CarCommands.WeaponCycle | CarCommands.Lights : CarCommands.None;
        var i = new CarInput { Buttons = b, Commands = c };
        if (t is > 160 and < 190) i = new CarInput { Buttons = CarButtons.Analog | CarButtons.Fire, Steer = (short)(t * 300 - 52000), Throttle = 20000 };
        return i;
    }

    private static (SimSession, MissionSetup) Melee(uint seed, int rate)
    {
        var install = new GameInstall(InstallDir!);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var w = new MissionWorld(vfs, asm, install.FindMission("m01")!);
        var setup = MissionSetup.Build(vfs, w, asm, "valepre1.vcf", melee: ["vppirna1.vcf", "vcgcour1.vcf"], rand: new CrtRand(seed));
        setup.Melee?.Start();
        var s = new SimSession(setup, rate: SimRate.FromLabel(rate));
        s.AddDriver(setup.World.Player!);
        return (s, setup);
    }

    /// <summary>
    /// A 200-tick run with every tick's input and state hash recorded, written out and read back, replays on a fresh
    /// session to the same state hash on every tick; one tick's input changed, the replay leaves it at that tick.
    /// </summary>
    [Fact]
    public void ARecordedRunReplaysToTheSameStateHashEveryTick()
    {
        if (InstallDir is null) return;
        var (a, sa) = Melee(12345, 30);
        var rec = new InputTrace { Mission = "m01", Car = "valepre1.vcf", Seed = 12345, Rate = a.Rate.Label, Melee = ["vppirna1.vcf", "vcgcour1.vcf"] };
        var start = a.World.Player!.Pos;
        for (int t = 0; t < 200; t++)
        {
            var input = Keys(t);
            a.Tick([input]);
            sa.Combat.Events.Clear(); sa.Damage.Events.Clear();
            rec.Add(input, a.StateHash());
        }
        Assert.True(System.Math.Abs(a.World.Player!.Pos.Z - start.Z) + System.Math.Abs(a.World.Player.Pos.X - start.X) > 10, "the scripted keys drove the car");

        int Replay(InputTrace trace)
        {
            var (b, sb) = Melee(trace.Seed, trace.Rate);
            for (int t = 0; t < trace.Count; t++)
            {
                b.Tick([trace.At(t)]);
                sb.Combat.Events.Clear(); sb.Damage.Events.Clear();
                if (b.StateHash() != trace.HashAt(t)) return t;
            }
            return -1;
        }
        var back = RoundTrip(rec);
        Assert.Equal(200, back.Count);
        Assert.Equal(-1, Replay(back));

        back.Inputs[120] = back.Inputs[120] with { Buttons = back.Inputs[120].Buttons ^ CarButtons.SteerRight };
        Assert.Equal(120, Replay(back));
    }
}
