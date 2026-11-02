using I76.Game.Shell;
using I76.Sim.Weapons;

namespace I76.Game.Boot;

/// <summary>
/// The Play Options the combat reads, applied at each mission start, over a checkpoint's restored copies and when the Esc menu
/// closes (Main.ApplyPlayOptions): the cheats (Unlim. Ammo / Armor / Chas. Rein.), the Difficulty Level and Original Recipe's Live Ordnance. Engine-free, so the
/// game tests can call it.
/// </summary>
public static class PlayOptionsCombat
{
    public static void Apply(ShellSettings s, Combat c)
    {
        // the cheats are for the player and never in a network session, as the original's tests; Unlim. Wiper Fluid (0x20) has
        // no reader in either game
        c.UnlimitedAmmo = (s.PlayOptions & 4) != 0 && !c.NetSession;
        if (c.Damage is I76.Sim.Damage.DamageModel dm)
        {
            dm.UnlimitedArmour = (s.PlayOptions & 8) != 0;
            dm.UnlimitedChassis = (s.PlayOptions & 0x10) != 0;
        }
        // the shell's value as the original's byte; the network rule is the combat's own (Combat.DifficultyFactor)
        c.Difficulty = s.Difficulty;
        // Original Recipe's rule of the simulation: offline only, a network game keeps the original's
        c.UnlimitedOrdnance = s.OrdnanceLimit != 0 && !c.NetworkGame;
    }
}
