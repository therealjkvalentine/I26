using System;
using System.Collections.Generic;

namespace I76.Game.Boot;

/// <summary>
/// A load's steps (Main.AttachSteps) with their failure caught: a step that throws ends the steps there and hands the exception
/// to <c>onFail</c>, once, rather than out of the enumerator (where it faulted the load's task and left the loading card up over a
/// paused tree for good). An iterator cannot yield inside a try with a catch, hence the enumerator driven by hand.
/// </summary>
public static class GuardedSteps
{
    public static IEnumerable<object?> Of(IEnumerable<object?> steps, Action<Exception> onFail)
    {
        using var e = steps.GetEnumerator();
        while (true)
        {
            Exception? failed = null;
            bool more = false;
            try { more = e.MoveNext(); }
            catch (Exception caught) { failed = caught; }
            if (failed is not null) { onFail(failed); yield break; }
            if (!more) yield break;
            yield return e.Current;
        }
    }
}
