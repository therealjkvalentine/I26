using Godot;
using I76.Formats.Input;
using I76.Formats.Shell;
using I76.Net;
using I76.Server;

namespace I76.Game.Shell;

/// <summary>
/// The host form opened from a co-op game's lobby (the fork's GAME SETUP; docs/multiplayer.md "Changing the game in the lobby"): the same
/// form, art and rows as when the game was hosted, set to the game as it stands, for the host to pick another mission from the same list
/// and change the settings that can change between missions. BROADCAST GAME says APPLY, which hands the setup back to the lobby (the
/// server builds the new mission and tells everyone); CANCEL or Esc goes back with nothing changed. What cannot change while the players
/// are connected is left as it was: the game type (co-op), SIM RATE (every game's clock and input timing came from it at the connection)
/// and the car, which each player picks in the lobby itself.
/// </summary>
public sealed partial class MeleeScreen
{
    /// <summary>The game's setup when the form is opened from the lobby (null: the form hosts a new game).</summary>
    public LobbySetup? FromLobby { get; init; }
    /// <summary>The host's car in the lobby (a car file or <see cref="Protocol.MissionCar"/>), shown in the car panel.</summary>
    public string? LobbyCar { get; init; }
    /// <summary>The host's name in the lobby, shown as LOCAL DRIVER.</summary>
    public string? LobbyDriver { get; init; }
    /// <summary>APPLY's setup, or null for CANCEL.</summary>
    public Action<LobbySetup?>? SetupDone { get; init; }

    private bool LobbyMode => FromLobby is not null;
    public override bool EscapeOpensOptions => !LobbyMode;

    /// <summary>
    /// The form's hotspots a lobby's setup leaves alone: the car (3 MAKE / MODEL, 4 VARIANT, 5 CONFIGURE CHASSIS), LOCAL DRIVER's RENAME 14, the
    /// game type (32; Nitro's 23), SIM RATE 33 and the Nitro form's WEAPON RESTRICTIONS 24 (its checklist is the game's from its hosting).
    /// </summary>
    private static readonly HashSet<int> LobbyFixed = [3, 4, 5, 14, 23, 24, 32, 33];

    /// <summary>The settings as they were when the form opened (shell.cfg's, which the form's rows change as they go), put back by CANCEL.</summary>
    private (int Scale, float Damage, bool Hidden, bool CarReset, bool Cb, bool Turrets1997, bool OwnCar) _lobbyWas;

    /// <summary>The form set to the game as it stands.</summary>
    private void EnterFromLobby()
    {
        var s = FromLobby!;
        var st = Shell.Settings;
        _lobbyWas = (st.CoopScale, st.CoopDamage, st.HiddenContent, st.CarReset, st.CbRadio, st.HostTurrets1997, st.CoopOwnCar);
        if (Nitro) { _kind = Array.FindIndex(Types, t => t.Coop); _teamPlay = false; }
        else SetCoopBase(true);
        int at = CoopList.FindIndex(m => m.Msn.Equals(s.Mission, StringComparison.OrdinalIgnoreCase));
        Area = Math.Max(0, at);
        _drivers = Math.Clamp(s.MaxPlayers, 1, Protocol.MaxCoopSeats);
        _inviteOnly = s.InviteOnly;
        _password = s.Password;
        _gameName = s.Name;
        _gameRenamed = Arenas.Count > 0 && s.Name != Arenas[Area].Name;
        st.CoopScale = s.CoopScale;
        st.CoopDamage = s.CoopDamage;
        st.CarReset = s.AllowCarReset;
        st.CbRadio = s.AllowCb;
        st.HostTurrets1997 = s.Turrets1997;
        if (st.HiddenContent != s.AllowHidden) ToggleHidden();
        // the car panel shows the host's lobby car (the lobby is where it is changed)
        st.CoopOwnCar = LobbyCar is null or Protocol.MissionCar;
        if (LobbyCar is { } car && car != Protocol.MissionCar && _data.Find(car) is var (m, v)) _slots[0] = (_slots[0].Name, m, v);
        if (LobbyDriver is { Length: > 0 } who) _slots[0] = (who, _slots[0].Model, _slots[0].Variant);
        _scroll = Math.Clamp(Area - Rows + 1, 0, Math.Max(0, ListCount - Rows));
    }

