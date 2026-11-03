using I76.Net;
using I76.Server;
using Xunit;

namespace I76.Net.Tests;

/// <summary>The build id on the network forms (protocol 26): what it is, how two compare, the notice's words, and the wire.</summary>
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
        Assert.Equal("*** Michael is on a different build (abc1234; the host def5678) - update to play together",
            BuildId.JoinNotice("Michael", "abc1234", "def5678"));
        Assert.Equal("Host is on a different build (def5678) - update to play together", BuildId.GameNotice("def5678"));
        Assert.Equal("unknown", BuildId.Shown(""));
        Assert.True(BuildId.JoinNotice(new string('M', Protocol.MaxName), "abc1234", "def5678").Length <= Protocol.MaxChat);
    }

    [Fact] public void TheJoinRequestAndTheSessionAnswerCarryTheBuild()
    {
        var w = new NetWriter();
        new JoinRequest(Protocol.Version, "Michael", "vppirna1.vcf", 0, []) { Build = "abc1234" }.Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal("abc1234", JoinRequest.Read(ref r).Build);

        var s = new NetWriter();
        new SessionInfo(1, Protocol.Version, "g", "m05", 1, 4, false, false, 0, 0, false, [new SessionDriver("Michael", "vppirna1.vcf", 0)]) { Build = "def5678" }.Write(s);
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
        Assert.Equal("", SessionInfo.TryRead(bytes[..^1])!.Build);     // the empty string's length byte gone: a protocol 25 answer
        Assert.Equal(26, Protocol.Version);
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
        m.Connect("127.0.0.1", server.Port, "Michael", "vppirna1.vcf");
        bool Told(MeleeClient c) => c.Events.SelectMany(x => x.Events).OfType<ChatEvent>().Any(e => e.From < 0 && e.Text.StartsWith("*** Michael is on a different build ("));
        for (int t = 0; t < 500 && !(Told(a) && Told(m)); t++) { a.Poll(); m.Poll(); Thread.Sleep(10); }
        Assert.Equal(ClientState.Joined, m.State);
        if (BuildId.Current.Length == 0) return;    // a build that cannot tell its own differs from none
        Assert.True(Told(a));
        Assert.True(Told(m));
    }
}
