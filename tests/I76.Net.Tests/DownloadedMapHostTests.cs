using System.Security.Cryptography;
using System.Text;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Net;
using I76.Server;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// A map downloaded from a network game's host can be hosted (#207): the downloaded maps the host form lists (whole downloads, not the
/// install's own copies again), and a game hosted on one plays from the downloaded folder and sends it on to a client that lacks it.
/// </summary>
public class DownloadedMapHostTests(ITestOutputHelper output)
{
    /// <summary>A whole download of these files in the cache, as <see cref="MapDownload"/> leaves one: the folder named by the map's hash.</summary>
    private static string Put(MapCache cache, string mission, params (string Name, string Text)[] files)
    {
        var list = files.Select(f => (f.Name, Data: Encoding.Latin1.GetBytes(f.Text))).ToList();
        var hash = MapFileSet.HashOf(mission, list.Select(f => (f.Name, (long)f.Data.Length, SHA256.HashData(f.Data))));
        var dir = cache.Dir(hash);
        Directory.CreateDirectory(dir);
        foreach (var (name, data) in list) File.WriteAllBytes(Path.Combine(dir, name), data);
        return dir;
    }

    [Fact]
    public void OnlyWholeDownloadsAreListed()
    {
        using var scratch = new ScratchCache();
        var cache = scratch.Cache;
        Assert.Empty(cache.Maps());       // no folder yet
        var good = Put(cache, "zzring.lvl", ("zzring.lvl", "mission"), ("zzring.ter", "terrain"), ("zzring.npt", ""));
        var race = Put(cache, "zzlap.rac", ("zzlap.rac", "race"), ("zzlap.ter", "laps"));
        // a file of it changed afterwards; a file added to it; a file lost from it
        var changed = Put(cache, "zzbad.lvl", ("zzbad.lvl", "mission"), ("zzbad.ter", "terrain"));
        File.WriteAllText(Path.Combine(changed, "zzbad.ter"), "terrair");
        var added = Put(cache, "zzadd.lvl", ("zzadd.lvl", "mission"), ("zzadd.ter", "terrain"));
        File.WriteAllText(Path.Combine(added, "zzadd.npt"), "more");
        var lost = Put(cache, "zzlost.lvl", ("zzlost.lvl", "mission"), ("zzlost.ter", "terrain"));
        File.Delete(Path.Combine(lost, "zzlost.ter"));
        // a download not finished, a folder that is no hash, a file that is no map's
        Directory.CreateDirectory(cache.PartDir(new byte[32]));
        File.WriteAllText(Path.Combine(cache.PartDir(new byte[32]), "zzpart.lvl"), "half");
        Directory.CreateDirectory(Path.Combine(cache.Root, "notahash"));
        File.WriteAllText(Path.Combine(cache.Root, "notahash", "zznot.lvl"), "x");
        var odd = Put(cache, "zzodd.lvl", ("zzodd.lvl", "mission"));
        File.WriteAllText(Path.Combine(odd, "readme.txt"), "not a map file");
        var maps = cache.Maps();
        output.WriteLine(string.Join(Environment.NewLine, maps));
        Assert.Equal(["zzlap.rac", "zzring.lvl"], maps.Select(m => m.Mission).Order());
        var ring = maps.Single(m => m.Mission == "zzring.lvl");
        Assert.Equal((good, Path.GetFileName(good), 14L), (ring.Dir, ring.HashHex, ring.Size));
        Assert.Equal(race, maps.Single(m => m.Mission == "zzlap.rac").Dir);
    }

    [Fact]
    public void TheHostsListLeavesOutWhatTheInstallHasAlready()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var host = new ScratchMapInstall(install, "dlhost");
        var name = host.SyntheticMap("m01", "zzmap");
        using var mine = new ScratchMapInstall(install, "dlmine");
        using var scratch = new ScratchCache();
        var cache = scratch.Cache;
        // the host's zzmap downloaded as a client would have it, and a race map
        var set = MapFileSet.Resolve(new GameInstall(host.Root) { MapDir = null }, name)!;
        var dir = cache.Dir(set.Hash);
        Directory.CreateDirectory(dir);
        foreach (var f in set.Files) File.Copy(f.Path, Path.Combine(dir, f.Name));
        Put(cache, "zzlap.rac", ("zzlap.rac", "race"), ("zzlap.ter", "laps"));

