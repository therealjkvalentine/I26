using Godot;
using I76.Formats.Shell;
using I76.Formats.Tests;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The forms' scroll triangles and ⊟ ⊞ boxes, and the blueprint's DMG stamps, redrawn in the modern look (FormGlyphs, DamageStamp).</summary>
public class FormGlyphsTests
{
    private static readonly Color Ink = new(0.2f, 0.18f, 0.1f), Fill = new(0.93f, 0.61f, 0.29f);

    private static byte[] Rgba(FormGlyphs.Glyph g, float scale) => FormGlyphs.Raster(g, scale, Vector2.Zero, Ink, Fill).Rgba;

    [Fact]
    public void A_lists_up_and_down_arrows_are_one_shape_mirrored()
    {
        // the salvage list's pair: the same size, the down one the up one upside down, pixel for pixel at the owner's 3x
        var up = new FormGlyphs.Glyph(FormGlyphs.Kind.Up, 19.5f, 283.5f, 10, 8, 1);
        var down = up with { Kind = FormGlyphs.Kind.Down };
        var (ru, pu) = FormGlyphs.Raster(up, 3, Vector2.Zero, Ink, Fill);
        var (rd, pd) = FormGlyphs.Raster(down, 3, Vector2.Zero, Ink, Fill);
        Assert.Equal(ru, rd);
        int w = ru.Size.X, h = ru.Size.Y;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                    Assert.Equal(pu[(y * w + x) * 4 + c], pd[((h - 1 - y) * w + x) * 4 + c]);
        // and even: left and right halves mirror each other
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.Equal(pu[(y * w + x) * 4 + 3], pu[(y * w + (w - 1 - x)) * 4 + 3]);
    }

    [Fact]
    public void Left_and_right_arrows_mirror_each_other()
    {
        var left = new FormGlyphs.Glyph(FormGlyphs.Kind.Left, 17.5f, 64.5f, 8.5f, 10, 1.5f);
        var (rl, pl) = FormGlyphs.Raster(left, 3, Vector2.Zero, Ink, Fill);
        var (rr, pr) = FormGlyphs.Raster(left with { Kind = FormGlyphs.Kind.Right }, 3, Vector2.Zero, Ink, Fill);
        Assert.Equal(rl, rr);
        int w = rl.Size.X, h = rl.Size.Y;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.Equal(pl[(y * w + x) * 4 + 3], pr[(y * w + (w - 1 - x)) * 4 + 3]);
    }

    [Fact]
    public void The_boxes_are_crisp_and_the_plus_is_the_minus_with_its_upright()
    {
        var minus = new FormGlyphs.Glyph(FormGlyphs.Kind.Minus, 113, 361, 9, 9, 1);
        var plus = minus with { Kind = FormGlyphs.Kind.Plus };
        // at a whole scale every pixel is wholly ink, fill or paper: no soft edge
        foreach (var g in new[] { minus, plus })
            foreach (float s in new[] { 2f, 3f, 4f })
            {
                var px = Rgba(g, s);
                for (int i = 0; i < px.Length; i += 4) Assert.True(px[i + 3] is 0 or 255, $"{g.Kind} at {s}x: alpha {px[i + 3]}");
            }
        // the box's side is 10 pixels (its stroke's centre line 9), the bar 6 by 2, as the art's
        Assert.Equal(2, minus.At(108.6f, 361));
        Assert.Equal(2, minus.At(110.2f, 361));
        Assert.Equal(1, minus.At(113, 359.5f));
        Assert.Equal(2, plus.At(113, 359.5f));
        Assert.Equal(0, minus.At(118.6f, 361));
        var pm = Rgba(minus, 3);
        var pp = Rgba(plus, 3);
        for (int i = 0; i < pm.Length; i += 4)
            if (pm[i] < 128 && pm[i + 3] == 255) Assert.True(pp[i] < 128, "every ink pixel of the minus is ink in the plus");
    }

    [Fact]
    public void Edges_are_signed_and_the_apex_is_on_the_line()
    {
        var up = new FormGlyphs.Glyph(FormGlyphs.Kind.Up, 10, 10, 10, 8, 1);
        Assert.True(up.Edge(10, 11) < -1);
        Assert.True(up.Edge(20, 10) > 1);
        Assert.Equal(0, up.Edge(10, 6), 3);
        Assert.Equal(0, up.Edge(5, 14), 3);
        Assert.Equal(0, up.Edge(15, 14), 3);
        var right = new FormGlyphs.Glyph(FormGlyphs.Kind.Right, 0, 0, 8, 10, 1);
        Assert.Equal(0, right.Edge(4, 0), 3);
        Assert.Equal(0, right.Edge(-4, -5), 3);
    }

