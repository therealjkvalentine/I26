using System;
using System.IO;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// The settings Godot reads only at start, which Graphic Detail offers as "(restart)" rows: the log file and the renderer; and the
/// audio input, on only while a CB Microphone is chosen (the macOS driver creates its input only at start). Godot
/// reads project settings, then the override file project.godot names (application/config/project_settings_override), so the
/// rows write that file for the next start; none of them on (Log off, Renderer Auto, no microphone) is no file. A --rendering-driver or
/// --rendering-method on the command line still wins over it. The file's name is older than the renderer row.
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
        string text = Text(log, renderer, D3D12Offered, micInput, lowLatencyAudio);
        try
        {
            if (text.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PrintErr($"startup settings not saved: {e.Message}"); }
    }

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

/// <summary>Graphic Detail's Renderer (restart): Auto leaves it to Godot (Vulkan, Direct3D 12 on Windows where it prefers it, OpenGL where neither runs).</summary>
public enum RendererChoice { Auto = 0, Vulkan = 1, D3D12 = 2, OpenGL = 3 }
