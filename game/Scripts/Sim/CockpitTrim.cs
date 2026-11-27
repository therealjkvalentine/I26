using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's cockpit trim ([port] cockpit_trim, off by default; experimental comps for the owner): the cockpit model's flat
/// untextured faces of the roof part (xx5?BDYT: the headliner, the windscreen header, the A pillars and the roof rails, one part
/// with no texture coordinates) and of the side part (xx5?BDYM: the door and side panels) drawn with a procedural vinyl
/// (cockpit_trim.gdshaderinc) projected in the mesh's own frame, in metres, so it stays put as the head moves. The faces are told
/// apart by their facing (the normal toward the cabin, Godot axes, y up): a roof face looks down, a pillar or rail face is slanted or
/// upright, a door face looks sideways into the cabin. The side part's other flat faces (the dash's face, the floor, the shelf) and
/// every textured face keep their own material. The 1997 look draws every face as before.
/// </summary>
public static class CockpitTrim
{
    /// <summary>The trim's styles: Off draws the original's flat colour (no material made); the others are the comps.</summary>
    public enum Style
    {
        Off = 0,
        /// <summary>B: perforated black vinyl headliner with transverse seams (bows), grained vinyl pillars, grained doors with two pleats.</summary>
        Perforated = 1,
        /// <summary>C: as B in a dark charcoal with a touch more sheen.</summary>
        Charcoal = 2,
        /// <summary>D: basket-weave vinyl headliner with seams, grained pillars and doors.</summary>
        Basket = 3,
        /// <summary>E: a muted colour-keyed headliner (perforated), black grained pillars and doors.</summary>
        Accent = 4,
        /// <summary>F: the lane's pick: a fine perforated headliner with soft bows, moulded satin pillars with a faint grain, doors with one pleat.</summary>
        Moulded = 5,
    }

    /// <summary>The words of [port] cockpit_trim, in Style order.</summary>
    public static readonly string[] Words = ["off", "perforated", "charcoal", "basket", "accent", "moulded"];

    /// <summary>[port] cockpit_trim: a style's word (any case); anything else, or none, Off.</summary>
    public static Style Parse(string? word)
    {
        var w = word?.Trim().Trim('"') ?? "";
        for (int i = 0; i < Words.Length; i++)
            if (string.Equals(w, Words[i], StringComparison.OrdinalIgnoreCase)) return (Style)i;
        return Style.Off;
    }

    public static string Word(Style s) => (int)s >= 0 && (int)s < Words.Length ? Words[(int)s] : Words[0];

    /// <summary>I76_COCKPIT_TRIM: a style's word in place of the setting for the run (a review sheet); null when unset.</summary>
    public static Style? Hook() => System.Environment.GetEnvironmentVariable("I76_COCKPIT_TRIM") is { Length: > 0 } h ? Parse(h) : null;

    /// <summary>I76_TRIM_KNOBS=relief,roughness,specular,albedo: the shader's tuning for the run (a negative roughness or specular keeps the style's); null when unset or unreadable.</summary>
    public static Vector4? Knobs()
    {
        var s = System.Environment.GetEnvironmentVariable("I76_TRIM_KNOBS")?.Split(',');
        if (s is not { Length: 4 }) return null;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(s[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return null;
        return new Vector4(v[0], v[1], v[2], v[3]);
    }

    /// <summary>Which trim a face is.</summary>
    public enum Part { None = 0, Roof = 1, Pillar = 2, Door = 3 }

    /// <summary>A roof face looks at least this far down (the normal's y; the headliner and its rear slope: -0.86 and steeper on the Piranha, its header and A pillar faces -0.79 and less).</summary>
    public const float RoofDown = -0.82f;

    /// <summary>A door face looks at least this far sideways (|x| of the normal): the door's panels and its top lip; the dash's face looks back, the floor up.</summary>
    public const float DoorSide = 0.4f;

    /// <summary>
    /// Whether a flat face's colour is the cockpit's trim grey (a neutral grey no lighter than 96: the 54 and 56 of every car's roof and
    /// side parts, the 80 of the Piranha's left pillar faces): the helicopters' light grey and yellow panels and the near-black keep theirs.
    /// </summary>
    public static bool TrimGrey(byte r, byte g, byte b) => r == g && g == b && r is >= 32 and <= 96;

    /// <summary>The part's zone from its node name (xx5?BDYT, xx5?BDYM: four characters from the fifth), the cockpit's detail-5 parts only.</summary>
    public static string? Zone(string nodeName) =>
        nodeName.Length >= 8 && nodeName[2] == '5' ? nodeName.Substring(4, 4).ToUpperInvariant() is var z && z is "BDYT" or "BDYM" ? z : null : null;

    /// <summary>
    /// A flat face's trim from its part's node name and its unit normal toward the cabin (Godot axes, y up, the car's frame): the roof
    /// part's faces looking down the headliner, its others the pillars, header and rails; the side part's faces looking sideways the
    /// door; anything else none (left as it is).
    /// </summary>
    public static Part Classify(string nodeName, Vector3 normal) => Zone(nodeName) switch
    {
        "BDYT" => normal.Y <= RoofDown ? Part.Roof : Part.Pillar,
        "BDYM" => MathF.Abs(normal.X) >= DoorSide ? Part.Door : Part.None,
        _ => Part.None,
    };
}
