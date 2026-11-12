using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The SFX / Voice slider's law: the original's 400 x log2(level / 10) millibels, 0 dB at level 10.</summary>
public class AudioLevelsTests
{
    [Fact] public void Level10IsFullVolume() => Assert.Equal(0f, AudioLevels.EffectDb(10), 3);

    /// <summary>T[trunc(100% x L x 0.1)] of the original's table (T[i] = 400 x log2(i / 100) mB) for every level 2..10.</summary>
    [Fact]
    public void EveryLevelMatchesTheOriginalsTable()
    {
        foreach (var (level, db) in new[] { (2, -9.29f), (3, -6.95f), (4, -5.29f), (5, -4f), (6, -2.95f), (7, -2.06f), (8, -1.29f), (9, -0.61f), (10, 0f) })
            Assert.Equal(db, AudioLevels.EffectDb(level), 2);
    }

    [Fact] public void ADoublingIsFourDecibels() => Assert.Equal(4f, AudioLevels.EffectDb(10) - AudioLevels.EffectDb(5), 3);
}
