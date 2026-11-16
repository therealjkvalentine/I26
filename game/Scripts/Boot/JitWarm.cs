using System.Reflection;
using System.Runtime.CompilerServices;

namespace I76.Game.Boot;

/// <summary>
/// The game's own code compiled ahead of its first call, on worker threads (a mission loading under its briefing): the first run of
/// a view, the first tick of the simulation and the first frame of each effect were each tens to hundreds of milliseconds of the
/// JIT on the main thread, a stall of the movie. <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> compiles a method
/// without running it (nor its type's static constructor); generic and abstract methods are left to their first call.
/// </summary>
public static class JitWarm
{
    private static int _done;

    /// <summary>
    /// The game's, the simulation's and the formats' code compiled behind the first mission's loading card, on a worker below the main
    /// thread's priority, once a run: the first use of a path in play (a weapon's first shot, a wreck's first fire, a menu) then no
    /// longer stops a frame for the JIT. I76_JIT_WARM=0 leaves it out (docs/dev-hooks.md).
    /// </summary>
    public static void Start()
    {
        if (System.Environment.GetEnvironmentVariable("I76_JIT_WARM") == "0" || System.Threading.Volatile.Read(ref _done) == 1) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            var me = System.Threading.Thread.CurrentThread;
            var was = me.Priority;
            me.Priority = System.Threading.ThreadPriority.BelowNormal;
            try
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int n = Prepare(typeof(JitWarm).Assembly, typeof(I76.Sim.Session.SimSession).Assembly, typeof(I76.Formats.Vfs.AssetVfs).Assembly);
                Godot.GD.Print($"jit warm-up: {n} methods compiled ahead in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
            }
            finally { me.Priority = was; }
        });
    }

    /// <summary>Every method of these assemblies' types compiled, once a run (the second call does nothing); the count compiled.</summary>
    public static int Prepare(params Assembly[] assemblies)
    {
        if (System.Threading.Interlocked.Exchange(ref _done, 1) == 1) return 0;
        var methods = new List<RuntimeMethodHandle>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var asm in assemblies)
        {
            System.Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.OfType<System.Type>().ToArray(); }
            foreach (var t in types)
            {
                if (t.ContainsGenericParameters) continue;
                MethodBase[] ms;
                try { ms = [.. t.GetMethods(all), .. t.GetConstructors(all)]; } catch { continue; }
                foreach (var m in ms)
                    if (!m.IsAbstract && !m.ContainsGenericParameters && m.GetMethodBody() is not null && m is not ConstructorInfo { IsStatic: true }) methods.Add(m.MethodHandle);
            }
        }
        int n = 0;
        System.Threading.Tasks.Parallel.ForEach(methods, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = System.Math.Max(1, System.Environment.ProcessorCount / 4) }, h =>
        {
            try { RuntimeHelpers.PrepareMethod(h); System.Threading.Interlocked.Increment(ref n); } catch { }
        });
        return n;
    }
}
