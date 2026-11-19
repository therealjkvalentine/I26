using Godot;
using I76.Net;
using I76.Server;

namespace I76.Game.Shell;

/// <summary>
/// The host form opened from the game itself (the fork's in-game GAME SETUP; docs/multiplayer.md "Changing the game for the next round"):
/// the listen host's Esc pad has Game Setup, which hosts this form over the running game, set to the melee as it stands. The host changes
/// what the next round can take (<see cref="RoundSetup"/>: the arena of the same game type, the name, seats, invite rule, password, the limit,
/// the bots, Share Maps and the host's rules) and APPLY hands it to the server, which builds the next round from it and tells everyone now.
/// What cannot change while the players are in (the game type, team play, the sim rate, the car rules, the car) is drawn faded, its clicks
/// do nothing, and its tooltip says why. CANCEL or Esc goes back with nothing changed.
/// </summary>
public sealed partial class MeleeScreen
{
    /// <summary>The game as it is and as its next round will be (null: the form hosts a new game, or is a lobby's).</summary>
    public (ServerOptions Now, RoundSetup Next)? FromGame { get; init; }
    /// <summary>APPLY's setup for the next round, or null for CANCEL.</summary>
    public Action<RoundSetup?>? RoundDone { get; init; }

    private bool GameMode => FromGame is not null;

    /// <summary>
    /// The hotspots the game's setup leaves alone (MeleeScreen's ids): the car (3, 4, 5), LOCAL DRIVER's RENAME 14, TEAM PLAY 16, the game type
    /// (32; the Nitro form's 23), SIM RATE 33, UNLIMITED TURRETS 35, HIDDEN CARS 28 and WEAPON RESTRICTIONS 24.
    /// </summary>
    private static readonly HashSet<int> GameFixed = [3, 4, 5, 14, 16, 23, 24, 28, 32, 33, 35];

    /// <summary>Why a fixed row cannot change in the game (its tooltip), by hotspot id.</summary>
    internal static string? GameFixedWhy(int id) => id switch
    {
        3 or 4 or 5 => "Your car: change it with Reconfigure while your car is dead.",
        14 => "Your name is the one you joined with.",
        16 => "Teams are picked as each player joins and carried round to round: host the game again to change Team Play.",
        23 or 32 => "The game type is set when the game is hosted: host it again for another type. The arena list has this type's arenas.",
        33 => "Set when the game was hosted: every player's game runs its clock at this rate, so it stays until the game is hosted again.",
        35 or 28 or 24 => "The cars allowed are checked as each player joins, and carried round to round: host the game again to change the car rules.",
        _ => null,
    };

    /// <summary>The settings as they were when the form opened (shell.cfg's, which the form's rows change as they go), put back by CANCEL.</summary>
    private (bool Hidden, bool CarReset, bool Cb, bool Ignition, bool Crosshair, bool Turrets1997, I76.Sim.Missions.HandicapMode Handicaps, bool ShareMaps)? _gameWas;

