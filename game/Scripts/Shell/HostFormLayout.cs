namespace I76.Game.Shell;

/// <summary>
/// Where things are on the base game's network host form (<see cref="MeleeScreen"/>, modes 3 and 4; the play fork's layout,
/// docs/multiplayer.md "The host form"): the original's art (record 19) with LOCAL DRIVER, AREA OF PLAY and its list, GAME NAME,
/// NO. DRIVERS, the car and the buttons where the original has them, and the port's settings in two places drawn in the form's
/// own paper and lines. The options box under NO. DRIVERS holds the game: BOTS, the switches as check boxes, MAX SCORE (on a
/// co-op form its Co-op Scaling, Co-op Damage and Rolling Start as labelled rows, then the switches it keeps). The rules table at
/// the top of the car panel, where the original drew the car's front view, holds the host's rules, a labelled row each: Sim
/// Rate, Unlimited Turrets, Ignition Lock, Handicaps, Password, Chase Crosshair. Pure layout, no engine: the hotspot ids are the
/// form's own (MeleeScreen's table), every one kept from the form before (and from upstream's drawn sheet, 56373bd).
/// </summary>
public static class HostFormLayout
{
    /// <summary>A hotspot: hit strictly inside (x0 &lt; x &lt; x1, y0 &lt; y &lt; y1).</summary>
    public readonly record struct Rect(int X0, int X1, int Y0, int Y1);

    /// <summary>
    /// The original's own hotspots the host form keeps (record 19's art): MAKE / MODEL's arrow 3, VARIANT's 4, CONFIGURE CHASSIS 5,
    /// BROADCAST GAME 6, the area list 11 and its arrows 12 and 13, LOCAL DRIVER's RENAME 14, CANCEL 15, GAME NAME's RENAME 22, and
    /// NO. DRIVERS' ⊟ ⊞ 8 and 9 (on the host form's NO. DRIVERS bar). The original's driver box (7) is left out: on a host form it
    /// only showed the host's own car again. 32 is the port's game type button at the AREA OF PLAY bar's right end.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, Rect> Art = new Dictionary<int, Rect>
    {
        [3] = new(610, 621, 400, 415), [4] = new(437, 449, 419, 434), [5] = new(456, 616, 422, 435), [6] = new(229, 369, 453, 471),
        [8] = new(150, 166, 338, 353), [9] = new(198, 213, 338, 353), [11] = new(31, 216, 150, 270), [12] = new(9, 27, 128, 144),
        [13] = new(9, 27, 250, 268), [14] = new(173, 213, 46, 57), [15] = new(489, 630, 453, 471), [22] = new(173, 213, 306, 317),
        [32] = new(173, 213, 78, 89),
    };

    // ---- the options box: from NO. DRIVERS' foot rule (y 355) to its own foot (BoxFoot), x 10..215 inside

    /// <summary>The box's inside, x0, x1, y0, y1 inclusive; its foot rule; its rows' baselines (five, 22 px apart; the first far enough under NO. DRIVERS' ⊞ that the pad steps down onto it).</summary>
    public static (int X0, int X1, int Y0, int Y1) BoxInside => (10, 215, 356, BoxFoot - 1);
    public const int BoxFoot = 473;
    public static readonly int[] BoxRow = [374, 396, 418, 440, 462];
    /// <summary>The check boxes' columns: the labels' x and the box's (TEAM PLAY's own, 11 x 11).</summary>
    public const int LabelA = 15, CheckA = 90, LabelB = 112, CheckB = 200;
    /// <summary>A box row's strip between two x: 14 px over its baseline to 5 under.</summary>
    public static Rect BoxStrip(int row, int x0, int x1) => new(x0, x1, BoxRow[row] - 14, BoxRow[row] + 5);
    /// <summary>A labelled row's blank in the box (the co-op rows): from here to the box's right.</summary>
    public const int BoxBlank0 = 118, BoxBlank1 = 212;

    // ---- the rules table: the car panel's top, under DRIVER:, six rows of 17 px

    public const int TableX0 = 222, TableBlank0 = 350, TableX1 = 452, TableTop = 78, TableRowH = 17, TableRows = 6;
    public static int TableBaseline(int k) => TableTop + k * TableRowH + 13;
    public static Rect TableStrip(int k) => new(TableX0, TableX1, TableTop + k * TableRowH, TableTop + (k + 1) * TableRowH);
    /// <summary>The table's foot: the arena sketch's room starts under it.</summary>
    public static int TableFoot => TableTop + TableRows * TableRowH;

    /// <summary>What the form's settings are, as they stand.</summary>
    public sealed record State
    {
        public bool Coop { get; init; }
        public bool TeamPlay { get; init; }
        public bool InviteOnly { get; init; }
        public bool ShareMaps { get; init; }
        public bool Hidden { get; init; }
        public bool CarReset { get; init; }
        public bool Cb { get; init; }
        public bool ScoreOn { get; init; }
        public int MaxScore { get; init; }
        public int Bots { get; init; }
        public string BotSkill { get; init; } = "";
        public int Rate { get; init; }
        public bool Turrets { get; init; }
        public bool IgnitionStaysOn { get; init; }
        public string Handicaps { get; init; } = "Off";
        public bool Password { get; init; }
        public bool Crosshair { get; init; }
        public string CoopScale { get; init; } = "Off";
        public string CoopDamage { get; init; } = "x1";
        public bool RollingStart { get; init; }
    }

