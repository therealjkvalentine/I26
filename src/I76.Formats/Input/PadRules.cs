namespace I76.Formats.Input;

/// <summary>
/// The port's bindings that input.map has no form for (the original gives an input, or a chord, one command): a button that does
/// one command on a tap and another on a hold, commands that stand for others while the head looks back, a stick deflection that gives a
/// command past a threshold. Kept apart from input.map, so the original still reads that file as it was, in user://input.rules: a rule
/// a line, its kind first ('#' starts a comment; a line of a kind this version does not know is kept as it is). Each frame the rules see
/// the sticks, the time and the look, and say which commands are held, which are pressed once, which commands stand for which, and
/// which inputs they take over from input.map for that frame.
/// </summary>
public sealed partial class PadRules
{
    // the kinds register themselves in their own files' field initializers: run before any member is used
    static PadRules() { }

    /// <summary>What the rules see in a frame: the sticks, the time in milliseconds (the platform's clock: input, not the simulation), the look.</summary>
    public readonly record struct Frame(JoystickBank Bank, long Ms, bool LookingBack = false);

    /// <summary>What the rules say in a frame.</summary>
    public sealed class Output
    {
        internal readonly HashSet<string> Held = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Pulses = [];
        internal readonly Dictionary<string, string> Redirects = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<InputTerm> Masked = [];

        /// <summary>The command is held this frame.</summary>
        public void Hold(string command) => Held.Add(command);
        /// <summary>The command is pressed once (a key going down and up).</summary>
        public void Pulse(string command) => Pulses.Add(command);
        /// <summary>This frame the inputs of <paramref name="from"/> give <paramref name="to"/>, and not <paramref name="from"/>.</summary>
        public void Redirect(string from, string to) => Redirects[from] = to;
        /// <summary>This frame the input's own input.map blocks do nothing.</summary>
        public void Mask(InputTerm input) => Masked.Add(input);

        internal void Clear() { Held.Clear(); Pulses.Clear(); Redirects.Clear(); Masked.Clear(); }
    }

    /// <summary>One rule: its line in the file, the pad inputs it reads, and what it does each frame.</summary>
    public abstract class Rule
    {
        /// <summary>The line it is in the file, its kind first.</summary>
        public abstract string Line { get; }
        /// <summary>The pad inputs it takes (none for a rule on commands): an input has one binding, so a new one there replaces it.</summary>
        public virtual IEnumerable<InputTerm> Inputs => [];
        public abstract void Update(in Frame f, Output o);
    }

    private static Dictionary<string, Func<string[], Rule?>>? _kinds;
    private static Dictionary<string, Func<string[], Rule?>> Kinds => _kinds ??= new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A kind of rule, by its line's first word.</summary>
    private static bool Kind(string word, Func<string[], Rule?> parse) { Kinds[word] = parse; return true; }

    private readonly List<Rule> _rules = [];
    private readonly List<string> _kept = [];

    public IReadOnlyList<Rule> Rules => _rules;
    public bool IsEmpty => _rules.Count == 0 && _kept.Count == 0;

    public const string Header = "# I26's own bindings beside input.map (docs/joystick.md); the original does not read this file";

    public static PadRules Parse(string? text)
    {
        var r = new PadRules();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (Kinds.TryGetValue(words[0], out var parse) && parse(words) is { } rule) r._rules.Add(rule);
            else r._kept.Add(line);
        }
        return r;
    }

    public string ToText() => string.Join("\n", [Header, .. _rules.Select(x => x.Line), .. _kept]) + "\n";

    /// <summary>A copy with no state of its own (a held button is not carried over).</summary>
    public PadRules Clone() => Parse(ToText());

    /// <summary>A rule in; any other rule on the same inputs goes.</summary>
    public void Add(Rule rule)
    {
        foreach (var t in rule.Inputs) RemoveInput(t);
        _rules.Add(rule);
    }

    public void Remove(Rule rule) => _rules.Remove(rule);

    /// <summary>The rules that read an input (a pad button, hat direction or axis) go.</summary>
    public void RemoveInput(InputTerm input) => _rules.RemoveAll(r => r.Inputs.Any(t => Same(t, input)));

    /// <summary>The rule that reads an input, or null.</summary>
    public Rule? On(InputTerm input) => _rules.FirstOrDefault(r => r.Inputs.Any(t => Same(t, input)));

    internal static bool Same(InputTerm a, InputTerm b) =>
        a.Device.Equals(b.Device, StringComparison.OrdinalIgnoreCase)
        && (VirtualStick.DiscreteId(a.Channel) is >= 0 and var i && !IsAnalog(a.Channel) ? i == VirtualStick.DiscreteId(b.Channel) && !IsAnalog(b.Channel) : a.Channel.Equals(b.Channel, StringComparison.OrdinalIgnoreCase));

    private static bool IsAnalog(string c) => VirtualStick.AnalogNames.Any(n => n.Equals(c, StringComparison.OrdinalIgnoreCase));

    // ---- the frame -------------------------------------------------------------------------------

    private readonly Output _out = new();

    /// <summary>One frame of every rule.</summary>
    public void Update(in Frame f)
    {
        _out.Clear();
        foreach (var r in _rules) r.Update(f, _out);
    }

    /// <summary>The commands pressed once this frame.</summary>
    public IReadOnlyList<string> Pulses => _out.Pulses;

    /// <summary>Whether a rule holds the command this frame.</summary>
    public bool Held(string command) => _out.Held.Contains(command);

    /// <summary>The command this one's inputs give this frame instead, or null.</summary>
    public string? RedirectOf(string command) => _out.Redirects.TryGetValue(command, out var to) ? to : null;

    /// <summary>The commands whose inputs give this one this frame: itself (unless it stands for another) and those redirected to it.</summary>
    public IEnumerable<string> Sources(string command)
    {
        if (!_out.Redirects.ContainsKey(command)) yield return command;
        foreach (var (from, to) in _out.Redirects)
            if (to.Equals(command, StringComparison.OrdinalIgnoreCase)) yield return from;
    }

    /// <summary>Whether the block does nothing this frame: a pad block whose input a rule has taken over.</summary>
    public bool Masks(InputBlock b) => b.Terms.Length > 0 && _out.Masked.Count > 0 && _out.Masked.Any(m => Same(m, b.Terms[0]));

    /// <summary>Whether the command is held: a rule holds it, or a block of a command whose inputs give it holds (input.map's chords, a taken-over input left out).</summary>
    public bool Holds(BindingTable table, string command, JoystickBank bank, Func<InputTerm, bool> down)
    {
        if (Held(command)) return true;
        foreach (var c in Sources(command))
            foreach (var b in table.Of(c))
                if (!Masks(b) && InputChord.Holds(b.Terms, bank, down)) return true;
        return false;
    }
}
