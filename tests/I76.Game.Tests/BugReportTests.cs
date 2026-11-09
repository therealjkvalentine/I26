using System.IO.Compression;
using I76.Formats.Assembly;
using I76.Formats.Tests;
using I76.Formats.Vfs;
using I76.Game.Boot;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Save;
using I76.Sim.Session;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Report a Bug (Boot/BugReport.cs, docs/playing.md "Reporting a bug"): a report has every section and file, nothing personal
/// leaks into its text, the log and input rings keep the last lines, and a report's state reopens at its tick in its car.
/// </summary>
public class BugReportTests
{
    private const string UserName = "somebodyprivate";
    private static readonly string Home = $@"C:\Users\{UserName}";
    private static readonly string UserFolder = $@"C:\Users\{UserName}\AppData\Roaming\I26";
    private const string Install = @"D:\Games\Interstate 76";

    private static BugReport.Data Full(byte[]? snapshot = null, int tick = 0) => new()
    {
        Stamp = new DateTime(2026, 11, 8, 21, 45, 0),
        Build = "I26 0.12.0 (3b80955f)  net 28",
        System = $"os: Windows 10.0.19045\nwindow: 3440x1440\nlog at {UserFolder}\\logs",
        Game = "game: Interstate '76, trip mission\nmission: t05 \"Scene 5\"\nsim: tick 1234 at 20 Hz",
        Car = $"file: vppirna1.vcf\nplace: (1.0, 2.0, 3.0) heading 90 deg\nrequested from {Install}\\addon",
        Settings = "sim rate 20 Hz",
        Packs = $"menu art: HD Menu Pack 1.2 ({UserFolder}/tex-menu/)",
        Network = "",
        Snapshot = snapshot ?? [1, 2, 3, 4],
        SnapshotTick = tick,
        SettingsFile = $"[port]\n\nffb_device=\"{UserName}'s wheel\"\nsim_rate=20\n",
        Screenshot = [0x89, 0x50, 0x4e, 0x47],
        ScreenshotNote = "1920x804 (scaled down from 3440x1440)",
        LogLines = [$"install: {Install}", $"user data: {UserFolder}", $"home: {Home}\\Documents", "renderer: forward_plus"],
        InputLines = ["# I76_INPUT 1 mission=t05 car= seed=1 rate=20", InputTrace.Columns, "1200,0x1,0x0,0,0,"],
        Redact = [(UserFolder, "<user folder>"), (Install, "<install>"), (Home, "<home>"), (UserName, "<user>")],
    };

