using I76.Net;
using LiteNetLib;

namespace I76.Server;

/// <summary>The host's bans (the listen host's /ban NAME and /unban NAME, i26-server's <c>ban</c> and <c>unban</c>): a boot that also refuses the player's rejoins for the rest of the game.</summary>
public sealed partial class NetMeleeServer
{
    private readonly BanList _bans = new();

    /// <summary>
    /// The host bans a player: <paramref name="who"/> as <see cref="Kick(string)"/> takes it (a slot, a name or a unique start of one).
    /// A player in the game (or a co-op lobby) is booted and refused by name and address from then on. A name that matches nobody is
    /// refused (a typo would ban nobody), with the names it may have meant; "name:NAME" bans a name not in the game by name only. Bots
    /// are booted, not banned. Returns null when done, else why not.
    /// </summary>
    public string? Ban(string who)
    {
        lock (_lock)
        {
            who = who.Trim();
            bool byName = who.StartsWith(NameOnly, StringComparison.OrdinalIgnoreCase);
            if (byName) who = who[NameOnly.Length..].Trim();
            if (who.Length == 0) return "Ban: whom? (/ban NAME)";
            (string Name, NetPeer? Peer)? target = null;
            if (byName) { }
            else if (_lobbyOpen && FindInLobby(who) is { } m)
            {
                if (m.Peer.Id == _hostPeer) return "Ban: not the host";
                target = (m.Request.Name, m.Peer);
            }
            else if (FindSeat(who) is { } s)
            {
                if (s.Slot == Host.HostSlot) return "Ban: not the host";
                target = (s.Name, s.Connection as NetPeer);
            }
            else if (int.TryParse(who, out _) || Host.Bots.Any(b => b.Name.StartsWith(who, StringComparison.OrdinalIgnoreCase))) return Kick(who);     // a bot, or no such slot
            else return NoSuchPlayer(who, [.. _byPeer.Values.Select(x => x.Name), .. _lobbyOpen ? _lobby.Select(m => m.Request.Name) : []]);
            var (name, peer) = target ?? (who, null);
            if (!_bans.Add(name, peer?.Address) && peer is null)
                return $"Ban: {_bans.Names.First(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))} is banned already";
            Log?.Invoke($"{name} is banned{(peer is null ? " (by name: not in the game)" : $" (name and address {peer.Address})")}");
            Host.Chat(null, $"*** {name} has been banned by the host");
            if (peer is not null) Kick(who);
            return null;
        }
    }

    /// <summary>The prefix of a ban by name alone, of a player not in the game: <c>/ban name:NAME</c>.</summary>
    public const string NameOnly = "name:";

    /// <summary>
    /// Why a ban of <paramref name="who"/> matching nobody in <paramref name="names"/> is refused: the names it starts (more than one), or
    /// else those sharing its first letter, as the ones the host may have meant, and how to ban a name not in the game.
    /// </summary>
    public static string NoSuchPlayer(string who, IReadOnlyList<string> names)
    {
        var starts = names.Where(n => n.StartsWith(who, StringComparison.OrdinalIgnoreCase)).ToList();
        if (starts.Count == 0) starts = [.. names.Where(n => n.Length > 0 && char.ToLowerInvariant(n[0]) == char.ToLowerInvariant(who[0]))];
        string meant = starts.Count == 0 ? "" : $" ({string.Join(" or ", starts.Distinct(StringComparer.OrdinalIgnoreCase).Take(4))}?)";
        return $"Ban: nobody in the game is called \"{who}\"{meant}; /ban {NameOnly}{who} bans the name alone";
    }

    /// <summary>The host lifts a ban (a name, or a unique start of one). Returns null when done, else why not.</summary>
    public string? Unban(string who)
    {
        lock (_lock)
        {
            if (who.Trim().Length == 0) return "Unban: whom? (/unban NAME)";
            if (_bans.Remove(who) is not { } name) return $"Unban: nobody banned called \"{who.Trim()}\" (or more than one)";
            Log?.Invoke($"{name}'s ban is lifted");
            return null;
        }
    }

    /// <summary>The names banned in this game.</summary>
    public IReadOnlyList<string> Banned { get { lock (_lock) return _bans.Names; } }

    /// <summary>Why a join is turned away for a ban, or null.</summary>
    private string? CheckBan(System.Net.IPAddress from, string name) => _bans.Refuses(name, from) ? Protocol.Banned : null;
}
