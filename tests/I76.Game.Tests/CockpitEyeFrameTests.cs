using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The player's cockpit is drawn in the eye's frame (CarNode.PinCockpit, object.gdshaderinc): its meshes' transforms to the eye are
/// made on the CPU from the small transforms inside the car, because the engine's path through the car's place in the world rounded
/// the dash by up to a pixel a frame at 3440x1440, hundreds of metres from the floating origin. Both looks draw the cockpit with the
/// same include, so the uniforms must be declared for both.
/// </summary>
public class CockpitEyeFrameTests
{
    private static string Object()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Shaders", "object.gdshaderinc"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, "game", "Shaders", "object.gdshaderinc")).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheEyeFrameUniformsAreDeclaredForBothLooks()
    {
        var s = Object();
        foreach (var name in new[] { "pit_x", "pit_y", "pit_z", "pit_eye" })
        {
            int at = s.IndexOf($"instance uniform vec4 {name} = vec4(0.0);", StringComparison.Ordinal);
            Assert.True(at >= 0, name);
            // outside every #if block: count the opening and closing lines before it
            var before = s[..at];
            int open = Regex.Matches(before, @"^#if", RegexOptions.Multiline).Count, close = Regex.Matches(before, @"^#endif", RegexOptions.Multiline).Count;
            Assert.Equal(open, close);
        }
    }

    [Fact]
    public void EveryVertexIsPlacedWhetherOrNotItIsTheCockpits()
    {
        // a shader that writes POSITION anywhere must write it for every vertex: the engine's own placement is then not made
        var vertex = Regex.Match(Object(), @"\nvoid vertex\(\) \{\n.*?\n\}\n", RegexOptions.Singleline).Value;
        var writes = Regex.Matches(vertex, @"POSITION\s*=").Count;
        Assert.Equal(1, writes);
        Assert.Contains("\tPOSITION = PROJECTION_MATRIX * p_eye;\n}", vertex);
        Assert.Contains(": MODELVIEW_MATRIX * vec4(VERTEX, 1.0);", vertex);
        // only the cockpit camera's own pass: a shadow's or another camera's keeps the engine's path
        Assert.Contains("distance((MODEL_MATRIX * vec4(pit_eye.xyz, 1.0)).xyz, CAMERA_POSITION_WORLD) < 0.05", vertex);
    }
}
