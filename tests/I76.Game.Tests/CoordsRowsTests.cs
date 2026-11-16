using I76.Game.Sim;
using I76.Sim.Math;
using Xunit;

namespace I76.Game.Tests;

/// <summary>A rotation's rows to a Godot basis without the array: the same basis, bit for bit, as through <c>ToEngine</c>.</summary>
public class CoordsRowsTests
{
    [Fact]
    public void TheRowsGiveTheBasisTheArrayGives()
    {
        var rng = new Random(76);
        for (int n = 0; n < 1000; n++)
        {
            var q = System.Numerics.Quaternion.Normalize(new((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            var m = System.Numerics.Matrix4x4.CreateFromQuaternion(q);
            var rows = new Rows(new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33));
            var a = Coords.ToGodot(rows.ToEngine());
            var b = Coords.ToGodot(rows);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Assert.Equal(BitConverter.SingleToInt32Bits(a[i][j]), BitConverter.SingleToInt32Bits(b[i][j]));
        }
    }
}
