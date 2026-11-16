using System.Globalization;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Net;
using I76.Sim.Missions;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// What a co-op game's host may change in its lobby, between missions (the I26 play fork's; docs/multiplayer.md "Changing the game in the
/// lobby"): the mission the start plays and the settings of the host form a story mission has a use for. The Sim Rate is not among them
/// (every game's clock and input timing were set from it at the connection), nor Rolling Start (no row anywhere), nor the WEAPON
/// RESTRICTIONS of the Nitro form (a checklist of their own).
/// </summary>
public sealed record LobbySetup(string Mission, string Name, int MaxPlayers, bool InviteOnly, bool AllowHidden, bool AllowCarReset, bool AllowCb,
    int CoopScale, float CoopDamage, bool Turrets1997, string Password)
{
    /// <summary>The setup of a game as its options have it.</summary>
    public static LobbySetup Of(ServerOptions o) => new(o.Coop ?? o.Arena, o.Name, o.MaxPlayers, o.InviteOnly, o.AllowHidden, o.AllowCarReset, o.AllowCb,
        o.CoopScale, o.CoopDamage, o.Turrets1997, o.Password);

    /// <summary>The options with this setup in them (the rest, the seed too, as they were).</summary>
    public ServerOptions Into(ServerOptions o) => o with
    {
        Arena = Mission, Coop = Mission, Name = Name, MaxPlayers = MaxPlayers, InviteOnly = InviteOnly, AllowHidden = AllowHidden, AllowCarReset = AllowCarReset,
        AllowCb = AllowCb, CoopScale = CoopScale, CoopDamage = CoopDamage, Turrets1997 = Turrets1997, Password = Password,
    };

    /// <summary>Without the password (a record prints every member).</summary>
    public override string ToString() => $"LobbySetup {{ Mission = {Mission}, Name = {Name}, MaxPlayers = {MaxPlayers}, Locked = {Password.Length > 0} }}";

    /// <summary>Extra Enemies in the host form's words: Off, Up to +1, Up to +2, and at the cap "+1/player".</summary>
    public static string ScaleWord(int n) => n <= 0 ? "Off" : n >= CoopSetup.MaxExtraEnemies ? "+1/player" : $"Up to +{n}";

    public static string FactorWord(float f) => $"x{f.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The lines the players are told of a change from <paramref name="was"/> to <paramref name="now"/>, one a setting, in the host form's words
    /// (the mission by the title <paramref name="title"/> gives it).
    /// </summary>
    public static List<string> Lines(LobbySetup was, LobbySetup now, Func<string, string> title)
    {
        var l = new List<string>();
        static string OnOff(bool on) => on ? "on" : "off";
        if (!was.Mission.Equals(now.Mission, StringComparison.OrdinalIgnoreCase)) l.Add($"The host changed the mission: {title(now.Mission)}");
        // (a name that only follows the mission, as the host form's does until renamed, is said by the mission's line)
        if (was.Name != now.Name && now.Name != title(now.Mission)) l.Add($"Game name: {now.Name}");
        if (was.MaxPlayers != now.MaxPlayers) l.Add($"No. drivers: {now.MaxPlayers} (was {was.MaxPlayers})");
        if (was.CoopScale != now.CoopScale) l.Add($"Extra enemies: {ScaleWord(now.CoopScale)} (was {ScaleWord(was.CoopScale)})");
        if (was.CoopDamage != now.CoopDamage) l.Add($"Damage taken: {FactorWord(now.CoopDamage)} (was {FactorWord(was.CoopDamage)})");
        if (was.InviteOnly != now.InviteOnly) l.Add($"Invite only: {OnOff(now.InviteOnly)}");
        if (was.AllowHidden != now.AllowHidden) l.Add($"Hidden cars: {(now.AllowHidden ? "allowed" : "not allowed")}");
        if (was.AllowCarReset != now.AllowCarReset) l.Add($"Car reset: {OnOff(now.AllowCarReset)}");
        if (was.AllowCb != now.AllowCb) l.Add($"CB radio: {OnOff(now.AllowCb)}");
        if (was.Turrets1997 != now.Turrets1997) l.Add($"Turrets: {(now.Turrets1997 ? "1997 CD" : "1998")}");
        if (was.Password != now.Password) l.Add(now.Password.Length == 0 ? "Password: none" : was.Password.Length == 0 ? "Password: set" : "Password: changed");
        return l;
    }
}

