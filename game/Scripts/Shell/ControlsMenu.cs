namespace I76.Game.Shell;

/// <summary>
/// Controls (the port's sheet, in the Options Menu's place of the original's Control Configuration): the Options Menu's notepad
/// (record 6) with "Controls" on its first line, then force feedback (Force Feedback, Rumble Strength, Wheel Strength, Play Options'
/// until round 5), head tracking (Head Tracking, Head Track Sens., Reach and Centre and the three opentrack rows, Play Options' before) and, under a blank line, Mouse Options and the key map (the original's Control Configuration screen, record 16),
/// each a screen of its own that comes back here. Done saves, Cancel puts back the sheet's own rows (also across a visit to Mouse
/// Options or the key map, whose own Done and Cancel decide theirs). The Esc menu opens the same sheet over the paused game.
/// </summary>
public sealed class ControlsMenuScreen : OverlayScreen
{
    protected override int Record => 6;
    private const int ListY = 118, LineH = 23;
    private static int LineY(int line) => ListY + LineH * line;

    /// <summary>The sheet's rows as the menus label them, in order (null: a blank line).</summary>
    public static readonly string?[] Rows =
    [
        "Force Feedback", "Rumble Strength", "Wheel Strength",
        "Head Tracking", "Head Track Sens.", "Head Track Reach", "Head Track Centre", "Opentrack", "Opentrack Auto", "Opentrack Profile",
        null, "Mouse Options...", "Key Map...",
    ];

    /// <summary>The settings as the sheet was first opened, kept across a visit to Mouse Options or the key map, for Cancel.</summary>
    private static ShellSettings? _carried;
    private ShellSettings _saved = null!;

    /// <summary>The settings this sheet edits, copied from one settings object to another (and the mouse's, for the in-mission menu's hand-back).</summary>
    public static void CopyControls(ShellSettings from, ShellSettings to)
    {
        CopyForce(from, to);
        MouseOptionsScreen.CopyMouse(from, to);
    }

    /// <summary>The sheet's own rows: force feedback and its two strengths, Head Tracking, its sensitivity and Opentrack Auto.</summary>
    public static void CopyForce(ShellSettings from, ShellSettings to)
    {
        to.ForceFeedbackMode = from.ForceFeedbackMode;
        to.RumbleStrength = from.RumbleStrength;
        to.FfbStrength = from.FfbStrength;
        to.HeadTrack = from.HeadTrack;
        to.HeadTrackSensitivity = from.HeadTrackSensitivity;
        to.HeadTrackReach = from.HeadTrackReach;
        to.HeadTrackCentre = from.HeadTrackCentre;
        to.OpenTrackAuto = from.OpenTrackAuto;
    }

    /// <summary>The Opentrack row's state as last drawn, polled twice a second (<see cref="Tick"/>).</summary>
    private I76.Game.OpenTrackState _otState;
    private double _otPoll;
    private string _otProfile = "Write";