    /// <summary>Where a setting is drawn: a row of the rules table, a labelled row of the box, or a check box in the box (column A or B).</summary>
    public enum Place { Table, BoxRow, CheckA, CheckB }

    /// <summary>One setting: where, its row, its label, its value (a check box's: "X" ticked, "" not), its hotspot id.</summary>
    public sealed record Field(Place Where, int Row, string Label, string Value, int Id);

    private static string OnOff(bool on) => on ? "On" : "Off";
    private static string Tick(bool on) => on ? "X" : "";

    /// <summary>
    /// The settings with a label of their own, by where they go. BOTS and MAX SCORE (melee) are the box's stepped lines, drawn
    /// with their own ⊟ ⊞ (<see cref="Hot"/> has their parts). A co-op game has no team play, score limit, bots or maps to share:
    /// those are left out, and the co-op rules take the box's first rows.
    /// </summary>
    public static List<Field> Fields(State s)
    {
        var l = new List<Field>();
        if (s.Coop)
        {
            l.Add(new(Place.BoxRow, 0, "CO-OP SCALING", s.CoopScale, 40));
            l.Add(new(Place.BoxRow, 1, "CO-OP DAMAGE", s.CoopDamage, 41));
            l.Add(new(Place.BoxRow, 2, "ROLLING START", OnOff(s.RollingStart), 42));
            l.Add(new(Place.CheckA, 3, "INVITE ONLY", Tick(s.InviteOnly), 17));
            l.Add(new(Place.CheckB, 3, "HIDDEN CARS", Tick(s.Hidden), 28));
            l.Add(new(Place.CheckA, 4, "CAR RESET", Tick(s.CarReset), 36));
            l.Add(new(Place.CheckB, 4, "CB RADIO", Tick(s.Cb), 37));
        }
        else
        {
            l.Add(new(Place.CheckA, 1, "TEAM PLAY", Tick(s.TeamPlay), 16));
            l.Add(new(Place.CheckB, 1, "INVITE ONLY", Tick(s.InviteOnly), 17));
            l.Add(new(Place.CheckA, 2, "SHARE MAPS", Tick(s.ShareMaps), 26));
            l.Add(new(Place.CheckB, 2, "HIDDEN CARS", Tick(s.Hidden), 28));
            l.Add(new(Place.CheckA, 3, "CAR RESET", Tick(s.CarReset), 36));
            l.Add(new(Place.CheckB, 3, "CB RADIO", Tick(s.Cb), 37));
        }
        int k = 0;
        void Rule(string label, string value, int id) => l.Add(new(Place.Table, k++, label, value, id));
        Rule("SIM RATE", $"{s.Rate} Hz", 33);
        Rule("UNLIMITED TURRETS", OnOff(s.Turrets), 35);
        Rule("IGNITION LOCK", OnOff(s.IgnitionStaysOn), 39);
        Rule("HANDICAPS", s.Handicaps, 43);
        Rule("PASSWORD", s.Password ? "Set" : "None", 27);
        Rule("CHASE CROSSHAIR", OnOff(s.Crosshair), 38);
        return l;
    }

    /// <summary>The melee box's stepped lines: BOTS (row 0: its ⊟ ⊞ at x 50 and 81, the skill, A.I. CARS) and MAX SCORE (row 4: its box, ⊟ at 112, ⊞ at 200).</summary>
    public const int BotsRow = 0, ScoreRow = 4;

    private static readonly Dictionary<int, Rect> MeleeHot = Build(false), CoopHot = Build(true);

    /// <summary>Every hotspot of the base host form by id, melee or co-op.</summary>
    public static IReadOnlyDictionary<int, Rect> Hot(bool coop) => coop ? CoopHot : MeleeHot;

    private static Dictionary<int, Rect> Build(bool coop)
    {
        var hot = new Dictionary<int, Rect>(Art);
        foreach (var f in Fields(new State { Coop = coop }))
            hot[f.Id] = f.Where switch
            {
                Place.Table => TableStrip(f.Row),
                Place.BoxRow => BoxStrip(f.Row, 12, 214),
                Place.CheckA => BoxStrip(f.Row, 12, 102),
                _ => BoxStrip(f.Row, 109, 212),
            };
        if (!coop)
        {
            // BOTS: ⊟ 29, ⊞ 30, the skill 31, A.I. CARS 34; MAX SCORE: its box 18, ⊟ 20, ⊞ 21
            hot[29] = BoxStrip(BotsRow, 46, 63); hot[30] = BoxStrip(BotsRow, 77, 94);
            hot[31] = BoxStrip(BotsRow, 94, 168); hot[34] = BoxStrip(BotsRow, 168, 214);
            hot[18] = BoxStrip(ScoreRow, 12, 102); hot[20] = BoxStrip(ScoreRow, 108, 126); hot[21] = BoxStrip(ScoreRow, 196, 213);
        }
        return hot;
    }
}
