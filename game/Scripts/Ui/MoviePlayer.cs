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
    /// <see cref="WaitingTag"/> shows over it too, faint (READY once <see cref="Waiting"/> is false: StandByLine). No press skips meanwhile,
    /// nor is one kept for later: a new press once it is false does (MovieSkipGate).
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
    /// <summary>The player skipped it (a skip press taken, kept or done), as against its running out or being ended (a network co-op briefing tells the host).</summary>
    public bool Skipped { get; private set; }
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

    /// <summary>The upscaled replacements' frame rate (the renders' own, 30 a second), for <see cref="NextFrameMs"/>.</summary>
    private const double ReplacementFps = 30;
    private static double _dueSeconds = double.NaN;
    private static long _dueAt;

    private static void Due(double seconds) { _dueSeconds = seconds; _dueAt = System.Diagnostics.Stopwatch.GetTimestamp(); }

    /// <summary>
    /// The port's: how long until the playing movie's next frame is due to go up, milliseconds, or null with no movie playing (held or
    /// over): a mission attaching under its briefing starts its steps right after a frame went up, not just before one (Main.RunSteps).
    /// </summary>
    public static double? NextFrameMs => double.IsNaN(_dueSeconds) ? null
        : _dueSeconds * 1000 - System.Diagnostics.Stopwatch.GetElapsedTime(_dueAt).TotalMilliseconds;

    private Smacker _movie = null!;
    /// <summary>
    /// An upscaled replacement decoded on this player's own thread ([port] movie_decode=thread, the native library loaded): the
    /// second source of frames for the same picture, clock, sound and watch as the Smacker's, in place of the engine's player.
    /// </summary>
    private OgvDecoder? _ogv;
    /// <summary>A movie of either source is open: the picture's size, a frame's time and the frame count (an .ogv's from its last page; set at its end when that was unknown).</summary>
    private bool _source;
    private int _w, _h;
    private double _fd;
    private volatile int _fc;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _decoded;
    /// <summary>Frames decoded ahead: the picture's RGBA (or the Smacker's indices and palette for the deband pass; an .ogv's packed 4:2:0 planes).</summary>
    private readonly MovieRing<(byte[] Rgba, byte[]? Pal)> _frames = new();
    private readonly ConcurrentQueue<Vector2[]> _sound = new();
    /// <summary>
    /// Frames decoded ahead of the picture, their sound with them: 20, or 60 (4 s at 15 a second) for a briefing over a
    /// mission's load (<see cref="Loading"/>), whose main-thread steps can hold the game for a second or more (#157).
    /// An .ogv's by time and memory (MovieAhead: 2 s over a load, 31 frames, 123 MB at 1920x1440; 0.75 s otherwise, 12 frames, 47 MB).
    /// </summary>
    private int Ahead => _ogvAhead > 0 ? _ogvAhead : Loading is null ? 20 : 60;
    private int _ogvAhead;
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
        if (!Faithful && Replacement(Path) is { } ogv)
        {
            if (DecodeOgvHere(ogv)) { AddTag(ReplacementPicture(new Vector2(_w, _h))); return; }
            if (PlayReplacement(ogv)) { AddTag(ReplacementPicture(_video?.GetVideoTexture()?.GetSize() ?? Vector2.Zero)); return; }
        }
        try { _movie = Smacker.Open(Path); }
        catch (System.Exception ex)
        {
            GD.PrintErr($"movie {Path}: {ex.Message}");
            CallDeferred(MethodName.Finish);
            return;
        }
        (_source, _w, _h, _fd, _fc) = (true, _movie.Width, _movie.Height, _movie.FrameDuration, _movie.FrameCount);
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
                I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio {entry} ({ogg.Channels} ch, {ogg.Rate} Hz, {ogg.Samples} samples, {ogg.Seconds:0.000} s) "
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
                    _frames.Add(index, ((byte[])px.Clone(), rgbaPal));
                    _produced++;
                    continue;
                }
                var rgba = new byte[m.Width * m.Height * 4];
                for (int i = 0, o = 0; i < px.Length; i++, o += 4)
                {
                    int c = px[i] * 3;
                    rgba[o] = pal[c]; rgba[o + 1] = pal[c + 1]; rgba[o + 2] = pal[c + 2]; rgba[o + 3] = 255;
                }
                _frames.Add(index, (rgba, null));
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
        if (_done || !_source) return;
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
            if (IsLoading) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_SKIP while the mission loads, not a skip");
            else { I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_SKIP skips it{(IsWaiting ? " (kept until the mission is ready)" : "")}"); if (Skip() || _held) return; }
        }
        if (_endAfter >= 0 && _endAfter-- == 0) { I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_END runs it out"); RunOut(); return; }
        // capture hook: I76_MOVIE_HOLD=N freezes the picture on frame N, so shots of different runs compare
        if (_hold >= 0 && _frames.IsEmpty && !_decoded) _clock.Set(_clock.Seconds - delta);   // (a capture runs faster than real time: wait for the decoder)
        if (_hold >= 0 && _clock.Seconds >= _hold * _fd) _clock.Set(_hold * _fd);
        PushSound();
        int want = _clock.Frame;
        Due((want + 1) * _fd - _clock.Seconds);
        if (_ogv is not null) Publish(_clock.Seconds);
        (int Index, byte[] Rgba, byte[]? Pal)? show = null;
        if (_frames.TakeDue(want, out int shownIndex, out var taken, out int passed, _ogv is null ? null : Release))
        {
            show = (shownIndex, taken.Rgba, taken.Pal);
            _dropped += passed;   // frames whose turn passed between two of the game's frames: never shown
        }
        double wallS = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        if (show is not null) PictureUp(wallS, show.Value.Index);
        _pace ??= new MoviePace(_fd);
        _pace.Frame(wallS, show?.Index ?? -1);
        PaceRow(wallS, show?.Index ?? -1);
        if (show is { } so && _ogv is not null)
        {
            // the packed 4:2:0 planes in one R8 image: the shader makes the colour; the buffer back to the decoder once copied
            var (pw, ph) = _ogv.Packed;
            var img = Image.CreateFromData(pw, ph, false, Image.Format.R8, so.Rgba);
            Release((so.Rgba, null));
            if (_tex is null) { _tex = ImageTexture.CreateFromImage(img); _view.Texture = _tex; }
            else _tex.Update(img);
            img.Dispose();
            ShotAt(_clock.Seconds);
        }
        else if (show is { } s1)
        {
            var s = s1;
            if (s.Index == _fc - 1) TellSync();
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
        I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio sync: last frame {_movie.FrameCount - 1} (due {frame:0.000} s) up at clock {_clock.Seconds:0.000} s, "
            + $"the track heard at {heard}, wall {wall:0.000} s; the track ends at {_hifiInfo.Seconds:0.000} s, the picture at {end:0.000} s "
            + $"({Signed((_hifiInfo.Seconds - end) * 1000, "0.0")} ms, {Signed((_hifiInfo.Seconds - end) / _movie.FrameDuration, "0.00")} frames), its own track at {_hifiInfo.Own:0.000} s");
    }

    private static string Signed(double v, string format) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private MovieStep Watch(int want) =>
        _watch.Step(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency, _produced, _decoded, !_frames.IsEmpty, _started, want, _fc);

    /// <summary>
    /// A new movie frame went up at <paramref name="wall"/> seconds: the picture's pace (a frame shown more than two of the movie's
    /// frames after the one before it is late) and how far it is behind the wall clock since the first frame (the same on the
    /// engine's player, ReplacementStep).
    /// </summary>
    private void PictureUp(double wall, int index)
    {
        if (_lastShown > 0 && wall - _lastShown > 2 * _fd) _late++;
        if (_lastShown > 0) _worstShowGap = Math.Max(_worstShowGap, wall - _lastShown);
        if (_shown == 0)
        {
            _firstShownWall = wall - index * _fd;
            if (I76.Game.Boot.Main.AudioRecSeconds() is { } rec) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: first picture up ({Source()}), the recording at {rec:0.000} s");
        }
        _lastShown = wall;
        _lastShownIndex = index;
        _shown++;
    }

    private double _firstShownWall;
    private int _lastShownIndex = -1;

    /// <summary>How far the picture up is behind the wall clock from the first frame (ms): what a stall cost it and never made up.</summary>
    private double BehindMs() => _shown == 0 ? 0 : (Wall() - _firstShownWall - _lastShownIndex * _fd) * 1000;

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

    /// <summary>
    /// The skip input an event is, named for MovieSkipGate, or null: Esc, Space, Enter, a mouse button (not the wheel, whose notches
    /// come as presses) or any pad's A, B or Start (not only one the pad list knows yet: the intro plays before it has looked).
    /// </summary>
    public static string? SkipInput(InputEvent e) => e switch
    {
        InputEventKey { Keycode: Key.Escape or Key.Space or Key.Enter or Key.KpEnter } k => "key:" + k.Keycode,
        InputEventMouseButton m when MovieSkipGate.SkipsWith((int)m.ButtonIndex) => "mouse:" + m.ButtonIndex,
        InputEventJoypadButton { ButtonIndex: JoyButton.A or JoyButton.B or JoyButton.Start } j => $"pad{j.Device}:{j.ButtonIndex}",
        _ => null,
    };

    public override void _Input(InputEvent e)
    {
        if (_done || !Skippable) return;
        if (SkipInput(e) is not { } input) return;
        if (!e.IsPressed()) { _gate.Release(input); return; }
        GetViewport().SetInputAsHandled();   // (a skip key is the movie's, held or repeating too: nothing under it acts on it)
        if (e is InputEventKey { Echo: true }) return;
        // only a new press made while the movie plays: one already down as it started (the key, click or button that started
        // the mission) must be let go first; and none while the mission still loads under it (dropped, not kept for later)
        bool loading = IsLoading;
        if (!_gate.Press(input, loading)) { I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: {input} {(loading ? "pressed while the mission loads" : "was down as it started")}, not a skip"); return; }
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
            if (down) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: I76_MOVIE_INPUT sends {what}");
            if (Scripted(what, down) is { } e) Input.ParseInputEvent(e);
        }
    }

    /// <summary>A capture hook's input by name (I76_MOVIE_INPUT, I76_CARD_INPUT), pressed or let go, or null.</summary>
    public static InputEvent? Scripted(string what, bool down) => what switch
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

    /// <summary>The skip keys and buttons down now (MovieSkipGate: those down as a movie or the card starts must be let go before they skip).</summary>
    public static IEnumerable<string> HeldSkipInputs()
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
        Skipped = true;
        if (!_skipWanted) I76.Game.World.MissionBuilder.Trace($"movie: skip pressed ({(IsWaiting ? "the mission not ready" : "the mission ready")})");
        if (!IsWaiting) { Finish(); return true; }
        if (!_skipWanted) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: skip asked for, kept until the mission is ready");
        _skipWanted = true;
        _tag?.FadeIn();
        return false;
    }

    private bool _skipWanted, _toldReady;

    /// <summary>
    /// The movie is over by itself: it ends. While <see cref="Waiting"/> what follows it shows the wait (Main.BuildBehindCard: the
    /// loading card again, with PLEASE STAND BY and its progress line), rather than its last frame standing still.
    /// </summary>
    private void RunOut()
    {
        if (IsWaiting) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: over before the mission is ready, the loading card follows");
        Finish();
    }

    /// <summary>The picture stands still where it is, its sound stopped, until the mission can start (_Process ends it then).</summary>
    private void Hold(string why)
    {
        if (_held) return;
        _held = true;
        _dueSeconds = double.NaN;
        _tag?.ShowNow();   // (the rest of the load follows on the main thread: no frames for a fade)
        _stop = true;   // (the decoder is done with: only the frame shown stays)
        if (_source) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: held at frame {_clock.Frame}{Sync()}");
        _out = null;
        GetNodeOrNull<AudioStreamPlayer>("Sound")?.Stop();
        _hifi?.Stop();
        if (_video is not null) _video.Paused = true;
        I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: {why}, its frame held until the mission can start");
    }

    /// <summary>
    /// The port's: the stand-by line over the playing picture while the mission is not ready (<see cref="Waiting"/>), READY in its
    /// place once it is (StandByLine), from the picture's first frame (its lettering made meanwhile, under the black).
    /// </summary>
    private void TagForLoading()
    {
        if (_tag is null || Loading is null || _skipWanted || _held) return;
        var (says, faint) = StandByLine.For(_video is not null || _started, !IsWaiting, false);
        if (says == StandByLine.Says.Ready && !_toldReady) { _toldReady = true; I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: the mission ready, READY: a press starts it"); I76.Game.Boot.RunLog.Flush(); }
        _tag.Show(says, faint);
    }

    /// <summary>The sound against the picture: how far apart they are, and how long the picture has waited for the sound.</summary>
    private string Sync() => _out is not null && _audioClock is { } ac
        ? $", sound heard to {ac.Heard(_out.GetFramesAvailable()):0.000} s with the picture at {_clock.Seconds:0.000} s (waited {_waitedForSound * 1000:0} ms for it; longest frame {_worstDelta * 1000:0} ms; buffer {SoundBuffer} s, {Ahead} frames ahead)"
        : "";

    private void GiveUp(string why) => GD.PrintErr($"movie {Path}: {why}, ending it");

    /// <summary>The picture's pace (late frames against their schedule, the longest frame of the game) while it played.</summary>
    private MoviePace? _pace;
    /// <summary>The last Smacker movie's pace: its frames, those that came up over 1.5 frame times late, the latest's lateness and the game's longest frame (ms).</summary>
    public static (int Frames, int Late, double LateMs, double WorstFrameMs) LastPace;

    /// <summary>
    /// I76_MOVIE_PACE=FILE.csv (debug): one row for each of the game's frames while the movie plays: the wall time from its first
    /// frame, the frame's length, the movie's frame shown (-1: none), the picture's clock against the sound heard, the collections
    /// and their pause, the pipelines compiled, the video memory of textures and buffers, and the load's last stamp. Late frames
    /// set against what the frames around them did say what the load did under them (tools/load-time/pace.py).
    /// </summary>
    private static readonly string? PaceLog = System.Environment.GetEnvironmentVariable("I76_MOVIE_PACE") is { Length: > 0 } pl ? pl : null;
    private System.IO.StreamWriter? _paceOut;
    private double _paceStart, _paceLast;

    private static readonly bool PacePipes = System.Environment.GetEnvironmentVariable("I76_MOVIE_PACE_PIPES") == "1";

    private void PaceRow(double wall, int shown)
    {
        if (PaceLog is null) return;
        if (_paceOut is null)
        {
            try
            {
                // a second movie of the run (a warm start's) goes to FILE.2.csv, and so on
                string path = PaceLog;
                for (int n = 2; System.IO.File.Exists(path) && _paceFiles.Contains(path); n++) path = PaceLog.Replace(".csv", $".{n}.csv");
                _paceFiles.Add(path);
                // (rows kept in a big buffer: written when it fills or the movie ends, not every frame)
                _paceOut = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8, 1 << 22);
                _paceOut.WriteLine("t_ms,frame_ms,shown,clock_ms,heard_ms,offset_ms,gc0,gc1,gc2,gc_pause_ms,pipelines,tex_mb,buf_mb,draw_ms,loading,stage");
                // the renderer's part of each frame: from its pre-draw to its post-draw signal (the queued calls of other threads run in it)
                RenderingServer.Singleton.FramePreDraw += PacePreDraw;
                RenderingServer.Singleton.FramePostDraw += PacePostDraw;
            }
            catch (System.IO.IOException ex) { GD.PrintErr($"movie pace: {ex.Message}"); return; }
            _paceStart = _paceLast = wall;
        }
        double heard = _out is not null && _audioClock is { } ac ? ac.Heard(_out.GetFramesAvailable()) : double.NaN;
        long pipes = -1;   // I76_MOVIE_PACE_PIPES=1: the renderer's pipeline count and video memory (each read waits for its thread: off by default)
        if (PacePipes)
        {
            pipes = 0;
            foreach (var k in new[] { RenderingServer.RenderingInfo.PipelineCompilationsCanvas, RenderingServer.RenderingInfo.PipelineCompilationsMesh,
                     RenderingServer.RenderingInfo.PipelineCompilationsSurface, RenderingServer.RenderingInfo.PipelineCompilationsDraw, RenderingServer.RenderingInfo.PipelineCompilationsSpecialization })
                pipes += (long)RenderingServer.GetRenderingInfo(k);
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double texMb = PacePipes ? Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0 : -1;
        double bufMb = PacePipes ? Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576.0 : -1;
        _paceOut.WriteLine(string.Create(inv, $"{(wall - _paceStart) * 1000:0.00},{(wall - _paceLast) * 1000:0.00},{shown},{_clock.Seconds * 1000:0.0},{heard * 1000:0.0},{(_clock.Seconds - heard) * 1000:0.0},"
            + $"{System.GC.CollectionCount(0)},{System.GC.CollectionCount(1)},{System.GC.CollectionCount(2)},{System.GC.GetTotalPauseDuration().TotalMilliseconds:0.00},{pipes},"
            + $"{texMb:0.00},{bufMb:0.00},"
            + $"{_paceDraw * 1000:0.00},{(IsLoading ? 1 : 0)},\"{I76.Game.World.MissionBuilder.LastStage.Replace("\"", "'")}\""));
        _paceLast = wall;
        _paceDraw = 0;
    }

    private double _preDrawAt, _paceDraw;
    private void PacePreDraw() => _preDrawAt = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    private void PacePostDraw() { if (_preDrawAt > 0) _paceDraw += System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - _preDrawAt; }

    private void ClosePace()
    {
        if (_paceOut is null) return;
        _paceOut.Dispose();
        _paceOut = null;
        RenderingServer.Singleton.FramePreDraw -= PacePreDraw;
        RenderingServer.Singleton.FramePostDraw -= PacePostDraw;
    }

    private static readonly HashSet<string> _paceFiles = [];

    /// <summary>Ends the movie at once, as a skip does (what follows it runs): a mission whose build under its briefing failed.</summary>
    public void End() => Finish();

    private void Finish()
    {
        if (_done) return;
        _done = true;
        _dueSeconds = double.NaN;
        _stop = true;
        if (_hifi is not null && !_hifiTold && _started)
            I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: HiFi Audio: ended at clock {_clock.Seconds:0.000} s (frame {_clock.Frame} of {_movie.FrameCount}), the track heard at {(Heard() is { } h ? $"{h:0.000} s" : "its end")}");
        if (_source || _video is not null)
        {
            long skips = _out is null ? 0 : _out.GetSkips() - _skipsAtStart;
            Last = (_shown, _dropped, _late, _worstShowGap * 1000, skips);
            if (I76.Game.Boot.Main.AudioRecSeconds() is { } rec) I76.Game.Boot.RunLog.Print($"movie {System.IO.Path.GetFileName(Path)}: over, the recording at {rec:0.000} s");
            // (the engine's player: frames shown are its picture's changes of frame, dropped those it went past; its sound is its own)
            I76.Game.Boot.RunLog.Print($"movie: {System.IO.Path.GetFileName(Path)} over ({Source()}): {_shown} frames shown, {_dropped} dropped, {_late} late (worst gap {_worstShowGap * 1000:0} ms), "
                + $"behind the wall clock {BehindMs():0} ms at frame {_lastShownIndex}, audio underruns {skips} mixes{Sync()}{(_ogv is not null ? $", {_uncopied} decoded past uncopied" : "")}");
            if (_pace is not null)
            {
                var (late, lateBy) = _pace.Late();
                LastPace = (_pace.Frames, late, lateBy * 1000, _pace.WorstFrame * 1000);
                I76.Game.Boot.RunLog.Print($"movie pace: {System.IO.Path.GetFileName(Path)}: {_pace.Frames} frames, {late} came up over {MoviePace.LateFrames} frame times late (the latest {lateBy * 1000:0} ms), the game's longest frame {_pace.WorstFrame * 1000:0} ms");
            }
        }
        ClosePace();
        if (_video is not null)
            I76.Game.Boot.RunLog.Print($"movie: replacement over: {_video.GetStreamLength():0.000} s of stream in {Wall() - _videoStart:0.000} s of wall clock, {_lateFrames} late frames (worst {_worstGap * 1000:0} ms)");
        _video?.Stop();
        // a decoder stuck inside a frame is left to itself (a background thread) rather than holding the game
        if (_thread is null || _thread.Join(1000)) { _movie?.Dispose(); _ogv?.Dispose(); _frames.Clear(); }
        // gone first, whatever follows: a failure in what comes next must not leave the movie's black over the game
        QueueFree();
        try { Finished?.Invoke(); }
        catch (System.Exception ex) { GD.PrintErr($"movie {Path}: what follows it failed: {ex}"); }
    }

    public override void _ExitTree()
    {
        _dueSeconds = double.NaN;
        ClosePace();
        _stop = true;
        if ((_thread is null || _thread.Join(1000)) && !_done) { _ogv?.Dispose(); _frames.Clear(); }
    }

    /// <summary>What decodes the picture, for the log: the Smacker's thread, the .ogv's own thread or the engine's player.</summary>
    private string Source() => _video is not null ? "the engine's player" : _ogv is not null ? "the .ogv on its own thread" : "the Smacker on its own thread";

    // ---- an upscaled replacement decoded here ([port] movie_decode=thread, the default): the native library (native/ogv, i26ogv)
    // decodes it on this player's thread as the Smacker is, the picture ahead by time (MovieAhead) as packed 4:2:0 planes the
    // shader colours, its sound ahead of the picture into the same generator and audio clock; without the library, or a file it
    // cannot open, the engine's player below as before

    /// <summary>Where the native decoder is looked for: I76_OGV_LIB, the project's native/&lt;platform&gt;/, beside the executable (an export), beside the assemblies.</summary>
    public static IEnumerable<string?> OgvLibraryPaths()
    {
        string file = OgvLibrary.FileName, plat = OgvLibrary.Platform;
        yield return System.Environment.GetEnvironmentVariable("I76_OGV_LIB");
        string res = ProjectSettings.GlobalizePath($"res://native/{plat}/{file}");
        if (System.IO.Path.IsPathRooted(res)) yield return res;
        string exe = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        yield return System.IO.Path.Combine(exe, file);
        yield return System.IO.Path.Combine(exe, "native", plat, file);
        yield return System.IO.Path.Combine(System.AppContext.BaseDirectory, file);
    }

    /// <summary>What plays an upscaled movie now: the setting and its hook, thread only with the library loaded (logged once).</summary>
    public static MovieDecodeMode OgvDecodeMode()
    {
        var mode = MovieDecode.Choose(I76.Game.Shell.ShellSettings.Load().MovieDecode, System.Environment.GetEnvironmentVariable("I76_MOVIE_DECODE"), () =>
        {
            bool first = !_libraryTold;
            bool ok = OgvLibrary.Load(OgvLibraryPaths());
            if (first)
            {
                _libraryTold = true;
                I76.Game.Boot.RunLog.Print(ok ? $"movie decode: {OgvLibrary.LoadedFrom} ({OgvLibrary.Versions})" : $"movie decode: the native decoder not loaded ({OgvLibrary.Missing}), the engine's player plays the upscaled movies");
            }
            return ok;
        });
        return mode;
    }

    private static bool _libraryTold;

    /// <summary>The replacement opened in the native decoder and its thread started (true); false: the engine's player instead.</summary>
    private bool DecodeOgvHere(string ogv)
    {
        if (OgvDecodeMode() != MovieDecodeMode.Thread) return false;
        OgvDecoder? d;
        try { d = OgvDecoder.Open(ogv); }
        catch (System.Exception ex) { GD.PrintErr($"movie {ogv}: {ex.Message}"); d = null; }
        if (d is null || !d.Info.Is420)
        {
            GD.PrintErr($"movie {ogv}: {(d is null ? "the native decoder cannot open it" : $"pixel format {d.Info.PixelFormat}, not 4:2:0")}, the engine's player instead");
            d?.Dispose();
            return false;
        }
        _ogv = d;
        var info = d.Info;
        (_source, _w, _h, _fd) = (true, info.Width, info.Height, info.FrameDuration);
        _fc = info.FrameCount > 0 ? (int)info.FrameCount : int.MaxValue;
        _ogvAhead = MovieAhead.Frames(Loading is null ? MovieAhead.PlainSeconds : MovieAhead.LoadingSeconds, _fd, d.PackedBytes);
        _clock = new MovieClock(_fd);
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        var box = new AspectRatioContainer { AnchorRight = 1, AnchorBottom = 1, Ratio = (float)_w / _h, MouseFilter = Control.MouseFilterEnum.Ignore };
        var (pw, ph) = d.Packed;
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/movie_yuv.gdshader") };
        mat.SetShaderParameter("luma_size", new Vector2(_w, _h));
        mat.SetShaderParameter("packed_size", new Vector2(pw, ph));
        _view = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear, Material = mat, Name = "Video",
        };
        box.AddChild(_view);
        AddChild(box);
        if (info.AudioChannels > 0 && _hold < 0)
        {
            var gen = new AudioStreamGenerator { MixRate = info.AudioRate, BufferLength = SoundBuffer };
            var player = new AudioStreamPlayer { Stream = gen, Name = "Sound", Bus = I76.Game.Boot.Main.MovieBus() };
            AddChild(player);
            player.Play();
            _out = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
            _audioClock = new MovieAudioClock(info.AudioRate, _out.GetFramesAvailable());
        }
        if (System.Environment.GetEnvironmentVariable("I76_MOVIE_SHOT") is { } shot && shot.LastIndexOf('@') is > 0 and var at
            && double.TryParse(shot[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture, out double t)) { _shotPath = shot[..at]; _shotAt = t; }
        I76.Game.Boot.RunLog.Print($"movie: {System.IO.Path.GetFileName(Path)} replaced by {System.IO.Path.GetFileName(ogv)} on its own thread ({_w}x{_h}, {1 / _fd:0.###} fps, "
            + $"{(info.FrameCount > 0 ? $"{info.FrameCount} frames, {info.FrameCount * _fd:0.0} s" : "length unknown")}, sound {info.AudioRate} Hz x{info.AudioChannels}; "
            + $"{_ogvAhead} frames ahead, {MovieAhead.Bytes(_ogvAhead, d.PackedBytes) / 1048576.0:0} MB)");
        _thread = new Thread(DecodeOgv) { IsBackground = true, Name = "ogv" };
        _thread.Start();
        return true;
    }

    /// <summary>Packed frames' buffers, back from the main thread once uploaded (pinned: the native decoder writes into them).</summary>
    private readonly ConcurrentBag<byte[]> _pool = [];
    private byte[] Rent() => _pool.TryTake(out var b) ? b : System.GC.AllocateUninitializedArray<byte>(_ogv!.PackedBytes, pinned: true);
    private void Release((byte[] Rgba, byte[]? Pal) f) { if (_pool.Count < Ahead + 2) _pool.Add(f.Rgba); }

    /// <summary>The main thread's clock as it last set it, and when: the decoder's due frame runs on from it through a long step (MovieAhead.Due).</summary>
    private readonly object _publishLock = new();
    private double _pubClock;
    private long _pubAt;
    private bool _pubStarted;
    private void Publish(double clock) { lock (_publishLock) { _pubClock = clock; _pubAt = System.Diagnostics.Stopwatch.GetTimestamp(); _pubStarted = _started; } }
    private int DueNow()
    {
        lock (_publishLock)
            return MovieAhead.Due(_pubStarted, _pubClock, System.Diagnostics.Stopwatch.GetElapsedTime(_pubAt).TotalSeconds, _fd);
    }

    /// <summary>Frames the decoder went past without copying out (their turn gone: after a long main-thread step).</summary>
    private volatile int _uncopied;

    private void DecodeOgv()
    {
        var d = _ogv!;
        int ch = d.Info.AudioChannels, rate = d.Info.AudioRate;
        bool feed = ch > 0 && _out is not null;
        var pcm = new float[1024 * Math.Max(1, ch)];
        double videoAt = 0, soundAt = 0;
        bool videoDone = false, soundDone = !feed;
        long next = 0, last = -1;
        int run = 0;
        try
        {
            while (!_stop)
            {
                // the sound first, SoundLeadSeconds past the picture decoded: with the ring it reaches past the generator's buffer, so a
                // long step of the main thread (which pushes it) finds enough waiting for the whole buffer
                if (!soundDone && (videoDone || soundAt < videoAt + MovieAhead.SoundLeadSeconds))
                {
                    int n = d.ReadAudio(pcm);
                    if (n == 0) { soundDone = true; continue; }
                    var frames = new Vector2[n];
                    for (int i = 0; i < n; i++) frames[i] = ch == 1 ? new Vector2(pcm[i], pcm[i]) : new Vector2(pcm[i * ch], pcm[i * ch + 1]);
                    _sound.Enqueue(frames);
                    soundAt += n / (double)rate;
                    continue;
                }
                if (videoDone) { if (soundDone) break; Thread.Sleep(5); continue; }
                int due = DueNow();
                if (_frames.Count >= Ahead)
                {
                    // full: what a long step has passed by goes, so the ring holds frames from the due one on when it ends
                    if (_frames.DropPassed(due, Release) == 0) Thread.Sleep(2);
                    continue;
                }
                bool copy = MovieAhead.ShouldCopy(next, due, run);
                byte[]? buf = copy ? Rent() : null;
                if (!d.NextPacked(buf, out long index, out double seconds))
                {
                    if (buf is not null) _pool.Add(buf);
                    videoDone = true;
                    _fc = (int)(last + 1);
                    continue;
                }
                last = index;
                next = index + 1;
                videoAt = seconds + _fd;
                if (buf is null) { run++; _uncopied++; continue; }
                run = 0;
                _frames.Add((int)index, (buf, null));
                _produced++;
            }
        }
        catch (System.Exception ex) { GD.PrintErr($"movie {Path}: {ex.Message}"); }
        _decoded = true;
    }

    /// <summary>I76_MOVIE_SHOT=out.png@seconds on the threaded path: the window saved as the clock passes that point (the picture's check against a reference decode).</summary>
    private void ShotAt(double clock)
    {
        if (_shotFrame >= 0 && _shotPath is not null)
        {
            // the frame uploaded last time has been drawn since: the window holds it now
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            I76.Game.Boot.RunLog.Print($"movie: shot of frame {_shotFrame} to {_shotPath}");
            _shotFrame = -1; _shotPath = null;
            return;
        }
        if (_shotAt < 0 || clock < _shotAt || _shotPath is null) return;
        _shotAt = -1;
        _shotFrame = _lastShownIndex;
    }

    private int _shotFrame = -1;

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
        // (its frame rate for the pace measure only, as the threaded path's: the engine's player does not tell it)
        _fd = TheoraHeader.Read(ogv)?.FrameDuration is > 0 and var fd ? fd : 1 / ReplacementFps;
        // capture hook: I76_MOVIE_SHOT=out.png@seconds saves the window at that point of the movie
        if (System.Environment.GetEnvironmentVariable("I76_MOVIE_SHOT") is { } shot && shot.LastIndexOf('@') is > 0 and var at
            && double.TryParse(shot[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture, out double t)) { _shotPath = shot[..at]; _shotAt = t; }
        I76.Game.Boot.RunLog.Print($"movie: {System.IO.Path.GetFileName(Path)} replaced by {System.IO.Path.GetFileName(ogv)} ({video.GetStreamLength():0.0} s, {size.X}x{size.Y})");
        return true;
    }

    /// <summary>
    /// The picture the stand-by line is laid out on over a replacement: the SMK's own height (the line's size and margin are in its
    /// pixels, as over the SMK itself) at the replacement's aspect. Its 4:3 alone, as before, put the line 14 picture pixels in from a
    /// 4x3 picture's corner, that is far off the screen, so over a replacement nothing showed until the loading card came back.
    /// </summary>
    private Vector2 ReplacementPicture(Vector2 size)
    {
        float h = I76.Game.Shell.ShellFrame.H;
        try { using var smk = Smacker.Open(Path); if (smk.Height > 0) h = smk.Height; }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or System.UnauthorizedAccessException) { }
        var (x, y) = StandByLine.ReplacementPicture(h, size.X, size.Y);
        return new Vector2(x, y);
    }

    private static double Wall() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>The same watch as the SMK's: the stream's position (in ms) is its progress; none for 5 s ends it.</summary>
    private void ReplacementStep()
    {
        if (_done || _video is null) return;
        double pos = _video.StreamPosition, now = Wall();
        Due((System.Math.Floor(pos * ReplacementFps) + 1) / ReplacementFps - pos);
        // the screen's pace under the video: frames that came later than two of its frames (1/15 s at 30 fps)
        if (_lastStep > 0 && now - _lastStep > 1 / 15.0) { _lateFrames++; _worstGap = Math.Max(_worstGap, now - _lastStep); if (_videoLog) I76.Game.Boot.RunLog.Print($"movie: late frame {(now - _lastStep) * 1000:0} ms at {pos:0.000} s"); }
        if (_videoLog && (int)(now - _videoStart) != (int)(_lastStep - _videoStart)) I76.Game.Boot.RunLog.Print($"movie: stream {pos:0.000} s at wall {now - _videoStart:0.000} s");
        _lastStep = now;
        // the same pace measure as the threaded paths': its picture's frame from the stream's position, a change of it a frame up
        int index = pos > 0 ? (int)Math.Floor(pos / _fd + 1e-6) : -1;
        if (index > _lastShownIndex)
        {
            if (_lastShownIndex >= 0) _dropped += index - _lastShownIndex - 1;
            PictureUp(now, index);
        }
        _pace ??= new MoviePace(_fd);
        _pace.Frame(now, index);   // (a frame still up counts once: MoviePace takes only a later one)
        if (_shotAt >= 0 && pos >= _shotAt)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            I76.Game.Boot.RunLog.Print($"movie: shot at {pos:0.000} s of the stream, {Wall() - _videoStart:0.000} s of wall clock");
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
