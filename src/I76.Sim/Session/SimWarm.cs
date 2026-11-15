using System.Reflection;
using System.Runtime.CompilerServices;

namespace I76.Sim.Session;

/// <summary>
/// The simulation's code compiled ahead of its first call (docs/perf/tick-bench.md). A path's first run is the JIT's: the first
/// tick, the first shot, the first kill, the first path search were each 2-80 ms of one tick, measured headless, the largest of
/// the tick's rare slow ones. <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> compiles a method without running
/// it (nor its type's static constructor), so nothing the sim computes changes; generic and abstract methods are left to their
/// first call. The game's own <c>Boot/JitWarm</c> does the same for every assembly.
/// </summary>
public static class SimWarm
{
    /// <summary>Every method of these assemblies' types compiled, on up to <paramref name="threads"/> threads; the count compiled.</summary>
    public static int Prepare(int threads, params Assembly[] assemblies)
    {
        var methods = new List<RuntimeMethodHandle>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = [.. e.Types.OfType<Type>()]; }
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
        Parallel.ForEach(methods, new ParallelOptions { MaxDegreeOfParallelism = System.Math.Max(1, threads) }, h =>
        {
            try { RuntimeHelpers.PrepareMethod(h); Interlocked.Increment(ref n); } catch { }
        });
        return n;
    }

    /// <summary>The simulation's own assembly and the file readers it calls in play.</summary>
    public static int PrepareSim(int threads) => Prepare(threads, typeof(SimSession).Assembly, typeof(I76.Formats.Vfs.AssetVfs).Assembly);
}
