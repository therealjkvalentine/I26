using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// The modern look's mood prototypes (a comps lane, not a setting): switchable changes to its sky, light, shadow colour, ground
/// saturation and grade, to compare against the 1997 look. Nothing changes unless I76_MOOD (or an I76_SHOT_SEQ step) asks.
///
/// I76_MOOD="key=value&amp;key=value":
///   cal=off|card|filmic   the lighting calibration for the run (as [port] lighting_calibration), solved again live
///   sky=tex|model         the sky in the texture's own colours (the calibrated sky's mapping) or the modern model's (its default under off/filmic)
///   skytex=PATH           the sky's cloud texture replaced by that image (an upscale of the original made outside the game), its colours measured again
///   amb=R,G,B|zenith|grey the ambient light's colour (sRGB) instead of the sky's radiance (ambsky= how much of the radiance stays, 0 by default with amb)
///   ambe=X                the ambient energy times X (no effect while the sky's contribution is 1: Godot ignores it then)
///   ambfix=X              the sky's light (radiance) scaled by X times the ambient energy the code asks for; ambr=X by X
///   ambtint=zenith|R,G,B  the sky's light tinted toward that colour at its own luminance (ambtintk= how far, 1)
///   sun=R,G,B|cloud|white the sun's colour (sRGB); cloud: the sky texture's lit-cloud colour at the default's luminance; sune=X its energy times X
///   sat=X                 the map's ground and the distant ring at X times their saturation (about their luminance)
///   exp=X                 the tonemap exposure times X
///   lut=PATH              a 3D colour LUT (PNG strip N²×N: x = r + b·N, y = g) as the colour correction (display values in, display values out)
///   radchroma=X           how much colour the sky's light on the scene keeps (sky_modern radiance_chroma)
///
/// I76_SHOT_SEQ="name|cam|mood;name|cam|mood;...", with --screenshot OUT.png: from frame I76_SHOT_SEQ_START (90) each step holds
/// I76_SHOT_SEQ_HOLD (45) frames and its last is saved as OUT_name.png; then the game quits. cam: free:x,y,z,yaw,pitch[,fov]
/// (I76_FREECAM), car:dx,dy,dz,yaw,pitch (I76_CAP_CAM), or empty (as the step before). mood: as I76_MOOD, on top of it.
/// </summary>
public partial class Main
{
    private static readonly string MoodBase = System.Environment.GetEnvironmentVariable("I76_MOOD") ?? "";
    private Dictionary<string, string> _mood = ParseMood(MoodBase);
    private bool _moodSaved;
    private Color _moodSun0;
    private Color _moodAmbColour0;
    private float _moodAmbSky0 = 1f;
    private Variant _moodClouds0, _moodSkyLum0, _moodCloudLum0;
    /// <summary>The mood's cal= (I76_MOOD, then each I76_SHOT_SEQ step) over [port] lighting_calibration, in LightingOf.</summary>
    private static Lighting? _moodCal = CalOf(ParseMood(MoodBase));

    private static Lighting? CalOf(Dictionary<string, string> m) => m.TryGetValue("cal", out var c) ? c switch { "off" => Lighting.Off, "card" => Lighting.Card, "filmic" => Lighting.Filmic, _ => null } : null;

