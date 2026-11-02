namespace I76.Formats.Input;

public sealed partial class PadRules
{
    private static readonly bool TapHoldKind = Kind(TapHold.Word, TapHold.Parse);

    /// <summary>
    /// One pad button, two commands (the port's; the original's input.map gives an input one): let go before <see cref="HoldMs"/>, a tap, it
    /// presses <see cref="Tap"/> once; held to <see cref="HoldMs"/>, it presses <see cref="Hold"/> then and holds it until let go, and the tap
    /// never comes. As a pad layout players used over the original (through a key remapper) had it on A: a tap one shot, a hold the nitrous.
    /// Line: <c>taphold joystick1 Button1 weapon_fire special1</c>.
    /// </summary>
    public sealed class TapHold(InputTerm input, string tap, string hold) : Rule
    {
        public const string Word = "taphold";
        /// <summary>How long a press must last to be a hold (a release before it is a tap).</summary>
        public const long HoldMs = 400;

        public InputTerm Input { get; } = input with { Plus = true };
        public string Tap { get; } = tap;
        public string Hold { get; } = hold;

        public enum Phase { Up, Down, Holding }
        public Phase State { get; private set; }
        private long _down;

        public override string Line => $"{Word} {Input.Device} {Input.Channel} {Tap} {Hold}";
        public override IEnumerable<InputTerm> Inputs => [Input];

        public static Rule? Parse(string[] w) =>
            w.Length == 5 && JoystickBank.DeviceOf(w[1]) >= 0 && VirtualStick.DiscreteId(w[2]) >= 0 ? new TapHold(new InputTerm(true, w[1], w[2]), w[3], w[4]) : null;

        public override void Update(in Frame f, Output o)
        {
            bool held = f.Bank.Held(JoystickBank.DeviceOf(Input.Device), Input.Channel);
            switch (State)
            {
                case Phase.Up when held: State = Phase.Down; _down = f.Ms; break;
                case Phase.Down when !held: State = Phase.Up; o.Pulse(Tap); return;
                case Phase.Holding when !held: State = Phase.Up; return;
            }
            if (State == Phase.Down && f.Ms - _down >= HoldMs) { State = Phase.Holding; o.Pulse(Hold); }
            if (State == Phase.Holding) o.Hold(Hold);
        }
    }
}
