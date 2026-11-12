using Godot;
using I76.Game.Ui;
using System.Collections.Generic;

namespace I76.Game.Boot;

/// <summary>
/// A mission's briefing played while the mission loads (Main.BuildBehindCard, BriefingOverlap, docs/deviations.md): the audit of
/// what the mission does under the movie.
/// </summary>
public partial class Main
{
    /// <summary>
    /// I76_BRIEFING_AUDIT=1: under the briefing, twice a second, a line of what the mission does meanwhile (it must do nothing): its
    /// ticks, its script's tick, and the game's sounds heard (a player whose position moved since the last line; the movie's own
    /// are left out). "briefing audit: ... heard none" all the way through is the frozen mission.
    /// </summary>
    private static readonly bool AuditOn = System.Environment.GetEnvironmentVariable("I76_BRIEFING_AUDIT") == "1";
    private readonly Dictionary<ulong, double> _audited = [];
    private double _auditAt;

    /// <summary>The audit's lines for the first two seconds after the movie: the mission's own sounds (T02's first CB line) are heard now.</summary>
    private async void AuditAfter()
    {
        for (int i = 0; i < 5; i++)
        {
            _auditAt = 0;
            Audit();
            await ToSignal(GetTree().CreateTimer(0.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            if (!IsInsideTree()) return;
        }
    }

    private void Audit()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _auditAt < 0.5) return;
        _auditAt = now;
        var heard = new List<string>();
        void Walk(Node n)
        {
            if (n is MoviePlayer) return;
            double? at = n switch
            {
                AudioStreamPlayer { Playing: true } p => p.GetPlaybackPosition(),
                AudioStreamPlayer3D { Playing: true } p => p.GetPlaybackPosition(),
                AudioStreamPlayer2D { Playing: true } p => p.GetPlaybackPosition(),
                _ => null,
            };
            if (at is { } pos)
            {
                ulong id = n.GetInstanceId();
                if (_audited.TryGetValue(id, out var was) && pos != was) heard.Add($"{n.GetParent()?.Name}/{n.Name} {was:0.00}->{pos:0.00}");
                _audited[id] = pos;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(GetTree().Root);
        GD.Print($"briefing audit{(GetNodeOrNull("Movie") is null ? " (the mission going)" : "")}: ticks {_sim?.Session.Ticks.ToString() ?? "-"}, script tick {_sim?.Script?.Host.FsmTick.ToString() ?? "-"}, "
            + $"CB line {_sim?.Script?.Host.Cb.Current?.Name ?? "none"}, paused {GetTree().Paused}, heard {(heard.Count == 0 ? "none" : string.Join(", ", heard))}");
    }
}