    protected override void Build()
    {
        var s = Shell.Settings;
        _saved = _carried ?? CopyTo(s);
        _carried = null;
        Add(200, LineY(0), "Controls", null);
        // head tracking follows the sheet at once (the socket and the FreeTrack map with it, in a mission the head too), so the
        // Opentrack row can see poses arriving; Cancel puts it back
        I76.Game.HeadTracker.Apply(s);
        _otState = I76.Game.OpenTrackLauncher.State(s);
        _otProfile = I76.Game.OpenTrackLauncher.ProfileDir(s) is { } pd && File.Exists(Path.Combine(pd, I76.Game.OpenTrack.ProfileName)) ? "Present" : "Write";
        for (int k = 0; k < Rows.Length; k++)
        {
            if (Rows[k] is not { } label) continue;
            int y = LineY(k + 1);
            Item value;
            switch (label)
            {
                // Off, the pad's rumble (the original's effects, or with the port's pulses added), or a wheel (DirectInput, Windows only:
                // elsewhere the click skips it); a click steps, live
                case "Force Feedback":
                    Add(200, y, label, null);
                    value = Add(397, y, () => ShellSettings.ForceFeedbackNames[s.ForceFeedbackMode],
                        () => { s.ForceFeedbackMode = (s.ForceFeedbackMode + 1) % (I76.Game.DirectInputForce.Supported ? 4 : 3); ApplyForce(s); });
                    value.Right = 461;
                    break;
                // the strength of every level sent to the pad (the pad's events are Force Feedback's)
                case "Rumble Strength":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.RumbleStrength + (s.RumbleStrength == I76.Formats.Input.RumbleStrength.Default ? " %" : " %" + ShellSettings.PortMark),
                        () => { s.RumbleStrength = I76.Formats.Input.RumbleStrength.Next(s.RumbleStrength); ApplyForce(s); });
                    break;
                // the force-feedback wheel's strength (its device gain): a click steps 10 %, past 100 back to 0
                case "Wheel Strength":
                    Add(200, y, label, null);
                    value = Add(397, y, () => I76.Game.DirectInputForce.Supported ? $"{s.FfbStrength}%" : "Windows only",
                        () => { s.FfbStrength = s.FfbStrength >= 100 ? 0 : Math.Min(100, s.FfbStrength / 10 * 10 + 10); ApplyForce(s); });
                    value.Right = 461;
                    break;
                // Head Tracking (opentrack, docs/playing.md): a click steps Off / Analog / Digital, live
                case "Head Tracking":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.HeadTrack == I76.Game.HeadTrackMode.Off ? "Off" : s.HeadTrack + ShellSettings.PortMark,
                        () => { s.HeadTrack = (I76.Game.HeadTrackMode)(((int)s.HeadTrack + 1) % 3); I76.Game.HeadTracker.Apply(s); });
                    break;
                case "Head Track Sens.":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.HeadTrackSensitivity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                        () => { s.HeadTrackSensitivity = NextSensitivity(s.HeadTrackSensitivity); I76.Game.HeadTracker.Apply(s); });
                    break;
                // how far round the view goes at the full head turn (180: out of the rear window); a click steps, live
                case "Head Track Reach":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.HeadTrackReach.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        () => { s.HeadTrackReach = NextReach(s.HeadTrackReach); I76.Game.HeadTracker.Apply(s); });
                    break;
                // the still zone round straight ahead and the curve out of it (Off / Light / Snap / Strong); a click steps, live
                case "Head Track Centre":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.HeadTrackCentre.ToString(),
                        () => { s.HeadTrackCentre = (I76.Game.HeadTrackCentre)(((int)s.HeadTrackCentre + 1) % 4); I76.Game.HeadTracker.Apply(s); });
                    break;
                // Opentrack: its state (Missing / Stopped / Running / Tracking: poses arriving over UDP or FreeTrack); a click when Stopped starts it
                case "Opentrack":
                    Add(200, y, label, null);
                    value = Add(397, y, () => I76.Game.OpenTrack.Label(_otState),
                        () => { if (_otState == I76.Game.OpenTrackState.Stopped && I76.Game.OpenTrackLauncher.Start(s)) { _otState = I76.Game.OpenTrackState.Running; Redraw(); } });
                    break;
                // Opentrack Auto: a mission with Head Tracking on starts it, and the game closes the one it started when it exits
                case "Opentrack Auto":
                    Add(200, y, label, null);
                    value = Add(397, y, () => s.OpenTrackAuto ? "On" : "Off", () => s.OpenTrackAuto = !s.OpenTrackAuto);
                    break;
                // Opentrack Profile: a click writes I26.ini (the webcam tracker, UDP to this machine) into opentrack's profiles, never over one
                case "Opentrack Profile":
                    Add(200, y, label, null);
                    value = Add(397, y, () => _otProfile, () => { _otProfile = I76.Game.OpenTrack.Label(I76.Game.OpenTrackLauncher.WriteProfile(s)); Redraw(); });
                    value.Right = 461;
                    break;
                case "Mouse Options...":
                    value = Add(200, y, () => label, () => Open(new MouseOptionsScreen { ReturnToControlsMenu = true }), w: 262, h: 18);
                    break;
                default:
                    value = Add(200, y, () => label, () => Open(new ControlsScreen()), w: 262, h: 18);
                    break;
            }
            Tip(value, () => MenuTips.Of(label), 200, 462, -2, LineH);
        }
        Add(350, 438, "Done", () => { s.Save(); Shell.Options(); }, Small);
        Add(402, 438, "Cancel", Cancel, Small);
    }

    private static ShellSettings CopyTo(ShellSettings s)
    {
        var t = new ShellSettings();
        CopyForce(s, t);
        return t;
    }

    /// <summary>Mouse Options or the key map, which come back to a new sheet: the snapshot goes with them.</summary>
    private void Open(ShellScreen screen)
    {
        _carried = _saved;
        Shell.Overlay(screen);
    }

    private void Cancel()
    {
        // the sheet's rows back, saved: Mouse Options' Done may have saved the file with them changed since
        CopyForce(_saved, Shell.Settings);
        ApplyForce(Shell.Settings);
        I76.Game.HeadTracker.Apply(Shell.Settings);
        Shell.Settings.Save();
        Shell.Options();
    }

    protected override void Escape() => Cancel();

    /// <summary>The Opentrack row follows opentrack: its state looked at twice a second, the sheet drawn again when it changes.</summary>
    public override void Tick()
    {
        base.Tick();
        if (Shell.Current != this || (_otPoll += Shell.Delta) < 0.5) return;
        _otPoll = 0;
        var now = I76.Game.OpenTrackLauncher.State(Shell.Settings);
        if (now == _otState) return;
        _otState = now;
        Redraw();
    }

    /// <summary>Head Track Sens.'s next step (<see cref="I76.Game.HeadLook.Sensitivities"/>, past the last back to the first).</summary>
    internal static float NextSensitivity(float now)
    {
        var o = I76.Game.HeadLook.Sensitivities;
        int i = Array.FindIndex(o, v => v > now + 0.01f);
        return o[i < 0 ? 0 : i];
    }

    /// <summary>Head Track Reach's next step (<see cref="I76.Game.HeadLook.Reaches"/>, past the last back to the first).</summary>
    internal static int NextReach(int now)
    {
        var o = I76.Game.HeadLook.Reaches;
        int i = Array.FindIndex(o, v => v > now);
        return o[i < 0 ? 0 : i];
    }

    /// <summary>Force feedback as the sheet left it, at once (a pad's rumble, the wheel or nothing; the strengths).</summary>
    private static void ApplyForce(ShellSettings s)
    {
        I76.Game.ForceFeedback.Strength = s.FfbStrength;
        I76.Game.ForceFeedback.UseWheel = s.ForceFeedbackWheel;
        I76.Game.ForceFeedback.Enabled = s.ForceFeedback;
        I76.Game.ForceFeedback.Mode = s.RumbleMode;
        I76.Game.ForceFeedback.PadStrength = s.RumbleStrength;
    }
}
