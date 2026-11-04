using I76.Formats.Assembly;
using I76.Formats.Mission;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// Map Edge's Hide (EdgeWalls): on a flat plate ringed by a wall and a plateau behind it, with a mesa inside, the ring and
/// the plateau are hidden and drawn at the plate's level, the mesa and the plate are left, and ground beyond the crest that
/// already lies below the plate keeps its height.
/// </summary>
public class EdgeWallsTests
{
    private const int T = TerrainTiles.Tile;

    /// <summary>One tile: heights by sample (metres), in tile (0, 0) of the grid.</summary>
    private static TerrainTiles Tile(Func<int, int, double> height)
    {
        var ter = new byte[MissionFile.TerrainTileBytes];
        for (int z = 0; z < T; z++)
            for (int x = 0; x < T; x++)
            {
                ushort v = (ushort)Math.Clamp((int)Math.Round(height(x, z) / MissionWorld.HeightScale), 0, MissionFile.HeightMask);
                int o = (z * T + x) * 2;
                ter[o] = (byte)v; ter[o + 1] = (byte)(v >> 8);
            }
        var grid = Enumerable.Repeat(MissionFile.ZmapEmpty, MissionFile.ZmapGrid * MissionFile.ZmapGrid).ToArray();
        grid[0] = 0;
        return new TerrainTiles(ter, 1, grid);
    }

    // the plate at 20 m inside 24 samples of the edge; a wall up to 70 m over two samples at 24..22 in; the plateau at 70 m
    // beyond, except the south-east corner, which drops to 5 m; a mesa 8 samples square at 60 m in the middle
    private static double Height(int x, int z)
    {
        int inset = Math.Min(Math.Min(x, z), Math.Min(T - 1 - x, T - 1 - z));
        if (Math.Abs(x - 64) <= 4 && Math.Abs(z - 64) <= 4) return 60;
        if (inset >= 24) return 20;
        if (inset == 23) return 45;
        if (x > 100 && z > 100) return 5;
        return 70;
    }

    [Fact]
    public void WallsAndThePlateauAreHiddenToThePlateAndTheMesaStays()
    {
        var tiles = Tile(Height);
        var surf = new TerrainSurface(tiles, [new System.Numerics.Vector2(40.5f, 40.5f)]);
        var walls = EdgeWalls.Build(tiles, surf)!;
        Assert.Equal((0, 0, T, T), (walls.X0, walls.Z0, walls.W, walls.H));
        bool Hidden(int x, int z) => walls.Hidden[z * T + x];
        // the plate and the mesa (enclosed by drivable ground) are not hidden; the wall and the plateau are
        Assert.False(Hidden(40, 40));
        Assert.False(Hidden(64, 64));
        Assert.False(Hidden(60, 64));   // the mesa's west face
        Assert.False(Hidden(24, 64));   // the plate's last sample before the wall: an anchor
        Assert.True(Hidden(23, 64));
        Assert.True(Hidden(10, 64));
        Assert.True(Hidden(0, 0));
        Assert.True(Hidden(110, 110));  // the low corner is outer ground too
        int plateau = 0, hidden = 0;
        for (int z = 0; z < T; z++)
            for (int x = 0; x < T; x++)
            {
                int inset = Math.Min(Math.Min(x, z), Math.Min(T - 1 - x, T - 1 - z));
                if (inset <= 23) { plateau++; if (Hidden(x, z)) hidden++; }
            }
        Assert.Equal(plateau, hidden);
        Assert.Equal(plateau, walls.HiddenCount);
        // the foot over the hidden ground is the plate's level
        for (int z = 0; z < T; z += 7)
            for (int x = 0; x < T; x += 5)
                if (Hidden(x, z)) Assert.InRange(walls.Foot[z * T + x], 19.99, 20.01);
        // drawn: the wall and the plateau at 20 m, the plate and the mesa as they are, the low corner as it is (below the foot)
        Assert.Equal(20, walls.Ground(10 * 5, 64 * 5, 70), 3);
        Assert.Equal(20, walls.Ground(23 * 5, 64 * 5, 45), 3);
        Assert.Equal(20, walls.Ground(40 * 5, 40 * 5, 20), 3);
        Assert.Equal(60, walls.Ground(64 * 5, 64 * 5, 60), 3);
        Assert.Equal(5, walls.Ground(110 * 5, 110 * 5, 5), 3);
        // between the anchor at 24 and the hidden sample at 23 the clamp blends in
        Assert.InRange(walls.Ground(23.5 * 5, 64 * 5, 32.5), 20, 32.5);
        // the chunk boxes: the lowest drawn ground over the wall is the foot; over the plate's middle nothing is hidden
        Assert.InRange(walls.Lowest(0, 0, 32)!.Value, 4.9, 20.01);
        Assert.Null(walls.Lowest(40, 40, 8));
        var tex = walls.Texels();
        Assert.Equal(T * T * 2, tex.Length);
        Assert.Equal(1f, tex[(64 * T + 10) * 2 + 1]);
        Assert.Equal(0f, tex[(40 * T + 40) * 2 + 1]);
    }

