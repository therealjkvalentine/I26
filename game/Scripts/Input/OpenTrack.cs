using System.Diagnostics;

namespace I76.Game;

/// <summary>What the menus' Opentrack row shows.</summary>
public enum OpenTrackState { NotInstalled, Stopped, Running, Receiving }

/// <summary>
/// Finding opentrack, its state and its profile, apart from the engine and the machine so it can be tested. opentrack has no switch to
/// start tracking from its command line (its main() hands argv to Qt and reads nothing); a profile it is told to start comes from its own
/// Options > Game detection list (a running program's name, then the profile), its Start button or a hotkey. The game only starts and
/// stops the program, and writes a profile on request.
/// </summary>
public static class OpenTrack
{
    public enum Os { Windows, MacOS, Linux }

    public static Os Current => OperatingSystem.IsWindows() ? Os.Windows : OperatingSystem.IsMacOS() ? Os.MacOS : Os.Linux;

    /// <summary>The profile the game writes (on request, never over one that exists).</summary>
    public const string ProfileName = "I26.ini";
    /// <summary>opentrack's settings folder name (its organisation name), under Documents.</summary>
    public const string Folder = "opentrack-2.3";

    private static string Exe(Os os) => os == Os.Windows ? "opentrack.exe" : "opentrack";

    /// <summary>
    /// The places opentrack may be, in order: the configured path (the program, its folder or its .app), then the usual installs:
    /// Windows' Program Files (both) and per-user Programs, then PATH; macOS's Applications (the system's, then the user's), then PATH;
    /// elsewhere PATH, then /usr/local, /usr, /opt and ~/.local.
    /// </summary>
    public static IEnumerable<string> Candidates(string configured, Os os, Func<string, string?> env, string home)
    {
        string exe = Exe(os);
        if (configured.Trim() is { Length: > 0 } c)
        {
            if (c.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || c.EndsWith(".app/", StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(c.TrimEnd('/'), "Contents", "MacOS", "opentrack");
            else
            {
                yield return c;
                yield return Path.Combine(c, exe);
                yield return Path.Combine(c, "bin", exe);
            }
        }
        if (os == Os.Windows)
        {
            foreach (var v in new[] { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432" })
                if (env(v) is { Length: > 0 } pf) yield return Path.Combine(pf, "opentrack", exe);
            if (env("LOCALAPPDATA") is { Length: > 0 } la) yield return Path.Combine(la, "Programs", "opentrack", exe);
        }
        else if (os == Os.MacOS)
        {
            yield return "/Applications/opentrack.app/Contents/MacOS/opentrack";
            yield return Path.Combine(home, "Applications", "opentrack.app", "Contents", "MacOS", "opentrack");
        }
        foreach (var dir in (env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(dir, exe);
        if (os == Os.Linux)
        {
            yield return "/usr/local/bin/opentrack";
            yield return "/usr/bin/opentrack";
            yield return "/opt/opentrack/bin/opentrack";
            yield return Path.Combine(home, ".local", "bin", "opentrack");
        }
    }

    /// <summary>The first candidate that is a file, or null (not installed, or not where the game looks: set [port] opentrack_path).</summary>
    public static string? Find(string configured, Os os, Func<string, string?> env, string home, Func<string, bool> isFile) =>
        Candidates(configured, os, env, home).FirstOrDefault(isFile);

    /// <summary>The row's state: poses arriving win (a tracker on another machine too), then a running opentrack, then one found.</summary>
    public static OpenTrackState StateOf(bool found, bool running, bool receiving) =>
        receiving ? OpenTrackState.Receiving : running ? OpenTrackState.Running : found ? OpenTrackState.Stopped : OpenTrackState.NotInstalled;

    public static string Label(OpenTrackState s) => s switch
    {
        OpenTrackState.NotInstalled => "Missing",
        OpenTrackState.Stopped => "Stopped",
        OpenTrackState.Running => "Running",
        _ => "Tracking",
    };

    /// <summary>A mission starts opentrack only with Opentrack Auto and Head Tracking on, opentrack found, none running and no poses arriving.</summary>
    public static bool ShouldAutoStart(bool auto, HeadTrackMode mode, OpenTrackState state) =>
        auto && mode != HeadTrackMode.Off && state == OpenTrackState.Stopped;

    /// <summary>
    /// opentrack's profile folder as it reads it: portable (a portable.txt beside the program, Windows) its ini folder; otherwise
    /// Documents/opentrack-2.3, or on Linux the config folder's opentrack-2.3 when Documents has none. Null when it does not exist yet
    /// (opentrack makes it on its first run).
    /// </summary>
    public static string? ProfileDir(Os os, string? program, string documents, string configHome, Func<string, bool> isDir, Func<string, bool> isFile)
    {
        if (os == Os.Windows && program is not null && Path.GetDirectoryName(program) is { } bin && isFile(Path.Combine(bin, "portable.txt")))
            return isDir(Path.Combine(bin, "ini")) ? Path.Combine(bin, "ini") : null;
        string docs = Path.Combine(documents, Folder);
        if (isDir(docs)) return docs;
        if (os == Os.Linux && isDir(Path.Combine(configHome, Folder))) return Path.Combine(configHome, Folder);
        return null;
    }

    /// <summary>
    /// The game's profile: the neural-net webcam tracker (a face, no markers) and the UDP output to this machine on the game's port.
    /// The camera, the filter and the hotkeys stay opentrack's defaults: the player chooses the camera once in the tracker's settings.
    /// </summary>
    public static string ProfileText(int port) =>
        "[modules]\n" +
        "tracker-dll=neuralnet\n" +
        "protocol-dll=udp\n" +
        "filter-dll=accela-hamilton\n" +
        "\n" +
        "[udp-proto]\n" +
        "ip1=127\nip2=0\nip3=0\nip4=1\n" +
        $"port={port}\n";

    public enum WriteResult { Written, Exists, NoFolder, Failed }

    /// <summary>The profile written into the folder, only if no file of its name is there (never overwritten).</summary>
    public static WriteResult WriteProfile(string? dir, int port)
    {
        if (dir is null || !Directory.Exists(dir)) return WriteResult.NoFolder;
        string path = Path.Combine(dir, ProfileName);
        if (File.Exists(path)) return WriteResult.Exists;
        try
        {
            using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var b = System.Text.Encoding.UTF8.GetBytes(ProfileText(port));
            f.Write(b, 0, b.Length);
            return WriteResult.Written;
        }
        catch (IOException) when (File.Exists(path)) { return WriteResult.Exists; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return WriteResult.Failed; }
    }

    public static string Label(WriteResult r) => r switch
    {
        WriteResult.Written => "Written",
        WriteResult.Exists => "Present",
        WriteResult.NoFolder => "No folder",
        _ => "Failed",
    };
}

/// <summary>
/// The one opentrack the game started, if any: started on request (the menus' Opentrack row, or a mission with Opentrack Auto), and
/// closed when the game exits. An opentrack the player started is never touched: only the process the game started, by its handle.
/// </summary>
public static class OpenTrackLauncher
{
    private static Process? _ours;
    private static bool _hooked;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The program, or null.</summary>
    public static string? Program(Shell.ShellSettings s) =>
        OpenTrack.Find(s.OpenTrackPath, OpenTrack.Current, Environment.GetEnvironmentVariable, Home, File.Exists);

    /// <summary>opentrack's profile folder on this machine, or null (see <see cref="OpenTrack.ProfileDir"/>).</summary>
    public static string? ProfileDir(Shell.ShellSettings s)
    {
        string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".config");
        return OpenTrack.ProfileDir(OpenTrack.Current, Program(s), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            config, Directory.Exists, File.Exists);
    }

    /// <summary>The menus' Opentrack Profile: I26.ini written for the game's port, never over a file of that name.</summary>
    public static OpenTrack.WriteResult WriteProfile(Shell.ShellSettings s) => OpenTrack.WriteProfile(ProfileDir(s), s.HeadTrackPort);

    /// <summary>The game's opentrack is alive.</summary>
    public static bool OursRunning
    {
        get
        {
            try { return _ours is { HasExited: false }; }
            catch (InvalidOperationException) { return false; }
        }
    }

    /// <summary>Any opentrack runs (the game's, or one by its process name: the player's).</summary>
    public static bool AnyRunning
    {
        get
        {
            if (OursRunning) return true;
            try
            {
                var found = Process.GetProcessesByName("opentrack");
                foreach (var p in found) p.Dispose();
                return found.Length > 0;
            }
            catch (Exception) { return false; }
        }
    }

    public static OpenTrackState State(Shell.ShellSettings s) =>
        OpenTrack.StateOf(Program(s) is not null, AnyRunning, HeadTracker.Receiving);

    /// <summary>Starts opentrack when it is found and none runs; true when it was started.</summary>
    public static bool Start(Shell.ShellSettings s)
    {
        if (AnyRunning || Program(s) is not { } exe) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
            _ours = Process.Start(psi);
            if (_ours is null) return false;
            if (!_hooked) { _hooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => StopOurs(); }
            Godot.GD.Print($"opentrack: started (pid {_ours.Id})");
            return true;
        }
        catch (Exception e)
        {
            Godot.GD.PrintErr($"opentrack: cannot start {exe}: {e.Message}");
            return false;
        }
    }

    /// <summary>A mission's start: opentrack started when Opentrack Auto and Head Tracking are on and it is found, not running and silent.</summary>
    public static void AutoStart(Shell.ShellSettings s)
    {
        if (!s.OpenTrackAuto || s.HeadTrack == HeadTrackMode.Off) return;
        if (OpenTrack.ShouldAutoStart(s.OpenTrackAuto, s.HeadTrack, State(s))) Start(s);
    }

    /// <summary>The game is exiting: the opentrack it started is asked to close (its window's close, as the player's would), then ended if it lingers.</summary>
    public static void StopOurs()
    {
        var p = System.Threading.Interlocked.Exchange(ref _ours, null);
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                if (!p.CloseMainWindow() || !p.WaitForExit(3000)) p.Kill();
            }
        }
        catch (Exception) { }   // gone meanwhile
        finally { p.Dispose(); }
    }
}
