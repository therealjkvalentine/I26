using I76.Sim.Terrain;

namespace I76.Sim.Vehicles;

/// <summary>What a wheel stands on, as the bass shaker's crossing knock tells it apart (<see cref="WheelCrossings"/>).</summary>
public enum GroundKind { None, OffRoad, DirtRoad, Paved, Bridge }

/// <summary>
/// The port's wheel crossings, for the bass shaker's knock (the original has none): each corner wheel's ground, from the
/// surface id under it (asphalt and parking lot paved; dirt road, planks and dirt junction a dirt road; dirt, sand and rough
/// ground off-road) or a structure's drive-on surface over the heightfield (a bridge, a ramp), and the changes from one tick
/// to the next. Presentation only: the shaker owns one, nothing of it is sim state, and it draws no random numbers.
/// </summary>
public sealed class WheelCrossings
{
    /// <summary>A structure surface this far over the bare heightfield is a deck under the wheel, not the ground.</summary>
    public const float DeckRise = 0.1f;
    /// <summary>Under this speed (m/s) a change is taken without a knock: creeping over a lip is not felt as one.</summary>
    public const float MinSpeed = 2f;
    /// <summary>A wheel knocks at most once in this long (s): a wheel running along a road's edge flickers between its cells.</summary>
    public const float HoldOff = 0.25f;

    /// <summary>The car runs along a paved road's edge this long (s) before its shoulder plays: a crossing at an angle straddles it for a moment only.</summary>
    public const float ShoulderAfter = 0.3f;

    private readonly GroundKind[] _kind = new GroundKind[4];
    private readonly float[] _quiet = new float[4];
    private float _straddled;

    /// <summary>
    /// The car runs along a paved road's edge: one side's two wheels on asphalt, the other side's two off it (the ground or a
    /// dirt road), for <see cref="ShoulderAfter"/> or more. The heightfield's surface cells are 5 m, so the edge is the cells'
    /// edge, not the road's drawn one; the game's roads have no shoulder of their own.
    /// </summary>
    public bool Shoulder => _straddled >= ShoulderAfter;

    /// <summary>The four corner wheels' ground at the last <see cref="Step"/> (front right, front left, rear right, rear left).</summary>
    public IReadOnlyList<GroundKind> Kinds => _kind;

    /// <summary>The ground kind of a surface id (the heightfield's, 0 to 7).</summary>
    public static GroundKind OfSurface(int id) => (id & 7) switch
    {
        2 or 6 => GroundKind.Paved,
        3 or 4 or 7 => GroundKind.DirtRoad,
        _ => GroundKind.OffRoad,
    };

    /// <summary>What is under (x, z) at height y: a structure's deck over the bare ground first, else the surface id's kind.</summary>
    public static GroundKind At(IGround g, double x, double y, double z)
    {
        float deck = g.Query(x, y + 0.5, z).Height;
        if (deck > g.BareHeight(x, z) + DeckRise) return GroundKind.Bridge;
        return OfSurface(g.SurfaceId(x, z));
    }

    /// <summary>
    /// A crossing's strength for the knock, 0 to 1: onto or off a bridge or asphalt from the ground 1 (a deck's joint, a
    /// pavement's lip), a dirt road from the ground 0.5 (a graded edge), asphalt and a dirt road 0.35 (a lip on one side).
    /// </summary>
    public static float Strength(GroundKind from, GroundKind to)
    {
        if (from == to || from == GroundKind.None || to == GroundKind.None) return 0f;
        if (from == GroundKind.Bridge || to == GroundKind.Bridge) return 1f;
        var (a, b) = from < to ? (from, to) : (to, from);
        return (a, b) switch
        {
            (GroundKind.OffRoad, GroundKind.Paved) => 1f,
            (GroundKind.OffRoad, GroundKind.DirtRoad) => 0.5f,
            _ => 0.35f,
        };
    }

    /// <summary>
    /// One tick of <paramref name="dt"/> seconds: each wheel's ground, and the strength of each crossing into
    /// <paramref name="into"/>. In the air nothing changes (the landing is the springs' jolt, not a crossing); the first
    /// look at a wheel, or one after <see cref="Reset"/>, knocks nothing.
    /// </summary>
    public void Step(IGround g, Vehicle v, float dt, List<float> into)
    {
        if ((v.Flags & VehicleFlags.Airborne) != 0) { _straddled = 0; return; }
        float speed = v.Vel.Length();
        int i = 0;
        foreach (var w in v.Wheels)
        {
            var off = v.Rot.ToWorld(w.Mount);
            var kind = At(g, v.Pos.X + off.X, v.Pos.Y + off.Y, v.Pos.Z + off.Z);
            _quiet[i] = MathF.Max(_quiet[i] - dt, 0f);
            if (kind != _kind[i])
            {
                float s = Strength(_kind[i], kind);
                if (s > 0 && speed >= MinSpeed && _quiet[i] <= 0) { into.Add(s); _quiet[i] = HoldOff; }
                _kind[i] = kind;
            }
            i++;
        }
        // front right, front left, rear right, rear left: the right pair against the left pair
        bool Paved(int k) => _kind[k] == GroundKind.Paved;
        bool Off(int k) => _kind[k] is GroundKind.OffRoad or GroundKind.DirtRoad;
        bool straddle = (Paved(0) && Paved(2) && Off(1) && Off(3)) || (Paved(1) && Paved(3) && Off(0) && Off(2));
        _straddled = straddle && speed >= MinSpeed ? _straddled + dt : 0f;
    }

    /// <summary>Forget the wheels (a new car, a teleport, the pause): the next look knocks nothing.</summary>
    public void Reset()
    {
        Array.Clear(_kind);
        Array.Clear(_quiet);
        _straddled = 0;
    }
}
