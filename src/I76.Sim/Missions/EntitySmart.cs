using System.Numerics;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' state on a car (the play fork's; <see cref="Missions.SmartAi"/>, docs/deviations.md "Smarter opponents").</summary>
public sealed partial class Entity
{
    /// <summary>
    /// Smarter-opponent behaviours this car has on top of the session's rule (<see cref="ScriptHost.SmartAi"/>): none by default. The
    /// hook for a skill tier that turns them on for one car; the tests and the headless probe set it to match one car against another.
    /// </summary>
    public SmartAi SmartAi;

    /// <summary>Gun lay (T1) steered this frame; the mission time the nose came onto the lead point (−1: not on it), for Settle (T5).</summary>
    public bool LayActive;
    public float LayAlignedAt = -1;

    /// <summary>
    /// Stuck recovery (N5): where the car last made progress and when, the back-away under way as a reverse arc toward
    /// <see cref="UnstickHeading"/>, and the 10 m cell it got stuck at, which too far's search keeps off until <see cref="BlockedUntil"/>.
    /// </summary>
    public Vector3 ProgressAt;
    public float ProgressTime;
    public bool Unsticking;
    public float UnstickHeading;
    public int BlockedX, BlockedZ;
    public float BlockedUntil = -1;

    /// <summary>Drop when chased (D1): the last drop's time, and the line held after it (heading, until).</summary>
    public float LastDropAt = -100;
    public float HoldLineUntil = -1;
    public float HoldLineHeading;

    /// <summary>
    /// The terrain group (N): the car's gap to its target when it last gained 20 m on it and when, and until when it drives without
    /// the group's caution after chasing without a gain (<see cref="Ptable.ChasePenned"/>).
    /// </summary>
    public float ChaseGap = float.MaxValue;
    public float ChaseGapTime = -1;
    public float TerrainFreeUntil = -1;
}
