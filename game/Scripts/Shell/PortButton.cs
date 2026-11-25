using I76.Formats.Shell;
using Godot;

namespace I76.Game.Shell;

/// <summary>
/// A main-menu button of the port's own, with no sprite in the original's bank (TRIP's SCENARIOS and NITRO PACK, the Nitro visit's
/// BACK): its label set in Impact as the menu's sprites are (white lit, grey dim, slanted, on a black box), at the window's
/// resolution on the Impact layer in the modern look (HD menu pack or not), cut into the frame's pixels in the 1997 look (as the
/// MAP EDITOR entry is), and in the shell's own face where there is no Impact at all. Its banner, while lit, is lettered the same way.
/// </summary>
public sealed class PortButton(int id, Rect2I box, ImpactLabels.Spec label, Rect2I bannerBox, string banner, Color dim)
{
    public int Id { get; } = id;
    public Rect2I Box { get; } = box;
    public ImpactLabels.Spec Label { get; } = label;
    public Rect2I BannerBox { get; } = bannerBox;
    public string Banner { get; } = banner;
    public bool Lit { get; set; }
    public bool Selected { get; set; }

    /// <summary>A banner's lettering: the base banners' size and tracking (sprite 41's fit), centred in its box.</summary>
    public static ImpactLabels.Spec BannerSpec(string words) => new(words, 12f, 1f, 0f, 14.123f, 0f, Centre: true);

    /// <summary>The width a label needs at its size: Impact's kerned width, tracking and slant, plus a margin each side.</summary>
    public static int WidthFor(ImpactLabels.Spec spec, int margin = 4)
    {
        float w;
        if (HdAssets.Impact(TextServer.FontAntialiasing.None, TextServer.Hinting.None) is { } font)
            w = font.GetStringSize(spec.Text, HorizontalAlignment.Left, -1, Math.Max(1, (int)MathF.Round(spec.Size))).X;
        else w = spec.Text.Length * spec.Size * 0.5f;
        w += spec.Text.Length * spec.Track + spec.Text.Count(c => c == ' ') * spec.WordSpace + spec.Size * MathF.Tan(spec.Slant * MathF.PI / 180f);
        return (int)MathF.Ceiling(w) + 2 * margin;
    }

    public bool Hit(Vector2I m) => Box.Position.X < m.X && m.X < Box.End.X - 1 && Box.Position.Y < m.Y && m.Y < Box.End.Y - 1;

    private readonly Dictionary<(string, int, int), bool[]?> _masks = [];

    public void Draw(ShellFrame f, ImpactLayer? impact, ShellFont fallback)
    {
        bool lit = Lit || Selected;
        Put(f, impact, fallback, Box, Label, lit ? Colors.White : dim);
        if (Lit && !Selected) Put(f, impact, fallback, BannerBox, BannerSpec(Banner), Colors.White);
        else { f.Restore(BannerBox.Position.X, BannerBox.Position.Y, BannerBox.Size.X, BannerBox.Size.Y); impact?.Hide(BannerBox.Position.X, BannerBox.Position.Y); }
    }

    public void Erase(ShellFrame f, ImpactLayer? impact)
    {
        f.Restore(Box.Position.X, Box.Position.Y, Box.Size.X, Box.Size.Y);
        impact?.Hide(Box.Position.X, Box.Position.Y);
        f.Restore(BannerBox.Position.X, BannerBox.Position.Y, BannerBox.Size.X, BannerBox.Size.Y);
        impact?.Hide(BannerBox.Position.X, BannerBox.Position.Y);
    }

    private void Put(ShellFrame f, ImpactLayer? impact, ShellFont fallback, Rect2I r, ImpactLabels.Spec spec, Color colour)
    {
        int x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        f.Restore(x, y, w, h);
        // the modern look: the layer draws the black box and the lettering over the frame
        if (impact is { Available: true }) { impact.Put(x, y, w, h, spec, colour); return; }
        f.Fill(x, y, w, h, f.Nearest(Colors.Black));
        var key = (spec.Text, w, h);
        if (!_masks.TryGetValue(key, out var mask)) _masks[key] = mask = ImpactPixels.Mask(spec, w, h);
        if (mask is not null)
        {
            var ink = f.Nearest(colour);
            for (int r0 = 0; r0 < h; r0++)
                for (int c = 0; c < w; c++)
                    if (mask[r0 * w + c]) f.Fill(x + c, y + r0, 1, 1, ink);
            return;
        }
        // not even Impact: the shell's own face
        var remap = new byte[256];
        Array.Fill(remap, f.Nearest(colour));
        remap[0] = 0xff;
        remap[fallback.Key & 0xff] = 0xff;
        f.Text(fallback, x + w / 2, y + (h - fallback.Height) / 2, "~" + spec.Text, remap);
    }
}
