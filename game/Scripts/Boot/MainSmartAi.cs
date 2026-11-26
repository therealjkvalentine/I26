using Godot;

namespace I76.Game.Boot;

/// <summary>The smarter opponents' rule a game is started with (the play fork's; docs/deviations.md "Smarter opponents").</summary>
public partial class Main
{
    /// <summary>
    /// The rule as set: <c>I76_SMART_AI</c> (docs/dev-hooks.md) when given, else <c>[port] smart_ai</c> (docs/playing.md); words
    /// it does not know are warned of and add nothing. None, the original's AI.
    /// </summary>
    private static I76.Sim.Missions.SmartAi SmartAiSetting()
    {
        string? hook = System.Environment.GetEnvironmentVariable("I76_SMART_AI");
        string text = hook ?? I76.Game.Shell.ShellSettings.Load().SmartAiText;
        var bits = I76.Sim.Missions.SmartAiNames.Parse(text, out var bad);
        if (bad.Count > 0) GD.PushWarning($"{(hook is not null ? "I76_SMART_AI" : "[port] smart_ai")}: {string.Join(", ", bad)} not known (docs/playing.md, smart_ai)");
        return bits;
    }

    /// <summary>
    /// The rule a single-player mission is built by: a checkpoint's retry's or a reopened bug report's (its snapshot's build, which the
    /// restore requires), else the setting (<see cref="SmartAiSetting"/>).
    /// </summary>
    private I76.Sim.Missions.SmartAi SmartAiRule()
    {
        if (_retry is { } cp)
        {
            try { return I76.Sim.Save.SnapshotHeader.Read(cp.Snapshot).Build.SmartAi; }
            catch (System.Exception e) when (e is I76.Sim.Save.SnapshotFormatException or I76.Sim.Save.SnapshotMismatchException) { }
        }
        var bits = SmartAiSetting();
        if (bits != I76.Sim.Missions.SmartAi.None) GD.Print($"smarter opponents: {I76.Sim.Missions.SmartAiNames.Describe(bits)}");
        return bits;
    }
}
