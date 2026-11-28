using I76.Formats.Remix;
using I76.Formats.Vfs;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// A co-op roguetrip's roster (docs/remix-missions.md "Rosters and salvage"): the crew's gear (each slot the mean of the lobby's cars) is a
/// roster input the host sends with the job (builder v7, at the end of RoguetripState so an older reader stops before it), and the host and
/// a guest build the same cars from it.
/// </summary>
[Collection("UserMissionDir")]
public class RoguetripRosterCoopTests(ITestOutputHelper output)
{
    [Fact]
    public void TheCrewsGearTravelsWithTheJob()
    {
        string gear = CoopJob.CrewGear(["vppirna1.vcf", "vcmanta1.vcf"]);
        Assert.Equal(RemixRoster.Gear.Mean([RemixRoster.GearOfCar("vppirna1.vcf"), RemixRoster.GearOfCar("vcmanta1.vcf")])!.ToString(), gear);
        Assert.Equal("", CoopJob.CrewGear([]));
        var run = CoopRun.Start(4242);
        var job = new CoopJob(4242, RemixBuilder.Version, 2, "t07", "defend", 637742, 1.0, 2, gear);
        var w = new NetWriter();
        RoguetripState.Of(run, "abc1234", [], job, [9]).Write(w);
        var bytes = w.ToArray();
        var r = new NetReader(bytes);
        r.U8();
        var back = RoguetripState.Read(ref r);
        Assert.Equal(job, back.Job);
        Assert.Equal(0, r.Remaining);
        // a host of builder v6 sent no gear: read as none (the job is then refused by its builder number anyway)
        var old = new NetWriter();
        RoguetripState.Of(run, "abc1234", [], job with { Gear = "" }, [9]).Write(old);
        var cut = old.ToArray()[..^1];
        var ro = new NetReader(cut);
        ro.U8();
        Assert.Equal("", RoguetripState.Read(ref ro).Job!.Gear);
        // the roster is in the job: another crew's gear is another job
        Assert.NotEqual(RemixDirector.ToYaml(job.Spec()), RemixDirector.ToYaml((job with { Gear = "13.12.10.11.10" }).Spec()));
        Assert.Equal(RemixDirector.ToYaml(job.Spec()), RemixDirector.ToYaml(back.Job!.Spec()));
    }

    [Fact]
    public void HostAndGuestFieldTheSameCarsForTheCrew()
    {
        if (HostTests.InstallDir is not { } inst) return;
        var dir = Path.Combine(Path.GetTempPath(), "i26-rostercoop-" + Environment.ProcessId);
        var before = GameInstall.UserMissionDir;
        GameInstall.UserMissionDir = dir;
        try
        {
            int n = 0;
            foreach (var crew in new[] { new[] { "vppirna1.vcf" }, ["vppirna1.vcf", "vfratlr1.vcf"], ["vcmanta1.vcf", "vdstag1.vcf", "vppirna1.vcf", "vccameo1.vcf"] })
            {
                var run = CoopRun.Start(777 + crew.Length);
                foreach (var offer in run.Offers())
                {
                    offer.Gear = CoopJob.CrewGear(crew);
                    var job = run.Job(offer, crew.Length);
                    var host = job.Build(new GameInstall(inst));
                    var w = new NetWriter();
                    RoguetripState.Of(run, "abc1234", [offer], job, CoopJob.Hash(host)).Write(w);
                    var rd = new NetReader(w.ToArray());
                    rd.U8();
                    var told = RoguetripState.Read(ref rd);
                    var guest = told.Job!.Build(new GameInstall(inst));
                    Assert.Null(told.Verify(CoopJob.Hash(guest), "abc1234"));
                    Assert.Equal(host.Mission, guest.Mission);
                    var cars = job.Spec().Cars.Values.Where(c => c.Team == 0).Select(c => c.Vcf).ToList();
                    output.WriteLine($"crew {string.Join("+", crew)} gear {offer.Gear}: {job.Map} {job.Template}: {string.Join(" ", cars)}");
                    n++;
                }
            }
            Assert.True(n >= 6);
        }
        finally
        {
            GameInstall.UserMissionDir = before;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
