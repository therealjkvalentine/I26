using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using I76.Net;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// Nothing in this assembly may listen beyond loopback: a socket on a non-loopback address makes Windows Defender Firewall prompt
/// for every new test host exe (every git worktree has its own). Product code binds <see cref="NetBind"/>'s address; this assembly
/// sets it to loopback before any test runs.
/// </summary>
public static class TestsListenOnLoopbackOnly
{
    [ModuleInitializer] internal static void Init() => NetBind.Loopback();
}

public class LoopbackOnlyTests
{
    [Fact] public void TheProductsBindAddressIsLoopbackInTests()
    {
        Assert.Equal(IPAddress.Loopback, NetBind.V4);
        Assert.Equal(IPAddress.IPv6Loopback, NetBind.V6);
    }

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact] public void NoTestHelperBindsAWildcardAddress()
    {
        var bad = new List<string>();
        // a wildcard receive-from placeholder (new IPEndPoint(IPAddress.Any, 0) handed to Receive) binds nothing
        var placeholder = new Regex(@"var from = new IPEndPoint\(IPAddress\.Any, 0\);");
        var wildcard = new Regex(@"IPAddress\.(Any|IPv6Any)\b|new UdpClient\(\s*[A-Za-z_0-9.]+\s*\)|new TcpListener\(\s*(IPAddress\.Any|\d)|IPAddress\.Parse\(""0\.0\.0\.0""\)");
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "tests"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.EndsWith("LoopbackOnlyTests.cs")) continue;
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
                if (wildcard.IsMatch(lines[i]) && !placeholder.IsMatch(lines[i])) bad.Add($"{Path.GetRelativePath(Root(), f)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(bad.Count == 0, "a test helper binds a wildcard address:\n" + string.Join("\n", bad));
    }

    [Fact] public void ProductNetManagersStartThroughNetBind()
    {
        var bad = new List<string>();
        var direct = new Regex(@"\b_?net\.(Start|StartInManualMode)\(");
        foreach (var dir in new[] { "src/I76.Net", "src/I76.Server" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), dir), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.EndsWith("NetBind.cs")) continue;
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    if (direct.IsMatch(lines[i])) bad.Add($"{Path.GetRelativePath(Root(), f)}:{i + 1}: {lines[i].Trim()}");
            }
        Assert.True(bad.Count == 0, "start a NetManager through NetBind so tests can keep it on loopback:\n" + string.Join("\n", bad));
    }
}