    /// <summary>APPLY: the form's setup to the lobby; the mission is remembered as the host form's next co-op mission.</summary>
    private void ApplyLobby()
    {
        if (Arenas.Count == 0) { Say("There are no story missions."); return; }
        var st = Shell.Settings;
        string name = _gameName.Trim().Length > 0 ? _gameName.Trim() : Arenas[Area].Name;
        var s = FromLobby! with
        {
            Mission = Arenas[Area].Msn.ToLowerInvariant(), Name = name, MaxPlayers = Math.Clamp(_drivers, 1, Protocol.MaxCoopSeats), InviteOnly = _inviteOnly,
            AllowHidden = st.HiddenContent, AllowCarReset = st.CarReset, AllowCb = st.CbRadio, CoopScale = st.CoopScale, CoopDamage = st.CoopDamage,
            Turrets1997 = st.HostTurrets1997, Password = _password,
        };
        st.CoopOwnCar = _lobbyWas.OwnCar;
        st.Save();
        _setup.CoopArena = s.Mission;
        _setup.CoopHost = true;
        _setup.Save();
        SetupDone?.Invoke(s);
    }

    /// <summary>CANCEL (or Esc): shell.cfg's settings as they were, and back to the lobby.</summary>
    private void CancelLobby()
    {
        var st = Shell.Settings;
        if (st.HiddenContent != _lobbyWas.Hidden) ToggleHidden();
        (st.CoopScale, st.CoopDamage, st.CarReset, st.CbRadio, st.HostTurrets1997, st.CoopOwnCar) = (_lobbyWas.Scale, _lobbyWas.Damage, _lobbyWas.CarReset, _lobbyWas.Cb, _lobbyWas.Turrets1997, _lobbyWas.OwnCar);
        st.Save();
        SetupDone?.Invoke(null);
    }

    /// <summary>SIM RATE's tooltip on a lobby's form: why it is not offered.</summary>
    private (string Text, Rect2I Zone)? LobbyTipAt(Vector2I p)
    {
        if (!LobbyMode || Nitro || !HostFormLayout.Hot(true).TryGetValue(33, out var r) || !(r.X0 < p.X && p.X < r.X1 && r.Y0 < p.Y && p.Y < r.Y1)) return null;
        return ("Set when the game was hosted: every player's game runs its clock at this rate, so it stays until the game is hosted again.", new Rect2I(r.X0, r.Y0, r.X1 - r.X0, r.Y1 - r.Y0));
    }

    private bool _applyBaked;
    private (Rect2I Border, ArtButtons.ArtLabel Cancel, int Plain)? _applyArt;
    private bool _applyRead;

    /// <summary>
    /// BROADCAST GAME says APPLY: its word covered with a plain column of its own face stretched across (a change to the background, so an
    /// upscaled background is changed the same way), then APPLY lettered as the art letters CANCEL beside it (the lobby's START is the same).
    /// </summary>
    private void DrawApplyButton()
    {
        if (!_applyRead)
        {
            _applyRead = true;
            var bg = Shell.Db.Background(Frame.Record);
            byte Px(int x, int y) => bg.Pixels[Math.Clamp(y, 0, bg.Height - 1) * bg.Width + Math.Clamp(x, 0, bg.Width - 1)];
            Rect2I Around((int X0, int X1, int Y0, int Y1) h) => new(h.X0 - 4, h.Y0 - 4, h.X1 - h.X0 + 8, h.Y1 - h.Y0 + 8);
            // CANCEL's word as the join form's art (record 18) has it, where the lobby's START is measured: the same button at the same place
            var roster = Shell.Db.Background(18);
            byte Rx(int x, int y) => roster.Pixels[Math.Clamp(y, 0, roster.Height - 1) * roster.Width + Math.Clamp(x, 0, roster.Width - 1)];
            if (ArtButtons.Label(Px, bg.Palette, Around(Rect(6))) is { } join && ArtButtons.Label(Px, bg.Palette, join.Box.Grow(-3)) is { } word
                && ArtButtons.Label(Rx, roster.Palette, Around(Rect(15))) is { } cancel && ArtButtons.Label(Rx, roster.Palette, cancel.Box.Grow(-3)) is { } cw)
            {
                // (its inks are the roster's palette's: the nearest of this form's)
                Color Of(byte i) => Color.Color8(roster.Palette[i * 3], roster.Palette[i * 3 + 1], roster.Palette[i * 3 + 2]);
                _applyArt = (join.Box, cw with { Ink = Frame.Nearest(Of(cw.Ink)), Soft = Frame.Nearest(Of(cw.Soft)) }, word.Box.Position.X - 2);
            }
        }
        if (_applyArt is not var (border, like, plain))
        {
            var f = Shell.Db.Font(0x47);
            Frame.Text(f, 299 - f.Measure("APPLY") / 2, 457, "APPLY", ShellFrame.DefaultInk);
            return;
        }
        if (!_applyBaked)
        {
            _applyBaked = true;
            const int edge = 4;
            int y0 = border.Position.Y + 2, h = border.Size.Y - 4;
            for (int x = border.Position.X + edge; x < border.End.X - edge; x++) Frame.CopyBackground(plain, y0, 1, h, x, y0);
        }
        ArtButtons.Letter(Frame, Shell.Db.Font(0x47), "APPLY", like, "CANCEL", border);
    }
}
