using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The tap / hold binding: one pad button, a command on a tap and another on a hold (docs/joystick.md).</summary>
public class TapHoldTests
{
    private sealed class Pads : IPadSource
    {
        public List<PadRaw> List = [new PadRaw()];
        public IReadOnlyList<PadRaw> Read() => List;
    }

    /// <summary>A button held over a timeline: each frame (ms, held) and what the rules said in it.</summary>
    private static List<(long Ms, string[] Pulses, bool Holds)> Run(PadRules rules, string hold, params (long Ms, bool A)[] frames)
    {
        var src = new Pads();
        var bank = new JoystickBank(src);
        var o = new List<(long, string[], bool)>();
        foreach (var (ms, a) in frames)
        {
            src.List[0] = new PadRaw(0, 0, 0, 0, 0, 0, a ? 1UL << PadButton.A : 0);
            bank.Poll(true);
            rules.Update(new PadRules.Frame(bank, ms));
            o.Add((ms, [.. rules.Pulses], rules.Held(hold)));
        }
        return o;
    }

    private static PadRules ARule() => PadRules.Parse("taphold joystick1 Button1 weapon_fire special1\n");

    [Fact]
    public void AReleaseUnder400MsIsATap()
    {
        var r = Run(ARule(), "special1", (0, false), (16, true), (200, true), (399, true), (415, false), (500, false));
        Assert.Equal(["weapon_fire"], r.Single(x => x.Pulses.Length > 0).Pulses);
        Assert.Equal(415, r.Single(x => x.Pulses.Length > 0).Ms);              // on the release, not the press
        Assert.DoesNotContain(r, x => x.Holds);
    }

    [Fact]
    public void AHoldPast400MsPressesAndHoldsTheSecondCommandAndNeverTaps()
    {
        var r = Run(ARule(), "special1", (0, true), (300, true), (399, true), (400, true), (800, true), (1200, false), (1300, false));
        Assert.Equal(["special1"], r.Single(x => x.Pulses.Length > 0).Pulses);
        Assert.Equal(400, r.Single(x => x.Pulses.Length > 0).Ms);              // exactly at 400 ms is a hold
        Assert.Equal([false, false, false, true, true, false, false], r.Select(x => x.Holds));
        Assert.DoesNotContain(r, x => x.Pulses.Contains("weapon_fire"));
    }

    [Fact]
    public void EachPressIsTimedOnItsOwn()
    {
        var r = Run(ARule(), "special1", (0, true), (100, false), (150, true), (560, true), (600, false), (700, true), (750, false));
        Assert.Equal(["weapon_fire", "special1", "weapon_fire"], r.SelectMany(x => x.Pulses));
    }

    [Fact]
    public void TheRuleIsALineOfItsOwnFileAndTakesItsButton()
    {
        var rules = ARule();
        var text = rules.ToText();
        Assert.Contains("taphold joystick1 Button1 weapon_fire special1", text);
        var t = Assert.IsType<PadRules.TapHold>(PadRules.Parse(text).Rules.Single());
        Assert.Equal(("weapon_fire", "special1"), (t.Tap, t.Hold));
        // a line of a kind this version does not know is kept; a malformed one too
        var mixed = PadRules.Parse("# a comment\nsomething joystick1 Button3 x\ntaphold joystick1\n");
        Assert.Empty(mixed.Rules);
        Assert.Contains("something joystick1 Button3 x", mixed.ToText());
        // the same button given another binding: the rule goes, and clearing the input does it too
        var table = PadPreset.Build((string?)null);
        table.Rules = ARule();
        table.Assign("weapon_cycle", "joystick1", "Button1");
        Assert.True(table.Rules.IsEmpty);
        table.Rules = ARule();
        table.ClearInput("Button1");
        Assert.True(table.Rules.IsEmpty);
        // and they travel with a copy of the bindings, without the state of a held button
        table.Rules = ARule();
        Assert.Equal(table.Rules.ToText(), table.Clone().Rules.ToText());
    }

    [Fact]
    public void ATapIsPressedOnceThroughTheBindingsQuestions()
    {
        // the rule's commands are asked as the bindings' are: a hold is held, a tap only pressed
        var table = PadPreset.Build((string?)null);
        table.ClearInput("Button1");
        table.Rules.Add(new PadRules.TapHold(new InputTerm(true, "joystick1", "Button1"), "weapon_fire", "hardpoint2_fire"));
        var src = new Pads();
        var bank = new JoystickBank(src);
        void Frame(long ms, bool a) { src.List[0] = new PadRaw(0, 0, 0, 0, 0, 0, a ? 1UL << PadButton.A : 0); bank.Poll(true); table.Rules.Update(new PadRules.Frame(bank, ms)); }
        Frame(0, true);
        Assert.False(table.Rules.Holds(table, "weapon_fire", bank, _ => false));     // A no longer fires by itself
        Frame(450, true);
        Assert.True(table.Rules.Holds(table, "hardpoint2_fire", bank, _ => false));
        Frame(500, false);
        Assert.False(table.Rules.Holds(table, "hardpoint2_fire", bank, _ => false));
    }
}
