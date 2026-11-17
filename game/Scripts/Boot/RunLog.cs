using System.Collections.Generic;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_RUN_LOG=PATH: the measuring hooks' lines (the build trace's stamps, the hitch lines, the slow steps, the movie's lines) kept in
/// memory and written to PATH when the mission is ready (READY), when the loading card goes and when the game quits, instead of
/// printed one by one: a print to the console costs the main thread milliseconds (about 30 ms each through the console wrapper's
/// pipe), which the load's own frames then carried. Unset, each line is printed as before.
/// </summary>
public static class RunLog
{
    public static readonly string? Path = System.Environment.GetEnvironmentVariable("I76_RUN_LOG") is { Length: > 0 } p ? p : null;
    private static readonly List<string> Lines = [];
    private static readonly object Lock = new();
    private static bool _hooked;

    /// <summary>A line of the run's log: kept (I76_RUN_LOG) or printed.</summary>
    public static void Print(string line)
    {
        if (Path is null) { GD.Print(line); return; }
        lock (Lock)
        {
            Lines.Add(line);
            if (!_hooked) { _hooked = true; System.AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); }
        }
    }

    /// <summary>The lines kept so far appended to the file (at READY, the card's end and the quit: not every frame).</summary>
    public static void Flush()
    {
        if (Path is null) return;
        lock (Lock)
        {
            if (Lines.Count == 0) return;
            try { System.IO.File.AppendAllLines(Path, Lines); Lines.Clear(); }
            catch (System.IO.IOException ex) { GD.PrintErr($"run log: {ex.Message}"); }
        }
    }
}
