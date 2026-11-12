using System.Collections.Concurrent;
using Godot;
using I76.Formats.Video;

namespace I76.Game.Ui;

/// <summary>
/// Plays one of the install's Smacker movies over everything, as
/// StartSmackerPlayback does: the intro and opening credits at startup, a
/// mission's briefing when its script asks (opcode 51). A thread decodes a
/// little ahead; the picture follows the clock, dropping frames if the
/// screen falls behind, and the sound streams as it is decoded. Esc, Space,
/// Enter or a click skips it, once the mission loading under it (if any) is
/// done. It runs while the tree is paused.
/// With HiFi Audio on and a track for the movie in the pack, that track plays
/// in place of the movie's own sound (track 0, the only one the games' movies
/// have): it starts with the first frame and the picture follows it as it is
/// heard (<see cref="MovieClock"/>); the end logs how the two met.
/// </summary>
public partial class MoviePlayer : CanvasLayer
{
    public required string Path { get; init; }
    /// <summary>The 1997 look: the picture sampled nearest, as the original's 640x480 surface.</summary>
    public bool Faithful { get; init; }
    /// <summary>Esc, Space, Enter, a click or a pad button skips it (the original: Esc only; docs/deviations.md). Off in a network co-op game for all but the host (their Esc is the menu, over the movie).</summary>
    public bool Skippable { get; init; } = true;
    /// <summary>The Nitro Pack's movie (HiFi Audio's movies/nitro/ wins then; both games have a CREDF01 and an INTROF01).</summary>
    public bool Nitro { get; init; }
    /// <summary>
    /// While true (a briefing played while its mission loads, until the mission can start): a skip is not done yet but kept, and
    /// <see cref="WaitingTag"/> fades in over the picture; the movie then ends the moment this turns false. A movie that runs out
    /// meanwhile holds its last frame under the tag until then. No skip, no tag: the movie plays on as any other.
    /// </summary>
    public Func<bool>? Waiting { get; init; }
    /// <summary>Faded in over the picture when the player waits on the mission (a skip asked for, or the movie run out): the boot screen's "please stand by", small, in a corner (StandByTag).</summary>
    public StandByTag? WaitingTag { get; init; }
    /// <summary>
    /// The port's: while true, the mission is loading under the playing picture (its build's thread at work), and
    /// <see cref="WaitingTag"/> shows over it too, fading out when it turns false. No press skips meanwhile, nor is one kept for
    /// later: a new press once it is false does (MovieSkipGate).
    /// </summary>
    public Func<bool>? Loading { get; init; }
    private bool IsWaiting => Waiting?.Invoke() == true;
    private bool IsLoading => Loading?.Invoke() == true;
    private StandByTag? _tag;
    /// <summary>The movie ran out (or was held after a skip, <see cref="HoldPicture"/>) while <see cref="Waiting"/>: its frame stays up until the mission can start.</summary>
    private bool _held;
    /// <summary>The picture stands still under the stand-by line (run out, or held after a skip) until the mission can start.</summary>
    public bool Held => _held;
    /// <summary>A skip was asked for while <see cref="Waiting"/>: kept, the stand-by line up, the movie playing on until the mission is ready or held.</summary>
    public bool SkipWanted => _skipWanted;
    /// <summary>
    /// The port's: after a kept skip, the picture stops where it is (its sound too), as a movie run out holds its last frame, so that
    /// what is left of the load runs behind a still picture instead of under a playing one (Main.BuildBehindCard).
    /// </summary>
    public void HoldPicture() => Hold("skipped");
    /// <summary>Capture hook: I76_MOVIE_END=N runs the movie out N drawn frames after its first, as its end would (held while waiting).</summary>
    private int _endAfter = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MOVIE_END"), out int ea) ? ea : -1;
    /// <summary>Capture hook: I76_MOVIE_SKIP=N skips the movie N drawn frames after its first, as a key would (a briefing ended early).</summary>
    private int _skipAfter = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MOVIE_SKIP"), out int sk2) ? sk2 : -1;
    /// <summary>
    /// Capture hook: I76_MOVIE_INPUT="wheel@N;click@N;key@N" sends, N drawn frames after the first, a notch of the mouse wheel, a left
    /// click (pressed, then released the next frame) or Enter (likewise) through the engine's input, as the player's would come; also
    /// rclick, esc, space and pad (pad 0's A). <c>N</c> ending in <c>ms</c>: that many milliseconds after the first frame instead (a
    /// build under the briefing draws at up to 1000 fps).
    /// </summary>
    private readonly List<(string What, int At, bool Ms)> _inputs = [.. (System.Environment.GetEnvironmentVariable("I76_MOVIE_INPUT") ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Split('@')).Where(p => p.Length == 2 && int.TryParse(p[1].TrimEnd('m', 's'), out _))
        .Select(p => (p[0], int.Parse(p[1].TrimEnd('m', 's')), p[1].EndsWith("ms")))];
    private int _drawn;
    private ulong _firstFrameAt;
    /// <summary>The drawn frame an <c>ms</c> input of I76_MOVIE_INPUT came due on (0: not yet).</summary>
    private readonly Dictionary<int, int> _inputDue = [];
    public event Action? Finished;

