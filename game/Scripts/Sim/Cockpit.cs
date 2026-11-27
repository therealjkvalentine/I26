using Godot;
using I76.Formats.Input;
using I76.Sim.Vehicles;

namespace I76.Game.Sim;

/// <summary>
/// The in-car view's live parts: the head (eye at
/// VLOC[0], 73.7° vertical field of view = the original's 90° horizontal on
/// 4:3, pitched −13°, the eight-state glance machine eased at 4·dt), the
/// panels composed into their textures (PRND21, compass, damage panel,
/// weapons and specials panels with their rolling counters), and the
/// animated strips (steering wheel, radar sweep, the arms). The panels are
/// kept up to date while the cockpit camera is current or the external
/// instruments are shown (hud || hud2d), the weapons panel
/// always; the head, wheel, arms, 3D needles and mirror only in
/// the car.
/// </summary>
public partial class Cockpit : Node
{
    public required Vehicle Car { get; init; }
    public required CarNode Node3D { get; init; }
    public required CockpitPanels Panels { get; init; }
    public required I76.Formats.Assembly.TextureResolver Resolver { get; init; }
    public required Assets.TextureFactory Textures { get; init; }
    public required Assets.MaterialLibrary Materials { get; init; }
    public I76.Sim.Weapons.Combat? Combat { get; init; }
    public Assets.AudioBank? Audio { get; init; }
    /// <summary>The art variant: the driver's (re-n3), 1 in the base game</summary>
    public int LightVariant { get; init; } = 1;
    /// <summary>The door mirrors' painted glass in the interior (MirrorGlass), which [port] side_mirrors puts the view behind into.</summary>
    public IReadOnlyList<I76.Formats.Assembly.MirrorGlass.Pane> Glass { get; init; } = [];
    /// <summary>The mirror's clear colour, palette index 0xea.</summary>
    public Color MirrorBackground { get; init; } = Colors.Black;
    /// <summary>The 1997 look: the mirror at the original's 256x64, nearest-sampled, and starting in objects-only mode.</summary>
    public bool Faithful
    {
        get => _faithful;
        set
        {
            bool was = _faithful;
            _faithful = value;
            Node3D.RingDrawn = I76.Game.Ui.ReticleArt.Drawn && !value;
            ApplyMirrorMode();
            _repaintWeapons = _repaintSpecials = true;
            // the modern look's labels want the weapons and specials panels at twice the original's pixels at least
            Panels.LabelScale = value ? 1 : 2;
            Panels.KeyRuledLines = !value;
            if (was != value && IsInsideTree()) ArtSwapped();
        }
    }
    private bool _faithful;

    private bool Active => Node3D.CockpitCamera?.Current == true;

    /// <summary>
    /// Graphic Detail's Ext. Instrum. Disp. (or the `[` key): the 2D panels
    /// are drawn outside the car too, so
    /// they are updated there as well.
    /// </summary>
    public Func<bool>? Instruments { get; set; }

    /// <summary>The radar's current frame, as the 2D panel shows it (rows bottom-up), or null before the first sweep.</summary>
    public Texture2D? RadarTexture => _radarLive?.Texture;
    /// <summary>The radar's current picture's texels per original pixel (a pack's upscaled frames; the weapon camera's is 1).</summary>
    public int RadarScale { get; private set; } = 1;
    /// <summary>The original radar frame's width, which the scope's and the layout's pixels count in.</summary>
    private int _radarBaseW = 256;

    /// <summary>Made by <see cref="ReadySteps"/>, a step at a time, by whoever added it (a mission loading under its briefing), not all in <see cref="_Ready"/>.</summary>
    public bool Staged { get; init; }

    public override void _Ready()
    {
        if (!Staged) foreach (var _ in ReadySteps()) { }
    }

    /// <summary>What <see cref="_Ready"/> makes (the panels, the wheel's, arms' and radar's frames, the mirror), a null between the costly parts.</summary>
    public IEnumerable<object?> ReadySteps()
    {
        SetProcess(false);   // in the tree while its steps run, frames drawn between them: not processed until all of it is made
        // the panels' and the frames' upscales read and decoded on a worker (no engine resource made there): on the main thread the
        // decoding's wait was a frame of a third of a second under a playing briefing
        var fetched = I76.Game.Boot.Beside.Run(() => { Panels.Prefetch(); PrefetchArt(); });
        yield return fetched;
        fetched.GetAwaiter().GetResult();
        I76.Game.World.MissionBuilder.Trace("cockpit: panels prefetched");
        foreach (var step in LoadArtSteps(prefetched: true)) yield return step;
        _rng = new System.Random();
        var shellSettings = I76.Game.Shell.ShellSettings.Load();
        HeadTracker.Apply(shellSettings);
        OpenTrackLauncher.AutoStart(shellSettings);   // Opentrack Auto: opentrack started when none runs (docs/playing.md)
        _roundSetting = shellSettings.MirrorRound;
        _glassRoundSetting = shellSettings.SideMirrorRound;
        _headTurned = shellSettings.SideMirrorHeadTurned;
        _faceProud = FaceHook is { } fh ? DoorFace.Proud(fh) : shellSettings.SideMirrorFaceProud;
        _faceTurned = FaceHook is { } fh2 ? DoorFace.Turned(fh2) : shellSettings.SideMirrorFaceTurned;
        _faceOverSetting = [shellSettings.SideMirrorFaceOverLeft, shellSettings.SideMirrorFaceOverRight];
        _tiltSetting = MirrorAim.TiltOverride(TiltHook) is { } tilt ? [tilt.L, tilt.R] : [shellSettings.SideMirrorTiltLeft, shellSettings.SideMirrorTiltRight];
        yield return null;
        // the mirror's edge field and fill (arithmetic on its pictures) on a worker, its viewport and material here
        if (Resolver.FindByStem("zmiri101") is { } mirrorMember)
        {
            var pre = I76.Game.Boot.Beside.Run(() =>
            {
                var o = Textures.GetOriginalRgba(mirrorMember);
                var a = Textures.GetRgba(mirrorMember);
                _mirrorPre = (o is null ? null : MirrorEdge.Field(o.Rgba, o.Width, o.Height),
                    a is null ? null : MirrorEdge.Fill(a.Rgba, a.Width, a.Height, Textures.Scale(mirrorMember)));
            });
            yield return pre;
            pre.GetAwaiter().GetResult();
        }
        foreach (var step in SetupMirrorSteps()) yield return step;
        _mirrorPre = default;
        I76.Game.World.MissionBuilder.Trace("cockpit: mirror");
        yield return null;
        CompassOutline();
        I76.Game.World.MissionBuilder.Trace("cockpit: compass");
        _lookArt.Made((Textures.Hd, _faithful));
        Controls.LookingBack = () => IsInstanceValid(this) && LookingBack;
        Textures.Swapped += ArtSwapped;
        SetProcess(true);
    }

    /// <summary>
    /// The frames LoadArt makes (wheel, arms, shots), as stems: a mission's build prefetches them on its thread (Main.PrefetchCars), with
    /// the radar's (RadarFrames.Prefetch), which are kept in their own form.
    /// </summary>
    public static IEnumerable<string> ArtStems(int lightVariant) =>
        new[] { ("zswls{0}{1:00}", 36), ("zhr45{0}{1:00}", 6), ("zhl45{0}{1:00}", 6), ("zsr45{0}{1:00}", 3), ("zsl45{0}{1:00}", 3) }
            .SelectMany(f => Enumerable.Range(1, f.Item2).Select(i => string.Format(f.Item1, lightVariant, i))).Append("zradmask");

    /// <summary>The frames the cockpit swaps in (wheel, arms, radar), from what the factory serves: the originals or a pack's upscales.</summary>
    private void LoadArt()
    {
        foreach (var _ in LoadArtSteps()) { }
    }

    /// <summary><see cref="LoadArt"/> as steps, a null between the costly parts.</summary>
    private IEnumerable<object?> LoadArtSteps(bool prefetched = false)
    {
        if (!prefetched) PrefetchArt();
        // their pictures read and their mipmaps made on a worker, then sent to the card a few at a time (a mission attaching under its briefing)
        foreach (var step in FramesSteps("zswls{0}{1:00}", 36, f => _wheelFrames = f)) yield return step;
        I76.Game.World.MissionBuilder.Trace("cockpit: wheel frames");
        yield return null;
        foreach (var step in FramesSteps("zhr45{0}{1:00}", 6, f => _armR = f)) yield return step;
        foreach (var step in FramesSteps("zhl45{0}{1:00}", 6, f => _armL = f)) yield return step;
        foreach (var step in FramesSteps("zsr45{0}{1:00}", 3, f => _shotR = f)) yield return step;
        foreach (var step in FramesSteps("zsl45{0}{1:00}", 3, f => _shotL = f)) yield return step;
        I76.Game.World.MissionBuilder.Trace("cockpit: arms");
        yield return null;
        _radarFrames = RadarFrames.Take(Textures, Resolver);
        I76.Game.World.MissionBuilder.Trace("cockpit: radar");
        yield return null;
        OriginalArt();
        I76.Game.World.MissionBuilder.Trace("cockpit: original art");
    }

    /// <summary>
    /// A pack's upscales of the frames LoadArt makes, read together on worker threads (done already when the build prefetched them); a
    /// .dds goes to the GPU as stored, so none is decompressed.
    /// </summary>
    private void PrefetchArt() => Textures.Prefetch(ArtStems(LightVariant).Select(Resolver.FindByStem).OfType<string>());

    /// <summary>What LoadArt reads of the originals, whatever is served (the radar's mask and its measure).</summary>
    private void OriginalArt()
    {
        _radarMember = Resolver.FindByStem("zradf000");
        _radarDiffs.Clear();
        if (_radarMember is not null && Textures.GetOriginalRgba(_radarMember) is { } r0) _radarBaseW = r0.Width;
        // the weapon camera is composed at the original's 256x128 under the original's mask
        _radarMask = Resolver.FindByStem("zradmask") is { } rm && Textures.GetOriginalRgba(rm) is { } mo ? Godot.Image.CreateFromData(mo.Width, mo.Height, false, Godot.Image.Format.Rgba8, mo.Rgba) : null;
        if (Resolver.FindByStem("zradmask") is { } mm && Resolver.Load(mm) is { } mt && mt.Width == 256) _radarClear = I76.Sim.Weapons.RadarScope.WeaponCameraClear(mt.Indices);
    }

    /// <summary>
    /// The HD Textures row or a flip of the look changed what the factory serves: the panels, the radar's and the wheel's
    /// frames are made again at its scale (the 1997 look always the originals) and everything is redrawn. The mirror's art is
    /// the factory's own texture, swapped in place. A change of look calls this twice (the factory's swap, then Faithful): the
    /// work is done once, before the frame is drawn.
    /// </summary>
    private void ArtSwapped()
    {
        if (_artPending) return;
        _artPending = true;
        CallDeferred(MethodName.RedrawArt);
    }
    private bool _artPending;

    /// <summary>
    /// The art for what the factory serves and the look (TextureFactory.KeepBothLooks): kept from the first time each is shown to the
    /// mission's end, so a change back takes it as it was instead of reading and decoding the pack's pictures again.
    /// </summary>
    private sealed record LookArt((string?, ImageTexture?[], bool) Wheel, (string?, ImageTexture?[], bool) ArmR, (string?, ImageTexture?[], bool) ArmL,
        (string?, ImageTexture?[], bool) ShotR, (string?, ImageTexture?[], bool) ShotL, RadarFrames Radar, Godot.Image? MirrorFill, Godot.Image? CompassFill);
    private readonly Assets.LookKept<(bool Hd, bool Faithful), LookArt> _lookArt = new();
    private bool KeepLooks => Textures.KeepBothLooks && Assets.TextureFactory.FastLookSwitch;

    private void RedrawArt()
    {
        _artPending = false;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Panels.KeepLooks = _lookArt.Keep = KeepLooks;
        // without a texture pack the frames, the radar's and the fills are the originals in either look: kept as they are (only the
        // panels follow the look); else the art shown until now kept for a change back, and this look's if it was shown before
        bool same = !Assets.HdTextures.Installed;
        LookArt? a = null;
        bool had = same || _lookArt.Change((Textures.Hd, _faithful), () => new LookArt(_wheelFrames, _armR, _armL, _shotR, _shotL, _radarFrames, _mirrorFillImage, _compassFillImage), out a);
        bool panelsKept = Panels.Holds;
        if (!panelsKept) Panels.Prefetch();
        bool restored = Panels.Reload();
        double panelsMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (same) { }
        else if (had)
        {
            (_wheelFrames, _armR, _armL, _shotR, _shotL, _radarFrames) = (a!.Wheel, a.ArmR, a.ArmL, a.ShotR, a.ShotL, a.Radar);
            OriginalArt();
            MirrorFill(a.MirrorFill);
            CompassOutline(a.CompassFill);
        }
        else
        {
            LoadArt();
            MirrorFill();
            CompassOutline();
        }
        // panels kept show as their look left them: redrawn one group a frame over the next four (each 8x group is 5-17 ms at
        // 3440x1440, all at once 40-60 ms in the first frame); made again, they are bare and all are redrawn in the next frame
        if (restored) _repaintStep = 1;
        else
        {
            ResetPanels();
            _repaintWeapons = _repaintSpecials = _radarRedraw = true;
        }
        _wheelShown = -1;
        _armShown[0] = _armShown[1] = (-1, -1);
        GD.Print($"cockpit art: panels {(panelsKept ? "kept" : "made")} in {panelsMs:0} ms, frames and radar {(had ? "kept" : "made")}, all in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
        _artRedrawnAt = System.Diagnostics.Stopwatch.GetTimestamp();
    }
    /// <summary>When the art was last made again (the next frame's panels repaint is timed for the log).</summary>
    private long _artRedrawnAt;

    /// <summary>The kept panels' redraw after a change of look, a group a frame (0: none under way).</summary>
    private int _repaintStep;

    private void RepaintStep()
    {
        switch (_repaintStep++)
        {
            case 1: _gear = -1; _compass = float.NaN; System.Array.Fill(_parts, -1); _speedFrame = _tachFrame = -1; break;
            case 2: _specRows = null; _repaintSpecials = true; break;
            case 3: _radarRedraw = true; break;
            default: _repaintWeapons = true; _repaintStep = 0; break;
        }
    }

    /// <summary>
    /// The counters' digit anchors sit a pixel right of the drums the housings paint (their own 0000 is drawn a pixel to
    /// the left): the original stamps the 8-wide strip there, its white right edge over the drum's dark divider. The
    /// modern look draws it on the housing's drum; the 1997 look at the anchor, as the original.
    /// </summary>
    public static int DigitShift(bool faithful) => faithful ? 0 : -1;
    private int DigitNudge => DigitShift(_faithful);

    /// <summary>The weapons and specials panels redraw on their next update whatever moved (their rows and housings kept).</summary>
    private bool _repaintWeapons, _repaintSpecials;

    public override void _ExitTree()
    {
        Controls.LookingBack = null;
        Textures.Swapped -= ArtSwapped;
        // the other look's frames kept for F12 go with the mission (the materials hold the shown look's)
        foreach (var art in _lookArt.Release())
            foreach (var set in new[] { art.Wheel, art.ArmR, art.ArmL, art.ShotR, art.ShotL })
                foreach (var t in set.Item2) t?.Dispose();
        Panels.Free();
        _sweepGpu?.Dispose();
        _sweepGpu = null;
        _radarLive?.Free();
        foreach (var d in _depth) d?.Free();
    }

    /// <summary>The head looks back, in the car: the rear glance, or the look stick pushed round to the rear (the look-back rules' context, PadRules).</summary>
    public bool LookingBack => Active && (_state >= 6 || _pose == FreeLook.Pose.Rear);

    /// <summary>
    /// A strip's frames as textures. A pack's frames (wider than the original's) have their mipmaps and are read filtered in the modern
    /// look (MaterialLibrary.OverrideTexture filtered, object.gdshaderinc albedo_frames): point-sampled from the full-size level, the
    /// 8x wheel's rim and the arms' edges stepped and shimmered as they turned. A pack's .dds goes as stored, block-compressed with its
    /// own mipmaps (TextureFactory.UpscaleImage): decompressed to RGBA8 with mipmaps made here, an 8x frame took four times the video
    /// memory. The originals stay point-sampled, as the rest of the art.
    /// </summary>
    /// <summary>
    /// <see cref="Frames"/> as steps: the pictures read and mipmapped on a worker (a Task to wait for: no texture made there), then
    /// made textures here a few milliseconds at a time, a <c>null</c> between; <paramref name="done"/> takes the result.
    /// </summary>
    private IEnumerable<object?> FramesSteps(string pattern, int n, System.Action<(string? Member, ImageTexture?[] Tex, bool Filtered)> done)
    {
        (string? Member, Image?[] Imgs, bool Filtered) read = default;
        var job = I76.Game.Boot.Beside.Run(() => read = FrameImages(pattern, n));
        yield return job;
        job.GetAwaiter().GetResult();
        var tex = new ImageTexture?[n + 1];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= n; i++)
        {
            if (read.Imgs[i] is { } img) tex[i] = ImageTexture.CreateFromImage(img);
            if (sw.Elapsed.TotalMilliseconds > 6 && i < n) { yield return null; sw.Restart(); }
        }
        done((read.Member, tex, read.Filtered));
    }

    private (string? Member, ImageTexture?[] Tex, bool Filtered) Frames(string pattern, int n)
    {
        var (member, imgs, filtered) = FrameImages(pattern, n);
        var tex = new ImageTexture?[n + 1];
        for (int i = 1; i <= n; i++) if (imgs[i] is { } img) tex[i] = ImageTexture.CreateFromImage(img);
        return (member, tex, filtered);
    }

    /// <summary>The frames' pictures (a pack's upscale as stored, else the original), mipmapped when a pack's are larger: no engine resource made.</summary>
    private (string? Member, Image?[] Imgs, bool Filtered) FrameImages(string pattern, int n)
    {
        var member = Resolver.FindByStem(string.Format(pattern, 1, 1));
        int baseW = member is not null && Textures.GetOriginalRgba(member) is { } o ? o.Width : int.MaxValue;
        var imgs = new Image?[n + 1];
        var stored = new bool[n + 1];
        for (int i = 1; i <= n; i++)
        {
            string stem = string.Format(pattern, LightVariant, i);
            if (Resolver.FindByStem(stem) is { } m && Textures.UpscaleImage(m) is { } up) { imgs[i] = up; stored[i] = true; }
            else imgs[i] = Image(stem);
        }
        // I76_FRAMES_POINT=1: a pack's frames point-sampled as before (a capture's before | after)
        bool filtered = System.Environment.GetEnvironmentVariable("I76_FRAMES_POINT") != "1" && imgs.Any(im => im is not null && im.GetWidth() > baseW);
        if (filtered) System.Threading.Tasks.Parallel.For(1, n + 1, i => { if (!stored[i]) imgs[i]?.GenerateMipmaps(); });
        return (member, imgs, filtered);
    }

    private Image? Image(string stem) =>
        Resolver.FindByStem(stem) is { } m && Textures.GetRgba(m) is { } t ? Godot.Image.CreateFromData(t.Width, t.Height, false, Godot.Image.Format.Rgba8, t.Rgba) : null;

    private void Sound(string name)
    {
        if (Audio?.Get(name) is { } s) Node3D.OneShot(s, bus: I76.Game.Boot.AudioMix.BusFor(name));
    }

    /// <summary>
    /// I76_PANELS_CHECK=1 (or quit: the game quits after it): the first frame's panels as the workers drew them compared byte for
    /// byte with the same panels drawn again here on the main thread, the way play draws them; one line says which differ.
    /// </summary>
    private static readonly string? PanelsCheck = System.Environment.GetEnvironmentVariable("I76_PANELS_CHECK") is { Length: > 0 } pc && pc != "0" ? pc : null;

    private void CheckPanels(bool pit)
    {
        var names = new[] { "zgear101.map", "zcm_.map", "zsy_.map", "zbks_.map", "zbk_.map", "zspeedo6.map", "ztach6.map" };
        var worker = names.ToDictionary(n => n, n => Panels.WorkBytes(n));
        var specRows = _specRows;   // (the housings chosen at random on the first draw kept, as the weapons' are)
        ResetPanels();
        _specRows = specRows;
        _repaintWeapons = _repaintSpecials = true;
        Gear(); Compass(); DamagePanel(); SpecialsPanel();
        if (!pit || !Node3D.Dash3D && !Binoculars) Gauges();
        WeaponsPanel();
        var drawn = names.Where(n => worker[n] is not null).ToList();
        var differ = drawn.Where(n => Panels.WorkBytes(n) is not { } m || !m.AsSpan().SequenceEqual(worker[n])).ToList();
        I76.Game.Boot.RunLog.Print($"panels check: {Car.Definition} {(_faithful ? "1997" : "modern")} look: {drawn.Count} panels drawn on workers, "
            + (differ.Count == 0 ? "all identical to the main thread's draw" : $"DIFFERENT: {string.Join(", ", differ)}") + $" ({string.Join(", ", drawn)})");
        I76.Game.Boot.RunLog.Flush();
        if (PanelsCheck == "quit") GetTree().Quit();
    }

    /// <summary>The panels the cockpit's first frame draws whatever the view (FirstFrameSteps).</summary>
    private static readonly string[] FirstPanels = ["zgear101.map", "zcm_.map", "zsy_.map", "zbks_.map", "zbk_.map"];

    /// <summary>
    /// The cockpit's first frame a part at a time, a <c>null</c> between (the effects rehearsal under a playing briefing, Main): its
    /// panels drawn and sent to the card, which in one frame was most of a second; the first <see cref="_Process"/> after it finds them made.
    /// </summary>
    public IEnumerable<object?> FirstFrameSteps()
    {
        bool pit = Active;
        if (!pit && Instruments?.Invoke() != true) yield break;
        // the panels' pictures and sheets made ahead on a worker (their first draws read them)
        // (the panels the draws below draw: the gauges' only where Gauges runs)
        string[] first = !pit || !Node3D.Dash3D && !Binoculars ? [.. FirstPanels, "zspeedo6.map", "ztach6.map"] : FirstPanels;
        var prepared = I76.Game.Boot.Beside.Run(() => Panels.Prepare(first));
        I76.Game.World.MissionBuilder.Trace("cockpit first frame: preparing");
        yield return prepared;
        prepared.GetAwaiter().GetResult();
        foreach (var step in Panels.MakeTargetsSteps()) yield return step;
        ResetPanels();
        _wasShown = true;
        I76.Game.World.MissionBuilder.Trace("cockpit first frame: panels reset");
        yield return null;
        // the panels' draws (the pictures' bytes stamped, their labels lettered: no engine call once their textures are made) on a
        // worker while the main thread waits; one whose texture is not made yet is drawn here, as in play
        IEnumerable<object?> Draw(System.Action draw, params string[] panels)
        {
            if (!panels.All(Panels.Made)) { draw(); yield break; }
            var job = I76.Game.Boot.Beside.Run(draw);
            yield return job;
            job.GetAwaiter().GetResult();
        }
        Radar();
        yield return null;
        // the rest in one go on a worker (one wait for all of them, not one a panel); a panel whose texture is not made is drawn here
        bool gauges = !pit || !Node3D.Dash3D && !Binoculars;
        foreach (var step in Draw(() => { Gear(); Compass(); DamagePanel(); SpecialsPanel(); if (gauges) Gauges(); WeaponsPanel(); },
            gauges ? ["zgear101.map", "zcm_.map", "zsy_.map", "zbks_.map", "zbk_.map", "zspeedo6.map", "ztach6.map"] : ["zgear101.map", "zcm_.map", "zsy_.map", "zbks_.map", "zbk_.map"]))
            yield return step;
        if (PanelsCheck is not null) CheckPanels(pit);
        I76.Game.World.MissionBuilder.Trace("cockpit first frame: drawn");
        yield return null;
        Panels.Commit();
        I76.Game.World.MissionBuilder.Trace("cockpit first frame: committed");
        yield return null;
    }

