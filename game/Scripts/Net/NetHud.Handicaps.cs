using Godot;
using I76.Net.Hud;
using I76.Sim.Missions;

namespace I76.Game.Net;

/// <summary>
/// The listen host's handicaps (the port's; docs/multiplayer.md "Handicaps"): with the host's rule on and the score window up,
/// Shift+1..9 picks the n-th numbered row (the rows BOOT_n numbers), Shift+[ lowers and Shift+] raises that player's level,
/// −3..+3, through <see cref="I76.Server.NetMeleeServer.SetHandicap"/>. Everyone sees the level by the name on the scoreboard.
/// </summary>
public partial class NetHud
{
    /// <summary>The slot whose handicap the host's keys step, −1 before one is picked.</summary>
    private int _handicapPick = -1;
    /// <summary>The score window was up in the last frame (the host's handicap keys only act on what it shows).</summary>
    private bool _boardShown;
    private bool _handicapHintSaid;

    /// <summary>The host's rule is on in this game and this machine runs it.</summary>
    private bool HostHandicaps => Server is { Options.Handicaps: true };

    /// <summary>A Shift key of the host's handicaps; returns whether it acted.</summary>
    private bool HandicapKey(InputEventKey k)
    {
        if (k.Echo || !k.ShiftPressed || k.CtrlPressed || k.AltPressed || !HostHandicaps || !_boardShown || Server is not { } server) return false;
        if (k.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            int n = (int)(k.Keycode - Key.Key1) + 1;
            var numbered = PlayersPage()?.Numbered ?? [];
            if (n <= numbered.Count) _handicapPick = numbered[n - 1].Slot;
            return true;
        }
        if (k.Keycode is not (Key.Bracketleft or Key.Bracketright)) return false;
        if (_handicapPick < 0 || Client.Scores?.Players.Any(p => p.Slot == _handicapPick) != true)
        {
            _handicapPick = -1;
            Say("Handicaps: Shift+1..9 picks a player first", FeedKind.Own);
            return true;
        }
        int level = server.HandicapOf(_handicapPick) + (k.Keycode == Key.Bracketright ? 1 : -1);
        if (server.SetHandicap(_handicapPick, level) is { } why) Say(why, FeedKind.Own);
        else
        {
            level = server.HandicapOf(_handicapPick);
            Say($"{SlotName(_handicapPick)}: handicap {Handicaps.Label(level)} (damage x{Handicaps.Factor(level).ToString(System.Globalization.CultureInfo.InvariantCulture)})", FeedKind.Own);
        }
        return true;
    }

    /// <summary>The score window's frame: remembers that it is up, and tells the host its keys the first time.</summary>
    private void HandicapBoard(bool shown)
    {
        _boardShown = shown;
        if (!shown || _handicapHintSaid || !HostHandicaps) return;
        _handicapHintSaid = true;
        Say("Handicaps: Shift+1..9 picks a player, Shift+[ and Shift+] set the level", FeedKind.Own);
    }
}