    private Smacker _movie = null!;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _decoded;
    private readonly ConcurrentQueue<(int Index, byte[] Rgba, byte[]? Pal)> _frames = new();
    private readonly ConcurrentQueue<Vector2[]> _sound = new();
    /// <summary>
    /// Frames decoded ahead of the picture, their sound with them: 20, or 60 (4 s at 15 a second) for a briefing over a
    /// mission's load (<see cref="Loading"/>), whose main-thread steps can hold the game for a second or more (#157).
    /// </summary>
    private int Ahead => Loading is null ? 20 : 60;
    /// <summary>The sound's buffer (seconds): over a load it carries the sound through a hitch of the main thread that feeds it.</summary>
    private float SoundBuffer => Loading is null ? 0.5f : 4.5f;
    private MovieAudioClock? _audioClock;
    private TextureRect _view = null!;
    private ImageTexture? _tex, _palTex, _blkTex;
    /// <summary>Palette-constrained debanding and deblocking in shaders: off by default (smooth-scaled raw frames); I76_MOVIE_DEBAND=1 turns it on.</summary>
    private bool _deband;
    private int _hold = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MOVIE_HOLD"), out int h) ? h : -1;
    private SubViewport? _pass;
    private ColorRect? _debandRect;
    private AudioStreamGeneratorPlayback? _out;
    private Vector2[]? _pending;
    private int _pendingAt;
    private MovieClock _clock = null!;
    private bool _started, _done;
    /// <summary>HiFi Audio's track in place of the movie's own sound, or null.</summary>
    private AudioStreamPlayer? _hifi;
    private (string Entry, double Seconds, double Own) _hifiInfo;
    private long _hifiStart;
    private bool _hifiTold;
    /// <summary>Frames the decoder has made (its thread counts them), for the watch on a decoder that stops.</summary>
    private volatile int _produced;
    private readonly MovieWatch _watch = new();