public sealed partial class NetMeleeServer
{
    /// <summary>A change of the lobby's setup is being built (one at a time).</summary>
    private bool _changing;

    /// <summary>The co-op game's setup as it stands (null for a melee).</summary>
    public LobbySetup? Setup { get { lock (_lock) return Options.Coop is null ? null : LobbySetup.Of(Options); } }

    /// <summary>
    /// The host changes the game in its lobby (a listen host's GAME SETUP, the console's <c>mission</c> and <c>set</c>): the mission the start
    /// plays and its settings. Only between missions: refused once the mission has started (it is changed from the lobby the game goes back to).
    /// A new mission or setting builds the game again from the new options, off the server's tick (the old one stands meanwhile; nobody is seated
    /// in a lobby, so nothing is lost); then everyone in the lobby is sent the lobby (its mission line and seats, in the message upstream's
    /// lobby reads too) and a line for each change in its chat, and the listing follows. A player whose car the new rules refuse (Hidden Cars
    /// off) is put in the mission's own car, its ready mark off, and told why as a refused car is (<see cref="ReconfigureAnswer"/>).
    /// Null when done (or nothing changed), else why not. Blocks while the mission builds.
    /// </summary>
    public string? ChangeLobby(LobbySetup want)
    {
        ServerOptions old, next;
        lock (_lock)
        {
            if (Options.Coop is null || !Options.Lobby) return "not a co-op game with a lobby";
            if (!_lobbyOpen) return _lobbyClosed ? "the lobby is closed" : "the mission has started: change it in the lobby (Back to Lobby)";
            if (_changing) return "a change is already being made";
            if (Check(want) is { } bad) return bad;
            old = Options;
            if (LobbySetup.Of(old) == want with { Mission = want.Mission.ToLowerInvariant() }) return null;
            next = want.Into(old) with { Arena = want.Mission.ToLowerInvariant(), Coop = want.Mission.ToLowerInvariant() };
            _changing = true;
        }
        try
        {
            MeleeHost built;
            try { built = new MeleeHost(next); }
            catch (Exception ex)
            {
                Log?.Invoke($"lobby: {next.Coop} could not be built ({ex.Message}): the game stays as it was");
                return $"{next.Coop} could not be built ({ex.Message})";
            }
            lock (_lock)
            {
                if (!_lobbyOpen) return "the mission started meanwhile";
                if (_lobby.Count > next.MaxPlayers) return $"{_lobby.Count} players are in the lobby";
                var install = new GameInstall(next.Install);
                var lines = LobbySetup.Lines(LobbySetup.Of(old), LobbySetup.Of(next), m => MissionTitles.CoopTitle(install, m));
                Host.Log -= _hostLog;
                Host = built;
                built.Log += _hostLog;
                Log?.Invoke($"lobby: the host changed the game: {string.Join("; ", lines)}");
                foreach (var line in lines) LobbySay("Server", line);
                RecheckLobbyCars();
                SendLobby();
            }
            return null;
        }
        finally { lock (_lock) _changing = false; }
    }

    /// <summary>Why a setup cannot be played here, or null.</summary>
    private string? Check(LobbySetup s)
    {
        var install = new GameInstall(Options.Install);
        if (!MissionTitles.Coop(install).Any(m => m.Stem.Equals(s.Mission, StringComparison.OrdinalIgnoreCase))) return $"{s.Mission} is not a story mission of this install";
        if (s.MaxPlayers is < 1 or > Protocol.MaxCoopSeats) return $"the seats are 1 to {Protocol.MaxCoopSeats}";
        if (s.MaxPlayers < _lobby.Count) return $"{_lobby.Count} players are in the lobby";
        if (s.CoopScale is < 0 or > CoopSetup.MaxExtraEnemies) return $"extra enemies are 0 to {CoopSetup.MaxExtraEnemies}";
        if (!CoopSetup.DamageFactors.Contains(s.CoopDamage)) return $"damage taken is one of {string.Join(", ", CoopSetup.DamageFactors.Select(LobbySetup.FactorWord))}";
        if (s.Name.Trim().Length == 0 || s.Name.Length > 64) return "the game needs a name";
        if (s.Password.Length > Protocol.MaxPassword) return $"the password is at most {Protocol.MaxPassword} characters";
        return null;
    }

