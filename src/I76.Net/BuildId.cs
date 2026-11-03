using System.Reflection;

namespace I76.Net;

/// <summary>
/// Which build of I26 this is: the commit's short id (seven hex digits), or empty when it cannot be told. Players of one game
/// should be on the same build; the protocol version only says whether two builds can talk at all. A build from a git
/// checkout carries the commit in its assembly's informational version (the SDK's source revision); a build from
/// <c>git archive</c> (no .git to ask) gets it written into <see cref="Archived"/> (.gitattributes export-subst).
/// I76_BUILD_ID sets it (captures of a mismatch). The join request and the session answer carry it (protocol 26).
/// </summary>
public static class BuildId
{
    /// <summary>Filled in by <c>git archive</c>; left as it is in a checkout.</summary>
    private const string Archived = "$Format:%h$";

    public const int Length = 7;

    public static string Current { get; } = Resolve(Environment.GetEnvironmentVariable("I76_BUILD_ID"), Archived,
        typeof(BuildId).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The first of: the override, the archive's commit, the informational version's "+commit"; each cut to <see cref="Length"/> hex digits.</summary>
    public static string Resolve(string? overrideId, string archived, string? informational)
    {
        if (Clean(overrideId) is { Length: > 0 } o) return o;
        if (Clean(archived) is { Length: > 0 } a) return a;
        int plus = informational?.IndexOf('+') ?? -1;
        return plus >= 0 ? Clean(informational![(plus + 1)..]) : "";
    }

    /// <summary>A commit id's first <see cref="Length"/> hex digits, lower case; empty when it is not one (an unexpanded placeholder).</summary>
    public static string Clean(string? id)
    {
        id = id?.Trim().ToLowerInvariant() ?? "";
        if (id.Length < Length || !id.Take(Length).All(char.IsAsciiHexDigit)) return "";
        return id[..Length];
    }

    /// <summary>Two builds known and not the same commit (one not known, an older peer's or a build without git, is not a difference).</summary>
    public static bool Differs(string a, string b) => Clean(a) is { Length: > 0 } x && Clean(b) is { Length: > 0 } y && x != y;

    /// <summary>How a build shows on the forms: its id, or "unknown".</summary>
    public static string Shown(string id) => Clean(id) is { Length: > 0 } c ? c : "unknown";

    /// <summary>The line every player of the game sees when one joins on another build than the host's.</summary>
    public static string JoinNotice(string name, string theirs, string hosts) =>
        $"*** {name} is on a different build ({Shown(theirs)}; the host {Shown(hosts)}) - update to play together";

    /// <summary>The roster's line for a listed game on another build than this one.</summary>
    public static string GameNotice(string hosts) => $"Host is on a different build ({Shown(hosts)}) - update to play together";
}