    public override void _Ready()
    {
        Layer = Skippable ? 100 : 30;   // an unskippable movie is under the menu a player's Esc opens
        ProcessMode = ProcessModeEnum.Always;
        _gate.HeldAtStart(HeldSkipInputs());
        if (!Faithful && Replacement(Path) is { } ogv && PlayReplacement(ogv)) { AddTag(new Vector2(4, 3)); return; }
        try { _movie = Smacker.Open(Path); }
        catch (System.Exception ex)
        {
            GD.PrintErr($"movie {Path}: {ex.Message}");
            CallDeferred(MethodName.Finish);
            return;
        }
        _clock = new MovieClock(_movie.FrameDuration);
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        _deband = !Faithful && System.Environment.GetEnvironmentVariable("I76_MOVIE_DEBAND") == "1";   // off by default (the user's call); =1 tries it
        _view = new TextureRect
        {
            AnchorRight = 1, AnchorBottom = 1, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = Faithful ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear,
        };
        AddChild(_view);
        if (_deband)
        {
            // pass 1 at the movie's own size into a half-float target (so the smoothed colours keep their precision), pass 2 scales it up
            _pass = new SubViewport
            {
                Size = new Vector2I(_movie.Width, _movie.Height), UseHdr2D = true, Disable3D = true, TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, GuiDisableInput = true,
            };
            _debandRect = new ColorRect
            {
                Size = new Vector2(_movie.Width, _movie.Height), MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/movie_deband.gdshader") },
            };
            _pass.AddChild(_debandRect);
            AddChild(_pass);
            _view.Texture = _pass.GetTexture();
            _view.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/movie_deband_view.gdshader") };
        }
        // HiFi Audio: the pack's track for this movie, which starts with its first frame (_Process); none, and the movie's own sound streams
        if (_movie.HasAudio(0) && _hold < 0)
        {
            long own = _movie.AudioSamples(0);
            double ownSeconds = own / (double)_movie.SampleRate(0);
            if (Assets.HiFiAudio.LoadMovie(System.IO.Path.GetFileName(Path), Nitro, ownSeconds) is { } found)
            {
                var (stream, ogg, entry) = found;
                _hifi = new AudioStreamPlayer { Stream = stream, Name = "HiFiSound", Bus = I76.Game.Boot.Main.MovieBus() };
                AddChild(_hifi);
                _hifiInfo = (entry, ogg.Seconds, ownSeconds);
                GD.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio {entry} ({ogg.Channels} ch, {ogg.Rate} Hz, {ogg.Samples} samples, {ogg.Seconds:0.000} s) "
                    + $"in place of its own track ({_movie.SampleRate(0)} Hz, {(_movie.Is16Bit(0) ? 16 : 8)}-bit {(_movie.Stereo(0) ? "stereo" : "mono")}, {own} samples, {ownSeconds:0.000} s; "
                    + $"the picture {_movie.FrameCount} frames, {_movie.FrameCount * _movie.FrameDuration:0.000} s)");
            }
        }
        if (_movie.HasAudio(0) && _hifi is null)
        {
            var gen = new AudioStreamGenerator { MixRate = _movie.SampleRate(0), BufferLength = SoundBuffer };
            var player = new AudioStreamPlayer { Stream = gen, Name = "Sound", Bus = I76.Game.Boot.Main.MovieBus() };
            AddChild(player);
            player.Play();
            _out = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
            // the buffer's room before anything is pushed is its size: what is pushed less what is still in it has been heard
            _audioClock = new MovieAudioClock(_movie.SampleRate(0), _out.GetFramesAvailable());
        }
        AddTag(new Vector2(_movie.Width, _movie.Height));
        _thread = new Thread(Decode) { IsBackground = true, Name = "smacker" };
        _thread.Start();
    }

    /// <summary>The stand-by line over the picture (its own size, the movie's aspect), when the start gave one.</summary>
    private void AddTag(Vector2 picture)
    {
        if (WaitingTag is not { } tag) return;
        tag.Picture = picture;
        AddChild(_tag = tag);
        if (Loading is not null) tag.Prewarm();   // (it fades in over the playing picture: its lettering made under the black first)
    }

    private void Decode()
    {
        var m = _movie;
        int track = m.HasAudio(0) && _hifi is null ? 0 : -1;   // (HiFi Audio's track in its place: the movie's own is not unpacked)
        bool wide = track >= 0 && m.Is16Bit(0), stereo = track >= 0 && m.Stereo(0);
        try
        {
            while (!_stop)
            {
                if (_frames.Count >= Ahead) { Thread.Sleep(5); continue; }
                int index = m.Position;
                if (!m.NextFrame(track < 0 ? null : (t, pcm) => { if (t == track) _sound.Enqueue(Samples(pcm, wide, stereo)); })) break;
                var px = m.Pixels; var pal = m.Palette;
                if (_deband)
                {
                    var rgbaPal = new byte[1024 + _movie.BlockTypes.Length];   // the palette, then the block types
                    for (int i = 0; i < 256; i++) { rgbaPal[i * 4] = pal[i * 3]; rgbaPal[i * 4 + 1] = pal[i * 3 + 1]; rgbaPal[i * 4 + 2] = pal[i * 3 + 2]; rgbaPal[i * 4 + 3] = 255; }
                    _movie.BlockTypes.CopyTo(rgbaPal, 1024);
                    _frames.Enqueue((index, (byte[])px.Clone(), rgbaPal));
                    _produced++;
                    continue;
                }
                var rgba = new byte[m.Width * m.Height * 4];
                for (int i = 0, o = 0; i < px.Length; i++, o += 4)
                {
                    int c = px[i] * 3;
                    rgba[o] = pal[c]; rgba[o + 1] = pal[c + 1]; rgba[o + 2] = pal[c + 2]; rgba[o + 3] = 255;
                }
                _frames.Enqueue((index, rgba, null));
                _produced++;
            }
        }
        catch (System.Exception ex) { GD.PrintErr($"movie {Path}: {ex.Message}"); }
        _decoded = true;
    }

    /// <summary>PCM as the generator's stereo float frames: unsigned 8-bit or signed 16-bit, mono or interleaved.</summary>
    private static Vector2[] Samples(byte[] pcm, bool wide, bool stereo)
    {
        int bytes = wide ? 2 : 1, ch = stereo ? 2 : 1, n = pcm.Length / (bytes * ch);
        var o = new Vector2[n];
        float S(int i) => wide ? (short)(pcm[i] | (pcm[i + 1] << 8)) / 32768f : (pcm[i] - 128) / 128f;
        for (int i = 0; i < n; i++)
        {
            float l = S(i * bytes * ch), r = stereo ? S((i * ch + 1) * bytes) : l;
            o[i] = new Vector2(l, r);
        }
        return o;
    }

    /// <summary>I76_MOVIE_SKIP_MS=N: a skippable movie is skipped N ms after it was put up, as a key would (the load-time harness's skip path); with the mission
    /// still loading under it then, the moment the load is done (the first press a player's skip can be).</summary>
    private static readonly int SkipAfterMs = int.TryParse(System.Environment.GetEnvironmentVariable("I76_MOVIE_SKIP_MS"), out int sk) ? sk : -1;
    private readonly ulong _upAt = Time.GetTicksMsec();

    public override void _Process(double delta)
    {
        // a skip asked for while the mission was not ready, or the movie run out: on into the mission the moment it is
        if ((_skipWanted || _held) && !IsWaiting) { Finish(); return; }
        TagForLoading();
        if (_held) return;
        if (SkipAfterMs >= 0 && Skippable && !_done && !_skipWanted && !IsLoading && Time.GetTicksMsec() - _upAt >= (ulong)SkipAfterMs) { if (Skip() || _held) return; }
        if (_video is not null) { ReplacementStep(); return; }
        if (_done || _movie is null) return;
        // the clock starts with the first frame, so a slow first decode does not eat into the picture
        // (a decoder that died or never delivers ends the movie instead: nothing else would ever start the clock, and the
        // black under the picture would stay over the paused game, MovieWatch)
        if (!_started)
        {
            if (_frames.IsEmpty)
            {
                if (Watch(0) == MovieStep.NoFirstFrame) { GiveUp("no first frame"); RunOut(); }
                return;
            }
            _started = true;
            _skipsAtStart = _out?.GetSkips() ?? 0;
            _firstFrameAt = Time.GetTicksMsec();
            I76.Game.World.MissionBuilder.Trace($"movie: first frame of {System.IO.Path.GetFileName(Path)}");
            if (_hifi is not null) { _hifi.Play(); _hifiStart = System.Diagnostics.Stopwatch.GetTimestamp(); }
        }
        else if (_out is not null && _audioClock is { } ac && _hold < 0)
        {
            // the picture held to the sound (#157): it catches up with sound that played on through a stall the delta did not
            // carry, and waits for sound a hitch longer than its buffer stopped
            double was = _clock.Seconds;
            int room = _out.GetFramesAvailable();
            _clock.Set(ac.Step(was, delta, room, !_decoded || !_sound.IsEmpty || (_pending is not null && _pendingAt < _pending.Length)));
            if (was + delta - _clock.Seconds > 0.05) _waitedForSound += was + delta - _clock.Seconds;
            if (delta > _worstDelta) _worstDelta = delta;
        }
        else _clock.Step(delta, Heard());   // HiFi Audio's track as heard, or the frame's delta
        if (_inputs.Count > 0) SendInputs(++_drawn);
        if (_skipAfter >= 0 && _skipAfter-- == 0)
        {
            // as a key: not while the mission loads under the picture (dropped, not kept)
            if (IsLoading) GD.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_SKIP while the mission loads, not a skip");
            else { GD.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_SKIP skips it{(IsWaiting ? " (kept until the mission is ready)" : "")}"); if (Skip() || _held) return; }
        }
        if (_endAfter >= 0 && _endAfter-- == 0) { GD.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_END runs it out"); RunOut(); return; }
        // capture hook: I76_MOVIE_HOLD=N freezes the picture on frame N, so shots of different runs compare
        if (_hold >= 0 && _frames.IsEmpty && !_decoded) _clock.Set(_clock.Seconds - delta);   // (a capture runs faster than real time: wait for the decoder)
        if (_hold >= 0 && _clock.Seconds >= _hold * _movie.FrameDuration) _clock.Set(_hold * _movie.FrameDuration);
        PushSound();
        int want = _clock.Frame;
        (int Index, byte[] Rgba, byte[]? Pal)? show = null;
        int taken = 0;
        while (_frames.TryPeek(out var f) && f.Index <= want) { _frames.TryDequeue(out f); show = f; taken++; }
        if (taken > 1) _dropped += taken - 1;   // frames whose turn passed between two of the game's frames: never shown
        if (show is not null)
        {
            // the picture's pace: a frame shown more than two of the movie's frames after the one before it is late
            double nowS = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if (_lastShown > 0 && nowS - _lastShown > 2 * _movie.FrameDuration) { _late++; _worstShowGap = Math.Max(_worstShowGap, nowS - _lastShown); }
            _lastShown = nowS;
            _shown++;
        }
        if (show is { } s)
        {
            if (s.Index == _movie.FrameCount - 1) TellSync();
            if (s.Pal is not null)
            {
                var idx = Image.CreateFromData(_movie.Width, _movie.Height, false, Image.Format.R8, s.Rgba);
                var pal = Image.CreateFromData(256, 1, false, Image.Format.Rgba8, s.Pal[..1024]);
                var blk = Image.CreateFromData(_movie.Width / 4, _movie.Height / 4, false, Image.Format.R8, s.Pal[1024..]);
                if (_tex is null)
                {
                    _tex = ImageTexture.CreateFromImage(idx); _palTex = ImageTexture.CreateFromImage(pal); _blkTex = ImageTexture.CreateFromImage(blk);
                    var mat = (ShaderMaterial)_debandRect!.Material;
                    if (System.Environment.GetEnvironmentVariable("I76_MOVIE_DEBLOCK") == "0") mat.SetShaderParameter("deblock_strength", 0f);
                    mat.SetShaderParameter("idx", _tex); mat.SetShaderParameter("pal", _palTex); mat.SetShaderParameter("blk", _blkTex);
                }
                else { _tex.Update(idx); _palTex!.Update(pal); _blkTex!.Update(blk); }
            }
            else
            {
                var img = Image.CreateFromData(_movie.Width, _movie.Height, false, Image.Format.Rgba8, s.Rgba);
                if (_tex is null) { _tex = ImageTexture.CreateFromImage(img); _view.Texture = _tex; }
                else _tex.Update(img);
            }
        }
        switch (Watch(want))
        {
            case MovieStep.Over: RunOut(); break;
            case MovieStep.Stalled: GiveUp("the decoder stopped making frames"); RunOut(); break;   // stuck part way: it ends there
        }
    }

    /// <summary>HiFi Audio's track position as heard now (seconds from its first sample), or null when none plays.</summary>
    private double? Heard() =>
        _hifi is { } p && p.Playing ? p.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency() : null;

    /// <summary>
    /// The drift check, as the last frame goes up: where HiFi Audio's track is against that frame's time (the picture follows the
    /// track, so this is under a display frame), the wall clock's view of the same, and the track's end against the picture's.
    /// </summary>
    private void TellSync()
    {
        if (_hifi is null || _hifiTold) return;
        _hifiTold = true;
        double frame = (_movie.FrameCount - 1) * _movie.FrameDuration, end = _movie.FrameCount * _movie.FrameDuration;
        double wall = System.Diagnostics.Stopwatch.GetElapsedTime(_hifiStart).TotalSeconds;
        string heard = Heard() is { } h ? $"{h:0.000} s ({Signed((h - frame) * 1000, "0.0")} ms)" : "over";
        GD.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio sync: last frame {_movie.FrameCount - 1} (due {frame:0.000} s) up at clock {_clock.Seconds:0.000} s, "
            + $"the track heard at {heard}, wall {wall:0.000} s; the track ends at {_hifiInfo.Seconds:0.000} s, the picture at {end:0.000} s "
            + $"({Signed((_hifiInfo.Seconds - end) * 1000, "0.0")} ms, {Signed((_hifiInfo.Seconds - end) / _movie.FrameDuration, "0.00")} frames), its own track at {_hifiInfo.Own:0.000} s");
    }

    private static string Signed(double v, string format) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private MovieStep Watch(int want) =>
        _watch.Step(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency, _produced, _decoded, !_frames.IsEmpty, _started, want, _movie.FrameCount);

    private void PushSound()
    {
        if (_out is null) { _sound.Clear(); return; }
        int room = _out.GetFramesAvailable();
        while (room > 0)
        {
            if (_pending is null || _pendingAt >= _pending.Length)
            {
                if (!_sound.TryDequeue(out _pending)) return;
                _pendingAt = 0;
            }
            int n = Math.Min(room, _pending.Length - _pendingAt);
            _out.PushBuffer(_pending.AsSpan(_pendingAt, n).ToArray());
            _audioClock?.Pushed(n);
            _pendingAt += n;
            room -= n;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (_done || !Skippable) return;
        // Esc, Space, Enter, a mouse button (not the wheel, whose notches come as presses) or any pad's A, B or Start (not only one the
        // pad list knows yet: the intro plays before it has looked)
        string? input = e switch
        {
            InputEventKey { Keycode: Key.Escape or Key.Space or Key.Enter or Key.KpEnter } k => "key:" + k.Keycode,
            InputEventMouseButton m when MovieSkipGate.SkipsWith((int)m.ButtonIndex) => "mouse:" + m.ButtonIndex,
            InputEventJoypadButton { ButtonIndex: JoyButton.A or JoyButton.B or JoyButton.Start } j => $"pad{j.Device}:{j.ButtonIndex}",
            _ => null,
        };
        if (input is null) return;
        if (!e.IsPressed()) { _gate.Release(input); return; }
        GetViewport().SetInputAsHandled();   // (a skip key is the movie's, held or repeating too: nothing under it acts on it)
        if (e is InputEventKey { Echo: true }) return;
        // only a new press made while the movie plays: one already down as it started (the key, click or button that started
        // the mission) must be let go first; and none while the mission still loads under it (dropped, not kept for later)
        bool loading = IsLoading;
        if (!_gate.Press(input, loading)) { GD.Print($"movie {System.IO.Path.GetFileName(Path)}: {input} {(loading ? "pressed while the mission loads" : "was down as it started")}, not a skip"); return; }
        Skip();
    }

    private readonly MovieSkipGate _gate = new();

    private void SendInputs(int frame)
    {
        for (int n = 0; n < _inputs.Count; n++)
        {
            var (what, at, ms) = _inputs[n];
            if (ms)
            {
                if (!_inputDue.ContainsKey(n) && Time.GetTicksMsec() - _firstFrameAt >= (ulong)at) _inputDue[n] = frame;
                at = _inputDue.TryGetValue(n, out int due) ? due : -2;
            }
            bool down = frame == at;
            if (!down && frame != at + 1) continue;
            if (down) GD.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_INPUT sends {what}");
            InputEvent? e = what switch
            {
                "wheel" when down => new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 },
                "click" => new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 },
                "rclick" => new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = down, ButtonMask = down ? MouseButtonMask.Right : 0 },
                "key" => new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = down },
                "esc" => new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = down },
                "space" => new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = down },
                "pad" => new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = down },
                _ => null,
            };
            if (e is not null) Input.ParseInputEvent(e);
        }
    }

    /// <summary>The skip keys and buttons down as the movie starts (MovieSkipGate: they must be let go before they skip).</summary>
    private static IEnumerable<string> HeldSkipInputs()
    {
        foreach (var k in (Key[])[Key.Escape, Key.Space, Key.Enter, Key.KpEnter])
            if (Input.IsKeyPressed(k) || Input.IsPhysicalKeyPressed(k)) yield return "key:" + k;
        foreach (var b in (MouseButton[])[MouseButton.Left, MouseButton.Right, MouseButton.Middle])
            if (Input.IsMouseButtonPressed(b)) yield return "mouse:" + b;
        foreach (int d in Input.GetConnectedJoypads())
            foreach (var b in (JoyButton[])[JoyButton.A, JoyButton.B, JoyButton.Start])
                if (Input.IsJoyButtonPressed(d, b)) yield return $"pad{d}:{b}";
    }

    /// <summary>A skip asked for (true when the movie ended): at once, or, while <see cref="Waiting"/>, kept with the stand-by line faded in.</summary>
    private bool Skip()
    {
        if (!IsWaiting) { Finish(); return true; }
        if (!_skipWanted) GD.Print($"movie {System.IO.Path.GetFileName(Path)}: skip asked for, kept until the mission is ready");
        _skipWanted = true;
        _tag?.FadeIn();
        return false;
    }

    private bool _skipWanted;

    /// <summary>The movie is over by itself: it ends, or, while <see cref="Waiting"/>, its last frame stays up under the stand-by line until the mission can start.</summary>
    private void RunOut()
    {
        if (!IsWaiting) { Finish(); return; }
        _tag?.FadeIn();   // (the player waits on the mission now)
        Hold("over");
    }

    /// <summary>The picture stands still where it is, its sound stopped, until the mission can start (_Process ends it then).</summary>
    private void Hold(string why)
    {
        if (_held) return;
        _held = true;
        _tag?.ShowNow();   // (the rest of the load follows on the main thread: no frames for a fade)
        _stop = true;   // (the decoder is done with: only the frame shown stays)
        if (_movie is not null && _video is null) GD.Print($"movie {System.IO.Path.GetFileName(Path)}: held at frame {_clock.Frame}{Sync()}");
        _out = null;
        GetNodeOrNull<AudioStreamPlayer>("Sound")?.Stop();
        _hifi?.Stop();
        if (_video is not null) _video.Paused = true;
        GD.Print($"movie {System.IO.Path.GetFileName(Path)}: {why}, its frame held until the mission can start");
    }

    /// <summary>The port's: the stand-by line over the playing picture while the mission loads under it (<see cref="Loading"/>), out when it is done.</summary>
    private void TagForLoading()
    {
        if (_tag is null || Loading is null || _skipWanted || _held) return;
        if (_video is null && !_started) return;   // (from the picture's first frame: its lettering made meanwhile, under the black)
        if (Loading()) _tag.FadeIn(faint: true); else _tag.FadeOut();
    }

    /// <summary>The sound against the picture: how far apart they are, and how long the picture has waited for the sound.</summary>
    private string Sync() => _out is not null && _audioClock is { } ac
        ? $", sound heard to {ac.Heard(_out.GetFramesAvailable()):0.000} s with the picture at {_clock.Seconds:0.000} s (waited {_waitedForSound * 1000:0} ms for it; longest frame {_worstDelta * 1000:0} ms; buffer {SoundBuffer} s, {Ahead} frames ahead)"
        : "";

    private void GiveUp(string why) => GD.PrintErr($"movie {Path}: {why}, ending it");

    /// <summary>Ends the movie at once, as a skip does (what follows it runs): a mission whose build under its briefing failed.</summary>
    public void End() => Finish();

    private void Finish()
    {
        if (_done) return;
        _done = true;
        _stop = true;
        if (_hifi is not null && !_hifiTold && _started)
            GD.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio: ended at clock {_clock.Seconds:0.000} s (frame {_clock.Frame} of {_movie.FrameCount}), the track heard at {(Heard() is { } h ? $"{h:0.000} s" : "its end")}");
        if (_movie is not null && _video is null)
        {
            long skips = _out is null ? 0 : _out.GetSkips() - _skipsAtStart;
            Last = (_shown, _dropped, _late, _worstShowGap * 1000, skips);
            GD.Print($"movie: {System.IO.Path.GetFileName(Path)} over: {_shown} frames shown, {_dropped} dropped, {_late} late (worst gap {_worstShowGap * 1000:0} ms), audio underruns {skips} mixes{Sync()}");
        }
        if (_video is not null)
            GD.Print($"movie: replacement over: {_video.GetStreamLength():0.000} s of stream in {Wall() - _videoStart:0.000} s of wall clock, {_lateFrames} late frames (worst {_worstGap * 1000:0} ms)");
        _video?.Stop();
        // a decoder stuck inside a frame is left to itself (a background thread) rather than holding the game
        if (_thread is null || _thread.Join(1000)) _movie?.Dispose();
        // gone first, whatever follows: a failure in what comes next must not leave the movie's black over the game
        QueueFree();
        try { Finished?.Invoke(); }
        catch (System.Exception ex) { GD.PrintErr($"movie {Path}: what follows it failed: {ex}"); }
    }

    public override void _ExitTree()
    {
        _stop = true;
        _thread?.Join(1000);
    }

    // ---- an upscaled replacement (docs/playing.md "HD movies"): user://movies/<the SMK's stem>.ogv, Ogg Theora with Vorbis sound,
    // played by Godot's video player in the modern look; the 1997 look, or a file the player cannot open, plays the SMK
    private VideoStreamPlayer? _video;
    private double _videoStart, _shotAt = -1, _lastStep, _worstGap;
    private int _lateFrames;
    private int _shown, _dropped, _late;
    private long _skipsAtStart;
    /// <summary>How long the picture has waited for its sound (a hitch longer than the sound's buffer), and the longest frame.</summary>
    private double _waitedForSound, _worstDelta;
    private double _lastShown, _worstShowGap;
    /// <summary>The last Smacker movie's pace: frames shown, dropped (their turn passed unseen), late (over two frames after the one before), the worst gap, the audio generator's underrun frames.</summary>
    public static (int Shown, int Dropped, int Late, double WorstMs, long AudioSkips) Last;
    private readonly bool _videoLog = System.Environment.GetEnvironmentVariable("I76_MOVIE_LOG") == "1";
    private string? _shotPath;

    /// <summary>The user folder's movies/ file standing in for <paramref name="smk"/> (its stem, any case, .ogv), or null.</summary>
    public static string? Replacement(string smk) => MovieReplacement.Find(ProjectSettings.GlobalizePath($"user://{MovieReplacement.Folder}/"), smk);

    private bool PlayReplacement(string ogv)
    {
        var box = new AspectRatioContainer { AnchorRight = 1, AnchorBottom = 1, Ratio = 4f / 3f, MouseFilter = Control.MouseFilterEnum.Ignore };
        var video = new VideoStreamPlayer
        {
            Stream = new VideoStreamTheora { File = ogv }, Bus = I76.Game.Boot.Main.MovieBus(), Expand = true,
            MouseFilter = Control.MouseFilterEnum.Ignore, Name = "Video",
        };
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        box.AddChild(video);
        AddChild(box);
        video.Play();
        if (!video.IsPlaying()) { GD.PrintErr($"movie {ogv}: not playable, the SMK instead"); box.QueueFree(); GetChild(0).QueueFree(); return false; }
        var size = video.GetVideoTexture()?.GetSize() ?? Vector2.Zero;
        if (size.Y > 0) box.Ratio = size.X / size.Y;
        video.Finished += RunOut;
        _video = video;
        _videoStart = Wall();
        // capture hook: I76_MOVIE_SHOT=out.png@seconds saves the window at that point of the movie
        if (System.Environment.GetEnvironmentVariable("I76_MOVIE_SHOT") is { } shot && shot.LastIndexOf('@') is > 0 and var at
            && double.TryParse(shot[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture, out double t)) { _shotPath = shot[..at]; _shotAt = t; }
        GD.Print($"movie: {System.IO.Path.GetFileName(Path)} replaced by {System.IO.Path.GetFileName(ogv)} ({video.GetStreamLength():0.0} s, {size.X}x{size.Y})");
        return true;
    }

    private static double Wall() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>The same watch as the SMK's: the stream's position (in ms) is its progress; none for 5 s ends it.</summary>
    private void ReplacementStep()
    {
        if (_done || _video is null) return;
        double pos = _video.StreamPosition, now = Wall();
        // the screen's pace under the video: frames that came later than two of its frames (1/15 s at 30 fps)
        if (_lastStep > 0 && now - _lastStep > 1 / 15.0) { _lateFrames++; _worstGap = Math.Max(_worstGap, now - _lastStep); if (_videoLog) GD.Print($"movie: late frame {(now - _lastStep) * 1000:0} ms at {pos:0.000} s"); }
        if (_videoLog && (int)(now - _videoStart) != (int)(_lastStep - _videoStart)) GD.Print($"movie: stream {pos:0.000} s at wall {now - _videoStart:0.000} s");
        _lastStep = now;
        if (_shotAt >= 0 && pos >= _shotAt)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GD.Print($"movie: shot at {pos:0.000} s of the stream, {Wall() - _videoStart:0.000} s of wall clock");
            _shotAt = -1;
        }
        int ms = (int)(pos * 1000);
        switch (_watch.Step(Wall(), ms, false, false, ms > 0, 0, int.MaxValue))
        {
            case MovieStep.NoFirstFrame: GiveUp("no first frame"); RunOut(); break;
            case MovieStep.Stalled: GiveUp("the video stopped"); RunOut(); break;
        }
    }

    /// <summary>The install's smk/ file of that name (any case), or null.</summary>
    public static string? Find(I76.Formats.Vfs.GameInstall install, string name)
    {
        if (install.FindDir("smk") is not { } dir) return null;
        return System.IO.Directory.EnumerateFiles(dir).FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