    private static string Temp()
    {
        string d = Path.Combine(Path.GetTempPath(), "i26-bug-report-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void AReportHasEverySectionAndFile()
    {
        string root = Temp();
        try
        {
            var d = Full();
            var (dir, zip) = BugReport.Write(root, d);
            Assert.Equal(Path.Combine(root, "20261108-214500"), dir);
            Assert.Equal(Path.Combine(root, "i26-bug-20261108-214500.zip"), zip);
            foreach (var f in new[] { BugReport.ReportFile, BugReport.StateFile, BugReport.SettingsFile, BugReport.ScreenshotFile, BugReport.LogFile, BugReport.InputsFile })
                Assert.True(File.Exists(Path.Combine(dir, f)), f);
            string report = File.ReadAllText(Path.Combine(dir, BugReport.ReportFile));
            foreach (var title in BugReport.Sections) Assert.Contains($"== {title} ==", report);
            Assert.Contains("I26 0.12.0 (3b80955f)  net 28", report);
            Assert.Contains("--bug-report-open", report);
            Assert.Contains("the last 4 lines", report);
            Assert.Contains("the last 1 ticks", report);
            Assert.DoesNotContain("(none)", report.Split("== Network ==")[0]);   // every section but the empty Network one has words
            using var z = ZipFile.OpenRead(zip);
            Assert.Equal(6, z.Entries.Count);
            Assert.Equal(d.Snapshot, BugReport.ReadState(zip));
            Assert.Equal(d.Snapshot, BugReport.ReadState(dir));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>The user name, the home folder, the user folder and the install's path appear in no text file of the report, in either slash form.</summary>
    [Fact]
    public void NothingPersonalLeaks()
    {
        string root = Temp();
        try
        {
            var (dir, zip) = BugReport.Write(root, Full());
            foreach (var f in new[] { BugReport.ReportFile, BugReport.SettingsFile, BugReport.LogFile, BugReport.InputsFile })
            {
                string text = File.ReadAllText(Path.Combine(dir, f));
                Assert.DoesNotContain(UserName, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Home, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Home.Replace('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Install, text, StringComparison.OrdinalIgnoreCase);
            }
            string report = File.ReadAllText(Path.Combine(dir, BugReport.ReportFile));
            Assert.Contains("<user folder>/tex-menu/", report);
            Assert.Contains("<install>\\addon", report);
            string log = File.ReadAllText(Path.Combine(dir, BugReport.LogFile));
            Assert.Contains("install: <install>", log);
            Assert.Contains("home: <home>\\Documents", log);
            Assert.Contains("ffb_device=\"<user>'s wheel\"", File.ReadAllText(Path.Combine(dir, BugReport.SettingsFile)));
            using var z = ZipFile.OpenRead(zip);
            using var r = new StreamReader(z.GetEntry(BugReport.LogFile)!.Open());
            Assert.DoesNotContain(UserName, r.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RedactionBlanksPathsAndNamesButNotWords()
    {
        var secrets = new[] { (@"C:\Users\al\AppData\Roaming\I26", "<user folder>"), (@"C:\Users\al", "<home>"), ("al", "<user>") };
        Assert.Equal("at <user folder>/logs and <home>\\x by <user>; all alpha final", BugReport.Redact(@"at C:/Users/al/AppData/Roaming/I26/logs and C:\Users\al\x by al; all alpha final", secrets));
        Assert.Equal("none", BugReport.Redact("none", []));
    }

    [Fact]
    public void TheLogRingKeepsTheLastLines()
    {
        var ring = new LineRing(3);
        Assert.Empty(ring.Lines());
        for (int i = 1; i <= 5; i++) ring.Add($"line {i}");
        Assert.Equal(3, ring.Count);
        Assert.Equal(["line 3", "line 4", "line 5"], ring.Lines());
        Assert.Equal(400, Main.BugReportLog.Lines);
    }

    [Fact]
    public void TheInputRingKeepsTheLastSecondsAndReadsBackAsATrace()
    {
        var header = new InputTrace { Mission = "t05", Seed = 7, Rate = 20 };
        var ring = new InputRing(header, (int)(InputRing.Seconds * 20));
        Assert.Equal(200, ring.Capacity);
        for (int t = 0; t < 500; t++) ring.Add(t, new CarInput { Buttons = t % 2 == 0 ? CarButtons.ThrottleUp : CarButtons.None, Steer = (short)t });
        Assert.Equal(200, ring.Count);
        var rows = ring.Rows();
        Assert.Equal(202, rows.Count);
        Assert.StartsWith("# I76_INPUT 1 mission=t05", rows[0]);
        Assert.Equal(InputTrace.Columns, rows[1]);
        Assert.StartsWith("300,", rows[2]);
        Assert.StartsWith("499,", rows[^1]);
        var trace = InputTrace.Read(new StringReader(string.Join('\n', rows)));
        Assert.Equal(500, trace.Count);
        Assert.Equal(default, trace.At(299));
        Assert.Equal(CarButtons.ThrottleUp, trace.At(300).Buttons);
        Assert.Equal(499, trace.At(499).Steer);
    }

    /// <summary>The bug_report_inputs key: on by default, written only when off.</summary>
    [Fact]
    public void TheInputsKeyIsOnByDefault()
    {
        Assert.True(new I76.Game.Shell.ShellSettings().BugReportInputs);
        Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "bug_report_inputs");
    }

    /// <summary>
    /// The round trip: a session's snapshot in a report, read back from the zip, restored into a mission rebuilt from the
    /// header's build (another rand seed): the tick and the car match, and the sim goes on identically.
    /// </summary>
    [InstallFact]
    public void AReportsStateReopensAtItsTickInItsCar()
    {
        string dir = TestInstall.Dir!;
        var install = new GameInstall(dir);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        const string mission = "t01";
        const string car = "vppirna1.vcf";
        var build = new SessionBuild { Mission = mission, Install = SessionBuild.InstallIdentity(install, mission), Car = car };
        var world = new MissionWorld(vfs, asm, install.FindMission(mission)!);
        static (SimSession, MissionSetup) Make(AssetVfs vfs, MissionWorld world, ModelAssembler asm, SessionBuild b, uint seed)
        {
            var setup = MissionSetup.Build(vfs, world, asm, b.Car, rand: new CrtRand(seed), seed: unchecked((int)seed));
            var s = new SimSession(setup, SessionOptions.SinglePlayer, SimRate.Default) { Build = b };
            foreach (var h in setup.Humans) s.AddDriver(h);
            return (s, setup);
        }
        static CarInput Input(int t) => new() { Buttons = CarButtons.ThrottleUp | (t % 40 < 20 ? CarButtons.SteerLeft : CarButtons.None) };
        var (a, _) = Make(vfs, world, asm, build, 1234);
        for (int t = 0; t < 60; t++) a.Tick([Input(t)]);
        var snap = a.Snapshot();
        string root = Temp();
        try
        {
            var (_, zip) = BugReport.Write(root, Full(snap, a.Ticks));
            var bytes = BugReport.ReadState(zip)!;
            var header = SnapshotHeader.Read(bytes);
            Assert.Equal(mission, header.Build.Mission);
            Assert.Equal(car, header.Build.Car);
            Assert.Equal(60, header.Ticks);
            var (b, _) = Make(vfs, world, asm, header.Build, 999);
            b.Restore(bytes);
            Assert.Equal(a.Ticks, b.Ticks);
            Assert.Equal(a.World.Player!.Definition, b.World.Player!.Definition);
            Assert.Equal(a.World.Player.Pos, b.World.Player.Pos);
            Assert.Equal(a.StateHash(), b.StateHash());
            for (int t = 60; t < 90; t++) { a.Tick([Input(t)]); b.Tick([Input(t)]); }
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        finally { Directory.Delete(root, true); }
    }
}
