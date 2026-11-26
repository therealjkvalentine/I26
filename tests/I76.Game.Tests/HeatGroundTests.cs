using System.Numerics;
using I76.Formats.Assembly;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Heat Shimmer's road ripple by the ground (HeatBand.Surface, HeatTune.ByGround): on ground that runs nearly flat to the line of sight,
/// full on paved road, about half on open flat ground, less on rough ground, none on what is not ground or is seen steeply; and the paved
/// mask (HeatRoadMask) baked from the road quads with the terrain shader's own asphalt-or-dirt test. Without the engine.
/// </summary>
public class HeatGroundTests
{
    private const float Eye = 1.2f;

    /// <summary>The angle a level surface d metres ahead is seen at from the cockpit (degrees).</summary>
    private static float LevelAt(float d) => MathF.Atan2(Eye, d) * 180f / MathF.PI;

    [Fact]
    public void ALevelRoadFarAheadShimmersAndARiseAheadDoesNot()
    {
        // a level road 400 m ahead: seen 0.17 degrees off its surface, full
        Assert.Equal(1f, HeatBand.Surface(LevelAt(400), 400, paved: 1), 2);
        // the same road climbing 4 degrees toward the eye's line: seen over 4 degrees off its surface, nothing
        Assert.Equal(0f, HeatBand.Surface(LevelAt(400) + 4f, 400, paved: 1));
        // a road falling away below a hill top at a grazing angle (looked down at by 3 degrees, falling 2.9): it shimmers
        Assert.True(HeatBand.Surface(0.1f, 400, paved: 1) > 0.9f);
        // near the car nothing, whatever the angle
        Assert.Equal(0f, HeatBand.Surface(LevelAt(50), 50, paved: 1));
    }

    [Fact]
    public void PavedRoadIsFullOpenGroundHalfRoughLessPlantsNothing()
    {
        float road = HeatBand.Surface(0.2f, 500, paved: 1, upY: 1f);
        float open = HeatBand.Surface(0.2f, 500, paved: 0, upY: 1f);
        float rough = HeatBand.Surface(0.2f, 500, paved: 0, upY: 0.94f);
        float plant = HeatBand.Surface(0.2f, 500, paved: 0, upY: 0.5f);
        Assert.Equal(HeatBand.GroundShare / HeatBand.PavedShare, open / road, 3);
        Assert.InRange(HeatBand.GroundShare, 0.4f, 0.6f);   // the owner: reduced, not nothing, on open flat ground
        Assert.True(rough > 0f && rough < open);
        Assert.Equal(0f, plant);
        // the shares are the Developer panel's to change
        Assert.Equal(0.8f, HeatBand.Share(0f, 1f, groundShare: 0.8f), 3);
        Assert.Equal(0.3f, HeatBand.Share(1f, 1f, pavedShare: 0.3f), 3);
        // a road's edge, half paved, between the two
        Assert.Equal(0.75f, HeatBand.Share(0.5f, 1f), 3);
    }

    [Fact]
    public void ItStartsFarDownTheRoad()
    {
        // the owner: "it shouldn't show up as close as it currently is": from 200 m, full from 400 (section IJ's 75 and 195 put it by the nose)
        Assert.Equal(200f, HeatBand.DefaultNear);
        Assert.Equal(400f, HeatBand.DefaultFull);
        // the mirage on paved asphalt only (the owner: "only asphalt reflects")
        Assert.Equal(0f, HeatBand.MirageGroundShare);
        Assert.Equal(HeatBand.DefaultNear, HeatTune.DefaultRoadNear);
        foreach (float d in (float[])[30, 75, 150, 200]) Assert.Equal(0f, HeatBand.Surface(LevelAt(d), d, paved: 1));
        Assert.True(HeatBand.Surface(LevelAt(250), 250, paved: 1) < 0.2f);   // eases in
        Assert.True(HeatBand.Surface(LevelAt(300), 300, paved: 1) is > 0.3f and < 0.7f);
        Assert.Equal(1f, HeatBand.Surface(LevelAt(400), 400, paved: 1), 3);
        // the Developer panel's start and full distances move it
        Assert.Equal(1f, HeatBand.Distance(160, near: 75, full: 150), 3);
        Assert.Equal(0f, HeatBand.Distance(75, near: 75, full: 150));
        try
        {
            HeatTune.RoadNear = 120; HeatTune.RoadFull = 260;
            Assert.Contains("road from 120 m, full from 260 m", HeatTune.Line());
            HeatTune.Reset();
            Assert.Equal(200f, HeatTune.RoadNear);
            Assert.Equal(400f, HeatTune.RoadFull);
        }
        finally { HeatTune.Reset(); }
    }

