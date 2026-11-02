using I76.Formats.Assembly;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>Play Options' Melee A.I. (docs/deviations.md): the skill an Instant Melee's computer cars open at.</summary>
public class MeleeAiSkillTests
{
    [Theory]
    [InlineData(0, BotSkill.Easy)]     // Wimp
    [InlineData(1, BotSkill.Normal)]   // Champ
    [InlineData(2, BotSkill.Hard)]     // Badass
    [InlineData(7, BotSkill.Normal)]   // anything else reads as Champ
    public void TheDifficultyLevelIsABotSkill(int difficulty, BotSkill skill) => Assert.Equal(skill, MeleeGame.SkillOfDifficulty(difficulty));

    [Fact]
    public void TheOpeningStepOfEachChoice()
    {
        // Original is the original's 3 whatever the difficulty
        for (int d = 0; d < 3; d++) Assert.Equal(3, MeleeGame.OpeningStep(MeleeAiSkill.Original, d));
        // Difficulty: the bots' tiers (1 the best, 5 the worst), Wimp 4, Champ 3, Badass 1
        Assert.Equal([4, 3, 1], new[] { 0, 1, 2 }.Select(d => MeleeGame.OpeningStep(MeleeAiSkill.Difficulty, d)));
        // the player's own choice, whatever the difficulty
        for (int d = 0; d < 3; d++)
        {
            Assert.Equal(4, MeleeGame.OpeningStep(MeleeAiSkill.Easy, d));
            Assert.Equal(1, MeleeGame.OpeningStep(MeleeAiSkill.Hard, d));
        }
    }

    private static MissionSetup Melee(SnapshotRig.Loaded l)
    {
        var world = new MissionWorld(l.Vfs, l.Asm, l.Install.FindMission("m01")!);
        return MissionSetup.Build(l.Vfs, world, l.Asm, "vppirna1.vcf", rand: new CrtRand(1), melee: ["vcgcour1.vcf", "valepre1.vcf"]);
    }

    /// <summary>The opponents take the opening step's skill and aggression row; without a choice they take the original's 3.</summary>
    [Fact]
    public void AnInstantMeleesOpponentsOpenAtTheStep()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var plain = Melee(l);
        plain.Melee!.Start();
        var easy = Melee(l);
        easy.Melee!.OpenAt(MeleeGame.OpeningStep(MeleeAiSkill.Difficulty, 0));
        easy.Melee.Start();
        var hard = Melee(l);
        hard.Melee!.OpenAt(MeleeGame.OpeningStep(MeleeAiSkill.Hard, 0));
        hard.Melee.Start();
        static List<Entity> Opponents(MissionSetup s) => s.Script!.Host.Entities.Where(e => e.Vehicle is not null && e.Vehicle != s.World.Player && e.Name.StartsWith("nvcl")).ToList();
        Assert.Equal(2, Opponents(plain).Count);
        Assert.All(Opponents(plain), e => Assert.Equal((3, 3, 2), (e.SkillA, e.SkillB, e.Aggression)));
        Assert.All(Opponents(easy), e => Assert.Equal((4, 4, 3), (e.SkillA, e.SkillB, e.Aggression)));
        Assert.All(Opponents(hard), e => Assert.Equal((1, 1, 0), (e.SkillA, e.SkillB, e.Aggression)));
        Assert.Equal(4, easy.Melee.Aggression);
        Assert.Throws<InvalidOperationException>(() => easy.Melee.OpenAt(2));   // only before the start
    }
}
