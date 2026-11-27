using I76.Sim.Damage;

namespace I76.Game.Sim;

/// <summary>
/// [port] own_smoke: where the player's own damage smoke starts in the modern look's cockpit view. The original's damage smoke
/// starts behind the car (1 m past its rear body section, 1.2 to 1.7 m up) whatever is damaged, from a health gauge below 0.75, and the
/// gauge reads the weakest armour or chassis zone while the engine, suspension and brake are whole; so armour alone starts it. The
/// modern look draws it from the engine bay, just above the hood (DamageSmoke.EnginePoint), so from the driver's seat it rises in
/// front of the windscreen. <c>any</c> (the default) keeps that; <c>engine</c> keeps the engine bay for the player's own car in the
/// cockpit only while its engine is damaged, and otherwise starts it at the original's point behind the car, where the mirrors show
/// it. When and how much a car smokes is the original's either way; other cars, the chase view and the 1997 look are unchanged.
/// Rendering only: nothing reaches the simulation, the wire or the snapshot.
/// </summary>
public static class OwnSmoke
{
    public const string Any = "any", Engine = "engine";

    /// <summary>The setting (OriginalBehavior.Apply and the Developer panel): true for <c>engine</c>.</summary>
    public static bool EngineOnly;

    /// <summary>I76_OWN_SMOKE=engine or any: the setting for a run, whatever the file says (captures).</summary>
    private static readonly string? Hook = System.Environment.GetEnvironmentVariable("I76_OWN_SMOKE");

    /// <summary>[port] own_smoke's text: <c>engine</c> (any case) is true, anything else the default.</summary>
    public static bool Parse(string? s) => string.Equals(s?.Trim(), Engine, StringComparison.OrdinalIgnoreCase);

    /// <summary>In force: the hook where it is set, else the setting.</summary>
    public static bool On => Hook is { Length: > 0 } h ? Parse(h) : EngineOnly;

    /// <summary>Whether the car's engine has taken any damage (its hit points below its maximum).</summary>
    public static bool EngineDamaged(VehicleDamage? d) => d?.Engine is { } e && e.Hp < e.Max;

    /// <summary>
    /// Whether a smoking car's modern puffs start at the original's point behind it rather than in its engine bay: only with
    /// <paramref name="engineOnly"/>, for the player's own car seen from the cockpit, while its engine is whole.
    /// </summary>
    public static bool FromRear(bool engineOnly, bool own, bool cockpit, bool engineDamaged) => engineOnly && own && cockpit && !engineDamaged;
}
