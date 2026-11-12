namespace I76.Game.Assets;

/// <summary>
/// What was made for each look of some art (a look being what the texture factory serves and the look's own settings), kept from the
/// first change of look on (F12, the HD Textures row) so that a change back takes it as it was instead of reading and decoding a pack's
/// pictures again (TextureFactory.KeepBothLooks). The art shown now is the caller's; what is kept is every other look's. Pure managed
/// code (no engine), so it is tested on its own.
/// </summary>
public sealed class LookKept<TKey, T> where TKey : struct, IEquatable<TKey>
{
    private readonly Dictionary<TKey, T> _kept = [];

    /// <summary>Keep the other looks' art (off: nothing is kept, each change makes it again).</summary>
    public bool Keep { get; set; } = true;

    /// <summary>The look the caller's art was made for (null before anything was made).</summary>
    public TKey? Shown { get; private set; }

    /// <summary>The art kept for the looks not shown.</summary>
    public IEnumerable<T> Others => _kept.Values;

    public int Count => _kept.Count;

    /// <summary>Art is kept for <paramref name="look"/> (a change to it hands it back).</summary>
    public bool Has(TKey look) => Keep && _kept.ContainsKey(look);

    /// <summary>The caller made its art for <paramref name="look"/> (its start): noted unless a look is noted already.</summary>
    public void Made(TKey look) => Shown ??= look;

    /// <summary>
    /// The look changes to <paramref name="look"/>: the art shown until now (<paramref name="shown"/>, asked only when it is to be kept)
    /// is kept under its look, and the art kept for <paramref name="look"/> is handed back (true; it is the caller's again, no longer
    /// kept here), else false: the caller makes it. A change to the look shown keeps nothing and hands nothing back.
    /// </summary>
    public bool Change(TKey look, Func<T> shown, out T kept)
    {
        kept = default!;
        if (!Keep) _kept.Clear();
        else if (Shown is { } was && !was.Equals(look)) _kept.TryAdd(was, shown());
        bool same = Shown is { } now && now.Equals(look);
        Shown = look;
        return !same && Keep && _kept.Remove(look, out kept!);
    }

    /// <summary>Lets every other look's art go (the mission's end); returns it, for the caller to free what it holds.</summary>
    public List<T> Release()
    {
        var all = _kept.Values.ToList();
        _kept.Clear();
        return all;
    }
}
