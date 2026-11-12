namespace I76.Game.Ui;

/// <summary>
/// Whether the CB handset is raised for the line on the air, as the original decides it: a line the player speaks raises
/// the handset when it starts only if the binoculars' mask is off then (0x423861 for the first line, 0x4239c8 for the next
/// one in the queue: the mask flag 0x54b67c set skips the raise 0x459d60). Only the CB code moves the handset, so raising
/// the binoculars during a line leaves it up, and lowering them does not bring it up for the rest of a line that began
/// under them.
/// </summary>
public sealed class HandsetGate
{
    /// <summary>The line key for this player's own CB transmission in a network game (the talk key held).</summary>
    public static readonly object Microphone = new();

    private object? _line;
    private bool _blocked;

    /// <summary>
    /// Called every frame with the player's line on the air (null when none: another speaker's line is null too) and
    /// whether the binoculars are up; returns whether the handset is raised.
    /// </summary>
    public bool Raised(object? line, bool binoculars)
    {
        if (!ReferenceEquals(line, _line))
        {
            _line = line;
            _blocked = line is not null && binoculars;
        }
        return line is not null && !_blocked;
    }
}
