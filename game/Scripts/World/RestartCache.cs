namespace I76.Game.World;

/// <summary>The restart cache's keys (<see cref="RestartCache{T}"/>).</summary>
public static class RestartCache
{
    /// <summary>A key of its parts in order (a part's own separators escaped, so two lists of parts never give one key).</summary>
    public static string KeyOf(params object?[] parts) =>
        string.Join('|', parts.Select(p => p switch
        {
            null => "-",
            bool b => b ? "1" : "0",
            _ => (System.Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture) ?? "").Replace("%", "%25").Replace("|", "%7C").Replace("-", "%2D"),
        }));
}

/// <summary>
/// What the last mission's build made that the same mission's next build can take as it is (Replay, the mission chosen again):
/// one entry, under a key of the mission and everything its textures are made from (MissionBuilder.KitKey). A build of another
/// mission (another key) drops it first, so at most one mission's worth is held; it is dropped too when it holds more than
/// <see cref="CeilingBytes"/> or the machine's memory is short, checked when a mission is about to load (<see cref="Take"/>) and
/// when the player is back at the shell (<see cref="Trim"/>). I76_RESTART_CACHE=0 keeps nothing (every build makes its own, as before).
/// </summary>
public sealed class RestartCache<T>(long ceilingBytes, Func<T, long> bytes, Func<bool> short_) where T : class
{
    public long CeilingBytes { get; } = ceilingBytes;
    public string? Key { get; private set; }
    private T? _value;

    /// <summary>Why the last entry went ("another mission", "over the ceiling", "memory short"), for the log.</summary>
    public string? Dropped { get; private set; }

    public bool Holds => _value is not null;

    /// <summary>What the cache holds for <paramref name="key"/>, or null; an entry under another key is dropped.</summary>
    public T? Take(string key)
    {
        Trim();
        if (_value is null) return null;
        if (Key == key) return _value;
        Drop("another mission");
        return null;
    }

    /// <summary>The entry the build made, kept under its key (in place of any other).</summary>
    public void Keep(string key, T value)
    {
        Key = key;
        _value = value;
        Dropped = null;
    }

    /// <summary>Drops the entry when it holds more than the ceiling or the machine's memory is short; true if it did.</summary>
    public bool Trim()
    {
        if (_value is null) return false;
        if (bytes(_value) > CeilingBytes) { Drop("over the ceiling"); return true; }
        if (short_()) { Drop("memory short"); return true; }
        return false;
    }

    public void Drop(string why)
    {
        if (_value is null) return;
        _value = null;
        Key = null;
        Dropped = why;
    }
}
