using Godot;

namespace I76.Game.Boot;

/// <summary>The release's version (project.godot's application/config/version, stamped by tools/release.sh as "1.2 (3e3e5b5)", the short commit and "-dirty" for local changes; "dev" without) and the network protocol's.</summary>
public static class BuildInfo
{
    public static string Version
    {
        get
        {
            var v = (string)ProjectSettings.GetSetting("application/config/version", "");
            return string.IsNullOrWhiteSpace(v) ? "dev" : v;
        }
    }

    /// <summary>
    /// The commit the game's assembly was built from (game.csproj stamps git's short hash into the informational version), "-" when
    /// it was built without git: for the generated jobs' reproduction line (docs/remix-missions.md).
    /// </summary>
    public static string Commit
    {
        get
        {
            var v = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(BuildInfo).Assembly)?.InformationalVersion ?? "";
            int plus = v.IndexOf('+');
            return plus >= 0 && plus + 1 < v.Length ? v[(plus + 1)..] : "-";
        }
    }

    /// <summary>For the menu's corner: "I26 1.2 (3e3e5b5)  net 5".</summary>
    public static string Line => $"I26 {Version}  net {I76.Net.Protocol.Version}";
}