    // a ridge across the middle from wall to wall (about z 63.5): a hill rising 8 m a sample (58°: no car climbs it, but no
    // cliff) to 72 m, or a fence of the walls' kind, 70 m flat over z 58..69 with 50 m steps on both sides
    private static double Hill(int x, int z) => Math.Max(Height(x, z), Math.Abs(z - 63.5) < 7 ? 76 - 8 * Math.Abs(z - 63.5) : 0);
    private static double Fence(int x, int z) => z is >= 58 and <= 69 ? 70 : Height(x, z);
    private static readonly System.Numerics.Vector2[] BothSides = [new(64.5f, 40.5f), new(64.5f, 90.5f)];

    [Fact]
    public void ARidgeBetweenTwoDrivableRegionsStaysThoughItJoinsTheWalls()
    {
        var tiles = Tile(Hill);
        var surf = new TerrainSurface(tiles, BothSides);
        var walls = EdgeWalls.Build(tiles, surf)!;
        bool Hidden(int x, int z) => walls.Hidden[z * T + x];
        Assert.True(surf.Open(64, 40) && surf.Open(64, 90));
        Assert.False(Hidden(64, 63));   // the ridge's middle
        Assert.False(Hidden(40, 59));   // and its south face
        Assert.False(Hidden(90, 68));   // and its north face
        Assert.True(Hidden(10, 64));    // the wall it joins is still hidden
        Assert.True(Hidden(64, 10));
        Assert.Equal(0, walls.ThinCount);   // a hill, not a fence
        Assert.Equal(72, walls.Ground(64 * 5, 63 * 5, 72), 3);
        // the wall beside the ridge's end rises to it (the clamp fades in over Ramp samples from kept outer ground, and the hill's
        // slope, which climbs onto the plateau there, is its foot); the south wall, further than that from the ridge, is at the plate's level
        Assert.InRange(walls.Ground(10 * 5, 64 * 5, 70), 25, 70);
        Assert.Equal(20, walls.Ground(64 * 5, 8 * 5, 70), 3);
        Assert.Equal(1f, walls.Mask[8 * T + 64]);
        Assert.InRange(walls.Mask[64 * T + 10], 0.05, 0.95);
    }

    // the Height plate whose north side rises to the edge as a bank of 7 m a sample (54°: no car climbs it, but no cliff) from z 104
    // to a shelf at 90 m, with a 40 m cliff onto a plateau at 130 m from z 118 (T05's valley sides, T16's banks under its fences)
    private static double Bank(int x, int z) => z < 104 ? Height(x, z) : z < 118 ? Math.Min(20 + 7 * (z - 103), 90) : 130;