    private static readonly FormGlyphs.Glyph Box = new(FormGlyphs.Kind.Minus, 30, 30, 9, 9, 1);

    [Fact]
    public void A_glyph_copied_by_an_edit_follows_it_and_one_written_over_goes()
    {
        // the host form's ⊟ copied onto a new row, the old place then written over by a blank row
        var copy = new ShellFrame.BackgroundEdit(-1, 20, 20, 20, 20, 120, 60, null);
        var wipe = new ShellFrame.BackgroundEdit(-1, 300, 300, 30, 4, 20, 32, null);
        var after = FormGlyphs.Follow([Box], [copy, wipe]);
        Assert.Equal([Box.Moved(100, 40)], after);
        // the host form's paste of the box's own 10 by 10 pixels onto the BOTS row carries it
        var exact = new ShellFrame.BackgroundEdit(0, 25, 25, 10, 10, 50, 300, null);
        Assert.Equal([Box, Box.Moved(25, 275)], FormGlyphs.Follow([Box], [exact]));
        // a copy that leaves part of it out does not carry it
        var mask = new bool[20 * 20];
        Array.Fill(mask, true);
        mask[10 * 20 + 10] = false;
        Assert.Equal([Box], FormGlyphs.Follow([Box], [copy with { Mask = mask }]));
        // from the background as loaded (From 0), after the glyph was wiped there
        var fromLoaded = new ShellFrame.BackgroundEdit(0, 20, 20, 20, 20, 200, 200, null);
        Assert.Equal([Box.Moved(180, 180)], FormGlyphs.Follow([Box], [wipe, fromLoaded]));
    }

    // a palette: 0 black ink, 1 dark ink, 10..13 paper shades, 20 the glyph's fill
    private static byte[] Palette()
    {
        var p = new byte[768];
        void Set(int i, byte r, byte g, byte b) { p[i * 3] = r; p[i * 3 + 1] = g; p[i * 3 + 2] = b; }
        Set(0, 20, 18, 10); Set(1, 60, 50, 25);
        Set(10, 231, 173, 74); Set(11, 225, 168, 70); Set(12, 236, 178, 80); Set(13, 228, 170, 72);
        Set(20, 237, 156, 74);
        return p;
    }

