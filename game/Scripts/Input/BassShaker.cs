using Godot;
using I76.Formats.Input;
using I76.Sim.Damage;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;

namespace I76.Game;

/// <summary>
/// The port's bass-shaker output (Audio Control's LFE Level above 0; the original has none): <see cref="TactileSynth"/>'s
/// signal from the player's car on a bus of its own ("Tactile", into Master; the LFE Level is the synth's gain, inside its limiter). In 5.1 and 7.1 it is
/// on the LFE channel alone, for a shaker on an amplifier's or receiver's sub output: Godot's buses carry the output as stereo
/// pairs (front, centre and LFE, rear, side), a player with the centre mix target writes the centre/LFE pair, its frame's right
/// half the LFE, so the frames go out as (0, s). With a stereo device there is no LFE pair (the right half would land on the
/// right speaker): nothing plays. Godot's 3D panner also sends every positional sound to the LFE at full level, so the shaker
/// feels the game's sounds under the synth; no effect a script can add reaches one channel of a pair, so that stays. In Stereo
/// (and Phones) the frames go out as (s, s) on the front pair, for a sub or shaker fed from the stereo output below its own
/// crossover, or however a rig takes it (the effects' own bass is boosted there too: <see cref="I76.Game.Boot.Main.ApplyAudio"/>). Presentation only.
/// </summary>
public partial class BassShaker : Node
{
    /// <summary>Audio Control's Output.</summary>
    public static AudioOutput Output { get; private set; }
    /// <summary>True while something is over the game that does not pause it (as <see cref="ForceFeedback.Held"/>).</summary>
    public static Func<bool>? Held { get; set; }
    public const string BusName = "Tactile";
    private static BassShaker? _instance;

    public Func<Vehicle?>? Player { get; set; }

    private static TactileEq? _eq;
    private static float _crossover = AudioOutputs.DefaultCrossover;
    private static float _gain = AudioOutputs.ShakerGain(AudioOutputs.DefaultLevel);
    private static int _level = AudioOutputs.DefaultLevel;

    private static int[] _sources = TactileSources.Defaults();

    /// <summary>Audio Control's Output and LFE Level (0 to 200 %, the synth's gain; the Tactile bus muted at 0), [port] tactile_eq, crossover and the sources' levels (lfe_engine ...): live.</summary>
    public static void Apply(AudioOutput output, int levelPercent, TactileEq? eq, float crossover, int[] sources)
    {
        _sources = sources;
        Output = output;
        _eq = eq;
        _crossover = crossover;
        _gain = AudioOutputs.ShakerGain(levelPercent);
        _level = levelPercent;
        int bus = TactileBus();
        AudioServer.SetBusMute(bus, levelPercent <= 0);
        // the level is the synth's, before its limiter: a gain on the bus would put the loud thumps over the Master limiter
        AudioServer.SetBusVolumeDb(bus, 0f);
        if (_instance is { } me) { me._synth.Eq = eq; me._synth.LowPassHz = crossover; me._synth.Gain = _gain; me._synth.SetWeights(sources); }
    }

    private static int TactileBus()
    {
        int bus = AudioServer.GetBusIndex(BusName);
        if (bus < 0) { AudioServer.AddBus(); bus = AudioServer.BusCount - 1; AudioServer.SetBusName(bus, BusName); AudioServer.SetBusSend(bus, "Master"); }
        return bus;
    }

    /// <summary>The shaker signal is wanted: the LFE Level above 0.</summary>
    public static bool On => AudioOutputs.Tactile(_level);

    /// <summary>The output the shaker plays on now (true both channels, false the LFE of 5.1/7.1 with an LFE channel), or null for none.</summary>
    private static bool? Playing => !On ? null : !AudioOutputs.Discrete(Output) ? true : HasLfe ? false : null;
    private bool _stereoPair;

    private readonly TactileSynth _synth = new();
    private readonly TactileEvents _events = new();
    private readonly List<(int Channel, int Amount)> _hits = [];
    private AudioStreamPlayer? _out;
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _block = [];
    private Vector2[] _frames = [];

    /// <summary>The output has an LFE channel (3.1, 5.1 or 7.1).</summary>
    public static bool HasLfe => AudioServer.GetSpeakerMode() != AudioServer.SpeakerMode.ModeStereo;

    /// <summary>The Developer panel's line: the mode, the output, and the signal's peak over the last block.</summary>
    public static string Status
    {
        get
        {
            string speakers = AudioServer.GetSpeakerMode() switch
            {
                AudioServer.SpeakerMode.Surround31 => "3.1",
                AudioServer.SpeakerMode.Surround51 => "5.1",
                AudioServer.SpeakerMode.Surround71 => "7.1",
                _ => "stereo",
            };
            if (Playing is null) return $"bass shaker: {speakers} output, no LFE channel: silent";
            float peak = _instance?._out is null ? 0 : _instance._synth.Peak;
            string to = Playing == true ? $"both channels of {speakers}, under {_crossover:0} Hz" : $"LFE of {speakers}";
            return $"bass shaker: {to}{(_eq is null ? "" : ", EQ on")}, peak {(peak > 0 ? $"{20 * MathF.Log10(peak):0.0} dBFS" : "silent")}";
        }
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;     // it fills the stream under the pause too, with silence
        _instance = this;
    }

