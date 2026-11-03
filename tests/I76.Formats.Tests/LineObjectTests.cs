using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;
using Xunit;
using Xunit.Abstractions;

namespace I76.Formats.Tests;

/// <summary>LDEF strings (fences, tyre walls): where each link's piece stands (issue #137).</summary>
public class LineObjectTests(ITestOutputHelper log)
{
    /// <summary>
    /// A 5 m barrier piece (afence4's part: a centred mesh, its file offset 0.5, 0.65, 2.5) on a 5.6 m link along +z:
    /// the original sets the mesh's centre on the link's midpoint, lifted by its half height, so the piece runs from
    /// 0.3 to 5.3 m with its sides 0.5 m either side of the line and its foot on it; not from 0 to 5 beside the line.
    /// </summary>
    [Fact]
    public void APieceIsCentredOnItsLinkWithItsFootOnTheLine()
    {
        var line = new LineObject("afence4", new byte[0x64], [new(10, 0, 20), new(10, 0, 25.6f)]);
        var (rot, pos) = Assert.Single(LineObjects.Placements(line, (_, _) => 3, new Vector3(0.5f, 0.65f, 2.5f), 0.65f));
        var place = Affine.FromEngine(rot, pos);
        var toModel = new Affine([1, 0, 0, 0, 1, 0, 0, 0, 1], new Vector3(0.5f, 0.65f, 2.5f));
        Vector3 World(Vector3 mesh) => place.Apply(toModel.Apply(mesh));
        Assert.Equal(new Vector3(10, 3.65f, 22.8f), World(Vector3.Zero), new Vec3Comparer());
        Assert.Equal(new Vector3(9.5f, 3, 20.3f), World(new Vector3(-0.5f, -0.65f, -2.5f)), new Vec3Comparer());
        Assert.Equal(new Vector3(10.5f, 3, 25.3f), World(new Vector3(0.5f, -0.65f, 2.5f)), new Vec3Comparer());
    }

    /// <summary>S07's compound: every afence4 piece of the LDEF rows is centred on its link, foot on the line.</summary>
    [InstallFact]
    public void S07BarriersAreCentredOnTheirLinks()
    {
        var asm = new ModelAssembler(TestInstall.Vfs);
        var w = new MissionWorld(TestInstall.Vfs, asm, Path.Combine(TestInstall.Install.MissionDir!, "s07.msn"));
        var m = asm.Assemble(asm.ResolveDefinition("afence4")!)!;
        var part = m.Parts[0];
        var pieces = w.LinePlacements.Where(p => p.Object.Name == "afence4").ToList();
        log.WriteLine($"{pieces.Count} afence4 pieces");
        Assert.True(pieces.Count > 50);
        var posts = LineObjects.Read(w.Mission).Where(l => l.Name == "afence4")
            .SelectMany(l => l.Points.Zip(l.Points.Skip(1), (a, b) => (a, b))).ToList();
        Assert.Equal(posts.Count, pieces.Count);
        for (int i = 0; i < pieces.Count; i++)
        {
            var place = Affine.FromEngine(pieces[i].Object.Rotation, w.GroundedPosition(pieces[i]));
            var centre = place.Apply(part.ToModel.Apply(Vector3.Zero));
            var (a, b) = posts[i];
            var mid = new Vector3((a.X + b.X) / 2, (float)(w.BilinearHeight(a.X, a.Z) + w.BilinearHeight(b.X, b.Z)) / 2 + 0.65f, (a.Z + b.Z) / 2);
            Assert.True(Vector3.Distance(centre, mid) < 0.01f, $"piece {i}: centre {centre}, link midpoint {mid}");
        }
    }

    private sealed class Vec3Comparer : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
        public int GetHashCode(Vector3 v) => 0;
    }
}
