using Godot;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Game.Ui;

/// <summary>
/// A generated job's loading card (docs/remix-missions.md "Loading"): a stock mission shows its briefing while it loads (the
/// briefing movie over the card); a generated job has no movie, so its card is its own briefing: the job's map (the whole map,
/// its marks and landmarks, RemixMapPicture as the M key shows it) on the left two thirds, and its notepad's text (the objectives
/// a player can see at the start and the notes: where the places are, the job's number) on a paper column to the right, in the
/// notepad's hand (Lee). PLEASE STAND BY and the load's line stay in their corner over it (LoadingScreen).
/// </summary>
public partial class RemixLoadingCard : Control
{
    public required GameInstall Install { get; init; }
    public required string Mission { get; init; }
    public bool Faithful { get; init; }

    private Font? _lee;
    private readonly List<string> _lines = [];
    private string? _title;

    /// <summary>Whether a generated job's card can be made (its map and its text are there).</summary>
    public static bool Available(GameInstall install, string? mission) =>
        mission is { Length: > 0 } && RemixMarks.IsRemix(mission) && RemixMarks.For(mission) is not null && install.Root is not null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var f = new FontFile { Antialiasing = Faithful ? TextServer.FontAntialiasing.None : TextServer.FontAntialiasing.Gray };
        if (f.LoadDynamicFont(Install.FindFile("Lee_____.ttf") ?? Path.Combine(Install.Root!, "Lee_____.ttf")) == Error.Ok) _lee = f;
        // the map: the whole map with the job's marks, as the M key shows it, in the left two thirds
        if (RemixMapPicture.For(Install.Root, Mission, Faithful) is { } pic)
        {
            _title = pic.Title;
            var box = new Control { AnchorRight = 0.68f, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(box);
            box.AddChild(new MissionPicture
            {
                What = MissionPicture.Kind.Map, Picture = pic.Picture, Lee = _lee, Marks = pic.Marks, Title = pic.Title, Routes = pic.Lines, Inked = pic.Inked,
                Landmarks = pic.Landmarks, Passive = true, Closed = _ => { },
            });
        }
        // the notepad's text: the objectives shown at the start, then the notes
        if (GameInstall.InUserMissionDir(Mission + ".npt") is { } npt)
            try
            {
                var text = ObjectiveText.Parse(File.ReadAllText(npt));
                foreach (var (line, hidden) in text.Objectives) if (!hidden) _lines.Add(RemixBuilder.Plain(line) is var p && p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p);
            }
            catch (IOException) { }
        _lines.Add("");
        _lines.AddRange(RemixMarks.For(Mission)?.Notes ?? []);
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        // the column beside the map, on the 640 x 480 frame kept 4:3 over black (the map's picture is centred in its own box)
        float s = Mathf.Min(Size.X / 640f, Size.Y / 480f);
        var o = (Size - new Vector2(640, 480) * s) / 2;
        float x0 = Size.X * 0.68f + 6 * s, x1 = Mathf.Min(Size.X - 8 * s, o.X + 632 * s);
        var paper = RemixMapPicture.Paper;
        DrawRect(new Rect2(x0, o.Y + 56 * s, x1 - x0, 392 * s), paper);
        if (_lee is null) return;
        var ink = RemixMapPicture.Ink;
        int head = Mathf.Max(1, Mathf.RoundToInt(16 * s)), size = Mathf.Max(1, Mathf.RoundToInt(12 * s));
        float y = o.Y + 80 * s, w = x1 - x0 - 16 * s;
        DrawString(_lee, new Vector2(x0 + 8 * s, y), "THE JOB", HorizontalAlignment.Left, -1, head, ink);
        y += 22 * s;
        foreach (var line in _lines)
        {
            // (a line is drawn on its baseline: the rule goes under the last line, the next line a full line below it)
            if (line.Length == 0) { DrawLine(new Vector2(x0 + 8 * s, y - 6 * s), new Vector2(x0 + 70 * s, y - 6 * s), ink, Mathf.Max(1f, s)); y += 10 * s; continue; }
            foreach (var part in Wrap(line, w, size))
            {
                if (y > o.Y + 440 * s) return;
                DrawString(_lee, new Vector2(x0 + 8 * s, y), part, HorizontalAlignment.Left, -1, size, ink);
                y += 14 * s;
            }
            y += 4 * s;
        }
    }

    private List<string> Wrap(string text, float width, int size)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && _lee!.GetStringSize(next, HorizontalAlignment.Left, -1, size).X > width) { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
}
