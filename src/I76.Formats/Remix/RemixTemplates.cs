using System.Globalization;
using System.Text;

namespace I76.Formats.Remix;

/// <summary>
/// The objective templates: each writes its part of the script (sounds, globals, routines, threads) in the script source
/// language (docs/mission-scripts.md), built from the stock missions' own blocks (park and wake, teleport-in, kill counters,
/// structures as targets with random picks, clear-area arrival, gates). Names are prefixed rx_ so they never meet the base's.
/// Objective numbers and failure lines each template uses are in docs/remix-missions.md.
/// </summary>
public static class RemixTemplates
{
    public sealed record Context(RemixSpec Spec, string Player, Func<string, string> Actor, Func<string, string> Place, Func<string, int> Heading);

    // (the source's raw strings carry the file's line ends: a checkout with CRLF ones must emit the same source as one with LF)
    public static string Emit(Context c) => (Common(c) + (c.Spec.Objective.Template switch
    {
        "defend" => Defend(c),
        "deliver" => Deliver(c),
        "tail" => Tail(c),
        "intercept" or "ambush" => Intercept(c),
        "escort" => Escort(c),
        "raid" => Raid(c),
        var t => throw new RemixError($"objective template '{t}': defend, deliver, tail, intercept, escort or raid"),
    }) + (c.Spec.Objective.Template is "intercept" or "ambush" or "defend" or "escort" or "raid" ? Sleepers(c) : "") + Reinforcements(c)).Replace("\r\n", "\n");

