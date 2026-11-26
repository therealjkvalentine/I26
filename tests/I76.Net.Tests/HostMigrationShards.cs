using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

// Host migration's two slow theories, one row per class: xUnit runs the tests of a class one after another and the classes
// side by side, so rows in HostMigrationTests would add up (about 47 s each). Each game binds its own ports and shares nothing
// but the read-only install. The bodies stay in HostMigrationTests; add a row there and here.

public class HostMigrationBotsA(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2, 0, false, true)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationBotsB(ITestOutputHelper output)
{
    [Theory]
    [InlineData(3, 2, false, false)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationBotsC(ITestOutputHelper output)
{
    [Theory]
    [InlineData(3, 2, true, true)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationBotsD(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 0, true, false)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationBotsE(ITestOutputHelper output)
{
    [Theory]
    [InlineData(3, 1, false, true, 5, true)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationBotsF(ITestOutputHelper output)
{
    [Theory]
    [InlineData(3, 0, true, false, 5, false)]
    public void BotsSurviveTwoMigrationsAndPlayOn(int bots, int ai, bool restricted, bool graceful, int maxPlayers = 8, bool teams = false) =>
        new HostMigrationTests(output).BotsSurviveTwoMigrationsAndPlayOn(bots, ai, restricted, graceful, maxPlayers, teams);
}

public class HostMigrationNextRoundA(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [Trait("Load", "Sensitive")]   // wall-clock timing: flakes on a busy machine; feature runs skip it (--filter "Load!=Sensitive"), a release check runs it
    public void TheNewHostsNextRoundHasTheBotsToo(bool restricted) => new HostMigrationTests(output).TheNewHostsNextRoundHasTheBotsToo(restricted);
}

public class HostMigrationNextRoundB(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [Trait("Load", "Sensitive")]   // wall-clock timing: flakes on a busy machine; feature runs skip it (--filter "Load!=Sensitive"), a release check runs it
    public void TheNewHostsNextRoundHasTheBotsToo(bool restricted) => new HostMigrationTests(output).TheNewHostsNextRoundHasTheBotsToo(restricted);
}
