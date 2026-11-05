using I76.Game.Shell;
using I76.Sim.Weapons;

namespace I76.Game.Boot;

/// <summary>
/// The Play Options the combat reads, applied at each mission start, over a checkpoint's restored copies and when the Esc menu
/// closes (Main.ApplyPlayOptions): upstream's ShellSettings.ApplyTo (the cheats and the Difficulty Level), then Unlim. Ordnance. Engine-free, so the
/// game tests can call it.
/// </summary>
public static class PlayOptionsCombat
{
    public static void Apply(ShellSettings s, Combat c)
    {
        // the cheats and the Difficulty Level as upstream applies them (ShellSettings.ApplyTo: never in a network game)
        s.ApplyTo(c);
        // Unlim. Ordnance, the one rule of the simulation among the port's rows here: offline only, a network game keeps the original's
        c.UnlimitedOrdnance = s.OrdnanceLimit != 0 && !c.NetworkGame;
    }
}
