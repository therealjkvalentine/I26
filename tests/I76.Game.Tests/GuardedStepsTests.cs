using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A load step that throws (the car's file unreadable after the world was built) ends the load as a failed one, said once, and
/// the steps come to an end: it used to escape the steps, fault the load's task and leave the loading card up for good.
/// </summary>
public class GuardedStepsTests
{
    private static IEnumerable<object?> Steps(List<string> ran, bool throwAtTwo)
    {
        ran.Add("one"); yield return null;
        if (throwAtTwo) throw new InvalidDataException("bad chunk 'PN\\x19\\x00'");
        ran.Add("two"); yield return null;
        ran.Add("three");
    }

    [Fact]
    public void AThrowingStepEndsTheStepsAndIsHandedOnOnce()
    {
        var ran = new List<string>();
        var failures = new List<Exception>();
        int yielded = GuardedSteps.Of(Steps(ran, throwAtTwo: true), failures.Add).Count();   // ends: no exception, no endless wait
        Assert.Equal(1, yielded);
        Assert.Equal(["one"], ran);
        Assert.IsType<InvalidDataException>(Assert.Single(failures));
    }

    [Fact]
    public void StepsThatDoNotThrowRunAsTheyWere()
    {
        var ran = new List<string>();
        var failures = new List<Exception>();
        Assert.Equal(2, GuardedSteps.Of(Steps(ran, throwAtTwo: false), failures.Add).Count());
        Assert.Equal(["one", "two", "three"], ran);
        Assert.Empty(failures);
    }

    /// <summary>Driven as the loader drives them (an awaited task over the steps): the task completes rather than faulting.</summary>
    [Fact]
    public async Task TheLoadsTaskCompletesWhenAStepThrows()
    {
        var failures = new List<Exception>();
        async Task Run(IEnumerable<object?> steps) { foreach (var _ in steps) await Task.Yield(); }
        var t = Run(GuardedSteps.Of(Steps([], throwAtTwo: true), failures.Add));
        Assert.Same(t, await Task.WhenAny(t, Task.Delay(5000)));
        Assert.True(t.IsCompletedSuccessfully);
        Assert.Single(failures);
    }
}