    [Fact]
    public void ASlopeRisingToTheEdgeStaysAndOnlyTheCliffAboveItIsHidden()
    {
        var tiles = Tile(Bank);
        var surf = new TerrainSurface(tiles, [new System.Numerics.Vector2(40.5f, 40.5f)]);
        var walls = EdgeWalls.Build(tiles, surf)!;
        bool Hidden(int x, int z) => walls.Hidden[z * T + x];
        Assert.False(surf.Open(64, 108));   // too steep to drive
        Assert.False(Hidden(64, 108));      // but a slope: it stays as it is
        Assert.False(Hidden(64, 117));      // and the shelf above it
        Assert.Equal(55, walls.Ground(64 * 5, 108 * 5, 55), 3);
        Assert.True(walls.SlopeCount > 0);
        // the cliff and the plateau behind it are hidden, drawn down to the shelf at the cliff's foot, not to the plate
        Assert.True(Hidden(64, 120));
        Assert.Equal(90, walls.Ground(64 * 5, 124 * 5, 130), 1);
        // the walls with a cliff at the plate's edge are hidden to the plate as before
        Assert.True(Hidden(64, 10));
        Assert.Equal(20, walls.Ground(64 * 5, 10 * 5, 70), 3);
        // without the rule the bank goes with the walls, drawn down to the plate, as before
        var before = EdgeWalls.Build(tiles, surf, slopes: false)!;
        Assert.True(before.Hidden[108 * T + 64]);
        Assert.Equal(20, before.Ground(64 * 5, 108 * 5, 55), 3);
        Assert.Equal(0, before.SlopeCount);
    }

    [Fact]
    public void AThinFenceBetweenTwoDrivableRegionsGoesWithTheWalls()
    {
        // 60 m across with 50 m cliffs on both sides and its top 50 m over the drivable ground: the terraces' kind of barrier
        var tiles = Tile(Fence);
        var surf = new TerrainSurface(tiles, BothSides);
        var walls = EdgeWalls.Build(tiles, surf)!;
        bool Hidden(int x, int z) => walls.Hidden[z * T + x];
        Assert.True(Hidden(64, 63));
        Assert.True(walls.Thin![63 * T + 64]);
        Assert.True(walls.ThinCount > 0);
        Assert.False(walls.Ridge[63 * T + 64]);
        Assert.Equal(20, walls.Ground(64 * 5, 63 * 5, 70), 3);   // drawn down at the plate's level
        Assert.False(Hidden(64, 40));
        // without the rule it is kept as a ridge, as before
        var kept = EdgeWalls.Build(tiles, surf, thinBarriers: false)!;
        Assert.False(kept.Hidden[63 * T + 64]);
        Assert.Null(kept.Thin);
    }

    [Fact]
    public void AThinFenceWithARoadAtItsFootStays()
    {
        // the fence of the test above with a road along its south foot (z 56, 10 m off its cliff): it is the scenery the road
        // runs under, seen close up from it (T05's mountain road), and stays; the same road 140 m off (z 30) does not keep it
        var tiles = Tile(Fence);
        var surf = new TerrainSurface(tiles, BothSides);
        System.Numerics.Vector2[] Along(float z) => [new(26.5f, z), new(64.5f, z), new(101.5f, z)];
        var near = EdgeWalls.Build(tiles, surf, roads: [Along(56.5f)])!;
        Assert.False(near.Hidden[63 * T + 64]);
        Assert.True(near.Ridge[63 * T + 64]);
        Assert.True(near.RoadKept![63 * T + 64]);
        Assert.True(near.RoadKeptCount > 0);
        Assert.Equal(0, near.ThinCount);
        Assert.Equal(70, near.Ground(64 * 5, 63 * 5, 70), 3);
        Assert.True(near.Hidden[10 * T + 64]);   // the walls are still hidden
        var far = EdgeWalls.Build(tiles, surf, roads: [Along(30.5f)])!;
        Assert.True(far.Hidden[63 * T + 64]);
        Assert.Equal(0, far.RoadKeptCount);
        Assert.Equal(20, far.Ground(64 * 5, 63 * 5, 70), 3);
    }

