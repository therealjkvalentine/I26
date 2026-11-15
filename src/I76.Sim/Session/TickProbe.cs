namespace I76.Sim.Session;

/// <summary>The parts of a tick the tick bench times apart (docs/perf/tick-bench.md).</summary>
public enum TickSection
{
    /// <summary>The whole of <see cref="SimSession.Tick"/> (the others nest in it).</summary>
    Tick,
    /// <summary>The drivers' keys, controls, triggers, selection and arms.</summary>
    Controls,
    /// <summary>The AI's order machines (<see cref="Missions.Ptable.Issue"/>) and the world's placeholder drivers.</summary>
    Ai,
    /// <summary>The collision pass: every car against the later cars and the scenery.</summary>
    Contacts,
    /// <summary>Each car's update: the physics substeps, the far path, the helicopters.</summary>
    Physics,
    /// <summary>The mission script (the FSM's threads, a melee's frame, co-op, the camera and CB), less the AI inside it.</summary>
    Script,
    /// <summary>The weapons and rounds in flight (emplacements, the combat frame, the session's specials).</summary>
    Weapons,
    /// <summary>A damage packet applied (<see cref="Damage.DamageModel.Apply(object, Weapons.DamagePacket, System.Numerics.Vector3, int)"/>).</summary>
    Damage,
}

/// <summary>What the tick bench plugs in: told where each section starts and ends (they nest).</summary>
public interface ITickTimer
{
    void Begin(TickSection s);
    void End(TickSection s);
}

/// <summary>
/// The tick bench's hook (tools/I76.Cli tick-bench). Null in the game and in every test: then each mark is a load and a
/// test, and nothing the sim computes depends on it either way. The sim never reads a clock; the bench's timer does.
/// Not thread-safe: the bench runs one session on one thread.
/// </summary>
public static class TickProbe
{
    public static ITickTimer? Timer;

    public static void Begin(TickSection s) => Timer?.Begin(s);
    public static void End(TickSection s) => Timer?.End(s);
}