    /// <summary>
    /// The lobby's cars against the game's rules as they are now (the join's car check): a car they refuse is taken off its player, who is in the
    /// mission's own car until they pick another, and is told as a refused car is; the others hear it in the chat.
    /// </summary>
    private void RecheckLobbyCars()
    {
        foreach (var m in _lobby)
        {
            if (m.Request.Car == Protocol.MissionCar) continue;
            if (Host.CheckJoin(m.Request.Car, m.Request.Config.Length > 0 ? m.Request.Config : null) is not { } why) continue;
            Log?.Invoke($"lobby: {m.Request.Name}'s car {m.Request.Car} is not allowed now: {why}");
            m.Request = m.Request with { Car = Protocol.MissionCar, Config = [], Paint = null };
            m.Ready = false;
            _msg.Clear();
            // (the words first: a line is cut at 255 bytes, and the car check's reason can be long)
            new ReconfigureAnswer(false, $"the host's new rules: you are in the mission's own car. {why}").Write(_msg);
            m.Peer.Send(_msg.Span, DeliveryMethod.ReliableOrdered);
            LobbySay("Server", $"{m.Request.Name}'s car is not allowed now: the mission's own car until another is picked");
        }
    }

    /// <summary>
    /// The console's change of one thing (i26-server's <c>mission M</c> and <c>set KEY VALUE</c>): mission, name, drivers, extra-enemies (0-3),
    /// damage (0.5, 0.75, 1, 1.5), invite, hidden, car-reset, cb, turrets (1997 or 1998), password (empty: none). Null when done, else why not.
    /// A game named after its mission (not renamed) takes the new mission's name.
    /// </summary>
    public string? SetLobby(string key, string value)
    {
        if (Setup is not { } s) return "not a co-op game";
        value = value.Trim();
        bool? On() => value.ToLowerInvariant() switch { "on" or "yes" or "1" or "true" => true, "off" or "no" or "0" or "false" => false, _ => null };
        LobbySetup? want;
        switch (key.Trim().ToLowerInvariant())
        {
            case "mission":
            {
                var install = new GameInstall(Options.Install);
                bool named = s.Name == MissionTitles.CoopTitle(install, s.Mission) || s.Name == $"I76 co-op {s.Mission}";
                want = s with { Mission = value.ToLowerInvariant(), Name = named ? MissionTitles.CoopTitle(install, value) : s.Name };
                break;
            }
            case "name": want = s with { Name = value }; break;
            case "drivers" or "max" or "seats": want = int.TryParse(value, out int n) ? s with { MaxPlayers = n } : null; break;
            case "extra-enemies" or "coop-scale": want = int.TryParse(value, out int e) ? s with { CoopScale = e } : null; break;
            case "damage" or "coop-damage": want = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? s with { CoopDamage = f } : null; break;
            case "invite": want = On() is { } i ? s with { InviteOnly = i } : null; break;
            case "hidden": want = On() is { } h ? s with { AllowHidden = h } : null; break;
            case "car-reset": want = On() is { } r ? s with { AllowCarReset = r } : null; break;
            case "cb": want = On() is { } c ? s with { AllowCb = c } : null; break;
            case "turrets": want = value is "1997" or "1998" ? s with { Turrets1997 = value == "1997" } : null; break;
            case "password": want = s with { Password = value }; break;
            default: return $"no setting {key} (mission, name, drivers, extra-enemies, damage, invite, hidden, car-reset, cb, turrets, password)";
        }
        return want is null ? $"{key}: {value} is no value for it" : ChangeLobby(want);
    }
}