    [Fact]
    public void AThinFenceBesideARoadIsNotDrawnDownThere()
    {
        // the fence of the tests above with a road 42 m off its south cliff (z 49.5), too far for RoadShare: within RoadClear (80 m,
        // to z 65) of the road's middle line it stays as it is, the clamp fades in to RoadClear + RoadFade (120 m); the wall about
        // the map is hidden though the road ends 17 m from it (x 26.5)
        var tiles = Tile(Fence);
        var surf = new TerrainSurface(tiles, BothSides);
        System.Numerics.Vector2[] road = [new(26.5f, 49.5f), new(101.5f, 49.5f)];
        var walls = EdgeWalls.Build(tiles, surf, roads: [road])!;
        Assert.Equal(0, walls.RoadKeptCount);
        Assert.False(walls.Hidden[60 * T + 64]);
        Assert.Equal(70, walls.Ground(64 * 5, 60 * 5, 70), 3);
        Assert.True(walls.Hidden[68 * T + 64]);
        Assert.InRange(walls.Ground(64 * 5, 68 * 5, 70), 20.5, 69.5);
        Assert.True(walls.Hidden[49 * T + 20]);
        Assert.Equal(20, walls.Ground(20 * 5, 49 * 5, 70), 3);
        // without the rule it is drawn down to the plate, as before
        var before = EdgeWalls.Build(tiles, surf, roads: [road], roadside: false)!;
        Assert.True(before.Hidden[60 * T + 64]);
        Assert.Equal(20, before.Ground(64 * 5, 60 * 5, 70), 3);
    }

    [Fact]
    public void AWideFenceStays()
    {
        // the same fence 300 m across (z 34..93): wider than ThinAcross from one drivable side to the other
        var tiles = Tile((x, z) => z is >= 34 and <= 93 ? 70 : Height(x, z));
        var surf = new TerrainSurface(tiles, [new System.Numerics.Vector2(64.5f, 28.5f), new System.Numerics.Vector2(64.5f, 99.5f)]);
        var walls = EdgeWalls.Build(tiles, surf)!;
        Assert.True(surf.Open(64, 28) && surf.Open(64, 99));
        Assert.False(walls.Hidden[63 * T + 64]);
        Assert.Equal(0, walls.ThinCount);
    }

    [Fact]
    public void AMapWhoseGroundReachesTheEdgeHidesNothing()
    {
        var tiles = Tile((x, z) => 20 + (x % 3 == 0 ? 0.5 : 0));
        var walls = EdgeWalls.Build(tiles, new TerrainSurface(tiles, [new System.Numerics.Vector2(64.5f, 64.5f)]))!;
        Assert.Equal(0, walls.HiddenCount);
        Assert.Equal(20.5, walls.Ground(12 * 5, 50 * 5, 20.5), 3);
    }

    // the Height plate with a pad 8 samples square at 120 m on the west plateau, cliffs all round it (T05's helicopter mesas)
    private static double Pad(int x, int z) => x is >= 6 and <= 13 && z is >= 60 and <= 67 ? 120 : Height(x, z);

