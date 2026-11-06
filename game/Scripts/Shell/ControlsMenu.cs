namespace I76.Game.Shell;

/// <summary>
/// Controls (the port's sheet, in the Options Menu's place of the original's Control Configuration): the Options Menu's notepad
/// (record 6) with "Controls" on its first line, then force feedback (Force Feedback, Rumble Strength, Wheel Strength, Play Options'
/// until round 5) and, under a blank line, Mouse Options and the key map (the original's Control Configuration screen, record 16),
/// each a screen of its own that comes back here. Done saves, Cancel puts back the sheet's own rows (also across a visit to Mouse
/// Options or the key map, whose own Done and Cancel decide theirs). The Esc menu opens the same sheet over the paused game.
/// </summary>
public sealed class ControlsMenuScreen : OverlayScreen
{
    protected override int Record => 6;
    private const int ListY = 118, LineH = 23;
    private static int LineY(int line) => ListY + LineH * line;

    /// <summary>The sheet's rows as the menus label them, in order (null: a blank line).</summary>
    public static readonly string?[] Rows = ["Force Feedback", "Rumble Strength", "Wheel Strength", null, "Mouse Options...", "Key Map..."];

    /// <summary>The settings as the sheet was first opened, kept across a visit to Mouse Options or the key map, for Cancel.</summary>
    private static ShellSettings? _carried;
    private ShellSettings _saved = null!;

    /// <summary>The settings this sheet edits, copied from one settings object to another (and the mouse's, for the in-mission menu's hand-back).</summary>
    public static void CopyControls(ShellSettings from, ShellSettings to)
    {
        CopyForce(from, to);
        MouseOptionsScreen.CopyMouse(from, to);
    }

    /// <summary>The sheet's own rows: force feedback and its two strengths.</summary>
    public static void CopyForce(ShellSettings from, ShellSettings to)
    {
        to.ForceFeedbackMode = from.ForceFeedbackMode;
        to.RumbleStrength = from.RumbleStrength;
        to.FfbStrength = from.FfbStrength;
    }

    protected override void Build()
    {
        var s = Shell.Settings;
        _saved = _carried ?? CopyTo(s);
        _carried = null;
        Add(200, LineY(0), "Controls", null);
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
        Shell.Settings.Save();
        Shell.Options();
    }

    protected override void Escape() => Cancel();

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
