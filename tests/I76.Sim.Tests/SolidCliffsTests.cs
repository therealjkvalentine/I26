using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Solid cliffs (the play fork's rule, <see cref="SimWorld.SolidCliffs"/>; docs/deviations.md "Solid cliffs"; upstream #225, #226).
/// The original lifts a grounded car by its deepest wheel with no limit, and only the hull-point sweep stops a car at a face, with
/// the normal of one fixed triangle per 5 m cell: on a face across the grid a car driven into it goes on into the face and is
/// lifted metres in one substep, or ends on top or upside down. With the rule a face steeper than 60 degrees stops the car at its
/// foot; a slope of 60 degrees or less climbs as before, and a car on one of the mission script's paths keeps the original's rule.
/// The faces are made in code on one tile of M01's grid (the install's car and surfaces); without an install the tests pass empty.
/// </summary>
public class SolidCliffsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private sealed record Run(float MaxRise, bool UpsideDown, float EndRise, int Blocks, List<(Double3 Pos, Vector3 Vel)> Track);

    /// <summary>
    /// One 640 m tile of M01's grid, flat at 5 m, with a planar face of <paramref name="angle"/> degrees rising to
    /// <paramref name="height"/> m above it, its rise direction <paramref name="orient"/> degrees from +x toward +z (0: along the
    /// grid; 45 and 315: across it); the player's Piranha 50 m before the face's foot, coming in <paramref name="approach"/> degrees
    /// off the face's normal at <paramref name="mph"/>, held on the line and at the speed for 6 s more than the run-up needs.
    /// </summary>
    private Run Drive(float angle, float height, float orient, float approach, float mph, int hz, bool solid, bool exempt = false)
    {
        var install = new GameInstall(InstallDir!);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var w = new MissionWorld(vfs, asm, install.FindMission("m01")!);
        const int row = 60, col = 10, T = TerrainTiles.Tile;
        if (w.Terrain.TileAt(row, col) == MissionFile.ZmapEmpty) Assert.True(w.Terrain.AddTile(row, col, 0) >= 0);
        float per = MathF.Tan(angle * MathF.PI / 180) * 5f / 0.1f;   // tenths of a metre a sample
        var u = new Vector2(MathF.Cos(orient * MathF.PI / 180), MathF.Sin(orient * MathF.PI / 180));
        int sx0 = col * T, sz0 = row * T, mid = T / 2;
        for (int j = 0; j < T; j++)
            for (int i = 0; i < T; i++)
            {
                float proj = (i - mid) * u.X + (j - mid) * u.Y;
                w.Terrain.SetSample(sx0 + i, sz0 + j, (ushort)System.Math.Clamp((int)MathF.Round(50 + System.Math.Clamp(per * proj, 0, height * 10)), 0, 4095));
            }
        w.ForgetOutside();
        var setup = MissionSetup.Build(vfs, w, asm, "vppirna1.vcf", script: false);
        var world = setup.World;
        var v = world.Player!;
        world.Vehicles.RemoveAll(x => x != v);
        var session = new SimSession(setup, rate: SimRate.FromLabel(hz));
        session.AddDriver(v);
        world.SolidCliffs = solid;
        if (exempt) world.Step.FollowsScriptPath = c => c == v;
        var g = world.Ground;
        var face = new Vector2((sx0 + mid) * 5f, (sz0 + mid) * 5f);
        float phi = approach * MathF.PI / 180;
        var dir = new Vector2(u.X * MathF.Cos(phi) - u.Y * MathF.Sin(phi), u.X * MathF.Sin(phi) + u.Y * MathF.Cos(phi));
        var start = face - dir * 50;
        var fwd = new Vector3(dir.X, 0, dir.Y);
        v.Pos = new Double3(start.X, g.BareHeight(start.X, start.Y), start.Y);
        v.Rot = new Rows(Vector3.Cross(Vector3.UnitY, fwd), Vector3.UnitY, fwd);
        float speed = mph * 0.44704f;
        v.Vel = fwd * speed; v.Speed = speed;
        var (gh, gn) = g.Query(start.X, v.Pos.Y, start.Y);
        v.GroundNormal = gn; v.GroundHeight = gh; v.NormalLoad = gn.Y * gn.Y * gn.Y * 9.8f;
        VehicleStep.PrimeWheels(v, g);
        float y0 = (float)v.Pos.Y, maxRise = 0;
        var track = new List<(Double3, Vector3)>();
        int ticks = (int)((50f / speed + 6f) / session.TickSeconds);
        for (int t = 0; t < ticks; t++)
        {
            var p = new Vector2((float)v.Pos.X, (float)v.Pos.Z);
            var aim = start + dir * (Vector2.Dot(p - start, dir) + 20) - p;
            float err = MathF.Atan2(aim.X, aim.Y) - MathF.Atan2(v.Rot.Forward.X, v.Rot.Forward.Z);
            while (err > MathF.PI) err -= 2 * MathF.PI;
            while (err < -MathF.PI) err += 2 * MathF.PI;
            var input = new CarInput();
            if (err > 0.03f) input.Buttons |= CarButtons.SteerRight;
            else if (err < -0.03f) input.Buttons |= CarButtons.SteerLeft;
            if (v.Speed < speed) input.Buttons |= CarButtons.ThrottleUp;
            else if (v.Speed > speed + 1.5f) input.Buttons |= CarButtons.ThrottleDown;
            float before = (float)v.Pos.Y;
            session.Tick([input]);
            maxRise = MathF.Max(maxRise, (float)v.Pos.Y - before);
            track.Add((v.Pos, v.Vel));
        }
        output.WriteLine($"{angle} deg {height} m face {orient}, approach {approach}, {mph} mph, {hz} Hz, solid {solid}: max rise {maxRise:0.00}, end rise {(float)v.Pos.Y - y0:0.00}, upside down {v.Rot.Up.Y < 0}, blocks {world.Step.CliffBlocks}, end speed {v.Speed:0.0}");
        return new Run(maxRise, v.Rot.Up.Y < 0, (float)v.Pos.Y - y0, world.Step.CliffBlocks, track);
    }

    /// <summary>
    /// A 75 degree face 30 m high across the grid (rising toward 315 and 45 degrees), met square and at 45 degrees, at 20, 24 and 60 Hz:
    /// with the rule the car stops at the foot, never rises more than 1.5 m in a tick, and is not upside down.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(60)]
    public void ASteepFaceAcrossTheGridStopsTheCarAtItsFoot(int hz)
    {
        if (InstallDir is null) return;
        foreach (float orient in new[] { 315f, 45f })
            foreach (float approach in new[] { 0f, 45f })
                foreach (float mph in new[] { 20f, 60f })
                {
                    var r = Drive(75, 30, orient, approach, mph, hz, solid: true);
                    string at = $"face {orient} deg, approach {approach} deg, {mph} mph, {hz} Hz";
                    Assert.True(r.MaxRise <= 1.5f, $"{at}: rose {r.MaxRise:0.00} m in one tick");
                    Assert.False(r.UpsideDown, $"{at}: ended upside down");
                    Assert.True(r.EndRise < 3f, $"{at}: ended {r.EndRise:0.0} m up the face");
                }
    }

    /// <summary>The same faces without the rule: the original's lift takes the car up them (the reports' fault, which the rule is for).</summary>
    [Fact]
    public void WithoutTheRuleTheOriginalLiftsTheCarUpTheFace()
    {
        if (InstallDir is null) return;
        float worst = 0, highest = 0;
        foreach (float orient in new[] { 315f, 45f })
            foreach (float mph in new[] { 20f, 60f })
            {
                var r = Drive(75, 30, orient, 45, mph, 20, solid: false);
                Assert.Equal(0, r.Blocks);
                worst = MathF.Max(worst, r.MaxRise);
                highest = MathF.Max(highest, r.EndRise);
            }
        Assert.True(worst > 1.5f || highest > 3f, $"the original rose at most {worst:0.00} m a tick and ended at most {highest:0.0} m up");
    }

    /// <summary>
    /// A 40 degree slope 6 m high across the grid (rising toward 315 and 45 degrees), met at 45 degrees at 40 mph, at 20, 24 and 60 Hz:
    /// the car reaches the top with the rule as without it, tick for tick (the rule never acts on it).
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(60)]
    public void AClimbableSlopeClimbsAsInTheOriginal(int hz)
    {
        if (InstallDir is null) return;
        foreach (float orient in new[] { 315f, 45f })
        {
            var on = Drive(40, 6, orient, 45, 40, hz, solid: true);
            var off = Drive(40, 6, orient, 45, 40, hz, solid: false);
            Assert.True(on.EndRise > 4.8f, $"face {orient} deg, {hz} Hz: the car ended {on.EndRise:0.0} m up a 6 m slope");
            Assert.Equal(0, on.Blocks);
            Assert.Equal(off.Track, on.Track);
        }
    }

    /// <summary>A car the session says is on a scripted path keeps the original's rule: the same drive as with the rule off, tick for tick.</summary>
    [Fact]
    public void ACarOnAScriptedPathKeepsTheOriginalsRule()
    {
        if (InstallDir is null) return;
        var off = Drive(85, 30, 315, 45, 20, 20, solid: false);
        var exempt = Drive(85, 30, 315, 45, 20, 20, solid: true, exempt: true);
        var on = Drive(85, 30, 315, 45, 20, 20, solid: true);
        Assert.Equal(off.Track, exempt.Track);
        Assert.Equal(0, exempt.Blocks);
        Assert.NotEqual(off.Track, on.Track);
    }

    /// <summary>The rule is deterministic: the same drive into a face twice, bit for bit.</summary>
    [Fact]
    public void TheSameDriveTwiceIsTheSame()
    {
        if (InstallDir is null) return;
        var a = Drive(85, 30, 45, 45, 60, 24, solid: true);
        var b = Drive(85, 30, 45, 45, 60, 24, solid: true);
        Assert.Equal(a.Track, b.Track);
    }

    /// <summary>
    /// Who is exempt (<see cref="SimSession.FollowsScriptPath"/>): a computer car on a goto, evade or race order with a path, the
    /// detour states the AI pushes on top of one included; not a car told to attack, sit or follow; not the player's car while the
    /// keys drive it, though the script ordered it along a path before.
    /// </summary>
    [Fact]
    public void ACarIsExemptOnlyWhileItsOrderIsAScriptedPath()
    {
        if (InstallDir is null) return;
        var install = new GameInstall(InstallDir!);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, install.FindMission("t01")!), asm);
        var session = new SimSession(setup);
        session.AddDriver(setup.World.Player!);
        var host = setup.Script!.Host;
        var player = setup.Entities.Single(e => e.Vehicle == setup.World.Player);
        var ai = setup.Entities.First(e => e.Vehicle is { IsHeli: false, IsEmplacement: false } && e != player);
        var car = ai.Vehicle!;
        var start = car.Pos.ToVector3();
        List<Vector3> path = [start, start + new Vector3(0, 0, 200), start + new Vector3(50, 0, 400)];
        foreach (var order in new[] { AiOrder.Goto, AiOrder.Evade, AiOrder.Race })
        {
            host.Order(ai, null, order, path, 20);
            Assert.True(session.FollowsScriptPath(car), $"{order}");
            Assert.True(setup.World.Step.FollowsScriptPath!(car));
        }
        // a detour on the way (an escape state pushed over the path follow) keeps the order
        ai.Stack.Add(16); ai.Depth++;
        Assert.True(session.FollowsScriptPath(car));
        host.Order(ai, player, AiOrder.Attack, null, 0);
        Assert.False(session.FollowsScriptPath(car));
        host.Order(ai, null, AiOrder.Sit, null, 0);
        Assert.False(session.FollowsScriptPath(car));
        // the player's car: its goto order stays on the entity, but the keys have it
        player.Order = AiOrder.Goto; player.Path = path;
        Assert.False(session.CutsceneOf(player.Vehicle!));
        Assert.False(session.FollowsScriptPath(player.Vehicle!));
    }
}
