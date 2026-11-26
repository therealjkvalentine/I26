using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

// CoopNetTests' slowest tests, one class each: xUnit runs the tests of a class one after another and the classes side by side,
// so the class's total (about four minutes) was the project's floor. Each game binds its own ports and shares nothing but the
// read-only installs. The bodies stay in CoopNetTests (partial, in three files); add a theory row there and here.

public class CoopNetIntroTests(ITestOutputHelper output)
{
    [Fact]
    public void CarsInViewOfTheIntroStayOnTheGroundOnTheClient() => new CoopNetTests(output).CarsInViewOfTheIntroStayOnTheGroundOnTheClient();
}

public class CoopNetStoryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("i76", "t01")]
    [InlineData("nitro", "p01")]
    public void AStoryMissionPlaysOverTheNetwork(string game, string mission) => new CoopNetTests(output).AStoryMissionPlaysOverTheNetwork(game, mission);
}

public class CoopNetSceneTests(ITestOutputHelper output)
{
    [Fact]
    public void AGateTheScriptSlidesMovesTheClientsColliderToo() => new CoopNetTests(output).AGateTheScriptSlidesMovesTheClientsColliderToo();

    [Fact]
    public void ACarSpawnedMidMissionShowsItsDamageOnTheClient() => new CoopNetTests(output).ACarSpawnedMidMissionShowsItsDamageOnTheClient();

    [Fact]
    public void AMovieStandsTheGameStillForEveryoneUntilTheHostsCopyIsOver() => new CoopNetTests(output).AMovieStandsTheGameStillForEveryoneUntilTheHostsCopyIsOver();
}

public class CoopNetReplayTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("i76", "t01", false)]
    [InlineData("nitro", "p01", false)]
    [InlineData("i76", "t01", true)]
    [InlineData("nitro", "p01", true)]
    public void TheSuccessorsReplayOfAMissionIsTheHostsGame(string game, string mission, bool chosenCars) => new CoopNetTests(output).TheSuccessorsReplayOfAMissionIsTheHostsGame(game, mission, chosenCars);

    [Theory]
    [InlineData("i76", "t01")]
    [InlineData("nitro", "p01")]
    public void EightPlayersShareAStoryMission(string game, string mission) => new CoopNetTests(output).EightPlayersShareAStoryMission(game, mission);
}
