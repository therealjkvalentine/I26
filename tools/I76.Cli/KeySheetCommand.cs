using I76.Formats.Input;

namespace I76.Cli;

/// <summary>
/// i76 keysheet [input.map] [out.html]: the bindings as a printable page (KeySheet). Without a file, the player's input.map in
/// the game's user data (Windows %APPDATA%\I26, else $XDG_DATA_HOME/I26 or ~/.local/share/I26), else the install's input.map
/// with the port's default keys laid over it, as the game reads it. Without an output, the page goes to stdout.
/// </summary>
public static class KeySheetCommand
{
    public static int Run(List<string> args, string? installDir)
    {
        string? input = args.Count > 1 ? args[1] : null, output = args.Count > 2 ? args[2] : null;
        BindingTable table;
        string source;
        if (input is not null)
        {
            if (!File.Exists(input)) { Console.Error.WriteLine($"keysheet: no file {input}"); return 1; }
            table = BindingTable.Parse(File.ReadAllText(input));
            source = Path.GetFileName(input);
        }
        else if (UserMap() is { } user)
        {
            table = BindingTable.Parse(File.ReadAllText(user));
            source = "input.map (the player's)";
        }
        else if (installDir is not null && Directory.Exists(installDir)
            && Directory.EnumerateFiles(installDir).FirstOrDefault(p => Path.GetFileName(p).Equals("input.map", StringComparison.OrdinalIgnoreCase)) is { } inst)
        {
            table = BindingTable.Parse(File.ReadAllText(inst, System.Text.Encoding.Latin1)).WithWasd();
            source = "input.map (the install's, with the port's default keys)";
        }
        else { Console.Error.WriteLine("keysheet: no input.map given, none saved by the game and no install (I76_INSTALL / --install)"); return 1; }
        var html = KeySheet.Html(table, source);
        if (output is null) Console.Write(html);
        else { File.WriteAllText(output, html); Console.WriteLine($"keysheet: {KeySheet.Layout(table).Sum(g => g.Rows.Count)} commands to {output}"); }
        return 0;
    }

    /// <summary>The game's saved input.map (Godot's custom user folder, "I26"), if there is one.</summary>
    private static string? UserMap()
    {
        string? dir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var path = Path.Combine(dir, "I26", "input.map");
        return File.Exists(path) ? path : null;
    }
}
