namespace I76.Game.Shell;

/// <summary>
/// The "cheats used" marker, two parts. The original's: set whenever the Play Options are read back with Unlim. Ammo, Armor
/// or Chas. Rein. on (bits 0x1c), when the in-mission options menu closes and after every return from the shell, so at every
/// mission start; nothing but the end menu's "Turn off Cheater Options" clears it, and it lasts as long as the process (the
/// cheat turned off in the shell does not unmark it). The port's: a Developer panel action that changed the running attempt's
/// simulation (docs/dev-hooks.md); it is for that attempt alone and goes when a mission starts fresh (a launch from the shell,
/// Replay, the panel's Start), not on a checkpoint's retry, which goes on with the attempt. A win with either set becomes the
/// cheater's end menu: no salvage, no next scene.
/// </summary>
public static class CheatMarker
{
    public const int Bits = 0x1c;

    private static bool _options, _attempt;

    public static bool Set => _options || _attempt;

    /// <summary>The Play Options as read back: a cheat bit sets the original's marker; none leaves it as it was.</summary>
    public static void Read(int playOptions) { if ((playOptions & Bits) != 0) _options = true; }

    /// <summary>A Developer panel action that changed the attempt's simulation: marks this attempt.</summary>
    public static void Mark() => _attempt = true;

    /// <summary>A mission started fresh (not a checkpoint's retry): the attempt's part goes; the options' part is the original's and stays.</summary>
    public static void NewAttempt() => _attempt = false;

    /// <summary>"Turn off Cheater Options", or a fresh process (a game switch): both parts.</summary>
    public static void Clear() { _options = false; _attempt = false; }
}
