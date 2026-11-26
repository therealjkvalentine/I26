using Godot;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Game.Ui;

/// <summary>
/// The map (M) of a generated remix (docs/remix-missions.md): the base mission's own map art is a drawing of the stock mission
/// with its marks painted in, so a remix shows the port's pen sketch of the ground round its places (ArenaSketch.Around) on a
/// plain sheet, with the remix's marks (RemixMarks: the start, the template's places) lettered beside them by MissionPicture.
/// Built once per mission and look.
/// </summary>
public static class RemixMapPicture
{
    /// <summary>The sketch's box on the 640 x 480 sheet.</summary>
    private static readonly Rect2I Box = new(40, 56, 560, 392);
    /// <summary>The sheet's paper and the frame's ink.</summary>
    public static readonly Color Paper = Color.Color8(232, 222, 196), Ink = Color.Color8(40, 30, 18);
    private static readonly Dictionary<string, (Texture2D, List<(Vector2, string)>, string?)?> Made = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The picture, its marks (where on the 640 x 480 sheet, and the label) and its title, or null when the mission is no remix or has no terrain.</summary>
    public static (Texture2D Picture, List<(Vector2 At, string Label)> Marks, string? Title)? For(string? installRoot, string? mission, bool look1997)
    {
        if (installRoot is null || mission is null || RemixMarks.For(mission) is not { } marks) return null;
        string key = $"{mission}:{look1997}";
        if (Made.TryGetValue(key, out var done)) return done;
        (Texture2D, List<(Vector2, string)>, string?)? made = null;
        try
        {
            var install = new GameInstall(installRoot);
            if (install.FindMission(mission) is { } path)
            {
                var msn = new MissionFile(path);
                if (I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) is { } ter
                    && ArenaSketch.Around(msn, TerrainTiles.Load(msn, ter), [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], Box.Size.X, Box.Size.Y) is { } sketch)
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
                    made = (ImageTexture.CreateFromImage(img), at, WorldSettings.ArenaName(msn)?.Trim().ToUpperInvariant());
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { GD.PrintErr($"remix map: {mission}: {e.Message}"); }
        return Made[key] = made;
    }
}