    public static Dictionary<string, string> ParseMood(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in s.Split('&', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            if (kv.Split('=', 2) is [var k, var v]) d[k.ToLowerInvariant()] = v;
        return d;
    }

    private static float MoodF(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private float MoodNum(string key, float fallback) => _mood.TryGetValue(key, out var v) ? MoodF(v) : fallback;

    private static Color? MoodRgb(string s) => s.Split(',') is [var r, var g, var b] ? new Color(MoodF(r), MoodF(g), MoodF(b)) : null;

    public static float Luma(Color c) { var l = c.SrgbToLinear(); return 0.2126f * l.R + 0.7152f * l.G + 0.0722f * l.B; }

    /// <summary>A colour (sRGB) with the linear luminance of <paramref name="like"/>.</summary>
    public static Color AtLumaOf(Color c, Color like)
    {
        float k = Luma(like) / Mathf.Max(Luma(c), 1e-5f);
        var l = c.SrgbToLinear();
        return new Color(l.R * k, l.G * k, l.B * k).LinearToSrgb();
    }

    /// <summary>The mood's calibration choice before the solve (ApplySkySteps reads the settings; the mood may override them).</summary>
    private void MoodLightingChoice()
    {
        _moodCal = CalOf(_mood);
        var want = LightingOf(I76.Game.Shell.ShellSettings.Load());
        if (want != _lighting) { _lighting = want; SolveLightingCalibration(); }
    }

    private readonly Dictionary<string, ImageTexture> _moodSkyTex = [];
    private readonly Dictionary<string, ImageTexture3D> _moodLuts = [];

    /// <summary>The mood on top of ApplyLightingCalibration (called at its end): each key from the defaults it saved the first time.</summary>
    private void MoodApply()
    {
        if (_mode1997 || _modernEnv is null || _calibratedSky is null || _sun is null) return;
        if (!_moodSaved)
        {
            _moodSaved = true;
            _moodSun0 = _sun.LightColor;
            _moodAmbColour0 = _modernEnv.AmbientLightColor;
            _moodAmbSky0 = _modernEnv.AmbientLightSkyContribution;
            _moodClouds0 = _calibratedSky.GetShaderParameter("clouds");
            _moodSkyLum0 = _calibratedSky.GetShaderParameter("sky_lum");
            _moodCloudLum0 = _calibratedSky.GetShaderParameter("cloud_lum");
        }
        var skies = new[] { _calibratedSky, _skyFog?.MaterialOverride as ShaderMaterial };
        // the sky's texture and its measured colours
        Texture2D clouds = _moodClouds0.As<Texture2D>();
        var pal = _builder?.Palette;
        Color average = _builder?.SkyAverage ?? Colors.Gray;
        if (_mood.TryGetValue("skytex", out var path) && MoodSkyTexture(path) is { } st)
        {
            clouds = st;
            var img = st.GetImage();
            pal = I76.Game.World.MissionBuilder.MeasureSkyOf(img);
            var one = (Image)img.Duplicate();
            one.Resize(1, 1, Image.Interpolation.Lanczos);
            average = one.GetPixel(0, 0);
        }
        bool skyTex = _mood.TryGetValue("sky", out var skyWord) ? skyWord == "tex" : _lighting == Lighting.Card;
        bool day = _calibratedLight is { IsDay: true };
        if (pal is not null && (_mood.ContainsKey("skytex") || _mood.ContainsKey("sky")))
        {
            var sc = skyTex
                ? new SkyColours(pal.Sky, average, pal.Cloud, pal.Shade, 1f, 1f, true)
                : day ? new SkyColours(pal.Sky.Darkened(0.12f), pal.Sky.Lerp(average, 0.6f).Lerp(Colors.White, 0.25f), pal.Cloud.Lerp(Colors.White, 0.4f), pal.Shade.Lerp(pal.Sky, 0.35f).Darkened(0.1f), 1f, 1f, false)
                : new SkyColours(pal.Sky, pal.Sky.Lerp(average, 0.5f), pal.Cloud, pal.Shade.Lerp(pal.Cloud, 0.5f), 1f, 1f, false);
            foreach (var m in skies)
            {
                if (m is null) continue;
                m.SetShaderParameter("zenith_color", sc.Zenith);
                m.SetShaderParameter("horizon_color", sc.Horizon);
                m.SetShaderParameter("cloud_color", sc.Cloud);
                m.SetShaderParameter("cloud_shade", sc.Shade);
                m.SetShaderParameter("texture_colours", sc.Texture);
            }
            if (_horizon?.Material is not null) _horizon.Set("horizon_color", sc.Horizon);
        }
        foreach (var m in skies)
        {
            if (m is null) continue;
            m.SetShaderParameter("clouds", clouds);
            m.SetShaderParameter("sky_lum", pal is not null && _mood.ContainsKey("skytex") ? pal.SkyLum : _moodSkyLum0);
            m.SetShaderParameter("cloud_lum", pal is not null && _mood.ContainsKey("skytex") ? pal.CloudLum : _moodCloudLum0);
            if (_mood.TryGetValue("radchroma", out var rc)) m.SetShaderParameter("radiance_chroma", MoodF(rc));
        }
        // the ambient: a colour in place of (or beside) the sky's radiance
        if (_mood.TryGetValue("amb", out var amb))
        {
            var p = pal ?? _builder?.Palette;
            Color c = amb switch
            {
                "zenith" when p is not null => p.Sky,
                "grey" => new Color(0.5f, 0.5f, 0.5f),
                _ => MoodRgb(amb) ?? Colors.White,
            };
            _modernEnv.AmbientLightColor = c;
            _modernEnv.AmbientLightSkyContribution = MoodNum("ambsky", 0f);
        }
        else { _modernEnv.AmbientLightColor = _moodAmbColour0; _modernEnv.AmbientLightSkyContribution = _moodAmbSky0; }
        _modernEnv.AmbientLightEnergy *= MoodNum("ambe", 1f);
        // the sky's light on the scene: Godot ignores AmbientLightEnergy while the sky's contribution is 1 (Environment docs), so
        // the energy the calibration asks for never reached the scene; ambfix=X scales the radiance itself by X times that energy,
        // ambr=X by X; ambtint=zenith tints it toward the texture's open sky (luminance kept)
        float rg = _mood.TryGetValue("ambfix", out var af) ? MoodF(af) * _modernEnv.AmbientLightEnergy : MoodNum("ambr", 1f);
        var tint = new Vector3(1, 1, 1);
        if (_mood.TryGetValue("ambtint", out var at) && (at == "zenith" ? (pal ?? _builder?.Palette)?.Sky : MoodRgb(at)) is { } tc)
        {
            var l = tc.SrgbToLinear(); float y = 0.2126f * l.R + 0.7152f * l.G + 0.0722f * l.B;
            tint = new Vector3(l.R, l.G, l.B) / Mathf.Max(y, 1e-4f);
            tint = new Vector3(1, 1, 1).Lerp(tint, MoodNum("ambtintk", 1f));
        }
        foreach (var m in skies) { m?.SetShaderParameter("radiance_gain", rg); m?.SetShaderParameter("radiance_tint", tint); }
        // the sun
        var sun = _moodSun0;
        if (_mood.TryGetValue("sun", out var sw))
            sun = sw switch
            {
                "cloud" when (pal ?? _builder?.Palette) is { } cp => AtLumaOf(cp.Cloud, _moodSun0),
                "white" => AtLumaOf(Colors.White, _moodSun0),
                _ => MoodRgb(sw) ?? _moodSun0,
            };
        _sun.LightColor = sun;
        _sun.LightEnergy *= MoodNum("sune", 1f);
        RenderingServer.GlobalShaderParameterSet("i76_mood_sat", MoodNum("sat", 1f));
        _modernEnv.TonemapExposure *= MoodNum("exp", 1f);
        if (_mood.TryGetValue("lut", out var lut) && MoodLut(lut) is { } t3)
        {
            _modernEnv.AdjustmentEnabled = true;
            _modernEnv.AdjustmentColorCorrection = t3;
        }
        if (_mood.Count > 0) GD.Print($"mood: {string.Join(" ", _mood)} (lighting {_lighting}, exposure {_modernEnv.TonemapExposure:0.000}, sun {_sun.LightColor} x{_sun.LightEnergy:0.00}, ambient {_modernEnv.AmbientLightEnergy:0.00} sky {_modernEnv.AmbientLightSkyContribution:0.00})");
    }

    private ImageTexture? MoodSkyTexture(string path)
    {
        if (_moodSkyTex.TryGetValue(path, out var t)) return t;
        var img = Image.LoadFromFile(path);
        if (img is null || img.IsEmpty()) { GD.PrintErr($"mood: no sky texture {path}"); return null; }
        img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps();
        return _moodSkyTex[path] = ImageTexture.CreateFromImage(img);
    }

    private ImageTexture3D? MoodLut(string path)
    {
        if (_moodLuts.TryGetValue(path, out var t)) return t;
        var img = Image.LoadFromFile(path);
        if (img is null || img.IsEmpty()) { GD.PrintErr($"mood: no LUT {path}"); return null; }
        img.Convert(Image.Format.Rgb8);
        int n = img.GetHeight();
        if (img.GetWidth() != n * n) { GD.PrintErr($"mood: LUT {path} is not N²×N"); return null; }
        var slices = new Godot.Collections.Array<Image>();
        for (int b = 0; b < n; b++) slices.Add(img.GetRegion(new Rect2I(b * n, 0, n, n)));
        var tex = new ImageTexture3D();
        tex.Create(Image.Format.Rgb8, n, n, n, false, slices);
        return _moodLuts[path] = tex;
    }

    /// <summary>The HUD, the dash panels and the frame counter out of the sequence's shots: every canvas layer but the grade (29) and the crash blur (30).</summary>
    private static void HideOverlays(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is CanvasLayer cl && cl.Layer != 29 && cl.Layer != 30) cl.Visible = false;
            else if (c is CanvasItem ci && n is Window && ci.Visible) ci.Visible = false;
            HideOverlays(c);
        }
    }

