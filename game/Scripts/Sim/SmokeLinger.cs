namespace I76.Game.Sim;

/// <summary>
/// How long the modern look's smoke hangs in the air ([port] smoke_linger, in percent of each smoke's own life): the cars' damage
/// smoke and the burning wrecks' (DamageSmoke, WreckFire), the rockets' and the Ball O'Flame's trails (RocketSmoke) and the
/// explosions' smoke (ModernFx). 100 is each smoke's life as it was (the 1997 look always draws the original's: 20 ticks a puff, the
/// contrails' 23 sections); the default, 150, lets it linger half as long again. A puff's whole life stretches, so it swells and
/// erodes away over the longer time (the shaders thin it by its age over its life) rather than holding and then going at once; the
/// pools that hold the puffs grow with it, so a melee's trails are as dense as before, only longer. Purely visual: nothing goes back
/// into the simulation or onto the wire. Not the Nitro chemical mortar's cloud, whose puffs mark where the round still harms, nor the
/// guns' whiffs at the muzzle. I76_SMOKE_LINGER=N overrides the file for a run (a before | after).
/// </summary>
public static class SmokeLinger
{
    public const int Default = 150, Min = 100, Max = 300;
    /// <summary>The steps the Developer panel offers.</summary>
    public static readonly int[] Steps = [100, 125, 150, 200, 300];
    public static int Percent = Default;
    public static readonly int? Hook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_SMOKE_LINGER"), out int h) ? h : null;

    /// <summary>The file's word (100 to 300), the default when absent or not a number.</summary>
    public static int Parse(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, Min, Max) : Default;

    /// <summary>The setting now: the hook, else the file's, 100 to 300.</summary>
    public static int Setting => Math.Clamp(Hook ?? Percent, Min, Max);

    /// <summary>The factor on the modern look's smoke lives now (1 to 3).</summary>
    public static float Factor => Setting / 100f;

    /// <summary>The largest factor: the pools are made this big and used up to <see cref="Factor"/> of their base size.</summary>
    public const float MaxFactor = Max / 100f;

    /// <summary>A pool's size in use for the factor now, of a base size (the size at 100).</summary>
    public static int Pool(int baseSize) => Math.Max(1, (int)(baseSize * Factor + 0.5f));

    /// <summary>A pool's size as made: room for the largest factor.</summary>
    public static int PoolMax(int baseSize) => (int)(baseSize * MaxFactor + 0.5f);
}
