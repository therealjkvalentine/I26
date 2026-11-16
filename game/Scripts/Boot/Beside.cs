namespace I76.Game.Boot;

/// <summary>
/// Work beside a playing briefing (a mission attaching under it, Main.AttachSteps): on a thread of its own below the main thread's
/// priority, so that on a busy machine it gives way to the movie's frames rather than taking a core from them.
/// </summary>
public static class Beside
{
    public static System.Threading.Tasks.Task Run(System.Action work) =>
        System.Threading.Tasks.Task.Factory.StartNew(() => Low(work), System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskCreationOptions.LongRunning, System.Threading.Tasks.TaskScheduler.Default);

    public static System.Threading.Tasks.Task<T> Run<T>(System.Func<T> work) =>
        System.Threading.Tasks.Task.Factory.StartNew(() => { T r = default!; Low(() => r = work()); return r; }, System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskCreationOptions.LongRunning, System.Threading.Tasks.TaskScheduler.Default);

    private static void Low(System.Action work)
    {
        var t = System.Threading.Thread.CurrentThread;
        var was = t.Priority;
        t.Priority = System.Threading.ThreadPriority.BelowNormal;
        try { work(); }
        finally { t.Priority = was; }
    }
}
