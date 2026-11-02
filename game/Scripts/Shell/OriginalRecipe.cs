using Godot;

namespace I76.Game.Shell;

/// <summary>
/// The Original Recipe pad (the Esc menu's and the shell's Options Menu's): every choice the port offers between the
/// original's behaviour and an updated one, each a row of the original's first and the update after, bound to the same
/// ShellSettings field as wherever else the setting has a row. Four rows follow the look until the player chooses
/// (Original in the 1997 look, the update in the modern one): the value shown is the one in effect. docs/deviations.md
/// has a row for each update.
/// </summary>
public static class OriginalRecipe
{
    public static readonly string[] MirrorRangeNames = ["Original", "Full"];
    public static readonly string[] BrakeLampNames = ["Original", "Held"];
    public static readonly string[] MgTracerNames = ["Original", "Bolts"];
    public static readonly string[] SkyDriftNames = ["Original", "Steady"];
    public static readonly string[] DeathOrbitNames = ["Original", "Smooth"];
    public static readonly string[] MusicTrackNames = ["Original", "All"];
    public static readonly string[] OrdnanceNames = ["Original", "Unlimited"];
    public static readonly string[] PhysicsNames = ["Original", "Modern"];
    public static readonly string[] RumbleNames = ["Original", "Enhanced", "Off"];

    /// <summary>
    /// One row: its label, its values (the original's first), the value in effect in a look, a step to a value (a row that
    /// follows the look keeps following it unless the value changes), and copying the setting from one block to another
    /// (Cancel, Done's snapshot).
    /// </summary>
    public sealed record Row(string Label, string[] Names, Func<ShellSettings, bool, int> Get, Action<ShellSettings, bool, int> Set,
        Action<ShellSettings, ShellSettings> Copy, Action<ShellSettings> Reset);

    private static Row Plain(string label, string[] names, Func<ShellSettings, int> get, Action<ShellSettings, int> set) =>
        new(label, names, (s, _) => Math.Clamp(get(s), 0, names.Length - 1), (s, _, v) => set(s, v), (from, to) => set(to, get(from)), s => set(s, 0));

    private static Row Look(string label, string[] names, Func<ShellSettings, int?> get, Action<ShellSettings, int?> set) =>
        new(label, names, (s, look) => I76.Formats.Shell.LookChoice.Resolve(get(s), look),
            (s, look, v) => set(s, I76.Formats.Shell.LookChoice.Choose(get(s), look, v)),
            (from, to) => set(to, get(from)), s => set(s, null));

    /// <summary>The rows, in the pad's order: the simulation's first, then the views', the sound's and the pad's.</summary>
    public static readonly Row[] Rows =
    [
        Plain("Physics", PhysicsNames, s => s.ModernPhysics ? 1 : 0, (s, v) => s.ModernPhysics = v != 0),
        Plain("Live Ordnance", OrdnanceNames, s => s.OrdnanceLimit, (s, v) => s.OrdnanceLimit = v),
        Plain("Melee A.I.", ShellSettings.MeleeAiNames, s => (int)s.MeleeAi, (s, v) => s.MeleeAi = (I76.Sim.Missions.MeleeAiSkill)v),
        Look("Draw Distance", I76.Formats.Imaging.DrawDistance.Names, s => s.DrawDistanceChoice, (s, v) => s.DrawDistanceChoice = v),
        Plain("Field of View", I76.Sim.Views.ViewFov.Names, s => s.FieldOfView, (s, v) => s.FieldOfView = v),
        Look("Rear Mirror Range", MirrorRangeNames, s => s.MirrorRangeChoice, (s, v) => s.MirrorRangeChoice = v),
        Plain("Sky Drift", SkyDriftNames, s => s.SkyDrift, (s, v) => s.SkyDrift = v),
        Look("Brake Lamps", BrakeLampNames, s => s.BrakeLampChoice, (s, v) => s.BrakeLampChoice = v),
        Look("MG Tracers", MgTracerNames, s => s.MgTracerChoice, (s, v) => s.MgTracerChoice = v),
        Plain("Death Orbit", DeathOrbitNames, s => s.DeathOrbit, (s, v) => s.DeathOrbit = v),
        Plain("Music Tracks", MusicTrackNames, s => s.MusicTracks, (s, v) => s.MusicTracks = v),
        Plain("Rumble", RumbleNames, s => (int)s.RumbleMode, (s, v) => s.RumbleMode = (I76.Formats.Input.RumbleMode)v),
    ];

    /// <summary>Every row back to its default: the original's, or following the look.</summary>
    public static void ResetAll(ShellSettings s) { foreach (var r in Rows) r.Reset(s); }

    /// <summary>A stored word as an index into <paramref name="names"/> ("unlimited" for "Unlimited"); null when absent or unknown.</summary>
    public static int? ParseChoice(string? word, string[] names)
    {
        if (word is null) return null;
        int i = Array.FindIndex(names, n => Word(n) == word.Trim().ToLowerInvariant());
        return i >= 0 ? i : null;
    }

    public static string Word(string name) => name.ToLowerInvariant().Replace(' ', '_');

    internal static int? ReadChoice(ConfigFile c, string key, string[] names) =>
        c.HasSectionKey("port", key) ? ParseChoice((string)c.GetValue("port", key), names) : null;

    internal static void WriteChoice(ConfigFile c, string key, string[] names, int? choice)
    {
        if (choice is { } v && v >= 0 && v < names.Length) c.SetValue("port", key, Word(names[v]));
    }
}
