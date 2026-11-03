namespace I76.Formats.Input;

public sealed partial class PadRules
{
    private static readonly bool LookBackKinds = Kind(LookBack.Word, LookBack.Parse) && Kind(LookBackInput.Word, LookBackInput.Parse) && Kind(PulledBack.Word, PulledBack.Parse);

    /// <summary>
    /// While the head looks back (the rear glance, or the look stick pushed round to the rear), one command's inputs give another: the
    /// fire button fires the rear hardpoint. Line: <c>lookback weapon_fire hardpoint3_fire</c>.
    /// </summary>
    public sealed class LookBack(string from, string to) : Rule
    {
        public const string Word = "lookback";
        public string From { get; } = from;
        public string To { get; } = to;
        public override string Line => $"{Word} {From} {To}";
        public static Rule? Parse(string[] w) => w.Length == 3 ? new LookBack(w[1], w[2]) : null;

        public override void Update(in Frame f, Output o)
        {
            if (f.LookingBack) o.Redirect(From, To);
        }
    }

    /// <summary>
    /// While the head looks back, a pad input gives a command of its own instead of its bindings: the look stick's click drops the
    /// dropper. The input keeps its bindings otherwise. Line: <c>lookbackinput joystick1 Button10 hardpoint4_fire</c>.
    /// </summary>
    public sealed class LookBackInput(InputTerm input, string command) : Rule
    {
        public const string Word = "lookbackinput";
        public InputTerm Input { get; } = input with { Plus = true };
        public string Command { get; } = command;
        private bool _was;
        public override string Line => $"{Word} {Input.Device} {Input.Channel} {Command}";
        public static Rule? Parse(string[] w) =>
            w.Length == 4 && JoystickBank.DeviceOf(w[1]) >= 0 && VirtualStick.DiscreteId(w[2]) >= 0 ? new LookBackInput(new InputTerm(true, w[1], w[2]), w[3]) : null;

        public override void Update(in Frame f, Output o)
        {
            bool held = f.Bank.Held(JoystickBank.DeviceOf(Input.Device), Input.Channel);
            if (f.LookingBack)
            {
                o.Mask(Input);
                if (held) { o.Hold(Command); if (!_was) o.Pulse(Command); }
            }
            _was = held;
        }
    }

    /// <summary>
    /// With a stick pulled back past half its travel, one command's inputs give another: the handbrake button toggles reverse (a
    /// three-point turn without letting go of the stick). Line: <c>pulledback joystick1 Down/Up e_brake reverse_direction</c>.
    /// </summary>
    public sealed class PulledBack(InputTerm axis, string from, string to) : Rule
    {
        public const string Word = "pulledback";
        /// <summary>Half the stick's travel back, after the response's square (a quarter of full scale).</summary>
        public static readonly int Past = JoystickBank.Response(0.5f);
        public InputTerm Axis { get; } = axis with { Plus = true };
        public string From { get; } = from;
        public string To { get; } = to;
        public override string Line => $"{Word} {Axis.Device} {Axis.Channel} {From} {To}";
        public static Rule? Parse(string[] w) =>
            w.Length == 5 && JoystickBank.DeviceOf(w[1]) >= 0 && VirtualStick.AnalogId(w[2]) >= 0 ? new PulledBack(new InputTerm(true, w[1], w[2]), w[3], w[4]) : null;

        public override void Update(in Frame f, Output o)
        {
            // back is + on the stick's Y (a pad's left stick down, a flight stick pulled towards the player)
            if (f.Bank.Analog(JoystickBank.DeviceOf(Axis.Device), Axis.Channel) > Past) o.Redirect(From, To);
        }
    }

    /// <summary>The look-back pair of the pad layout players used over the original: looking back, fire is the rear gun (3) and the look stick's click the dropper (4).</summary>
    public static Rule[] LookBackPair() =>
        [new LookBack("weapon_fire", "hardpoint3_fire"), new LookBackInput(new InputTerm(true, "joystick1", "Button10"), "hardpoint4_fire")];

    /// <summary>With the left stick pulled back the handbrake toggles reverse.</summary>
    public static Rule StickBackReverse() => new PulledBack(new InputTerm(true, "joystick1", "Down/Up"), "e_brake", "reverse_direction");

    /// <summary>Whether the rules have a rule of this line.</summary>
    public bool Has(Rule r) => _rules.Any(x => x.Line.Equals(r.Line, StringComparison.OrdinalIgnoreCase));

    /// <summary>The rules on or off together (each in once).</summary>
    public void Toggle(params Rule[] rules)
    {
        bool on = rules.All(Has);
        foreach (var r in rules)
        {
            _rules.RemoveAll(x => x.Line.Equals(r.Line, StringComparison.OrdinalIgnoreCase));
            if (!on) _rules.Add(r);
        }
    }
}
