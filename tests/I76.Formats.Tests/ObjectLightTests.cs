using System.Buffers.Binary;
using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Bwd2;
using I76.Formats.Geo;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// A definition's own night lights (LOBJ): the record, the engine's clamps, and the night gas station's in the mission where the
/// player stops for petrol, with the light posts beside it.
/// </summary>
public class ObjectLightTests
{
    private static byte[] Lobj(uint flags, float[] rot, Vector3 pos, float cone, float range)
    {
        var b = new byte[ObjectLight.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(b, flags);
        for (int i = 0; i < 9; i++) BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4 + 4 * i), rot[i]);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(40), pos.X);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(44), pos.Y);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(48), pos.Z);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(60), cone);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(64), range);
        return b;
    }

    private static byte[] Chunk(string tag, byte[] payload)
    {
        var b = new byte[8 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)b.Length);
        payload.CopyTo(b, 8);
        return b;
    }

    private static readonly float[] Down = [1, 0, 0, 0, 0, 1, 0, -1, 0];
    private static readonly float[] Ahead = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    [Fact]
    public void TheRecordReadsItsFrameConeAndRangeAndTheEngineClampsThem()
    {
        var file = new Bwd2File([.. Chunk("BWD2", []), .. Chunk("LOBJ", Lobj(1, Down, new Vector3(74.7f, 5, 32.5f), 1.178f, 6.6f)),
            .. Chunk("LOBJ", Lobj(1, Ahead, new Vector3(0, 0.68f, 2.3f), 0.384f, 10)), .. Chunk("LOBJ", Lobj(0, Ahead, Vector3.Zero, 3f, 40)), .. Chunk("EXIT", [])]);
        var lights = file.ObjectLights();
        Assert.Equal(3, lights.Count);
        // a canopy's: a spot pointing down, its short range brought up to 20 m, a wide cone whose spot term falls to half at its edge with n = 1
        var canopy = lights[0];
        Assert.True(canopy.Spot);
        Assert.Equal(new Vector3(74.7f, 5, 32.5f), canopy.Position);
        Assert.Equal(new Vector3(0, -1, 0), canopy.Forward);
        Assert.Equal(20f, canopy.ClampedRange);
        Assert.Equal(1f, canopy.Exponent);
        Assert.Equal(1f / 400, canopy.K, 6);
        // a car's: along the nose, range 10 (so 20), cone 22 degrees, n = 18 as the light table holds it at run time
        var car = lights[1];
        Assert.Equal(new Vector3(0, 0, 1), car.Forward);
        Assert.Equal(20f, car.ClampedRange);
        Assert.Equal(18f, car.Exponent);
        // an omni with a cone past 90 degrees and a long range: the cone held at 90, the range kept
        var omni = lights[2];
        Assert.False(omni.Spot);
        Assert.Equal(ObjectLight.MaxCone, omni.ClampedCone);
        Assert.Equal(40f, omni.ClampedRange);
        Assert.Equal(1f, omni.Exponent);
    }

    [InstallFact]
    public void TheNightGasStationAndItsLightPostsCarryTheirOwnLightsAndLitFaces()
    {
        var vfs = TestInstall.Vfs;
        var asm = new ModelAssembler(vfs);
        var msn = Directory.EnumerateFiles(TestInstall.Install.MissionDir!).First(f => Path.GetFileName(f).Equals("t06.msn", StringComparison.OrdinalIgnoreCase));
        var w = new MissionWorld(vfs, asm, msn);
        var actors = w.Mission.FsmActors().ToDictionary(a => a.Label, a => a.Object, StringComparer.OrdinalIgnoreCase);
        // the station the script names for the stop, placed once; the day station at the start (no petrol) beside it for contrast
        var station = w.Placements.Single(p => p.Object.Name.Equals(actors["petrol"], StringComparison.OrdinalIgnoreCase));
        var day = w.Placements.Single(p => p.Object.Name.Equals(actors["nogas"], StringComparison.OrdinalIgnoreCase));
        var night = asm.Assemble(station.Definition!)!;
        var dayModel = asm.Assemble(day.Definition!)!;
        // one light under the canopy, a spot pointing straight down from about 5 m, wide (67 degrees: n = 1), its range clamped to 20
        var l = Assert.Single(night.Lights);
        Assert.True(l.Spot);
        Assert.Equal(-1f, l.Forward.Y, 3);
        Assert.InRange(l.Position.Y, 4, 6);
        Assert.InRange(l.ClampedCone * 180 / MathF.PI, 60, 70);
        Assert.Equal(1f, l.Exponent);
        Assert.True(l.Range < ObjectLight.MinRange);
        Assert.Equal(20f, l.ClampedRange);
        Assert.Empty(dayModel.Lights);
        // the light posts placed within 100 m of the station: one light each, pointing down
        var posts = w.Placements.Where(p => p != station && p.Definition is { } d && asm.Assemble(d) is { Lights.Count: > 0 }
            && Vector3.Distance(new(p.Object.Position.X, 0, p.Object.Position.Z), new(station.Object.Position.X, 0, station.Object.Position.Z)) < 100).ToList();
        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal(-1f, asm.Assemble(p.Definition!)!.Lights.Single().Forward.Y, 3));
        // the night station's lit parts (the canopy's underside, the pumps, the pad) are faces the engine draws at full brightness
        static int Unlit(AssembledModel m) => m.Parts.Sum(p => p.Mesh.Faces.Count(f => f.Texture.Length > 0 && FaceLight.Unlit(f)));
        Assert.True(Unlit(night) >= 10, $"night station unlit faces {Unlit(night)}");
        Assert.True(Unlit(night) > Unlit(dayModel), $"day station unlit faces {Unlit(dayModel)}");
    }
}
