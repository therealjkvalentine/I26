using I76.Net;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The host's firewall line (<see cref="HostFirewall"/>), on rules in the form Windows keeps them. Measured on two machines:
/// a game whose program had no rule was listening on 0.0.0.0:7676, answered its own machine's LAN search and no other
/// computer's (neither the broadcast nor a query to its address); one with an Allow rule for the program answered both.
/// </summary>
public class HostFirewallTests
{
    private const string Game = @"C:\Games\I26\Godot_v4.7.2-stable_mono_win64.exe";
    private static string Rule(string action, string rest) => $"v2.30|Action={action}|Active=TRUE|Dir=In|{rest}|Name=test|";

    [Fact] public void NoRuleForTheProgramBlocksIt() =>
        Assert.True(HostFirewall.Blocks([Rule("Allow", @"Protocol=17|Profile=Private|App=C:\Games\other\i76.exe")], Game, 7676));

    [Fact] public void AnAllowRuleForTheProgramOnEveryProfileLetsItIn() =>
        Assert.False(HostFirewall.Blocks([Rule("Allow", $"App={Game.ToLowerInvariant()}")], Game, 7676));

    /// <summary>Windows' own question, answered "private networks": allowed on private, blocked on public. Which the network is, the rules do not say: no line.</summary>
    [Fact] public void AllowedOnPrivateNetworksAloneIsNotCalledBlocked() =>
        Assert.False(HostFirewall.Blocks([Rule("Allow", $"Protocol=17|Profile=Private|App={Game}"), Rule("Block", $"Protocol=17|Profile=Public|App={Game}")], Game, 7676));

    /// <summary>The question dismissed: Windows leaves a Block rule for the program on both kinds of network, which wins over any port rule.</summary>
    [Fact] public void ABlockRuleForTheProgramBlocksItWhateverElseAllows() =>
        Assert.True(HostFirewall.Blocks([Rule("Block", $"Protocol=17|Profile=Private|Profile=Public|App={Game}"), Rule("Allow", "Protocol=17|LPort=7676")], Game, 7676));

    [Fact] public void APortRuleLetsEveryProgramInOnThatPortOnly()
    {
        string[] rules = [Rule("Allow", "Protocol=17|LPort=7676"), Rule("Allow", "Protocol=17|LPort2_10=23513-23522")];
        Assert.False(HostFirewall.Blocks(rules, Game, 7676));
        Assert.True(HostFirewall.Blocks(rules, Game, 7677));
        Assert.False(HostFirewall.Blocks(rules, Game, 23520));
    }

    /// <summary>The test hosts' rule (UDP to 127.0.0.1 alone), a TCP rule, an outbound one, a disabled one and a service's open nothing for the game.</summary>
    [Fact] public void RulesThatDoNotReachTheGameDoNotCount()
    {
        string[] rules =
        [
            Rule("Allow", "Protocol=17|LPort2_10=1024-65535|LA4=127.0.0.1"),
            Rule("Allow", $"Protocol=6|App={Game}"),
            $"v2.30|Action=Allow|Active=TRUE|Dir=Out|App={Game}|Name=out|",
            $"v2.30|Action=Allow|Active=FALSE|Dir=In|App={Game}|Name=off|",
            Rule("Allow", @"Protocol=17|LPort=7676|App=%SystemRoot%\system32\svchost.exe|Svc=upnphost"),
            Rule("Allow", "Protocol=17|LPort=7676|Svc=upnphost"),
            // a packaged app's rule names no program path and is still that app's alone (a laptop held 26 of these and nothing for the game)
            Rule("Allow", "Profile=Domain|Profile=Private|Profile=Public|PFN=Microsoft.XboxGamingOverlay_8wekyb3d8bbwe|LUOwn=S-1-5-21-1-2-3-1001|EmbedCtxt=Game Bar|Platform=2:6:2|Platform2=GTEQ|Edge=TRUE"),
        ];
        Assert.True(HostFirewall.Blocks(rules, Game, 7676));
        Assert.False(HostFirewall.Blocks([.. rules, Rule("Allow", $"Protocol=17|RA4=LocalSubnet|App={Game}")], Game, 7676));
    }

    /// <summary>
    /// The shipped game, server and browser listen on every address, as before they started through <see cref="NetBind"/>
    /// (LiteNetLib's own Start() passes the same two), and only the tests narrow it: nothing in the product sets the bind
    /// address or calls <see cref="NetBind.Loopback"/>.
    /// </summary>
    [Fact] public void TheShippedGameListensOnEveryAddress()
    {
        Assert.Equal("0.0.0.0", NetBind.DefaultV4.ToString());
        Assert.Equal("::", NetBind.DefaultV6.ToString());
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
        Assert.NotNull(d);
        var narrows = new System.Text.RegularExpressions.Regex(@"NetBind\.(Loopback\(|V4\s*=[^=]|V6\s*=[^=])");
        var bad = new List<string>();
        foreach (var dir in new[] { "src", "game/Scripts", "tools" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(d!.FullName, dir), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    if (narrows.IsMatch(lines[i])) bad.Add($"{Path.GetRelativePath(d.FullName, f)}:{i + 1}: {lines[i].Trim()}");
            }
        Assert.True(bad.Count == 0, "the product narrows its bind address:\n" + string.Join("\n", bad));
    }
}
