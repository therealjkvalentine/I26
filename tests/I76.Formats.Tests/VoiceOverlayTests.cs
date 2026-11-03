using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

public class VoiceOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "i26-voices-" + Guid.NewGuid().ToString("N"));

    public VoiceOverlayTests() => Directory.CreateDirectory(Path.Combine(_root, "voices", "de"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("fr")]
    public void Nothing_is_resolved_for_no_overlay_or_an_absent_language(string? arg) => Assert.Null(VoiceOverlay.Resolve(arg, _root));

    [Fact]
    public void A_language_names_its_folder_in_the_user_folder()
        => Assert.Equal(Path.Combine(_root, "voices", "de"), VoiceOverlay.Resolve("de", _root));

    [Fact]
    public void A_folder_is_taken_as_given()
        => Assert.Equal(Path.GetFullPath(Path.Combine(_root, "voices", "de")), VoiceOverlay.Resolve(Path.Combine(_root, "voices", "de"), "nowhere"));

    [Fact]
    public void Files_stand_in_by_name_and_by_stem_across_wav_and_gpw()
    {
        var dir = Path.Combine(_root, "voices", "de");
        File.WriteAllBytes(Path.Combine(dir, "Taurus1.WAV"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "stem2.gpw"), [2]);
        File.WriteAllBytes(Path.Combine(dir, "new3.wav"), [3]);
        File.WriteAllBytes(Path.Combine(dir, "readme.txt"), [4]);
        var install = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "taurus1.wav", "stem2.wav" };
        var map = VoiceOverlay.Files(dir, install.Contains);
        Assert.Equal(Path.Combine(dir, "Taurus1.WAV"), map["taurus1.wav"]);
        Assert.Equal(Path.Combine(dir, "stem2.gpw"), map["stem2.gpw"]);
        Assert.Equal(Path.Combine(dir, "stem2.gpw"), map["stem2.wav"]);   // the install's name for that stem
        Assert.Equal(Path.Combine(dir, "new3.wav"), map["new3.wav"]);
        Assert.False(map.ContainsKey("readme.txt"));
    }
}
