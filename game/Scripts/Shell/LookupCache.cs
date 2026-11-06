namespace I76.Game.Shell;

/// <summary>
/// Names read from this install's files for the shell's lists, which draw again with every answer from the network: a name found is
/// kept, and a miss (a listed game's map or car this install lacks) for <c>missFor</c>, so the lists do not probe the folders and parse
/// the files on every redraw, while a map downloaded or unpacked meanwhile is found once it has passed.
/// </summary>
public sealed class LookupCache(TimeSpan missFor, Func<long>? clockMs = null)
{
    private readonly Dictionary<string, (string Value, long Until)> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock = clockMs ?? (() => Environment.TickCount64);

    /// <summary>The name under <paramref name="key"/>, from <paramref name="look"/> when not known (empty: not found).</summary>
    public string Get(string key, Func<string> look)
    {
        long now = _clock();
        if (_known.TryGetValue(key, out var k) && (k.Value.Length > 0 || now < k.Until)) return k.Value;
        string v = look();
        _known[key] = (v, v.Length > 0 ? long.MaxValue : now + (long)missFor.TotalMilliseconds);
        return v;
    }

    /// <summary>Forgets the misses (the install gained files): the next ask looks again.</summary>
    public void ForgetMisses()
    {
        foreach (var key in _known.Where(kv => kv.Value.Value.Length == 0).Select(kv => kv.Key).ToList()) _known.Remove(key);
    }
}
