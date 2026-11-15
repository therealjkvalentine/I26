using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Which of Godot's renderers the game runs on. Forward+ (Vulkan or D3D12) is the one it is made for; a machine
/// whose driver has neither falls back to Compatibility (OpenGL), which lacks some of what the modern look uses
/// (docs/compatibility-renderer.md): there the game turns those off or swaps in a plainer way, and says so once.
/// </summary>
public static class RenderBackend
{
    /// <summary>"forward_plus", "mobile" or "gl_compatibility".</summary>
    public static string Method => RenderingServer.GetCurrentRenderingMethod();

    /// <summary>True when running on the Compatibility (OpenGL) renderer.</summary>
    public static bool Compatibility => Method == "gl_compatibility";

    /// <summary>The shell's compatibility notice is shown once a run.</summary>
    public static bool NoticeShown { get; set; }

    /// <summary>The notice's text (the shell's font has capitals and no more than three lines of it fit).</summary>
    public const string Notice = "NO VULKAN OR D3D12 DRIVER FOUND. COMPATIBILITY MODE: SOME EFFECTS ARE OFF.";

    /// <summary>The line the log opens its graphics section with: method, driver and device.</summary>
    public static string Describe() =>
        $"renderer: {Method} on {Driver} ({RenderingServer.GetVideoAdapterApiVersion()}, {RenderingServer.GetVideoAdapterName()})"
        + (StartChoice is { } c ? $", setting {c}" : ", chosen on the command line");

    /// <summary>The driver Godot runs on: "vulkan", "d3d12", "metal", "opengl3", "opengl3_angle", "opengl3_es".</summary>
    public static string Driver => RenderingServer.GetCurrentRenderingDriverName();

    /// <summary>
    /// The Renderer setting this run started with (what the override file asked Godot for), or null when the command line chose the
    /// renderer (--rendering-driver / --rendering-method win over the file). Noted once at boot (<see cref="NoteStart"/>).
    /// </summary>
    public static RendererChoice? StartChoice { get; private set; }
    private static bool _startNoted;

    public static void NoteStart(RendererChoice saved, bool d3d12Offered)
    {
        if (_startNoted) return;
        _startNoted = true;
        StartChoice = CommandLineChose(System.Environment.GetCommandLineArgs()) ? null : Effective(saved, d3d12Offered);
    }

    /// <summary>Whether the command line named the renderer (Godot takes these out of OS.GetCmdlineArgs; the process's own arguments keep them).</summary>
    public static bool CommandLineChose(IEnumerable<string> args) =>
        args.TakeWhile(a => a is not ("--" or "++")).Any(a => a.StartsWith("--rendering-driver", StringComparison.Ordinal) || a.StartsWith("--rendering-method", StringComparison.Ordinal));

    /// <summary>The choice as written to the override file: D3D12 where it is not offered is Auto (<see cref="StartupSettings.Text"/>).</summary>
    public static RendererChoice Effective(RendererChoice c, bool d3d12Offered) => c == RendererChoice.D3D12 && !d3d12Offered ? RendererChoice.Auto : c;

    /// <summary>A driver's name on the row ("Vulkan", "D3D12", "OpenGL", "Metal"), null for one not known.</summary>
    public static string? DriverLabel(string driver) => driver switch
    {
        "vulkan" => "Vulkan",
        "d3d12" => "D3D12",
        "metal" => "Metal",
        _ when driver.StartsWith("opengl3", StringComparison.Ordinal) => "OpenGL",
        _ => null,
    };

    /// <summary>The choice a driver answers, null for one the row does not offer (Metal, or none known).</summary>
    private static RendererChoice? ChoiceOf(string driver) => DriverLabel(driver) switch
    {
        "Vulkan" => RendererChoice.Vulkan,
        "D3D12" => RendererChoice.D3D12,
        "OpenGL" => RendererChoice.OpenGL,
        _ => null,
    };

    /// <summary>
    /// Graphic Detail's Renderer value and a sentence for its tooltip (null for none). <paramref name="shown"/> is the setting on the
    /// row, <paramref name="started"/> the one this run started with (null: the command line chose), <paramref name="driver"/> the
    /// running driver. The setting unchanged since the start: Auto says what it picked ("Auto (Vulkan)"); a choice that did not start
    /// and fell back says so ("OpenGL (fallback)", the tooltip naming the one that failed); a choice the platform answers with another
    /// driver without a failure (Vulkan on macOS runs Metal) names it ("Vulkan (Metal)"). A setting changed for the next start shows
    /// as it is, the tooltip saying what runs now.
    /// </summary>
    public static (string Value, string? Note) RowText(RendererChoice shown, RendererChoice? started, string driver, bool d3d12Offered)
    {
        shown = Effective(shown, d3d12Offered);
        string? running = DriverLabel(driver);
        string name = shown.ToString();
        if (running is null) return (name, null);
        string now = $"Running now: {running}.";
        if (started is null) return (name, $"{now} The command line chose it.");
        if (shown != started) return (name, $"{now} {name} from the next start.");
        if (shown == RendererChoice.Auto) return ($"Auto ({running})", $"Auto picked {running}.");
        if (ChoiceOf(driver) == shown) return (name, null);
        if (ChoiceOf(driver) is not null) return ($"{running} (fallback)", $"{name} did not start here: the game fell back to {running}.");
        return ($"{name} ({running})", now);
    }

    private static long? _vram;
    private static bool _vramRead;

    /// <summary>
    /// The adapter's video memory in MiB, or null where it is not known: on Windows the display driver's own figure for the adapter
    /// the renderer runs on (its registry entry's HardwareInformation.qwMemorySize, matched by name); on macOS the machine's memory,
    /// which Apple silicon's GPU shares; elsewhere null. For the first start's defaults (HdTextures.FirstStart), read once.
    /// </summary>
    public static long? VideoMemoryMb
    {
        get
        {
            if (_vramRead) return _vram;
            _vramRead = true;
            try
            {
                if (OperatingSystem.IsWindows()) _vram = WindowsVideoMemory(RenderingServer.GetVideoAdapterName()) is { } b ? b / (1024 * 1024) : null;
                else if (OperatingSystem.IsMacOS() && OS.GetMemoryInfo()["physical"].AsInt64() is > 0 and var p) _vram = p / (1024 * 1024);
            }
            catch (Exception e) { GD.Print($"video memory: not read ({e.Message})"); }
            GD.Print($"video memory: {(_vram is { } v ? $"{v} MiB" : "unknown")}");
            return _vram;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static long? WindowsVideoMemory(string adapter)
    {
        using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return null;
        long? any = null;
        foreach (var name in cls.GetSubKeyNames())
        {
            if (name.Length != 4 || !char.IsDigit(name[0])) continue;
            using var k = cls.OpenSubKey(name);
            if (k?.GetValue("HardwareInformation.qwMemorySize") is not long bytes || bytes <= 0) continue;
            if (string.Equals(k.GetValue("DriverDesc") as string, adapter, StringComparison.OrdinalIgnoreCase)) return bytes;
            any = Math.Max(any ?? 0, bytes);
        }
        return any;   // the name not matched (a driver that words it otherwise): the largest adapter's
    }
}
