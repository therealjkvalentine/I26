using I76.Formats.Fsm;
using I76.Formats.Mission;

namespace I76.Sim.Fsm;

/// <summary>
/// The cars a mission script makes the players protect (docs/multiplayer.md "Co-op", "Escorted cars"): an actor whose
/// death the script answers with a failed mission. Found statically, by the routines' rules (<see cref="FsmRoutines"/>):
/// <list type="bullet">
/// <item>a rule holding on isDead(actor) whose statements fail the mission (failAllObj, failAll), or which goes to a
/// state that fails it once it has waited out the radio or a timer (a state of one rule on true, isCBEmpty or timeGreater);</item>
/// <item>a rule holding on isDead(actor) whose statements set (or inc) a data cell that another rule tests with isEqual
/// or isGreater before failing the mission: S05's bus sets a flag another thread fails on, T01's trucks count up to the
/// limit the mission thread fails at.</item>
/// </list>
/// Only a thread's own routine is read, each parameter bound to the data cell its table entry names; a call in a failing
/// rule counts as a failure when the called routine fails the mission.
/// </summary>
public static class CoopEscorts
{
    private static readonly HashSet<string> Fails = new(StringComparer.OrdinalIgnoreCase) { "failAllObj", "failAll" };
    private static readonly HashSet<string> Waits = new(StringComparer.OrdinalIgnoreCase) { "true", "isCBEmpty", "timeGreater" };

    /// <summary>The actor indices whose death fails the mission (sorted), the player's slots among them as the script has them.</summary>
    public static IReadOnlyList<int> Analyze(FsmProgram p)
    {
        var code = p.Code.Select(c => new FsmInstr((int)c.Op, (int)c.Arg)).ToList();
        var depthAt = new Dictionary<int, int>();
        foreach (var t in p.Threads) depthAt.TryAdd(t.StartPc, t.Depth);
        var entries = p.Threads.Select(t => t.StartPc).Concat(code.Where(i => i.Op == FsmOp.Call).Select(i => i.Arg));
        var routines = FsmRoutines.Split(code, p.Prototypes, entries, pc => depthAt.TryGetValue(pc, out int d) ? d : null).ToDictionary(r => r.Pc);
        string Name(FsmStmt s) => s.IsCall || s.Proto < 0 || s.Proto >= p.Prototypes.Count ? "" : p.Prototypes[s.Proto];

        bool RoutineFails(int pc, int depth) =>
            depth < 4 && routines.TryGetValue(pc, out var r) && (r.Asm?.Any(i => i.Op == FsmOp.Exec && i.Arg >= 0 && i.Arg < p.Prototypes.Count && Fails.Contains(p.Prototypes[i.Arg])) == true
                || r.States.Any(s => s.Rules.Any(x => x.Body.Any(b => Fails.Contains(Name(b)) || b.IsCall && RoutineFails(b.CallTarget, depth + 1)))));
        bool BodyFails(IEnumerable<FsmStmt> body) => body.Any(b => Fails.Contains(Name(b)) || b.IsCall && RoutineFails(b.CallTarget, 0));
        // a rule fails the mission: its statements, or the state it goes to does once it has waited out the radio or a
        // timer (a state of one rule on true, isCBEmpty or timeGreater: T15's death waits for the radio, then fails)
        bool RuleFails(FsmRoutine r, FsmRule rule)
        {
            if (BodyFails(rule.Body)) return true;
            var exit = rule.Exit;
            for (int hop = 0; hop < 4 && !exit.IsReturn && exit.Value >= 0 && exit.Value < r.States.Count; hop++)
            {
                if (r.States[exit.Value].Rules is not [{ Cond: FsmExpr.Term { Op: var t } } first] || !Waits.Contains(Name(t))) break;
                if (BodyFails(first.Body)) return true;
                exit = first.Exit;
            }
            return false;
        }

        // every thread's rules, with its parameters' cells
        var rules = new List<(int Thread, FsmRoutine Routine, FsmRule Rule)>();
        for (int ti = 0; ti < p.Threads.Count; ti++)
            if (routines.TryGetValue(p.Threads[ti].StartPc, out var r))
                foreach (var s in r.States)
                    foreach (var rule in s.Rules) rules.Add((ti, r, rule));
        int Cell(int thread, FsmArg a) => a.Kind == FsmArgKind.Param ? CoopRoutines.CellOf(p, thread, a.Value) : -1;

        // the flags a failing rule tests: (cell, value, at least) — isEqual(cell, v) holds at v, isGreater(cell, v) above v
        var flags = new List<(int Cell, int Value, bool Above)>();
        var dead = new SortedSet<int>();
        foreach (var (ti, r, rule) in rules)
        {
            if (!RuleFails(r, rule)) continue;
            foreach (var t in Positive(rule.Cond))
            {
                string n = Name(t);
                if (n.Equals("isDead", StringComparison.OrdinalIgnoreCase) && t.Args.Count == 1 && Cell(ti, t.Args[0]) is var dc and >= 0 && dc < p.Data.Length) dead.Add(p.Data[dc]);
                else if (n.Equals("isEqual", StringComparison.OrdinalIgnoreCase) || n.Equals("isGreater", StringComparison.OrdinalIgnoreCase))
                    if (t.Args.Count == 2 && Cell(ti, t.Args[0]) is var fc and >= 0 && t.Args[1].Kind == FsmArgKind.Imm)
                        flags.Add((fc, t.Args[1].Value, n.Equals("isGreater", StringComparison.OrdinalIgnoreCase)));
            }
        }
        // a death that raises such a flag: set(cell, v) to the value tested, or inc(cell) of a count tested
        foreach (var (ti, r, rule) in rules)
        {
            var deaths = Positive(rule.Cond).Where(t => Name(t).Equals("isDead", StringComparison.OrdinalIgnoreCase) && t.Args.Count == 1)
                .Select(t => Cell(ti, t.Args[0])).Where(c => c >= 0 && c < p.Data.Length).ToList();
            if (deaths.Count == 0) continue;
            bool raises = rule.Body.Any(b => Name(b) switch
            {
                var n when n.Equals("set", StringComparison.OrdinalIgnoreCase) && b.Args.Count == 2 && b.Args[1].Kind == FsmArgKind.Imm =>
                    Cell(ti, b.Args[0]) is var c and >= 0 && flags.Any(f => f.Cell == c && (f.Above ? b.Args[1].Value > f.Value : b.Args[1].Value == f.Value)),
                var n when n.Equals("inc", StringComparison.OrdinalIgnoreCase) && b.Args.Count >= 1 =>
                    Cell(ti, b.Args[0]) is var c and >= 0 && flags.Any(f => f.Cell == c && f.Value > 0),
                _ => false,
            });
            if (raises) foreach (int c in deaths) dead.Add(p.Data[c]);
        }
        dead.RemoveWhere(a => a < 0 || a >= p.Actors.Count);
        return [.. dead];
    }

    /// <summary>The terms a condition holds on (not under a negation).</summary>
    private static IEnumerable<FsmStmt> Positive(FsmExpr e) => e switch
    {
        FsmExpr.Term t => [t.Op],
        FsmExpr.And a => Positive(a.L).Concat(Positive(a.R)),
        FsmExpr.Or o => Positive(o.L).Concat(Positive(o.R)),
        _ => [],
    };
}
