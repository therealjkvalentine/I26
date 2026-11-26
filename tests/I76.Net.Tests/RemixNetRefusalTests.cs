using I76.Formats.Vfs;
using I76.Server;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The port's generated remix scenarios are single player (docs/remix-missions.md): a server asked to host one (a mission found
/// in GameInstall.UserMissionDir, where the remix builder writes them) refuses it rather than start a game no client could join.
/// </summary>
[Collection("UserMissionDir")]
public class RemixNetRefusalTests
{
    [Fact]
    public void AServerRefusesToHostAGeneratedMission()
    {
        if (HostTests.InstallDir is not { } inst) return;
        var install = new GameInstall(inst);
        var dir = Path.Combine(Path.GetTempPath(), "i26-remix-net-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        var before = GameInstall.UserMissionDir;
        try
        {
            // a stand-in for a built remix: an arena's file under a generated name, in the generated missions' folder
            File.Copy(install.FindMission("m01")!, Path.Combine(dir, "x99net.msn"), overwrite: true);
            File.WriteAllText(Path.Combine(dir, "x99net.remix"), "remix test");
            GameInstall.UserMissionDir = dir;
            Assert.True(GameInstall.IsUserMission(install.FindMission("x99net")!));
            Assert.False(GameInstall.IsUserMission(install.FindMission("m01")!));
            var ex = Assert.Throws<NotSupportedException>(() => new MeleeHost(new ServerOptions { Install = inst, Arena = "x99net" }));
            Assert.Contains("single player", ex.Message);
            Assert.Throws<NotSupportedException>(() => new MeleeHost(new ServerOptions { Install = inst, Arena = "x99net", Coop = "x99net" }));
        }
        finally
        {
            GameInstall.UserMissionDir = before;
            Directory.Delete(dir, recursive: true);
        }
    }
}
