using I76.Formats.Remix;
using Xunit;

namespace I76.Formats.Tests;

public class RemixStampTests
{
    /// <summary>
    /// A job's build stamp is the same on every platform: the description's hash is of its YAML with "\n" line ends, which the
    /// serializer would otherwise write as Environment.NewLine ("\r\n" on Windows). The value is the job of seed 1 at builder v8
    /// (re-record it when the director rolls other jobs); an Arm64 Mac gave it before this fix, Windows x64 after.
    /// </summary>
    [Fact]
    public void TheStampIsTheSameOnEveryPlatform()
    {
        Assert.Equal("remix 8 spec df8eeda46bf0b64d base t03 e3b0c44298fc1c14", RemixBuilder.Stamp(RemixDirector.Roll(1), []));
    }
}
