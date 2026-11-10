using System.Numerics;

namespace I76.Formats.Bwd2;

/// <summary>
/// A definition's own night light (an LOBJ chunk of a .sdf or .vdf): a white light the engine adds under a night preset to the
/// list every object takes its four strongest from, lighting the objects and the terrain clutter near it, never the ground. A
/// spot (the stock ones all are) points along its frame's z axis; its cone is the half-angle its spot term falls to half at.
/// The engine clamps the range up to 20 m and the cone into 10 to 90 degrees when it adds the light. A car's sits at the nose
/// (range 10, cone 22 degrees); a night gas station's hangs under its canopy pointing down (cone 67 degrees), and the light
/// posts beside it carry one each.
/// </summary>
public sealed record ObjectLight(bool Spot, float[] Rotation, Vector3 Position, float Cone, float Range)
{
    public const int Size = 72;
    public const float MinRange = 20f, MinCone = 10f * MathF.PI / 180f, MaxCone = 90f * MathF.PI / 180f;

    /// <summary>Where the spot points: the frame's z axis (the third row of the rotation), definition space.</summary>
    public Vector3 Forward => Vector3.Normalize(new Vector3(Rotation[6], Rotation[7], Rotation[8]));

    /// <summary>The range as the engine adds the light: at least 20 m.</summary>
    public float ClampedRange => MathF.Max(Range, MinRange);

    /// <summary>The cone as the engine adds the light: 10 to 90 degrees.</summary>
    public float ClampedCone => Math.Clamp(Cone, MinCone, MaxCone);

    /// <summary>The spot term's exponent n: ((1 + cos)/2)^n falls to half at the cone's edge, n = floor(-ln 2 / ln((1 + cos cone)/2)), at least 1.</summary>
    public float Exponent => MathF.Max(1f, MathF.Floor(-MathF.Log(2f) / MathF.Log((1f + MathF.Cos(ClampedCone)) / 2f)));

    /// <summary>The attenuation's k, 1/range², for 1/(k·d² + 1): half at the range.</summary>
    public float K => 1f / (ClampedRange * ClampedRange);

    /// <summary>The record: a flags word (bit 0 spot), the 3 x 3 rotation (rows), the position, then the cone (radians) and the range (m).</summary>
    public static ObjectLight Read(byte[] d, int at)
    {
        var rot = new float[9];
        for (int i = 0; i < 9; i++) rot[i] = Bin.F32(d, at + 4 + 4 * i);
        return new ObjectLight((Bin.U32(d, at) & 1) != 0, rot, new Vector3(Bin.F32(d, at + 40), Bin.F32(d, at + 44), Bin.F32(d, at + 48)), Bin.F32(d, at + 60), Bin.F32(d, at + 64));
    }
}
