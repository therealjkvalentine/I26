using I76.Formats.Geo;

namespace I76.Formats.Assembly;

/// <summary>
/// Where the 3D cockpit's gauge needle (needle.geo) turns. The strip is symmetric about its own origin, 107 mm long, its
/// narrow +z end the reading end; at its VLOC (the dial's hub) the original turns it with the reading side about 0.69 of
/// its length and the tail 0.31: measured in the original (retail 1.0, software renderer, 640x480, T01 by day, both
/// gauges, parked and at 20 m/s; the pivot found where the two poses' lines cross, relative to the dial's hub)
/// 0.70 and 0.71 on the speedometer, 0.66 and 0.70 on the tachometer. Turned about the strip's middle, both sides
/// were alike and the needle did not show which way it read.
/// </summary>
public static class CockpitNeedle
{
    /// <summary>The share of the needle's length on the reading side of the pivot.</summary>
    public const float ReadingShare = 0.69f;

    /// <summary>The shift along the strip's +z (toward its reading end) that puts the pivot, the part's origin, at <see cref="ReadingShare"/>.</summary>
    public static float Shift(GeoMesh needle)
    {
        if (needle.Vertices.Length == 0) return 0;
        float lo = needle.Vertices.Min(v => v.Z), hi = needle.Vertices.Max(v => v.Z);
        return (hi - lo) * ReadingShare - hi;
    }
}