    /// <summary>
    /// Sleepers for the templates without their own (intercept, defend): each waits parked where it is until the player comes within
    /// its wake radius or shoots it, then (siren on if asked) comes for him. A roadblock on a convoy's road, a guard at a farm.
    /// </summary>
    private static string Sleepers(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Sleepers.Count == 0) return "";
        var y = new StringBuilder("---\nglobals:\n");
        y.Append($"  rx_suser: actor.{c.Player}\n");
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_y{i}: actor.{c.Actor(o.Sleepers[i].Car)}\n  rx_yw{i}: {N(o.Sleepers[i].Wake)}\n  rx_ys{i}: {(o.Sleepers[i].Siren ? 1 : 0)}\n");
        y.Append($$"""
            scripts:
              rx_gsleeper:
                fields: [car, user, wake, siren]
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                    while_waiting: hide(car)
                  - wait_until: isWithin(car, user, wake) || isAttacked(car)
                    then:
                      - startCar(car)
                      - setSiren(car, siren)
                    while_waiting: hide(car)
                  - wait_until: isDead(user)
                    while_waiting: attack(car, user, 1)
            threads:

            """);
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_gsleeper{i}: {{script: rx_gsleeper, args: {{car: rx_y{i}, user: rx_suser, wake: rx_yw{i}, siren: rx_ys{i}}}}}\n");
        return y.ToString();
    }

    /// <summary>The objective lines a template's own steps take (a finish step's line follows them).</summary>
    public static int OwnObjectives(RemixSpec spec)
    {
        var o = spec.Objective;
        return o.Template switch
        {
            "defend" => 3,
            "deliver" => (o.Pickup is not null ? 2 : 1) + (o.Clear > 0 ? 1 : 0),
            "tail" => 2,
            "intercept" or "ambush" => o.Convoy is { KillEscorts: true, Escorts.Count: > 0 } ? 2 : 1,
            "escort" => 2,
            "raid" => 3,
            _ => 0,
        };
    }

    /// <summary>The finish step's objective number (the line after the template's own), or 0 without one.</summary>
    public static int FinishObjective(RemixSpec spec) => spec.Objective.Finish is null ? 0 : OwnObjectives(spec) + 1;

    /// <summary>The failure line (1-based) shown for a death while objective <paramref name="k"/> is open: the deaths follow the description's failures.</summary>
    public static int DeathLine(RemixSpec spec, int k) => spec.Text.Failures.Count + k;

    /// <summary>
    /// What a controller does as objective <paramref name="k"/> opens: the step's number (reinforcements wait for it), the
    /// death line that names it, and the step's radio (the instruction, RadioSpec.Steps).
    /// </summary>
    private static IEnumerable<string> Open(Context c, int k) =>
        [$"set(step, {k})", $"set(dl, {DeathLine(c.Spec, k)})", .. Say(c, k - 1 < c.Spec.Radio.Steps.Count ? c.Spec.Radio.Steps[k - 1] : [])];

    /// <summary>A controller's death rule: the failure line of the open objective's step.</summary>
    private const string Death = "          - when: isDead(user)\n            then: failAllObj(6, dl)\n            goto: stop\n";

    /// <summary>The controllers' shared fields and their bindings (the step and the death line).</summary>
    private const string StepFields = ", step, dl, done", StepArgs = ", step: rx_step, dl: rx_dl, done: rx_done";

    /// <summary>
    /// The end of a controller from its win's rule on: the template's own successes, then (with a finish) its line revealed and a
    /// last step to the finish zone with nobody near, then the win's gates and radio, and successAll once the CB is quiet.
    /// </summary>
    private static string Ending(Context c, string when, IEnumerable<string> successes)
    {
        var o = c.Spec.Objective;
        var y = new StringBuilder();
        int f = FinishObjective(c.Spec);
        var win = new[] { "set(done, 1)" }.Concat(Gates(c, c.Spec.Gates.Win)).Concat(Say(c, c.Spec.Radio.Win));
        if (o.Finish is { } fin)
        {
            y.Append($"          - when: {when}\n" + Then(12, successes.Concat([$"reveal({f})", "startTimer(fin)"]).Concat(Open(c, f))));
            y.Append("      - wait:\n" + Death);
            string arrive = o.FinishClear > 0 ? $"isWithinNav(finish, user, {fin.Radius}) && !isWithinEnemy(user, {o.FinishClear})" : $"isWithinNav(finish, user, {fin.Radius})";
            if (o.FinishAfter > 0) arrive += $" && timeGreater(fin, {o.FinishAfter})";
            y.Append($"          - when: {arrive}\n" + Then(12, new[] { $"success({f})" }.Concat(win)));
        }
        else y.Append($"          - when: {when}\n" + Then(12, successes.Concat(win)));
        y.Append("      - wait:\n" + Death);
        y.Append("          - when: isCBEmpty()\n            then: successAll(3)\n            goto: stop\n");
        return y.ToString();
    }

    /// <summary>The finish zone's field and binding, when there is one.</summary>
    private static string FinishField(Context c) => c.Spec.Objective.Finish is null ? "" : ", finish, fin";
    private static string FinishArg(Context c) => c.Spec.Objective.Finish is null ? "" : ", finish: rx_finish, fin: rx_fin";

    /// <summary>
    /// Reinforcements (a beat's twist): each car waits hidden off the map until its step opens, then a few seconds more, and is
    /// teleported in at its entry, one behind the other, and comes for the player; the first says the reinforcement's line.
    /// </summary>
    private static string Reinforcements(Context c)
    {
        var rs = c.Spec.Objective.Reinforce;
        if (rs.Count == 0) return "";
        int last = Math.Max(OwnObjectives(c.Spec), FinishObjective(c.Spec));
        var y = new StringBuilder("---\nglobals:\n");
        var threads = new StringBuilder("threads:\n");
        int n = 0;
        for (int j = 0; j < rs.Count; j++)
        {
            var r = rs[j];
            if (r.Cars.Count == 0) throw new RemixError($"reinforce {j + 1}: no cars");
            if (r.AtStep < 1 || r.AtStep > last) throw new RemixError($"reinforce {j + 1}: at_step {r.AtStep}: 1-{last}");
            int hd = c.Heading(r.Entry);
            double rad = hd * Math.PI / 180;
            y.Append($"  rx_re{j}: path.{c.Place(r.Entry)}\n  rx_rh{j}: {N(hd)}\n  rx_ra{j}: {N(r.AtStep)}\n  rx_rd{j}: {N(r.Delay)}\n  rx_rs{j}: {N(r.Speed)}\n  rx_rn{j}: {(r.Siren ? 1 : 0)}\n");
            // whom they come for: the player, or an escort's car (an ambush on the van)
            string target = r.Target switch
            {
                null or "" or "player" => "rx_user",
                "escort" when c.Spec.Objective.Escort is { Car.Length: > 0 } => $"rx_rt{j}",
                var t => throw new RemixError($"reinforce {j + 1}: target '{t}': player, or escort (an escort's car)"),
            };
            if (target != "rx_user") y.Append($"  rx_rt{j}: actor.{c.Actor(c.Spec.Objective.Escort!.Car)}\n");
            for (int i = 0; i < r.Cars.Count; i++, n++)
            {
                // the first car of each makes the call: its reinforcement's number, the line it says
                y.Append($"  rx_r{n}: actor.{c.Actor(r.Cars[i])}\n  rx_rx{n}: {N(Off(i, r.Cars.Count, rad).X)}\n  rx_rz{n}: {N(Off(i, r.Cars.Count, rad).Z)}\n  rx_rf{n}: {(i == 0 ? j + 1 : 0)}\n");
                threads.Append($"  rx_reinf{n}: {{script: rx_reinf, args: {{car: rx_r{n}, user: {target}, step: rx_step, at: rx_ra{j}, delay: rx_rd{j}, entry: rx_re{j}, heading: rx_rh{j}, speed: rx_rs{j}, dx: rx_rx{n}, dz: rx_rz{n}, siren: rx_rn{j}, first: rx_rf{n}}}}}\n");
            }
        }
        // each reinforcement's own line (a generated job's), else the radio's reinforce clip
        var calls = new StringBuilder();
        for (int j = 0; j < rs.Count; j++)
        {
            IEnumerable<string> clip = rs[j].Say is { Length: > 0 } own ? [own] : c.Spec.Radio.Reinforce.Take(1);
            calls.Append($"          - when: first == {j + 1}\n            then:\n              - set(first, 0)\n");
            foreach (var a in Say(c, clip)) calls.Append($"              - {a}\n");
        }
        var o = c.Spec.Objective;
        y.Append($$"""
            scripts:
              rx_reinf:
                fields: [car, user, step, at, delay, entry, heading, speed, dx, dz, siren, first]
                variables: {timer: null}
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                    while_waiting: hide(car)
                  - wait_until: '!(step < at)'
                    then: startTimer(timer)
                    while_waiting: hide(car)
                  - wait_until: timeGreater(timer, delay)
                    then:
                      - startCar(car)
                      - teleportOffset(car, entry, speed, heading, dx, dz)
                      - setSiren(car, siren)
                    while_waiting: hide(car)
                  - wait:
            {{calls}}          - when: 'true'
                  - wait_until: isDead(user)
                    while_waiting: attack(car, user, 1)

            """);
        return y.Append(threads).ToString();
    }

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The count lines ("Two down, one to go"): a routine over a counter of the template's, each cue's line as the counter reaches
    /// exactly its number (two falling at once skip the line that would be wrong), from step <paramref name="from"/> on, never after
    /// the win. Empty without cues.
    /// </summary>
    private static (string Script, string Thread) CountScript(Context c, string counter, int from)
    {
        var cues = c.Spec.Radio.Counts.OrderBy(q => q.Dead).ToList();
        if (cues.Count == 0) return ("", "");
        var y = new StringBuilder("  rx_count:\n    fields: [n, step, done]\n    steps:\n");
        foreach (var q in cues)
        {
            y.Append($"      - wait_until: '!(n < {q.Dead}) && !(step < {from})'\n      - wait:\n          - when: n == {q.Dead} && done == 0\n");
            y.Append(Then(12, Say(c, [q.Clip])));
            y.Append("          - when: 'true'\n");
        }
        return (y.ToString(), $"  rx_count: {{script: rx_count, args: {{n: {counter}, step: rx_step, done: rx_done}}}}\n");
    }

    /// <summary>
    /// Where the i-th of n cars teleported in at an entry goes, from the entry (cm, as teleportOffset takes it): in pairs side by
    /// side, 3.5 m either side of the entry's line, each pair 12 m behind the one before (a single file of eight ran 85 m back,
    /// off the map's edge at an entry by it).
    /// </summary>
    private static (int X, int Z) Off(int i, int n, double heading)
    {
        double back = 1200 * (n > 1 ? i / 2 : i), side = n > 1 ? (i % 2 == 0 ? -350 : 350) : 0;
        double fx = DetMath.Sin(heading), fz = DetMath.Cos(heading);   // ahead; right is (fz, -fx)
        return ((int)Math.Round(-fx * back + fz * side), (int)Math.Round(-fz * back - fx * side));
    }

    private static string SoundKey(string clip) => "rx_cb_" + clip.ToLowerInvariant().Replace('@', '_').Replace('-', '_');

    /// <summary>
    /// What every template shares, as a document of its own (merged like the template's): the radio's clips in the sounds table,
    /// the gate queue and the start's gates and radio. A gate ignores a trigger while it slides (GateMotion: the original's), so
    /// the templates never trigger one directly: each moment adds one to the gate's count of wanted triggers (rx_gwK), and a
    /// thread per gate sends them one at a time, <see cref="GateSpec.Settle"/> seconds apart.
    /// </summary>
    private static string Common(Context c)
    {
        var y = new StringBuilder();
        var clips = c.Spec.Radio.All.Concat(c.Spec.Objective.Reinforce.Select(r => r.Say).OfType<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // a clip is a sound file's stem, or a cut of one: STEM@START-END, in milliseconds (docs/remix-missions.md "The radio")
        foreach (var clip in clips)
        {
            var stem = Audio.SoundFiles.Cut(clip) is { } cut ? cut.Stem : clip;
            if (clip.Length is 0 or > 34 || stem.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_') || (stem != clip && Audio.SoundFiles.Cut(clip) is null))
                throw new RemixError($"radio clip '{clip}': a sound file's stem, or STEM@START-END in milliseconds");
        }
        if (clips.Count > 0)
        {
            y.Append("sounds:\n");
            foreach (var clip in clips) y.Append($"  {SoundKey(clip)}: {clip.ToLowerInvariant()}.wav\n");
        }
        var gates = GateList(c);
        if (c.Spec.Gates.Settle is < 1 or > 600) throw new RemixError($"gates: settle {c.Spec.Gates.Settle}: 1-600 seconds");
        // the open objective's step (reinforcements wait for theirs) and the failure line a death shows (the step's own)
        y.Append($"globals:\n  rx_step: 1\n  rx_done: 0\n  rx_dl: {N(DeathLine(c.Spec, 1))}\n");
        if (c.Spec.Objective.Finish is { } fin) y.Append($"  rx_finish: path.{c.Place(fin.Place)}\n  rx_fin: 0\n");
        for (int k = 0; k < gates.Count; k++) y.Append($"  rx_g{k}: actor.{c.Actor(gates[k])}\n  rx_gw{k}: 0\n");
        var hurry = c.Spec.Radio.Hurry;
        for (int k = 0; k < hurry.Count; k++)
        {
            var h = hurry[k];
            if (h.From < 1 || h.Until < h.From || h.At < 1) throw new RemixError($"radio hurry {k + 1}: from 1 or more, until no less, at 1 s or more");
            y.Append($"  rx_hf{k}: {N(h.From)}\n  rx_hu{k}: {N(h.Until)}\n  rx_ha{k}: {N(h.At)}\n  rx_hl{k}: {N(k)}\n");
        }
        var startClips = c.Spec.Radio.Start.Concat(c.Spec.Radio.Steps.Count > 0 ? c.Spec.Radio.Steps[0] : []).ToList();
        bool start = c.Spec.Gates.Start.Count > 0 || startClips.Count > 0;
        if (gates.Count > 0 || start || hurry.Count > 0) y.Append("scripts:\n");
        // the clock nearly out: a cue's line once its step has been open its seconds, if that step (or one before it) is still open
        if (hurry.Count > 0)
        {
            y.Append("""
                  rx_hurry:
                    fields: [step, done, from, until, at, line]
                    variables: {timer: null}
                    steps:
                      - wait_until: '!(step < from)'
                        then: startTimer(timer)
                      - wait_until: timeGreater(timer, at)
                      - wait:

                """);
            for (int k = 0; k < hurry.Count; k++)
                y.Append($"          - when: line == {k} && step < {hurry[k].Until + 1} && done == 0\n" + Then(12, Say(c, [hurry[k].Clip])) + "            goto: stop\n");
            y.Append("          - when: 'true'\n            goto: stop\n");
        }
        if (gates.Count > 0)
            y.Append($$"""
                  rx_gate:
                    fields: [gate, want]
                    variables: {done: 0, timer: null}
                    steps:
                      - repeat:
                          - wait_until: done < want
                            then:
                              - triggerGate(gate)
                              - inc(done)
                              - startTimer(timer)
                          - wait_until: timeGreater(timer, {{c.Spec.Gates.Settle}})

                """);
        if (start)
        {
            y.Append($"  rx_start:\n    fields: [{GateFields(c, lead: false)}]\n    steps:\n      - do:\n");
            foreach (var a in Gates(c, c.Spec.Gates.Start).Concat(Say(c, startClips))) y.Append($"          - {a}\n");
        }
        if (gates.Count > 0 || start || hurry.Count > 0) y.Append("threads:\n");
        for (int k = 0; k < hurry.Count; k++)
            y.Append($"  rx_hurry{k}: {{script: rx_hurry, args: {{step: rx_step, done: rx_done, from: rx_hf{k}, until: rx_hu{k}, at: rx_ha{k}, line: rx_hl{k}}}}}\n");
        for (int k = 0; k < gates.Count; k++) y.Append($"  rx_gate{k}: {{script: rx_gate, args: {{gate: rx_g{k}, want: rx_gw{k}}}}}\n");
        if (start) y.Append($"  rx_start: {{script: rx_start, args: {{{GateArgs(c, lead: false)}}}}}\n");
        return y.Append("---\n").ToString();
    }

    /// <summary>Every gate a moment triggers, each once, in the order first named.</summary>
    private static List<string> GateList(Context c) =>
        [.. c.Spec.Gates.Start.Concat(c.Spec.Gates.Trap).Concat(c.Spec.Gates.Win).Distinct(StringComparer.Ordinal)];

    /// <summary>The gate counters as a routine's fields (", gw0, gw1"; without the leading comma when <paramref name="lead"/> is false).</summary>
    private static string GateFields(Context c, bool lead = true)
    {
        var f = string.Join(", ", GateList(c).Select((_, k) => $"gw{k}"));
        return lead && f.Length > 0 ? ", " + f : f;
    }

    /// <summary>The gate counters bound in a thread's args.</summary>
    private static string GateArgs(Context c, bool lead = true)
    {
        var f = string.Join(", ", GateList(c).Select((_, k) => $"gw{k}: rx_gw{k}"));
        return lead && f.Length > 0 ? ", " + f : f;
    }

    /// <summary>A moment's gates: one more wanted trigger each (the gate's thread sends it when the gate is still).</summary>
    private static IEnumerable<string> Gates(Context c, IEnumerable<string> gates)
    {
        var all = GateList(c);
        return gates.Select(g => $"inc(gw{all.IndexOf(g)})");
    }

    /// <summary>
    /// A moment's radio: each clip queued at priority 3; a spoken line of the player's own (Groove's, RemixVoice) from the player, so
    /// the handset comes up as for the stock game's lines he speaks.
    /// </summary>
    private static IEnumerable<string> Say(Context c, IEnumerable<string> clips) =>
        clips.Select(k => RemixVoice.Of(k) is { PlayerSpeaks: true } ? $"cbFromPrior(sound.{SoundKey(k)}, actor.{c.Player}, 3)" : $"cbPrior(sound.{SoundKey(k)}, 3)");

    /// <summary>Actions as a <c>then:</c> list at an indent (the rule's).</summary>
    private static string Then(int indent, IEnumerable<string> actions)
    {
        var l = actions.ToList();
        if (l.Count == 0) return "";
        var pad = new string(' ', indent);
        return $"{pad}then:\n" + string.Concat(l.Select(a => $"{pad}  - {a}\n"));
    }

    /// <summary>
    /// Defend structures (Nitro P11's pattern, written fresh): attackers parked off the map start in waves (by seconds from the
    /// start and by kill count), each teleports to a random entry, picks a random target (one of the structures, or the player
    /// once he has arrived) and keeps it until it dies or it has been near it a while, turning on the player when hit. The trap
    /// gates shut when the player arrives; the win gates open at the end, after the radio. Objectives: 1 arrive (or done at the
    /// start), 2 defend, 3 kill them all. Failures: 1 death, 2 left the map, 3 too many structures lost, 4 too late to arrive.
    /// </summary>
    private static string Defend(Context c)
    {
        var o = c.Spec.Objective;
        var cars = o.Waves.SelectMany(w => w.Cars.Select(car => (Car: car, Wave: w))).ToList();
        if (o.Defend.Count == 0 || cars.Count == 0 || o.Entries.Count == 0) throw new RemixError("defend: needs defend, waves (with cars) and entries");
        if (c.Spec.Text.Objectives.Count < 3) throw new RemixError("defend: three objective lines (arrive, defend, kill them all)");
        if (c.Spec.Text.Failures.Count < (o.Arrive is { Seconds: > 0 } ? 4 : 3)) throw new RemixError("defend: failure lines 3 (structures lost) and 4 (too late) as used");
        int n = o.Defend.Count, m = cars.Count, e = o.Entries.Count;
        int mayLose = o.MayLose ?? n - 1;
        var y = new StringBuilder();
        y.Append("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_dead: 0\n  rx_kills: 0\n  rx_lost: 0\n  rx_arrived: {(o.Arrive is null ? 1 : 0)}\n  rx_started: 0\n");
        if (o.Arrive is { } ar) y.Append($"  rx_zone: path.{c.Place(ar.Place)}\n");
        for (int i = 0; i < n; i++) y.Append($"  rx_s{i}: actor.{c.Actor(o.Defend[i])}\n");
        for (int i = 0; i < e; i++) y.Append($"  rx_e{i}: path.{c.Place(o.Entries[i])}\n  rx_h{i}: {N(c.Heading(o.Entries[i]))}\n");
        // a wave starts at its kill count, counting the player's kills only (isGroovesFault), or, once the player has arrived, when
        // every car of the waves before it is dead however it died (friendly fire, its own mines): a raider's own death never
        // starts a wave without the player, and no wave waits for a kill that can no longer come
        var before = o.Waves.Select((w, j) => o.Waves.Take(j).Sum(x => x.Cars.Count)).ToList();
        for (int i = 0; i < m; i++)
            y.Append($"  rx_a{i}: actor.{c.Actor(cars[i].Car)}\n  rx_wt{i}: {N(cars[i].Wave.AtSeconds)}\n  rx_wk{i}: {N(cars[i].Wave.AtKills)}\n  rx_wb{i}: {N(before[o.Waves.IndexOf(cars[i].Wave)])}\n");

        y.Append("scripts:\n");
        var sFields = string.Join(", ", Enumerable.Range(0, n).Select(i => $"s{i}"));
        var eFields = string.Join(", ", Enumerable.Range(0, e).Select(i => $"e{i}, h{i}"));
        y.Append($$"""
              rx_raider:
                fields: [car, user, dead, kills, started, arrived, wtime, wkills, wbefore, {{sFields}}, {{eFields}}]
                variables: {timer: null, pick: null, target: null, entry: null, keep: null}
                guards:
                  - when: isDead(car) && isGroovesFault(car)
                    then:
                      - inc(dead)
                      - inc(kills)
                    goto: stop
                  - when: isDead(car)
                    then: inc(dead)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                      - setMaxAttackers(car, 1)
                      - rand(entry, {{e}})
                    while_waiting: hide(car)
                  - wait_until: started == 1
                    then: startTimer(timer)
                    while_waiting: hide(car)
                  - wait_until: timeGreater(timer, wtime) && (!(kills < wkills) || arrived == 1 && !(dead < wbefore))
                    then: startCar(car)
                    while_waiting: hide(car)
                  - wait:

            """);
        for (int i = 0; i < e; i++) y.Append($"          - when: entry == {i}\n            then: teleport(car, e{i}, 15, h{i})\n");
        y.Append($$"""
                  - repeat:
                      - do: rand(pick, {{n + 1}})
                      - wait:
                          - when: pick == 0 && arrived == 0
                            then:
                              - rand(pick, {{n}})
                              - inc(pick)
                            goto: restart
                          - when: pick == 0
                            then:
                              - setId(target, user)
                              - startTimer(keep)

            """);
        for (int i = 0; i < n; i++)
            y.Append($"              - when: pick == {i + 1} && !isDead(s{i})\n                then:\n                  - setId(target, s{i})\n                  - startTimer(keep)\n");
        y.Append($$"""
                          - when: 'true'
                            then:
                              - setId(target, user)
                              - startTimer(keep)
                      - wait:
                          - when: isDead(target)
                            goto: continue
                          - when: isAttacked(car)
                            then:
                              - setId(target, user)
                              - startTimer(keep)
                            goto: rx_fight
                          - when: isWithin(car, target, {{o.Near}})
                            then: startTimer(keep)
                        while_waiting: attack(car, target, 1)
                      - id: rx_fight
                        wait:
                          - when: isDead(target)
                            goto: continue
                          - when: isAttacked(car)
                            then:
                              - setId(target, user)
                              - startTimer(keep)
                            goto: restart
                          - when: timeGreater(keep, {{o.Retarget}})
                            goto: continue
                        while_waiting: attack(car, target, 1)
              rx_lost:
                fields: [s, lost]
                steps:
                  - wait_until: isDead(s)
                    then: inc(lost)

            """);
        // the controller: arrival (the trap shuts), the later waves' radio, the fails, the win
        var later = o.Waves.Select((w, j) => (w.AtKills, Before: before[j])).Where(w => w.AtKills > 0).ToList();
        var vars = string.Join(", ", new[] { "clock: null" }.Concat(later.Select((_, k) => $"wave{k}: 0")));
        y.Append($"  rx_defend:\n    fields: [user, dead, kills, lost, arrived, started{(o.Arrive is null ? "" : ", zone")}{GateFields(c)}{StepFields}{FinishField(c)}]\n    variables: {{{vars}}}\n    steps:\n");
        y.Append("      - do:\n          - startTimer(clock)\n          - set(started, 1)\n");
        if (o.Arrive is null) y.Append(string.Concat(Open(c, 2).Select(a => $"          - {a}\n")));
        y.Append("      - wait:\n");
        y.Append(Death);
        y.Append($"          - when: lost > {mayLose}\n            then: failAllObj(6, 3)\n            goto: stop\n");
        if (o.Arrive is { } a)
        {
            y.Append($"          - when: arrived == 0 && isWithinNav(zone, user, {a.Radius})\n");
            y.Append(Then(12, new[] { "set(arrived, 1)", "success(1)" }.Concat(Open(c, 2)).Concat(Gates(c, c.Spec.Gates.Trap)).Concat(Say(c, c.Spec.Radio.Trap))));
            y.Append("            goto: restart\n");
            if (a.Seconds > 0) y.Append($"          - when: arrived == 0 && timeGreater(clock, {a.Seconds})\n            then: failAllObj(6, 4)\n            goto: stop\n");
        }
        if (c.Spec.Radio.Wave.Count > 0)
            for (int k = 0; k < later.Count; k++)
            {
                // a wave's call as it starts (its kill count, or the waves before it gone; one clip a wave, in turn)
                y.Append($"          - when: wave{k} == 0 && (!(kills < {later[k].AtKills}) || arrived == 1 && !(dead < {later[k].Before}))\n");
                y.Append(Then(12, [$"set(wave{k}, 1)", .. Say(c, [c.Spec.Radio.Wave[k % c.Spec.Radio.Wave.Count]])]));
                y.Append("            goto: restart\n");
            }
        y.Append(Ending(c, $"dead == {m}", ["success(1)", "success(2)", "success(3)"]));
        bool wiped = c.Spec.Radio.Wiped.Count > 0 && mayLose >= 1;
        if (wiped)
            y.Append($"  rx_wiped:\n    fields: [lost, done]\n    steps:\n      - wait_until: '!(lost < {mayLose})'\n      - wait:\n          - when: lost == {mayLose} && done == 0\n"
                + Then(12, Say(c, c.Spec.Radio.Wiped.Take(1))) + "          - when: 'true'\n");

        y.Append("threads:\n");
        if (wiped) y.Append("  rx_wiped: {script: rx_wiped, args: {lost: rx_lost, done: rx_done}}\n");
        y.Append($"  rx_defend: {{script: rx_defend, args: {{user: rx_user, dead: rx_dead, kills: rx_kills, lost: rx_lost, arrived: rx_arrived, started: rx_started{(o.Arrive is null ? "" : ", zone: rx_zone")}{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        for (int i = 0; i < n; i++) y.Append($"  rx_lost{i}: {{script: rx_lost, args: {{s: rx_s{i}, lost: rx_lost}}}}\n");
        for (int i = 0; i < m; i++)
        {
            var args = new List<string> { $"car: rx_a{i}", "user: rx_user", "dead: rx_dead", "kills: rx_kills", "started: rx_started", "arrived: rx_arrived", $"wtime: rx_wt{i}", $"wkills: rx_wk{i}", $"wbefore: rx_wb{i}" };
            args.AddRange(Enumerable.Range(0, n).Select(k => $"s{k}: rx_s{k}"));
            args.AddRange(Enumerable.Range(0, e).SelectMany(k => new[] { $"e{k}: rx_e{k}", $"h{k}: rx_h{k}" }));
            y.Append($"  rx_raider{i}: {{script: rx_raider, args: {{{string.Join(", ", args)}}}}}\n");
        }
        return y.ToString();
    }

    /// <summary>
    /// Deliver (T05's rescue run, made generic): an optional pickup zone (a zone stands in for the package), then home, where the
    /// win needs no enemy within the clear radius. Sleepers wait parked where they are and wake when the player comes near (or
    /// when shot); pursuers wait off the map and are teleported in behind an entry once the pickup is made (at the start
    /// without one). The deadline runs on the return leg (from the pickup, T05's clock) unless it is from the start.
    /// Objectives: with a pickup 1 pickup, 2 home, 3 nobody follows; without, 1 home, 2 nobody follows. Failures: 1 death,
    /// 2 left the map, 3 too slow.
    /// </summary>
    private static string Deliver(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Home is not { } home) throw new RemixError("deliver: needs home");
        bool pick = o.Pickup is not null;
        bool fromStart = (o.DeadlineFrom ?? (pick ? "pickup" : "start")) switch
        {
            "start" => true, "pickup" when pick => false,
            var f => throw new RemixError($"deliver: deadline_from '{f}': start{(pick ? " or pickup" : " (there is no pickup)")}"),
        };
        int need = (pick ? 2 : 1) + (o.Clear > 0 ? 1 : 0);
        if (c.Spec.Text.Objectives.Count < need) throw new RemixError($"deliver: {need} objective lines");
        if (o.Deadline > 0 && c.Spec.Text.Failures.Count < 3) throw new RemixError("deliver: failure line 3 (too slow) for the deadline");
        int homeObj = pick ? 2 : 1, clearObj = homeObj + 1;
        var y = new StringBuilder();
        y.Append("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_have: {(pick ? 0 : 1)}\n  rx_home: path.{c.Place(home.Place)}\n");
        if (o.Pickup is { } pu) y.Append($"  rx_pickup: path.{c.Place(pu.Place)}\n");
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_z{i}: actor.{c.Actor(o.Sleepers[i].Car)}\n  rx_zw{i}: {N(o.Sleepers[i].Wake)}\n  rx_zs{i}: {(o.Sleepers[i].Siren ? 1 : 0)}\n");
        var pu2 = o.Pursuit;
        if (pu2 is not null)
        {
            if (pu2.Cars.Count == 0) throw new RemixError("deliver: pursuit needs cars");
            int hd = c.Heading(pu2.Entry);
            y.Append($"  rx_pentry: path.{c.Place(pu2.Entry)}\n  rx_ph: {N(hd)}\n  rx_pdelay: {N(pu2.Delay)}\n  rx_pspeed: {N(pu2.Speed)}\n");
            // each a car's length or two behind the one before, against the entry's heading (cm, as teleportOffset takes it)
            double r = hd * Math.PI / 180;
            for (int i = 0; i < pu2.Cars.Count; i++)
                y.Append($"  rx_p{i}: actor.{c.Actor(pu2.Cars[i])}\n  rx_px{i}: {N(Off(i, pu2.Cars.Count, r).X)}\n  rx_pz{i}: {N(Off(i, pu2.Cars.Count, r).Z)}\n");
        }
        // the first pursuer says the wave's line as it comes in
        var pursuitCall = string.Concat(Say(c, c.Spec.Radio.Wave.Take(1)).Select(a => $"\n              - {a}"));
        y.Append($$"""
            scripts:
              rx_sleeper:
                fields: [car, user, have, wake, siren]
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                    while_waiting: hide(car)
                  - wait_until: isWithin(car, user, wake) || isAttacked(car)
                    then:
                      - startCar(car)
                      - setSiren(car, siren)
                    while_waiting: hide(car)
                  - wait_until: isDead(user)
                    while_waiting: attack(car, user, 1)
              rx_pursuer:
                fields: [car, user, have, entry, heading, delay, speed, dx, dz, first]
                variables: {timer: null}
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                    while_waiting: hide(car)
                  - wait_until: have == 1
                    then: startTimer(timer)
                    while_waiting: hide(car)
                  - wait_until: timeGreater(timer, delay)
                    then:
                      - startCar(car)
                      - teleportOffset(car, entry, speed, heading, dx, dz)
                    while_waiting: hide(car)
                  - wait:
                      - when: first == 1
                        then:
                          - set(first, 0){{pursuitCall}}
                      - when: 'true'
                  - wait_until: isDead(user)
                    while_waiting: attack(car, user, 1)
              rx_deliver:
                fields: [user, have, home{{(pick ? ", pickup" : "")}}{{GateFields(c)}}{{StepFields}}{{FinishField(c)}}]
                variables: {clock: null}
                steps:
                  - do: startTimer(clock)

            """);
        string Fails() =>
            Death
            + (o.Deadline > 0 ? $"          - when: timeGreater(clock, {o.Deadline})\n            then: failAllObj(6, 3)\n            goto: stop\n" : "");
        if (o.Pickup is { } p)
        {
            y.Append("      - wait:\n" + Death);
            if (fromStart && o.Deadline > 0) y.Append($"          - when: timeGreater(clock, {o.Deadline})\n            then: failAllObj(6, 3)\n            goto: stop\n");
            var acts = new List<string> { "success(1)", "reveal(2)" };
            if (o.Clear > 0) acts.Add("reveal(3)");
            acts.AddRange(Open(c, 2));
            acts.Add("set(have, 1)");
            if (!fromStart) acts.Add("startTimer(clock)");   // the return leg's clock
            acts.AddRange(Gates(c, c.Spec.Gates.Trap));
            acts.AddRange(Say(c, c.Spec.Radio.Trap));
            y.Append($"          - when: isWithinNav(pickup, user, {p.Radius})\n" + Then(12, acts));
        }
        y.Append("      - wait:\n" + Fails());
        string arrive = o.Clear > 0 ? $"isWithinNav(home, user, {home.Radius}) && !isWithinEnemy(user, {o.Clear})" : $"isWithinNav(home, user, {home.Radius})";
        var win = new List<string> { $"success({homeObj})" };
        if (o.Clear > 0) win.Add($"success({clearObj})");
        y.Append(Ending(c, arrive, win));

        y.Append("threads:\n");
        y.Append($"  rx_deliver: {{script: rx_deliver, args: {{user: rx_user, have: rx_have, home: rx_home{(pick ? ", pickup: rx_pickup" : "")}{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_sleeper{i}: {{script: rx_sleeper, args: {{car: rx_z{i}, user: rx_user, have: rx_have, wake: rx_zw{i}, siren: rx_zs{i}}}}}\n");
        if (pu2 is not null)
        {
            // a flag per pursuer, set for the first only: who makes the call
            var globals = string.Concat(Enumerable.Range(0, pu2.Cars.Count).Select(i => $"  rx_pf{i}: {(i == 0 ? 1 : 0)}\n"));
            for (int i = 0; i < pu2.Cars.Count; i++)
                y.Append($"  rx_pursuer{i}: {{script: rx_pursuer, args: {{car: rx_p{i}, user: rx_user, have: rx_have, entry: rx_pentry, heading: rx_ph, delay: rx_pdelay, speed: rx_pspeed, dx: rx_px{i}, dz: rx_pz{i}, first: rx_pf{i}}}}}\n");
            y.Insert(y.ToString().IndexOf("scripts:", StringComparison.Ordinal), globals);
        }
        return y.ToString();
    }

    /// <summary>
    /// Lose the tail (the design list's lose-the-tail, a "lost them" phase before home): pursuers teleported in behind an entry
    /// (and sleepers that wake as the player passes) chase him. Once they have caught up (or a minute has gone by without), they
    /// are lost when nobody hostile has been within the lost radius for the lost seconds (dead ones count as lost); they then give up, each parking where it is and chasing again only while
    /// the player is within half that radius. The hideout counts only after that, with nobody within clear (default the lost
    /// radius); reaching it with an enemy that near fails: he led them home. Objectives: 1 lose them, 2 the hideout. Failures:
    /// 1 death, 2 left the map, then 3 too slow (with a deadline, from the start) and led them home (3 without a deadline, 4 with).
    /// </summary>
    private static string Tail(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Hideout is not { } hide) throw new RemixError("tail: needs hideout");
        var lost = o.Lost ?? new LostSpec();
        if (lost.Radius < 50 || lost.Seconds < 1) throw new RemixError("tail: lost needs a radius of 50 m or more and 1 s or more");
        if (o.Pursuit is null && o.Sleepers.Count == 0) throw new RemixError("tail: needs pursuit or sleepers");
        if (o.Pursuit is { Cars.Count: 0 }) throw new RemixError("tail: pursuit needs cars");
        int clear = o.Clear > 0 ? o.Clear : lost.Radius;
        int led = o.Deadline > 0 ? 4 : 3;
        if (c.Spec.Text.Objectives.Count < 2) throw new RemixError("tail: two objective lines (lose them, the hideout)");
        if (c.Spec.Text.Failures.Count < led) throw new RemixError($"tail: {led} failure lines ({(o.Deadline > 0 ? "3 too slow, 4" : "3")} led them home)");
        int near = Math.Max(50, lost.Radius / 2);
        var y = new StringBuilder("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_lost: 0\n  rx_hide: path.{c.Place(hide.Place)}\n  rx_near: {N(near)}\n  rx_far: {N(lost.Radius)}\n");
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_z{i}: actor.{c.Actor(o.Sleepers[i].Car)}\n  rx_zw{i}: {N(o.Sleepers[i].Wake)}\n  rx_zs{i}: {(o.Sleepers[i].Siren ? 1 : 0)}\n");
        var pu = o.Pursuit;
        if (pu is not null)
        {
            int hd = c.Heading(pu.Entry);
            y.Append($"  rx_pentry: path.{c.Place(pu.Entry)}\n  rx_ph: {N(hd)}\n  rx_pdelay: {N(pu.Delay)}\n  rx_pspeed: {N(pu.Speed)}\n  rx_psiren: {(pu.Siren ? 1 : 0)}\n");
            double r = hd * Math.PI / 180;
            for (int i = 0; i < pu.Cars.Count; i++)
                y.Append($"  rx_p{i}: actor.{c.Actor(pu.Cars[i])}\n  rx_px{i}: {N(Off(i, pu.Cars.Count, r).X)}\n  rx_pz{i}: {N(Off(i, pu.Cars.Count, r).Z)}\n  rx_pf{i}: {(i == 0 ? 1 : 0)}\n");
        }
        // what a woken car does: chase until they are lost, then park, and chase only while the player is near again
        string Hunt() => """
                  - wait_until: lost == 1
                    while_waiting: attack(car, user, 1)
                  - repeat:
                      - wait_until: isWithin(car, user, near) || isAttacked(car)
                        while_waiting: sit(car)
                      - wait_until: '!isWithin(car, user, far)'
                        while_waiting: attack(car, user, 1)

            """;
        var call = string.Concat(Say(c, c.Spec.Radio.Wave.Take(1)).Select(a => $"\n              - {a}"));
        int quietFrom = (pu?.Delay ?? 0) + 3;   // the pursuers are in before the lost clock starts
        y.Append($$"""
            scripts:
              rx_tsleeper:
                fields: [car, user, lost, wake, siren, near, far]
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                    while_waiting: hide(car)
                  - wait_until: isWithin(car, user, wake) || isAttacked(car)
                    then:
                      - startCar(car)
                      - setSiren(car, siren)
                    while_waiting: hide(car)

            """);
        y.Append(Hunt());
        y.Append($$"""
              rx_tpursuer:
                fields: [car, user, lost, entry, heading, delay, speed, dx, dz, first, siren, near, far]
                variables: {timer: null}
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                      - startTimer(timer)
                    while_waiting: hide(car)
                  - wait_until: timeGreater(timer, delay)
                    then:
                      - startCar(car)
                      - teleportOffset(car, entry, speed, heading, dx, dz)
                      - setSiren(car, siren)
                    while_waiting: hide(car)
                  - wait:
                      - when: first == 1
                        then:
                          - set(first, 0){{call}}
                      - when: 'true'

            """);
        y.Append(Hunt());
        y.Append($$"""
              rx_quiet:
                fields: [user, lost]
                variables: {quiet: null}
                steps:
                  - do: startTimer(quiet)
                  - wait_until: timeGreater(quiet, {{quietFrom}})
                  - wait_until: isWithinEnemy(user, {{lost.Radius}}) || timeGreater(quiet, {{quietFrom + 60}})
                  - id: rx_near
                    wait_until: '!isWithinEnemy(user, {{lost.Radius}})'
                    then: startTimer(quiet)
                  - wait:
                      - when: isWithinEnemy(user, {{lost.Radius}})
                        goto: rx_near
                      - when: timeGreater(quiet, {{lost.Seconds}})
                        then: set(lost, 1)
                        goto: stop
              rx_tail:
                fields: [user, lost, hide{{GateFields(c)}}{{StepFields}}{{FinishField(c)}}]
                variables: {clock: null}
                steps:
                  - do: startTimer(clock)

            """);
        // "led them home" counts only once the hideout is revealed (objective 2 open): a hideout the player has not been told of is no
        // place he can fail at (a position-based end before its step opens: docs/remix-missions.md "Boundaries")
        string Fails(bool ledHome = true) =>
            "      - wait:\n" + Death
            + (o.Deadline > 0 ? $"          - when: timeGreater(clock, {o.Deadline})\n            then: failAllObj(6, 3)\n            goto: stop\n" : "")
            + (ledHome ? $"          - when: isWithinNav(hide, user, {hide.Radius}) && isWithinEnemy(user, {clear})\n            then: failAllObj(6, {led})\n            goto: stop\n" : "");
        y.Append(Fails(ledHome: false));
        y.Append("          - when: lost == 1\n" + Then(12, new[] { "success(1)", "reveal(2)" }.Concat(Open(c, 2)).Concat(Gates(c, c.Spec.Gates.Trap)).Concat(Say(c, c.Spec.Radio.Trap))));
        y.Append(Fails());
        y.Append(Ending(c, $"isWithinNav(hide, user, {hide.Radius})", ["success(2)"]));

        y.Append("threads:\n");
        y.Append($"  rx_tail: {{script: rx_tail, args: {{user: rx_user, lost: rx_lost, hide: rx_hide{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        y.Append("  rx_quiet: {script: rx_quiet, args: {user: rx_user, lost: rx_lost}}\n");
        for (int i = 0; i < o.Sleepers.Count; i++)
            y.Append($"  rx_tsleeper{i}: {{script: rx_tsleeper, args: {{car: rx_z{i}, user: rx_user, lost: rx_lost, wake: rx_zw{i}, siren: rx_zs{i}, near: rx_near, far: rx_far}}}}\n");
        if (pu is not null)
            for (int i = 0; i < pu.Cars.Count; i++)
                y.Append($"  rx_tpursuer{i}: {{script: rx_tpursuer, args: {{car: rx_p{i}, user: rx_user, lost: rx_lost, entry: rx_pentry, heading: rx_ph, delay: rx_pdelay, speed: rx_pspeed, dx: rx_px{i}, dz: rx_pz{i}, first: rx_pf{i}, siren: rx_psiren, near: rx_near, far: rx_far}}}}\n");
        return y.ToString();
    }

    /// <summary>
    /// Intercept a convoy (the design list's convoy ambush): the lead drives a route to its exit, the escorts keeping station
    /// behind it; once alerted (the player within the alert radius, the lead or an escort shot (a bump among themselves is no
    /// alert), or the lead dead) the lead speeds
    /// up and the escorts turn on the player. The lead reaching its exit fails. Objectives: 1 the lead dead, 2 the escorts dead
    /// (with kill_escorts). Failures: 1 death, 2 left the map, 3 the convoy got through.
    /// </summary>
    private static string Intercept(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Convoy is not { } cv || cv.Lead.Length == 0 || cv.Route.Length == 0 || cv.Exit is not { } exit)
            throw new RemixError("intercept: needs convoy with lead, route and exit");
        bool esc = cv.KillEscorts && cv.Escorts.Count > 0;
        int n = cv.Escorts.Count;
        if (c.Spec.Text.Objectives.Count < (esc ? 2 : 1)) throw new RemixError($"intercept: {(esc ? 2 : 1)} objective lines (the lead{(esc ? ", the escorts" : "")})");
        if (c.Spec.Text.Failures.Count < 3) throw new RemixError("intercept: failure line 3 (the convoy got through)");
        if (cv.Speed < 1 || cv.AlertSpeed < cv.Speed) throw new RemixError("intercept: speed 1 or more, alert_speed no less");
        var y = new StringBuilder("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_lead: actor.{c.Actor(cv.Lead)}\n  rx_route: path.{c.Place(cv.Route)}\n  rx_exit: path.{c.Place(exit.Place)}\n  rx_alert: 0\n  rx_edead: 0\n");
        // stations: in pairs behind the lead, either side of its track (metres in the lead's frame, +z ahead)
        for (int i = 0; i < n; i++)
            y.Append($"  rx_c{i}: actor.{c.Actor(cv.Escorts[i])}\n  rx_cx{i}: {N(n == 1 ? 0 : i % 2 == 0 ? -4 : 4)}\n  rx_cz{i}: {N(-cv.Spacing * (i / 2 + 1))}\n");
        y.Append($$"""
            scripts:
              rx_lead:
                fields: [car, user, route, alert]
                variables: {timer: null}
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                      - startCar(car)
                      - startTimer(timer)
                  - wait_until: timeGreater(timer, {{cv.Delay}}){{(cv.Roll > 0 ? $" || isWithin(car, user, {cv.Roll})" : "")}}
                    while_waiting: sit(car)
                  - wait_until: alert == 1 || isShot(car) || isWithin(car, user, {{cv.Alert}})
                    then: set(alert, 1)
                    while_waiting: goto(car, route, {{cv.Speed}})
                  - wait_until: isDead(user)
                    while_waiting: goto(car, route, {{cv.AlertSpeed}})
              rx_escort:
                fields: [car, lead, user, alert, edead, dx, dz]
                guards:
                  - when: isDead(car)
                    then: inc(edead)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, {{o.Skill}}, {{o.Skill}})
                      - setAgg(car, {{o.Aggression}})
                      - startCar(car)
                  - wait_until: alert == 1 || isShot(car) || isDead(lead)
                    then: set(alert, 1)
                    while_waiting: follow(car, lead, user, 0, dx, dz)
                  - wait_until: isDead(user)
                    while_waiting: attack(car, user, 1)
              rx_intercept:
                fields: [user, lead, exit, alert, edead{{GateFields(c)}}{{StepFields}}{{FinishField(c)}}]
                variables: {said: 0}
                steps:

            """);
        y.Append("      - wait:\n" + Death);
        y.Append($"          - when: '!isDead(lead) && isWithinNav(exit, lead, {exit.Radius})'\n            then: failAllObj(6, 3)\n            goto: stop\n");
        if (c.Spec.Radio.Wave.Count > 0)
            y.Append("          - when: alert == 1 && said == 0\n" + Then(12, ["set(said, 1)", .. Say(c, c.Spec.Radio.Wave.Take(1))]) + "            goto: restart\n");
        if (esc)
        {
            y.Append("          - when: isDead(lead)\n" + Then(12, new[] { "success(1)", "reveal(2)" }.Concat(Open(c, 2)).Concat(Say(c, c.Spec.Radio.Trap))));
            y.Append("      - wait:\n" + Death);
            y.Append(Ending(c, $"'!(edead < {n})'", ["success(2)"]));
        }
        else y.Append(Ending(c, "isDead(lead)", new[] { "success(1)" }.Concat(Say(c, c.Spec.Radio.Trap))));
        var (countScript, countThread) = esc ? CountScript(c, "rx_edead", 2) : ("", "");
        y.Append(countScript);
        // the lead close to its way out, alive: the warning (once)
        if (c.Spec.Radio.Getaway.Count > 0)
            y.Append($"  rx_getaway:\n    fields: [lead, exit, done]\n    steps:\n      - wait_until: isDead(lead) || isWithinNav(exit, lead, {Math.Max(exit.Radius + 200, 600)})\n      - wait:\n          - when: '!isDead(lead) && done == 0'\n"
                + Then(12, Say(c, c.Spec.Radio.Getaway.Take(1))) + "          - when: 'true'\n");

        y.Append("threads:\n");
        y.Append(countThread);
        if (c.Spec.Radio.Getaway.Count > 0) y.Append("  rx_getaway: {script: rx_getaway, args: {lead: rx_lead, exit: rx_exit, done: rx_done}}\n");
        y.Append($"  rx_intercept: {{script: rx_intercept, args: {{user: rx_user, lead: rx_lead, exit: rx_exit, alert: rx_alert, edead: rx_edead{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        y.Append("  rx_lead: {script: rx_lead, args: {car: rx_lead, user: rx_user, route: rx_route, alert: rx_alert}}\n");
        for (int i = 0; i < n; i++)
            y.Append($"  rx_escort{i}: {{script: rx_escort, args: {{car: rx_c{i}, lead: rx_lead, user: rx_user, alert: rx_alert, edead: rx_edead, dx: rx_cx{i}, dz: rx_cz{i}}}}}\n");
        return y.ToString();
    }

    /// <summary>
    /// Escort (the design list's escort, written fresh): a slow friendly car drives a route to its goal by way of a halfway point.
    /// It drives only while the player keeps up: once he is more than the wait radius away it stops, and goes on when he is back
    /// within the resume radius (the stock escorts' helpers ran ahead or fell behind: the owner's complaint), so it never meets an
    /// ambush alone. Wingmen ride with the escorted car, in line behind it on the road (a station off to its side put one in a ditch), firing at the nearest enemy, and stop when
    /// it stops (one left following a stopped van nosed into it and stayed in its collision dodge; following the
    /// player instead, a wingman fell 250 m behind, forever avoiding the player's own car in the column). Ambushers (sleepers on the way,
    /// reinforcements as a step opens, some of them for the escorted car) come for them. Objectives: 1 the halfway point, 2 the
    /// goal (then the finish, when there is one). Failures: 1 death, 2 left the map, 3 the escorted car destroyed.
    /// </summary>
    private static string Escort(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Escort is not { } e || e.Car.Length == 0 || e.Route.Length == 0 || e.Mid is not { } mid || e.Goal is not { } goal)
            throw new RemixError("escort: needs escort with car, route, mid and goal");
        if (c.Spec.Text.Objectives.Count < 2) throw new RemixError("escort: two objective lines (the halfway point, the goal)");
        if (c.Spec.Text.Failures.Count < 3) throw new RemixError("escort: failure line 3 (the escorted car destroyed)");
        if (e.Speed < 1 || e.Resume < 20 || e.Wait <= e.Resume) throw new RemixError("escort: speed 1 or more, resume 20 m or more, wait beyond resume");
        var y = new StringBuilder("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_van: actor.{c.Actor(e.Car)}\n  rx_route: path.{c.Place(e.Route)}\n  rx_mid: path.{c.Place(mid.Place)}\n  rx_goal: path.{c.Place(goal.Place)}\n  rx_vgo: 1\n");
        // the wingmen's stations: in line 40 m behind the escorted car and more (metres in its frame, +z ahead): nearer, one kept nosing
        // into its back and stopping to dodge it, and fell 150 m behind
        for (int i = 0; i < e.Wingmen.Count; i++)
            y.Append($"  rx_w{i}: actor.{c.Actor(e.Wingmen[i])}\n  rx_wx{i}: 0\n  rx_wz{i}: {N(-40 - 15 * i)}\n");
        y.Append($$"""
            scripts:
              rx_van:
                fields: [car, user, route, go]
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, 2, 2)
                      - startCar(car)
                  - repeat:
                      - wait_until: '!isWithin(car, user, {{e.Wait}})'
                        then: set(go, 0)
                        while_waiting: goto(car, route, {{e.Speed}})
                      - wait_until: isWithin(car, user, {{e.Resume}})
                        then: set(go, 1)
                        while_waiting: sit(car)
              rx_wing:
                fields: [car, van, go, dx, dz]
                variables: {foe: null, timer: null}
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - do:
                      - setSkill(car, 2, 2)
                      - startCar(car)
                  - repeat:
                      - wait_until: go == 1
                        while_waiting: sit(car)
                      - do:
                          - nearestEnemy(car, foe)
                          - startTimer(timer)
                      - wait_until: timeGreater(timer, 3) || go == 0
                        while_waiting: follow(car, van, foe, 1, dx, dz)
              rx_escort:
                fields: [user, van, mid, goal{{GateFields(c)}}{{StepFields}}{{FinishField(c)}}]
                steps:

            """);
        string vanDead = "          - when: isDead(van)\n            then: failAllObj(6, 3)\n            goto: stop\n";
        y.Append("      - wait:\n" + Death + vanDead);
        y.Append($"          - when: isWithinNav(mid, van, {mid.Radius})\n" + Then(12, new[] { "success(1)", "reveal(2)" }.Concat(Open(c, 2)).Concat(Gates(c, c.Spec.Gates.Trap)).Concat(Say(c, c.Spec.Radio.Trap))));
        y.Append("      - wait:\n" + Death + vanDead);
        // the goal: the escorted car there (the finish, when there is one, then wants the player there with nobody near)
        y.Append(Ending(c, $"isWithinNav(goal, van, {goal.Radius})", ["success(2)"]).Replace("      - wait:\n" + Death, "      - wait:\n" + Death + vanDead));
        y.Append("threads:\n");
        y.Append($"  rx_escort: {{script: rx_escort, args: {{user: rx_user, van: rx_van, mid: rx_mid, goal: rx_goal{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        y.Append("  rx_van: {script: rx_van, args: {car: rx_van, user: rx_user, route: rx_route, go: rx_vgo}}\n");
        for (int i = 0; i < e.Wingmen.Count; i++) y.Append($"  rx_wing{i}: {{script: rx_wing, args: {{car: rx_w{i}, van: rx_van, go: rx_vgo, dx: rx_wx{i}, dz: rx_wz{i}}}}}\n");
        return y.ToString();
    }

    /// <summary>
    /// Raid (the design list's hit-and-run): the targets (structures of the base, or parked cars that never start) stand at a place
    /// with guards parked by them; objective 1 is getting there (or hitting one from afar), 2 every target down, 3 getting out to the
    /// escape zone before the roads close: the escape's seconds run from the last target's fall, and the reinforcements that come
    /// in as step 3 opens close in on the way out. Objectives: 1 there, 2 the targets, 3 out. Failures: 1 death, 2 left the map,
    /// 3 too slow getting out.
    /// </summary>
    private static string Raid(Context c)
    {
        var o = c.Spec.Objective;
        if (o.Targets.Count == 0 || o.Escape is not { } esc || esc.Seconds <= 0) throw new RemixError("raid: needs targets and escape (with its seconds)");
        if (c.Spec.Text.Objectives.Count < 3) throw new RemixError("raid: three objective lines (get there, the targets, get out)");
        if (c.Spec.Text.Failures.Count < 3) throw new RemixError("raid: failure line 3 (too slow getting out)");
        int n = o.Targets.Count;
        var y = new StringBuilder("globals:\n");
        y.Append($"  rx_user: actor.{c.Player}\n  rx_tdead: 0\n  rx_escape: path.{c.Place(esc.Place)}\n");
        if (o.Arrive is { } ar) y.Append($"  rx_zone: path.{c.Place(ar.Place)}\n");
        for (int i = 0; i < n; i++) y.Append($"  rx_t{i}: actor.{c.Actor(o.Targets[i])}\n");
        var parked = o.Targets.Where(t => c.Spec.Cars.ContainsKey(t)).ToList();
        y.Append("""
            scripts:
              rx_target:
                fields: [t, tdead]
                steps:
                  - wait_until: isDead(t)
                    then: inc(tdead)
              rx_parked:
                fields: [car]
                guards:
                  - when: isDead(car)
                    goto: stop
                steps:
                  - wait_until: 'false'
                    while_waiting: hide(car)

            """);
        y.Append($"  rx_raid:\n    fields: [user, tdead, escape{(o.Arrive is null ? "" : ", zone")}{GateFields(c)}{StepFields}{FinishField(c)}]\n    variables: {{out: null}}\n    steps:\n");
        y.Append("      - wait:\n" + Death);
        string there = o.Arrive is { } a ? $"isWithinNav(zone, user, {a.Radius}) || tdead > 0" : "tdead > 0";
        y.Append($"          - when: {there}\n" + Then(12, new[] { "success(1)", "reveal(2)" }.Concat(Open(c, 2)).Concat(Gates(c, c.Spec.Gates.Trap)).Concat(Say(c, c.Spec.Radio.Trap))));
        y.Append("      - wait:\n" + Death);
        y.Append($"          - when: '!(tdead < {n})'\n" + Then(12, new[] { "success(2)", "reveal(3)", "startTimer(out)" }.Concat(Open(c, 3))));
        y.Append("      - wait:\n" + Death + $"          - when: timeGreater(out, {esc.Seconds})\n            then: failAllObj(6, 3)\n            goto: stop\n");
        y.Append(Ending(c, $"isWithinNav(escape, user, {esc.Radius})", ["success(3)"]));
        var (countScript, countThread) = CountScript(c, "rx_tdead", 2);
        y.Append(countScript);
        y.Append("threads:\n");
        y.Append(countThread);
        y.Append($"  rx_raid: {{script: rx_raid, args: {{user: rx_user, tdead: rx_tdead, escape: rx_escape{(o.Arrive is null ? "" : ", zone: rx_zone")}{GateArgs(c)}{StepArgs}{FinishArg(c)}}}}}\n");
        for (int i = 0; i < n; i++) y.Append($"  rx_target{i}: {{script: rx_target, args: {{t: rx_t{i}, tdead: rx_tdead}}}}\n");
        for (int i = 0; i < n; i++) if (parked.Contains(o.Targets[i])) y.Append($"  rx_parked{i}: {{script: rx_parked, args: {{car: rx_t{i}}}}}\n");
        return y.ToString();
    }
}
