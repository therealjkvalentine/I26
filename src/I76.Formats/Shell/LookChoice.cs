namespace I76.Formats.Shell;

/// <summary>
/// A port setting that follows the look until the player chooses (Draw Distance, Rear Mirror Range, Brake Lamps, MG Tracers):
/// stored as a choice or none, 0 the original's (the 1997 look's default), 1 the update (the modern look's). A row over one
/// stores a choice only when its value changes, so opening a pad and pressing Done or Cancel leaves it following the look.
/// </summary>
public static class LookChoice
{
    public const int Original = 0, Updated = 1;

    /// <summary>The value in effect: the player's choice, or with none the look's (Original in the 1997 look, the update in the modern one).</summary>
    public static int Resolve(int? choice, bool look1997) => choice is { } c ? (c == Original ? Original : Updated) : look1997 ? Original : Updated;

    /// <summary>The choice to keep when a row is set to <paramref name="value"/>: the one there unless the value in effect changes.</summary>
    public static int? Choose(int? choice, bool look1997, int value) => value == Resolve(choice, look1997) ? choice : value;
}
