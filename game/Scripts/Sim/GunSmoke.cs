namespace I76.Game.Sim;

/// <summary>
/// The modern look's smoke from firing guns ([port] gun_smoke, the Developer panel): off (the look before the key: a faint particle whiff
/// at the muzzle, outside the cockpit only), light or heavy: puffs of light grey-white smoke at the muzzle of each firing gun on a car,
/// the player's, the computer's and the other players' alike, drawn through the smoke layer with the damage smoke (DamageSmoke.GunPuff),
/// so the sun and sky light them (Smoke Lighting), the mirrors show them, and they show from the cockpit too. A machine gun leaves wisps,
/// at most one puff a gun every <see cref="Puff.Every"/> seconds whatever its rate, so sustained fire draws a thin drifting ribbon; a cannon
/// a distinct puff each shot. Each puff is whiter and thinner than damage smoke and short-lived: it grows gently and fades to nothing over
/// the last 40 % of its life (SmokeFade), never handed to Smoke Linger Long's haze. It leaves the muzzle with part of the car's velocity and
/// a push along the barrel, then the air holds it, so at speed it streams back past the car. The original draws no gun smoke (its guns
/// show the xmuzz flash sprite; only missiles leave smoke trails): a deliberate modern-look addition. Droppers, mines, flamers and the
/// launchers (their trails are their smoke) have none. The 1997 look never draws it. Rendering only: nothing here reaches the simulation.
/// </summary>
public static class GunSmoke
{
    public enum Level { Off, Light, Heavy }

    public static readonly string[] Words = ["off", "light", "heavy"];

    /// <summary>The repo default: light (the owner asked for it; unobtrusive in every view checked).</summary>
    public const Level Default = Level.Light;

    public static Level Setting = Default;

    /// <summary>I76_GUN_SMOKE=off|light|heavy overrides the file for a run.</summary>
    public static readonly Level? Hook = Parse(System.Environment.GetEnvironmentVariable("I76_GUN_SMOKE"), null);

    public static Level Now => Hook ?? Setting;

    /// <summary>The file's word, <paramref name="fallback"/> when absent or unknown.</summary>
    public static Level? Parse(string? word, Level? fallback) => word?.Trim().ToLowerInvariant() switch
    {
        "off" => Level.Off, "light" => Level.Light, "heavy" => Level.Heavy, _ => fallback,
    };

    /// <summary>
    /// One gun's puff: at most one every <paramref name="Every"/> seconds a gun; its size from <paramref name="Start"/> to
    /// <paramref name="Final"/> metres (on SmokeFade's easing curve), its density, its life in seconds.
    /// </summary>
    public readonly record struct Puff(float Every, float Start, float Final, float Dens, float Life);

    /// <summary>The puff for a weapon of ordnance <paramref name="kind"/> at <paramref name="level"/>, or null: none (off, or not a gun).</summary>
    public static Puff? For(uint kind, Level level)
    {
        if (level == Level.Off) return null;
        bool heavy = level == Level.Heavy;
        return kind switch
        {
            1 => heavy ? new Puff(0.10f, 0.25f, 2.0f, 0.20f, 2.0f) : new Puff(0.15f, 0.2f, 1.4f, 0.12f, 1.4f),      // machine guns: wisps
            6 => heavy ? new Puff(0.05f, 0.5f, 3.6f, 0.45f, 2.4f) : new Puff(0.05f, 0.4f, 2.6f, 0.30f, 1.8f),       // cannons: a puff a shot
            0x12 => heavy ? new Puff(0.15f, 0.15f, 1.2f, 0.15f, 1.5f) : new Puff(0.2f, 0.15f, 0.9f, 0.10f, 1.2f),   // the driver's .45s
            _ => null,
        };
    }

    /// <summary>The share of the car's velocity a puff leaves the muzzle with, and its push along the barrel (m/s).</summary>
    public const float Carried = 0.5f, Push = 1.5f;

    /// <summary>How fast the air takes a puff's own speed off it (per second): it is left behind within about half a second.</summary>
    public const float Drag = 2.5f;

    /// <summary>A puff's starting velocity (game axes, m/s): part of the car's, a push along the barrel.</summary>
    public static System.Numerics.Vector3 Start(System.Numerics.Vector3 carVel, System.Numerics.Vector3 forward) =>
        carVel * Carried + (forward.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(forward) : System.Numerics.Vector3.Zero) * Push;
}
