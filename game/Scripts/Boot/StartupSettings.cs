using System;
using System.IO;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// The settings Godot reads only at start, which Graphic Detail offers as rows taking effect from the next start: the log file and the renderer; and the
/// audio input, on only while a CB Microphone is chosen (the macOS driver creates its input only at start). Godot
/// reads project settings, then the override file project.godot names (application/config/project_settings_override), so the
/// rows write that file for the next start; none of them on (Log off, Renderer Auto, no microphone) is no file. A --rendering-driver or
/// --rendering-method on the command line still wins over it. The file's name is older than the renderer row.
/// Renderer Auto is Godot's own choice but on Windows with an AMD card, where it is Direct3D 12 (<see cref="AutoPick"/>); the
/// card is known only once a renderer runs, so the game writes that at start (<see cref="PickAtStart"/>) and it holds from the next.
/// </summary>
public static class StartupSettings
{
    public const string OverridePath = "user://log_settings.cfg";

    /// <summary>Whether a Direct3D 12 renderer is offered here (Windows only).</summary>
    public static bool D3D12Offered => OS.GetName() == "Windows";

    /// <summary>The next start's settings: the override file, or none when the log is off and the renderer is Auto.</summary>
    public static void Write(bool log, RendererChoice renderer, bool micInput = false, bool lowLatencyAudio = true)
    {
        string path = ProjectSettings.GlobalizePath(OverridePath);
        if (renderer == RendererChoice.Auto) renderer = AutoPick(OS.GetName(), RenderingServer.GetVideoAdapterVendor());
        string text = Text(log, renderer, D3D12Offered, micInput, lowLatencyAudio);
        try
        {
            if (text.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PrintErr($"startup settings not saved: {e.Message}"); }
    }

    /// <summary>
    /// What Renderer Auto runs on a machine: Direct3D 12 on Windows with an AMD card, whose Vulkan driver draws the 1997 look's ground
    /// black where D3D12 and OpenGL draw it right (#133; the same card's Linux Vulkan driver is fine); otherwise Auto, Godot's own
    /// choice (Vulkan where it runs).
    /// </summary>
    public static RendererChoice AutoPick(string os, string gpuVendor) =>
        os == "Windows" && IsAmd(gpuVendor) ? RendererChoice.D3D12 : RendererChoice.Auto;

    /// <summary>An AMD card by the vendor the renderer reports: "AMD" (Vulkan, D3D12), "ATI Technologies Inc." (OpenGL).</summary>
    public static bool IsAmd(string? gpuVendor) => gpuVendor is { } v
        && (v.Trim().Equals("AMD", StringComparison.OrdinalIgnoreCase) || v.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase)
            || v.Contains("ATI Technologies", StringComparison.OrdinalIgnoreCase) || v.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// At start, with Renderer Auto: the override file made to say Auto's pick for this machine, so the next start runs on it. Written
    /// only when it says something else, so nothing is rewritten at every start (and a file Godot could not follow, falling back from
    /// D3D12 to Vulkan, is left alone). True when the file was written.
    /// </summary>
    public static bool PickAtStart(bool log, RendererChoice renderer, bool micInput = false, bool lowLatencyAudio = true)
    {
        var pick = AutoPick(OS.GetName(), RenderingServer.GetVideoAdapterVendor());
        if (renderer != RendererChoice.Auto || pick == RendererChoice.Auto) return false;
        string path = ProjectSettings.GlobalizePath(OverridePath);
        string want = Text(log, pick, D3D12Offered, micInput, lowLatencyAudio);
        try { if (File.Exists(path) && File.ReadAllText(path) == want) return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        Write(log, renderer, micInput, lowLatencyAudio);
        GD.Print($"renderer: Auto picks {Label(pick)} on this machine from the next start");
        return true;
    }

    public static string Label(RendererChoice r) => r.ToString();

    /// <summary>The override file's text ("" for none).</summary>
    public static string Text(bool log, RendererChoice renderer, bool d3d12Offered, bool micInput = false, bool lowLatencyAudio = true)
    {
        string s = "";
        if (log) s += "[debug]\n\nfile_logging/enable_file_logging=true\nfile_logging/enable_file_logging.pc=true\nfile_logging/max_log_files=3\n\n";
        if (renderer == RendererChoice.D3D12 && !d3d12Offered) renderer = RendererChoice.Auto;
        switch (renderer)
        {
            case RendererChoice.Vulkan:
                s += "[rendering]\n\nrenderer/rendering_method=\"forward_plus\"\nrendering_device/driver.windows=\"vulkan\"\nrendering_device/driver.linuxbsd=\"vulkan\"\n";
                break;
            case RendererChoice.D3D12:
                s += "[rendering]\n\nrenderer/rendering_method=\"forward_plus\"\nrendering_device/driver.windows=\"d3d12\"\n";
                break;
            case RendererChoice.OpenGL:
                s += "[rendering]\n\nrenderer/rendering_method=\"gl_compatibility\"\nrenderer/rendering_method.mobile=\"gl_compatibility\"\ngl_compatibility/driver=\"opengl3\"\n";
                break;
        }
        string audio = (micInput ? "driver/enable_input=true\n" : "")
            // [port] low_latency_audio=false: Godot's stock output latency instead of project.godot's short one (AudioLatency)
            + (lowLatencyAudio ? "" : $"driver/output_latency={AudioLatency.StockMs}\n");
        if (audio.Length > 0) s += (s.Length > 0 ? "\n" : "") + "[audio]\n\n" + audio;
        return s;
    }
}

/// <summary>
/// [port] low_latency_audio (docs/1997-parity.md, round 3): project.godot asks the audio driver for a 5 ms output buffer (Godot's stock 15
/// gets 10 on WASAPI) and the game runs Godot's physics step at 240 a second, not 60: a positional voice (AudioStreamPlayer3D, every
/// sound on a car) starts only at the next physics step, so a gun's report waited up to 16.7 ms for it. Off: the stock 15 ms (in the
/// override file, for the next start) and 60 steps. Modern Physics still steps Jolt at its own 120 while it drives.
/// </summary>
public static class AudioLatency
{
    public const int Ms = 5, StockMs = 15, PhysicsRate = 240, StockPhysicsRate = 60;

    /// <summary>The physics steps a second for the setting (applied at start and live).</summary>
    public static int PhysicsFor(bool lowLatency) => lowLatency ? PhysicsRate : StockPhysicsRate;
}

/// <summary>Graphic Detail's Renderer (restart): Auto leaves it to Godot (Vulkan; OpenGL where neither Vulkan nor Direct3D 12 runs), but for Direct3D 12 on Windows with an AMD card (<see cref="StartupSettings.AutoPick"/>).</summary>
public enum RendererChoice { Auto = 0, Vulkan = 1, D3D12 = 2, OpenGL = 3 }
