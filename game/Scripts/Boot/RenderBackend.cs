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
        $"renderer: {Method} ({RenderingServer.GetVideoAdapterApiVersion()}, {RenderingServer.GetVideoAdapterName()})";

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
