namespace I76.Formats.Input;

/// <summary>
/// The port's two pistol keys (the original has no such command): held, one glances out of its side window as Look Left or
/// Look Right does, and that side's .45 fires as soon as the arm is up (the original's half-second raise kept), as the fire key
/// would with the glance held; let go, the head comes back ahead. input.map blocks <c>pistol_left</c> and <c>pistol_right</c>,
/// which only the port reads; their default keys are , and . where the map leaves them free (<see cref="BindingTable.WithPistolKeys"/>).
/// </summary>
public static class PistolKeys
{
    public const string Left = "pistol_left", Right = "pistol_right";

    /// <summary>Each pistol key, the glance it holds and its default key.</summary>
    public static readonly (string Command, string Glance, string Key)[] Pairs =
        [(Left, "pilot_glance_left", "Comma"), (Right, "pilot_glance_right", "Period")];

    /// <summary>The pistol key that also holds this glance, or null.</summary>
    public static string? Of(string glance)
    {
        foreach (var (cmd, g, _) in Pairs) if (g.Equals(glance, StringComparison.OrdinalIgnoreCase)) return cmd;
        return null;
    }
}