    // ---- the capture sequence ----
    private static readonly string[]? ShotSeq = System.Environment.GetEnvironmentVariable("I76_SHOT_SEQ")?.Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
    private static readonly int ShotSeqStart = int.TryParse(System.Environment.GetEnvironmentVariable("I76_SHOT_SEQ_START"), out int s0) ? s0 : 90;
    private static readonly int ShotSeqHold = int.TryParse(System.Environment.GetEnvironmentVariable("I76_SHOT_SEQ_HOLD"), out int h0) ? h0 : 45;

    /// <summary>I76_SHOT_SEQ: each frame, the step's camera and mood at its start, its shot at its end.</summary>
    private void ShotSeqFrame()
    {
        if (ShotSeq is null || _opt.Screenshot is null || _frames < ShotSeqStart) return;
        int k = (_frames - ShotSeqStart) / ShotSeqHold, at = (_frames - ShotSeqStart) % ShotSeqHold;
        if (k >= ShotSeq.Length) { GetTree().Quit(); return; }
        if (System.Environment.GetEnvironmentVariable("I76_SHOT_SEQ_HUD") != "1") HideOverlays(GetTree().Root);
        var parts = ShotSeq[k].Split('|');
        string name = parts[0];
        if (at == 0)
        {
            string cam = parts.Length > 1 ? parts[1] : "";
            if (cam.StartsWith("free:")) { System.Environment.SetEnvironmentVariable("I76_FREECAM", cam[5..]); System.Environment.SetEnvironmentVariable("I76_CAP_CAM", null); }
            else if (cam.StartsWith("car:")) { System.Environment.SetEnvironmentVariable("I76_CAP_CAM", cam[4..]); System.Environment.SetEnvironmentVariable("I76_FREECAM", null); }
            _mood = ParseMood(MoodBase + "&" + (parts.Length > 2 ? parts[2] : ""));
            MoodLightingChoice();
            ApplyLightingCalibration();
            GD.Print($"shot seq: step {k} {name} cam {cam}");
        }
        else if (at == ShotSeqHold - 1)
        {
            var path = _opt.Screenshot.Replace(".png", $"_{name}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"shot seq: saved {path}");
        }
    }
}
