namespace I76.Formats.Input;

public sealed partial class PadRules
{
    private static readonly bool ThresholdKind = Kind(AxisThreshold.Word, AxisThreshold.Parse);

    /// <summary>
    /// A stick deflection as a button (the port's; the original's input.map reads an axis only as an axis): past <see cref="Engage"/> of
    /// its travel one way the command goes, and it stays until the stick is back under <see cref="Release"/> (the gap keeps a stick resting
    /// near the line from chattering). Three ways, after the stick-as-gearbox layer players used over the original with a flight stick
    /// beside a wheel: <see cref="Mode.Level"/> holds the command while past (a handbrake), <see cref="Mode.Edge"/> presses it once per
    /// excursion (a sequential gear change: held over, it does not shift again), <see cref="Mode.Momentary"/> presses it going in and
    /// again coming out (reverse, which is a toggle, held only while the stick is pushed). The deflection is the stick's own, before the
    /// response's square. Line: <c>threshold joystick2 Down/Up + 0.45 0.30 level e_brake</c>.
    /// </summary>
    public sealed class AxisThreshold(InputTerm axis, bool positive, float engage, float release, AxisThreshold.Mode mode, string command) : Rule
    {
        public const string Word = "threshold";
        public enum Mode { Level, Edge, Momentary }

        public InputTerm Axis { get; } = axis with { Plus = true };
        /// <summary>Which way: + is the axis's positive side (a stick's Y back, X right).</summary>
        public bool Positive { get; } = positive;
        public float Engage { get; } = engage;
        public float Release { get; } = Math.Min(release, engage);
        public Mode Kind { get; } = mode;
        public string Command { get; } = command;
        public bool Past { get; private set; }

        public override string Line => FormattableString.Invariant($"{Word} {Axis.Device} {Axis.Channel} {(Positive ? '+' : '-')} {Engage:0.##} {Release:0.##} {Kind.ToString().ToLowerInvariant()} {Command}");
        public override IEnumerable<InputTerm> Inputs => [Axis with { Channel = Axis.Channel + (Positive ? "+" : "-") }];

        public static Rule? Parse(string[] w)
        {
            if (w.Length != 8 || JoystickBank.DeviceOf(w[1]) < 0 || VirtualStick.AnalogId(w[2]) < 0 || w[3] is not ("+" or "-")) return null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(w[4], inv, out var e) || !float.TryParse(w[5], inv, out var r) || !Enum.TryParse<Mode>(w[6], true, out var m)) return null;
            return new AxisThreshold(new InputTerm(true, w[1], w[2]), w[3] == "+", Math.Clamp(e, 0.05f, 1f), Math.Clamp(r, 0f, 1f), m, w[7]);
        }

        /// <summary>The deflection one way, 0..1, from the bank's squared reading.</summary>
        public float Deflection(JoystickBank bank)
        {
            float d = AxisControls.Deflection(bank.Analog(JoystickBank.DeviceOf(Axis.Device), Axis.Channel));
            return Positive ? Math.Max(0, d) : Math.Max(0, -d);
        }

        public override void Update(in Frame f, Output o)
        {
            float d = Deflection(f.Bank);
            bool was = Past;
            Past = was ? d >= Release : d >= Engage;
            if (Past && !was) o.Pulse(Command);
            if (!Past && was && Kind == Mode.Momentary) o.Pulse(Command);
            if (Past && Kind == Mode.Level) o.Hold(Command);
        }
    }
}
