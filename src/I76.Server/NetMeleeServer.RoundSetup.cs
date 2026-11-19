using I76.Formats.Vfs;
using I76.Net;
using I76.Sim.Missions;

namespace I76.Server;

/// <summary>
/// What a melee's host may change for the next round while the game runs (the I26 play fork's; docs/multiplayer.md "Changing the game
/// for the next round"): the settings that the next round's game is built from and that every player reads again from the round's
/// accept, or that only the server uses. The round in play is not touched. Left out, as the host form shows them greyed: the game type
/// and team play (the kind, the CTF teams and each player's team are taken at the join and carried), the sim rate (every game's clock
/// and input timing were set from it at the connection), and the car rules (Hidden Weapons &amp; Cars, Unlim. Turrets, the weapon
/// restrictions: the players carried into the next round keep the cars their join was checked with).
/// </summary>
public sealed record RoundSetup(string Arena, string? MapDir, string Name, int MaxPlayers, bool InviteOnly, string Password,
    int MaxScore, MeleeGoal Goal, int GoalValue, float TimeLimit, int Bots, BotSkill BotSkill, IReadOnlyList<string> BotChoices,
    bool ShareMaps, bool AllowCarReset, bool AllowCb, bool IgnitionStaysOn, bool AllowCrosshair, bool Turrets1997, HandicapMode Handicaps)
{
    /// <summary>The setup a game's options have.</summary>
    public static RoundSetup Of(ServerOptions o) => new(o.Arena, o.MapDir, o.Name, o.MaxPlayers, o.InviteOnly, o.Password,
        o.MaxScore, o.Goal, o.GoalValue, o.TimeLimit, o.Bots, o.BotSkill, o.BotChoices,
        o.ShareMaps, o.AllowCarReset, o.AllowCb, o.IgnitionStaysOn, o.AllowCrosshair, o.Turrets1997, o.Handicaps);

    /// <summary>The options with this setup in them (the rest, the kind, teams, rate and car rules, as they were).</summary>
    public ServerOptions Into(ServerOptions o) => o with
    {
        Arena = Arena, MapDir = MapDir, Name = Name, MaxPlayers = MaxPlayers, InviteOnly = InviteOnly, Password = Password,
        MaxScore = MaxScore, Goal = Goal, GoalValue = GoalValue, TimeLimit = TimeLimit, Bots = Bots, BotSkill = BotSkill, BotChoices = BotChoices,
        ShareMaps = ShareMaps, AllowCarReset = AllowCarReset, AllowCb = AllowCb, IgnitionStaysOn = IgnitionStaysOn, AllowCrosshair = AllowCrosshair,
        Turrets1997 = Turrets1997, Handicaps = Handicaps,
    };

    /// <summary>Without the password (a record prints every member).</summary>
    public override string ToString() => $"RoundSetup {{ Arena = {Arena}, Name = {Name}, MaxPlayers = {MaxPlayers}, Locked = {Password.Length > 0} }}";

    /// <summary>What ends the game, in the host form's words: the score or goal, the clock, either that comes first, or no limit.</summary>
    public static string LimitWord(int maxScore, MeleeGoal goal, int value, float seconds)
    {
        var l = new List<string>();
        if (value > 0) l.Add(goal switch { MeleeGoal.Kills => $"{value} kills", MeleeGoal.Laps => $"{value} laps", MeleeGoal.Captures => $"{value} captures", _ => $"{value} points" });
        else if (maxScore > 0) l.Add($"{maxScore} points");
        if (seconds > 0) l.Add(seconds % 60 == 0 ? $"{seconds / 60:0} minutes" : $"{seconds:0} s");
        return l.Count == 0 ? "no limit" : string.Join(" or ", l);
    }

    /// <summary>
    /// The lines the players are told of a change from <paramref name="was"/> to <paramref name="now"/>, one a setting, "Next round: ..." in the
    /// host form's words (the arena by the name <paramref name="title"/> gives it).
    /// </summary>
    public static List<string> Lines(RoundSetup was, RoundSetup now, Func<string, string> title)
    {
        var l = new List<string>();
        static string OnOff(bool on) => on ? "on" : "off";
        void Say(string what) => l.Add($"Next round: {what}");
        if (!was.Arena.Equals(now.Arena, StringComparison.OrdinalIgnoreCase) || was.MapDir != now.MapDir) Say($"the arena is {title(now.Arena)}");
        if (was.Name != now.Name && now.Name != title(now.Arena)) Say($"game name {now.Name}");
        if (was.MaxPlayers != now.MaxPlayers) Say($"no. drivers {now.MaxPlayers} (was {was.MaxPlayers})");
        string lw = LimitWord(was.MaxScore, was.Goal, was.GoalValue, was.TimeLimit), ln = LimitWord(now.MaxScore, now.Goal, now.GoalValue, now.TimeLimit);
        if (lw != ln) Say($"the limit is {ln} (was {lw})");
        if (was.Bots != now.Bots) Say($"bots {now.Bots} (was {was.Bots})");
        if (was.BotSkill != now.BotSkill) Say($"bot skill {now.BotSkill} (was {was.BotSkill})");
        if (!was.BotChoices.SequenceEqual(now.BotChoices)) Say("the bots' cars changed");
        if (was.InviteOnly != now.InviteOnly) Say($"invite only {OnOff(now.InviteOnly)}");
        if (was.ShareMaps != now.ShareMaps) Say($"share maps {OnOff(now.ShareMaps)}");
        if (was.AllowCarReset != now.AllowCarReset) Say($"car reset {OnOff(now.AllowCarReset)}");
        if (was.AllowCb != now.AllowCb) Say($"CB radio {OnOff(now.AllowCb)}");
        if (was.IgnitionStaysOn != now.IgnitionStaysOn) Say($"ignition lock {OnOff(now.IgnitionStaysOn)}");
        if (was.AllowCrosshair != now.AllowCrosshair) Say($"chase crosshair {OnOff(now.AllowCrosshair)}");
        if (was.Turrets1997 != now.Turrets1997) Say($"turrets {(now.Turrets1997 ? "1997 CD" : "1998")}");
        if (was.Handicaps != now.Handicaps) Say($"handicaps {now.Handicaps.ToString().ToLowerInvariant()}");
        if (was.Password != now.Password) Say(now.Password.Length == 0 ? "no password" : was.Password.Length == 0 ? "password set" : "password changed");
        return l;
    }
}

