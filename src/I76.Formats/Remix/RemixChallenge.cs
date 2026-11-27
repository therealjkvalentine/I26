namespace I76.Formats.Remix;

/// <summary>
/// A job's challenge, as a number (docs/remix-missions.md "Challenge"): what comes at the player, how much of it at once, how
/// many steps the job has and how tight its clock is. Calibrated on Back to Spanner (x14back), which the owner found a good
/// challenge: the director builds each rolled job to a budget of that score times its level.
///
///   car     s = strength × skill: strength is the car's hit points (armour + chassis) and its guns' damage a second, each
///             against the player's Piranha's, as sqrt(hp × dps) (1: a match for the Piranha); skill 1 (best) .. 5 is
///             × 1.3 .. × 0.7. A convoy's lead counts half (it runs; its escorts fight).
///   group   the cars that come at the player together: a wave, a pursuit, a reinforcement, sleepers parked within 300 m of
///             each other, a convoy. A group is (Σ s)^1.5: two cars at once are harder than two one after the other.
///   steps   + 1 for each objective step after the first (a reach, a fight, a return, a finish).
///   clock   + 2 × (the time the job's legs need at 22 m/s ÷ the deadline), capped at 2: a deadline you can just make.
///
/// Score = Σ groups + steps + clock.
/// </summary>
public static class RemixChallenge
{
    /// <summary>Back to Spanner's score with the install's cars (i76 remix score --builtin; docs/remix-missions.md): the budget's 1.0.</summary>
    public const double Spanner = 16.8;

    /// <summary>The director's levels, as budgets in Spanner's units: easy, fair, Spanner's, hard, brutal.</summary>
    public static readonly double[] Levels = [0.6, 0.8, 1.0, 1.25, 1.6];

    /// <summary>The level the menu's RANDOM JOB rolls at (Spanner's).</summary>
    public const int DefaultLevel = 2;

    /// <summary>A car's strength in Piranhas, by its car file: the measured averages of the director's pools (i76 remix score --cars).</summary>
    public static double PoolStrength(string vcf) =>
        Array.IndexOf(RemixDirector.Police, vcf) >= 0 ? 0.87
        : Array.IndexOf(RemixDirector.Leads, vcf) >= 0 ? 1.32
        : RemixDirector.Heat[0].Contains(vcf) ? 0.46
        : RemixDirector.Heat[1].Contains(vcf) ? 0.81
        : RemixDirector.Heat[2].Contains(vcf) ? 1.21
        : 0.8;

    public static double Skill(int skill) => 1.3 - 0.15 * (Math.Clamp(skill, 1, 5) - 1);

    /// <summary>The score's parts, for reading.</summary>
    public sealed record Parts(double Groups, int Steps, double Clock, List<(string Name, double Score)> ByGroup)
    {
        public double Total => Groups + Steps + Clock;
        public override string ToString() =>
            $"{Total:0.0} = groups {Groups:0.0} [{string.Join(", ", ByGroup.Select(g => $"{g.Name} {g.Score:0.0}"))}] + steps {Steps} + clock {Clock:0.0}";
    }

    /// <summary>
    /// The score of a description. <paramref name="strength"/> gives a car's strength in Piranhas by its name in the description
    /// (a cars entry, a bind or a stock actor); <paramref name="at"/> where it waits (for grouping sleepers), or null.
    /// </summary>
    public static Parts Score(RemixSpec spec, Func<string, double> strength, Func<string, double[]?> at)
    {
        var o = spec.Objective;
        double k = Skill(o.Skill);
        var groups = new List<(string, double)>();
        void Group(string name, IEnumerable<string> cars, Func<string, double>? weight = null)
        {
            double sum = cars.Sum(c => strength(c) * k * (weight?.Invoke(c) ?? 1));
            if (sum > 0) groups.Add((name, Math.Pow(sum, 1.5)));
        }
        for (int i = 0; i < o.Waves.Count; i++) Group($"wave {i + 1}", o.Waves[i].Cars);
        if (o.Pursuit is { } p) Group("pursuit", p.Cars);
        for (int i = 0; i < o.Reinforce.Count; i++) Group($"reinforcement {i + 1}", o.Reinforce[i].Cars);
        if (o.Convoy is { } cv) Group("convoy", [cv.Lead, .. cv.Escorts], c => c == cv.Lead ? 0.5 : 1);
        // sleepers: those parked within 300 m of each other wake together
        var left = o.Sleepers.Select(s => s.Car).ToList();
        int n = 0;
        while (left.Count > 0)
        {
            var first = left[0];
            var p0 = at(first);
            var g = left.Where(c => c == first || p0 is not null && at(c) is { } pc && Math.Sqrt((pc[0] - p0[0]) * (pc[0] - p0[0]) + (pc[1] - p0[1]) * (pc[1] - p0[1])) < 300).ToList();
            left.RemoveAll(g.Contains);
            Group($"sleepers {++n}", g);
        }
        int steps = Math.Max(0, Math.Max(RemixTemplates.OwnObjectives(spec), RemixTemplates.FinishObjective(spec)) - 1);
        double clock = 0;
        if (o.Deadline > 0 && Legs(spec) is var need and > 0) clock = Math.Min(2, 2 * need / 22 / o.Deadline);
        return new Parts(groups.Sum(g => g.Item2), steps, clock, groups);
    }

    /// <summary>The metres the clock covers: the legs between the job's places in order (from the start, or the pickup for a return clock).</summary>
    private static double Legs(RemixSpec spec)
    {
        var o = spec.Objective;
        double[]? P(ZoneSpec? z) => z is not null && spec.Places.TryGetValue(z.Place, out var pts) && pts.Count > 0 ? pts[0] : null;
        var chain = new List<double[]?>();
        bool fromPickup = o.Pickup is not null && (o.DeadlineFrom ?? "pickup") == "pickup";
        if (!fromPickup) chain.Add(spec.Start?.At);
        chain.Add(P(o.Pickup)); chain.Add(P(o.Arrive)); chain.Add(P(o.Home)); chain.Add(P(o.Hideout));
        var pts = chain.Where(x => x is { Length: 2 }).Cast<double[]>().ToList();
        double d = 0;
        for (int i = 1; i < pts.Count; i++) d += Math.Sqrt((pts[i][0] - pts[i - 1][0]) * (pts[i][0] - pts[i - 1][0]) + (pts[i][1] - pts[i - 1][1]) * (pts[i][1] - pts[i - 1][1]));
        return d * 1.3;   // roads wind
    }
}
