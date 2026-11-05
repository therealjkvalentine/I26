using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_QUIET_WINDOW (docs/dev-hooks.md, a capture/test hook): the game starts in a window behind the others and never takes the
/// keyboard, the screen or the pointer, for test runs and captures on a desktop someone is using. <c>1</c> (or <c>on</c>, <c>behind</c>):
/// a window of the size asked for (Godot's <c>--resolution</c>, else shell.cfg's Resolution, else 1280x720), not the desktop's
/// borderless fullscreen, put at the bottom of the window stack, the foreground handed back to the window that had it; <c>min</c>:
/// the same, minimised (Godot draws nothing while a window is minimised, so a <c>--screenshot</c> or frame-profile run is put behind instead). The pointer is never
/// confined or captured (Main.UpdateMouseCapture). Windows does the stacking and the foreground through user32; elsewhere the window
/// is only made a window of that size (and minimised for <c>min</c>).
/// </summary>
public static class QuietWindow
{
    public enum Mode { Off, Behind, Minimised }

    /// <summary>I76_QUIET_WINDOW's value: unset, empty, <c>0</c>, <c>off</c>, <c>false</c> or <c>no</c> is off; <c>min</c>, <c>minimised</c>, <c>minimized</c> or <c>minimise</c> minimised; anything else behind.</summary>
    public static Mode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "0" or "off" or "false" or "no" => Mode.Off,
        "min" or "minimised" or "minimized" or "minimise" or "minimize" => Mode.Minimised,
        _ => Mode.Behind,
    };

    /// <summary>The default size when neither the command line nor shell.cfg names one.</summary>
    public static readonly (int W, int H) DefaultSize = (1280, 720);

    /// <summary>The quiet window's size: the engine's <c>--resolution WxH</c> (the last one given), else shell.cfg's Resolution, else <see cref="DefaultSize"/>.
    /// Only the engine's arguments count (those before <c>--</c>); a malformed value is passed over.</summary>
    public static (int W, int H) StartSize(IReadOnlyList<string> engineArgs, string? cfgResolution)
    {
        (int W, int H)? fromArgs = null;
        for (int i = 0; i < engineArgs.Count; i++)
        {
            if (engineArgs[i] is "--" or "++") break;
            if (engineArgs[i] == "--resolution" && i + 1 < engineArgs.Count && WindowSizes.Parse(engineArgs[i + 1]) is { } r) fromArgs = r;
        }
        return fromArgs ?? WindowSizes.Parse(cfgResolution) ?? DefaultSize;
    }

    /// <summary>Where the window goes: centred on the usable area when it fits there, else at the screen's corner (a desktop-sized measurement
    /// window, borderless so its client area is the whole size). Returns the position and whether the window is borderless.</summary>
    public static ((int X, int Y) Pos, bool Borderless) Place((int W, int H) size, (int X, int Y, int W, int H) usable, (int X, int Y) screen)
    {
        if (size.W <= usable.W && size.H <= usable.H) return ((usable.X + (usable.W - size.W) / 2, usable.Y + (usable.H - size.H) / 2), false);
        return (screen, true);
    }

    public static Mode Requested { get; private set; } = Parse(System.Environment.GetEnvironmentVariable("I76_QUIET_WINDOW"));
    public static bool Active => Requested != Mode.Off;

    /// <summary>The mode for a run: <c>min</c> is behind instead for a run that needs its frames drawn (a <c>--screenshot</c>, a frame profile),
    /// as Godot draws nothing in a minimised window and such a run would never end.</summary>
    public static Mode ForRun(Mode requested, bool needsFrames) => requested == Mode.Minimised && needsFrames ? Mode.Behind : requested;
    private static bool _sized;

    /// <summary>First thing the game does: on Windows, give the foreground back and go to the bottom of the stack (the engine showed its window
    /// in front before any script ran). Cheap and idempotent: called again after the window is sized.</summary>
    public static void Early()
    {
        if (!Active || DisplayServer.GetName() == "headless") return;
        if (OperatingSystem.IsWindows()) Win32.Retreat(Requested == Mode.Minimised);
    }

    /// <summary>In place of Graphic Detail's Resolution at the start: the window at the quiet size, once a run (a scene reload keeps it).</summary>
    public static void Apply(I76.Game.Shell.ShellSettings s, bool needsFrames)
    {
        if (!Active || _sized || DisplayServer.GetName() == "headless") return;
        _sized = true;
        if (ForRun(Requested, needsFrames) is var m && m != Requested)
        {
            GD.Print("quiet window: min is behind for this run (a minimised window draws no frames: no screenshot, no frame profile)");
            Requested = m;
        }
        var size = StartSize(OS.GetCmdlineArgs(), s.Resolution);
        var u = DisplayServer.ScreenGetUsableRect();
        var scr = DisplayServer.ScreenGetPosition();
        var (pos, borderless) = Place(size, (u.Position.X, u.Position.Y, u.Size.X, u.Size.Y), (scr.X, scr.Y));
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, borderless);
        DisplayServer.WindowSetSize(new Vector2I(size.W, size.H));
        DisplayServer.WindowSetPosition(new Vector2I(pos.X, pos.Y));
        GD.Print($"quiet window: {size.W}x{size.H} at {pos.X},{pos.Y}{(borderless ? " borderless" : "")}, {(Requested == Mode.Minimised ? "minimised" : "behind")}");
        if (OperatingSystem.IsWindows()) Win32.Retreat(Requested == Mode.Minimised);
        else if (Requested == Mode.Minimised) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
    }

    private static class Win32
    {
        private static readonly IntPtr HwndBottom = new(1);
        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpNoOwnerZOrder = 0x200;
        private const int SwShowMinNoActive = 7, GwHwndNext = 2, GwlExStyle = -20;
        private const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern int GetWindowTextLengthW(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

        /// <summary>The engine's main window to the bottom of the stack without activating it (or minimised, not activated); if it holds the
        /// foreground, the window just under it (the one the person was using) gets it back first.</summary>
        public static void Retreat(bool minimise)
        {
            try
            {
                var me = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
                if (me == IntPtr.Zero) return;
                if (GetForegroundWindow() == me && NextUserWindow(me) is { } back) SetForegroundWindow(back);
                if (minimise) ShowWindow(me, SwShowMinNoActive);
                else SetWindowPos(me, HwndBottom, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
            }
            catch (Exception e) { GD.PrintErr($"quiet window: {e.Message}"); }
        }

        /// <summary>The first window below this one that a person works in: visible, not minimised, titled, not a tool window, another process's.</summary>
        private static IntPtr? NextUserWindow(IntPtr me)
        {
            GetWindowThreadProcessId(me, out uint mine);
            for (var h = GetWindow(me, GwHwndNext); h != IntPtr.Zero; h = GetWindow(h, GwHwndNext))
            {
                if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLengthW(h) == 0) continue;
                if (GetWindowThreadProcessId(h, out uint pid) == 0 || pid == mine) continue;
                long ex = (long)GetWindowLongPtr(h, GwlExStyle);
                if ((ex & (WsExToolWindow | WsExNoActivate)) != 0) continue;
                return h;
            }
            return null;
        }
    }
}
