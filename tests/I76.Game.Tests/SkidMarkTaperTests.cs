using I76.Game.Sim;
using Xunit;
using SN = System.Numerics;

namespace I76.Game.Tests;

/// <summary>
/// The modern skid mark's fade toward its end (SkidMarks.EndTaper) sums the length to the end only while it is under the fade's reach: the same
/// fade as the whole sum's for every point (the whole sum for every point made a strip's rebuild quadratic in its points).
/// </summary>
public class SkidMarkTaperTests
{
    private static float Whole(IReadOnlyList<SN.Vector3> p, int k)
    {
        float toEnd = 0;
        for (int j = k; j < p.Count - 1; j++) { var d = p[j + 1] - p[j]; toEnd += System.MathF.Sqrt(d.X * d.X + d.Z * d.Z); }
        return Godot.Mathf.SmoothStep(0f, 1f, Godot.Mathf.Clamp(toEnd / 0.5f, 0f, 1f));
    }

    [Fact]
    public void TheFadeIsTheWholeSumsAtEveryPoint()
    {
        var rng = new System.Random(76);
        for (int strip = 0; strip < 200; strip++)
        {
            var p = new List<SN.Vector3>();
            var at = new SN.Vector3(rng.NextSingle() * 1000, 0, rng.NextSingle() * 1000);
            int n = rng.Next(2, 400);
            for (int i = 0; i < n; i++)
            {
                p.Add(at);
                // steps of the mark's 0.14 m, some shorter, some repeated points
                float step = rng.Next(5) == 0 ? 0 : 0.14f * (0.2f + rng.NextSingle());
                float a = rng.NextSingle() * 6.28f;
                at += new SN.Vector3(step * System.MathF.Cos(a), rng.NextSingle() * 0.01f, step * System.MathF.Sin(a));
            }
            for (int k = 0; k < n; k++) Assert.Equal(Whole(p, k), SkidMarks.EndTaper(p, k));
        }
    }
}
