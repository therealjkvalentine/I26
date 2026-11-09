using System.Linq;
using Godot;
using I76.Game.Ui;

namespace I76.Game.Boot;

/// <summary>
/// The game's part of "Report a Bug" (<see cref="BugReport"/>; docs/playing.md "Reporting a bug"): what the running game
/// knows, gathered into a report and written to the user folder, from the Esc menu's row or Ctrl+F11. The frame the player
/// saw is read when the Esc menu opens and kept while it is up, so the report's picture has no pad over it. The engine's
/// output is kept in a ring (<see cref="BugReportLog"/>) whether or not the log file is on, written only into a report.
/// <c>--bug-report-open ZIP</c> starts a report's mission and car and restores its state, as a checkpoint's retry does.
/// </summary>
public partial class Main
{
    /// <summary>The engine's output (prints, warnings, errors) as it goes by: the last lines go into a report's log.txt.</summary>
    public partial class BugReportLog : Logger
    {
        public const int Lines = 400;
        public static readonly LineRing Ring = new(Lines);
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try { OS.AddLogger(new BugReportLog()); }
            catch (System.Exception e) { GD.PrintErr($"bug report log: {e.Message}"); }
        }

        public override void _LogMessage(string message, bool error) => Ring.Add(message.TrimEnd('\n'));

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces) =>
            Ring.Add($"{(errorType == 1 ? "WARNING" : "ERROR")}: {(rationale.Length > 0 ? rationale : code)} at {function} ({file}:{line})");
    }

    /// <summary>The frame as the Esc menu opened, for the report's screenshot; dropped when the menu closes.</summary>
    private Image? _menuFrame;
    /// <summary>The mission's rand seed (SpawnCarSteps), for the report.</summary>
    private uint _randSeed;
    /// <summary>The report's screenshot is at most this wide (a 3440-wide frame is halved; the zip stays small).</summary>
    private const int ReportShotWidth = 1920;

    /// <summary>The last drawn frame (the game's own: nothing of a pad is drawn yet when the Esc menu reads it).</summary>
    private Image? GrabFrame()
    {
        try { return GetViewport().GetTexture().GetImage(); }
        catch (System.Exception e) { GD.PrintErr($"bug report: the frame could not be read ({e.Message})"); return null; }
    }

    /// <summary>
    /// Writes a report of the game as it is now, with <paramref name="frame"/> as its picture, and returns the words for the
    /// pad (or the notice) and the folder to open, or the failure.
    /// </summary>
    private (bool Ok, string Text, string? Folder) WriteBugReport(Image? frame)
    {
        try
        {
            var data = GatherBugReport(frame);
            string root = ProjectSettings.GlobalizePath($"user://{BugReport.Folder}");
            var (dir, zip) = BugReport.Write(root, data);
            GD.Print($"bug report: {zip}");
            return (true, $"Bug report saved: {System.IO.Path.GetFileName(zip)} in {BugReport.Folder} in the user folder. Drag the zip into Discord or a GitHub issue, with a line on what went wrong.", dir);
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
        {
            GD.PrintErr($"bug report: not written ({e.Message})");
            return (false, $"The bug report could not be written: {e.Message}", null);
        }
    }

    /// <summary>Ctrl+F11 in play (no menu): the frame as it is, then the Lee-hand notice the checkpoints use.</summary>
    private void BugReportKey()
    {
        var r = WriteBugReport(GrabFrame());
        if (_opt.Install is null) return;
        GetNodeOrNull("CheckpointNotice")?.Free();
        AddChild(new CheckpointNotice { InstallDir = _opt.Install, Faithful = _mode1997, Name = "CheckpointNotice", Hold = 3, Text = r.Ok ? "Bug report saved to bug-reports" : "Bug report not written (see the log)" });
    }

    private BugReport.Data GatherBugReport(Image? frame)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var settings = I76.Game.Shell.ShellSettings.Load();
        var s = _sim?.Session;
        string mission = (_opt.Mission ?? "").ToLowerInvariant();

        // ---- the build
        string build = $"{BuildInfo.Line}  snapshot format {I76.Sim.Save.SnapshotHeader.CurrentVersion}\nGodot {Engine.GetVersionInfo()["string"]}";

        // ---- the system: the OS, the renderer and the adapter, the window and the look (no machine name, no user)
        var win = DisplayServer.WindowGetSize();
        var screen = DisplayServer.ScreenGetSize();
        string system = string.Join('\n', new[]
        {
            $"os: {OS.GetName()} {OS.GetVersion()}{(OS.GetDistributionName() is { Length: > 0 } dn && dn != OS.GetName() ? $" ({dn})" : "")}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
            RenderBackend.Describe() + (RenderBackend.VideoMemoryMb is { } vram ? $", video memory {vram} MiB" : ""),
            $"window: {win.X}x{win.Y} {DisplayServer.WindowGetMode().ToString().ToLowerInvariant()}, screen {screen.X}x{screen.Y}, render scale {RenderScale.Percents[System.Math.Clamp(settings.RenderScale, 0, RenderScale.Percents.Length - 1)]} %, "
                + $"vsync {(settings.VSync ? "on" : "off")}, max fps {I76.Sim.Views.FrameLimit.Names[System.Math.Clamp(settings.MaxFps, 0, I76.Sim.Views.FrameLimit.Names.Length - 1)]}, "
                + $"anti-aliasing {RenderScale.AntiAliasing[System.Math.Clamp(settings.Graphics[0x8], 0, RenderScale.AntiAliasing.Length - 1)]}",
            $"look: {(_mode1997 ? (settings.SoftwareRenderer ? "1997 (software renderer)" : "1997") : "modern")}, renderer setting {settings.Renderer}, "
                + $"world textures {I76.Game.Assets.HdTextures.Word(I76.Game.Assets.HdTextures.WorldSize)}, car and dash textures {I76.Game.Assets.HdTextures.Word(I76.Game.Assets.HdTextures.CarSize)}, "
                + $"menu art {(I76.Game.Shell.HdAssets.Enabled && I76.Game.Shell.HdAssets.Source is not null ? "HD" : "the install's")}",
            $"shadow detail {settings.Graphics[0x6]}, terrain detail {settings.Graphics[0x2]}, ambient occlusion {(settings.AmbientOcclusion ? "on" : "off")}, vegetation {settings.Vegetation}, "
                + $"visibility range {(settings.DrawDistanceChoice is { } dd ? dd.ToString(inv) : "follows the look")}, quiet window {(QuietWindow.Active ? "on" : "off")}",
            $"input: {(Controls.PadSource.Read().Count > 0 ? "a pad or wheel is connected" : "keyboard and mouse")}, force feedback {I76.Game.Shell.ShellSettings.ForceFeedbackNames[System.Math.Clamp(settings.ForceFeedbackMode, 0, 3)]}",
        });

        // ---- the game: what is running and where it is
        string kind = _net is not null ? (NetCoop ? "network co-op" : "network melee") + (_net.Server is not null ? " (this machine hosts)" : " (joined)")
            : _launch is { } l ? l.Kind switch { I76.Game.Shell.GameKind.Trip => "trip mission (from the shell)", I76.Game.Shell.GameKind.AutoMelee => "instant melee (from the shell)", I76.Game.Shell.GameKind.Scenario => "scenario (from the shell)", _ => l.Kind.ToString() }
            : _opt.Melee ? "command-line mission as an auto melee" : "command-line mission";
        string title = mission.Length >= 3 && mission[0] == 't' && int.TryParse(mission.AsSpan(1), out int tn) ? $" \"{I76.Formats.Mission.MissionTitles.TripTitle(tn)}\"" : "";
        string install = _opt.Install is { } idir ? (new I76.Formats.Vfs.GameInstall(idir).IsNitro ? "Nitro Pack" : "Interstate '76") : "none";
        var game = new System.Text.StringBuilder();
        game.Append($"game: {install}, {kind}\n");
        game.Append($"mission: {mission}{title}");
        if (s is not null)
        {
            var (n, dt) = I76.Sim.Vehicles.VehicleStep.Substeps(s.Rate.FrameDelta(s.Ticks));
            game.Append($"\nsim: tick {s.Ticks} at {s.Rate.Label} Hz (sim time {s.World.Time.ToString("0.00", inv)} s, next tick {n} substep{(n == 1 ? "" : "s")} of {(dt * 1000).ToString("0.0", inv)} ms), rand seed {_randSeed}, install identity {s.Build?.Install ?? 0:x16}");
            game.Append($"\nstate: {(_sim!.DevFrozen ? "frozen by the Developer panel, " : "")}{(_sim.DevTimeScale != 1 ? $"time x{_sim.DevTimeScale.ToString("0.##", inv)}, " : "")}{(_ended ? "the mission has ended, " : "")}{(_gamePaused ? "paused (Pause key), " : "")}{(_script?.Host.View.Active == true ? "a scripted camera scene is on, " : "")}{(_menu is not null ? "the Esc menu is up" : "in play")}");
        }
        else game.Append("\nsim: no mission is running (the shell, a loading screen or a movie)");
        game.Append($"\noptions: Play Options 0x{settings.PlayOptions:x} (arcade {(settings.PlayOptions & 1) != 0}, no salvage {(settings.PlayOptions & 2) != 0}, cheats {(settings.PlayOptions & 0x1c) != 0}), difficulty {settings.Difficulty}, "
            + $"modern physics {(_modern?.Enabled == true ? "on" : "off")}, checkpoints {(settings.Checkpoints ? "on" : "off")}, auto reverse {(settings.AutoReverse ? "on" : "off")}, developer panel {(DevOn ? "on" : "off")}, "
            + $"cheater mark {(I76.Game.Shell.CheatMarker.Set ? "set" : "clear")}, ai {_opt.Ai}, fixed fps {Engine.MaxFps}, frame rate {Engine.GetFramesPerSecond():0} fps");
        game.Append($"\nview: {(_car?.CockpitCamera?.Current == true ? "cockpit" : _chase is { Current: true } ? "chase" : _cam.Current ? "free camera" : $"preset 0x{_lastPreset:x}")}, 3D dash {(settings.Dash3D ? "on" : "off")}");
        if (_checkpoint is { } cp && cp.Mission.Equals(mission, System.StringComparison.OrdinalIgnoreCase)) game.Append($"\ncheckpoint kept at tick {cp.Tick} ({cp.Why})");

        // ---- the car
        string car;
        if (_car?.Sim is { } v)
        {
            car = $"file: {v.Definition ?? "(none)"}{(string.IsNullOrEmpty(_opt.Drive) ? " (the mission's own)" : $" (requested {_opt.Drive})")}"
                + $"\nplace: ({v.Pos.X.ToString("0.0", inv)}, {v.Pos.Y.ToString("0.0", inv)}, {v.Pos.Z.ToString("0.0", inv)}) heading {I76.Sim.Session.DevTools.HeadingOf(v.Rot.Forward).ToString("0", inv)} deg, "
                + $"speed {(v.Speed * 3.6f).ToString("0", inv)} km/h, health {(v.Health?.HealthPercent() ?? 0).ToString("0", inv)} %{(_car.Wrecked ? ", wrecked" : "")}{(PlayerDead ? ", dead" : "")}"
                + $"\nweapons: {string.Join(", ", DevLoadoutOf(v).Select(w => $"HP{w.Hp} {w.File}"))}";
        }
        else car = "no drivable car";

        // ---- the state (docs/savestate.md): a network client's session takes a snapshot too; the restore is single player's
        byte[]? snapshot = null;
        string? stateNote = null;
        if (s is not null)
        {
            try { snapshot = s.Snapshot(_salvage?.Save()); }
            catch (System.Exception e) { stateNote = $"no snapshot: {e.Message}"; }
        }
        else stateNote = "no snapshot: no mission is running";

        // ---- the settings: the file whole, and the keys that decide most
        string? cfgText = null;
        try { string p = ProjectSettings.GlobalizePath("user://shell.cfg"); if (System.IO.File.Exists(p)) cfgText = System.IO.File.ReadAllText(p); }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { cfgText = null; }
        string set = $"sim rate {settings.SimRate} Hz, resolution setting \"{settings.Resolution}\", field of view {I76.Sim.Views.ViewFov.Names[System.Math.Clamp(settings.FieldOfView, 0, I76.Sim.Views.ViewFov.Names.Length - 1)]}, "
            + $"hood gloss {settings.HoodGloss}, 1997 mode {(settings.Look1997 ? "on" : "off")}, palette grade {(settings.PaletteGrade ? "on" : "off")}, log file {(settings.LogFile ? "on" : "off")}, "
            + $"audio {settings.Audio[0]}/{settings.Audio[1]}/{settings.Audio[2]}, output {I76.Formats.Input.AudioOutputs.Key(settings.Output)}, lfe {settings.LfeLevel}, mouse camera mode {settings.MouseCamera}, "
            + $"bug_report_inputs {(settings.BugReportInputs ? "true" : "false")}";

        // ---- the texture packs: each pack's own name and version (pack.json), the install's own where there is none
        string menuPack = I76.Game.Shell.HdAssets.Source is { } ms ? $"menu art: {ms.Info?.ToString() ?? "a pack without a pack.json"} ({ms.Origin})" : "menu art: none (the install's)";
        string worldPack = I76.Game.Assets.HdTextures.Source is { } ws ? $"world, car and dash textures: {ws.Info?.ToString() ?? "a pack without a pack.json"} ({ws.Origin}); sizes present: {string.Join(", ", I76.Game.Assets.HdTextures.AvailableSizes.Select(x => $"{x}x"))}" : "world, car and dash textures: none (the install's)";
        string packs = menuPack + "\n" + worldPack + $"\nin use: world {I76.Game.Assets.HdTextures.Word(I76.Game.Assets.HdTextures.WorldSize)}, cars and dash {I76.Game.Assets.HdTextures.Word(I76.Game.Assets.HdTextures.CarSize)}";

        // ---- the screenshot: the frame the player saw, no wider than 1920
        byte[]? png = null;
        string shotNote;
        if (frame is not null)
        {
            frame = (Image)frame.Duplicate();   // the menu's copy stays whole for a second report
            int w = frame.GetWidth(), h = frame.GetHeight();
            if (w > ReportShotWidth) frame.Resize(ReportShotWidth, System.Math.Max(1, h * ReportShotWidth / w), Image.Interpolation.Lanczos);
            png = frame.SavePngToBuffer();
            shotNote = $"{frame.GetWidth()}x{frame.GetHeight()}{(w > ReportShotWidth ? $" (scaled down from {w}x{h})" : "")}, {(_menuFrame is not null ? "the frame before the Esc menu opened" : "the frame as the key was pressed")}";
        }
        else shotNote = "no screenshot (the frame could not be read)";

        // ---- the log: this run's output from the ring, whether or not the log file is on
        var log = BugReportLog.Ring.Lines();
        string logNote = $"The log file setting is {(settings.LogFile || LogFile.Forced ? "on (logs/godot.log in the user folder has the whole run)" : "off")}.";

        // ---- the inputs: the last seconds of the player's keys and stick
        var inputs = _sim?.Inputs is { } ring ? ring.Rows() : [];
        string? inputsNote = _sim?.Inputs is null ? (settings.BugReportInputs ? "no inputs recorded (no mission, or a network game)" : "left out: [port] bug_report_inputs=false") : null;

        // ---- a network game: the protocol and the roster by car and team (no names, no addresses)
        string network = "";
        if (_net is not null)
        {
            var players = _net.Client.Scores?.Players;
            network = $"a network game, protocol {I76.Net.Protocol.Version}, {(_net.Server is not null ? "hosting" : "joined")}, {(NetCoop ? "co-op" : "melee")}, {players?.Count ?? 0} players (names and addresses left out)";
            if (players is not null)
                foreach (var p in players)
                    network += $"\n  player: car {CarOf(_netWorld?.Cars.GetValueOrDefault(p.CarSerial)?.Info)}, team {p.Team}, kills {p.Kills}, deaths {p.Deaths}, score {p.Score}";
        }

        // ---- what is blanked: every path that names the user or sits outside the user folder
        var redact = new System.Collections.Generic.List<(string, string)>();
        void Hide(string? path, string mask) { if (path is { Length: > 0 }) redact.Add((path, mask)); }
        Hide(OS.GetUserDataDir(), "<user folder>");
        Hide(_opt.Install, "<install>");
        Hide(System.IO.Path.GetDirectoryName(OS.GetExecutablePath()), "<i26>");
        Hide(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "<home>");
        Hide(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "<home>");
        Hide(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "<home>");
        Hide(System.Environment.UserName, "<user>");
        Hide(System.Environment.MachineName, "<machine>");

        return new BugReport.Data
        {
            Build = build, System = system, Game = game.ToString(), Car = car, Settings = set, Packs = packs, Network = network,
            Snapshot = snapshot, SnapshotTick = s?.Ticks ?? 0, StateNote = stateNote, SettingsFile = cfgText,
            Screenshot = png, ScreenshotNote = shotNote, LogLines = log, LogNote = logNote, InputLines = inputs, InputsNote = inputsNote, Redact = redact,
        };
    }

    /// <summary>A report's roster of an auto melee, for the session the reopened state is restored into (SpawnCarSteps).</summary>
    private static System.Collections.Generic.List<string>? _reportRoster;

    /// <summary>
    /// --bug-report-open ZIP: the report's mission and car as its snapshot names them, its state restored once the mission is
    /// built (a checkpoint's retry's path, at the snapshot's tick rate). A network or co-op state cannot be rebuilt here: the
    /// mission starts plain and the log says so.
    /// </summary>
    private void OpenBugReport(string path)
    {
        byte[]? snap;
        try { snap = BugReport.ReadState(path); }
        catch (System.Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.UnauthorizedAccessException)
        {
            GD.PrintErr($"--bug-report-open: {e.Message}");
            return;
        }
        if (snap is null) { GD.PrintErr("--bug-report-open: the report has no state.sav"); return; }
        I76.Sim.Save.SnapshotHeader header;
        try { header = I76.Sim.Save.SnapshotHeader.Read(snap); }
        catch (System.Exception e) when (e is I76.Sim.Save.SnapshotFormatException or I76.Sim.Save.SnapshotMismatchException)
        {
            GD.PrintErr($"--bug-report-open: {e.Message}");
            return;
        }
        var b = header.Build;
        _opt.Mission = b.Mission;
        _opt.MissionGiven = true;
        _opt.Drive = b.Car ?? "";
        _opt.Ai = b.Script ? "script" : "off";
        _opt.Melee = b.Melee is not null;
        _reportRoster = b.Melee?.ToList();
        if (b.Rules?.Network == true || b.Coop is not null)
        {
            GD.PrintErr($"--bug-report-open: the state is a {(b.Coop is not null ? "co-op" : "network")} game's, which cannot be rebuilt here; {b.Mission} starts without it");
            return;
        }
        _retry = new Checkpoint(b.Mission, snap, header.Host.Length > 0 ? header.Host : null, null, null, true, 0xf, "bug report", header.Ticks);
        GD.Print($"--bug-report-open: {b.Mission} in {b.Car ?? "the mission's own car"}, tick {header.Ticks} at {header.Rate.Label} Hz{(b.Melee is { Count: > 0 } m ? $", melee against {string.Join(", ", m)}" : "")}");
    }
}
