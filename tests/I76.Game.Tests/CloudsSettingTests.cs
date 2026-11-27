using I76.Formats.Imaging;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Clouds: Flat for a new player and for a settings file without [port] clouds (the fork's default; upstream's is
/// Volumetric, which under the fork's calibrated Lighting loses the dusk and dawn skies' colours), Volumetric only when chosen.
/// </summary>
public class CloudsSettingTests
{
    [Fact]
    public void ANewPlayerHasTheFlatClouds() => Assert.Equal(CloudNoise.Flat, new ShellSettings().Clouds);

    [Fact]
    public void AFileWithoutTheKeyHasTheFlatCloudsAndKeepsEverythingElse()
    {
        var mine = new ShellSettings { Haze = 2, PaletteGrade = true, MotionBlur = true };
        LightingRow.Put(mine, LightingRow.Sunlit);
        var written = mine.Write(null);
        var old = new SettingsFile();
        foreach (var (section, key, value) in written.Entries)
            if (key != "clouds") old.SetValue(section, key, value);
        var back = ShellSettings.Read(old);
        Assert.Equal(CloudNoise.Flat, back.Clouds);
        Assert.Equal((2, true, true, LightingRow.Sunlit), (back.Haze, back.PaletteGrade, back.MotionBlur, LightingRow.Index(back)));
        // and the next save writes the key
        Assert.True(back.Write(old).HasSectionKey("port", "clouds"));
    }

    [Theory]
    [InlineData(CloudNoise.Flat)]
    [InlineData(CloudNoise.Volumetric)]
    public void AChoiceIsSavedAndReadBack(int mode) => Assert.Equal(mode, ShellSettings.Read(new ShellSettings { Clouds = mode }.Write(null)).Clouds);
}