    /// <summary>The form set to the game: the next round's setup, and the game's fixed rules as they stand.</summary>
    private void EnterFromGame()
    {
        var (o, s) = FromGame!.Value;
        var st = Shell.Settings;
        _gameWas = (st.HiddenContent, st.CarReset, st.CbRadio, st.IgnitionStaysOn, st.HostCrosshair, st.HostTurrets1997, st.Handicaps, st.ShareMaps);
        string ext = Path.GetExtension(s.Arena).ToLowerInvariant();
        if (Nitro)
        {
            int k = Array.FindIndex(Types, t => !t.Coop && t.Kind == o.Kind && (t.Ext == ext || (ext == ".lvl" && t.Ext == ".cbt")));
            _kind = Math.Max(0, k);
        }
        else SetCoopBase(false);
        // the arena: the install's own, or the download it is played from
        string? dir = s.MapDir is { } d ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)) : null;
        int at = -1;
        for (int i = 0; i < Arenas.Count && at < 0; i++)
        {
            string msn = Arenas[i].Msn;
            string? md = DownloadedArenas.DirOf(msn) is { } m ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(m)) : null;
            if (DownloadedArenas.FileOf(msn).Equals(s.Arena, StringComparison.OrdinalIgnoreCase) && string.Equals(md, dir, StringComparison.OrdinalIgnoreCase)) at = i;
        }
        Area = Math.Max(0, at);
        _gameName = s.Name;
        _gameRenamed = Arenas.Count > 0 && s.Name != Arenas[Area].Name;
        _drivers = Math.Clamp(s.MaxPlayers, 1, MaxDrivers);
        _inviteOnly = s.InviteOnly;
        _password = s.Password;
        _teamPlay = o.TeamPlay;
        _rate = o.Rate.Label;
        SetTurrets(o.AllowTurrets);
        _restrictions = o.Restrictions;
        _bots = s.Bots;
        _botSkill = s.BotSkill;
        _botSkillDifficulty = false;
        _botCars = [.. s.BotChoices];
        if (Nitro)
        {
            var limits = LimitsOf(Types[_kind].Kind);
            int li = s.TimeLimit > 0 ? limits.ToList().FindIndex(l => l.Clock)
                : s.GoalValue > 0 ? limits.ToList().FindIndex(l => !l.Clock && l.Goal == s.Goal && l.Values.Contains(s.GoalValue))
                : limits.ToList().FindIndex(l => l.Values is [0]);
            if (li < 0) li = 0;
            int v = s.TimeLimit > 0 ? (int)Math.Round(s.TimeLimit / 60) : s.GoalValue;
            (_limit, _limitAt) = (li, Math.Max(0, Array.IndexOf(limits[li].Values, v)));
        }
        else
        {
            _scoreOn = s.MaxScore > 0;
            if (s.MaxScore > 0) _maxScore = HostRows.ScoreFrom(s.MaxScore);
        }
        (st.CarReset, st.CbRadio, st.IgnitionStaysOn, st.HostCrosshair, st.HostTurrets1997, st.Handicaps, st.ShareMaps)
            = (s.AllowCarReset, s.AllowCb, s.IgnitionStaysOn, s.AllowCrosshair, s.Turrets1997, s.Handicaps, s.ShareMaps);
        // HIDDEN CARS as the game has it (shown, not changed)
        if (st.HiddenContent != o.AllowHidden) ToggleHidden();
        _scroll = Math.Clamp(Area - Rows + 1, 0, Math.Max(0, ListCount - Rows));
    }

    /// <summary>The form's setup for the next round.</summary>
    private RoundSetup GameSetupOfForm()
    {
        var st = Shell.Settings;
        string msn = Arenas[Math.Clamp(Area, 0, Arenas.Count - 1)].Msn;
        string name = _gameName.Trim().Length > 0 ? _gameName.Trim() : Arenas[Area].Name;
        int bots = Math.Min(_bots, MaxBots);
        var (goal, value, minutes) = NitroGoal;
        return FromGame!.Value.Next with
        {
            Arena = DownloadedArenas.FileOf(msn), MapDir = DownloadedArenas.DirOf(msn), Name = name, MaxPlayers = Math.Clamp(_drivers, 1, MaxDrivers),
            InviteOnly = _inviteOnly, Password = _password,
            // (the base game's form has MAX SCORE only: a goal or time limit the game was hosted with stays as it is)
            MaxScore = !Nitro && _scoreOn ? _maxScore : 0,
            Goal = Nitro ? goal : FromGame!.Value.Next.Goal, GoalValue = Nitro ? value : FromGame!.Value.Next.GoalValue, TimeLimit = Nitro ? minutes * 60f : FromGame!.Value.Next.TimeLimit,
            Bots = bots, BotSkill = BotSkillPlayed, BotChoices = [.. _botCars.Take(bots)],
            ShareMaps = st.ShareMaps, AllowCarReset = st.CarReset, AllowCb = st.CbRadio, IgnitionStaysOn = st.IgnitionStaysOn, AllowCrosshair = st.HostCrosshair,
            Turrets1997 = st.HostTurrets1997, Handicaps = st.Handicaps,
        };
    }

    /// <summary>APPLY: the setup to the server for the next round; the host's rules stay as set (shell.cfg), HIDDEN CARS as it was, and the type's rows are kept (HostRows).</summary>
    private void ApplyGame()
    {
        if (Arenas.Count == 0) { Say("There are no arenas of that kind."); return; }
        var s = GameSetupOfForm();
        var st = Shell.Settings;
        if (_gameWas is { } w && st.HiddenContent != w.Hidden) ToggleHidden();
        st.Save();
        _setup.Host(HostTypeNow).Merge(FormRows(HostTypeNow));
        _setup.Save();
        RoundDone?.Invoke(s);
    }

    /// <summary>CANCEL (or Esc): shell.cfg's settings as they were, and back to the game.</summary>
    private void CancelGame()
    {
        var st = Shell.Settings;
        if (_gameWas is { } w)
        {
            if (st.HiddenContent != w.Hidden) ToggleHidden();
            (st.CarReset, st.CbRadio, st.IgnitionStaysOn, st.HostCrosshair, st.HostTurrets1997, st.Handicaps, st.ShareMaps) = (w.CarReset, w.Cb, w.Ignition, w.Crosshair, w.Turrets1997, w.Handicaps, w.ShareMaps);
            st.Save();
        }
        RoundDone?.Invoke(null);
    }

    /// <summary>A fixed row's tooltip on the game's form: why it does not change now.</summary>
    private (string Text, Rect2I Zone)? GameTipAt(Vector2I p)
    {
        if (!GameMode) return null;
        foreach (int id in GameFixed)
        {
            // (the ids this form has: the base form's layout, or the Nitro form's table and the car panel's)
            if (BaseHost ? !HostFormLayout.Hot(_coopBase).ContainsKey(id) : !NitroHost.ContainsKey(id) && id is not (3 or 4 or 5 or 14)) continue;
            var r = Rect(id);
            if (r.X0 < p.X && p.X < r.X1 && r.Y0 < p.Y && p.Y < r.Y1 && GameFixedWhy(id) is { } why)
                return (why, new Rect2I(r.X0, r.Y0, r.X1 - r.X0, r.Y1 - r.Y0));
        }
        return null;
    }
}
