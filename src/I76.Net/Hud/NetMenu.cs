using I76.Sim.Session;

namespace I76.Net.Hud;

/// <summary>
/// The network game's Esc menu actions that act on the game. Self Destruct (new car) sets the same flag Ctrl+Del does
/// (in a network game the original's Replay item sets it): the server destroys the car with no killer, a death
/// and -500 to a floor of 0, and the player comes back in a new car. Refused while the car is dead and once the game is decided, as the key is.
/// </summary>
public static class NetMenu
{
    public static bool CanSelfDestruct(bool dead, bool decided) => !dead && !decided;

    /// <summary>The command the menu's Self Destruct sends with the next tick (none when refused).</summary>
    public static CarCommands SelfDestruct(bool dead, bool decided) => CanSelfDestruct(dead, decided) ? CarCommands.SelfDestruct : 0;
}
