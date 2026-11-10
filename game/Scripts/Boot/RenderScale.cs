using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Graphic Detail's Render Scale: the 3D view drawn at 50 to 200 % of the window's size in each direction and scaled to it (the
/// viewport's 3D scaling). Below 100, fewer pixels to shade for a GPU that cannot fill the window (the ground, the horizon ring and
/// the sky are shaded per pixel, so the frame's cost falls nearly with the pixel count), scaled up by AMD FSR 1.0 (bilinear on the
/// Compatibility renderer, which has no FSR) with the texture mip bias the engine recommends for the scale, so the ground keeps its
/// detail; above 100, supersampling, a cleaner picture for a GPU with room to spare, held down as far as the card's video memory
/// needs (<see cref="Capped(float, Vector2I, int, long)"/>). The 2D layers (the HUD, the menus) are not scaled. 100 % is the
/// engine's own, as before. Also the Anti-Aliasing row's 8x step (4x on the Compatibility renderer, which may lack it).
/// </summary>
public static class RenderScale
{
    /// <summary>Percent, per choice, in the row's order; <see cref="Default"/> is 100.</summary>
    public static readonly int[] Percents = [50, 67, 75, 85, 100, 150, 200];
    /// <summary>The row's values: 100 % is the original's (the view drawn at the window's own size); the rest are marked as the port's.</summary>
    public static readonly string[] Names = ["50%*", "67%*", "75%*", "85%*", "100%", "150%*", "200%*"];
    public static readonly int Default = System.Array.IndexOf(Percents, 100);

    /// <summary>The choice for a shell.cfg [port] render_scale value (percent); anything not offered is 100.</summary>
    public static int Parse(int? percent) => percent is { } p && System.Array.IndexOf(Percents, p) is var i and >= 0 ? i : Default;

    public static float Scale(int choice) => Percents[System.Math.Clamp(choice, 0, Percents.Length - 1)] / 100f;

    /// <summary>The Anti-Aliasing row's choices: Off, 2x, 4x and 8x, the last marked as the port's (the row had 2x and 4x first; the original showed no anti-aliasing).</summary>
    public static readonly string[] AntiAliasing = ["Off", "2x", "4x", "8x" + I76.Game.Shell.ShellSettings.PortMark];

    /// <summary>The engine's MSAA for a row's choice; 8x is 4x where the renderer has no 8x (Compatibility, where it may be missing).</summary>
    public static Viewport.Msaa Msaa(int choice, bool compatibility) => choice switch
    {
        <= 0 => Viewport.Msaa.Disabled,
        1 => Viewport.Msaa.Msaa2X,
        2 => Viewport.Msaa.Msaa4X,
        _ => compatibility ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Msaa8X,
    };

    /// <summary>Samples a pixel of <paramref name="msaa"/>: 1 without it.</summary>
    public static int Samples(Viewport.Msaa msaa) => msaa switch
    {
        Viewport.Msaa.Msaa2X => 2,
        Viewport.Msaa.Msaa4X => 4,
        Viewport.Msaa.Msaa8X => 8,
        _ => 1,
    };

    /// <summary>
    /// The most pixels the supersampled 3D view is drawn at (a 3840x2160 frame) where the card's video memory is not known: 200 % of a
    /// 4K desktop is 7680x4320, whose buffers (with 8x Anti-Aliasing well over the memory of most GPUs) the renderer could not make.
    /// </summary>
    public const long MaxPixels = 3840L * 2160;

    /// <summary>The scale drawn for <paramref name="scale"/> on a window of <paramref name="size"/> where video memory is not known: no more than <see cref="MaxPixels"/>, never under 100 % (a scale under 100 % as chosen).</summary>
    public static float Capped(float scale, Vector2I size)
    {
        long px = (long)size.X * size.Y;
        if (px <= 0 || scale <= 1f) return scale;
        return System.Math.Clamp(System.MathF.Sqrt(MaxPixels / (float)px), 1f, scale);
    }

    /// <summary>
    /// Bytes the Forward+ renderer's 3D buffers take for a pixel drawn, whatever the multisampling: the resolved colour (half-float
    /// RGBA), depth and its copy, normal-roughness, and the glow and exposure chains at half size and down. Measured from the engine's
    /// own video memory figure at 100 and 200 % (T05 at 3440x1440, docs/deviations.md "Render Scale").
    /// </summary>
    public const double ResolvedBytes = 30;
    /// <summary>Bytes a sample of a multisampled pixel takes (colour, depth-stencil, normal-roughness, each per sample), with MSAA on.</summary>
    public const double SampleBytes = 17;
    /// <summary>Bytes a window pixel takes when the 3D view is scaled to it (the scaled result, and FSR's intermediate below 100 %).</summary>
    public const double WindowBytes = 16;

    /// <summary>The 3D view's buffers for a window of <paramref name="size"/> drawn at <paramref name="scale"/> with <paramref name="samples"/> samples a pixel, in bytes.</summary>
    public static long TargetBytes(Vector2I size, float scale, int samples)
    {
        double window = (double)System.Math.Max(0, size.X) * System.Math.Max(0, size.Y);
        double perPixel = ResolvedBytes + (samples > 1 ? SampleBytes * samples : 0);
        return (long)(window * scale * scale * perPixel + (scale != 1f ? window * WindowBytes : 0));
    }

    /// <summary>
    /// Of the card's video memory, the share the whole game may take (its textures and meshes, which the engine counts, and the 3D
    /// view's buffers); the rest is left to the desktop, other programs and the driver.
    /// </summary>
    public const double MemoryShare = 0.75;

    /// <summary>
    /// What the 3D view's buffers may take, in bytes, or null where it is not known (the 3840x2160 rule then): [port]
    /// render_scale_cap's MiB when set (<paramref name="capMb"/> above 0), else <see cref="MemoryShare"/> of the card's
    /// <paramref name="totalMb"/> less what the engine already holds besides the view (<paramref name="usedBytes"/>, the engine's
    /// video memory, less <paramref name="viewBytes"/>, the view's buffers now). Never under none.
    /// </summary>
    public static long? Budget(int? capMb, long? totalMb, long usedBytes, long viewBytes)
    {
        if (capMb is > 0) return (long)capMb.Value << 20;
        if (totalMb is not > 0) return null;
        long others = System.Math.Max(0, usedBytes - viewBytes);
        return System.Math.Max(0, (long)(totalMb.Value * MemoryShare * 1048576) - others);
    }

    /// <summary>
    /// The scale drawn for <paramref name="scale"/> on a window of <paramref name="size"/> with <paramref name="samples"/> samples a
    /// pixel, the view's buffers to fit <paramref name="budget"/> bytes: lowered to the whole percent that fits, never under 100 %
    /// (a scale of 100 % or under as chosen).
    /// </summary>
    public static float Capped(float scale, Vector2I size, int samples, long budget)
    {
        if (scale <= 1f || size.X <= 0 || size.Y <= 0 || TargetBytes(size, scale, samples) <= budget) return scale;
        double window = (double)size.X * size.Y;
        double perPixel = ResolvedBytes + (samples > 1 ? SampleBytes * samples : 0);
        double fit = System.Math.Sqrt(System.Math.Max(0, budget - window * WindowBytes) / (window * perPixel));
        return System.Math.Clamp((float)(System.Math.Floor(fit * 100) / 100), 1f, scale);
    }

    /// <summary>[port] render_scale_cap: "off" (no cap), "auto" (by the card's video memory, the default) or a number of MiB for the view's buffers. Returns 0 for off, null for auto.</summary>
    public static int? ParseCap(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "off" => 0,
        { } w when int.TryParse(w, out int mb) && mb > 0 => mb,
        _ => null,
    };

    /// <summary>The word stored for <see cref="ParseCap"/>'s value.</summary>
    public static string CapWord(int? cap) => cap switch { 0 => "off", > 0 => cap.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "auto" };

    /// <summary>
    /// The row's value for a choice: its name, or where video memory holds the choice lower the scale drawn ("held 160%"; the pads'
    /// value column has room for no more), <see cref="HeldNote"/> saying the rest in the row's tooltip.
    /// </summary>
    public static string Label(int choice) => Label(choice, _choice, _held);

    /// <summary><see cref="Label(int)"/> for the player's <paramref name="chosen"/> step drawn at <paramref name="held"/> percent (null: as chosen).</summary>
    public static string Label(int choice, int chosen, int? held)
    {
        int c = System.Math.Clamp(choice, 0, Names.Length - 1);
        return held is { } h && c == chosen && h < Percents[c] ? $"held {h}%" : Names[c];
    }

    /// <summary>The tooltip's and the bug report's words for a held scale ("200 % (held at 160 %: video memory)"), or "" when nothing holds it.</summary>
    public static string HeldNote => HeldWords(_choice, _held);

    public static string HeldWords(int chosen, int? held) =>
        held is { } h && h < Percents[System.Math.Clamp(chosen, 0, Percents.Length - 1)] ? $"{Percents[System.Math.Clamp(chosen, 0, Percents.Length - 1)]} % (held at {h} %: video memory)" : "";

    /// <summary>The row's values as <see cref="Label"/> words them now (the Esc menu's list; the same array, its words changed as the cap changes).</summary>
    public static readonly string[] Shown = (string[])Names.Clone();

    /// <summary>The scale drawn now, in percent, when video memory holds the player's choice lower; else null.</summary>
    public static int? Held => _held;

    private static int _choice = Default, _step = Default, _samples = 1;
    private static int? _cap, _held;
    private static long _viewBytes, _verifiedBytes;
    private static bool _watching;
    private static int _drawnFrames;

    /// <summary>A start that does not get to draw a frame at a supersampled scale leaves this behind: the next start draws at 100 %.</summary>
    private const string CheckFile = "user://render_scale.check";
    /// <summary>Frames drawn at a new supersampled scale before it counts as one the card can draw.</summary>
    private const int CheckFrames = 30;

    /// <summary>Sets the main window's 3D scale, live (and again as the window's size changes): FSR 1.0 up from below 100 % (bilinear where the renderer has none), bilinear down from above.</summary>
    public static void Apply(I76.Game.Shell.ShellSettings s)
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root }) return;
        if (!_watching)
        {
            _watching = true;
            Recover(s);
            root.SizeChanged += () => Set(root);
            RenderingServer.FramePostDraw += Drawn;
        }
        _choice = _step = s.RenderScale;
        _samples = Samples(Msaa(s.Graphics[0x8], RenderBackend.Compatibility));
        _cap = s.RenderScaleCap;
        Set(root);
    }

    /// <summary>The step [port] target_fps draws (FrameTarget), under the player's own: applied with the same cap.</summary>
    public static void Draw(int step)
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root }) return;
        _step = step;
        Set(root);
    }

    /// <summary>The cap again, the mission's textures loaded: what they hold is known now.</summary>
    public static void Refresh()
    {
        if (_watching && Engine.GetMainLoop() is SceneTree { Root: { } root }) Set(root);
    }

    private static void Set(Window root)
    {
        float scale = Scale(_step);
        float drawn = scale;
        if (_cap != 0 && scale > 1f)
        {
            long used = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
            drawn = Budget(_cap, RenderBackend.VideoMemoryMb, used, _viewBytes) is { } budget
                ? Capped(scale, root.Size, _samples, budget)
                : Capped(scale, root.Size);
        }
        int? held = _step == _choice && drawn < scale ? (int)System.MathF.Round(drawn * 100) : null;
        if (held != _held && held is { } h) GD.Print($"render scale: {Percents[_step]} % held at {h} % for video memory ({root.Size.X}x{root.Size.Y}, {_samples} samples)");
        _held = held;
        for (int i = 0; i < Shown.Length; i++) Shown[i] = Label(i);
        root.Scaling3DMode = drawn < 1f && !RenderBackend.Compatibility ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        root.Scaling3DScale = drawn;
        // no mip bias of our own: the engine adds log2 of the 3D scale to Viewport.texture_mipmap_bias itself
        long before = _viewBytes;
        _viewBytes = TargetBytes(root.Size, drawn, _samples);
        if (drawn > 1f && _viewBytes > _verifiedBytes && _viewBytes != before) Arm(drawn);
    }

    /// <summary>A supersampled view larger than any drawn yet this start (a scale, a window size, more samples): the check file says so until <see cref="CheckFrames"/> frames are drawn with it.</summary>
    private static void Arm(float scale)
    {
        _drawnFrames = 0;
        using var f = Godot.FileAccess.Open(CheckFile, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(((int)System.MathF.Round(scale * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Drawn()
    {
        if (_viewBytes <= _verifiedBytes || ++_drawnFrames < CheckFrames) return;
        _verifiedBytes = _viewBytes;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(CheckFile));
    }

    /// <summary>
    /// The last start set a supersampled scale and ended before it drew (the card could not make the view's buffers: a crash on
    /// D3D12 and Vulkan alike): this start draws at 100 % and saves it, so a scale the card cannot draw is not kept for every start.
    /// </summary>
    private static void Recover(I76.Game.Shell.ShellSettings s)
    {
        if (!Godot.FileAccess.FileExists(CheckFile)) return;
        string was = Godot.FileAccess.GetFileAsString(CheckFile).Trim();
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(CheckFile));
        if (Scale(s.RenderScale) <= 1f) return;
        GD.PrintErr($"render scale: the last start ended before it drew at {was} %: Render Scale is 100 % again");
        var saved = I76.Game.Shell.ShellSettings.Load();
        saved.RenderScale = s.RenderScale = Default;
        saved.Save();
    }
}