    [Fact]
    public void TheGrazingAngleIsBetweenTheRayAndTheSurface()
    {
        var up = Vector3.UnitY;
        Assert.Equal(0f, HeatBand.Graze(Vector3.UnitX, up), 3);
        Assert.Equal(90f, HeatBand.Graze(-up, up), 3);
        // looking 1 degree down at a road climbing 1 degree ahead: 2 degrees off it
        float d = 1f * MathF.PI / 180f, r = 1f * MathF.PI / 180f;
        var ray = new Vector3(MathF.Cos(d), -MathF.Sin(d), 0);
        var n = new Vector3(-MathF.Sin(r), MathF.Cos(r), 0);
        Assert.Equal(2f, HeatBand.Graze(ray, n), 2);
    }

    [Fact]
    public void ByTheGroundIsTheDefaultAndResetPutsItBack()
    {
        try
        {
            Assert.True(HeatTune.ByGround);
            HeatTune.ByGround = false; HeatTune.GroundShare = 0.9f; HeatTune.PavedShare = 0.2f; HeatTune.MirageGround = 1f;
            Assert.Contains("band round level", HeatTune.Line());
            HeatTune.Reset();
            Assert.True(HeatTune.ByGround);
            Assert.Equal(HeatBand.GroundShare, HeatTune.GroundShare);
            Assert.Equal(HeatBand.PavedShare, HeatTune.PavedShare);
            Assert.Equal(HeatBand.MirageGroundShare, HeatTune.MirageGround);
            Assert.Contains("open ground x0.5", HeatTune.Line());
        }
        finally { HeatTune.Reset(); }
    }

    [Fact]
    public void AsphaltIsPavedDirtIsNotThePaintIs()
    {
        Assert.Equal(1f, HeatRoadMask.Paved(90, 90, 92), 2);       // grey asphalt
        Assert.Equal(0f, HeatRoadMask.Paved(150, 100, 60), 2);     // a dirt track's brown
        Assert.True(HeatRoadMask.Paved(230, 220, 40) > 0.9f);      // the yellow dash (bright paint) is the road's
    }

    private static RoadRaster.Tex Plain(byte r, byte g, byte b)
    {
        var px = new byte[8 * 8 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        return new RoadRaster.Tex(8, 8, px);
    }

    private static RoadRaster.FlatQuad Strip(float x0, float x1, float z0, float z1, int tex) =>
        new(new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1), tex, new(x0, z0), new(x1, z1));

    [Fact]
    public void TheMaskCoversThePavedRoadAndNotTheDirtTrack()
    {
        var tex = new Dictionary<int, RoadRaster.Tex> { [0] = HeatRoadMask.PavedTexture(Plain(90, 90, 92)), [1] = HeatRoadMask.PavedTexture(Plain(150, 100, 60)) };
        var quads = new List<RoadRaster.FlatQuad> { Strip(100, 110, 0, 400, 0), Strip(300, 310, 0, 400, 1) };
        var corner = new Vector2(0, 0);
        var m = HeatRoadMask.Bake(quads, null, i => tex.GetValueOrDefault(i), corner, size: 512, res: 256);
        Assert.NotNull(m);
        byte At(float x, float z) => m![(int)(z / 2) * 256 + (int)(x / 2)];
        Assert.Equal(255, At(105, 200));   // on the asphalt (rows along +Z, columns along +X)
        Assert.Equal(0, At(90, 200));      // beside it
        Assert.Equal(0, At(305, 200));     // on the dirt track
        Assert.Null(HeatRoadMask.Bake([Strip(300, 310, 0, 400, 1)], null, i => tex.GetValueOrDefault(i), corner, 512, 256));
    }

    [Fact]
    public void TheMaskSitsOnItsGridAndIsBakedAgainOnlyFarFromItsMiddle()
    {
        var c = HeatRoadMask.CornerFor(10001.3f, 44000.9f);
        Assert.Equal(0f, c.X % HeatRoadMask.Metres, 3);
        Assert.Equal(0f, c.Y % HeatRoadMask.Metres, 3);
        Assert.InRange(10001.3f - c.X, HeatRoadMask.Size / 2 - HeatRoadMask.Metres, HeatRoadMask.Size / 2 + HeatRoadMask.Metres);
        // from a re-bake's edge the ripple's reach (about 1.4 km past the camera) is still inside the square
        Assert.True(HeatRoadMask.Size / 2 - HeatRoadMask.Recentre >= 1400f);
    }
}
