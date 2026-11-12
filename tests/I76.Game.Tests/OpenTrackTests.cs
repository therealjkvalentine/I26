using I76.Game;
using Xunit;
using Os = I76.Game.OpenTrack.Os;

namespace I76.Game.Tests;

/// <summary>Starting opentrack from the menus: where it is looked for, the row's state, when a mission starts it and the profile the game writes.</summary>
public class OpenTrackTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    private static Func<string, bool> Files(params string[] paths) =>
        p => paths.Contains(p.Replace('\\', '/'), StringComparer.Ordinal);

    [Fact] public void WindowsLooksInProgramFilesBothKindsThenPerUserThenPath()
    {
        var env = Env(("ProgramFiles(x86)", "C:/PF86"), ("ProgramFiles", "C:/PF"), ("LOCALAPPDATA", "C:/U/AppData/Local"), ("PATH", "C:/tools"));
        var c = OpenTrack.Candidates("", Os.Windows, env, "C:/U").Select(p => p.Replace('\\', '/')).ToList();
        Assert.Equal(["C:/PF86/opentrack/opentrack.exe", "C:/PF/opentrack/opentrack.exe", "C:/U/AppData/Local/Programs/opentrack/opentrack.exe", "C:/tools/opentrack.exe"], c);
        Assert.Equal("C:/PF/opentrack/opentrack.exe", OpenTrack.Find("", Os.Windows, env, "C:/U", Files("C:/PF/opentrack/opentrack.exe"))?.Replace('\\', '/'));
        Assert.Null(OpenTrack.Find("", Os.Windows, env, "C:/U", Files()));                       // not installed: nothing, quietly
    }

    [Fact] public void TheConfiguredPathComesFirstAsAProgramAFolderOrAnApp()
    {
        var env = Env(("ProgramFiles(x86)", "C:/PF86"));
        Assert.Equal("D:/ot/opentrack.exe",
            OpenTrack.Find("D:/ot", Os.Windows, env, "C:/U", Files("D:/ot/opentrack.exe", "C:/PF86/opentrack/opentrack.exe"))?.Replace('\\', '/'));
        Assert.Equal("D:/x/ot.exe", OpenTrack.Find("D:/x/ot.exe", Os.Windows, env, "C:/U", Files("D:/x/ot.exe"))?.Replace('\\', '/'));
        Assert.Equal("/Users/u/Apps/opentrack.app/Contents/MacOS/opentrack",
            OpenTrack.Find("/Users/u/Apps/opentrack.app", Os.MacOS, Env(), "/Users/u", Files("/Users/u/Apps/opentrack.app/Contents/MacOS/opentrack"))?.Replace('\\', '/'));
        // a configured path that is not there falls back to the usual places
        Assert.Equal("C:/PF86/opentrack/opentrack.exe",
            OpenTrack.Find("D:/gone", Os.Windows, env, "C:/U", Files("C:/PF86/opentrack/opentrack.exe"))?.Replace('\\', '/'));
    }

    [Fact] public void MacLooksInApplicationsThenPath()
    {
        var c = OpenTrack.Candidates("", Os.MacOS, Env(("PATH", "/opt/homebrew/bin")), "/Users/u").Select(p => p.Replace('\\', '/')).ToList();
        Assert.Equal(["/Applications/opentrack.app/Contents/MacOS/opentrack", "/Users/u/Applications/opentrack.app/Contents/MacOS/opentrack",
            "/opt/homebrew/bin/opentrack"], c);
    }

    [Fact] public void LinuxLooksOnPathThenTheUsualPlaces()
    {
        var c = OpenTrack.Candidates("", Os.Linux, Env(("PATH", "/usr/bin:/users/u/bin")), "/users/u").Select(p => p.Replace('\\', '/')).ToList();
        Assert.Equal(["/usr/bin/opentrack", "/users/u/bin/opentrack", "/usr/local/bin/opentrack", "/usr/bin/opentrack", "/opt/opentrack/bin/opentrack",
            "/users/u/.local/bin/opentrack"], c);
        Assert.DoesNotContain(c, p => p.EndsWith(".exe"));
    }

    [Theory]
    [InlineData(false, false, false, OpenTrackState.NotInstalled, "Missing")]
    [InlineData(true, false, false, OpenTrackState.Stopped, "Stopped")]
    [InlineData(true, true, false, OpenTrackState.Running, "Running")]
    [InlineData(false, true, false, OpenTrackState.Running, "Running")]      // running from somewhere the game does not look
    [InlineData(true, true, true, OpenTrackState.Receiving, "Tracking")]
    [InlineData(false, false, true, OpenTrackState.Receiving, "Tracking")]  // a tracker on another machine
    public void TheRowsStateIsReceivingThenRunningThenFound(bool found, bool running, bool receiving, OpenTrackState want, string label)
    {
        Assert.Equal(want, OpenTrack.StateOf(found, running, receiving));
        Assert.Equal(label, OpenTrack.Label(want));
    }

    [Fact] public void AMissionStartsOpentrackOnlyWhenAskedTrackingIsOnAndItIsStopped()
    {
        Assert.True(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Analog, OpenTrackState.Stopped));
        Assert.True(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Digital, OpenTrackState.Stopped));
        Assert.False(OpenTrack.ShouldAutoStart(false, HeadTrackMode.Analog, OpenTrackState.Stopped));      // Opentrack Auto off
        Assert.False(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Off, OpenTrackState.Stopped));          // Head Tracking off
        Assert.False(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Analog, OpenTrackState.Running));       // the player's own runs
        Assert.False(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Analog, OpenTrackState.Receiving));     // poses already come
        Assert.False(OpenTrack.ShouldAutoStart(true, HeadTrackMode.Analog, OpenTrackState.NotInstalled));
    }

    [Fact] public void TheProfileFolderIsDocumentsOrPortableOrLinuxsConfig()
    {
        Func<string, bool> dirs(params string[] d) => p => d.Contains(p.Replace('\\', '/'));
        Assert.Equal("C:/U/Documents/opentrack-2.3", OpenTrack.ProfileDir(Os.Windows, "C:/PF86/opentrack/opentrack.exe", "C:/U/Documents", "",
            dirs("C:/U/Documents/opentrack-2.3"), Files())?.Replace('\\', '/'));
        Assert.Equal("D:/ot/ini", OpenTrack.ProfileDir(Os.Windows, "D:/ot/opentrack.exe", "C:/U/Documents", "",
            dirs("C:/U/Documents/opentrack-2.3", "D:/ot/ini"), Files("D:/ot/portable.txt"))?.Replace('\\', '/'));
        Assert.Equal("/users/u/.config/opentrack-2.3", OpenTrack.ProfileDir(Os.Linux, null, "/users/u/Documents", "/users/u/.config",
            dirs("/users/u/.config/opentrack-2.3"), Files())?.Replace('\\', '/'));
        Assert.Null(OpenTrack.ProfileDir(Os.MacOS, null, "/Users/u/Documents", "/Users/u/.config", dirs(), Files()));   // never run: none yet
    }

    [Fact] public void TheProfileIsTheWebcamTrackerSendingUdpHereAndIsNeverWrittenOverAnother()
    {
        string text = OpenTrack.ProfileText(4242);
        Assert.Contains("[modules]\ntracker-dll=neuralnet\nprotocol-dll=udp\n", text);
        Assert.Contains("[udp-proto]\nip1=127\nip2=0\nip3=0\nip4=1\nport=4242\n", text);
        Assert.Contains("port=5555", OpenTrack.ProfileText(5555));

        string dir = Path.Combine(Path.GetTempPath(), "i26-opentrack-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(OpenTrack.WriteResult.NoFolder, OpenTrack.WriteProfile(dir, 4242));
        Assert.Equal(OpenTrack.WriteResult.NoFolder, OpenTrack.WriteProfile(null, 4242));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(OpenTrack.WriteResult.Written, OpenTrack.WriteProfile(dir, 4242));
            Assert.Equal(text, File.ReadAllText(Path.Combine(dir, OpenTrack.ProfileName)));
            File.WriteAllText(Path.Combine(dir, OpenTrack.ProfileName), "the player's own");
            Assert.Equal(OpenTrack.WriteResult.Exists, OpenTrack.WriteProfile(dir, 4242));
            Assert.Equal("the player's own", File.ReadAllText(Path.Combine(dir, OpenTrack.ProfileName)));
            Assert.Equal("Present", OpenTrack.Label(OpenTrack.WriteResult.Exists));
        }
        finally { Directory.Delete(dir, true); }
    }
}
