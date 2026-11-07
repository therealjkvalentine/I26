namespace I76.Game.World;

/// <summary>
/// A walk of a tree in pre-order (a node, then its children in order: the order a recursive walk visits them) that can stop
/// after any node and go on from there in a later frame, so a large tree's walk is spread over frames instead of stalling one.
/// </summary>
public sealed class StepWalk<T>(Func<T, IReadOnlyList<T>> children) where T : class
{
    private readonly Stack<T> _pending = new();

    /// <summary>Whether a walk is under way.</summary>
    public bool Walking => _pending.Count > 0;

    /// <summary>Starts a walk from the root (any walk under way is dropped).</summary>
    public void Start(T root)
    {
        _pending.Clear();
        _pending.Push(root);
    }

    /// <summary>
    /// Visits nodes until the walk ends or <paramref name="more"/> says stop (asked before each node after the first).
    /// <paramref name="visit"/> returns whether to go into the node's children. True when the walk has ended.
    /// </summary>
    public bool Step(Func<T, bool> visit, Func<bool> more)
    {
        bool first = true;
        while (_pending.Count > 0 && (first || more()))
        {
            first = false;
            var n = _pending.Pop();
            if (!visit(n)) continue;
            var ch = children(n);
            for (int i = ch.Count - 1; i >= 0; i--) _pending.Push(ch[i]);
        }
        return _pending.Count == 0;
    }
}
