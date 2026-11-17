using I76.Net;
using I76.Server;
using Xunit;

namespace I76.Net.Tests;

/// <summary>The build id on the network forms (the play fork's, appended to upstream's messages): what it is, how two compare, the notice's words, and the wire.</summary>
public class BuildIdTests
{
    [Fact] public void TheOverrideThenTheArchiveThenTheCheckoutsCommit()
    {
        const string informational = "1.0.0+913a5dcf0ec5fa24906d71a9918e34b7e3d6b281";
        Assert.Equal("913a5dc", BuildId.Resolve(null, "$Format:%h$", informational));     // a checkout: the placeholder stays
        Assert.Equal("3000ef8", BuildId.Resolve(null, "3000ef8", informational));          // git archive filled it in
        Assert.Equal("3000ef8", BuildId.Resolve(null, "3000ef81a", informational));        // a longer abbreviation is cut
        Assert.Equal("abc1234", BuildId.Resolve("ABC1234", "3000ef8", informational));
        Assert.Equal("", BuildId.Resolve(null, "$Format:%h$", "1.0.0"));                   // no git at all
        Assert.Equal("", BuildId.Resolve("", "$Format:%h$", null));
    }

    [Fact] public void OnlyTwoKnownBuildsCanDiffer()
    {
        Assert.True(BuildId.Differs("abc1234", "def5678"));
        Assert.False(BuildId.Differs("abc1234", "ABC1234"));
        Assert.False(BuildId.Differs("abc1234", "abc1234deadbeef"));
        Assert.False(BuildId.Differs("", "def5678"));         // an older host's or a build without git: not a difference
        Assert.False(BuildId.Differs("abc1234", "$Format:%h$"));
    }

    [Fact] public void TheNoticesWords()
    {
        Assert.Equal("*** Skeeter is on a different build (abc1234; the host def5678) - update to play together",
            BuildId.JoinNotice("Skeeter", "abc1234", "def5678"));
        Assert.Equal("Host is on a different build (def5678) - update to play together", BuildId.GameNotice("def5678"));
        Assert.Equal("unknown", BuildId.Shown(""));
        Assert.True(BuildId.JoinNotice(new string('M', Protocol.MaxName), "abc1234", "def5678").Length <= Protocol.MaxChat);
    }

    [Fact] public void TheJoinRequestAndTheSessionAnswerCarryTheBuild()
    {
        var w = new NetWriter();
        new JoinRequest(Protocol.Version, "Skeeter", "vppirna1.vcf", 0, []) { Build = "abc1234" }.Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal("abc1234", JoinRequest.Read(ref r).Build);

        var s = new NetWriter();
        new SessionInfo(1, Protocol.Version, "g", "m05", 1, 4, false, false, 0, 0, false, [new SessionDriver("Skeeter", "vppirna1.vcf", 0)]) { Build = "def5678" }.Write(s);
        Assert.Equal("def5678", SessionInfo.TryRead(s.ToArray())!.Build);

        var a = new NetWriter();
        new JoinAccept(2, 17, "m05", 1234, "vppirna1.vcf", false, 0, 0, 5, [], []) { Build = "def5678" }.Write(a);
        var ar = new NetReader(a.ToArray());
        ar.U8();
        Assert.Equal("def5678", JoinAccept.Read(ref ar).Build);
    }

    [Fact] public void AnAnswerWithoutABuildReadsAsNotKnown()
    {
        var s = new NetWriter();
        new SessionInfo(1, Protocol.Version, "g", "m05", 1, 4, false, false, 0, 0, false, []).Write(s);
        var bytes = s.ToArray();
        Assert.Equal("", SessionInfo.TryRead(bytes[..^1])!.Build);     // the empty string's length byte gone: an upstream host's answer, which ends with its host rules
        Assert.Equal(25, Protocol.Version);
    }

    /// <summary>
    /// Protocol 25's restricted specials end upstream's answer; the fork's build follows them. A stock upstream reader stops after the
    /// specials (the build is past its last field), an upstream host's answer (no build) reads as no build here, and the answer of a
    /// fork host of protocol 24, whose build stood where the specials now are, still gives its build and no specials.
    /// </summary>
    [Fact] public void TheBuildFollowsTheRestrictedSpecials()
    {
        var specials = I76.Sim.Trip.WeaponRestrictions.None.WithSpecials([1, 3]);
        var info = new SessionInfo(1, Protocol.Version, "g", "m05", 1, 4, false, false, 0, 0, false, [], Restrictions: specials) { Build = "abc1234" };
        var s = new NetWriter();
        info.Write(s);
        var bytes = s.ToArray();
        var back = SessionInfo.TryRead(bytes)!;
        Assert.Equal("abc1234", back.Build);
        Assert.Equal([1, 3], back.Restricted.Specials);
        // an upstream host of protocol 25: the same answer without the build ("abc1234" and its length byte)
        var upstream = SessionInfo.TryRead(bytes[..^8])!;
        Assert.Equal("", upstream.Build);
        Assert.Equal([1, 3], upstream.Restricted.Specials);
        // a fork host of protocol 24: no specials, the build right after the host rules
        var old = new NetWriter();
        (info with { Version = 24, Restrictions = null }).Write(old);
        var oldBytes = old.ToArray();
        var fork24 = new byte[oldBytes.Length - 1];
        int at = oldBytes.Length - 8 - 1;   // the specials' count (0) before the build
        Array.Copy(oldBytes, fork24, at);
        Array.Copy(oldBytes, at + 1, fork24, at, 8);
        var read24 = SessionInfo.TryRead(fork24)!;
        Assert.Equal(("abc1234", 0), (read24.Build, read24.Restricted.Specials.Count));
    }

    /// <summary>A joiner on another build is let in, and every player hears it once.</summary>
    [Fact]
    public void AJoinerOnAnotherBuildIsLetInAndEveryoneIsTold()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m01", Port = 0 });
        server.Start();
        using var a = new MeleeClient();
        a.Connect("127.0.0.1", server.Port, "Alice", "vppirna1.vcf");
        using var m = new MeleeClient { Build = BuildId.Current == "0000001" ? "0000002" : "0000001" };
        m.Connect("127.0.0.1", server.Port, "Skeeter", "vppirna1.vcf");
        bool Told(MeleeClient c) => c.Events.SelectMany(x => x.Events).OfType<ChatEvent>().Any(e => e.From < 0 && e.Text.StartsWith("*** Skeeter is on a different build ("));
        for (int t = 0; t < 500 && !(Told(a) && Told(m)); t++) { a.Poll(); m.Poll(); Thread.Sleep(10); }
        Assert.Equal(ClientState.Joined, m.State);
        if (BuildId.Current.Length == 0) return;    // a build that cannot tell its own differs from none
        Assert.True(Told(a));
        Assert.True(Told(m));
    }
}