    /// <summary>Dithered paper, a rule down x = <paramref name="rule"/>, the glyph painted from its geometry.</summary>
    private static int[] Paper(int w, int h, FormGlyphs.Glyph g, int rule)
    {
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                px[y * w + x] = 10 + (x * 7 + y * 3) % 4;
                if (x == rule) px[y * w + x] = 1;
                int k = g.At(x + 0.5f, y + 0.5f);
                if (k == 2) px[y * w + x] = 0;
                else if (k == 1) px[y * w + x] = 20;
            }
        return px;
    }

    [Fact]
    public void The_paper_patch_comes_from_plain_paper_and_keeps_the_rule_beside_it()
    {
        const int w = 80, h = 60;
        var g = new FormGlyphs.Glyph(FormGlyphs.Kind.Up, 20.5f, 30.5f, 10, 8, 1);
        int rule = 27;
        var px = Paper(w, h, g, rule);
        int Px(int x, int y) => x >= 0 && y >= 0 && x < w && y < h ? px[y * w + x] : -1;
        var pal = Palette();
        var inks = FormGlyphs.Inks(Px, pal, g);
        Assert.Equal(((byte)0, (byte)20), inks);
        var patch = FormGlyphs.Patch(Px, pal, w, h, g, [g]);
        Assert.NotNull(patch);
        Assert.Equal(FormGlyphs.PatchBox(g).Position, new Vector2I(patch!.Dx, patch.Dy));
        for (int y = 0; y < patch.H; y++)
            for (int x = 0; x < patch.W; x++)
            {
                if (!patch.Mask![y * patch.W + x]) continue;
                // every pixel laid down is paper, and the rule is never written over
                Assert.InRange(px[(patch.Sy + y) * w + patch.Sx + x], 10, 13);
                Assert.NotEqual(rule, patch.Dx + x);
                // every pixel of the glyph's ink is covered
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x] is 0 or 20)
                    Assert.True(x - patch.Dx is var mx && y - patch.Dy is var my && mx >= 0 && my >= 0 && mx < patch.W && my < patch.H && patch.Mask![my * patch.W + mx],
                        $"glyph pixel ({x}, {y}) under the patch");
    }

    [Fact]
    public void No_glyph_where_the_art_has_none()
    {
        var px = new int[40 * 40];
        Array.Fill(px, 10);
        Assert.Null(FormGlyphs.Inks((x, y) => x is >= 0 and < 40 && y is >= 0 and < 40 ? px[y * 40 + x] : -1, Palette(), new FormGlyphs.Glyph(FormGlyphs.Kind.Up, 20, 20, 10, 8, 1)));
    }

    // ---- the stamps ----

    /// <summary>A sprite from its pixels (−1 transparent), as the bank's RLE (literal runs).</summary>
    private static Sprite MakeSprite(int w, int h, Func<int, int, int> px, int x0 = 0, int y0 = 0)
    {
        var head = new byte[0x18];
        BitConverter.GetBytes(x0).CopyTo(head, 8);
        BitConverter.GetBytes(y0).CopyTo(head, 12);
        BitConverter.GetBytes(x0 + w - 1).CopyTo(head, 16);
        BitConverter.GetBytes(y0 + h - 1).CopyTo(head, 20);
        var b = new List<byte>(head);
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            while (x < w)
            {
                if (px(x, y) < 0)
                {
                    int n = 0;
                    while (x < w && px(x, y) < 0 && n < 255) { x++; n++; }
                    b.Add(1); b.Add((byte)n);
                    continue;
                }
                var run = new List<byte>();
                while (x < w && px(x, y) >= 0 && run.Count < 127) run.Add((byte)px(x++, y));
                b.Add((byte)(run.Count << 1 | 1));
                b.AddRange(run);
            }
            b.Add(0);
        }
        return Sprite.Decode(b.ToArray());
    }

    private static (Sprite Damaged, Sprite Undamaged) Stamped(float cx, float cy, float angleDeg, int w = 80, int h = 70)
    {
        // a part: its outline in ink 1 on transparent; the damaged copy with the stamp's design in ink 20 over it
        int Part(int x, int y) => x == 10 || y == 10 || x == w - 10 ? 1 : -1;
        var fit = new DamageStamp.Fit(cx, cy, angleDeg * MathF.PI / 180, 20, 0);
        int Damaged(int x, int y) => DamageStamp.At(fit, DamageStamp.Style.Outline, 0, x + 0.5f, y + 0.5f).Ink == 2 ? 20 : Part(x, y);
        return (MakeSprite(w, h, Damaged), MakeSprite(w, h, Part));
    }

    [Theory]
    [InlineData(40, 35, 0)]
    [InlineData(40.5f, 34, 48)]
    [InlineData(38, 36, -50)]
    [InlineData(40, 35, -90)]
    [InlineData(41, 33, 7)]
    [InlineData(39, 35, -82)]
    public void A_stamp_is_found_where_and_as_it_is_turned(float cx, float cy, float deg)
    {
        var (d, u) = Stamped(cx, cy, deg);
        var pal = Palette();
        // the stamp's ink: a red, far from the part's
        pal[20 * 3] = 202; pal[20 * 3 + 1] = 87; pal[20 * 3 + 2] = 54;
        var f = DamageStamp.Measure(d, u, pal);
        Assert.NotNull(f);
        Assert.Equal(20, f!.Ink);
        Assert.InRange(f.Cx, cx - 1, cx + 1);
        Assert.InRange(f.Cy, cy - 1, cy + 1);
        float got = f.Angle * 180 / MathF.PI;
        Assert.InRange(MathF.IEEERemainder(got - deg, 360), -2.5f, 2.5f);
        // square ones come out exactly square, as the art's
        if (deg % 90 == 0) Assert.Equal(deg, got, 3);
    }

    [Fact]
    public void A_sprite_with_no_stamp_has_none()
    {
        var (_, u) = Stamped(40, 35, 0);
        Assert.Null(DamageStamp.Measure(u, u, Palette()));
    }

    [Fact]
    public void The_two_treatments_letter_DMG_and_the_outline_leaves_the_strokes_hollow()
    {
        // the middle of the D's upright: inked solid, blank in the outline (its two edges are inked)
        Assert.True(DamageStamp.Depth(5.5f, 9.5f, DamageStamp.Style.Solid) > 0);
        Assert.True(DamageStamp.Depth(5.5f, 9.5f, DamageStamp.Style.Outline) < 0);
        Assert.True(DamageStamp.Depth(4.5f, 9.5f, DamageStamp.Style.Outline) >= 0);
        Assert.True(DamageStamp.Depth(6.5f, 9.5f, DamageStamp.Style.Outline) >= 0);
        // the bars' ring, and the paper between the bars and the letters
        Assert.True(DamageStamp.Depth(18, 0.5f, DamageStamp.Style.Outline) >= 0);
        Assert.True(DamageStamp.Depth(18, 1.5f, DamageStamp.Style.Outline) < 0);
        Assert.True(DamageStamp.Depth(18, 3.5f, DamageStamp.Style.Solid) < 0);
    }

    [Fact]
    public void A_stamps_ink_is_uneven_but_the_same_every_time()
    {
        var f = new DamageStamp.Fit(20, 12, 0.3f, 20, 0);
        var a = new List<float>();
        for (float y = 0; y < 24; y += 0.25f)
            for (float x = 0; x < 40; x += 0.25f)
                if (DamageStamp.At(f, DamageStamp.Style.Solid, 5, x, y) is (2, var al)) a.Add(al);
        Assert.True(a.Count > 1000);
        Assert.True(a.Max() - a.Min() > 0.2f, "uneven");
        Assert.True(a.Average() > 0.6f, "but inked");
        Assert.Equal(DamageStamp.At(f, DamageStamp.Style.Solid, 5, 17.3f, 11.1f), DamageStamp.At(f, DamageStamp.Style.Solid, 5, 17.3f, 11.1f));
    }

    [Fact]
    public void Noting_the_undamaged_part_leaves_the_frame_as_it_was()
    {
        // the 1997 look draws the frame's pixels: the damaged sprite's, with or without the note
        var (d, u) = Stamped(40, 35, 30);
        var a = new ShellFrame();
        var b = new ShellFrame();
        a.BlitAt(d, 100, 100);
        b.BlitAt(d, 100, 100, u);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels));
        // the modern look keeps the note on the placed sprite
        b.HasHdSprite = _ => true;
        b.BlitAt(d, 100, 100, u);
        Assert.Equal(u, b.Placed[^1].Undamaged!.Value.Sprite);
        b.BlitRotated(d, 300, 300, u);
        Assert.Equal((u, 300 - u.X0 - (u.Width - 1), 300 - u.Y0 - (u.Height - 1)), b.Placed[^1].Undamaged!.Value);
    }

    // ---- the install's art ----

    private static ShellDatabase Db() => ShellDatabase.Open(TestInstall.Install.FindFile("DATABASE.MW2")!);

    [InstallFact]
    public void Every_listed_glyph_is_in_the_art_and_has_paper_beside_it()
    {
        var db = Db();
        var missing = new List<string>();
        int noPatch = 0;
        foreach (int rec in FormGlyphs.RecordsWithGlyphs)
        {
            var bg = db.Background(rec);
            int Px(int x, int y) => x >= 0 && y >= 0 && x < bg.Width && y < bg.Height ? bg.Pixels[y * bg.Width + x] : -1;
            var all = FormGlyphs.ForRecord(rec);
            foreach (var g in all)
            {
                if (FormGlyphs.Inks(Px, bg.Palette, g) is null) missing.Add($"record {rec} {g.Kind} at ({g.Cx}, {g.Cy})");
                if (FormGlyphs.Patch(Px, bg.Palette, bg.Width, bg.Height, g, all) is null) noPatch++;
            }
        }
        var pal = db.Background(1).Palette;
        foreach (int sp in FormGlyphs.PopupsWithGlyphs)
        {
            var s = db.Bank(FormGlyphs.PopupBank)[sp]!;
            int Px(int x, int y) => x >= 0 && y >= 0 && x < s.Width && y < s.Height && s.Opaque[y * s.Width + x] ? s.Pixels[y * s.Width + x] : -1;
            var all = FormGlyphs.ForPopup(sp);
            foreach (var g in all)
            {
                if (FormGlyphs.Inks(Px, pal, g) is null) missing.Add($"pop-up {sp} {g.Kind} at ({g.Cx}, {g.Cy})");
                if (FormGlyphs.Patch(Px, pal, s.Width, s.Height, g, all) is null) noPatch++;
            }
        }
        Assert.Empty(missing);
        // three of the greyed BUILD AND REPAIR FORM's boxes (record 29: its dithered paper next to the next row's) have none near
        Assert.True(noPatch <= 3, $"{noPatch} without a patch");
    }

    [InstallFact]
    public void Every_damaged_part_in_the_art_has_its_stamp_found_upright_or_turned_at_most_a_quarter()
    {
        var db = Db();
        var pal = db.Background(1).Palette;
        int n = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // the garage's blueprint banks: engines, suspensions, brakes, tyres, weapons, droppers, specials
        foreach (int bank in new[] { 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 59 })
        {
            var b = db.Bank(bank);
            for (int i = 0; i < b.Count; i++)
            {
                if (i % 4 == 0 || b[i] is not { } d || b[i - i % 4] is not { } u) continue;
                var f = DamageStamp.Measure(d, u, pal);
                Assert.True(f is not null, $"bank {bank} sprite {i}: no stamp");
                Assert.InRange(f!.Angle * 180 / MathF.PI, -100, 100);
                Assert.True(f.Score > 25, $"bank {bank} sprite {i}: score {f.Score}");
                n++;
            }
        }
        Assert.Equal(189, n);
        // found when the form is drawn: a few milliseconds each
        Assert.True(sw.Elapsed.TotalMilliseconds / n < 60, $"{sw.Elapsed.TotalMilliseconds / n:0.0} ms a stamp");
    }
}
