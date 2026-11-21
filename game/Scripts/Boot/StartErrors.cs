using System.Collections.Generic;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// The engine errors of a mission's start: from the load's beginning (<see cref="Begin"/>) to <see cref="SettleMs"/> ms of play after
/// the mission is loaded (a texture the last mission left behind shows in those first seconds). One log line a start says how many
/// ("mission start: 0 engine errors"); with I76_START_ERRORS=fail any error fails the run: the errors are listed and the game quits
/// with exit code 3, for an automated run (the smoke runs of a load: docs/dev-hooks.md). Warnings are not counted.
/// </summary>
public static class StartErrors
{
    public const long SettleMs = 5000;
    /// <summary>The distinct errors the line lists.</summary>
    private const int Listed = 4;

    public static readonly bool Fail = System.Environment.GetEnvironmentVariable("I76_START_ERRORS") == "fail";
    private static readonly object Lock = new();
    private static readonly List<string> First = [];
    private static int _count;
    private static bool _open;
    private static long _settleAt = -1;

    /// <summary>A mission's load begins: its count from 0.</summary>
    public static void Begin()
    {
        lock (Lock) { _count = 0; First.Clear(); _open = true; }
        _settleAt = -1;
    }

    /// <summary>An engine error seen by the log (any thread).</summary>
    public static void Seen(string function, string message)
    {
        lock (Lock)
        {
            if (!_open) return;
            _count++;
            string what = $"{message} ({function})";
            if (First.Count < Listed && !First.Contains(what)) First.Add(what);
        }
    }

    /// <summary>The mission is loaded: the count closes <see cref="SettleMs"/> ms later (<see cref="Frame"/>).</summary>
    public static void Loaded() { if (_open && _settleAt < 0) _settleAt = (long)Time.GetTicksMsec() + SettleMs; }

    /// <summary>Main's frame: at the end of the start's window, the line; with I76_START_ERRORS=fail and an error, the quit.</summary>
    public static void Frame(SceneTree tree)
    {
        if (_settleAt < 0 || (long)Time.GetTicksMsec() < _settleAt) return;
        int n;
        string listed;
        lock (Lock) { _open = false; n = _count; listed = string.Join("; ", First); }
        _settleAt = -1;
        GD.Print($"mission start: {n} engine error{(n == 1 ? "" : "s")}{(n > 0 ? $": {listed}" : "")}");
        if (!Fail || n == 0) return;
        GD.Print($"mission start: failed (I76_START_ERRORS=fail): {n} engine errors");
        RunLog.Flush();
        tree.Quit(3);
    }
}