    public override void _ExitTree()
    {
        Close();
        if (_instance == this) _instance = null;
    }

    private void Open(bool stereoPair)
    {
        TactileBus();
        _stereoPair = stereoPair;
        _synth.Eq = _eq;
        _synth.LowPassHz = _crossover;
        _synth.Gain = _gain;
        _synth.SetWeights(_sources);
        float rate = AudioServer.GetMixRate();
        // 50 ms of queue: a 60 fps frame fills it with room to spare, and a hit is felt within about three frames
        var gen = new AudioStreamGenerator { MixRate = rate, BufferLength = 0.05f };
        // the centre target sends the frame's right half to the LFE at full level whatever VolumeDb says: the level is the synth's and the bus's
        _out = new AudioStreamPlayer { Name = "BassShaker", Stream = gen, Bus = BusName, ProcessMode = ProcessModeEnum.Always,
            MixTarget = stereoPair ? AudioStreamPlayer.MixTargetEnum.Stereo : AudioStreamPlayer.MixTargetEnum.Center };
        AddChild(_out);
        _out.Play();
        _playback = (AudioStreamGeneratorPlayback)_out.GetStreamPlayback();
    }

    private void Close()
    {
        _out?.QueueFree();
        _out = null;
        _playback = null;
        _hits.Clear();
        _synth.Reset();
    }

    public override void _Process(double delta)
    {
        bool? want = Playing;
        if (_out is not null && want != _stereoPair) Close();
        if (want is { } pair && _out is null) Open(pair);
        if (_playback is null) return;
        if (GetTree().Paused || Held?.Invoke() == true) { _synth.Reset(); _hits.Clear(); }
        int n = _playback.GetFramesAvailable();
        if (n <= 0) return;
        if (_block.Length < n) { _block = new float[n]; _frames = new Vector2[n]; }
        _synth.Fill(_block.AsSpan(0, n), (int)AudioServer.GetMixRate());
        for (int i = 0; i < n; i++) _frames[i] = new Vector2(_stereoPair ? _block[i] : 0, _block[i]);
        _playback.PushBuffer(n == _frames.Length ? _frames : _frames[..n]);
    }

    /// <summary>A damage packet reaching the player's car (DamageModel.Damaging): felt at the next tick.</summary>
    public static void Damaging(Vehicle v, DamagePacket p)
    {
        if (_instance is not { _out: not null } me || v != me.Player?.Invoke()) return;
        for (int ch = 0; ch < 4; ch++)
            if (p.Ch[ch] != 0) me._hits.Add((ch, p.Ch[ch]));
    }

    /// <summary>One sim tick of <paramref name="dt"/> seconds has run: the player's car, its shots among <paramref name="events"/> from <paramref name="from"/> on, the explosions, the hits queued since.</summary>
    public static void Tick(IReadOnlyList<CombatEvent>? events, int from, float dt)
    {
        if (_instance is not { _out: not null } me || me.GetTree().Paused || Held?.Invoke() == true) return;
        var e = me._events;
        e.Clear();
        e.Hits.AddRange(me._hits);
        me._hits.Clear();
        if (me.Player?.Invoke() is { } v)
        {
            var flags = v.Flags;
            e.EngineOn = (flags & VehicleFlags.EngineOn) != 0;
            e.Cranking = (flags & VehicleFlags.Started) != 0;
            e.Rpm = v.Engine.Rpm;
            // the car's own speed, not the speedometer's (which follows the engine): the road is felt with the engine off too
            e.Mph = MathF.Min(v.Vel.Length() * 2.2369363f, 165f);
            e.Surface = (flags & VehicleFlags.Airborne) != 0 ? 9 : v.Surface + 1;
            e.FlatTyres = (v.FrontRight.Blown ? 1 : 0) + (v.FrontLeft.Blown ? 1 : 0) + (v.RearRight.Blown ? 1 : 0) + (v.RearLeft.Blown ? 1 : 0);
            // each corner's travel over its limit (a negative rest reverses the limits: the span is the larger's size)
            int k = 0;
            foreach (var w in v.Wheels)
            {
                float span = MathF.Max(MathF.Abs(w.TravelMax), MathF.Abs(w.TravelMin));
                e.Travel[k++] = span > 1e-4f ? Math.Clamp(w.Travel / span, -1f, 1f) : 0f;
            }
            e.TickSeconds = dt;
            if (events is not null)
            {
                var at = v.Pos.ToVector3();
                for (int i = Math.Max(from, 0); i < events.Count; i++)
                    switch (events[i])
                    {
                        case ShotFired s when s.Shooter == v:
                            e.Shots.Add(new WeaponShot(0, s.Weapon.Def.Class, s.Weapon.Def.Tier, s.Weapon.Def.Rate));
                            break;
                        case Explosion x:
                            e.Blasts.Add((x.At - at).Length());
                            break;
                    }
            }
        }
        me._synth.Step(e);
    }
}