public sealed partial class NetMeleeServer
{
    private RoundSetup? _nextRound;

    /// <summary>The setup the next round will be built from (a change the host made, else the game's as it is); null for a game without rounds (co-op, --once).</summary>
    public RoundSetup? NextRoundSetup { get { lock (_lock) return CarriesOn ? _nextRound ?? RoundSetup.Of(Options) : null; } }

    /// <summary>Whether the host has changed the next round (the change is waiting for it).</summary>
    public bool NextRoundChanged { get { lock (_lock) return _nextRound is not null; } }

    /// <summary>
    /// The host changes the next round (a listen host's in-game GAME SETUP; the play fork's): the change waits for the round in play to end,
    /// and <see cref="StartNextRound"/> builds the next one from it (the rotation, if any, goes on from its arena). Everyone in the game is told
    /// at once, a chat line a setting ("Next round: ..."), which any build shows. Nothing new goes on the wire: the next round's accept carries
    /// the limits and the host's rules as every round's does, and the bots, seats, password and invite rule are the server's own. Only a melee
    /// that carries on; the arena must be of the game's kind (and a capture the flag arena of its team count) and on this install (or in its
    /// downloaded folder); the seats at least the players in the game. Null when done (or nothing changed), else why not.
    /// </summary>
    public string? SetNextRound(RoundSetup want)
    {
        lock (_lock)
        {
            if (!CarriesOn) return Options.Coop is not null ? "a co-op game has no rounds: change it in the lobby" : "this game has no next round";
            if (CheckRound(want) is { } bad) return bad;
            var was = _nextRound ?? RoundSetup.Of(Options);
            var install = new GameInstall(Options.Install) { MapDir = want.MapDir };
            var lines = RoundSetup.Lines(was, want, a => I76.Formats.Mission.MissionTitles.ArenaTitle(install, a) is { Length: > 0 } t ? t : a);
            if (lines.Count == 0) return null;
            _nextRound = want;
            Log?.Invoke($"next round: the host changed the game: {string.Join("; ", lines)}");
            foreach (var line in lines) Host.Chat(null, line);
            return null;
        }
    }

    /// <summary>Why a setup cannot be the next round here, or null.</summary>
    private string? CheckRound(RoundSetup s)
    {
        var install = new GameInstall(Options.Install) { MapDir = s.MapDir };
        if (s.MapDir is { } dir && !Directory.Exists(dir)) return $"{dir} is gone";
        if (install.FindMission(s.Arena) is null) return $"{s.Arena} is not an arena of this install";
        var (kind, teams) = ServerOptions.KindOf(s.Arena);
        var (nowKind, nowTeams) = ServerOptions.KindOf(Options.Arena);
        if (kind != nowKind || (kind == MeleeKind.Ctf && teams != nowTeams)) return $"{s.Arena} is another kind of game: host it again for that";
        if (s.MaxPlayers is < 1 or > Protocol.MaxSeats) return $"the seats are 1 to {Protocol.MaxSeats}";
        if (s.MaxPlayers < Host.Seats.Count) return $"{Host.Seats.Count} players are in the game";
        if (s.Bots is < 0 || s.Bots > Math.Max(0, s.MaxPlayers - 1)) return "the bots are 0 to the seats less the host's own";
        if (s.MaxScore < 0 || s.GoalValue < 0 || s.TimeLimit < 0) return "a limit is not below 0";
        if (s.Name.Trim().Length == 0 || s.Name.Length > 64) return "the game needs a name";
        if (s.Password.Length > Protocol.MaxPassword) return $"the password is at most {Protocol.MaxPassword} characters";
        return null;
    }

    /// <summary>The options the next round is built from: the round in play's, with the host's change if there is one (taken: the round after starts from it).</summary>
    private ServerOptions NextRoundBase(ServerOptions old)
    {
        if (_nextRound is not { } n) return old;
        _nextRound = null;
        return n.Into(old);
    }
}
