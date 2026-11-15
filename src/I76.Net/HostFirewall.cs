using System.Runtime.Versioning;
using Microsoft.Win32;

namespace I76.Net;

/// <summary>
/// Whether Windows Defender Firewall lets other computers reach a game hosted by this program. A host it does not is not
/// seen by a LAN search and not joined by its address, and nothing on the host says so: every query is dropped before the
/// game hears it (measured: a listen server on a machine without a rule answered its own searches and no other
/// computer's). Windows asks the first time a program listens, but not when the window is hidden or the question was
/// dismissed once, which leaves a Block rule. The rules are read from the registry, which needs no rights; other systems
/// and other firewalls are not looked at (null).
/// </summary>
public static class HostFirewall
{
    /// <summary>The host's one line (its log, and its HUD where the listing's line goes).</summary>
    public const string Hint = "Windows Firewall may be blocking this game: allow it on private networks";

    private const string Policy = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    private const string GroupPolicy = @"SOFTWARE\Policies\Microsoft\WindowsFirewall";

    /// <summary>
    /// True when the firewall is on and its rules do not let UDP <paramref name="port"/> in for this program, false when they
    /// do, null when that cannot be told (not Windows, no program path, the registry not readable).
    /// </summary>
    public static bool? Blocked(int port)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { Length: > 0 } program) return null;
        try { return BlockedOnWindows(program, port); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static bool? BlockedOnWindows(string program, int port)
    {
        var rules = new List<string>();
        bool any = false, on = false;
        foreach (var root in new[] { Policy, GroupPolicy })
        {
            using var k = Registry.LocalMachine.OpenSubKey(root + @"\FirewallRules");
            if (k is not null) { any = true; foreach (var n in k.GetValueNames()) if (k.GetValue(n) is string s) rules.Add(s); }
            foreach (var profile in new[] { "StandardProfile", "PublicProfile", "DomainProfile" })
            {
                using var p = Registry.LocalMachine.OpenSubKey($@"{root}\{profile}");
                if (p?.GetValue("EnableFirewall") is int enabled && enabled != 0) on = true;
            }
        }
        if (!any) return null;
        return on && Blocks(rules, program, port);
    }

    /// <summary>
    /// The rules' verdict for a program listening on a UDP port (each rule as the registry keeps it:
    /// <c>v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=17|Profile=Private|App=C:\...\game.exe|Name=...|</c>). On a network of
    /// one kind (private or public) the program is blocked when an active inbound Block rule for that kind names it, or when
    /// no active inbound Allow rule for that kind covers it. A rule covers it when it is for UDP or every protocol, for this
    /// program or every program (not a service or a packaged app), for this port or every port, and for any address or the local subnet. Which
    /// kind the network is the registry does not say: true only when the program is blocked on both, so a game allowed on
    /// private networks alone, as Windows' own question leaves it, is never called blocked.
    /// </summary>
    public static bool Blocks(IEnumerable<string> rules, string program, int port)
    {
        string[] kinds = ["Private", "Public"];
        var allowed = new HashSet<string>();
        var blocked = new HashSet<string>();
        foreach (var rule in rules)
        {
            var f = rule.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToLookup(p => p[0], p => p[1]);
            string One(string key) => f[key].FirstOrDefault() ?? "";
            if (!One("Active").Equals("TRUE", StringComparison.OrdinalIgnoreCase) || One("Dir") != "In") continue;
            if (One("Protocol") is { Length: > 0 } proto && proto != "17") continue;
            bool named = f["App"].Any();
            if (named && !SamePath(One("App"), program)) continue;
            var ports = f["LPort"].Concat(f["LPort2_10"]).ToList();
            if (ports.Count > 0 && !ports.Any(p => Covers(p, port))) continue;
            var on = f["Profile"].Any() ? kinds.Where(k => f["Profile"].Contains(k, StringComparer.OrdinalIgnoreCase)) : kinds;
            // an allow rule with a condition not known here (one address: the tests' loopback rule; a packaged app's PFN and
            // owner; a service; an interface; authentication) is taken not to open the game to the network
            bool plain = f.All(g => Plain.Contains(g.Key) || (g.Key is "RA4" or "RA6" && g.All(v => v == "LocalSubnet")));
            if (One("Action").Equals("Block", StringComparison.OrdinalIgnoreCase)) { if (named) blocked.UnionWith(on); }
            else if (plain && One("Action").Equals("Allow", StringComparison.OrdinalIgnoreCase)) allowed.UnionWith(on);
        }
        return kinds.All(k => blocked.Contains(k) || !allowed.Contains(k));
    }

    /// <summary>A rule's fields that narrow nothing for a program on the local network (or are looked at above).</summary>
    private static readonly HashSet<string> Plain = ["Action", "Active", "Dir", "Protocol", "Profile", "App", "LPort", "LPort2_10", "Name", "Desc", "EmbedCtxt", "Edge", "Defer"];

    private static bool Covers(string ports, int port)
    {
        var ends = ports.Split('-');
        return ends.Length switch
        {
            1 => int.TryParse(ends[0], out int p) && p == port,
            2 => int.TryParse(ends[0], out int a) && int.TryParse(ends[1], out int b) && port >= a && port <= b,
            _ => false,
        };
    }

    private static bool SamePath(string rule, string program)
    {
        try { return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(rule)), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
