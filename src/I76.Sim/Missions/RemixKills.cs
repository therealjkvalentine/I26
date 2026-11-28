using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// A generated job's kills at its end (docs/remix-missions.md "Civilian traffic"): the player's kills of cars on a side (the run's
/// kills), and of civilians (cars of nobody's side: the job's traffic), which the run counts apart as heat.
/// </summary>
public static class RemixKills
{
    public static (int Kills, int Civilians) Count(IEnumerable<Entity> entities, Vehicle? player = null)
    {
        int kills = 0, civilians = 0;
        foreach (var e in entities)
        {
            if (!e.Dead || !e.KilledByPlayer || (player is not null && e.Vehicle == player)) continue;
            if (e.Vehicle is not null && e.Team == 0) civilians++;
            else kills++;
        }
        return (kills, civilians);
    }
}