    [Fact]
    public void GroundTheMissionPutsSomethingOnStays()
    {
        var tiles = Tile(Pad);
        var surf = new TerrainSurface(tiles, [new System.Numerics.Vector2(40.5f, 40.5f)]);
        Assert.False(surf.Open(9, 63));
        // nothing on the pad: hidden with the plateau
        var bare = EdgeWalls.Build(tiles, surf)!;
        Assert.True(bare.Hidden[63 * T + 9]);
        Assert.Equal(20, bare.Ground(9 * 5, 63 * 5, 120), 3);
        // a helicopter on it: the pad stays at its height, its cliff rises from the plain on a slope, the plateau away from it is hidden
        var walls = EdgeWalls.Build(tiles, surf, keep: [new System.Numerics.Vector2(9.5f, 63.5f), new System.Numerics.Vector2(40.5f, 40.5f)])!;
        Assert.False(walls.Hidden[63 * T + 9]);
        Assert.True(walls.Kept[63 * T + 9]);
        Assert.Equal(64, walls.KeptCount);
        Assert.Equal(120, walls.Ground(9 * 5, 63 * 5, 120), 3);
        Assert.InRange(walls.Ground(9 * 5, 85 * 5, 70), 21, 69);
        Assert.True(walls.Hidden[10 * T + 64]);
        Assert.Equal(20, walls.Ground(64 * 5, 10 * 5, 70), 3);
        Assert.True(walls.Hidden[63 * T + 16]);   // the plateau beside the pad is not the pad's: it is not kept, only fades
        Assert.False(walls.Kept[63 * T + 16]);
    }

    [Fact]
    public void AnObjectOnAWallKeepsTheGroundAboutIt()
    {
        var tiles = Tile(Height);
        var surf = new TerrainSurface(tiles, [new System.Numerics.Vector2(40.5f, 40.5f)]);
        var walls = EdgeWalls.Build(tiles, surf, keep: [new System.Numerics.Vector2(23.5f, 64.5f)])!;
        Assert.True(walls.Kept[64 * T + 23] && walls.Kept[64 * T + 24]);
        Assert.False(walls.Kept[64 * T + 21]);   // the 3 x 3 cells about it, not the plateau behind
        Assert.False(walls.Hidden[64 * T + 23]);
        Assert.Equal(45, walls.Ground(23 * 5, 64 * 5, 45), 3);
        Assert.Equal(20, walls.Ground(64 * 5, 10 * 5, 70), 3);
    }

    [InstallTheory]
    [InlineData("t05")]   // two helicopters on the crest of the fence north-west of the start, 110 m over the plain
    [InlineData("t11")]   // two gun emplacements
    [InlineData("s06")]   // fence posts on a cliff
    public void NothingTheMissionPutsDownStandsOnHiddenGround(string mission)
    {
        var w = new MissionWorld(TestInstall.Vfs, new ModelAssembler(TestInstall.Vfs), TestInstall.Install.FindMission(mission)!);
        var walls = EdgeWalls.Build(w.Terrain, new TerrainSurface(w.Terrain, TerrainSurface.Seeds(w)), roads: EdgeWalls.Roads(w), keep: EdgeWalls.Keep(w))!;
        var bare = EdgeWalls.Build(w.Terrain, new TerrainSurface(w.Terrain, TerrainSurface.Seeds(w)), roads: EdgeWalls.Roads(w), roadside: false)!;   // T11's and S06's stand beside a road too
        var first = EdgeWalls.Build(w.Terrain, new TerrainSurface(w.Terrain, TerrainSurface.Seeds(w)), roads: EdgeWalls.Roads(w), roadside: false, slopes: false)!;   // and T11's on a slope
        int before = 0;
        foreach (var p in EdgeWalls.Keep(w))
        {
            double x = p.X / MissionWorld.SampleScale, z = p.Y / MissionWorld.SampleScale, g = w.BilinearHeight(x, z);
            Assert.Equal(g, walls.Ground(x, z, g), 1);
            if (g - first.Ground(x, z, g) > 0.5) before++;
        }
        Assert.True(before > 0);
        Assert.InRange(walls.HiddenCount, bare.HiddenCount * 0.85, bare.HiddenCount);   // the walls themselves stay hidden, but beside the roads
    }

    [Fact]
    public void NoTilesGivesNoWalls()
    {
        var grid = Enumerable.Repeat(MissionFile.ZmapEmpty, MissionFile.ZmapGrid * MissionFile.ZmapGrid).ToArray();
        var tiles = new TerrainTiles([], 0, grid);
        Assert.Null(EdgeWalls.Build(tiles, new BlockedSurface(tiles)));
    }
}
