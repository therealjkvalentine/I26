using Godot;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Game.Ui;

/// <summary>
/// The map (M) of a generated remix (docs/remix-missions.md): the base mission's own map art is a drawing of the stock mission
/// with its marks painted in, so a remix shows the port's pen sketch of the WHOLE map (ArenaSketch.Whole: every road, paved
/// solid, dirt dashed, a wash dotted) on a plain sheet, with the remix's marks (RemixMarks: the start, the template's places) and
/// one or two landmarks lettered beside them by MissionPicture. Built once per mission and look.
/// </summary>
public static class RemixMapPicture
{
    /// <summary>The sketch's box on the 640 x 480 sheet.</summary>
    private static readonly Rect2I Box = new(40, 56, 560, 392);
    /// <summary>The sheet's paper and the frame's ink.</summary>
    public static readonly Color Paper = Color.Color8(232, 222, 196), Ink = Color.Color8(40, 30, 18);
    private static readonly Dictionary<string, (Texture2D, List<(Vector2, string)>, string?, List<Vector2[]>, List<(Vector2, string)>, Func<int, int, bool>)?> Made = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The picture, its marks (where on the 640 x 480 sheet, and the label), its title, its routes (sheet points) and its landmarks,
    /// or null when the mission is no remix or has no terrain.
    /// </summary>
    public static (Texture2D Picture, List<(Vector2 At, string Label)> Marks, string? Title, List<Vector2[]> Lines, List<(Vector2 At, string Label)> Landmarks, Func<int, int, bool> Inked)? For(string? installRoot, string? mission, bool look1997)
    {
        if (installRoot is null || mission is null || RemixMarks.For(mission) is not { } marks) return null;
        // by the build's stamp too: RANDOM JOB rebuilds xjob for every roll, and a picture kept by the stem alone showed the first
        // job's map for every later one in the same run
        string key = $"{mission}:{look1997}:{RemixMarks.StampOf(mission)}";
        if (Made.TryGetValue(key, out var done)) return done;
        (Texture2D, List<(Vector2, string)>, string?, List<Vector2[]>, List<(Vector2, string)>, Func<int, int, bool>)? made = null;
        try
        {
            var install = new GameInstall(installRoot);
            if (install.FindMission(mission) is { } path)
            {
                var msn = new MissionFile(path);
                // the leave-map exits bound the whole map with its roads
                var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Points).Select(p => new System.Numerics.Vector2(p.X, p.Z)).ToList();
                if (I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) is { } ter
                    && ArenaSketch.Whole(msn, TerrainTiles.Load(msn, ter), [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], exits, Box.Size.X, Box.Size.Y) is { } sketch)
                {
                    int scale = look1997 ? 1 : 2;
                    var ink = sketch.Render(scale, aliased: look1997);
                    var img = Image.CreateEmpty(640 * scale, 480 * scale, false, Image.Format.Rgba8);
                    img.Fill(Paper);
                    int w = Box.Size.X * scale, h = Box.Size.Y * scale;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = (y * w + x) * 4;
                            float a = ink[i + 3] / 255f;
                            if (a <= 0) continue;
                            var under = img.GetPixel(Box.Position.X * scale + x, Box.Position.Y * scale + y);
                            img.SetPixel(Box.Position.X * scale + x, Box.Position.Y * scale + y, under.Lerp(Color.Color8(ink[i], ink[i + 1], ink[i + 2]), a));
                        }
                    var at = sketch.StartsInBox.Select((p, k) => (new Vector2(Box.Position.X + p.X * Box.Size.X, Box.Position.Y + p.Y * Box.Size.Y), marks.Points[k].Label)).ToList();
                    Vector2 Sheet(System.Numerics.Vector2 wpt) { var u = sketch.InBox(wpt); return new(Box.Position.X + u.X * Box.Size.X, Box.Position.Y + u.Y * Box.Size.Y); }
                    var lines = marks.Lines.Select(l => l.Points.Select(Sheet).ToArray()).ToList();
                    var land = marks.Landmarks.Select(l => (Sheet(l.At), l.Label)).ToList();
                    // where the sketch inks the sheet (sheet pixels), so the marks' words keep off the roads and the canyon walls where they can
                    var sk2 = sketch;
                    bool Inked(int x, int y) => sk2.Inked(x - Box.Position.X, y - Box.Position.Y);
                    made = (ImageTexture.CreateFromImage(img), at, WorldSettings.ArenaName(msn)?.Trim().ToUpperInvariant(), lines, land, Inked);
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { GD.PrintErr($"remix map: {mission}: {e.Message}"); }
        return Made[key] = made;
    }
}
