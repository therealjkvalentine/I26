namespace I76.Game.Shell;

/// <summary>
/// The base host form's tooltips (the port's; <see cref="MenuTooltip"/>, as every menu's): for the setting under the pointer, or the
/// one the pad's focus is on, what it does in a player's words, in a tooltip by it. Every row and check box of the options box has
/// one, melee and co-op, and the game type button. The strings live here, apart from the layout (<see cref="HostFormLayout"/>), so
/// the rows can move without touching them.
/// </summary>
public static class HostFormHelp
{
    /// <summary>The BOTS label's own id (the line's buttons have the form's).</summary>
    public const int BotsLabel = -1;

    /// <summary>The help line for a hotspot of the form; null for one without (the line stays blank).</summary>
    public static string? Text(int id) => id switch
    {
        BotsLabel => "Bots: cars the server drives in the free seats",
        29 => "Bots: one fewer",
        30 => "Bots: one more, up to the free seats",
        31 => "Bot skill: how well they drive: Easy, Normal or Hard",
        34 => "A.I. Cars: pick each bot's car (Random unless picked)",
        16 => "Team Play: players pick teams; no score on teammates",
        17 => "Invite Only: you let in or turn away each who asks",
        26 => "Share Maps: players without this custom map get it",
        28 => "Hidden Cars: the hidden weapons and cars are allowed",
        36 => "Car Reset: the reset key puts a car back upright",
        37 => "CB Radio: players may talk on the CB (push to talk)",
        18 => "Max Score: ticked, the game ends at this score",
        20 => "Max Score: lower the score that ends the game",
        21 => "Max Score: raise the score that ends the game",
        33 => "Sim Rate: simulation steps a second, for everyone",
        35 => "Unlimited Turrets: any number of roof turrets a car",
        39 => "Ignition Lock: nobody may switch their engine off",
        43 => "Handicaps: per-player damage levels, by you or Auto",
        27 => "Password: a word joiners must give; click to set",
        38 => "Chase Crosshair: players who have it on may see it",
        40 => "Extra Enemies: +1 of each enemy car per extra player",
        41 => "Damage Taken: what your cars take; x1 the original's",
        32 => "Game type: an arena melee or a co-op story mission",
        _ => null,
    };
}
