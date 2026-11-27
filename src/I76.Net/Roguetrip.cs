using I76.Formats.Remix;

namespace I76.Net;

/// <summary>A co-op player's car as the route map and the debrief show it: how much is left of it, and whether it was killed in the last job.</summary>
public sealed record RoguetripCrew(string Name, string Car, byte Armour, byte Chassis, byte Ammo, bool Out);

/// <summary>
/// A co-op roguetrip as the host has it (the I26 play fork's, <see cref="MsgType.RoguetripState"/>, only to a peer that says
/// <see cref="PeerFeatures.Roguetrip"/>; docs/remix-missions.md "Roguetrip in co-op"): the run (its number, phase, the jobs played), the
/// route map's offers with the host's highlighted one, the job about to be played or being played (<see cref="Job"/>, what each client
/// builds the mission from) and the hash of the host's build of it, the last job's objectives for the debrief, and the crew's cars. Sent
/// whenever it changes, and before every join accept of the game, so a client has built the job before it reads the accept.
/// </summary>
public sealed record RoguetripState(int Run, int Builder, string Build, string Phase, int Busts, int Won, int Kills, string Map,
    IReadOnlyList<RunLeg> Legs, IReadOnlyList<RunOffer> Offers, int Selected, CoopJob? Job, byte[] Hash,
    IReadOnlyList<CoopObjective> Objectives, IReadOnlyList<RoguetripCrew> Crew)
{
    public const int MaxList = 16;

    public void Write(NetWriter w)
    {
        w.U8((byte)MsgType.RoguetripState);
        w.VarU((uint)Run); w.VarU((uint)Builder); w.Str(Build); w.Str(Phase);
        w.VarU((uint)Busts); w.VarU((uint)Won); w.VarU((uint)Kills); w.Str(Map);
        w.U8((byte)Math.Min(Legs.Count, MaxList));
        foreach (var l in Legs.Take(MaxList))
        {
            w.Str(l.Map); w.Str(l.Name); w.Str(l.Template); w.VarU((uint)l.Seed); w.F64(l.Challenge); w.VarU((uint)l.Kills); w.Str(l.Outcome); w.Str(l.Why);
        }
        w.U8((byte)Math.Min(Offers.Count, MaxList));
        foreach (var o in Offers.Take(MaxList)) { w.Str(o.Map); w.Str(o.Name); w.Str(o.Template); w.VarU((uint)o.Seed); w.F64(o.Challenge); w.Str(o.Flavour); }
        w.U8((byte)Math.Clamp(Selected, 0, 255));
        w.Bool(Job is not null);
        if (Job is { } j)
        {
            w.VarU((uint)j.Run); w.VarU((uint)j.Builder); w.VarU((uint)j.Job); w.Str(j.Map); w.Str(j.Template); w.VarU((uint)j.Seed); w.F64(j.Challenge); w.U8((byte)j.Players);
            w.U8((byte)Hash.Length);
            w.Bytes(Hash);
        }
        w.U8((byte)Math.Min(Objectives.Count, MaxList));
        foreach (var o in Objectives.Take(MaxList)) { w.Str(o.Text); w.Bool(o.Done); w.Bool(o.Failed); }
        w.U8((byte)Math.Min(Crew.Count, Protocol.MaxCoopSeats * 2));
        foreach (var c in Crew.Take(Protocol.MaxCoopSeats * 2)) { w.Str(c.Name); w.Str(c.Car); w.U8(c.Armour); w.U8(c.Chassis); w.U8(c.Ammo); w.Bool(c.Out); }
    }

    /// <summary>Reads the body (after the type byte).</summary>
    public static RoguetripState Read(ref NetReader r)
    {
        int run = (int)r.VarU(), builder = (int)r.VarU();
        string build = r.Str(), phase = r.Str();
        int busts = (int)r.VarU(), won = (int)r.VarU(), kills = (int)r.VarU();
        string map = r.Str();
        int n = Count(ref r, MaxList);
        var legs = new List<RunLeg>(n);
        for (int i = 0; i < n; i++)
            legs.Add(new RunLeg { Map = r.Str(), Name = r.Str(), Template = r.Str(), Seed = (int)r.VarU(), Challenge = r.F64(), Kills = (int)r.VarU(), Outcome = r.Str(), Why = r.Str() });
        foreach (var l in legs) l.Won = l.Outcome == "won";
        n = Count(ref r, MaxList);
        var offers = new List<RunOffer>(n);
        for (int i = 0; i < n; i++) offers.Add(new RunOffer { Map = r.Str(), Name = r.Str(), Template = r.Str(), Seed = (int)r.VarU(), Challenge = r.F64(), Flavour = r.Str() });
        int selected = r.U8();
        CoopJob? job = null;
        byte[] hash = [];
        if (r.Bool())
        {
            job = new CoopJob((int)r.VarU(), (int)r.VarU(), (int)r.VarU(), r.Str(), r.Str(), (int)r.VarU(), r.F64(), r.U8());
            int h = r.U8();
            if (h > 64) throw new WireException($"job hash of {h}");
            hash = r.Bytes(h).ToArray();
        }
        n = Count(ref r, MaxList);
        var objectives = new List<CoopObjective>(n);
        for (int i = 0; i < n; i++) objectives.Add(new CoopObjective { Text = r.Str(), Done = r.Bool(), Failed = r.Bool() });
        n = Count(ref r, Protocol.MaxCoopSeats * 2);
        var crew = new List<RoguetripCrew>(n);
        for (int i = 0; i < n; i++) crew.Add(new RoguetripCrew(r.Str(), r.Str(), r.U8(), r.U8(), r.U8(), r.Bool()));
        return new RoguetripState(run, builder, build, phase, busts, won, kills, map, legs, offers, selected, job, hash, objectives, crew);
    }

    private static int Count(ref NetReader r, int max)
    {
        int n = r.U8();
        if (n > max) throw new WireException($"roguetrip list of {n}");
        return n;
    }

    /// <summary>The state of a host's run (<paramref name="hash"/>: its build of <paramref name="job"/>, empty with none).</summary>
    public static RoguetripState Of(CoopRun c, string build, IReadOnlyList<RunOffer> offers, CoopJob? job, byte[] hash) =>
        new(c.Run.Number, c.Run.Builder, build, c.Run.Phase, c.Run.Busts, c.Run.Won, c.Run.Kills, c.Run.Map, c.Run.Legs, offers, c.Selected, job, hash,
            c.LastObjectives, [.. c.Crew.Select(x => new RoguetripCrew(x.Name, x.Car, Pct(x.State?.ArmourLeft), Pct(x.State?.ChassisLeft), Pct(x.State?.AmmoLeft), x.Out))]);

    private static byte Pct(int? v) => (byte)Math.Clamp(v ?? 100, 0, 100);

    /// <summary>This game's build of <see cref="Job"/> against the host's: null when the same, else the refusal naming both builds.</summary>
    public string? Verify(byte[] mine, string myBuild) =>
        Hash.Length > 0 && mine.AsSpan().SequenceEqual(Hash) ? null : CoopJob.Mismatch(Build, myBuild);

    /// <summary>
    /// The run as the route map and the debrief draw it on a client (RoguetripScreen reads a <see cref="RemixRun"/>): its number, phase, jobs,
    /// busts and kills, and the car of the player named <paramref name="me"/>.
    /// </summary>
    public RemixRun ToRun(string me)
    {
        var run = RemixRun.Start(Run, "", me, "Your car", -1);
        run.Builder = Builder;
        run.Phase = Phase;
        run.Busts = Busts;
        run.Won = Won;
        run.Kills = Kills;
        run.Map = Map;
        run.Legs = [.. Legs];
        if (Crew.FirstOrDefault(c => c.Name == me) is { } mine)
            run.State = new RunCar { ArmourLeft = mine.Armour, ChassisLeft = mine.Chassis, AmmoLeft = mine.Ammo };
        return run;
    }
}
