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

    public static string Emit(Context c) => Common(c) + (c.Spec.Objective.Template switch
    {
        "defend" => Defend(c),
        "deliver" => Deliver(c),
        var t => throw new RemixError($"objective template '{t}': defend or deliver"),
    });

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string SoundKey(string clip) => "rx_cb_" + clip.ToLowerInvariant();

    /// <summary>
    /// What every template shares, as a document of its own (merged like the template's): the radio's clips in the sounds table,
    /// the gate queue and the start's gates and radio. A gate ignores a trigger while it slides (GateMotion: the original's), so
    /// the templates never trigger one directly: each moment adds one to the gate's count of wanted triggers (rx_gwK), and a
    /// thread per gate sends them one at a time, <see cref="GateSpec.Settle"/> seconds apart.
    /// </summary>
    private static string Common(Context c)
    {
        var y = new StringBuilder();
        var clips = c.Spec.Radio.All.ToList();
        foreach (var clip in clips)
            if (clip.Length is 0 or > 34 || clip.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_')) throw new RemixError($"radio clip '{clip}': a sound file's stem");
        if (clips.Count > 0)
        {
            y.Append("sounds:\n");
            foreach (var clip in clips) y.Append($"  {SoundKey(clip)}: {clip.ToLowerInvariant()}.wav\n");
        }
        var gates = GateList(c);
        if (gates.Count > 0)
        {
            if (c.Spec.Gates.Settle is < 1 or > 600) throw new RemixError($"gates: settle {c.Spec.Gates.Settle}: 1-600 seconds");
            y.Append("globals:\n");
            for (int k = 0; k < gates.Count; k++) y.Append($"  rx_g{k}: actor.{c.Actor(gates[k])}\n  rx_gw{k}: 0\n");
        }
        bool start = c.Spec.Gates.Start.Count > 0 || c.Spec.Radio.Start.Count > 0;
        if (gates.Count > 0 || start) y.Append("scripts:\n");
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
            foreach (var a in Gates(c, c.Spec.Gates.Start).Concat(Say(c.Spec.Radio.Start))) y.Append($"          - {a}\n");
        }
        if (gates.Count > 0 || start) y.Append("threads:\n");
        for (int k = 0; k < gates.Count; k++) y.Append($"  rx_gate{k}: {{script: rx_gate, args: {{gate: rx_g{k}, want: rx_gw{k}}}}}\n");
        if (start) y.Append($"  rx_start: {{script: rx_start, args: {{{GateArgs(c, lead: false)}}}}}\n");
        return y.Length == 0 ? "" : y.Append("---\n").ToString();
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

    private static IEnumerable<string> Say(IEnumerable<string> clips) => clips.Select(k => $"cbPrior(sound.{SoundKey(k)}, 3)");

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
        y.Append($"  rx_defend:\n    fields: [user, dead, kills, lost, arrived, started{(o.Arrive is null ? "" : ", zone")}{GateFields(c)}]\n    variables: {{{vars}}}\n    steps:\n");
        y.Append("      - do:\n          - startTimer(clock)\n          - set(started, 1)\n");
        y.Append("      - wait:\n");
        y.Append("          - when: isDead(user)\n            then: failAllObj(6, 1)\n            goto: stop\n");
        y.Append($"          - when: lost > {mayLose}\n            then: failAllObj(6, 3)\n            goto: stop\n");
        if (o.Arrive is { } a)
        {
            y.Append($"          - when: arrived == 0 && isWithinNav(zone, user, {a.Radius})\n");
            y.Append(Then(12, new[] { "set(arrived, 1)", "success(1)" }.Concat(Gates(c, c.Spec.Gates.Trap)).Concat(Say(c.Spec.Radio.Trap))));
            y.Append("            goto: restart\n");
            if (a.Seconds > 0) y.Append($"          - when: arrived == 0 && timeGreater(clock, {a.Seconds})\n            then: failAllObj(6, 4)\n            goto: stop\n");
        }
        if (c.Spec.Radio.Wave.Count > 0)
            for (int k = 0; k < later.Count; k++)
            {
                // a wave's call as it starts (its kill count, or the waves before it gone; one clip a wave, in turn)
                y.Append($"          - when: wave{k} == 0 && (!(kills < {later[k].AtKills}) || arrived == 1 && !(dead < {later[k].Before}))\n");
                y.Append(Then(12, [$"set(wave{k}, 1)", .. Say([c.Spec.Radio.Wave[k % c.Spec.Radio.Wave.Count]])]));
                y.Append("            goto: restart\n");
            }
        y.Append($"          - when: dead == {m}\n");
        y.Append(Then(12, new[] { "success(1)", "success(2)", "success(3)" }.Concat(Gates(c, c.Spec.Gates.Win)).Concat(Say(c.Spec.Radio.Win))));
        y.Append("      - wait:\n          - when: isDead(user)\n            then: failAllObj(6, 1)\n            goto: stop\n");
        y.Append("          - when: isCBEmpty()\n            then: successAll(3)\n            goto: stop\n");

        y.Append("threads:\n");
        y.Append($"  rx_defend: {{script: rx_defend, args: {{user: rx_user, dead: rx_dead, kills: rx_kills, lost: rx_lost, arrived: rx_arrived, started: rx_started{(o.Arrive is null ? "" : ", zone: rx_zone")}{GateArgs(c)}}}}}\n");
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
                y.Append($"  rx_p{i}: actor.{c.Actor(pu2.Cars[i])}\n  rx_px{i}: {N((int)Math.Round(-Math.Sin(r) * 1200 * i))}\n  rx_pz{i}: {N((int)Math.Round(-Math.Cos(r) * 1200 * i))}\n");
        }
        // the first pursuer says the wave's line as it comes in
        var pursuitCall = string.Concat(Say(c.Spec.Radio.Wave.Take(1)).Select(a => $"\n              - {a}"));
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
                fields: [user, have, home{{(pick ? ", pickup" : "")}}{{GateFields(c)}}]
                variables: {clock: null}
                steps:
                  - do: startTimer(clock)

            """);
        string Fails() =>
            "          - when: isDead(user)\n            then: failAllObj(6, 1)\n            goto: stop\n"
            + (o.Deadline > 0 ? $"          - when: timeGreater(clock, {o.Deadline})\n            then: failAllObj(6, 3)\n            goto: stop\n" : "");
        if (o.Pickup is { } p)
        {
            y.Append("      - wait:\n" + "          - when: isDead(user)\n            then: failAllObj(6, 1)\n            goto: stop\n");
            if (fromStart && o.Deadline > 0) y.Append($"          - when: timeGreater(clock, {o.Deadline})\n            then: failAllObj(6, 3)\n            goto: stop\n");
            var acts = new List<string> { "success(1)", "reveal(2)" };
            if (o.Clear > 0) acts.Add("reveal(3)");
            acts.Add("set(have, 1)");
            if (!fromStart) acts.Add("startTimer(clock)");   // the return leg's clock
            acts.AddRange(Gates(c, c.Spec.Gates.Trap));
            acts.AddRange(Say(c.Spec.Radio.Trap));
            y.Append($"          - when: isWithinNav(pickup, user, {p.Radius})\n" + Then(12, acts));
        }
        y.Append("      - wait:\n" + Fails());
        string arrive = o.Clear > 0 ? $"isWithinNav(home, user, {home.Radius}) && !isWithinEnemy(user, {o.Clear})" : $"isWithinNav(home, user, {home.Radius})";
        var win = new List<string> { $"success({homeObj})" };
        if (o.Clear > 0) win.Add($"success({clearObj})");
        win.AddRange(Gates(c, c.Spec.Gates.Win));
        win.AddRange(Say(c.Spec.Radio.Win));
        y.Append($"          - when: {arrive}\n" + Then(12, win));
        y.Append("      - wait:\n          - when: isDead(user)\n            then: failAllObj(6, 1)\n            goto: stop\n");
        y.Append("          - when: isCBEmpty()\n            then: successAll(3)\n            goto: stop\n");

        y.Append("threads:\n");
        y.Append($"  rx_deliver: {{script: rx_deliver, args: {{user: rx_user, have: rx_have, home: rx_home{(pick ? ", pickup: rx_pickup" : "")}{GateArgs(c)}}}}}\n");
        for (int i = 0; i < o.Sleepers.Count; i++) y.Append($"  rx_sleeper{i}: {{script: rx_sleeper, args: {{car: rx_z{i}, user: rx_user, have: rx_have, wake: rx_zw{i}, siren: rx_zs{i}}}}}\n");
        if (pu2 is not null)
        {
            // a flag per pursuer, set for the first only: who makes the call
            var globals = string.Concat(Enumerable.Range(0, pu2.Cars.Count).Select(i => $"  rx_pf{i}: {(i == 0 ? 1 : 0)}\n"));
            for (int i = 0; i < pu2.Cars.Count; i++)
                y.Append($"  rx_pursuer{i}: {{script: rx_pursuer, args: {{car: rx_p{i}, user: rx_user, have: rx_have, entry: rx_pentry, heading: rx_ph, delay: rx_pdelay, speed: rx_pspeed, dx: rx_px{i}, dz: rx_pz{i}, first: rx_pf{i}}}}}\n");
            y.Insert(y.ToString().IndexOf("scripts:\n", StringComparison.Ordinal), globals);
        }
        return y.ToString();
    }
}
