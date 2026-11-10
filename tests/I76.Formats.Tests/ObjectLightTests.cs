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

    [Fact]
    public void ANightWindowRowIsFoundByTheDefinitionsStemWhateverItsCase()
    {
        Assert.NotNull(NightWindows.For("BHJHOUS1.SDF"));
        Assert.NotNull(NightWindows.For("bhjhous1.sdf"));
        Assert.Null(NightWindows.For("bfparad2.sdf"));   // the station has its own light in the data
        Assert.All(NightWindows.Table.Values, r => { Assert.InRange(r.Energy, 0.1f, 8f); Assert.InRange(r.Range, 4f, 30f); });
    }

    [InstallFact]
    public void EveryNightMissionsSceneryIsLitByItsDataOrARow()
    {
        var vfs = TestInstall.Vfs;
        var asm = new ModelAssembler(vfs);
        var got = new SortedDictionary<string, (int Own, int Windows)>();
        foreach (var msn in Directory.EnumerateFiles(TestInstall.Install.MissionDir!, "*.msn"))
        {
            var file = new I76.Formats.Mission.MissionFile(msn);
            if (I76.Formats.Mission.WorldSettings.Read(file) is not { } ws || I76.Formats.Mission.Lighting.ForHour(ws.Hour).IsDay) continue;
            var w = new MissionWorld(vfs, asm, msn);
            int own = 0, windows = 0;
            foreach (var p in w.Placements)
            {
                if (p.Definition is not { } d || !d.EndsWith(".sdf", StringComparison.OrdinalIgnoreCase) || asm.Assemble(d) is not { } m) continue;
                own += m.Lights.Count;
                if (NightWindows.For(d) is not null) windows += NightWindows.Of(m).Count;
            }
            got[Path.GetFileNameWithoutExtension(msn).ToLowerInvariant()] = (own, windows);
        }
        // the stock night missions: the gas stop (its station and two posts, a night house), Night Driver (forty light posts),
        // T12 and Suburbia (night houses, their windows lit by the table's row)
        Assert.Equal(["m04", "m12", "t06", "t12"], got.Keys);
        Assert.Equal(3, got["t06"].Own);
        Assert.Equal(40, got["m12"].Own);
        Assert.Equal(0, got["t12"].Own);
        Assert.Equal(0, got["m04"].Own);
        Assert.True(got["t12"].Windows >= 9, $"t12 windows {got["t12"].Windows}");
        Assert.True(got["m04"].Windows >= 18, $"m04 windows {got["m04"].Windows}");
        Assert.True(got["t06"].Windows >= 1, $"t06 windows {got["t06"].Windows}");
        // a window's light stands just outside its wall: within the house's reach of its origin, above the ground
        var house = asm.Assemble(new MissionWorld(vfs, asm, Directory.EnumerateFiles(TestInstall.Install.MissionDir!).First(f => Path.GetFileName(f).Equals("t12.msn", StringComparison.OrdinalIgnoreCase)))
            .Placements.First(p => p.Definition is { } d && NightWindows.For(d) is not null).Definition!)!;
        Assert.All(NightWindows.Of(house), at => { Assert.InRange(at.Y, 0.2f, 12f); Assert.InRange(new Vector2(at.X, at.Z).Length(), 0f, 40f); });
    }
}
