using Godot;

namespace I76.Game.World;

/// <summary>
/// The modern look's night-lit scenery (docs/deviations.md "Night station lights (modern look)"): a definition's own night lights
/// (ObjectLight, the LOBJ chunks: a night gas station's canopy spot and the light posts beside it) drawn as real lights that light the
/// ground and the cars under them, and the faces the engine draws at full brightness (FaceLight: the station's lit canopy underside,
/// its pumps and pad) shown as painted, as emission (object.gdshaderinc night_glow, i76_night_glow). Both scale with
/// [port] night_station_light in percent (100: the lights at their level and the faces as painted; 0: neither, the scenery by the
/// moon alone as before); I76_NIGHT_STATION sets it outright. The 1997 look takes the lights into its own object light instead.
/// </summary>
public static class StationLight
{
    public const int Default = 100;
    public static readonly int[] Steps = [0, 25, 50, 75, 100, 125, 150, 200];

    /// <summary>A spot's energy at 100 percent: a canopy light 5 m up at 20 m range, the pad under it lit to the level the cutscene shows.</summary>
    public const float SpotEnergy = 4f;
    /// <summary>An omni's energy at 100 percent (no stock scenery light is one).</summary>
    public const float OmniEnergy = 2f;
    /// <summary>The lights' colour: a cool white, a forecourt's fluorescent tubes (the original's light is white).</summary>
    public static readonly Color Colour = new(0.95f, 0.97f, 1f);

    public static int Parse(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, 0, 200) : Default;
    public static int Nearest(int percent) => Array.IndexOf(Steps, Steps.MinBy(p => Math.Abs(p - percent)));

    /// <summary>The setting, 0 to 200; the hook wins.</summary>
    public static int Percent { get; set; } = Default;
    public static int Effective => int.TryParse(System.Environment.GetEnvironmentVariable("I76_NIGHT_STATION"), out int p) ? Math.Clamp(p, 0, 200) : Percent;

    private static readonly List<(Light3D Light, float Base)> Lights = [];

    /// <summary>A light placed with its scenery, at its base energy; its level follows the setting from here on.</summary>
    public static void Add(Light3D light, float baseEnergy)
    {
        Lights.RemoveAll(l => !GodotObject.IsInstanceValid(l.Light));
        light.LightEnergy = baseEnergy * Effective / 100f;
        Lights.Add((light, baseEnergy));
    }

    /// <summary>
    /// The level applied: the lights' energies, and the lit faces' emission at <paramref name="gain"/> (the painted colour's gain
    /// through the exposure in use, as the cockpit's prelit art takes it) under a <paramref name="night"/> preset, 0 by day.
    /// </summary>
    public static void Apply(bool night, float gain)
    {
        float k = Effective / 100f;
        foreach (var (light, b) in Lights) if (GodotObject.IsInstanceValid(light)) light.LightEnergy = b * k;
        RenderingServer.GlobalShaderParameterSet("i76_night_glow", night ? gain * k : 0f);
    }
}
