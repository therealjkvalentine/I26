using System.Collections.Generic;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// A flood of one engine error held to a few. A debug build of the engine (the editor's binary, which the game runs from) captures
/// every script language's backtrace for each error it logs, for C# a System.Diagnostics.StackTrace with its files and lines read
/// from the PDBs, before it hands the error to the loggers: some 20-130 ms an error on the thread that raised it. A renderer error that
/// comes back at every update (a material whose texture is gone, i26-play#103: some 400 on a second mission's load) was a minute's
/// freeze of the loading card.
///
/// A flood is the same error (function and message) <see cref="Repeats"/> times within <see cref="WindowMs"/> ms. Then the engine's
/// error messages are turned off (Engine.PrintErrorMessages: the engine returns before the capture) for <see cref="PauseMs"/> ms and
/// turned on again at the first frame after; every other error is logged as it comes, as before. While off, the engine reports
/// nothing, so what was left out cannot be counted: each pause is logged once, with the error that caused it, how often it had
/// come, and the first few distinct errors of the run that came before. Errors are on again when the pause ends, whatever the game
/// does meanwhile (a mission ended, the menu, a quit: the turning back on runs on the scene tree's frames, and Main's exit too).
/// Off under --dev (or I76_DEV=1) and with I76_ERROR_FLOOD=0: every error logged as it comes.
/// </summary>
public static class ErrorFlood
{
    public const int Repeats = 3;
    public const long WindowMs = 2000, PauseMs = 3000;
    /// <summary>The distinct errors the pause's line lists (the run's first ones).</summary>
    private const int Listed = 4;

    public static readonly bool Off = System.Environment.GetEnvironmentVariable("I76_ERROR_FLOOD") == "0"
        || System.Environment.GetEnvironmentVariable("I76_DEV") == "1" || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--dev") >= 0;
    private static readonly object Lock = new();
    private static readonly Dictionary<string, (int Count, long Since)> Recent = [];
    private static readonly List<string> Distinct = [];
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static long _pausedUntil = -1;
    private static string? _pausedFor;
    private static int _pausedCount, _pauses, _errors;
    private static bool _hooked, _told;

    /// <summary>The pauses so far this run (none: no error flooded).</summary>
    public static int Pauses => _pauses;

    /// <summary>An engine error seen by the log (any thread): a flood of it pauses the engine's error messages.</summary>
    public static void Seen(string function, string message)
    {
        if (Off) return;
        string key = function + "|" + message;
        long now = Clock.ElapsedMilliseconds;
        lock (Lock)
        {
            _errors++;
            if (Distinct.Count < Listed && !Distinct.Contains(message)) Distinct.Add(message);
            var (count, since) = Recent.TryGetValue(key, out var r) && now - r.Since < WindowMs ? r : (0, now);
            Recent[key] = (++count, since);
            if (count < Repeats || _pausedUntil >= 0) return;
            (_pausedUntil, _pausedFor, _pausedCount) = (now + PauseMs, message, count);
            _pauses++;
            Recent.Clear();
        }
        Engine.PrintErrorMessages = false;
        // (logged, and turned on again, from the main thread's frames: this may be the renderer's thread)
        if (!_hooked && Engine.GetMainLoop() is SceneTree tree) { _hooked = true; Callable.From(() => tree.ProcessFrame += Tick).CallDeferred(); }
    }

    private static void Tick()
    {
        try
        {
            string? message; long until; int count;
            lock (Lock) (message, until, count) = (_pausedFor, _pausedUntil, _pausedCount);
            if (until < 0) return;
            if (!_told)
            {
                _told = true;
                string first;
                lock (Lock) first = string.Join(" | ", Distinct);
                RunLog.Print($"engine errors: '{message}' came {count} times within {WindowMs} ms: the engine's error messages off for {PauseMs} ms "
                    + $"(each error costs a C# stack; pause {_pauses} of this run, {_errors} errors logged before it; the run's first errors: {first})");
            }
            if (Clock.ElapsedMilliseconds >= until) Restore();
        }
        catch (System.Exception) { Restore(); }
    }

    /// <summary>The engine's error messages on again now (the pause's end; Main's exit).</summary>
    public static void Restore()
    {
        bool was;
        lock (Lock) { was = _pausedUntil >= 0; _pausedUntil = -1; _pausedFor = null; }
        _told = false;
        if (was) Engine.PrintErrorMessages = true;
    }
}
