namespace I76.Game.Assets;

/// <summary>
/// A decode that is not kept (TextureFactory's .dds texels for CPU painting): each <see cref="Take"/> decodes, unless a decode made
/// ahead (<see cref="Ahead"/>, a prefetch's worker) is held, which the next Take gets and lets go. Safe from several threads: one
/// decode runs at a time, and a decode held is taken once. Pure managed code (no engine), so it is tested on its own.
/// </summary>
public sealed class HeldDecode<T>(Func<T> decode) where T : class
{
    private T? _held;
    private readonly object _gate = new();

    /// <summary>A decode is held for the next <see cref="Take"/>.</summary>
    public bool Holds => Volatile.Read(ref _held) is not null;

    /// <summary>What is held now, for a size count (null when nothing is).</summary>
    public T? Held => Volatile.Read(ref _held);

    /// <summary>The decode held, let go; else a new one.</summary>
    public T Take()
    {
        lock (_gate)
        {
            var t = _held ?? decode();
            _held = null;
            return t;
        }
    }

    /// <summary>Decodes now for the next <see cref="Take"/> (nothing when one is held already).</summary>
    public void Ahead()
    {
        lock (_gate) _held ??= decode();
    }
}
