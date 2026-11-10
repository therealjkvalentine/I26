namespace I76.Game.Sim;

/// <summary>
/// The modern look's muzzle light: the brief point light a shot sets off at the muzzle, reaching the world around the car at
/// night (the road and ground ahead and beside, scenery, other cars), falling off with distance, scaled by the weapon (a
/// cannon reaches further than a machine gun), and down to a trace in the sun. [port] muzzle_reach in percent: 100 the table
/// below; 0 the light as it was before the key (a 4 m pool, 0.8, 1.2 m ahead of the muzzle, that lit little but the firing car);
/// between and above, each of the light's figures goes along the line from that one to the table's. I76_MUZZLE_REACH overrides
/// the file for a run. The original's (retail, at night only) is an omni light of range 25 m on objects alone, never the ground
/// (docs/deviations.md); the 1997 look draws none of this.
/// </summary>
public static class MuzzleReach
{
    public const int Default = 100;
    public static int Percent = Default;
    public static readonly int? Hook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MUZZLE_REACH"), out int h) ? h : null;

    /// <summary>The file's word (0 to 200), the default when absent or not a number.</summary>
    public static int Parse(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, 0, 200) : Default;

    /// <summary>The setting now: the hook, else the file's, 0 to 200.</summary>
    public static int Setting => Math.Clamp(Hook ?? Percent, 0, 200);

    /// <summary>What fires: the light's row (by the ordnance kind, GDFC +0x5a).</summary>
    public enum Gun { Handgun, MachineGun, Cannon, Rocket, Mortar, Other }

    public static Gun Of(uint kind) => kind switch
    {
        0x12 => Gun.Handgun,
        1 => Gun.MachineGun,
        6 => Gun.Cannon,
        2 or 3 or 8 or 0x14 => Gun.Rocket,
        4 or 5 or 7 or 0x16 => Gun.Mortar,
        _ => Gun.Other,
    };

    /// <summary>
    /// One shot's light: its reach (the omni range, m), its energy, the falloff's exponent (Godot's omni attenuation: 1 the
    /// inverse distance, lower reaches further), how far ahead of the muzzle along the barrel it sits (m) and how long it lasts (s).
    /// </summary>
    public readonly record struct Flash(float Range, float Energy, float Decay, float Ahead, float Life);

    /// <summary>The light before the key (CombatView, every gun alike): what 0 gives.</summary>
    public static readonly Flash Before = new(4f, 0.8f, 1f, 1.2f, 0.05f);

    /// <summary>
    /// The table at 100, at night. The light sits ahead of the muzzle so that the firing car's own hood, behind it and edge on to
    /// it, takes little; the ground under it and ahead takes the most, and with the falloff below 1 the pool runs out to most of
    /// the range. A cannon's flash is the largest; a rocket's and a mortar's launch flash last longer than a gun's.
    /// </summary>
    public static Flash Night(Gun g) => g switch
    {
        Gun.Handgun => new(7f, 0.9f, 0.8f, 1.0f, 0.05f),
        Gun.MachineGun => new(16f, 1.7f, 0.7f, 2.8f, 0.05f),
        Gun.Cannon => new(26f, 2.8f, 0.65f, 3.5f, 0.07f),
        Gun.Rocket => new(18f, 2.0f, 0.7f, 2.4f, 0.1f),
        Gun.Mortar => new(15f, 1.7f, 0.7f, 2.0f, 0.08f),
        _ => Before,
    };

    /// <summary>What is left of the table's energy in full sun: a trace, as a flash is in daylight.</summary>
    public const float DayFloor = 0.12f;

    /// <summary>
    /// A shot's light at <paramref name="daylight"/> (LampStrength.Daylight: 1 in full sun, 0 at night) and the setting
    /// <paramref name="percent"/>: each figure from <see cref="Before"/> (0) to the table's (100), on past it to 200. The daylight
    /// scales the table's energy only, so 0 is the light before the key, day and night.
    /// </summary>
    public static Flash For(Gun g, float daylight, int percent)
    {
        float t = Math.Clamp(percent, 0, 200) / 100f;
        var n = Night(g);
        var b = Before;
        float day = LampStrength.Scale(daylight, DayFloor);
        static float Lerp(float a, float z, float k) => a + (z - a) * k;
        return new Flash(
            Math.Max(0.5f, Lerp(b.Range, n.Range, t)),
            Math.Max(0f, Lerp(b.Energy, n.Energy * day, t)),
            Math.Clamp(Lerp(b.Decay, n.Decay, t), 0.25f, 2f),
            Math.Max(0f, Lerp(b.Ahead, n.Ahead, t)),
            Math.Max(0.02f, Lerp(b.Life, n.Life, t)));
    }

    /// <summary>The light now: the setting (or the hook) at the mission's daylight.</summary>
    public static Flash Current(Gun g) => For(g, LampStrength.Current, Setting);
}