        var gi = new GameInstall(mine.Root);
        Assert.Equal(["zzmap.lvl"], cache.ForHost(gi, [".lvl"]).Select(m => m.Mission));
        Assert.Equal(["zzlap.rac"], cache.ForHost(gi, [".rac"]).Select(m => m.Mission));
        Assert.Equal(2, cache.ForHost(gi, GameInstall.MissionExtensions).Count);
        // an install with another zzmap of its own (an older version): the download is still offered, the map as its game had it
        mine.SyntheticMap("m01", "zzmap", salt: 0x33);
        Assert.Equal(["zzmap.lvl"], cache.ForHost(new GameInstall(mine.Root), [".lvl"]).Select(m => m.Mission));
        // the very same files in the install: listed there already, not again
        Assert.Empty(cache.ForHost(new GameInstall(host.Root), [".lvl"]));
    }

    /// <summary>
    /// The end to end: a client downloads a host's custom map; that client then hosts a game on it from its downloaded folder (its install
    /// lacks the map), and a third player, who lacks it too, is sent it and joins; the new host's own player finds it in its cache.
    /// </summary>
    [Fact]
    public void ADownloadedMapCanBeHostedAndSentOn()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var first = new ScratchMapInstall(install, "dlfirst");
        var arena = first.SyntheticMap("m01", "zzmap");
        using var second = new ScratchMapInstall(install, "dlsecond");
        using var third = new ScratchMapInstall(install, "dlthird");
        using var cache2 = new ScratchCache();
        using var cache3 = new ScratchCache();

        // the second player joins the first's game and downloads the map
        string dir;
        byte[] hash;
        using (var s1 = new NetMeleeServer(new ServerOptions { Install = first.Root, Arena = arena, Port = 0, MapBytesPerSecond = 256 * 1024 }))
        {
            s1.Start();
            hash = s1.Host.MapOffer!.Hash;
            using var c = new MeleeClient();
            c.Connect("127.0.0.1", s1.Port, "second", "vppirna1.vcf");
            for (int t = 0; t < 1500 && c.State != ClientState.Joined && c.State != ClientState.Disconnected; t++)
            {
                c.Poll();
                if (c.State == ClientState.MapOffered) c.DownloadMap(cache2.Cache);
                Thread.Sleep(10);
            }
            Assert.Equal(ClientState.Joined, c.State);
            dir = c.MapDir!;
        }

        // the second player's host form lists it (its install has no zzmap), and hosts it from the downloaded folder
        var listed = cache2.Cache.ForHost(new GameInstall(second.Root), [".lvl"]);
        Assert.Equal([(arena, dir)], listed.Select(m => (m.Mission, m.Dir)));
        using var s2 = new NetMeleeServer(new ServerOptions { Install = second.Root, Arena = listed[0].Mission, MapDir = listed[0].Dir, Port = 0, MapBytesPerSecond = 256 * 1024 });
        s2.Log += m => output.WriteLine($"[second's server] {m}");
        Assert.NotNull(s2.Host.MapOffer);
        Assert.Equal(hash, s2.Host.MapOffer!.Hash);         // the very map, sent on as it came
        Assert.All(MapFileSet.Resolve(new GameInstall(second.Root) { MapDir = dir }, arena)!.Files, f => Assert.StartsWith(dir, f.Path));
        s2.Start();

        // its own player: the cache has it
        Assert.Equal(MapChoice.Cached, cache2.Cache.Choose(new GameInstall(second.Root) { MapDir = null }, s2.Host.MapOffer, MapFileSet.DefaultMaxBytes, out var own));
        Assert.Equal(dir, own);
        // a third player who lacks it downloads it and joins
        using var c3 = new MeleeClient();
        c3.Connect("127.0.0.1", s2.Port, "third", "vppirna1.vcf");
        MapChoice? choice = null;
        for (int t = 0; t < 1500 && c3.State != ClientState.Joined && c3.State != ClientState.Disconnected; t++)
        {
            c3.Poll();
            if (c3.State == ClientState.MapOffered && choice is null)
            {
                choice = cache3.Cache.Choose(new GameInstall(third.Root) { MapDir = null }, c3.Offer!, MapFileSet.DefaultMaxBytes, out var d3);
                if (choice == MapChoice.Download) c3.DownloadMap(cache3.Cache); else c3.ReadyMap(d3);
            }
            Thread.Sleep(10);
        }
        Assert.Equal(MapChoice.Download, choice);
        Assert.Equal(ClientState.Joined, c3.State);
        Assert.Equal(hash, c3.Accept!.MapHash);
        Assert.Null(c3.Accept.MapMismatch(new GameInstall(third.Root) { MapDir = c3.MapDir }));
    }
}
