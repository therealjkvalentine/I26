using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// A co-op crew at a job's start (docs/remix-missions.md "Roguetrip in co-op", "Seats"): at every place of note of every table, with two,
/// four and eight players, no seat stands in a structure's solid parts, on another car or inside an exit's warning square; at a place inside
/// a structure the seats line up in it, behind the lead, where it has room.
/// </summary>
[Collection("remix")]   // GameInstall.UserMissionDir is one for the process
public class RemixSeatsTests(ITestOutputHelper output)
{
    [Fact]
    public void EverySeatAtEveryPlaceOfNoteIsClear()
    {
        if (SnapshotRig.Install is not { } inst) return;
        var install = new GameInstall(inst);
        var problems = new List<string>();
        int insideSeats = 0, insideUnder = 0, places = 0;
        foreach (var t in RemixNodes.Builtin().Where(t => !t.Nitro && install.FindMission(t.Base) is not null))
            foreach (var n in t.Nodes.Where(n => n.IsSpecial))
            {
                places++;
                foreach (int seats in new[] { 2, 4, 8 })
                {
                    var crew = RemixSeats.At(install, t, n, seats);
                    if (seats == 8 || n.IsInside)
                        output.WriteLine($"{t.Base} {n.Name}{(n.IsInside ? " (inside)" : "")} x{seats}: " + string.Join(" ", crew.Select(s => $"{s.Index}:{(s.Under ? "U" : "o")}{(s.Problem.Length > 0 ? "!" : "")}")));
                    foreach (var s in crew.Where(s => s.Problem.Length > 0)) problems.Add($"{t.Base} {n.Name} x{seats} seat {s.Index} at ({s.At.X:0}, {s.At.Y:0}): {s.Problem}");
                    if (n.IsInside && seats == 2) { insideSeats++; if (crew[1].Under) insideUnder++; }
                }
            }
        output.WriteLine($"{places} places of note; inside places with two players: the second seat under the structure at {insideUnder} of {insideSeats}");
        foreach (var p in problems) output.WriteLine(p);
        Assert.Empty(problems);
    }
}