    public override void _Process(double delta)
    {
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.Cockpit);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        DashStats.Begin();
        float dt = (float)System.Math.Clamp(delta, 0.001, 0.2);
        _steps = SimClock.Take(ref _lastTick);   // the ammo digits and the binocular glance step once a frame of the original: a sim tick
        bool pit = Active;
        Zoom(dt);
        if (!pit && Binoculars) SetBinoculars(false);   // any view but the binoculars: the ring is the ordinary one again (the camera view selection)
        // the draw returns at once unless hud || hud2d: in the car, or with the external instruments on
        bool shown = pit || Instruments?.Invoke() == true;
        if (!pit)
        {
            _wantArm[0] = _wantArm[1] = false;
            if (_mouseHead.HasOffset || _freeLooking)
            {
                _mouseHead.Reset();
                _freeLooking = false;
                _yaw = 0;
                _pitch = -13 * Mathf.Pi / 180;
                _roll = 0;
            }
            if (_mirrorView is not null) _mirrorView.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            MirrorsUnseen();
        }
        if (_repaintStep > 0) RepaintStep();
        if (shown && !_wasShown) ResetPanels();
        _wasShown = shown;
        if (pit) Head(dt);
        else HeadTracker.Step(dt, true);   // out of the car the tracker stands aside (and its timeout runs on)
        if (shown)
        {
            Gear();
            Compass();
            DamagePanel();
            SpecialsPanel();
            using (I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.CockpitRadar)) Radar();
            // the 2D speedometer and tachometer are drawn by the 2D dash and the external instruments, never over the 3D dash
            // or in the binoculars: drawn when they show, from where they were
            if (!pit || !Node3D.Dash3D && !Binoculars) Gauges();
        }
        WeaponsPanel();
        if (pit)
        {
            Wheel(dt);
            Arms();
            Needles();
            using (I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.CockpitMirror)) Mirror();
        }
        // the dash in the eye's frame, the eye and the needles moved for the frame; once more on leaving the car, to let it go
        if (pit || _pinned) Node3D.PinCockpit(pit);
        _pinned = pit;
        using (I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.CockpitPanels)) Panels.Commit();
        DashStats.End();
        if (_artRedrawnAt != 0)
        {
            GD.Print($"cockpit art: the next frame's panels drawn in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
            _artRedrawnAt = 0;
        }
    }

    // ---- binoculars ----

    /// <summary>Camera mode 2: the cockpit eye at a fixed 8x zoom, a level head and its own glances; the hood's gunsight ring is the fine one (zretc_6), the low-resolution one (zretc_1) otherwise.</summary>
    public bool Binoculars { get; private set; }

    /// <summary>The camera's zoom (clamped to [0.5, 8]).</summary>
    public float CameraZoom => Mathf.Clamp(ZoomValue, 0.5f, 8f);

    /// <summary>The zoom_factor axis (8..10): PgDn/PgUp move it, End resets it; the camera clamps it back to 8, so it never shows.</summary>
    public float ZoomValue => (_zoomPos + 0x90000) / 65536f;
    private int _zoomPos = -0x10000, _zoomVel;
    private int _binoYaw;
    private float _binoAcc;
    private int _steps, _lastTick = SimClock.Now;
    private bool _pinned;

    private void Zoom(float dt)
    {
        // held every frame in every view: a ramp of trunc(182 dt)*3 << 6 to +-0x5555, zeroed on release or reversal
        int dir = (Controls.Down("zoom_factor_plus") ? 1 : 0) - (Controls.Down("zoom_factor_minus") ? 1 : 0);
        if (dir == 0 || System.Math.Sign(_zoomVel) == -dir) _zoomVel = 0;
        if (dir != 0) _zoomVel = System.Math.Clamp(_zoomVel + dir * (((int)(182 * dt) * 3) << 6), -0x5555, 0x5555);
        _zoomPos = System.Math.Clamp(_zoomPos + _zoomVel, -0x10000, 0x10000);
        if (Controls.Down("zoom_factor_reset")) _zoomPos = -0x10000;
    }

    /// <summary>B from the cockpit, or B / F1 / V back: the head snaps (to 0, 0, 0 in the binoculars, to the forward state out of them) and the ring swaps.</summary>
    public void SetBinoculars(bool on)
    {
        if (Binoculars == on) return;
        Binoculars = on;
        _binoYaw = 0; _binoAcc = 0;
        _yaw = 0; _roll = 0; _pitch = on ? 0 : -13 * Mathf.Pi / 180;
        _state = 2;
        if (Node3D.Rtc1 is { } r1) r1.Visible = !on;
        if (Node3D.Rtc6 is { } r6) r6.Visible = on;
        if (!on && Node3D.CockpitCamera is { } cam) { cam.Basis = Basis.FromEuler(new Vector3(-_pitch, 0, 0), EulerOrder.Yxz); cam.Fov = I76.Sim.Views.ViewFov.Main; }
    }

    /// <summary>The glance in mode 2, per frame (a sim tick; 30 a second on the original's reference machine): yaw in integer degrees; pitch and roll stay 0.</summary>
    private int BinoGlance(float dt)
    {
        bool l = Glance("pilot_glance_left", FreeLook.Pose.Left), r = Glance("pilot_glance_right", FreeLook.Pose.Right), down = Glance("pilot_glance_down", FreeLook.Pose.Dash);
        _binoAcc += _steps;
        for (; _binoAcc >= 1; _binoAcc -= 1)
        {
            if (l && !r) _binoYaw = down ? -45 : _binoYaw > -88 ? _binoYaw - 29 : _binoYaw;
            else if (r && !l) _binoYaw = down ? 45 : _binoYaw < 93 ? _binoYaw + 31 : _binoYaw;
            else _binoYaw = 0;
        }
        return _binoYaw;
    }

    private bool _wasShown;
    private System.Random _rng = null!;

    /// <summary>Redraw everything the next update (the weapons panel, updated in every view, keeps its rows and housings).</summary>
    private void ResetPanels()
    {
        _gear = -1; _compass = float.NaN; System.Array.Fill(_parts, -1); _specRows = null; _speedFrame = _tachFrame = -1;
    }

    // ---- head ----

    private static readonly (float Yaw, float Pitch, float Roll)[] HeadStates =
        [(-88, -3, -2), (-45, 0, -2), (0, -13, 0), (0, 6, 0), (93, -3, 2), (45, 0, 2), (160, -2, 2), (160, 0, 2)];
    // rows: from-state pair; columns: L, L+down, none, down, R, R+down, up
    private static readonly int[][] Transitions = [[0, 1, 2, 3, 2, 3, 2], [0, 1, 2, 3, 4, 5, 4], [2, 3, 2, 3, 4, 5, 6], [4, 5, 4, 5, 4, 5, 6]];
    private int _state = 2;
    private float _yaw, _pitch = -13 * Mathf.Pi / 180, _roll;

    /// <summary>Whether the head is turned (the 2D panels and the gun are off while it is).</summary>
    public bool LookingAway => _state != 2 || _freeLooking;

    // the port's free look on the pad's look stick (FreeLook) and mouse (MouseCockpitOffset)
    private readonly FreeLook _look = new();
    private readonly I76.Sim.Views.MouseCockpitOffset _mouseHead = new();
    private FreeLook.Pose _pose;
    private bool _freeLooking;

    /// <summary>A glance key (or the port's pistol key on that side) held, the look stick pushed hard into its pose, or the head tracker's digital mode turned past it.</summary>
    private bool Glance(string command, FreeLook.Pose pose) => Controls.Down(command) || I76.Formats.Input.PistolKeys.Of(command) is { } pistol && Controls.Down(pistol) || _pose == pose
        || pose switch { FreeLook.Pose.Left => _tracked.Left, FreeLook.Pose.Right => _tracked.Right, FreeLook.Pose.Dash => _tracked.Down, _ => false };

    // the head tracker (HeadLook): digital's glances this frame, and the head as drawn (the glance machine's blended with analog's)
    private (bool Left, bool Right, bool Down) _tracked;
    private float _viewYaw, _viewPitch = -13 * Mathf.Pi / 180, _viewRoll;

    // ---- the 2D dash (dash3d == 0): what Hud draws over the driver's view ----

    /// <summary>The 3D dash is on; off, the interior model is hidden and the panels are drawn flat (ExtInstruments).</summary>
    public bool Dash3D => Node3D.Dash3D;

    private Texture2D? _reticle;

    /// <summary>The 32x32 gunsight reticle texture (<c>zretc_1</c>) from the game's archive.</summary>
    public Texture2D? ReticleTexture => _reticle ??= Image("zretc_1") is { } img ? ImageTexture.CreateFromImage(img) : null;

    /// <summary>
    /// The reticle zretc_1 (32x32) the 2D dash draws, with its top-left on the 640x480 frame: the side reticle at (300, 240) when
    /// an arm is raised or busy and the head has turned to its window (yaw beyond 88° right or -83° left), else the forward gunsight at
    /// (306, 300) with both arms idle and the head within 4° of straight ahead. Not in the binoculars. Those are the places for the
    /// original's view; with another Field of View the gun line projects elsewhere, so the sprite's centre moves with it.
    /// </summary>
    public (Texture2D Tex, float X, float Y)? Reticle()
    {
        if (Binoculars) return null;
        if (ReticleTexture is not { } tex) return null;
        bool Up(int ch) => Car.Arms[ch].Busy || Car.Arms.Ready(ch);
        (Texture2D, float, float) At(int x, int y)
        {
            var (cx, cy) = I76.Sim.Views.ViewFov.ScreenPoint(x + 16, y + 16, I76.Sim.Views.ViewFov.Main);
            return (tex, cx - 16, cy - 16);
        }
        if ((Up(1) && _yaw > 1.5359f) || (Up(2) && _yaw < -1.4486f)) return At(300, 240);
        if (Car.Arms.Idle(1) && Car.Arms.Idle(2) && MathF.Abs(_yaw) < 0.0698f) return At(306, 300);
        return null;
    }

    private Texture2D? _mirrorArt;
    /// <summary>The modern look's smooth outline (MirrorEdge): the masks' field, and the art with its frame colours spread.</summary>
    private ImageTexture? _mirrorEdge, _mirrorFill;
    /// <summary>The mirror as the 2D dash shows it (slot 7): its live view, its art and the smooth outline's two pictures, or null with the mirror off.</summary>
    public (Texture2D View, Texture2D Art, Texture2D? Edge, Texture2D? Fill)? MirrorFace => _mirrorView is not null && _mirrorMode != 0 && _mirrorArt is not null ? (_mirrorView.GetTexture(), _mirrorArt, _mirrorEdge, _mirrorFill) : null;
    /// <summary>The view's columns the rear mirror shows: the whole picture, or its middle band with the side-view mirrors on (MirrorBands), for the 2D dash's slot 7.</summary>
    public Vector2 MirrorBand { get; private set; } = new(0, 1);

    /// <summary>The pilot's glance as it stands (yaw, pitch down-positive, roll; radians): what the F6 view reads.</summary>
    public (float Yaw, float Pitch, float Roll) HeadAngles => (_viewYaw, _viewPitch, _viewRoll);

    private void Head(float dt)
    {
        var (sx, sy) = Controls.LookStick;
        _pose = _look.Update(sx, sy);
        _freeLooking = false;
        // a glance key or the look stick wins over the head tracker while held
        bool own = Controls.Down("pilot_glance_left") || Controls.Down("pilot_glance_right") || Controls.Down("pilot_glance_up") || Controls.Down("pilot_glance_down")
            || Controls.Down("pilot_glance_target") || Controls.Down(I76.Formats.Input.PistolKeys.Left) || Controls.Down(I76.Formats.Input.PistolKeys.Right)
            || _pose != FreeLook.Pose.Free || FreeLook.Head(sx, sy) is not null;
        var track = HeadTracker.Step(dt, own || Binoculars || !I76.Formats.Input.DashToggle.FreeLookAllowed(Node3D.Dash3D));
        _tracked = HeadTracker.Look.Glances(own);
        if (Binoculars) { BinoHead(dt); return; }
        bool l = Glance("pilot_glance_left", FreeLook.Pose.Left);
        bool r = Glance("pilot_glance_right", FreeLook.Pose.Right);
        bool up = Controls.Down("pilot_glance_up");       // towards the back (the stick's rear look is aimed below, over the shoulder on its side)
        bool down = Glance("pilot_glance_down", FreeLook.Pose.Dash);   // forward, tipped 6° down (the dash and the wheel)
        int col = (l, r, up, down) switch
        {
            (true, false, false, false) => 0,
            (true, false, false, true) => 1,
            (false, false, false, false) => 2,
            (false, false, false, true) => 3,
            (false, true, false, false) => 4,
            (false, true, false, true) => 5,
            (false, false, true, false) => 6,
            _ => -1,
        };
        if (col >= 0) _state = Transitions[_state / 2][col];
        // capture hook: I76_HEAD_STATE=N holds the glance state (4 looks out of the right window, 0 the left)
        if (int.TryParse(System.Environment.GetEnvironmentVariable("I76_HEAD_STATE"), out int hs) && hs is >= 0 and <= 7) _state = hs;
        var (ty, tp, tr) = HeadStates[_state];
        float yawT = ty * Mathf.Pi / 180, pitchT = tp * Mathf.Pi / 180, rollT = tr * Mathf.Pi / 180;
        // the free look: facing forward with no glance key held, the stick's or mouse's own aim (a little roll into the turn, as the glances lean)
        bool freeAllowed = _state == 2 && col == 2 && I76.Formats.Input.DashToggle.FreeLookAllowed(Node3D.Dash3D);
        var fh = freeAllowed ? FreeLook.Head(sx, sy) : null;
        if (MouseCamera.CockpitActive && freeAllowed)
        {
            var (my, mp) = MouseCamera.Take();
            _mouseHead.Step(dt, MouseCamera.ReturnBehind, fh is not null || Controls.Down("pilot_glance_target"), my, mp);
        }
        else
        {
            if (MouseCamera.CockpitActive) MouseCamera.Take();
            _mouseHead.Reset();
        }
        if (fh is { } stickHead)
        {
            yawT = stickHead.Yaw * Mathf.Pi / 180;
            pitchT = stickHead.Pitch * Mathf.Pi / 180;
            rollT = System.Math.Clamp(stickHead.Yaw / 90f, -1f, 1f) * 2 * Mathf.Pi / 180;
            _freeLooking = MathF.Abs(stickHead.Yaw) > 15 || MathF.Abs(stickHead.Pitch - FreeLook.Rest) > 10;
        }
        else if (_mouseHead.HasOffset)
        {
            yawT += _mouseHead.Yaw;
            pitchT += _mouseHead.Pitch;
            rollT = System.Math.Clamp(yawT / (Mathf.Pi / 2f), -1f, 1f) * 2 * Mathf.Pi / 180;
            _freeLooking = MathF.Abs(_mouseHead.Yaw) > 15 * Mathf.Pi / 180 || MathF.Abs(_mouseHead.Pitch) > 10 * Mathf.Pi / 180;
        }
        // capture hook: I76_HEAD=yaw,pitch in the game's degrees, held throughout or (yaw,pitch@from-to) over those frames drawn, the
        // head easing to it and back as a glance does
        if (HeadHook is { } hh && (hh.From < 0 || Godot.Engine.GetFramesDrawn() is var fd && fd >= hh.From && fd <= hh.To))
            (yawT, pitchT) = (hh.Yaw * Mathf.Pi / 180, hh.Pitch * Mathf.Pi / 180);
        // the stick pushed all the way round: the look rear straight over that shoulder (the original's glance machine would swing a left
        // look forward and across to its one rear pose over the right shoulder, a step a tick)
        if (_pose == FreeLook.Pose.Rear)
        {
            float side = _look.RearLeft ? -1 : 1;
            yawT = side * 160 * Mathf.Pi / 180;
            pitchT = -2 * Mathf.Pi / 180;
            rollT = side * 2 * Mathf.Pi / 180;
            _freeLooking = true;
        }
        // glance at the target while pilot_glance_target is held (bearing within −130..160°, elevation ±16°)
        if (Controls.Down("pilot_glance_target") && Combat?.RadarTarget.GetValueOrDefault(Car) is { } t)
        {
            var d = t.Pos.ToVector3() - Car.Pos.ToVector3();
            var f = Car.Rot.Forward;
            float b = Mathf.Wrap(Mathf.Atan2(d.X, d.Z) - Mathf.Atan2(f.X, f.Z), -Mathf.Pi, Mathf.Pi);
            if (b > -130 * Mathf.Pi / 180 && b < 160 * Mathf.Pi / 180) yawT = b;
            float e = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
            pitchT = -Mathf.Clamp(e, -16 * Mathf.Pi / 180, 16 * Mathf.Pi / 180);   // game pitch is positive downwards
        }
        float k = (_mouseHead.HasOffset ? 18 : 4) * dt;
        float dy = Mathf.Wrap(yawT - _yaw, -Mathf.Pi, Mathf.Pi);
        if (MathF.Abs(dy) >= 0.005f) _yaw += MathF.Min(1f, k) * dy;
        _pitch = k <= 0.95f && MathF.Abs(pitchT - _pitch) >= 0.005f ? _pitch + k * (pitchT - _pitch) : pitchT;
        _roll = k <= 0.95f && MathF.Abs(rollT - _roll) >= 0.005f ? _roll + k * (rollT - _roll) : rollT;
        // the head tracker's analog head, followed as it comes (the tracker smooths it) and blended over the glance machine's
        float w = track.Weight, d2r = Mathf.Pi / 180;
        _viewYaw = _yaw + w * Mathf.Wrap(track.Yaw * d2r - _yaw, -Mathf.Pi, Mathf.Pi);
        _viewPitch = _pitch + w * (track.Pitch * d2r - _pitch);
        _viewRoll = _roll + w * (track.Roll * d2r - _roll);
        if (w > 0.5f && (MathF.Abs(track.Yaw) > 15 || MathF.Abs(track.Pitch - FreeLook.Rest) > 10)) _freeLooking = true;
        if (Node3D.CockpitCamera is { } cam)
        {
            // game yaw is to the right and Godot's Z is mirrored: a right turn is a negative Y rotation here
            cam.Basis = Sway(dt) * Basis.FromEuler(new Vector3(-_viewPitch, -_viewYaw, _viewRoll), EulerOrder.Yxz);   // the Z mirror negates pitch and yaw, not roll
            cam.Position = Node3D.Eye + new Vector3(track.Offset.X + EyeHook.X, track.Offset.Y + EyeHook.Y, -(track.Offset.Z + EyeHook.Z));
            cam.Fov = I76.Sim.Views.ViewFov.Main;
        }
        // arms rise past ±58° / 53° to the side windows (the handguns): the sim moves them with the next tick
        _wantArm[0] = (_state == 4 || _mouseHead.HasOffset) && _yaw > 1.0123f;
        _wantArm[1] = (_state == 0 || _mouseHead.HasOffset) && _yaw < -0.9250f;
    }

    private static readonly (float Yaw, float Pitch, long From, long To)? HeadHook = ParseHeadHook(System.Environment.GetEnvironmentVariable("I76_HEAD"));
    /// <summary>I76_EYE_OFFSET=x,y,z: the eye moved that many metres (game axes: right, up, forward) on top of the head tracker's offset, for a capture from a moved head.</summary>
    private static readonly System.Numerics.Vector3 EyeHook = System.Environment.GetEnvironmentVariable("I76_EYE_OFFSET")?.Split(',') is [var ex, var ey, var ez]
        && float.TryParse(ex, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx)
        && float.TryParse(ey, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fy)
        && float.TryParse(ez, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fz) ? new(fx, fy, fz) : default;

    private static (float, float, long, long)? ParseHeadHook(string? v)
    {
        if (v is null) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var at = v.Split('@');
        long from = -1, to = -1;
        if (at.Length == 2 && at[1].Split('-') is [var a, var b] && long.TryParse(a, out from) && long.TryParse(b, out to)) { }
        else if (at.Length != 1) return null;
        return at[0].Split(',') is [var y, var p] && float.TryParse(y, c, out float fy) && float.TryParse(p, c, out float fp) ? (fy, fp, from, to) : null;
    }

    // ---- head sway: the eye tilts with the car's acceleration; view only ----

    private readonly I76.Sim.Views.HeadSway _sway = new();

    /// <summary>The sway stepped one frame, as a rotation in the eye's frame applied after the head turn (Euler · Sway · Eye).</summary>
    private Basis Sway(float dt)
    {
        var rot = Car.Rot;
        _sway.SnapSmall = OriginalBehavior.SwaySnap;
        _sway.Step(Car.Accel, rot.Right, rot.Forward, dt);
        var (r, u, f) = _sway.Basis(Car.P.Track, Car.P.Wheelbase);
        // the game's rows as columns, conjugated by the Z mirror: x and y keep their axes' z negated, z is negated whole
        static Vector3 M(System.Numerics.Vector3 v) => new(v.X, v.Y, -v.Z);
        return new Basis(M(r), M(u), -M(f));
    }

    private void BinoHead(float dt)
    {
        float yawT = BinoGlance(dt) * Mathf.Pi / 180, pitchT = 0;
        if (Controls.Down("pilot_glance_target") && Combat?.RadarTarget.GetValueOrDefault(Car) is { } t)
        {
            var d = t.Pos.ToVector3() - Car.Pos.ToVector3();
            var f = Car.Rot.Forward;
            float b = Mathf.Wrap(Mathf.Atan2(d.X, d.Z) - Mathf.Atan2(f.X, f.Z), -Mathf.Pi, Mathf.Pi);
            if (b > -130 * Mathf.Pi / 180 && b < 160 * Mathf.Pi / 180) yawT = b;
            float e = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
            pitchT = -Mathf.Clamp(e, -16 * Mathf.Pi / 180, 16 * Mathf.Pi / 180);
        }
        float k = 2 * dt;   // the cockpit eases at 4 dt
        float dy = Mathf.Wrap(yawT - _yaw, -Mathf.Pi, Mathf.Pi);
        if (MathF.Abs(dy) >= 0.005f) _yaw += k * dy;
        _pitch = k <= 0.95f && MathF.Abs(pitchT - _pitch) >= 0.005f ? _pitch + k * (pitchT - _pitch) : pitchT;
        _roll = 0;
        (_viewYaw, _viewPitch, _viewRoll) = (_yaw, _pitch, 0);
        _wantArm[0] = _wantArm[1] = false;
        if (Node3D.CockpitCamera is { } cam)
        {
            cam.Basis = Sway(dt) * Basis.FromEuler(new Vector3(-_pitch, -_yaw, 0), EulerOrder.Yxz);
            cam.Position = Node3D.Eye;
            // 8x: tan(h/2) = 1/8 across, so 0.75/8 down on 4:3 (10.71 degrees)
            cam.Fov = Mathf.RadToDeg(2 * MathF.Atan(0.75f / CameraZoom));
        }
    }

    // ---- PRND21 ----

    private int _gear = -1;
    private static readonly string[] GearAnchors = ["park", "reverse", "neutral", "drive", "second", "first"];

    private void Gear()
    {
        int g = Car.DriveState;
        if (g == _gear || g < 0 || g >= GearAnchors.Length) return;
        if (_gear >= 0) Sound("vshif1a.wav");
        int was = _gear;
        _gear = g;
        if (was < 0) { Panels.Clear("zgear101.map"); GearDraw(); return; }
        // the arrow moves: where it was and where it goes drawn again, the rest of the strip left as it is
        foreach (var at in new[] { GearAnchors[was], GearAnchors[g] })
        {
            if (Panels.Footprint("arrow", at) is not { } r) continue;
            Panels.Region("zgear101.map", r);
            GearDraw();
            Panels.EndClip("zgear101.map");
        }
    }

    private void GearDraw()
    {
        Panels.StampAt("prndback", "prnd");
        Panels.StampAt("arrow", GearAnchors[_gear]);
    }

    // ---- compass ----

    private float _compass = float.NaN;

    private void Compass()
    {
        var f = Car.Rot.Forward;
        float deg = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(f.X, f.Z)), 360f);
        if (MathF.Abs(deg - _compass) < 0.5f) return;
        _compass = deg;
        Panels.Window("zcm_.map", "compass_window", "left", "right", deg / 360f);
    }

    /// <summary>
    /// The modern look's smooth compass outline (MirrorEdge.KeyField, KeyFill): the art keys everything round its housing, a
    /// staircase a few screen pixels a step along the diagonal shoulders; its keyed texels inside the outline take the housing's colours. The field from the original art, the fill from the
    /// art served (the originals or a pack's upscale); the 3D compass's materials (object.gdshaderinc) and the 2D dash's slot
    /// (ExtInstruments) cut along it. The 1997 look's shaders key the art's own pixels.
    /// </summary>
    private ImageTexture? _compassEdge, _compassFill;
    /// <summary>The compass's smooth outline's two pictures (the mask's field, the art with its colours spread), or null.</summary>
    public (Texture2D Edge, Texture2D Fill)? CompassEdge => _compassEdge is not null && _compassFill is not null ? (_compassEdge, _compassFill) : null;

    /// <summary>The compass's fill as made for the art served now (kept with a look's art, LookArt).</summary>
    private Godot.Image? _compassFillImage;

    /// <param name="kept">The fill made before for the art served now (a change back to a look shown before), or null to make it.</param>
    private void CompassOutline(Godot.Image? kept = null)
    {
        if (Resolver.FindByStem("zcm_") is not { } member) return;
        if (_compassEdge is null && Textures.GetOriginalRgba(member) is { } o)
            _compassEdge = ImageTexture.CreateFromImage(Godot.Image.CreateFromData(o.Width * MirrorEdge.FieldScale, o.Height * MirrorEdge.FieldScale, false, Godot.Image.Format.R8, MirrorEdge.KeyField(o.Rgba, o.Width, o.Height)));
        if ((kept ?? (Textures.GetRgba(member) is { } a ? Godot.Image.CreateFromData(a.Width, a.Height, false, Godot.Image.Format.Rgba8, MirrorEdge.KeyFill(a.Rgba, a.Width, a.Height, Textures.Scale(member))) : null)) is { } img)
        {
            _compassFillImage = img;
            if (_compassFill is null) _compassFill = ImageTexture.CreateFromImage(img);
            else _compassFill.SetImage(img);
        }
        if (_compassEdge is null || _compassFill is null) return;
        foreach (var m in Materials.MadeFor(member).OfType<ShaderMaterial>())
        {
            m.SetShaderParameter("key_edge", _compassEdge);
            m.SetShaderParameter("key_edge_fill", _compassFill);
            m.SetShaderParameter("smooth_key_fill", true);
            m.SetShaderParameter("smooth_key", true);
        }
    }

    // ---- damage panel ----

    /// <summary>The band each part is drawn in (DamageParts' order), -1 before it is first drawn.</summary>
    private readonly int[] _parts = [.. Enumerable.Repeat(-1, DamageParts.Length)];
    private readonly int[] _partPct = new int[DamageParts.Length];
    private readonly int[] _partWas = new int[DamageParts.Length];

    /// <summary>The damage panel's parts in drawing order: anchor and sprite (the tyres share one).</summary>
    private static readonly (string Anchor, string Sprite)[] DamageParts =
    [
        ("engine", "engine"), ("suspen", "suspen"), ("brakes", "brakes"),
        ("rrtire", "tire"), ("rltire", "tire"), ("frtire", "tire"), ("fltire", "tire"),
        ("farm", "farm"), ("fchas", "fchas"), ("larm", "larm"), ("lchas", "lchas"), ("rarm", "rarm"), ("rchas", "rchas"), ("barm", "barm"), ("bchas", "bchas"),
    ];
    private static readonly int[] TireSlots = [4, 5, 0, 1];

    /// <summary>The panel's five bands: 100 exactly is the unlit plate, then green, yellow, red, and dark at 0.</summary>
    private static int Band(int pct) => pct == 100 ? 0 : pct >= 67 ? 1 : pct >= 34 ? 2 : pct >= 1 ? 3 : pct == 0 ? 4 : -1;
    private static readonly string[] PartSuffix = ["_off", "_grn", "_ylw", "_red", "_drk"];
    /// <summary>I76_DAMAGE_PANEL=0..4: every part of the damage panel shown in that band (unlit, green, yellow, red, dark), whatever the car's damage; for captures of the panel's art.</summary>
    private static readonly int? PanelBand = int.TryParse(System.Environment.GetEnvironmentVariable("I76_DAMAGE_PANEL"), out int b) && b is >= 0 and <= 4 ? b : null;

    private void DamagePanel()
    {
        if (Car.Health is not { } h) return;
        static int Pct(int cur, int max) => max <= 0 ? 0 : (int)(cur * (100f / max) + 0.5f);
        var pct = _partPct;
        pct[0] = h.Engine is { } e ? Pct(e.Hp, e.Max) : 100;
        pct[1] = h.Suspension is { } s ? Pct(s.Hp, s.Max) : 100;
        pct[2] = h.Brake is { } b ? Pct(b.Hp, b.Max) : 100;
        for (int i = 0; i < 4; i++) pct[3 + i] = h.Slots[TireSlots[i]] is { } w ? Pct(w.Hp, w.Max) : 100;
        for (int z = 0; z < 4; z++)
        {
            // a helicopter's hits take its one pool (the helicopter's damage), never the zones, so the original's plates stay unlit
            // however hurt it is; ours show the pool on every armour and chassis plate (docs/deviations.md, #38)
            if (Car.Heli is { } heli) pct[7 + 2 * z] = pct[8 + 2 * z] = Pct(heli.Hp, heli.MaxHp);
            else { pct[7 + 2 * z] = Pct(h.Armour[z], h.ArmourMax[z]); pct[8 + 2 * z] = Pct(h.Chassis[z], h.ChassisMax[z]); }
        }
        bool changed = false, fresh = true;
        for (int i = 0; i < DamageParts.Length; i++)
        {
            _partWas[i] = _parts[i];
            if (_parts[i] >= 0) fresh = false;
            int band = PanelBand ?? Band(pct[i]);
            if (band < 0 || _parts[i] == band) continue;
            if (_parts[i] >= 0 && band == 4) Sound("cwstat.wav");
            _parts[i] = band;
            changed = true;
        }
        if (!changed) return;
        if (fresh) { Panels.Clear("zsy_.map"); DamageDraw(); return; }
        // each part that changed band: its plate's place drawn again (the old plate's and the new one's), every part stamped
        // there in order, the rest of the panel left as it is
        for (int i = 0; i < DamageParts.Length; i++)
        {
            if (_partWas[i] == _parts[i]) continue;
            var (anchor, sprite) = DamageParts[i];
            var r = Panels.Footprint(sprite + PartSuffix[_parts[i]], anchor, up: true);
            if (_partWas[i] >= 0 && Panels.Footprint(sprite + PartSuffix[_partWas[i]], anchor, up: true) is { } old) r = r is { } n ? n.Merge(old) : old;
            if (r is null) continue;
            Panels.Region("zsy_.map", r.Value);
            DamageDraw();
            Panels.EndClip("zsy_.map");
        }
    }

    /// <summary>
    /// Every part's cell drawn a row above its label on the sheet and on the panel, as the original does (measured on its screen):
    /// the art sits a row above the label in the sheet, so drawn as labelled the front chevron lost its point and showed the next
    /// state's point as a bar under it, and every plate and bar lost its top edge.
    /// </summary>
    private void DamageDraw()
    {
        for (int i = 0; i < DamageParts.Length; i++)
            if (_parts[i] >= 0) Panels.StampAt(DamageParts[i].Sprite + PartSuffix[_parts[i]], DamageParts[i].Anchor, up: true);
    }

    // ---- weapons panel ----

    private sealed class Row
    {
        public int Housing, Diode = -1, Value = -1;
        public bool On;
        public int[] Digits = [10, 10, 10, 10];
        /// <summary>The housing colour each counter window read when the panel was last drawn whole (the modern look's drums).</summary>
        public (byte R, byte G, byte B)?[] Housings = new (byte, byte, byte)?[4];
    }
    private Row[]? _rows;
    private int _separator = -1;
    private static readonly string[] Places = ["ones", "tens", "hunds", "thous"];
    private static readonly string[] DiodeSprite = ["off", "green", "yellow", "red", "drk"];

    private static string Res(string name) => name.StartsWith('3') ? name[1..] : name;   // full-size art drops the '3'

    private void WeaponsPanel()
    {
        if (Car.Armory is not { } a) return;
        var ws = a.Weapons;
        int count = System.Math.Min(5, ws.Count);
        bool dirty = false, force = false;
        if (_rows is null || _rows.Length != count)
        {
            // one of five housings per row, chosen at random once
            _rows = new Row[count];
            for (int i = 0; i < count; i++) _rows[i] = new Row { Housing = 1 + _rng.Next(5) };
            int lastFront = -1;
            for (int i = 0; i < count; i++) if (ws[i].MountKind is 1 or 4) lastFront = i;
            _separator = lastFront + 1;
            dirty = force = true;
        }
        int counters = 0;   // the counter digits that rolled, four bits a row
        for (int i = 0; i < count; i++)
        {
            var w = ws[i]; var row = _rows[i];
            bool on = i == a.Selected || a.Group.Contains(i);
            int diode = Band(w.MaxHp <= 0 ? 0 : (int)((float)w.Hp / w.MaxHp * 100));
            if (on != row.On || diode != row.Diode) { row.On = on; row.Diode = diode; dirty = true; }
            counters |= Roll(row.Digits, w.Ammo, _steps, force) << (4 * i);
        }
        if (!dirty && !_repaintWeapons)
        {
            // only counters rolled (every shot of a gun): those digits' windows are drawn again over the panel as it stands, then
            // the separator bar that is stamped over them, held to each window (elsewhere it is on the panel already), which gives
            // the same texels as the whole panel drawn again; only the windows go up
            if (counters == 0) return;
            for (int i = 0; i < count; i++)
                for (int p = 0; p < 4; p++)
                {
                    if ((counters >> (4 * i + p) & 1) == 0) continue;
                    CounterWindow(_rows[i], i + 1, p);
                    if (_separator < 0 || _separator >= 5 || CounterRect(i + 1, p) is not { } r) continue;
                    Panels.ClipTo("zbk_.map", r);
                    Panels.StampAt("sepbar", SeparatorAnchors[_separator]);
                    Panels.EndClip("zbk_.map");
                }
            return;
        }
        _repaintWeapons = false;
        // a plate lit or a diode changed: the panel drawn again, and only what differs from it goes up
        Panels.Clear("zbk_.map", diff: true);
        for (int i = 0; i < count; i++)
        {
            var w = ws[i]; var row = _rows[i]; int n = i + 1;
            Panels.StampAt("housing" + row.Housing, $"bracket_{n}");
            if (row.Diode >= 0) Panels.StampAt(DiodeSprite[row.Diode], $"diode_{n}");
            string plate = Res(row.On ? w.Def.PlateOn : w.Def.PlateOff);
            // the 1997 look: the plate as painted; the modern look: the name embossed in the label font (docs/deviations.md)
            if (_faithful) Panels.StampAt(plate, $"dymo_{n}");
            else Panels.StampLabel(plate, row.On ? "empty_on" : "empty_off", $"dymo_{n}", DymoLabels.Text(plate, w.Def.Name), row.On);
            System.Array.Clear(row.Housings);
            for (int p = 0; p < 4; p++) CounterWindow(row, n, p);
        }
        if (_separator >= 0 && _separator < 5) Panels.StampAt("sepbar", SeparatorAnchors[_separator]);
    }

    private static readonly string[] SeparatorAnchors = ["separator_1", "separator_2", "separator_3", "separator_4", "separator_5"];
    private static readonly string[,] CounterAnchors = new string[5, 4];

    /// <summary>A counter digit's window on the weapons panel (row n, place p), in the layout's pixels, or null.</summary>
    private Rect2I? CounterRect(int n, int p) => Panels.Footprint("top", CounterAnchors[n - 1, p] ??= $"num_{Places[p]}_{n}", DigitNudge);

    /// <summary>One counter digit's window on the weapons panel (row n, place p): the strip's cell, or the modern look's drum.</summary>
    private void CounterWindow(Row row, int n, int p)
    {
        var anchor = CounterAnchors[n - 1, p] ??= $"num_{Places[p]}_{n}";
        if (_faithful) Panels.VWindow("zbk_.map", anchor, "top", "bottom", row.Digits[p] * 0.1f, DigitNudge);
        else row.Housings[p] = Panels.Digit("zbk_.map", anchor, "top", "bottom", row.Digits[p], DigitNudge, row.Housings[p]);
    }

    /// <summary>
    /// The rolling counter: each digit that differs from the value's steps down
    /// by one per update (an increase rolls down through 9), <paramref name="steps"/>
    /// updates; <paramref name="force"/> sets every digit at once. Returns the
    /// digits that moved, a bit a place (ones first).
    /// </summary>
    public static int Roll(int[] shown, int value, int steps, bool force)
    {
        int moved = 0;
        value = System.Math.Clamp(value, 0, 9999);
        for (int step = 0; step < System.Math.Max(steps, force ? 1 : 0); step++)
            for (int p = 0, k = 1; p < 4; p++, k *= 10)
            {
                int d = value / k % 10;
                if (shown[p] == d && !force) continue;
                if (force) shown[p] = d;
                else { shown[p] -= 1; if (shown[p] < 0) shown[p] = 9; }
                moved |= 1 << p;
            }
        return moved;
    }

    // ---- specials panel ----

    private static readonly string[] SpecialNames = ["sp_empty", "radar", "nitrous", "blower", "xaust", "structo", "curb", "mud", "heated", "cuphldr", "dash_mary", "tire_covr"];
    private Row[]? _specRows;

    /// <summary>
    /// The 3D dash's specials face's texel height over width on the car's model (PanelAspect.Of; 1.63 on every car): the panel shows
    /// its 181 x 76 art over a quad of the weapons panel's proportions. The modern look shows it square ([port] specials_panel).
    /// </summary>
    public float SpecialsTexelAspect { get; init; } = 1;
    /// <summary>The lowest v of the specials face (its art's last row, 0.4066 on every car).</summary>
    public float SpecialsMinV { get; init; }
    private bool? _specialsSquare;

    /// <summary>The specials face's texture coordinates for the look and [port] specials_panel (the modern look's shader reads uv_fit).</summary>
    private void FitSpecials()
    {
        bool square = OriginalBehavior.SquareSpecials(_faithful) && MathF.Abs(SpecialsTexelAspect - 1) > 0.01f;
        if (_specialsSquare == square || Resolver.FindByStem("zbks_") is not { } member) return;
        _specialsSquare = square;
        var fit = square ? I76.Formats.Assembly.PanelAspect.SquareFit(SpecialsTexelAspect) : new System.Numerics.Vector4(1, 1, 0, 0);
        foreach (var m in Materials.MadeFor(member))
            if (m is ShaderMaterial sm)
            {
                sm.SetShaderParameter("uv_fit", new Vector4(fit.X, fit.Y, fit.Z, fit.W));
                sm.SetShaderParameter("uv_floor", square ? SpecialsMinV : -1e9f);
            }
    }

    private readonly I76.Sim.Damage.SpecialPart?[] _specs = new I76.Sim.Damage.SpecialPart?[3];

    private void SpecialsPanel()
    {
        FitSpecials();
        if (Car.Health is not { } h) return;
        var specs = _specs;
        for (int i = 0; i < 3; i++) specs[i] = h.Slots[17 + i] as I76.Sim.Damage.SpecialPart;
        bool dirty = false, force = false;
        // one of the weapons panel's five housings per row, chosen at random on the first draw: the olive plate
        // the dymo label and the counters sit on
        if (_specRows is null) { _specRows = [new Row { Housing = 1 + _rng.Next(5) }, new Row { Housing = 1 + _rng.Next(5) }, new Row { Housing = 1 + _rng.Next(5) }]; dirty = force = true; }
        for (int i = 0; i < 3; i++)
        {
            var s = specs[i]; var row = _specRows[i];
            bool on = s is not null && s.Charges >= 1 && s.Unk_0x10 != 0;
            int diode = s is null ? -1 : Band(s.Max <= 0 ? 0 : (int)((float)s.Hp / s.Max * 100));
            if (on != row.On || diode != row.Diode) { row.On = on; row.Diode = diode; dirty = true; }
            if (Roll(row.Digits, s?.Charges ?? 0, _steps, force) != 0) dirty = true;
        }
        if (!dirty && !_repaintSpecials) return;
        _repaintSpecials = false;
        // drawn again whole (a counter's drum reads the housing round it as the panel stands); only what differs goes up
        Panels.Clear("zbks_.map", diff: true);
        for (int i = 0; i < 3; i++)
        {
            var s = specs[i]; var row = _specRows[i]; int n = i + 1;
            Panels.StampAt("housing" + row.Housing, $"sp_bracket_{n}");
            // an empty slot (type 0) keeps the housing's own EMPTY plate and 0000: no plate, diode or counter is drawn
            if (s is null || s.Type == 0)
            {
                // the modern look letters the housing's painted EMPTY as it does the plates
                if (!_faithful) Panels.StampLabel("sp_empty_off", "sp_empty_off", $"sp_dymo_{n}", DymoLabels.Text("sp_empty_off", null), false);
                continue;
            }
            int type = s.Type < 0 || s.Type >= SpecialNames.Length ? 0 : s.Type;
            string plate = SpecialNames[type] + (row.On ? "_on" : "_off");
            if (_faithful) Panels.StampAt(plate, $"sp_dymo_{n}");
            else Panels.StampLabel(plate, row.On ? "sp_empty_on" : "sp_empty_off", $"sp_dymo_{n}", DymoLabels.Text(plate, null), row.On);
            if (row.Diode >= 0) Panels.StampAt(DiodeSprite[row.Diode], $"sp_diode_{n}");
            for (int p = 0; p < 4; p++)
                if (_faithful) Panels.VWindow("zbks_.map", $"sp_num_{Places[p]}_{n}", "top", "bottom", row.Digits[p] * 0.1f, DigitNudge);
                else Panels.Digit("zbks_.map", $"sp_num_{Places[p]}_{n}", "top", "bottom", row.Digits[p], DigitNudge);
        }
    }

    // ---- steering wheel and arms ----

    private (string? Member, ImageTexture?[] Tex, bool Filtered) _wheelFrames, _armR, _armL;
    private float _wheel = 9;
    private int _wheelShown = -1;

    private void Wheel(float dt)
    {
        float s = Mathf.Clamp(Car.Steer, -0.9999f, 0.9999f);
        int tgt = s < 0 ? 9 + (int)(27 * -s) : 9 + (int)(9 * -s);
        float k = 2 * dt;
        _wheel = k > 0.95f || MathF.Abs(tgt - _wheel) < 0.005f ? tgt : _wheel + k * (tgt - _wheel);
        int frame = System.Math.Clamp((int)MathF.Round(_wheel), 1, 36);
        if (frame == _wheelShown || _wheelFrames.Member is null || _wheelFrames.Tex[frame] is not { } t) return;
        _wheelShown = frame;
        // each frame has its own key: the smooth outline of the first frame would cut the others
        Materials.OverrideTexture(_wheelFrames.Member, t, ownKey: true, filtered: _wheelFrames.Filtered);
    }

    private (string? Member, ImageTexture?[] Tex, bool Filtered) _shotR, _shotL;
    private readonly bool[] _wantArm = new bool[2];
    private readonly (int Axis, int Frame)[] _armShown = [(-1, -1), (-1, -1)];

    /// <summary>Whether the glance wants an arm raised to its window (crew channel 1 right, 2 left): the driver's input for the arms (<see cref="Vehicle.Arms"/>).</summary>
    public bool WantArm(int ch) => _wantArm[ch - 1];

    /// <summary>The arms as the sim has them (CrewArms, stepped once a tick): the raise strip's frame, or the shot's.</summary>
    private void Arms()
    {
        for (int i = 0; i < 2; i++)
        {
            var a = Car.Arms[i + 1];
            var (raise, shot) = i == 0 ? (_armR, _shotR) : (_armL, _shotL);
            int frame = System.Math.Clamp(a.Cur, 0, a.Axis == 0 ? 5 : 2) + 1;
            if ((frame, a.Axis) == (_armShown[i].Frame, _armShown[i].Axis) || raise.Member is null) continue;
            var tex = a.Axis == 1 ? shot.Tex[frame] : raise.Tex[frame];
            if (tex is null) continue;
            _armShown[i] = (a.Axis, frame);
            Materials.OverrideTexture(raise.Member, tex, ownKey: true, filtered: a.Axis == 1 ? shot.Filtered : raise.Filtered);
        }
    }

    // ---- radar ----

    private RadarFrames _radarFrames = new();
    private RadarFrames.Frame?[] _radarF => _radarFrames.Sweep;
    private RadarFrames.Frame?[] _radarB => _radarFrames.Locked;
    private string? _radarMember;
    private int _radarCur, _radarPrev;
    private bool _radarRedraw;
    private double _radarNext, _t;
    private readonly Dictionary<Vehicle, double> _sweptAt = [];
    /// <summary>The 256-colour palette the panels' bitmaps use (the radar's blip colours are indices into it).</summary>
    public I76.Formats.Imaging.Palette? Palette { get; init; }
    private bool _ledAny;
    /// <summary>Extra radar blips with their own colour: CTF's flags (class 0x5b, in the team's colour), each as a sim position and a colour. The vehicles' rule of range applies.</summary>
    public Func<IEnumerable<(System.Numerics.Vector3 Pos, Color Colour)>>? Blips { get; set; }
    public int RadarRange => Combat?.RadarRange.GetValueOrDefault(Car, 1) ?? 1;   // 0: 100 m, 1: 600 m: the sim's radar (R flips it)
    /// <summary>Radar +0xc bit 0: on, unless a script's setUserRadar has darkened the player's radar.</summary>
    public bool RadarOn => Combat is null || !(Combat.UserRadarDark && Car == Combat.World.Player);

    private void Radar()
    {
        _sweepSent.Clear();
        _sweepSentWhole = false;
        RadarDraw();
        // Radar Sweep: Smooth draws the beam or the cone on the GPU every frame, over the picture just sent (RadarSmooth)
        if (_sweepDraw != SweepDraw.None) RadarSmooth();
        // the frames that may come next decoded ahead on a worker, once this frame's screen is drawn: the sweep's next step, or the
        // locked frame's neighbours
        else if (_radarAt >= 0) { _radarFrames.Ahead(_radarB[(_radarAt + 1) % 30]); _radarFrames.Ahead(_radarB[(_radarAt + 29) % 30]); }
        else if (_radarAt == -1) _radarFrames.Ahead(_radarF[I76.Sim.Weapons.RadarScope.NextSweep(_radarCur)]);
        if (DashStats.On) DashStats.RadarKept = _radarFrames.Kept;
    }

    /// <summary>The locked frame shown (0..29), -1 sweeping, -2 neither (off, the weapon camera).</summary>
    private int _radarAt = -2;
    /// <summary>The screen kept its frame last time, the one wanted not decoded yet: drawn again as soon as it is.</summary>
    private bool _radarLate;

    private void RadarDraw()
    {
        _t += GetProcessDeltaTime();
        _radarAt = -2;
        _sweepDraw = SweepDraw.None;
        _sweepStepped = false;
        if (_radarMember is null || Combat is null) return;
        // the radar object (class 0x22) is built switched on at range 1 with the cockpit's radar part, special or not
        bool has = RadarOn;
        RadarFrames.Frame? frame;
        if (WeaponCamera() is { } cam) { UploadRadar(cam.GetData(), cam.GetWidth(), cam.GetHeight(), null); _radarDrawn.Forget(); return; }
        // Radar Sweep: Smooth (the modern look, a pack with the sweep's layers): the face is the frame and the GPU draws the beam or
        // the cone over it every frame; the steps, the pings and the blips run as ever. Into or out of it the screen is drawn whole
        var face = has ? SweepFace() : null;
        bool smooth = face is not null;
        if (smooth != _sweepWas) { _radarRedraw = true; _sweepWas = smooth; }
        var locked = has ? Combat.RadarTarget.GetValueOrDefault(Car) : null;
        // a step already drawn with the frame before it (the late frame below): its pings were made then
        bool retry = false;
        if (!has) frame = _radarF[30];
        else if (locked is not null)
        {
            // flag 0x10 (a target selected): the sweep stops and the locked frame ZRADBnnn follows the target's bearing
            // the frame is written into the sweep's current step, the step it left stays where the sweep stopped: the ping
            // window below runs from there to the target's bearing, and the sweep resumes from the target's frame
            var d = Car.Rot.ToLocal(locked.Pos.ToVector3() - Car.Pos.ToVector3());
            _radarAt = I76.Sim.Weapons.RadarScope.LockedFrame(d.X, d.Z);
            _radarCur = _radarAt;   // written into the sweep's step: the ping window runs from where the sweep stopped, which resumes from here
            frame = smooth ? face : _radarB[_radarAt] ?? _radarF[30];
            if (smooth) _sweepDraw = SweepDraw.Cone;
        }
        else
        {
            _radarAt = -1;
            if (smooth) _sweepDraw = SweepDraw.Beam;
            retry = _radarLate && _t < _radarNext && !_radarRedraw;
            if (_t < _radarNext && !_radarRedraw && !_radarLate) { if (!smooth) RadarAhead(); return; }
            if (!retry)
            {
                _radarPrev = _radarCur;
                while (_radarNext < _t) { _radarNext += 0.2; _radarCur = I76.Sim.Weapons.RadarScope.NextSweep(_radarCur); _sweepStepped = true; }
                if (RadarLog && _sweepStepped) GD.Print($"radar: step {_radarCur} at {_t:0.0000} s, frame {Godot.Engine.GetProcessFrames()} ({(smooth ? "smooth" : "stepped")})");
            }
            frame = smooth ? face : _radarF[_radarCur];
        }
        if (frame is null) return;
        bool redraw = _radarRedraw;
        _radarRedraw = false;
        // a frame not decoded ahead (a target picked or changed, the sweep taken up again: a pack's frame, 17 ms of decoding at 8x):
        // the screen keeps the frame it shows, its blips, plate and LED drawn as ever, until the worker has it, a frame or two later
        _radarLate = false;
        if (!redraw && _radarDrawn.Frame is RadarFrames.Frame shown && shown != frame && shown.W == frame.W && shown.H == frame.H && !_radarFrames.Ready(frame))
        {
            _radarFrames.Ahead(frame);
            (frame, _radarLate) = (shown, true);
        }
        var bytes = _radarFrames.Bytes(frame);
        int w = frame.W, h = frame.H;
        // the frames' texels per original pixel (a pack's upscale): the layout's anchors and the scope's are the original's pixels
        int k = System.Math.Max(1, w / _radarBaseW);
        // a newly picked target's byte is 0 (the target pick and the GOG build's E/Q: the one it replaces goes back to 0x69)
        if (locked != _radarPicked)
        {
            if (RadarLog) GD.Print($"radar: {(locked is null ? "target cleared" : "target picked")} at {_t:0.0000} s, frame {Godot.Engine.GetProcessFrames()}");
            if (_radarPicked is not null) _sweptAt.Remove(_radarPicked);
            if (locked is not null) _sweptAt[locked] = _t;
            _radarPicked = locked;
        }
        // what the scope shows: the range plate, the blips (their squares and colours) and the LED. With a target held this runs
        // every frame; the picture is made and uploaded again only when one of them changed
        _radarOps.Clear();
        string? range = null, led = null;
        if (has)
        {
            range = RadarRange == 0 ? "range_1" : "range_3";
            float R = RadarRange == 0 ? 100 : 600;
            bool any = false, pinged = false;
            foreach (var v in Combat.World.Vehicles)
            {
                if (v.Wreck || !Combat.IsContact(Car, v)) continue;   // the contact list: 600 m on the ground, no dead or engine-off car
                any = true;   // the LED and the growl: any listed car, on the scope or past its range circle
                if (I76.Sim.Weapons.RadarScope.Project(Car.Rot, Car.Pos.ToVector3(), v.Pos.ToVector3(), R) is not var (nx, nz)) continue;
                bool inWedge = PingsNow(v, locked, retry) && I76.Sim.Weapons.RadarScope.InPingWedge(_radarPrev, _radarCur, nx, nz);
                if (inWedge) { if (!pinged) { RadarPing(); pinged = true; } _sweptAt[v] = _t; }   // every pass pings; blips in one step share the sound
                // a raw bitmap row: bottom-up, as the image stores it
                // no team colours: palette index 0x69 when listed, 0 as the sweep passes or it is picked, +3 a second back to
                // 0x69; the target 0xff, which the 1997 CD build never writes (its target fades as the others do)
                bool soft = I76.Game.World.SoftwareLook.Active;
                int ci = !soft && Combat.RadarTarget.GetValueOrDefault(Car) == v ? 0xff
                    : _sweptAt.TryGetValue(v, out var st) ? System.Math.Min(0x69, (int)(3 * (_t - st))) : 0x69;
                _radarOps.Add((BlipRect(nx, nz, 2, w, h, k), soft ? SoftwareBlip(ci) : BlipColour(ci)));
            }
            if (Blips is { } blips)
                foreach (var (fp, fc) in blips())
                {
                    if (I76.Sim.Weapons.RadarScope.Project(Car.Rot, Car.Pos.ToVector3(), fp, R) is not var (nx, nz)) continue;
                    _radarOps.Add((BlipRect(nx, nz, 3, w, h, k), fc));
                }
            if (any && !_ledAny) Sound("cgrowl.wav");
            _ledAny = any;
            led = any ? "radled_1" : "radled_2";
        }
        // what changed since the screen was last drawn, as rectangles (raw rows): where the frame differs from the last one, the
        // blips' squares before and after, the range plate and the LED if they changed. The whole screen when nothing is there
        // to build on (the first frame, a size change, the weapon camera's picture shown since)
        var was = _radarDrawn.Frame as RadarFrames.Frame;
        bool whole = redraw || was is null || _radarWork?.Length != bytes.Length || _radarLive is null || _radarLive.Width != w || _radarLive.Height != h;
        _radarRects.Clear();
        if (!whole)
        {
            if (!ReferenceEquals(was, frame)) foreach (var r in RadarDiff(was!, frame, w, h)) CockpitPanels.AddRect(_radarRects, r);
            if (!_radarDrawn.SameOps(_radarOps))
            {
                foreach (var (r, _) in _radarDrawn.Ops) CockpitPanels.AddRect(_radarRects, r);
                foreach (var (r, _) in _radarOps) CockpitPanels.AddRect(_radarRects, r);
            }
            if (range != _radarDrawn.Range) { RadarStampRect(_radarDrawn.Range, "range_pos", k, h); RadarStampRect(range, "range_pos", k, h); }
            if (led != _radarDrawn.Led) { RadarStampRect(_radarDrawn.Led, "led_pos", k, h); RadarStampRect(led, "led_pos", k, h); }
            // what is drawn over the frame, before and after, whether it changed or not: a step shown from the strips sent ahead
            // (RadarFlip) puts the raw frame wherever it changed, and these are drawn over it again
            _radarSmall.Clear();
            foreach (var (r, _) in _radarDrawn.Ops) CockpitPanels.AddRect(_radarSmall, r);
            foreach (var (r, _) in _radarOps) CockpitPanels.AddRect(_radarSmall, r);
            RadarStampRect(_radarDrawn.Range, "range_pos", k, h, _radarSmall); RadarStampRect(range, "range_pos", k, h, _radarSmall);
            RadarStampRect(_radarDrawn.Led, "led_pos", k, h, _radarSmall); RadarStampRect(led, "led_pos", k, h, _radarSmall);
        }
        if (!_radarDrawn.Update(frame, range, led, _radarOps, redraw)) return;
        // drawn into one buffer kept from frame to frame: the frame's texels, the range plate, the blips, the LED; in the changed
        // rectangles only, each drawn as the whole screen would be there
        if (whole)
        {
            if (_radarWork?.Length != bytes.Length) _radarWork = new byte[bytes.Length];
            RadarCompose(bytes, w, h, k, range, led, new Rect2I(0, 0, w, h));
            _radarWorkStale = false;
            UploadRadar(_radarWork, w, h, null);
            return;
        }
        if (_radarRects.Count == 0) return;
        if (!ReferenceEquals(was, frame) && !smooth && RadarFlip(bytes, was!, frame, w, h, k, range, led)) return;
        // after a flip the kept buffer is drawn only where it was sent: made whole again before an upload may send all of it
        if (_radarWorkStale) { RadarCompose(bytes, w, h, k, range, led, new Rect2I(0, 0, w, h)); _radarWorkStale = false; }
        foreach (var r in _radarRects) RadarCompose(bytes, w, h, k, range, led, r.Intersection(new Rect2I(0, 0, w, h)));
        UploadRadar(_radarWork!, w, h, _radarRects);
    }

    private readonly List<Rect2I> _radarRects = [];
    /// <summary>Where two radar frames differ (raw rows), found once a pair.</summary>
    private readonly Dictionary<(RadarFrames.Frame, RadarFrames.Frame), List<Rect2I>> _radarDiffs = [];

    private List<Rect2I> RadarDiff(RadarFrames.Frame a, RadarFrames.Frame b, int w, int h)
    {
        if (_radarDiffs.TryGetValue((a, b), out var d)) return d;
        d = [];
        if (a.W == w && a.H == h) CockpitPanels.Differ(_radarFrames.Bytes(a), _radarFrames.Bytes(b), w, h, d);
        else d.Add(new Rect2I(0, 0, w, h));
        return _radarDiffs[(a, b)] = d;
    }

    /// <summary>Adds a stamped sprite's rectangle on the radar screen (raw rows) to the changed ones (or to <paramref name="into"/>).</summary>
    private void RadarStampRect(string? sprite, string anchor, int k, int h, List<Rect2I>? into = null)
    {
        if (sprite is not null && Panels.StampRect(k, "zrad.map", sprite, anchor) is { } r)
            CockpitPanels.AddRect(into ?? _radarRects, new Rect2I(r.Position.X, h - r.End.Y, r.Size.X, r.Size.Y));
    }

    private readonly List<Rect2I> _radarSmall = [];
    /// <summary>The kept buffer holds the last frame's texels where a flip copied the new ones on the GPU only.</summary>
    private bool _radarWorkStale;
    private readonly List<int> _radarAhead = [];

    /// <summary>
    /// Between the sweep's steps: the next step's frame (decoded ahead on its worker) sent to the radar's texture a few strips a frame,
    /// the strips its change crosses, spread over the frames left before the step. The screen shown is not touched; the step then
    /// copies the changed rectangles into place on the GPU (<see cref="RadarFlip"/>) instead of sending megabytes in its own frame.
    /// </summary>
    private void RadarAhead()
    {
        if (_radarLive is not { StripCount: > 0 } live || _radarDrawn.Frame is not RadarFrames.Frame shown) return;
        var next = _radarF[I76.Sim.Weapons.RadarScope.NextSweep(_radarCur)];
        if (next is null || ReferenceEquals(next, shown) || next.W != live.Width || next.H != live.Height || shown.W != next.W || shown.H != next.H
            || !_radarFrames.Ready(next) || !_radarFrames.Ready(shown)) return;
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.RadarUpload);
        MissingStrips(live, RadarDiff(shown, next, next.W, next.H), next, _radarAhead);
        if (_radarAhead.Count == 0) return;
        double dt = System.Math.Max(GetProcessDeltaTime(), 0.001);
        int frames = System.Math.Max(1, (int)((_radarNext - _t) / dt) - 1);
        int n = (_radarAhead.Count + frames - 1) / frames;
        var bytes = _radarFrames.Bytes(next);
        for (int i = 0; i < n; i++) live.SendStrip(bytes, _radarAhead[i], next);
    }

    /// <summary>The strips the rectangles cross that do not hold <paramref name="frame"/>, into <paramref name="into"/>.</summary>
    private static void MissingStrips(LiveTexture live, List<Rect2I> rects, RadarFrames.Frame frame, List<int> into)
    {
        into.Clear();
        foreach (var r in rects)
        {
            int s0 = System.Math.Max(0, r.Position.Y) / LiveTexture.StripRows, s1 = (System.Math.Min(live.Height, r.End.Y) - 1) / LiveTexture.StripRows;
            for (int s = s0; s <= s1 && s < live.StripCount; s++)
                if (!ReferenceEquals(live.StripOf(s), frame) && !into.Contains(s)) into.Add(s);
        }
    }

    /// <summary>
    /// A sweep step from <paramref name="was"/> to <paramref name="frame"/> shown from the strips sent ahead: the rectangles where the
    /// frames differ copied into place on the GPU, then the blips, the range plate and the LED (before and after) drawn over them and
    /// sent as before; the picture is the one the rectangles' upload made. False (the usual upload then) where most of the strips were
    /// not sent ahead (the frame came late, a step skipped, the frame rate too low to spread them).
    /// </summary>
    private bool RadarFlip(byte[] bytes, RadarFrames.Frame was, RadarFrames.Frame frame, int w, int h, int k, string? range, string? led)
    {
        if (_radarLive is not { StripCount: > 0 } live || live.Width != w || live.Height != h || !_radarFrames.Ready(was)) return false;
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.RadarUpload);
        var diff = RadarDiff(was, frame, w, h);
        MissingStrips(live, diff, frame, _radarAhead);
        // the few still missing go now (they and the copy are ordered on the GPU); more than a quarter of the screen: the usual upload
        if ((long)_radarAhead.Count * LiveTexture.StripRows * 4 > h) return false;
        foreach (int s in _radarAhead) live.SendStrip(bytes, s, frame);
        live.FromStrips(diff);
        var screen = new Rect2I(0, 0, w, h);
        foreach (var r in _radarSmall) RadarCompose(bytes, w, h, k, range, led, r.Intersection(screen));
        if (_radarSmall.Count > 0) live.Update(_radarWork!, _radarSmall);
        // the kept buffer is drawn only where it is sent; checked, it is made whole first so the GPU's copy is checked against all of it
        _radarWorkStale = true;
        if (DashStats.Verify) { RadarCompose(bytes, w, h, k, range, led, screen); _radarWorkStale = false; DashStats.Check("radar", live.Expected(_radarWork!), live.ReadBack()); }
        return true;
    }

    /// <summary>The radar screen drawn in one rectangle <paramref name="r"/> (raw rows) of the kept buffer: the frame, the range plate, the blips, the LED.</summary>
    private void RadarCompose(byte[] frame, int w, int h, int k, string? range, string? led, Rect2I r)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.RadarCompose);
        for (int y = r.Position.Y; y < r.End.Y; y++) System.Buffer.BlockCopy(frame, (y * w + r.Position.X) * 4, _radarWork!, (y * w + r.Position.X) * 4, r.Size.X * 4);
        // the stamps count rows from the top of the picture
        var top = new Rect2I(r.Position.X, h - r.End.Y, r.Size.X, r.Size.Y);
        if (range is not null) Panels.StampInto(_radarWork!, w, h, k, "zrad.map", range, "range_pos", top);
        foreach (var (rect, colour) in _radarOps) FillRgba(_radarWork!, w, h, rect.Intersection(r), Rgba8(colour));
        if (led is not null) Panels.StampInto(_radarWork!, w, h, k, "zrad.map", led, "led_pos", top);
    }

    /// <summary>
    /// A blip's square on a radar frame of w x h texels at k texels a pixel: size x size of the original's pixels at
    /// (99 + 51 x, 65 + 51 z), the scope's centre and radius in the original frame's pixels, kept inside the frame.
    /// </summary>
    public static Rect2I BlipRect(float nx, float nz, int size, int w, int h, int k)
    {
        int px = System.Math.Clamp((int)(99 + 51 * nx), 0, w / k - size);
        int py = System.Math.Clamp((int)(65 + 51 * nz), 0, h / k - size);
        return new Rect2I(px * k, py * k, size * k, size * k);
    }

    private Vehicle? _radarPicked;
    private readonly List<(Rect2I Rect, Color Colour)> _radarOps = [];
    private readonly ScopePicture _radarDrawn = new();
    private byte[]? _radarWork;
    private readonly Dictionary<Color, uint> _rgba8 = [];

    /// <summary>A colour's RGBA8 bytes as Image.FillRect writes them (a 1x1 fill once a colour, kept).</summary>
    private uint Rgba8(Color c)
    {
        if (_rgba8.TryGetValue(c, out uint px)) return px;
        var one = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
        one.FillRect(new Rect2I(0, 0, 1, 1), c);
        return _rgba8[c] = System.BitConverter.ToUInt32(one.GetData(), 0);
    }

    /// <summary>Image.FillRect on raw RGBA8 bytes of a w x h picture: the square, clipped to the picture, set to the pixel's bytes.</summary>
    public static void FillRgba(byte[] d, int w, int h, Rect2I r, uint px)
    {
        int x0 = System.Math.Max(0, r.Position.X), y0 = System.Math.Max(0, r.Position.Y);
        int x1 = System.Math.Min(w, r.End.X), y1 = System.Math.Min(h, r.End.Y);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan((y * w + x) * 4, 4), px);
    }

    /// <summary>
    /// What the radar screen was last drawn with: the frame, the range plate, the LED and the blips (squares and colours).
    /// <see cref="Update"/> says whether this frame's differs (and keeps it), so an unchanged scope is not made and uploaded again.
    /// </summary>
    public sealed class ScopePicture
    {
        private object? _frame;
        private string? _range, _led;
        private readonly List<(Rect2I, Color)> _ops = [];

        /// <summary>What was last drawn (the frame null when nothing was, or something else was shown since).</summary>
        public object? Frame => _frame;
        public string? Range => _range;
        public string? Led => _led;
        public IReadOnlyList<(Rect2I, Color)> Ops => _ops;

        public bool Update(object frame, string? range, string? led, IReadOnlyList<(Rect2I, Color)> ops, bool force)
        {
            if (!force && ReferenceEquals(frame, _frame) && range == _range && led == _led && SameOps(ops)) return false;
            (_frame, _range, _led) = (frame, range, led);
            _ops.Clear(); _ops.AddRange(ops);
            return true;
        }

        /// <summary>Whether <paramref name="ops"/> are the blips last drawn (compared without an enumerator made).</summary>
        public bool SameOps(IReadOnlyList<(Rect2I, Color)> ops)
        {
            if (ops.Count != _ops.Count) return false;
            for (int i = 0; i < ops.Count; i++) if (ops[i] != _ops[i]) return false;
            return true;
        }

        /// <summary>Something else was shown on the screen (the weapon camera): the next frame is drawn whatever it holds.</summary>
        public void Forget() => _frame = null;
    }
    private readonly Color?[] _softBlips = new Color?[256];

    /// <summary>
    /// A blip's palette index as the GOG build draws it. The mission palette's slot 255 is black (1,1,1), invisible on the
    /// green screen: the target's blip is drawn white, the colour a Windows 8-bit palette gives its last static slot.
    /// </summary>
    private Color BlipColour(int ci) => ci == 0xff ? Colors.White
        : Palette is { } pal ? Color.Color8(pal[ci].R, pal[ci].G, pal[ci].B) : new Color(ci / 255f, ci / 255f, ci / 255f);

    /// <summary>The Software renderer's blip: the 1997 CD build's green for the byte (SoftwarePalette.RadarGreen), as its palette entry.</summary>
    private Color SoftwareBlip(int ci)
    {
        if (_softBlips[ci] is { } c) return c;
        var (r, g, b) = I76.Formats.Imaging.SoftwarePalette.RadarGreen(ci);
        c = Palette is { } pal && I76.Formats.Imaging.SoftwarePalette.RadarBlip(pal, ci) is var i ? Color.Color8(pal[i].R, pal[i].G, pal[i].B) : new Color(r, g, b);
        return (_softBlips[ci] = c).Value;
    }

    /// <summary>The radar screen's picture (w x h RGBA8, raw rows) to its texture: whole, or only <paramref name="rects"/> (raw rows) of it.</summary>
    private void UploadRadar(byte[] px, int w, int h, List<Rect2I>? rects)
    {
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.RadarUpload);
        // a frame of another size (the art made again at another scale, the weapon camera's) is a new picture in the same texture
        RadarScale = System.Math.Max(1, w / _radarBaseW);
        bool all = _radarLive is null || rects is null || _radarLive.Width != w || _radarLive.Height != h;
        if (_radarLive is null) _radarLive = new LiveTexture(px, w, h);
        else if (all) _radarLive.Set(px, w, h);
        else _radarLive.Update(px, rects!);
        // what went up, for the smooth sweep's copy of the scope's window (RadarSmooth)
        if (all) _sweepSentWhole = true;
        else _sweepSent.AddRange(rects!);
        if (DashStats.Verify && _sweepDraw == SweepDraw.None) DashStats.Check("radar", _radarLive.Expected(px), _radarLive.ReadBack());
        if (System.Environment.GetEnvironmentVariable("I76_PIT_DUMP") is { } dir && Godot.Engine.GetProcessFrames() >= 15 && !_radarDumped) { Godot.Image.CreateFromData(w, h, false, Godot.Image.Format.Rgba8, px).SavePng(System.IO.Path.Combine(dir, "radar.png")); _radarDumped = true; }
        if (!_radarBound) { Materials.OverrideTexture(_radarMember!, _radarLive.Texture); _radarBound = true; }
    }

    // ---- Radar Sweep: Smooth (RadarSweepRow): the beam and the lock cone drawn on the GPU every frame ----

    private int _radarSweep = RadarSweepRow.Default;
    /// <summary>Graphic Detail's Radar Sweep ([port] radar_sweep): Stepped, the original's frames, or Smooth; I76_RADAR_SWEEP over it.</summary>
    public int RadarSweep { get => _radarSweep; set => _radarSweep = RadarSweepRow.Clamp(value); }
    /// <summary>I76_RADAR_LOG=1: a line for each sweep step (its frame, the cockpit's clock, the mode).</summary>
    private static readonly bool RadarLog = System.Environment.GetEnvironmentVariable("I76_RADAR_LOG") == "1";
    private enum SweepDraw { None, Beam, Cone }
    /// <summary>What the GPU draws over the screen this frame; whether the screen was smooth last frame; a sweep step this frame.</summary>
    private SweepDraw _sweepDraw;
    private bool _sweepWas, _sweepStepped;
    private RadarSweepGpu? _sweepGpu;
    private RadarSweepArt? _sweepFor;
    /// <summary>What UploadRadar sent this frame (raw rows), or all of it.</summary>
    private readonly List<Rect2I> _sweepSent = [];
    private bool _sweepSentWhole;
    /// <summary>The live texture the window's copy was last taken whole from (a new texture is redrawn whole first).</summary>
    private Rid _sweepCopied;
    /// <summary>The beam's last bearing and the cone's eased one (degrees clockwise from the front); whether the cone is held.</summary>
    private double _sweepBearing, _coneBearing;
    private bool _coneHeld;

    /// <summary>
    /// The face the screen is drawn on when the sweep is smooth (the modern look, the setting on, the frames' pack has the layers and
    /// the GPU kernel runs), else null: the stepped frames.
    /// </summary>
    private RadarFrames.Frame? SweepFace()
    {
        if (_faithful || (RadarSweepRow.Hook ?? _radarSweep) != RadarSweepRow.Smooth || _radarFrames.SweepArt is not { } art) return null;
        if (!ReferenceEquals(art, _sweepFor))
        {
            _sweepGpu?.Dispose();
            _sweepGpu = RadarSweepGpu.Make(art);
            _sweepFor = art;
            _sweepCopied = default;
            GD.Print(_sweepGpu is null ? "radar sweep: smooth asked for, but the kernel cannot run here (no RenderingDevice, or it did not compile): stepped"
                : $"radar sweep: smooth, the pack's layers at {art.Params.Scale}x (window {art.Params.Box.Size.X}x{art.Params.Box.Size.Y} texels)");
        }
        return _sweepGpu is null ? null : art.Face;
    }

    /// <summary>
    /// The smooth sweep's frame: the scope's window as just sent copied on the GPU, then drawn by the kernel with the beam at the
    /// sweep's continuous angle (the stepped frame's at each step's instant, RadarSweepLaw) or the cone eased onto the target's bearing.
    /// </summary>
    private void RadarSmooth()
    {
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.RadarSweep);
        if (_radarLive is not { } live || _sweepGpu is not { } gpu) return;
        if (_sweepSentWhole) { gpu.Took(live, null); _sweepCopied = live.DeviceRid; }
        else if (_sweepCopied != live.DeviceRid) { _radarRedraw = true; return; }   // a texture made since (a look's kept size shown): whole next frame
        else if (_sweepSent.Count > 0) gpu.Took(live, _sweepSent);
        var p = gpu.Art.Params;
        double angle;
        bool cone = _sweepDraw == SweepDraw.Cone;
        if (!cone)
        {
            double phase = RadarSweepLaw.Phase(_radarCur, _t, _radarNext);
            _sweepBearing = RadarSweepLaw.Bearing(phase, p.OffsetDeg);
            angle = RadarSweepLaw.FileAngle(phase, p.OffsetDeg);
            _coneHeld = false;
            if (DashStats.Verify && _sweepStepped) SweepCheck(gpu, live, RadarSweepLaw.FileAngle(_radarCur, p.OffsetDeg), false, _radarF[_radarCur]);
        }
        else
        {
            if (Combat?.RadarTarget.GetValueOrDefault(Car) is not { } target) return;
            var d = Car.Rot.ToLocal(target.Pos.ToVector3() - Car.Pos.ToVector3());
            double want = RadarSweepLaw.TargetBearing(d.X, d.Z);
            // a new lock swings the cone from where the beam was; then it follows the target's bearing, eased
            if (!_coneHeld) { _coneBearing = ((_sweepBearing % 360) + 360) % 360; _coneHeld = true; }
            _coneBearing = RadarSweepLaw.Ease(_coneBearing, want, GetProcessDeltaTime());
            angle = RadarSweepLaw.ConeFileAngle(_coneBearing);
            if (DashStats.Verify && _radarAt >= 0 && Godot.Engine.GetProcessFrames() % 30 == 0)
                SweepCheck(gpu, live, RadarSweepLaw.ConeFileAngle(RadarSweepLaw.StepDegrees * _radarAt), true, _radarB[_radarAt]);
        }
        gpu.Draw(live, angle, cone);
    }

    /// <summary>
    /// I76_DASH_STATS=verify: the kernel drawn at a step's own angle and read back, against the stepped frame of that step with the same
    /// blips, plate and lamp drawn on it (CPU), over the scope's window; a line with the differences.
    /// </summary>
    private void SweepCheck(RadarSweepGpu gpu, LiveTexture live, double angle, bool cone, RadarFrames.Frame? stepped)
    {
        if (stepped is null || stepped.W != live.Width || stepped.H != live.Height || _radarWork is null) return;
        if (!gpu.Draw(live, angle, cone) || live.ReadBack() is not { } back) return;
        int w = live.Width, h = live.Height, k = System.Math.Max(1, w / _radarBaseW);
        var mine = _radarWork;
        _radarWork = new byte[w * h * 4];
        RadarCompose(_radarFrames.Bytes(stepped), w, h, k, _radarDrawn.Range, _radarDrawn.Led, new Rect2I(0, 0, w, h));
        var want = _radarWork;
        _radarWork = mine;
        var b = gpu.Art.Params.Box;
        int n = 0, over2 = 0, over8 = 0, max = 0;
        for (int y = b.Position.Y; y < b.End.Y; y++)
            for (int x = b.Position.X; x < b.End.X; x++)
            {
                int i = (y * w + x) * 4, m = 0;
                for (int c = 0; c < 3; c++) m = System.Math.Max(m, System.Math.Abs(back[i + c] - want[i + c]));
                n++;
                if (m > 2) over2++;
                if (m > 8) over8++;
                if (m > max) max = m;
            }
        string what = cone ? $"cone, locked frame {_radarAt} (zradb{_radarAt:000})" : $"beam, step {_radarCur} (zradf{_radarCur:000})";
        GD.Print($"radar sweep check: {what} at {_t:0.000} s: {n} texels of the window, {over2} differ by more than 2, {over8} by more than 8, at most {max} (the stepped frame against the kernel at its angle)");
    }

    // ---- Toggle Weapon Cam: the turret's view in the radar screen ----

    private Image? _radarMask;
    private bool[]? _radarClear;   // the mask's corners outside the box, left transparent as the radar frames' are
    private SubViewport? _wcamView;
    private Camera3D? _wcamCam;
    /// <summary>The backdrop key: the camera's clear colour, replaced by the two greys; no object is this colour.</summary>
    private static readonly Color WcamKey = new(1, 0, 1);

    /// <summary>
    /// The radar picture while the weapon camera shows (the flag set, a target held, a turret, the radar on and
    /// zradmask.map found), else null: the turret's view read back from its 256x128 viewport,
    /// the two greys behind it, remapped to the green ramp by luminance, the mask over it. Raw rows, bottom-up.
    /// </summary>
    private Image? WeaponCamera()
    {
        var turret = Combat is null || !RadarOn || _radarMask is null || !Combat.RadarCamera.Contains(Car) || Combat.RadarTarget.GetValueOrDefault(Car) is null
            ? null : Car.Armory?.Weapons.LastOrDefault(w => w.Style == 3);
        if (turret is null)
        {
            if (_wcamView is not null) _wcamView.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return null;
        }
        if (_wcamView is null)
        {
            _wcamView = new SubViewport { Size = new Vector2I(256, 128), Name = "WeaponCam", RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            // 2 * atan(64 / 424.2): the engine's 135 degrees clamped, zoom 8 (focal length 424.2 px), over 128 rows
            _wcamCam = new Camera3D { Fov = 17.16f, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 0.05f, Far = 4000, Current = true,
                // no terrain, no sky, and not the interior
                CullMask = 0xFFFFFu & ~World.TerrainNode.TerrainLayer & ~World.TerrainNode.SkyFogLayer & ~CarNode.CockpitLayer & ~SmokeLayer.QuadLayer,
                Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = WcamKey,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.35f, 0.35f) } };
            _wcamView.AddChild(_wcamCam);
            AddChild(_wcamView);
        }
        _wcamView.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        // the pitch node: the car's pose, the mount's base, the yaw and the pitch (Armory.MuzzleRot), at the turret's pivot, looking down +Z
        var mount = Node3D.GlobalTransform * new Transform3D(Coords.ToGodot(turret.MuzzleRot), Coords.ToGodot(turret.TurretPivot ?? turret.MuzzlePos));
        // the view only, not the aim: a helicopter's turret hangs under the body, so its frame is turned over (or on its side) and the horizon
        // with it. The camera keeps the gun's line and rolls to the world's up; aimed straight up or down it has no horizon, and keeps the mount's roll.
        var gun = -mount.Basis.Z;
        if (new Vector3(gun.X, 0, gun.Z).LengthSquared() > 1e-4f) mount.Basis = Basis.LookingAt(gun, Vector3.Up);
        _wcamCam!.GlobalTransform = mount;
        var shot = _wcamView.GetTexture().GetImage();   // the last frame's render: one frame of lag, as the 3D dash had
        if (shot is null || shot.GetWidth() != 256) return _radarF[30] is { } blank ? Godot.Image.CreateFromData(blank.W, blank.H, false, Godot.Image.Format.Rgba8, _radarFrames.Bytes(blank)) : null;
        shot.Convert(Godot.Image.Format.Rgba8);
        var px = shot.GetData();
        var outp = new byte[256 * 128 * 4];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
            {
                int i = (y * 256 + x) * 4;
                float r = px[i] / 255f, g = px[i + 1] / 255f, b = px[i + 2] / 255f;
                // the backdrop: rows 0..47 of the picture grey 0.7 (sky), the rest 0.5 (ground)
                if (px[i] == 255 && px[i + 1] == 0 && px[i + 2] == 255) r = g = b = y < 48 ? 0.7f : 0.5f;
                float t = Mathf.Clamp((0.3f * r + 0.59f * g + 0.11f * b - 0.133f) / (0.617f - 0.133f), 0, 1);
                int o = ((127 - y) * 256 + x) * 4;   // raw rows are bottom-up
                outp[o] = (byte)Mathf.RoundToInt(255 * 0.388f * t);
                outp[o + 1] = (byte)Mathf.RoundToInt(255 * (0.225f + (0.776f - 0.225f) * t));
                outp[o + 2] = outp[o];
                outp[o + 3] = 255;
            }
        var img = Godot.Image.CreateFromData(256, 128, false, Godot.Image.Format.Rgba8, outp);
        img.BlendRect(_radarMask!, new Rect2I(0, 0, System.Math.Min(256, _radarMask!.GetWidth()), System.Math.Min(128, _radarMask.GetHeight())), Vector2I.Zero);
        if (_radarClear is { } clear)
        {
            // raw rows, bottom-up: the mask's index order, one row for one
            for (int i = 0; i < clear.Length && i < 256 * 128; i++)
                if (clear[i]) img.SetPixel(i % 256, i / 256, new Color(0, 0, 0, 0));
        }
        return img;
    }

    private LiveTexture? _radarLive;
    private bool _radarBound, _radarDumped;

    // ---- gauge needles ----

    /// <summary>
    /// The speedometer's mph: the engine's. A helicopter (class 9: the Huey, Cobra, balloon,
    /// UFO, Phantom) never runs the car update that keeps it, so in the original its speedometer stays at 0 however fast
    /// it flies (measured in the original game: mph 0 and both needles at rest at 107 u/s); here it reads the airspeed, |v| in mph as the
    /// engine scales it (docs/deviations.md).
    /// </summary>
    private float Mph => Car.Heli is null ? Car.Engine.Speedo : Car.Speed * 2.2369363f;

    private void Needles()
    {
        // speedo: 0..150 mph over 278°; tacho: 0..8400 rpm over 236.25°; past 180° wraps negative
        float Wrap(float deg) => deg > 180 ? deg - 360 : deg;
        float speedo = Wrap(System.Math.Clamp(Mph, 0, 150) * 1.8533334f);
        float tacho = Wrap(System.Math.Min(Car.Engine.Rpm, 8400) * 0.028125f);
        Turn(Node3D.Needles[0], speedo);
        Turn(Node3D.Needles[1], tacho);
        // RotY (the engine's [c 0 −s; 0 1 0; s 0 c]) ahead of the needle's VLOC; the Z mirror turns it the other way here
        static void Turn((Node3D Node, Transform3D Pose)? n, float deg)
        {
            if (n is { } x) x.Node.Transform = x.Pose * new Transform3D(new Basis(Vector3.Up, -Mathf.DegToRad(deg)), Vector3.Zero);
        }
    }

    // ---- the 2D gauges ----

    private int _speedFrame = -1, _tachFrame = -1;
    private static readonly string[] NeedleSprites = [.. Enumerable.Range(0, 40).Select(i => $"needle_{i}")];

    /// <summary>
    /// The external instruments' speedometer and tachometer: the dial
    /// (numerals only, no face) with one of zneedle6.map's 79x79 needle frames
    /// stamped at its anchor. Speed 0..150 mph over frames 9..38, rpm over
    /// 1..25 (8000 rpm). The original composes them only under the 2D dash
    /// (dash3d == 0), which leaves the default 3D dash's chase view without
    /// gauges; here they always work (docs/deviations.md).
    /// </summary>
    private void Gauges()
    {
        float s = System.Math.Clamp(Mph, 0, 150);
        Needle("zspeedo6.map", "speedneedleloc", System.Math.Min(9 - (int)(s * -0.19333334f), 38), ref _speedFrame);
        float r = System.Math.Clamp(Car.Engine.Rpm, 0, 8400);
        Needle("ztach6.map", "tachneedleloc", System.Math.Min((int)(r * 0.003f + 1f), 25), ref _tachFrame);

        // the anchor by its dial: the first speedneedleloc in the file is zspeedo3.map's
        void Needle(string dial, string anchor, int frame, ref int shown)
        {
            if (frame == shown || Panels.Layout.FindIn(dial, anchor) is not { } a) return;
            int was = shown;
            shown = frame;
            // the needle's square drawn again (the old frame's and the new one's), the dial round it left as it is
            if (was >= 0 && Panels.Texture(dial) is not null && Panels.FootprintIn(dial, NeedleSprites[frame], anchor) is { } r)
            {
                if (Panels.FootprintIn(dial, NeedleSprites[was], anchor) is { } o) r = r.Merge(o);
                Panels.Region(dial, r);
                Panels.Stamp(dial, NeedleSprites[frame], a.X, a.Y);
                Panels.EndClip(dial);
                return;
            }
            Panels.Clear(dial);
            Panels.Stamp(dial, NeedleSprites[frame], a.X, a.Y);
        }
    }

    // ---- rear-view mirror ----

    private SubViewport? _mirrorView;
    private Camera3D? _mirrorCam;
    private ShaderMaterial? _mirrorMat;
    private Godot.Environment? _mirrorFlat;
    private int _mirrorMode = 2;
    private bool _mirrorTick;

    /// <summary>The mirror's edge field and fill made ahead on a worker (ReadySteps), for SetupMirror to take; default otherwise.</summary>
    private (byte[]? Field, byte[]? Fill) _mirrorPre;

    private void SetupMirror() { foreach (var _ in SetupMirrorSteps()) { } }

    /// <summary><see cref="SetupMirror"/> in parts, a <c>null</c> between them (a mission attaching under its briefing).</summary>
    private IEnumerable<object?> SetupMirrorSteps()
    {
        if (Resolver.FindByStem("zmiri101") is not { } member || Node3D.CockpitMesh is not { Mesh: { } mesh } mi) yield break;
        var made = Materials.MadeFor(member).ToHashSet();
        var surfaces = Enumerable.Range(0, mesh.GetSurfaceCount()).Where(i => mesh.SurfaceGetMaterial(i) is { } m && made.Contains(m)).ToList();
        if (surfaces.Count == 0) yield break;
        _mirrorView = new SubViewport { Size = new Vector2I(1024, 256), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Name = "Mirror" };
        _mirrorCam = new Camera3D { Fov = MirrorCameraSettings.FovDegrees, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.1f, Far = MirrorCameraSettings.FarMetres, Current = true };
        _mirrorView.AddChild(_mirrorCam);
        SmokeLayer.MirrorCamera = _mirrorCam;
        AddChild(_mirrorView);
        yield return null;
        _mirrorFlat = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = MirrorBackground,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.35f, 0.35f) };
        _mirrorMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/cockpit_mirror.gdshader") };
        if (RimKeyHook) _mirrorMat.SetShaderParameter("rim_key", true);
        if (Textures.Get(member)?.Texture is { } art) { _mirrorMat.SetShaderParameter("art", art); _mirrorArt = art; }
        if (Textures.GetOriginalRgba(member) is { } o)
        {
            _mirrorEdge = ImageTexture.CreateFromImage(Godot.Image.CreateFromData(o.Width * MirrorEdge.FieldScale, o.Height * MirrorEdge.FieldScale, false, Godot.Image.Format.Rg8, _mirrorPre.Field ?? MirrorEdge.Field(o.Rgba, o.Width, o.Height)));
            _mirrorMat.SetShaderParameter("edge", _mirrorEdge);
        }
        yield return null;
        MirrorFill();
        _mirrorMat.SetShaderParameter("smooth_edge", _mirrorEdge is not null && _mirrorFill is not null);
        if (Textures.GetOriginalRgba(member) is { } ro) { _mirrorOriginal = (ro.Rgba, ro.Width, ro.Height); yield return null; RoundCorners(); }
        yield return null;
        if (System.Environment.GetEnvironmentVariable("I76_PIT_DUMP") is { } dir) foreach (var n in new[] { "zmiri101", "zdash101", "zdash102" }) Image(n)?.SavePng(System.IO.Path.Combine(dir, n + ".png"));
        var vt = _mirrorView.GetTexture();
        _mirrorMat.SetShaderParameter("view", vt);
        _mirrorMat.SetShaderParameter("view_nearest", vt);
        foreach (int i in surfaces) mi.SetSurfaceOverrideMaterial(i, _mirrorMat);
        _mirrorHost = mi; _mirrorSurface = surfaces[0];
        // the rear glass's box, for whether it is in the driver's view (MirrorCull)
        foreach (int i in surfaces)
            foreach (var p in mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                _rearBox = _rearBox is { } b ? b.Expand(p) : new Aabb(p, Vector3.Zero);
        yield return null;
        _mirrorMode = _mirrorSetting;
        ApplyMirrorMode();
    }

    /// <summary>The modern look's rounder rear mirror (MirrorRound, [port] mirror_round): the frame's outline and glass fitted from the original art, their corners that many of its pixels round (the glass's less the frame's width at the top); 0, the art's own. The 1997 look's path never reads them.</summary>
    private void RoundCorners()
    {
        if (_mirrorMat is not { } m || _mirrorOriginal.Rgba is not { } rgba) return;
        var (_, w, h) = _mirrorOriginal;
        float r = MirrorRound.Radius(System.Environment.GetEnvironmentVariable("I76_MIRROR_ROUND"), _roundSetting);
        var (outline, glass) = MirrorRound.Masks(rgba, w, h);
        _fit = MirrorRound.Fit(outline, w, h) is { } o && MirrorRound.Fit(glass, w, h) is { } g ? (o, g) : null;
        if (r <= 0 || _fit is not { } fit) m.SetShaderParameter("round_corners", false);
        else
        {
            var (fo, fg) = fit;
            float ri = Mathf.Max(r - (fg.TL.Y - fo.TL.Y), 1f);
            Shape(m, MirrorRound.Shrink(fo, r), r, MirrorRound.Shrink(fg, ri), ri, false);
            if (System.Environment.GetEnvironmentVariable("I76_MIRROR_CULL_LOG") == "1") GD.Print($"mirror-round: r {r} outline {fo} glass {fg} glass r {ri}");
        }
        foreach (var sm in new[] { _sideMatL, _sideMatR }) if (sm is not null) AddedFrame(sm);
    }

    private (MirrorRound.Quad Outline, MirrorRound.Quad Glass)? _fit;

    private void Shape(ShaderMaterial m, MirrorRound.Quad outer, float r, MirrorRound.Quad inner, float ri, bool glassFromShape)
    {
        m.SetShaderParameter("round_corners", true);
        if (_mirrorOriginal.Rgba is not null) m.SetShaderParameter("art_px", new Vector2(_mirrorOriginal.W, _mirrorOriginal.H));
        m.SetShaderParameter("outer_tl", outer.TL); m.SetShaderParameter("outer_tr", outer.TR); m.SetShaderParameter("outer_br", outer.BR); m.SetShaderParameter("outer_bl", outer.BL);
        m.SetShaderParameter("inner_tl", inner.TL); m.SetShaderParameter("inner_tr", inner.TR); m.SetShaderParameter("inner_br", inner.BR); m.SetShaderParameter("inner_bl", inner.BL);
        m.SetShaderParameter("outer_r", r); m.SetShaderParameter("inner_r", ri);
        m.SetShaderParameter("glass_from_shape", glassFromShape);
    }

    /// <summary>
    /// The added side glasses (side_mirrors=on, a car without door-mirror glass): the rear art's ends, their frame narrowed to 0.15 of its
    /// width as the door mirrors' rim is (MirrorRim.Narrowing): the glass is the outline drawn in by that, its corners the door mirrors' rounding
    /// (side_mirror_round: that share of the outline's height, at most half of it).
    /// </summary>
    private void AddedFrame(ShaderMaterial sm)
    {
        if (_fit is not { } fit) return;
        var (o, g) = fit;
        float t = (g.TL.Y - o.TL.Y) * MirrorRim.RimShare / (MirrorRim.RearFrameShare * 2f / 3f);   // MirrorRim.Narrowing of the frame's width (I76_MIRROR_RIM=wide: two thirds)
        float height = o.BL.Y - o.TL.Y;
        float r = Mathf.Clamp(GlassRound.Share(GlassRoundHook, _glassRoundSetting) / 100f * height, 1f, 0.49f * height), ri = Mathf.Max(r - t, 1f);
        Shape(sm, MirrorRound.Shrink(o, r), r, MirrorRound.Shrink(o, t + ri), ri, true);
        // the shader draws the frame t wide as seen across the glass and that many screen pixels all round
        sm.SetShaderParameter("frame_t", t);
    }

    /// <summary>The mirror's fill as made for the art served now (kept with a look's art, LookArt).</summary>
    private Godot.Image? _mirrorFillImage;

    /// <summary>The smooth outline's fill from the art served now (the originals or a pack's upscale), in the same texture; <paramref name="kept"/>: made before for it.</summary>
    private void MirrorFill(Godot.Image? kept = null)
    {
        if (_mirrorMat is null || Resolver.FindByStem("zmiri101") is not { } member) return;
        var img = kept;
        if (img is null)
        {
            if (Textures.GetRgba(member) is not { } a) return;
            img = Godot.Image.CreateFromData(a.Width, a.Height, false, Godot.Image.Format.Rgba8, _mirrorPre.Fill ?? MirrorEdge.Fill(a.Rgba, a.Width, a.Height, Textures.Scale(member)));
        }
        _mirrorFillImage = img;
        if (_mirrorFill is null) { _mirrorFill = ImageTexture.CreateFromImage(img); _mirrorMat.SetShaderParameter("fill", _mirrorFill); }
        else _mirrorFill.SetImage(img);
    }

    /// <summary>Puts a 2D overlay into the mirror's own picture (the target brackets, inMirror = 1); false with no mirror.</summary>
    public bool AddMirrorOverlay(Control c)
    {
        if (_mirrorView is null) return false;
        _mirrorView.AddChild(c);
        return true;
    }

    /// <summary>Graphic Detail's Rear Mirror (I76PLYR.DEF +0x4d): Off, Low (objects only), High (full) — the mode the mirror starts in.</summary>
    public int MirrorSetting
    {
        get => _mirrorSetting;
        set { _mirrorSetting = System.Math.Clamp(value, 0, 2); _mirrorMode = _mirrorSetting; ApplyMirrorMode(); }
    }
    private int _mirrorSetting = 2;

    /// <summary>` (TOGGLE_REARVIEW) cycles off, objects only (no sky or terrain), full; the state is kept in any view.</summary>
    public void CycleMirror()
    {
        if (_mirrorMat is null) return;
        _mirrorMode = (_mirrorMode + 1) % 3;
        ApplyMirrorMode();
    }

    /// <summary>The mirror's current mode (0 off, 1 objects only, 2 full), and whether the cockpit has a mirror at all.</summary>
    public int MirrorMode => _mirrorMode;
    public bool HasMirror => _mirrorView is not null;

    private void ApplyMirrorMode()
    {
        if (_mirrorView is null || _mirrorCam is null || _mirrorMat is null) return;
        // the side-view mirrors' one wide render (MirrorBands): the rear mirror's band keeps its 1024 columns across its 45 degrees
        var (glass, quads) = MirrorBands.Shown(_sideMirrors, Glass.Count > 0);
        bool wide = !_faithful && (glass || quads);
        float sideFov = MirrorBands.Fov(_mirrorFov);
        // with the side mirrors drawn by their own cameras the rear render shows its own 45 degrees alone: the same picture as the
        // wide render's middle band (the same 1024 columns across them), without the 90-degree pass's outer bands nobody sees
        // (I76_MIRROR_REAR_WIDE=1 draws it wide as before, for a measurement)
        bool rearWide = wide && (_sideRender != SideMirrorRender.Own || RearWideHook);
        float fov = rearWide ? sideFov : MirrorCameraSettings.FovDegrees;
        var shown = _faithful ? new Vector2I(256, 64) : new Vector2I(rearWide ? MirrorWidthOverride ?? MirrorBands.Width(fov, 1024) : 1024, 256);
        // Mirror Rate's Smooth values (MirrorWarp): the render a little wider and taller than the glass, at the glass's pixels per degree
        _warpPeriod = _faithful ? 0 : MirrorWarp.Period(_mirrorRate);
        var (ow, oh) = MirrorWarp.Size(shown.X, shown.Y, MirrorWarp.Overscan(_warpPeriod));
        _mirrorShown = shown;
        _mirrorView.Size = new Vector2I(ow, oh);
        I76.Game.Boot.LookFilters.Apply(_mirrorView, _faithful);
        float tx = MathF.Tan(fov * MathF.PI / 360f);
        _rearTan = ((tx, tx * shown.Y / shown.X), (tx * ow / shown.X, tx * oh / shown.X));
        _mirrorCam.Fov = ow == shown.X ? fov : 2f * MathF.Atan(_rearTan.Drawn.X) * 180f / MathF.PI;
        _warpDrawn[0] = _warpDrawn[1] = _warpDrawn[2] = null;
        var rear = rearWide ? MirrorBands.Rear(fov) : (0f, 1f);
        MirrorBand = new Vector2(rear.Item1, rear.Item2);
        _mirrorMat.SetShaderParameter("band", MirrorBand);
        _mirrorMat.SetShaderParameter("faithful", _faithful);
        _mirrorMat.SetShaderParameter("hidden", _mirrorMode == 0);
        SideMirrors(wide && quads, sideFov);
        GlassMirrors(wide && glass, sideFov);
        OwnCameras(wide && _sideRender == SideMirrorRender.Own, quads);
        // the modern terrain's far tier for the mirror (TerrainNode.NearTierLayer left out: terrain_modern.gdshaderinc reads no
        // material, relief or asphalt for a camera without it), as for the shadow passes
        _mirrorCam.CullMask = SmokeLayer.MirrorMask((_mirrorMode == 1 ? ~World.TerrainNode.TerrainLayer : 0xFFFFFu) & ~CarNode.CockpitLayer & ~SmokeLayer.QuadLayer & ~World.TerrainNode.NearTierLayer & ~CarNode.OwnBodyLayer & 0xFFFFF, 1);   // its smoke: the layer's own view (SmokeLayer)
        _mirrorCam.Far = OriginalBehavior.MirrorFar(_faithful);   // [port] mirror_range: the original's 100 m, or Full (the modern look's default)
        _mirrorCam.Environment = _mirrorMode == 1 ? _mirrorFlat : MirrorEnvironment();
        DepthPass();
        ApplyHaze();
        ApplyAa();
        Warp();
    }

    /// <summary>
    /// The full mirror's environment in the modern look: the world's own, copied, without its SSAO (the world's Ambient Occlusion is
    /// not drawn in a picture 160 pixels tall, and it makes the mirror's depth pre-pass draw every surface's normals: docs/perf/costs.md,
    /// 1 of the mirror's 3.5 ms at 3440x1440) and, at the original's 100 m reach, with a depth fog into the sky over the last stretch, so
    /// the clip is a fade as the original's was (its fog table ends the 100 m) and not a cut. Null in the 1997 look (the world's, which has
    /// neither). I76_MIRROR_SSAO=1 draws it through the world's environment, SSAO and all, for a measurement.
    /// </summary>
    private Godot.Environment? MirrorEnvironment()
    {
        if (_faithful || MirrorWorldEnv || GetViewport()?.World3D?.Environment is not { } we) return null;
        if (_mirrorEnvSource != we || _mirrorEnv is null) { _mirrorEnv = (Godot.Environment)we.Duplicate(); _mirrorEnvSource = we; }
        SyncMirrorEnvironment(we, _mirrorEnv);
        return _mirrorEnv;
    }

    /// <summary>What the world's environment changes after the mission is built (the lighting calibration, the sky, Monitor Brightness) carried into the mirror's copy; SSAO and the fog are the mirror's own.</summary>
    private void SyncMirrorEnvironment(Godot.Environment we, Godot.Environment me)
    {
        if (me.Sky != we.Sky) me.Sky = we.Sky;
        me.AmbientLightEnergy = we.AmbientLightEnergy;
        me.TonemapMode = we.TonemapMode;
        me.TonemapExposure = we.TonemapExposure;
        me.AdjustmentEnabled = we.AdjustmentEnabled;
        me.AdjustmentBrightness = we.AdjustmentBrightness;
        me.AdjustmentColorCorrection = we.AdjustmentColorCorrection;
        me.SsaoEnabled = false;
        me.SsrEnabled = false;     // Reflections and Glow are the main view's (EnvExtras): not drawn in a mirror's picture
        me.GlowEnabled = false;
        float far = _mirrorCam?.Far ?? MirrorCameraSettings.FarMetres;
        bool fog = MirrorFog && far <= MirrorCameraSettings.FarMetres + 1f;
        me.FogEnabled = fog;
        if (fog)
        {
            me.FogMode = Godot.Environment.FogModeEnum.Depth;
            me.FogDepthBegin = MirrorCameraSettings.FogBeginMetres;
            me.FogDepthEnd = far;
            me.FogDepthCurve = 1f;
            me.FogDensity = 1f;
            me.FogAerialPerspective = 1f;   // the fog takes the sky's colour behind it
            me.FogSkyAffect = 0f;
            me.FogLightEnergy = 1f;
        }
    }

    /// <summary>
    /// The mirror's cadence: every second frame, as the original's (docs/deviations.md); I76_MIRROR_RATE=1 every frame in the modern
    /// look, as it was before (perf survey). I76_MIRROR_SSAO=1 gives it the world's environment; I76_MIRROR_FOG=0 no fog at 100 m.
    /// </summary>
    private static readonly bool RearWideHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_REAR_WIDE") == "1";
    private static readonly bool MirrorEveryFrame = System.Environment.GetEnvironmentVariable("I76_MIRROR_RATE") == "1";
    private static readonly bool MirrorWorldEnv = System.Environment.GetEnvironmentVariable("I76_MIRROR_SSAO") == "1";
    private static readonly bool MirrorFog = System.Environment.GetEnvironmentVariable("I76_MIRROR_FOG") != "0";
    private Godot.Environment? _mirrorEnv, _mirrorEnvSource;
    private int _mirrorSync;

    /// <summary>The mirror drawn on its cadence while the tree is paused under the Esc menu, so the frame rate measured there is play's (the picture is the same frozen view).</summary>
    public void MirrorWhilePaused() { if (Active) Mirror(); }

    private void Mirror()
    {
        if (_mirrorView is null || _mirrorCam is null) return;
        if (_mirrorMode == 0) { _mirrorView.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; MirrorsUnseen(); return; }
        // the eye, not head-turned, its right and forward negated: looking backwards
        _mirrorCam.GlobalTransform = Node3D.GlobalTransform * new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Node3D.Eye);
        if (_sideL is { Visible: true }) SideMirrorYaw();
        // the original redraws it every second frame, and so do both looks (I76_MIRROR_RATE=1: the modern look every frame)
        _mirrorTick = !_mirrorTick;
        // the modern look draws it only while something shows it (MirrorCull): its glass in view, the 2D dash's mirror, or a side glass
        // that shows a band of it; back in view, it is drawn that frame, not on the next tick
        InView();
        bool rear = _faithful || CullOff || !Node3D.Dash3D || _rearInView || !_ownOn && (_sideInView[0] || _sideInView[1]);
        bool rearBack = rear && !_rearSeen && !_faithful && !CullOff;   // the 1997 look and I76_MIRROR_CULL=0 keep the tick alone
        _rearSeen = rear;
        if (CullLog && rearBack) GD.Print($"mirror-cull: rear back in view at frame {Godot.Engine.GetFramesDrawn()}, drawn that frame");
        // Mirror Rate (MirrorRateRow): every second frame (Alternate, Half), or every frame (Full; I76_MIRROR_RATE=1 too)
        var rate = _faithful ? MirrorRate.Half : _mirrorRate;
        if (_warpPeriod > 0)
        {
            // Smooth (MirrorWarp): each mirror on its frame of the period, in turn; the glass re-projected on the frames between
            _warpFrame++;
            bool draw = rear && (MirrorWarp.Draws(_warpPeriod, _warpFrame, 0) || rearBack || _warpDrawn[0] is null);
            _mirrorView.RenderTargetUpdateMode = draw ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
            if (draw) _warpDrawn[0] = _mirrorCam.GlobalTransform;
        }
        else
            _mirrorView.RenderTargetUpdateMode = !rear ? SubViewport.UpdateMode.Disabled
                : MirrorRateRow.Draws(!_faithful && MirrorEveryFrame ? MirrorRate.Full : rate, _mirrorTick) || rearBack ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        OwnCamerasFrame(_mirrorTick, rate);
        Warp();
        // the world's environment as it is now, about twice a second (the lighting calibration's solve ends after the mirror is made)
        if (_mirrorEnv is not null && _mirrorEnvSource is not null && _mirrorCam.Environment == _mirrorEnv && ++_mirrorSync % 30 == 0)
            SyncMirrorEnvironment(_mirrorEnvSource, _mirrorEnv);
    }

    // ---- Mirror Rate's Smooth values: the frames between a mirror's draws re-projected (MirrorWarp) ----

    /// <summary>The draw period under Smooth (2, 3, 4), 0 otherwise; the frames counted under it; each camera's place at its last draw (rear, left, right).</summary>
    private int _warpPeriod;
    private long _warpFrame;
    private readonly Transform3D?[] _warpDrawn = new Transform3D?[3];
    /// <summary>The rear render's size as the glass shows it (the render is larger by the overscan), and the half tangents shown and drawn, the rear's and the side cameras'.</summary>
    private Vector2I _mirrorShown = new(1024, 256);
    private ((float X, float Y) Shown, (float X, float Y) Drawn) _rearTan = ((1, 1), (1, 1)), _ownTan = ((1, 1), (1, 1));
    /// <summary>
    /// The re-projection: the rear mirror for the camera's turn and move (the move from its render's depth, MirrorDepth, a compositor
    /// effect on the camera), the side mirrors for the turn alone with the car's own flank held still (the move from their depth put
    /// the ground beside the flank wrong and measured no better). I76_MIRROR_WARP=turn: the turn alone on the rear too; "0", the
    /// schedule without any warp (the stale picture repeated, as Alternate shows it); both for comparisons.
    /// </summary>
    private static readonly string? WarpHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_WARP");
    private static readonly bool WarpDepth = WarpHook is not ("turn" or "0"), WarpNone = WarpHook == "0";
    /// <summary>Each mirror camera's depth effect (rear, left, right) under Smooth.</summary>
    private readonly MirrorDepth?[] _depth = new MirrorDepth?[3];

    private static readonly StringName SWarpOn = "warp_on", SViewWarp = "view_warp", SWarpDepth = "warp_depth", SWarpTurn = "warp_turn", SWarpShift = "warp_shift",
        STanNow = "tan_now", STanThen = "tan_then", SViewDepth = "view_depth", SHazeVeil = "haze_veil", SHazeBlur = "haze_blur", SHazeTint = "haze_tint", SAttach = "attach_m", SViewScale = "view_scale";

    /// <summary>One glass's re-projection this frame: off, or the homography and (with depth) the turn and move, the fields' tangents and the render holding the depth.</summary>
    private readonly record struct WarpState(bool On, Basis H, Basis Turn, Vector3 Shift, Vector2 TanNow, Vector2 TanThen, Texture2D? Depth, bool Move, float Attach);

    /// <summary>
    /// Metres within which a door mirror camera's render is the car's own flank (MirrorWarp's attach rule): level and looking back from
    /// beside the cab at a door's height, the ground first shows about 6 m out, the flank is within 5.
    /// </summary>
    private const float AttachMetres = 5f;

    private WarpState WarpOf(int mirror, Camera3D? cam, ((float X, float Y) Shown, (float X, float Y) Drawn) tan, Texture2D? depth)
    {
        if (_warpPeriod == 0 || cam is null || _warpDrawn[mirror] is not { } then) return default;
        var now = WarpNone ? then : cam.GlobalTransform;   // I76_MIRROR_WARP=0: the overscan's crop alone, the picture as drawn
        var h = MirrorWarp.Homography(then.Basis, now.Basis, tan.Shown, tan.Drawn);
        // the depth only once its effect runs on the camera and has made its texture: before, the texture is the engine's placeholder
        // (a checkerboard), so the turn alone (MirrorWarp.DepthUsable)
        bool usable = MirrorWarp.DepthUsable(_depthOn[mirror], _depth[mirror]?.Made == true);
        if (!usable) depth = null;
        // the side cameras keep the car's own flank still (their depth, MirrorDepth); the move from the depth with I76_MIRROR_WARP=depth
        float attach = mirror > 0 && depth is not null && OwnSelfHook ? AttachMetres : 0f;
        return new(true, h, MirrorWarp.Turn(then.Basis, now.Basis), MirrorWarp.Shift(then, now.Origin), new(tan.Shown.X, tan.Shown.Y), new(tan.Drawn.X, tan.Drawn.Y),
            depth, mirror == 0 && WarpDepth && depth is not null && !WarpNone, attach);
    }

    private void SetWarp(ShaderMaterial? m, in WarpState w)
    {
        if (m is null) return;
        m.SetShaderParameter(SWarpOn, w.On);
        if (!w.On) return;
        m.SetShaderParameter(SViewWarp, w.H);
        m.SetShaderParameter(SWarpDepth, w.Move);
        m.SetShaderParameter(SAttach, w.Attach);
        if (w.Depth is null) return;
        m.SetShaderParameter(SViewScale, new Vector2(w.TanNow.X / w.TanThen.X, w.TanNow.Y / w.TanThen.Y));
        m.SetShaderParameter(SWarpTurn, w.Turn);
        m.SetShaderParameter(SWarpShift, w.Shift);
        m.SetShaderParameter(STanNow, w.TanNow);
        m.SetShaderParameter(STanThen, w.TanThen);
        m.SetShaderParameter(SViewDepth, w.Depth);
    }

    private WarpState _rearWarp;
    private bool _warpWas;

    /// <summary>Each glass given its camera's re-projection for this frame (off outside Smooth: the shader's one test).</summary>
    private void Warp()
    {
        if (_mirrorMat is null) return;
        if (_warpPeriod == 0 && !_warpWas) return;
        _warpWas = _warpPeriod > 0;
        DepthPass();
        foreach (var d in _depth) d?.Sync();
        _rearWarp = WarpOf(0, _mirrorCam, _rearTan, _depth[0]?.Texture);
        SetWarp(_mirrorMat, _rearWarp);
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            var w = _ownOn ? WarpOf(1 + i, _ownCams[i], _ownTan, _depth[1 + i]?.Texture) : _rearWarp;
            SetWarp(left ? _sideMatL : _sideMatR, w);
            foreach (var g in _glass) if (g.Left == left) SetWarp(g.Mat, w);
        }
    }

    /// <summary>The 2D dash's mirror (slot 7) given the rear glass's re-projection and haze.</summary>
    public void MirrorLookTo(ShaderMaterial m)
    {
        SetWarp(m, _rearWarp);
        Haze(m);
    }

    /// <summary>
    /// The depth effects (MirrorDepth) under Smooth: on the rear camera (its move re-projected) and the side cameras (their flank held
    /// still); none otherwise.
    /// </summary>
    private void DepthPass()
    {
        for (int i = 0; i < 3; i++)
        {
            _depthOn[i] = false;
            if ((i == 0 ? _mirrorCam : _ownCams[i - 1]) is not { } cam) continue;
            bool on = _warpPeriod > 0 && !_faithful && (WarpDepth || (i > 0 && OwnSelfHook));
            if (on && _depth[i] is null) _depth[i] = new MirrorDepth();
            // in the camera's compositor beside the smoke layer's effect (CompositorShare), checked each frame (Warp)
            if (_depth[i] is { } d) _depthOn[i] = _depthSlots[i].Keep(cam, d, on);
        }
    }

    /// <summary>Each depth effect's place on its camera (rear, left, right), and whether it is in the camera's compositor.</summary>
    private readonly CompositorShare.Slot[] _depthSlots = [new(), new(), new()];
    private readonly bool[] _depthOn = new bool[3];

    // ---- Mirror Haze (MirrorHazeRow): the modern look's glass a little milky and soft ----

    private int _mirrorHaze = MirrorHazeRow.Default;
    /// <summary>I76_MIRROR_HAZE=0..3 (off, low, med, high): the level instead of the setting's (captures).</summary>
    private static readonly int? HazeHook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MIRROR_HAZE"), out int hh) ? MirrorHazeRow.Clamp(hh) : null;

    private void Haze(ShaderMaterial? m)
    {
        if (m is null) return;
        var (veil, blur, tint) = _faithful ? (0f, 0f, Vector3.One) : MirrorHazeRow.Of(HazeHook ?? _mirrorHaze);
        m.SetShaderParameter(SHazeVeil, veil);
        m.SetShaderParameter(SHazeBlur, blur);
        m.SetShaderParameter(SHazeTint, new Color(tint.X, tint.Y, tint.Z));
    }

    private void ApplyHaze()
    {
        Haze(_mirrorMat);
        Haze(_sideMatL);
        Haze(_sideMatR);
        foreach (var g in _glass) Haze(g.Mat);
    }

    // ---- Mirror AA (MirrorAaRow): the mirror renders' own anti-aliasing (a SubViewport takes none from the main view) ----

    private int _mirrorAa = MirrorAaRow.Default, _viewMsaa;
    private bool _viewSmaa;
    /// <summary>I76_MIRROR_AA=0..6 (off, 2x, 4x, 8x, smaa, 2x+smaa, as view): the choice instead of the setting's (captures).</summary>
    private static readonly int? AaHook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MIRROR_AA"), out int ah) ? MirrorAaRow.Clamp(ah) : null;

    private void ApplyAa()
    {
        var aa = MirrorAaRow.Of(AaHook ?? _mirrorAa, _faithful, I76.Game.Boot.RenderBackend.Compatibility, _viewMsaa, _viewSmaa);
        if (_mirrorView is not null) MirrorAaRow.Apply(_mirrorView, aa);
        foreach (var vp in _ownViews) if (vp is not null) MirrorAaRow.Apply(vp, aa);
    }

    // ---- side-view mirrors (the port's own, docs/deviations.md: two more glasses of the one render, MirrorBands) ----

    private MeshInstance3D? _sideL, _sideR;
    private ShaderMaterial? _sideMatL, _sideMatR;
    private MeshInstance3D? _mirrorHost;
    private int _mirrorSurface = -1;
    private Vector3 _mirrorCentre;
    private float _mirrorHeight;
    private SideMirrorMode _sideMirrors = SideMirrorMode.Auto;
    private SideMirrorRender _sideRender = SideMirrorRender.Own;
    private MirrorRate _mirrorRate = MirrorRate.Alternate;
    private int _roundSetting = MirrorRound.DefaultRadius, _glassRoundSetting = GlassRound.DefaultShare;
    private static readonly string? GlassRoundHook = System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_ROUND");
    private (byte[]? Rgba, int W, int H) _mirrorOriginal;
    private float _mirrorFov = MirrorBands.DefaultFov, _sideShare = 1f, _sideAngle = MirrorBands.DefaultYaw, _sideDrop = MirrorBands.DefaultDrop, _sideOverlap = MirrorBands.DefaultOverlap, _sideYaw = float.NaN;
    /// <summary>I76_MIRROR_WIDTH: the wide render's width in pixels instead of the one keeping the rear mirror's pixels per degree (a cost measurement); I76_SIDE_MIRROR_MAP=cylinder: the side glasses' columns spread by angle (docs/dev-hooks.md).</summary>
    private static readonly int? MirrorWidthOverride = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MIRROR_WIDTH"), out int w) && w >= 256 ? w : null;
    private static readonly bool SideCylindrical = System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_MAP") == "cylinder";

    /// <summary>[port] side_mirrors, mirror_fov, side_mirror_share, side_mirror_angle, side_mirror_drop, side_mirror_overlap (docs/playing.md), applied with the settings.</summary>
    public void ApplySideMirrors(I76.Game.Shell.ShellSettings s)
    {
        _sideMirrors = s.SideMirrors;
        _mirrorFov = s.MirrorFov;
        _sideShare = s.SideMirrorShare / 100f;
        _sideAngle = s.SideMirrorAngle;
        _sideDrop = s.SideMirrorDrop / 100f;
        _sideOverlap = s.SideMirrorOverlap;
        _sideRender = s.SideMirrorRender;
        _mirrorRate = s.MirrorRate;
        _mirrorHaze = s.MirrorHaze;
        (_mirrorAa, _viewMsaa, _viewSmaa) = (s.MirrorAa, s.Graphics[0x8], I76.Game.Shell.AntiAliasingRow.SmaaOn(s));
        _rimOutSetting = s.SideMirrorRimOut;
        _faceOverSetting = [s.SideMirrorFaceOverLeft, s.SideMirrorFaceOverRight];
        if (_roundSetting != s.MirrorRound || _glassRoundSetting != s.SideMirrorRound) { _roundSetting = s.MirrorRound; _glassRoundSetting = s.SideMirrorRound; RoundCorners(); }
        _sideYaw = float.NaN;
        ApplyMirrorMode();
    }

    /// <summary>The side mirrors shown or hidden and their bands of the render set; made on first use from the rear mirror's own face.</summary>
    private void SideMirrors(bool on, float fov)
    {
        if (on && _sideL is null) MakeSideMirrors();
        if (_sideL is null || _sideR is null || _sideMatL is null || _sideMatR is null) return;
        _sideL.Visible = on && MirrorBands.SideShown(_sideMirrors, true);
        _sideR.Visible = on && MirrorBands.SideShown(_sideMirrors, false);
        if (!on) return;
        float t = MathF.Tan(MirrorBands.Fov(fov) * MathF.PI / 360f);
        foreach (var (m, left) in new[] { (_sideMatL, true), (_sideMatR, false) })
        {
            var band = MirrorBands.Side(fov, _sideShare, _sideOverlap, left);
            var angles = MirrorBands.Angles(band, fov);
            m.SetShaderParameter("band", new Vector2(band.From, band.To));
            m.SetShaderParameter("band_angles", new Vector2(angles.From, angles.To));
            m.SetShaderParameter("fov_tan", t);
            m.SetShaderParameter("cylindrical", SideCylindrical);
            m.SetShaderParameter("hidden", _mirrorMode == 0);
        }
        _sideYaw = float.NaN;
        SideMirrorYaw();
    }

    /// <summary>
    /// The two side mirrors from the rear mirror's face: the same quad half as wide about its middle (the art's two end quarters,
    /// the shader's art_crop) in the car's frame, each turned about the eye to its side by SideMirrorYaw. On the cockpit's layer, so
    /// the mirror camera does not see them, and under the interior's node, so they go with it.
    /// </summary>
    private void MakeSideMirrors()
    {
        if (_mirrorHost is not { Mesh: ArrayMesh mesh } host || _mirrorSurface < 0 || _mirrorMat is null) return;
        var arrays = mesh.SurfaceGetArrays(_mirrorSurface);
        var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (v.Length < 3) return;
        var centre = Vector3.Zero;
        foreach (var p in v) centre += p;
        centre /= v.Length;
        // the quad about its own middle, half as wide: SideMirrorYaw puts it where it goes
        var half = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) half[i] = (v[i] - centre) * new Vector3(0.5f, 1, 1);
        _mirrorCentre = centre;
        _mirrorHeight = v.Max(p => p.Y) - v.Min(p => p.Y);
        var side = new ArrayMesh();
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        a[(int)Mesh.ArrayType.Vertex] = half;
        a[(int)Mesh.ArrayType.Normal] = arrays[(int)Mesh.ArrayType.Normal];
        a[(int)Mesh.ArrayType.TexUV] = arrays[(int)Mesh.ArrayType.TexUV];
        I76.Game.MeshArrays.AddSurfaceArrays(side, Mesh.PrimitiveType.Triangles, a);   // the arrays held while the engine reads them
        _sideMatL = (ShaderMaterial)_mirrorMat.Duplicate();
        _sideMatR = (ShaderMaterial)_mirrorMat.Duplicate();
        _sideMatL.SetShaderParameter("art_crop", true);
        _sideMatR.SetShaderParameter("art_crop", true);
        if (_mirrorOriginal.Rgba is { } rgba)
        {
            var (fr, fg, fb) = MirrorRim.FrameColour(rgba, _mirrorOriginal.W, _mirrorOriginal.H);
            foreach (var sm in new[] { _sideMatL, _sideMatR }) sm.SetShaderParameter("rim_colour", new Color(fr / 255f, fg / 255f, fb / 255f));
        }
        // the quad's plane by the art's pixels the shader's frame is measured in (each half shows a quarter of the art across)
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var art = _mirrorOriginal.Rgba is not null ? new Vector2(_mirrorOriginal.W, _mirrorOriginal.H) : new Vector2(256, 64);
        if (uvs.Length == half.Length && GlassRound.Plane([.. half.Select((p, i) => (new Vector2(uvs[i].X * 0.5f * art.X, uvs[i].Y * art.Y), p))]) is { } plane)
            foreach (var sm in new[] { _sideMatL, _sideMatR })
            {
                sm.SetShaderParameter("pane_mid", half.Aggregate(Vector3.Zero, (a, b) => a + b) / half.Length);
                sm.SetShaderParameter("pane_a", plane.A); sm.SetShaderParameter("pane_b", plane.B); sm.SetShaderParameter("pane_half", new Vector2(0.25f * art.X, 0.5f * art.Y));
            }
        AddedFrame(_sideMatL);
        AddedFrame(_sideMatR);
        _sideL = new MeshInstance3D { Mesh = side, MaterialOverride = _sideMatL, Layers = CarNode.CockpitLayer, Name = "MirrorL" };
        _sideR = new MeshInstance3D { Mesh = side, MaterialOverride = _sideMatR, Layers = CarNode.CockpitLayer, Name = "MirrorR" };
        host.AddChild(_sideL);
        host.AddChild(_sideR);
    }

    /// <summary>
    /// Where the side mirrors sit: the wanted yaw from straight ahead, kept inside the window's view (MirrorBands.Yaw: the view's
    /// half field across follows the window's shape and the Field of View row), each the rear mirror's place turned about the eye's
    /// vertical axis from straight ahead (the rear mirror itself hangs to the right of the driver's eye, and on a wide window its
    /// right end reaches 50 degrees: a side mirror in its row would lie over it), hung below the rear mirror's row by side_mirror_drop
    /// of its height, at its distance, the sides facing the eye as the rear does.
    /// </summary>
    private void SideMirrorYaw()
    {
        if (_sideL is null || _sideR is null || Node3D.CockpitCamera is not { } cam) return;
        var size = cam.GetViewport()?.GetVisibleRect().Size ?? new Vector2(4, 3);
        float half = cam.KeepAspect == Camera3D.KeepAspectEnum.Width ? cam.Fov / 2 : MirrorBands.HalfFovAcross(cam.Fov, size.X / MathF.Max(size.Y, 1));
        float yaw = MirrorBands.Yaw(_sideAngle, half);
        if (MathF.Abs(yaw - _sideYaw) < 0.05f) return;
        _sideYaw = yaw;
        var eye = Node3D.Eye;
        var o = _mirrorCentre - eye;
        // the rear mirror's height and distance put straight ahead of the eye (forward is -Z here), then turned to each side: a
        // point ahead turned about the eye's up by a positive angle goes to the driver's left in this frame
        var ahead = new Vector3(0, o.Y - _sideDrop * _mirrorHeight, -MathF.Sqrt(o.X * o.X + o.Z * o.Z));
        var l = new Basis(Vector3.Up, Mathf.DegToRad(yaw));
        var r = new Basis(Vector3.Up, Mathf.DegToRad(-yaw));
        _sideL.Transform = new Transform3D(l, eye + l * ahead);
        _sideR.Transform = new Transform3D(r, eye + r * ahead);
    }

    // ---- the door mirrors' own glass (MirrorGlass: the Piranha's two, the CC cars' left one) ----

    /// <summary>A door mirror's glass: its mesh (Faces, the pane's own; or the plane's grown one while the moulding's outer edge reaches past it), its material, side, picture aspect, middle, convex outline in the shader's units, the plane its picture coordinates map onto, the driver's eye in its frame, and the outline the ring covers (GlassRound.Housing: the glass's and the housing's rim seen past it).</summary>
    private sealed record GlassPane(MeshInstance3D Mesh, ShaderMaterial Mat, bool Left, float Aspect, Vector3 Centre, List<Vector2> Hull, ArrayMesh Faces, (Vector3 O, Vector3 A, Vector3 B)? Plane, Vector3 Eye, List<Vector2> Cover)
    {
        public (float Grow, bool Dark, float R) GrownFor = (float.NaN, false, float.NaN);
        /// <summary>The glass tilted in its housing (MirrorAim.Tilt): Faces is then the head's own surface the tilted glass is drawn on as seen (TiltedGlass).</summary>
        public bool Tilted { get; init; }
        /// <summary>The proud face (DoorFace, side_mirror_face=proud): its plane and the housing end's outline it covers; Grown is its mesh, made for GrownFor's reach and share.</summary>
        public FacePlate? Face { get; init; }
        public ArrayMesh? Grown;
        /// <summary>The turned glass on the art's housing (side_mirror_face=turned): the node holding the glass turned as the turned head's, and whether it has been brought in front of the housing yet (TurnedClear).</summary>
        public Node3D? Holder { get; init; }
        public bool Cleared;
    }
    /// <summary>
    /// A proud face's plane (picture units as seen, u times Aspect and v, onto the head's frame: the end's plane, or the plane the face
    /// is turned to, through the end's middle), the end's points (the pane's corners and the housing's rim round it), the head's
    /// triangles (its housing and painted glass: the face stands in front of them) and the driver's eye, all in the head's frame.
    /// </summary>
    private sealed record FacePlate((Vector3 O, Vector3 A, Vector3 B) Plane, List<Vector3> End, List<(Vector3 A, Vector3 B, Vector3 C)> Tris, Vector3 Eye)
    {
        /// <summary>The plane's picture units in its own metres: an orthonormal frame (E1 along A, E2 the rest of B), M p = (A1 p.x + B1 p.y, B2 p.y).</summary>
        public (Vector3 E1, Vector3 E2, float A1, float B1, float B2) Metric
        {
            get
            {
                var e1 = Plane.A.Normalized(); var e2 = (Plane.B - e1 * Plane.B.Dot(e1)).Normalized();
                return (e1, e2, Plane.A.Dot(e1), Plane.B.Dot(e1), Plane.B.Dot(e2));
            }
        }
        public Vector2 ToMetres(Vector2 p) { var m = Metric; return new Vector2(m.A1 * p.X + m.B1 * p.Y, m.B2 * p.Y); }
        public Vector2 FromMetres(Vector2 q) { var m = Metric; float y = q.Y / m.B2; return new Vector2((q.X - m.B1 * y) / m.A1, y); }
    }
    private readonly List<GlassPane> _glass = [];
    /// <summary>I76_MIRROR_RIM_KEY=1: the rim flat magenta and the pane's outline outside it cyan (the grown glass then takes in the pane's outline, as with dark corners).</summary>
    private static readonly bool RimKeyHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_RIM_KEY") == "1";
    /// <summary>I76_MIRROR_POINTS=1: the door mirror head's vertices drawn as yellow dots and its glass's as green ones over everything, and both logged in the glass's picture units (mirror-points:), to find which points show past the ring.</summary>
    /// <summary>
    /// I76_MIRROR_RIM_POINTS: unset or "even", the ring one width all round, its outer edge a smooth outline the rim's reach past the
    /// housing's (GlassRound.EvenShape), "even:N" N rim widths (MirrorRim.OuterShare) past it instead; "corners" the outer edge's
    /// corners tighter than the glass's where the outline's are sharper, the band thicker there (the look before); "uniform" the whole
    /// ring grown evenly round the glass's rounded shape until the outline's corners fit inside it; "off" the outer edge as round as
    /// the glass's, the corners left to stick out.
    /// </summary>
    private static readonly string? PointsVariant = System.Environment.GetEnvironmentVariable("I76_MIRROR_RIM_POINTS");
    /// <summary>How far inside the ring's outer edge the glass outline's corners lie, in the glass's heights (about a pixel at 3440x1440).</summary>
    private const float PointMargin = 0.004f;
    private static readonly bool PointsHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_POINTS") == "1";

    private void MarkPoints(Node3D host, List<Vector3> glass, (Vector3 O, Vector3 A, Vector3 B) pl, Vector3 eye, bool left, float aspect, System.Numerics.Vector2[][] uvs)
    {
        var head = Node3D.MirrorHeads.FirstOrDefault(h => h.Left == left).Mesh;
        var verts = HeadPoints(left);
        // a point's picture units as seen from the eye: where the eye's ray through it meets the glass's plane
        Vector2? Seen(Vector3 q) => GlassRound.Seen(eye, pl, q)?.Uv;
        var n = pl.A.Cross(pl.B).Normalized(); if ((eye - pl.O).Dot(n) < 0) n = -n;
        var seen = new HashSet<(int, int)>();
        foreach (var q in verts.Distinct())
            if (Seen(q) is { } uv && seen.Add(((int)MathF.Round(uv.X * 1000), (int)MathF.Round(uv.Y * 1000))))
                GD.Print($"mirror-points: {(left ? "L" : "R")} head {uv.X:0.0000} {uv.Y:0.0000} plane {(q - pl.O).Dot(n):0.0000}");
        foreach (var uv in uvs.SelectMany(f => f)) GD.Print($"mirror-points: {(left ? "L" : "R")} glass {uv.X * aspect:0.0000} {uv.Y:0.0000}");
        foreach (var (pts, colour) in new[] { (verts, new Color(1, 1, 0)), (glass, new Color(0, 1, 0)) })
        {
            var a = new Godot.Collections.Array(); a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = pts.Distinct().ToArray();
            var pm = new ArrayMesh(); I76.Game.MeshArrays.AddSurfaceArrays(pm, Mesh.PrimitiveType.Points, a);
            var mat = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, depth_test_disabled, cull_disabled; uniform vec3 c; void vertex() { POINT_SIZE = 5.0; } void fragment() { ALBEDO = c; }" } };
            mat.SetShaderParameter("c", colour);
            var mi = new MeshInstance3D { Mesh = pm, MaterialOverride = mat, Layers = CarNode.CockpitLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            if (colour.R > 0.5f && head is not null) head.AddChild(mi); else host.AddChild(mi);
        }
    }
    /// <summary>The door mirror head's vertices (its own frame, as the glass under it), none where the car has no head.</summary>
    private List<Vector3> HeadPoints(bool left)
    {
        var verts = new List<Vector3>();
        if (Node3D.MirrorHeads.FirstOrDefault(h => h.Left == left).Mesh?.Mesh is { } hm)
            for (int si = 0; si < hm.GetSurfaceCount(); si++) verts.AddRange(hm.SurfaceGetArrays(si)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
        return verts;
    }

    private static readonly string? RimOutHook = System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_RIM_OUT");
    private int _rimOutSetting = GlassRound.DefaultRimOut;
    private readonly List<(MeshInstance3D Mesh, bool Left)> _rims = [];
    private ShaderMaterial? _rimMat;

    /// <summary>The door mirrors' glass shown or hidden and each given its band of the render; made on first use.</summary>
    private void GlassMirrors(bool on, float fov)
    {
        if (on && _glass.Count == 0) MakeGlass();
        // the heads turned to the driver while their glass shows the view (MirrorAim); the art's own place otherwise, the 1997 look's
        // Side Mirrors Left: the right one stays as the art paints and places it
        foreach (var (head, left) in Node3D.MirrorHeads) head.Transform = on && _aimed && MirrorBands.SideShown(_sideMirrors, left) ? _aim[left ? 0 : 1].Turn : Transform3D.Identity;
        int share = GlassRound.Share(GlassRoundHook, _glassRoundSetting);
        var rimOut = GlassRound.Out(RimOutHook, _rimOutSetting);
        foreach (var (r, left) in _rims) r.Visible = on && _mirrorMode != 0 && MirrorBands.SideShown(_sideMirrors, left) && !_glass.Any(g => g.Left == left && (g.Face is not null || GlassRound.Shape(g.Hull, share) is not null));
        // the housing's painted glass hidden behind a glass whose moulding is rounded outside too (its corners cut away)
        foreach (var (paint, left) in Node3D.MirrorPaint) { paint.Visible = true; paint.MaterialOverride = null; }
        foreach (var g in _glass)
        {
            g.Mesh.Visible = on && MirrorBands.SideShown(_sideMirrors, g.Left);
            if (!g.Mesh.Visible) { g.Mesh.Mesh = g.Faces; continue; }
            // the turned glass keeps the turned head's rounded ring (at least GlassRound.RoundedRimOut of the moulding's widths past the housing)
            var gRim = g.Holder is null ? rimOut : GlassRound.Out(RimOutHook, Math.Max(_rimOutSetting, GlassRound.RoundedRimOut));
            if (RoundGlass(g, share, gRim) && _mirrorMode != 0 && g.Holder is null)
                foreach (var (paint, left) in Node3D.MirrorPaint) if (left == g.Left) paint.Visible = false;
            if (g.Holder is not null && _mirrorMode != 0)
            {
                // the art's painted glass behind the turned glass: the shell's dark inside where it shows past it
                foreach (var (paint, left) in Node3D.MirrorPaint) if (left == g.Left) paint.MaterialOverride = ShellMaterial();
                if (!g.Cleared) TurnedClear(g);
            }
            if (g.Tilted && _mirrorMode != 0)
                foreach (var (paint, left) in Node3D.MirrorPaint) if (left == g.Left) paint.MaterialOverride = ShellMaterial();
            g.Mat.SetShaderParameter("view", _mirrorView?.GetTexture());
            var size = _mirrorView is null ? new Vector2I(1024, 256) : _mirrorShown;
            var band = MirrorBands.Glass(fov, _sideOverlap, g.Aspect, size.X / (float)size.Y, g.Left);
            g.Mat.SetShaderParameter("band", new Vector2(band.From, band.To));
            g.Mat.SetShaderParameter("hidden", _mirrorMode == 0);
        }
    }

    /// <summary>
    /// A mesh over each pane of glass, its faces 3 mm toward the eye (over the painted blue), both windings, on the cockpit's layer
    /// and under the interior's node; its UVs the glass as the driver sees it (MirrorGlass.Picture), so the band fills the glass's shape.
    /// </summary>
    private void MakeGlass()
    {
        if (Node3D.CockpitMesh is not { } cockpit || _mirrorMat is null) return;
        var eye = Node3D.Eye;
        var gameEye = new System.Numerics.Vector3(eye.X, eye.Y, -eye.Z);
        Aim();
        foreach (var pane in Glass)
        {
            // under the door mirror's head, so the glass and its rim turn with it; the picture as the driver sees the turned glass
            Node3D host = Node3D.MirrorHeads.FirstOrDefault(h => h.Left == pane.Left).Mesh ?? cockpit;
            Node3D? holder = null;
            if (_faceTurned && !_headTurned && host != cockpit)
            {
                // the turned head's glass and ring, exactly as side_mirror_head=turned draws them, on a node of its own turned as that head
                // is: the housing stays where the art puts it; TurnedClear later brings the glass in front of it along the eye's rays
                holder = new Node3D { Name = "TurnedGlass" + (pane.Left ? "L" : "R"), Transform = _glassTurn[pane.Left ? 0 : 1] };
                host.AddChild(holder);
                host = holder;
            }
            else if (_faceProud && !_headTurned && host != cockpit && FaceGlass(pane, _tilt[pane.Left ? 0 : 1]?.Q ?? System.Numerics.Quaternion.Identity, (MeshInstance3D)host, gameEye) is { } face)
            {
                _glass.Add(face);
                continue;
            }
            if (holder is null && _tilt[pane.Left ? 0 : 1] is { } tilt && !_headTurned && host != cockpit && TiltedGlass(pane, tilt, (MeshInstance3D)host, gameEye) is { } tilted)
            {
                _glass.Add(tilted);
                continue;
            }
            var (turn, pivot) = _aimTurn[pane.Left ? 0 : 1];
            var seen = pane with { Faces = pane.Faces.Select(f => f.Select(p => pivot + System.Numerics.Vector3.Transform(p - pivot, turn)).ToArray()).ToList() };
            var near = (holder is not null ? _glassTurn[pane.Left ? 0 : 1] : _aim[pane.Left ? 0 : 1].Turn).AffineInverse() * eye;   // the eye in the head's (or the turned glass's) frame
            var (uvs, aspect) = I76.Formats.Assembly.MirrorGlass.Picture(seen, gameEye);
            var v = new List<Vector3>();
            var t = new List<Vector2>();
            for (int f = 0; f < pane.Faces.Count; f++)
            {
                var pts = pane.Faces[f].Select(q => { var g = Coords.ToGodot(q); return g + (near - g).Normalized() * 0.003f; }).ToArray();
                for (int k = 1; k + 1 < pts.Length; k++)
                    foreach (var i in new[] { 0, k, k + 1, 0, k + 1, k })
                    {
                        v.Add(pts[i]);
                        t.Add(new Vector2(uvs[f][i].X, uvs[f][i].Y));
                    }
            }
            var a = new Godot.Collections.Array();
            a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = v.ToArray();
            a[(int)Mesh.ArrayType.TexUV] = t.ToArray();
            var mesh = new ArrayMesh();
            I76.Game.MeshArrays.AddSurfaceArrays(mesh, Mesh.PrimitiveType.Triangles, a);
            var mat = (ShaderMaterial)_mirrorMat.Duplicate();
            mat.SetShaderParameter("pane", true);
            if (GlassKeyHook) mat.SetShaderParameter("glass_key", true);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Layers = CarNode.CockpitLayer, Name = "Glass" + (pane.Left ? "L" : "R"),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            host.AddChild(mi);
            var centre = Vector3.Zero; foreach (var q in v) centre += q; centre /= Math.Max(1, v.Count);
            var plane = GlassRound.Plane([.. v.Zip(t, (p, uv) => (new Vector2(uv.X * aspect, uv.Y), p))]);
            var hull = GlassRound.Hull(uvs.SelectMany(f => f).Select(q => new Vector2(q.X * aspect, q.Y)));
            var cover = plane is { } hp ? GlassRound.Housing(hull, HeadPoints(pane.Left).Distinct().Select(q => GlassRound.Seen(near, hp, q)).OfType<(Vector2, float)>()) : hull;
            if (holder is not null && plane is { } tp) cover = TurnedCover(hull, cover, tp, near, pane.Left);
            _glass.Add(new GlassPane(mi, mat, pane.Left, aspect, centre, hull, mesh, plane, near, cover) { Holder = holder });
            if (PointsHook && plane is { } pl) MarkPoints(host, v, pl, near, pane.Left, aspect, uvs);
            // the rim inside the pane's outline, a hair nearer the eye than the glass, the rear frame's colour (MirrorRim)
            _rimMat ??= RimMaterial();
            var shifted = pane.Faces.Select(f => f.Select(q => { var g = Coords.ToGodot(q); return g + (near - g).Normalized() * 0.004f; }).ToArray()).ToList();
            foreach (var loop in MirrorRim.Outline(shifted))
            {
                var (rv, ruv) = MirrorRim.Ring(loop, MirrorRim.Width(loop));
                var ra = new Godot.Collections.Array();
                ra.Resize((int)Mesh.ArrayType.Max);
                ra[(int)Mesh.ArrayType.Vertex] = rv;
                ra[(int)Mesh.ArrayType.TexUV] = ruv;
                var rm = new ArrayMesh();
                I76.Game.MeshArrays.AddSurfaceArrays(rm, Mesh.PrimitiveType.Triangles, ra);
                var rim = new MeshInstance3D { Mesh = rm, MaterialOverride = _rimMat, Layers = CarNode.CockpitLayer, Name = "Rim" + (pane.Left ? "L" : "R"), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                host.AddChild(rim);
                _rims.Add((rim, pane.Left));
            }
        }
    }

    /// <summary>
    /// A door mirror's glass tilted in its housing (MirrorAim.Tilt): the pane turned about its outer corner, its picture coordinates as
    /// the driver sees the turned glass (MirrorGlass.Picture), and the glass drawn where the eye sees it, on the head's own faces (housing
    /// and painted glass, each moved 3 mm toward the eye along the eye's ray, so nothing moves on screen): the shader finds each pixel's
    /// point on the turned glass's plane along the eye's ray (cockpit_mirror.gdshader's decal) and draws the glass where that lies
    /// inside its outline. So the glass is never seen outside the housing's outline, whatever the angle, the housing hides and is hidden
    /// as before, and the housing's painted glass where the turned glass leaves it shows the shell's dark inside (ShellMaterial).
    /// </summary>
    private GlassPane? TiltedGlass(I76.Formats.Assembly.MirrorGlass.Pane pane, (System.Numerics.Quaternion Q, System.Numerics.Vector3 Pivot) tilt, MeshInstance3D head, System.Numerics.Vector3 gameEye)
    {
        if (_mirrorMat is null || head.Mesh is not { } hm) return null;
        var faces = MirrorAim.Tilted(pane.Faces, tilt.Q, tilt.Pivot);
        var (uvs, aspect) = I76.Formats.Assembly.MirrorGlass.Picture(pane with { Faces = faces }, gameEye);
        var pts = new List<(Vector2, Vector3)>();
        for (int f = 0; f < faces.Count; f++)
            for (int k = 0; k < faces[f].Length; k++) pts.Add((new Vector2(uvs[f][k].X * aspect, uvs[f][k].Y), Coords.ToGodot(faces[f][k])));
        if (GlassRound.Plane(pts) is not { } plane) return null;
        var hull = GlassRound.Hull(pts.Select(q => q.Item1));
        if (hull.Count < 3 || hull.Count > GlassRound.MaxCorners) return null;
        var eye = Node3D.Eye;   // the head is at the art's place: its frame is the cockpit's
        var v = new List<Vector3>();
        foreach (var m in new[] { hm }.Concat(Node3D.MirrorPaint.Where(p => p.Left == pane.Left).Select(p => p.Mesh.Mesh)).OfType<Mesh>())
            for (int si = 0; si < m.GetSurfaceCount(); si++)
            {
                var arr = m.SurfaceGetArrays(si);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int n = idx?.Length ?? verts.Length;
                for (int k = 0; k < n; k++) { var q = verts[idx?[k] ?? k]; v.Add(q + (eye - q).Normalized() * 0.003f); }
            }
        if (v.Count < 3) return null;
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        a[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        var mesh = new ArrayMesh();
        I76.Game.MeshArrays.AddSurfaceArrays(mesh, Mesh.PrimitiveType.Triangles, a);
        var mat = (ShaderMaterial)_mirrorMat.Duplicate();
        mat.SetShaderParameter("pane", true);
        mat.SetShaderParameter("decal", true);
        mat.SetShaderParameter("decal_o", plane.O); mat.SetShaderParameter("decal_a", plane.A); mat.SetShaderParameter("decal_b", plane.B);
        mat.SetShaderParameter("pane_aspect", aspect);
        var hullArr = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < hull.Count; i++) hullArr[i] = hull[i];
        mat.SetShaderParameter("pane_hull", hullArr);
        mat.SetShaderParameter("pane_hull_n", hull.Count);
        if (GlassKeyHook) mat.SetShaderParameter("glass_key", true);
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Layers = CarNode.CockpitLayer, Name = "Glass" + (pane.Left ? "L" : "R"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        head.AddChild(mi);
        var centre = faces.SelectMany(f => f).Aggregate(System.Numerics.Vector3.Zero, (x, y) => x + y) / faces.Sum(f => f.Length);
        return new GlassPane(mi, mat, pane.Left, aspect, Coords.ToGodot(centre), hull, mesh, plane, eye, hull) { Tilted = true };
    }

    /// <summary>
    /// A door mirror's proud face (DoorFace): the pane turned by <paramref name="turn"/> about its middle (side_mirror_tilt_right /
    /// _left; none, the end's own plane), its picture coordinates as the driver sees it (MirrorGlass.Picture), and the housing end's
    /// outline (the pane's corners and the housing's rim round it, GlassRound.Housing's points) as the eye sees it on that plane. The
    /// face's mesh and shape are made by FaceRound, with the glass's corner share and the sides' reach.
    /// </summary>
    private GlassPane? FaceGlass(I76.Formats.Assembly.MirrorGlass.Pane pane, System.Numerics.Quaternion turn, MeshInstance3D head, System.Numerics.Vector3 gameEye)
    {
        if (_mirrorMat is null) return null;
        var all = pane.Faces.SelectMany(f => f).ToArray();
        var mid = all.Aggregate(System.Numerics.Vector3.Zero, (a, b) => a + b) / all.Length;
        var faces = MirrorAim.Tilted(pane.Faces, turn, mid);
        var (uvs, aspect) = I76.Formats.Assembly.MirrorGlass.Picture(pane with { Faces = faces }, gameEye);
        var pts = new List<(Vector2, Vector3)>();
        for (int f = 0; f < faces.Count; f++)
            for (int k = 0; k < faces[f].Length; k++) pts.Add((new Vector2(uvs[f][k].X * aspect, uvs[f][k].Y), Coords.ToGodot(faces[f][k])));
        if (GlassRound.Plane(pts) is not { } plane) return null;
        var eye = Node3D.Eye;   // the head is at the art's place: its frame is the cockpit's
        var endMid = Coords.ToGodot(mid);
        var n = Coords.ToGodot(pane.Normal).Normalized();
        if ((eye - endMid).Dot(n) < 0) n = -n;
        // the end: the pane's corners and the head's points on its plane or just behind it (the housing's rim), as the eye sees them on the face's plane
        var endPts = all.Select(Coords.ToGodot).Concat(HeadPoints(pane.Left).Distinct().Where(q => (q - endMid).Dot(n) is >= -GlassRound.HousingDepth and <= 0.01f));
        List<Vector3> end = [.. endPts];
        if (GlassRound.Hull(end.Select(q => GlassRound.Seen(eye, plane, q)).OfType<(Vector2 Uv, float Depth)>().Select(q => q.Uv)).Count < 3) return null;
        var mat = (ShaderMaterial)_mirrorMat.Duplicate();
        mat.SetShaderParameter("pane", true);
        mat.SetShaderParameter("pane_aspect", aspect);
        if (GlassKeyHook) mat.SetShaderParameter("glass_key", true);
        var mesh = new ArrayMesh();
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Layers = CarNode.CockpitLayer, Name = "Glass" + (pane.Left ? "L" : "R"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        head.AddChild(mi);
        if (TiltLog)
            GD.Print($"mirror-face: {(pane.Left ? "L" : "R")} turned {MirrorAim.AngleDegrees(turn):0.0} degrees, seen {MirrorAim.OffFace(gameEye, mid, System.Numerics.Vector3.Transform(pane.Normal, turn)):0.0} off its face ({MirrorAim.OffFace(gameEye, mid, pane.Normal):0.0} as the art sets it)");
        var tris = new List<(Vector3, Vector3, Vector3)>();
        foreach (var hm in new[] { head.Mesh }.Concat(Node3D.MirrorPaint.Where(p => p.Left == pane.Left).Select(p => p.Mesh.Mesh)).OfType<Mesh>())
            for (int si = 0; si < hm.GetSurfaceCount(); si++)
            {
                var arr = hm.SurfaceGetArrays(si);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int count = idx?.Length ?? verts.Length;
                for (int k = 0; k + 2 < count; k += 3) tris.Add((verts[idx?[k] ?? k], verts[idx?[k + 1] ?? k + 1], verts[idx?[k + 2] ?? k + 2]));
            }
        return new GlassPane(mi, mat, pane.Left, aspect, endMid, [], mesh, plane, eye, []) { Face = new FacePlate(plane, end, tris, eye) };
    }

    /// <summary>I76_SIDE_MIRROR_FACE: proud or inset in place of [port] side_mirror_face; I76_SIDE_MIRROR_FACE_OVER=L,R the sides' reach in place of side_mirror_face_over_left / _right (review sheets).</summary>
    private static readonly string? FaceHook = System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_FACE");
    private static readonly (int L, int R)? FaceOverHook = DoorFace.OverOverride(System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_FACE_OVER"));
    /// <summary>[port] side_mirror_face (DoorFace.Proud): the proud face, the default; read with the cockpit (the glass is made once). side_mirror_face_over_left / _right: the sides' reach, live.</summary>
    private bool _faceProud;
    /// <summary>[port] side_mirror_face=turned (DoorFace.Turned): the turned head's glass and ring on the art's housing; read with the cockpit.</summary>
    private bool _faceTurned;
    /// <summary>Each side's turn of the turned head (MirrorAim.Turn about the glass's middle, Godot axes, the cockpit's frame), for side_mirror_face=turned; left first.</summary>
    private readonly Transform3D[] _glassTurn = [Transform3D.Identity, Transform3D.Identity];
    private int[] _faceOverSetting = [DoorFace.DefaultOverLeft, DoorFace.DefaultOverRight];
    /// <summary>How far proud of the end's plane the face's nearest point stands, metres (I76_SIDE_MIRROR_FACE_CLEAR=mm for a review sheet).</summary>
    private static readonly float FaceClear = float.TryParse(System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_FACE_CLEAR"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fc) ? Math.Clamp(fc, 0f, 100f) / 1000f : 0.003f;

    /// <summary>
    /// The proud face's shape (DoorFace.Fit, in the plane's metres) for the glass's corner share and the sides' reach, fitted to the
    /// end's outline as the eye sees it on the face's plane; the plane slid along its normal until the whole face stands FaceClear in
    /// front of the housing along every eye's ray that meets it (a face turned about the end's middle would put its inner half inside
    /// the housing: it stands out on the driver's side instead, as a glass on its own mount; refitted at each step). Its mesh, and the shader's ring: the outer
    /// edge the rounded rectangle, the glass one rim's width inside it, the shape's units its own (face_ux, face_uy, face_c from the
    /// picture units), the whole picture spread over the glass.
    /// </summary>
    private void FaceRound(GlassPane g, FacePlate f, int share)
    {
        var m = g.Mat;
        int over = FaceOverHook is { } oh ? (g.Left ? oh.L : oh.R) : _faceOverSetting[g.Left ? 0 : 1];
        var metric = f.Metric;
        var up = new Vector2(Vector3.Up.Dot(metric.E1), Vector3.Up.Dot(metric.E2));
        var (o, a, b) = f.Plane;
        var nt = a.Cross(b).Normalized();
        if ((f.Eye - o).Dot(nt) < 0) nt = -nt;
        float slide = 0;
        DoorFace.Shape shape = default;
        List<Vector2> end = [];
        var plane = f.Plane;
        for (int step = 0; step < 6; step++)
        {
            plane = (o + nt * slide, a, b);
            var pl = plane;
            end = GlassRound.Hull(f.End.Select(q => GlassRound.Seen(f.Eye, pl, q)).OfType<(Vector2 Uv, float Depth)>().Select(q => q.Uv));
            shape = DoorFace.Fit([.. end.Select(f.ToMetres)], up, share, over);
            var sh = shape;
            // each sample's shortfall along its ray, as a slide of the plane along its normal
            float shortBy = DoorFace.Samples(shape).Select(q => { var p = f.FromMetres(sh.ToPlane(q)); var x = pl.Item1 + pl.Item2 * p.X + pl.Item3 * p.Y;
                float sb = DoorFace.Short(f.Eye, x, f.Tris, FaceClear); return sb == float.MinValue ? sb : sb * MathF.Abs((x - f.Eye).Normalized().Dot(nt)); })
                // and the housing's corners seen inside the face (a corner between the samples would poke through it)
                .Concat(f.Tris.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().Select(v =>
                {
                    if (GlassRound.Seen(f.Eye, pl, v) is not { } seen || !sh.Inside(sh.ToFace(f.ToMetres(seen.Uv)))) return float.MinValue;
                    return seen.Depth + FaceClear;
                })).Max();
            if (shortBy == float.MinValue || MathF.Abs(shortBy) < 1e-4f) break;
            slide += shortBy;
        }
        Vector2 Q(Vector2 p) => shape.ToFace(f.ToMetres(p));
        Vector2 P(Vector2 q) => f.FromMetres(shape.ToPlane(q));
        var q0 = Q(Vector2.Zero);
        float ringW = DoorFace.RimShare;
        m.SetShaderParameter("pane_round", true);
        m.SetShaderParameter("rim_round", true);
        m.SetShaderParameter("face_on", true);
        m.SetShaderParameter("face_ux", Q(new Vector2(1, 0)) - q0); m.SetShaderParameter("face_uy", Q(new Vector2(0, 1)) - q0); m.SetShaderParameter("face_c", q0);
        var core = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < shape.Core.Count; i++) core[i] = shape.Core[i];
        m.SetShaderParameter("pane_ocore", core);
        m.SetShaderParameter("pane_ocorners", shape.Core.Count);
        m.SetShaderParameter("pane_or", shape.R);
        m.SetShaderParameter("rim_out", 0f);
        m.SetShaderParameter("ring_w", ringW);
        m.SetShaderParameter("ring_from_outer", true);
        m.SetShaderParameter("rim_dark", false);
        _rimMat ??= RimMaterial();
        m.SetShaderParameter("rim_colour", _rimMat.GetShaderParameter("colour"));
        // the end's outline in the face's units: the key's (I76_MIRROR_RIM_KEY) housing outline
        var endQ = GlassRound.Hull(end.Select(Q));
        var hull = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < endQ.Count && i < hull.Length; i++) hull[i] = endQ[i];
        m.SetShaderParameter("pane_hull", hull);
        m.SetShaderParameter("pane_hull_n", endQ.Count <= GlassRound.MaxCorners ? endQ.Count : 0);
        // the picture over the glass's box (the rounded rectangle less the rim), as the shader's UV
        var glass = GlassRound.Around(shape.Core, MathF.Max(shape.R - ringW, 1e-3f)).Select(P).ToList();
        m.SetShaderParameter("view_box", new Vector4(glass.Min(q => q.X) / g.Aspect, glass.Min(q => q.Y), glass.Max(q => q.X) / g.Aspect, glass.Max(q => q.Y)));
        // the ring as many screen pixels wide all round as it is across the face as seen: the face's units' steps in the head's frame
        Vector3 At(Vector2 q) { var p = P(q); return plane.Item1 + plane.Item2 * p.X + plane.Item3 * p.Y; }
        m.SetShaderParameter("pane_mid", At(Vector2.Zero)); m.SetShaderParameter("pane_a", At(new Vector2(1, 0)) - At(Vector2.Zero)); m.SetShaderParameter("pane_b", At(new Vector2(0, 1)) - At(Vector2.Zero));
        m.SetShaderParameter("pane_half", new Vector2(shape.Width / 2, 0.5f));
        if (g.Grown is null || g.GrownFor != (over, false, share))
        {
            g.Grown = GrownGlass(plane, g.Aspect, GlassRound.Around(shape.Core, shape.R + FaceMargin).Select(P), f.Eye);
            g.GrownFor = (over, false, share);
            if (TiltLog) GD.Print($"mirror-face: {(g.Left ? "L" : "R")} {shape.Width:0.000} heights wide, {shape.Height * 100:0.0} cm tall, corners {shape.R:0.00}, slid {slide * 100:0.0} cm out, reach {over} %, its top {MathF.Asin(MathF.Min(1f, MathF.Abs(shape.X.Dot(up.Normalized())))) * 180f / MathF.PI:0.0} degrees off level");
        }
        g.Mesh.Mesh = g.Grown;
    }

    /// <summary>
    /// The turned glass's outline to cover (side_mirror_face=turned): the turned head's (as side_mirror_head=turned has it) and the
    /// corners of the art-placed housing's end and painted glass that the eye sees just past the ring's outer edge (within TurnedTuck of its heights), so
    /// no point of the housing's end shows past the ring; the housing's body, further out, shows behind the glass as the turned head's does.
    /// </summary>
    private List<Vector2> TurnedCover(List<Vector2> hull, List<Vector2> cover, (Vector3 O, Vector3 A, Vector3 B) plane, Vector3 near, bool left)
    {
        // the ring's outer edge as RoundGlass draws it (GlassRound.EvenShape for the turned glass's reach)
        if (GlassRound.Shape(hull, GlassRound.Share(GlassRoundHook, _glassRoundSetting)) is not { } shape) return cover;
        float grow = GlassRound.Out(RimOutHook, Math.Max(_rimOutSetting, GlassRound.RoundedRimOut)).Widths * MirrorRim.OuterShare;
        var outer = GlassRound.EvenShape(cover, shape.R, grow, PointMargin, MirrorRim.RingWidth(grow));
        var back = _glassTurn[left ? 0 : 1].AffineInverse();   // the art's points in the turned glass's frame: the eye's view of them unchanged
        var art = HeadPoints(left).Concat(Node3D.MirrorPaint.Where(p => p.Left == left).SelectMany(p => p.Mesh.Mesh is { } m
            ? Enumerable.Range(0, m.GetSurfaceCount()).SelectMany(si => m.SurfaceGetArrays(si)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) : []))
            .Distinct().Select(q => GlassRound.Seen(near, plane, back * q)).OfType<(Vector2 Uv, float Depth)>().Select(q => q.Uv)
            .Where(uv => GlassRound.Distance(outer.Core, uv) - outer.E - outer.Over is > 0 and < TurnedTuck).ToList();
        var h = GlassRound.Hull([.. cover, .. art]);
        return art.Count > 0 && h.Count <= GlassRound.MaxCorners ? h : cover;
    }
    /// <summary>How far past the ring's outer edge a corner of the art's housing end is taken under it, in the glass's heights.</summary>
    private const float TurnedTuck = 0.05f;

    /// <summary>
    /// The turned glass (side_mirror_face=turned) brought in front of the art's housing without changing what the driver sees: its
    /// node scaled about the eye by the least share of its distance that puts every point of it (its mesh, sampled) FaceClear in front
    /// of the housing and its painted glass along the eye's ray, and every corner of the housing seen through it behind it. Scaling
    /// about the eye keeps its outline, picture and ring on screen exactly the turned head's.
    /// </summary>
    private void TurnedClear(GlassPane g)
    {
        if (g.Holder is not { } holder || g.Mesh.Mesh is not ArrayMesh gm || gm.GetSurfaceCount() == 0) return;
        g.Cleared = true;
        var head = Node3D.MirrorHeads.FirstOrDefault(h => h.Left == g.Left).Mesh;
        if (head?.Mesh is null) return;
        var eye = Node3D.Eye;   // the head at the art's place: its frame is the cockpit's
        var turn = _glassTurn[g.Left ? 0 : 1];
        var tris = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        foreach (var hm in new[] { head.Mesh }.Concat(Node3D.MirrorPaint.Where(p => p.Left == g.Left).Select(p => p.Mesh.Mesh)).OfType<Mesh>())
            for (int si = 0; si < hm.GetSurfaceCount(); si++)
            {
                var arr = hm.SurfaceGetArrays(si);
                var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int count = idx?.Length ?? verts.Length;
                for (int k = 0; k + 2 < count; k += 3) tris.Add((verts[idx?[k] ?? k], verts[idx?[k + 1] ?? k + 1], verts[idx?[k + 2] ?? k + 2]));
            }
        var gv = gm.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(p => turn * p).ToArray();
        var glassTris = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        for (int k = 0; k + 2 < gv.Length; k += 3) glassTris.Add((gv[k], gv[k + 1], gv[k + 2]));
        float share = DoorFace.TurnedShare(eye, glassTris, tris, FaceClear);
        holder.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(share, share, share)), eye * (1 - share)) * turn;
        if (TiltLog)
        {
            var mid = turn * g.Centre;
            GD.Print($"mirror-turned: {(g.Left ? "L" : "R")} scaled {share:0.000} about the eye: its middle {(mid - eye).Length() * (1 - share) * 100:0.0} cm nearer the eye ({(mid - eye).Length() * 100:0.0} cm away)");
        }
    }

    /// <summary>How far the face's mesh reaches past its rounded outline, in its heights: room for the edge's one-pixel blend.</summary>
    private const float FaceMargin = 0.03f;

    /// <summary>I76_SIDE_MIRROR_KEY=1: a door glass's picture drawn flat green and its moulding magenta, to count its pixels in a capture.</summary>
    private static readonly bool GlassKeyHook = System.Environment.GetEnvironmentVariable("I76_SIDE_MIRROR_KEY") == "1";
    private StandardMaterial3D? _shellMat;
    /// <summary>The shell's inside where a tilted glass leaves its housing's painted glass: the moulding's colour, darker, unlit.</summary>
    private StandardMaterial3D ShellMaterial()
    {
        _rimMat ??= RimMaterial();
        var c = _rimMat.GetShaderParameter("colour").AsColor();
        return _shellMat ??= new StandardMaterial3D { AlbedoColor = new Color(c.R * 0.45f, c.G * 0.45f, c.B * 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    }

    /// <summary>
    /// A door mirror's glass rounded (GlassRound, side_mirror_round): the core and radius in the pane's units, the rim's width and colour;
    /// the rounded glass draws its own moulding, so the pane's straight rim (MirrorRim) is hidden while it is. 0, the glass's own corners
    /// and the straight rim.
    /// </summary>
    /// <remarks>
    /// With the moulding's outer edge rounded too (GlassRound.RimOut, side_mirror_rim_out) the glass is drawn on a flat polygon in the
    /// pane's plane holding the ring (and, with its corners dark, the pane's outline), the pane's picture coordinates carried onto it;
    /// true then, and the housing's painted glass is hidden. Otherwise the pane's own faces, as before.
    /// </remarks>
    private bool RoundGlass(GlassPane g, int share, GlassRound.RimOut rimOut)
    {
        var m = g.Mat;
        if (g.Face is { } plate) { FaceRound(g, plate, share); return true; }
        if (g.Tilted)
        {
            // the tilted glass keeps its moulding in the shader, straight-cornered at 0 (its own outline, no radius); no outer ring
            var (tcore, tr) = GlassRound.Shape(g.Hull, share) ?? (g.Hull, 0f);
            var tc = new Vector2[GlassRound.MaxCorners];
            for (int i = 0; i < tcore.Count && i < tc.Length; i++) tc[i] = tcore[i];
            m.SetShaderParameter("pane_round", true); m.SetShaderParameter("rim_round", false);
            m.SetShaderParameter("pane_core", tc); m.SetShaderParameter("pane_corners", Math.Min(tcore.Count, GlassRound.MaxCorners)); m.SetShaderParameter("pane_r", tr);
            m.SetShaderParameter("rim_w", MirrorRim.RimShare);
            _rimMat ??= RimMaterial();
            m.SetShaderParameter("rim_colour", _rimMat.GetShaderParameter("colour"));
            g.Mesh.Mesh = g.Faces;
            return false;
        }
        if (GlassRound.Shape(g.Hull, share) is not { } shape)
        {
            m.SetShaderParameter("pane_round", false); m.SetShaderParameter("rim_round", false);
            g.Mesh.Mesh = g.Faces;
            return false;
        }
        var core = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < shape.Core.Count; i++) core[i] = shape.Core[i];
        m.SetShaderParameter("pane_round", true);
        m.SetShaderParameter("pane_aspect", g.Aspect);
        m.SetShaderParameter("pane_core", core);
        m.SetShaderParameter("pane_corners", shape.Core.Count);
        m.SetShaderParameter("pane_r", shape.R);
        m.SetShaderParameter("rim_w", MirrorRim.RimShare);
        _rimMat ??= RimMaterial();
        m.SetShaderParameter("rim_colour", _rimMat.GetShaderParameter("colour"));
        bool round = rimOut.On && g.Plane is not null && g.Hull.Count <= GlassRound.MaxCorners && g.Cover.Count <= GlassRound.MaxCorners;
        m.SetShaderParameter("rim_round", round);
        if (!round) { g.Mesh.Mesh = g.Faces; return false; }
        // the outer edge in the rim's widths before the halving (MirrorRim.OuterShare): where it covered the housing's edges
        float grow = rimOut.Cover ? GlassRound.Cover(g.Hull, shape.Core, shape.R) : rimOut.Widths * MirrorRim.OuterShare;
        float ringW = MirrorRim.RingWidth(grow);
        // the outer edge holds every corner of the glass's outline (the housing's edge) inside it, so none shows past the ring as a
        // point: by default one smooth outline the rim's reach past the housing's all round, the glass's edge one band's width inside it
        // (the band one width everywhere, the glass grown to it); or (I76_MIRROR_RIM_POINTS) its corners tighter where the outline's are
        // sharper (the band thicker there), the whole ring grown evenly round the glass's rounded shape, or as round as the glass's
        var outer = (Core: shape.Core, R: shape.R);
        bool even = PointsVariant is null or "even" || PointsVariant.StartsWith("even:");
        // the outline the key marks and the grown mesh holds: the glass's and the housing's rim seen past it, whichever shape
        var outline = g.Cover;
        var box = (Min: Vector2.Zero, Max: Vector2.One);
        if (even)
        {
            float over = PointsVariant is { } pv && pv.StartsWith("even:") && float.TryParse(pv[5..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ow) ? ow * MirrorRim.OuterShare : grow;
            var es = GlassRound.EvenShape(g.Cover, shape.R, over, PointMargin, ringW);
            outer = (es.Core, es.E); grow = es.Over;
            box = GlassRound.PictureBox(es.Core, es.E + es.Over - ringW, g.Aspect);
        }
        else if (PointsVariant == "uniform") grow = GlassRound.UniformGrow(g.Hull, shape.Core, shape.R, grow, PointMargin);
        else if (PointsVariant != "off") outer = GlassRound.OuterShape(g.Hull, shape.Core, shape.R, grow, PointMargin);
        if (PointsHook)
            foreach (var v in outline)
                GD.Print($"mirror-points: {(g.Left ? "L" : "R")} corner {v.X:0.0000} {v.Y:0.0000} past-round {GlassRound.Distance(shape.Core, v) - shape.R - grow:0.0000} past-outer {GlassRound.Distance(outer.Core, v) - outer.R - grow:0.0000} outer-r {outer.R + grow:0.0000} glass-r {shape.R:0.0000} reach {grow:0.0000} ring {ringW:0.0000}");
        var hull = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < outline.Count; i++) hull[i] = outline[i];
        var ocore = new Vector2[GlassRound.MaxCorners];
        for (int i = 0; i < outer.Core.Count; i++) ocore[i] = outer.Core[i];
        m.SetShaderParameter("pane_ocore", ocore);
        m.SetShaderParameter("pane_ocorners", outer.Core.Count);
        m.SetShaderParameter("pane_or", outer.R);
        m.SetShaderParameter("rim_out", grow);
        m.SetShaderParameter("ring_w", ringW);   // the band as wide as before whichever way the outer edge reaches
        m.SetShaderParameter("ring_from_outer", even);
        m.SetShaderParameter("view_box", new Vector4(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
        m.SetShaderParameter("rim_dark", rimOut.Dark);
        m.SetShaderParameter("pane_hull", hull);
        m.SetShaderParameter("pane_hull_n", outline.Count);
        var (po, pa, pb) = g.Plane!.Value;
        // the ring as many screen pixels wide all round as it is across the glass as seen (the shader's px_per_unit)
        m.SetShaderParameter("pane_mid", po + pa * (g.Aspect / 2) + pb / 2); m.SetShaderParameter("pane_half", new Vector2(g.Aspect / 2, 0.5f)); m.SetShaderParameter("pane_a", pa); m.SetShaderParameter("pane_b", pb);
        if (g.Grown is null || g.GrownFor != (grow, rimOut.Dark, outer.R))
        {
            g.Grown = GrownGlass(g.Plane!.Value, g.Aspect, GlassRound.GrownOutline(outline, outer.Core, outer.R + grow, GrowMargin), g.Eye);
            g.GrownFor = (grow, rimOut.Dark, outer.R);
        }
        g.Mesh.Mesh = g.Grown;
        return true;
    }

    /// <summary>How far the grown glass's mesh reaches past the ring's outer edge, in the glass's heights: room for the edge's one-pixel blend.</summary>
    private const float GrowMargin = 0.02f;

    /// <summary>
    /// The grown glass: the hull of the outline's points as one flat fan in the pane's plane, the UVs the shader's picture coordinates;
    /// one-sided, its face toward <paramref name="eye"/> (the driver's, in the head's frame), so nothing of it is drawn from behind.
    /// </summary>
    private static ArrayMesh GrownGlass((Vector3 O, Vector3 A, Vector3 B) plane, float aspect, IEnumerable<Vector2> outline, Vector3 eye)
    {
        var poly = GlassRound.Hull(outline);
        var v = new List<Vector3>(); var t = new List<Vector2>();
        var at = poly.Select(q => plane.O + plane.A * q.X + plane.B * q.Y).ToArray();
        // Godot's front faces wind clockwise as seen: the face toward the eye has its right-handed normal pointing away from it
        bool flip = at.Length >= 3 && (at[1] - at[0]).Cross(at[2] - at[0]).Dot(eye - at[0]) > 0;
        for (int k = 1; k + 1 < poly.Count; k++)
            foreach (var i in flip ? new[] { 0, k + 1, k } : new[] { 0, k, k + 1 }) { v.Add(at[i]); t.Add(new Vector2(poly[i].X / aspect, poly[i].Y)); }
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        a[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        a[(int)Mesh.ArrayType.TexUV] = t.ToArray();
        var mesh = new ArrayMesh();
        I76.Game.MeshArrays.AddSurfaceArrays(mesh, Mesh.PrimitiveType.Triangles, a);
        return mesh;
    }

    /// <summary>Each side's picture (the own camera's forward) and its head's turn (in the car's frame, Godot axes), made once by Aim; left first.</summary>
    private readonly (Transform3D Turn, Vector3 Forward)[] _aim = [(Transform3D.Identity, OwnCamera.ForwardGodot(true, OwnCamera.YawDegrees, OwnCamera.PitchDegrees)), (Transform3D.Identity, OwnCamera.ForwardGodot(false, OwnCamera.YawDegrees, OwnCamera.PitchDegrees))];
    /// <summary>The same turns in game axes, about their pivots, for the glass's picture as the driver sees it.</summary>
    private readonly (System.Numerics.Quaternion Q, System.Numerics.Vector3 Pivot)[] _aimTurn = [(System.Numerics.Quaternion.Identity, default), (System.Numerics.Quaternion.Identity, default)];
    private bool _aimed;
    /// <summary>[port] side_mirror_head (MirrorAim.HeadTurned): the heads turned to the driver; false, the default, the art's place. Read with the cockpit (the glass is made once).</summary>
    private bool _headTurned;
    /// <summary>[port] side_mirror_tilt_left / _right (MirrorAim.Tilt): degrees each glass turns toward the driver in its housing, the head at the art's place; read with the cockpit, as side_mirror_head.</summary>
    private int[] _tiltSetting = [MirrorAim.DefaultTiltLeft, MirrorAim.DefaultTiltRight];
    /// <summary>Each side's glass tilt and the corner it turns about (game axes), made by Aim; null for none (0, the head turned, no head).</summary>
    private readonly (System.Numerics.Quaternion Q, System.Numerics.Vector3 Pivot)?[] _tilt = new (System.Numerics.Quaternion, System.Numerics.Vector3)?[2];
    /// <summary>I76_MIRROR_TILT=L,R: the glasses' tilt in degrees instead of the settings (a review sheet); I76_MIRROR_TILT_LOG=1 logs each tilt and how far off its face the glass is seen.</summary>
    private static readonly string? TiltHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_TILT");
    private static readonly bool TiltLog = System.Environment.GetEnvironmentVariable("I76_MIRROR_TILT_LOG") == "1";
    private static readonly (bool Art, float InwardL, float InwardR) AimHook = MirrorAim.Hook(System.Environment.GetEnvironmentVariable("I76_MIRROR_AIM"));

    /// <summary>
    /// Each side mirror aimed from the driver's eye (MirrorAim): the picture back along the side with the flank a sliver at its inner
    /// edge, and the door mirror's head (where the car has one) turned about its middle to the plane that reflects the eye's ray into it.
    /// </summary>
    private void Aim()
    {
        if (_aimed) return;
        _aimed = true;
        var eye = Node3D.Eye;
        var gameEye = new System.Numerics.Vector3(eye.X, eye.Y, -eye.Z);
        var body = Node3D.BodyBounds;
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float s = left ? -1 : 1;
            var pane = Glass.FirstOrDefault(p => p.Left == left);
            var at = OwnCameraBase(left, pane);
            var cam = new System.Numerics.Vector3(at.X + s * OwnAim.Outboard, at.Y, -at.Z);
            float inward = left ? AimHook.InwardL : AimHook.InwardR;
            float yaw = AimHook.Art ? OwnAim.Yaw : MirrorAim.YawDegrees(left, cam, left ? -body.Position.X : body.End.X, -body.End.Z, OwnAim.Fov, 2 * inward);
            var turn = Transform3D.Identity;
            if (!AimHook.Art && pane is not null && Node3D.MirrorHeads.FirstOrDefault(h => h.Left == left).Mesh is { Mesh: not null })
            {
                // about the glass's middle, as a door mirror's glass turns in its housing: the glass stays where the art puts it (the
                // right one clear of the windscreen pillar; turned about the head's middle it went in behind it) and the housing behind it
                var all = pane.Faces.SelectMany(f => f).ToArray();
                var centre = all.Aggregate(System.Numerics.Vector3.Zero, (a, b) => a + b) / all.Length;
                var gamePivot = centre;
                var q = MirrorAim.Turn(gameEye, centre, pane.Normal, gamePivot, MirrorAim.Axis(left, yaw, OwnAim.Pitch));
                if (_headTurned || _faceTurned)
                {
                    var pivot = Coords.ToGodot(gamePivot);
                    var basis = new Basis(MirrorAim.ToGodot(q));
                    _glassTurn[i] = new Transform3D(basis, pivot - basis * pivot);
                    if (_headTurned) turn = _glassTurn[i];
                    _aimTurn[i] = (q, gamePivot);
                }
                else if (MirrorAim.GlassTilt(_headTurned, _tiltSetting[i]) is > 0 and var degrees)
                {
                    // the head where the art puts it and its glass alone turned part of the way, about its outer corner (MirrorAim.Tilt)
                    var tq = MirrorAim.Tilt(q, degrees);
                    _tilt[i] = (tq, MirrorAim.TiltPivot(all, tq, gameEye));
                    if (TiltLog)
                        GD.Print($"mirror-tilt: {(left ? "L" : "R")} {MathF.Min(degrees, MirrorAim.AngleDegrees(q)):0.0} of {MirrorAim.AngleDegrees(q):0.0} degrees, seen "
                            + $"{MirrorAim.OffFace(gameEye, centre, pane.Normal):0.0} off its face, tilted {MirrorAim.OffFace(gameEye, centre, System.Numerics.Vector3.Transform(pane.Normal, tq)):0.0}; it reflects the eye's ray "
                            + $"{Outward(pane.Normal):0.0} degrees out from straight back as the art sets it, {Outward(System.Numerics.Vector3.Transform(pane.Normal, tq)):0.0} tilted; the picture looks {yaw:0.0} out");
                    float Outward(System.Numerics.Vector3 n) { var r = MirrorAim.Reflect(gameEye, centre, System.Numerics.Vector3.Normalize(n)); return MathF.Atan2(s * r.X, -r.Z) * 180f / MathF.PI; }
                }
            }
            _aim[i] = (turn, OwnCamera.ForwardGodot(left, yaw, OwnAim.Pitch));
        }
    }

    /// <summary>Where a side's own camera stands before its outboard step: the door mirror's glass (as the art places it), or a door's height beside the cab.</summary>
    private Vector3 OwnCameraBase(bool left, I76.Formats.Assembly.MirrorGlass.Pane? pane)
    {
        if (pane is not null)
        {
            var all = pane.Faces.SelectMany(f => f).ToArray();
            return Coords.ToGodot(all.Aggregate(System.Numerics.Vector3.Zero, (a, b) => a + b) / all.Length);
        }
        var bounds = Node3D.CockpitBounds;
        return new Vector3(left ? (bounds?.Position.X ?? -0.9f) : (bounds?.End.X ?? 0.9f), Node3D.Eye.Y - 0.15f, Node3D.Eye.Z - 0.3f);
    }

    /// <summary>The rim's material: the rear mirror frame's colour, the mean of the art's frame rows (MirrorRim.FrameColour).</summary>
    private ShaderMaterial RimMaterial()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/mirror_rim.gdshader") };
        if (Resolver.FindByStem("zmiri101") is { } member && Textures.GetOriginalRgba(member) is { } o)
        {
            var (r, g, b) = MirrorRim.FrameColour(o.Rgba, o.Width, o.Height);
            m.SetShaderParameter("colour", new Color(r / 255f, g / 255f, b / 255f));
        }
        return m;
    }

    // ---- the side mirrors' own cameras (side_mirror_render=own, the default: two more scene renders every second frame) ----

    private readonly SubViewport?[] _ownViews = new SubViewport?[2];
    private readonly Camera3D?[] _ownCams = new Camera3D?[2];
    private static readonly (float Fov, float Yaw, float Outboard, float Pitch) OwnAim = OwnCamera.Aim(System.Environment.GetEnvironmentVariable("I76_OWN_MIRROR"));
    /// <summary>I76_MIRROR_SELF=0: the own body left out of the side pictures, so a capture's difference from one with it measures the body's share of the glass.</summary>
    private static readonly bool OwnSelfHook = System.Environment.GetEnvironmentVariable("I76_MIRROR_SELF") != "0";
    private bool _ownOn;
    private readonly bool[] _ownUsed = [true, true];
    private bool _ownTurn;
    /// <summary>I76_OWN_MIRROR_FAR=metres: the side cameras' reach cut short, for a measurement (the default is the rear mirror's, mirror_range).</summary>
    private static readonly float? OwnFar = float.TryParse(System.Environment.GetEnvironmentVariable("I76_OWN_MIRROR_FAR"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float of) && of >= 50 ? of : null;
    private static readonly bool OwnAlternate = System.Environment.GetEnvironmentVariable("I76_OWN_MIRROR_ALT") != "0";

    /// <summary>Each side glass (the door mirror's or the added one's) given its own camera's picture whole, or the shared render's band back.</summary>
    private void OwnCameras(bool on, bool quads = false)
    {
        // a side is drawn only where something shows it: its door mirror's glass, or the added glasses (the CC cars have a left glass alone)
        for (int i = 0; i < 2; i++) _ownUsed[i] = (quads || Glass.Any(p => p.Left == (i == 0))) && MirrorBands.SideShown(_sideMirrors, i == 0);
        if (on && _ownViews[0] is null)
            for (int i = 0; i < 2; i++)
            {
                var vp = new SubViewport { Size = new Vector2I(OwnCamera.Width, OwnCamera.Height), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Name = i == 0 ? "MirrorOwnL" : "MirrorOwnR" };
                var cam = new Camera3D { Fov = OwnCamera.FovDegrees, KeepAspect = Camera3D.KeepAspectEnum.Width, Near = 0.05f, Current = true };
                vp.AddChild(cam);
                AddChild(vp);
                _ownViews[i] = vp; _ownCams[i] = cam;
                SmokeLayer.DoorMirrorCameras[i] = cam;   // the mirrors' smoke (MirrorDraw) for the door mirrors too
            }
        _ownOn = on;
        if (on) Aim();
        Node3D.BodyForMirrors(on);
        for (int i = 0; i < 2; i++)
        {
            if (_ownViews[i] is not { } vp || _ownCams[i] is not { } cam) continue;
            if (!on) { vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; continue; }
            bool left = i == 0;
            var (ow, oh) = MirrorWarp.Size(OwnCamera.Width, OwnCamera.Height, MirrorWarp.Overscan(_warpPeriod));
            if (vp.Size != new Vector2I(ow, oh)) vp.Size = new Vector2I(ow, oh);
            I76.Game.Boot.LookFilters.Apply(vp, _faithful);
            cam.Far = OwnFar ?? OriginalBehavior.MirrorFar(_faithful);
            cam.Environment = _mirrorMode == 1 ? _mirrorFlat : MirrorEnvironment();
            cam.CullMask = SmokeLayer.MirrorMask(((_mirrorMode == 1 ? ~World.TerrainNode.TerrainLayer : 0xFFFFFu) & ~CarNode.CockpitLayer & ~SmokeLayer.QuadLayer & ~World.TerrainNode.NearTierLayer & ~CarNode.OwnBodyLayer & 0xFFFFF) | (OwnSelfHook ? CarNode.OwnBodyLayer : 0u), 2 + i);
            foreach (var g in _glass.Where(g => g.Left == left)) { g.Mat.SetShaderParameter("view", vp.GetTexture()); g.Mat.SetShaderParameter("band", new Vector2(0, 1)); }
            var sm = left ? _sideMatL : _sideMatR;
            if (sm is not null) { sm.SetShaderParameter("view", vp.GetTexture()); sm.SetShaderParameter("band", new Vector2(0, 1)); sm.SetShaderParameter("cylindrical", false); }
        }
        if (!on)
        {
            var shared = _mirrorView?.GetTexture();
            foreach (var sm in new[] { _sideMatL, _sideMatR }) sm?.SetShaderParameter("view", shared);
        }
    }

    /// <summary>
    /// Where the own cameras stand this frame: at each door mirror's glass (or, for a car without one, a door's height beside the
    /// cab), OwnCamera.OutboardMetres outboard of it, looking back along the side turned outward and a little down
    /// (OwnCamera.Forward), drawn on the rear mirror's ticks by Mirror Rate (MirrorRateRow).
    /// </summary>
    private void OwnCamerasFrame(bool draw, MirrorRate rate)
    {
        if (!_ownOn) return;
        // one side a mirror tick, in turn: each side mirror every fourth frame, the rear one every second (with both every second
        // frame the two cameras cost 1.6 ms of GPU a frame over the shared render at 3440x1440, docs/deviations.md); a side the
        // other has no glass for is drawn every tick; I76_OWN_MIRROR_ALT=0 draws both every tick, as Mirror Rate Half does
        if (draw) _ownTurn = !_ownTurn;
        // a side whose glass is out of the driver's view gets no render (MirrorCull), and the other is then drawn on every tick; back
        // in view, a side is drawn that frame, so its glass never shows a picture from when it went out
        Span<bool> seen = [_ownUsed[0] && (CullOff || _sideInView[0]), _ownUsed[1] && (CullOff || _sideInView[1])];
        for (int i = 0; i < 2; i++)
        {
            if (_ownViews[i] is not { } vp || _ownCams[i] is not { } cam) continue;
            bool left = i == 0;
            // the glass's middle where the head has turned it (the glass mesh is the head's child)
            var at = _glass.FirstOrDefault(g => g.Left == left) is { Mesh: not null } g ? (g.Mesh.GetParent() is MeshInstance3D head && head != Node3D.CockpitMesh ? head.Transform * g.Centre : g.Centre)
                : OwnCameraBase(left, null);
            at.X += left ? -OwnAim.Outboard : OwnAim.Outboard;
            float tx = MathF.Tan(OwnAim.Fov * MathF.PI / 360f);
            _ownTan = ((tx, tx * OwnCamera.Height / OwnCamera.Width), (tx * vp.Size.X / OwnCamera.Width, tx * vp.Size.Y / OwnCamera.Width));
            cam.Fov = vp.Size.X == OwnCamera.Width ? OwnAim.Fov : 2f * MathF.Atan(_ownTan.Drawn.X) * 180f / MathF.PI;
            var forward = _aim[i].Forward;   // the engine's back is this frame's +Z: no mirroring of the forward's x
            var local = Transform3D.Identity.LookingAt(forward, Vector3.Up);
            cam.GlobalTransform = Node3D.GlobalTransform * new Transform3D(local.Basis, at);
            bool back = seen[i] && !_ownSeen[i] && !CullOff;
            bool due = _warpPeriod > 0 ? MirrorWarp.Draws(_warpPeriod, _warpFrame, 1 + i) || _warpDrawn[1 + i] is null
                : MirrorRateRow.Draws(rate == MirrorRate.Alternate && !OwnAlternate ? MirrorRate.Half : rate, draw, side: true, turn: _ownTurn == left, otherSeen: seen[left ? 1 : 0]);
            vp.RenderTargetUpdateMode = seen[i] && (due || back) ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
            if (_warpPeriod > 0 && vp.RenderTargetUpdateMode == SubViewport.UpdateMode.Once) _warpDrawn[1 + i] = cam.GlobalTransform;
            if (CullLog && vp.RenderTargetUpdateMode == SubViewport.UpdateMode.Once) _ownDrawn[i]++;
            if (CullLog && back) GD.Print($"mirror-cull: {(left ? "L" : "R")} back in view at frame {Godot.Engine.GetFramesDrawn()}, drawn that frame");
        }
        _ownSeen[0] = seen[0];
        _ownSeen[1] = seen[1];
    }

    // ---- which mirrors are in the driver's view (MirrorCull; the modern look) ----

    /// <summary>I76_MIRROR_CULL=0: every mirror drawn on its tick whether or not its glass is in view (main's, for a measurement); I76_MIRROR_CULL_LOG=1: a line a second, where each glass lies against the view and how often each was drawn.</summary>
    private static readonly bool CullOff = System.Environment.GetEnvironmentVariable("I76_MIRROR_CULL") == "0";
    private static readonly bool CullLog = System.Environment.GetEnvironmentVariable("I76_MIRROR_CULL_LOG") == "1";
    /// <summary>Degrees the view is widened by on every side for the test: a glass at the edge is drawn a little early, never late.</summary>
    private const float CullMarginDegrees = 2f;
    private readonly bool[] _sideInView = [true, true], _ownSeen = new bool[2];
    private readonly int[] _ownDrawn = new int[2];
    private bool _rearInView = true, _rearSeen;
    private int _cullFrames, _rearDrawn;
    /// <summary>The rear mirror's glass, its surfaces' box in the interior's frame (SetupMirror).</summary>
    private Aabb? _rearBox;
    private readonly System.Numerics.Vector3[] _corners = new System.Numerics.Vector3[8];

    /// <summary>Out of the car or the mirror off: nothing is in view, so each mirror is drawn afresh when it shows again.</summary>
    private void MirrorsUnseen()
    {
        _rearSeen = false;
        _ownSeen[0] = _ownSeen[1] = false;
        foreach (var vp in _ownViews) if (vp is not null) vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    /// <summary>Whether the rear glass and each side's glass (the door mirror's, or the added one) is in the eye's view this frame, the head's turn, the head tracker and the Field of View in it: the eye's camera as it stands, in the car's frame.</summary>
    private void InView()
    {
        if (Node3D.CockpitCamera is not { } cam) return;
        var size = cam.GetViewport()?.GetVisibleRect().Size ?? new Vector2(4, 3);
        var (tx, ty) = MirrorCull.HalfTangents(cam.Fov, cam.KeepAspect == Camera3D.KeepAspectEnum.Height, size.X / MathF.Max(size.Y, 1), CullMarginDegrees);
        var toEye = InCar(cam).AffineInverse();
        (float, float, float, float)? rearAt = null;
        _rearInView = _mirrorHost is not null && _rearBox is { } rb && Seen(_mirrorHost, rb, toEye, tx, ty, out rearAt);
        var at = new (float, float, float, float)?[2];
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0, any = false;
            foreach (var g in _glass)
            {
                if (g.Left != left || g.Mesh.Mesh is not { } m) continue;
                if (Seen(g.Mesh, m.GetAabb(), toEye, tx, ty, out var a)) any = true;
                at[i] ??= a;
            }
            var quad = left ? _sideL : _sideR;
            if (quad?.Mesh is { } qm && Seen(quad, qm.GetAabb(), toEye, tx, ty, out _)) any = true;
            _sideInView[i] = any;
        }
        if (!CullLog) return;
        if (_mirrorView?.RenderTargetUpdateMode is SubViewport.UpdateMode.Once or SubViewport.UpdateMode.Always) _rearDrawn++;
        if (++_cullFrames % 60 != 0) return;
        static string A((float X0, float X1, float Y0, float Y1)? a) => a is { } v ? FormattableString.Invariant($"x {v.X0:0.0}..{v.X1:0.0} y {v.Y0:0.0}..{v.Y1:0.0}") : "-";
        float hx = MathF.Atan(tx) * 180 / MathF.PI - CullMarginDegrees, hy = MathF.Atan(ty) * 180 / MathF.PI - CullMarginDegrees;
        GD.Print(FormattableString.Invariant($"mirror-cull: half {hx:0.0} x {hy:0.0} | L in={_sideInView[0]} drawn={_ownDrawn[0]}/60 {A(at[0])} | R in={_sideInView[1]} drawn={_ownDrawn[1]}/60 {A(at[1])} | rear in={_rearInView} drawn={_rearDrawn}/60 {A(rearAt)}"));
        _ownDrawn[0] = _ownDrawn[1] = _rearDrawn = 0;
    }

    /// <summary>A mesh's box (in its own frame) in view: shown in the tree and not wholly outside the view's sides (MirrorCull.InView); where its corners lie, for the log.</summary>
    private bool Seen(MeshInstance3D mesh, Aabb box, Transform3D toEye, float tx, float ty, out (float, float, float, float)? at)
    {
        at = CullLog ? Where(mesh, box, toEye) : null;
        if (!mesh.IsVisibleInTree()) return false;
        Corners(mesh, box, toEye);
        return MirrorCull.InView(_corners, tx, ty);
    }

    private (float, float, float, float)? Where(MeshInstance3D mesh, Aabb box, Transform3D toEye)
    {
        Corners(mesh, box, toEye);
        return MirrorCull.Angles(_corners);
    }

    private void Corners(Node3D node, Aabb box, Transform3D toEye)
    {
        var t = toEye * InCar(node);
        for (int k = 0; k < 8; k++)
        {
            var p = t * box.GetEndpoint(k);
            _corners[k] = new System.Numerics.Vector3(p.X, p.Y, p.Z);
        }
    }

    /// <summary>A node's transform in the car's frame (the eye and the interior are under the car; the car's own place in the world is left out, so the test is the frame's head turn whatever the car does).</summary>
    private Transform3D InCar(Node3D n)
    {
        var t = n.Transform;
        for (var p = n.GetParent() as Node3D; p is not null && p != Node3D; p = p.GetParent() as Node3D) t = p.Transform * t;
        return t;
    }
}
