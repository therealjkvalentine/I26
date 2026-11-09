using I76.Formats.Shell;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// With an HD menu pack the modern look hides a pop-up's own pixels under its upscale (ShellFrame.Placed, ShellSprites). The
/// BUILD AND REPAIR form's lists draw a damage stroke, a sprite with an upscale of its own, over the list's paper: the paper
/// must stay placed under it, or its pixels stay hidden with no picture over them and the list's entries float on the form.
/// A placed sprite goes only when the last of its pixels is written over (its box restored, another sprite over all of it).
/// </summary>
public class HdPopupPaperTests
{
    /// <summary>A sprite of w x h opaque pixels of one index, with no header offset.</summary>
    private static Sprite Make(int w, int h, byte index, int bank, int indexInBank)
    {
        var head = new byte[0x18];
        BitConverter.GetBytes(w - 1).CopyTo(head, 16); BitConverter.GetBytes(h - 1).CopyTo(head, 20);
        var rle = new List<byte>(head);
        for (int y = 0; y < h; y++)
        {
            rle.Add((byte)(w << 1 | 1));
            for (int x = 0; x < w; x++) rle.Add(index);
            rle.Add(0);
        }
        var s = Sprite.Decode(rle.ToArray());
        s.Bank = bank; s.Index = indexInBank;
        return s;
    }

    private static Sprite Paper() => Make(100, 60, 7, 0x24, 6);
    private static Sprite Stroke() => Make(30, 3, 9, 0x23, 0);

    private static ShellFrame Frame()
    {
        var f = new ShellFrame();
        f.LoadBackground(Pcx.Create(ShellFrame.W, ShellFrame.H, new byte[ShellFrame.W * ShellFrame.H], new byte[768]), black: false);
        f.HasHdSprite = _ => true;
        return f;
    }

    [Fact]
    public void AStrokeDrawnOnAListsPaperLeavesThePaperPlacedUnderIt()
    {
        var f = Frame();
        var paper = Paper();
        var stroke = Stroke();
        f.Blit(paper, 16, 96);
        f.Blit(stroke, 30, 110);
        // both placed, the paper first so the stroke's picture is drawn over it
        Assert.Equal([paper, stroke], f.Placed.Select(p => p.Sprite).ToArray());
        Assert.Equal((16, 96), (f.Placed[0].X, f.Placed[0].Y));
        // the paper's pixels stay hidden under its upscale: its corner, the stroke, and the paper beside the stroke
        Assert.True(f.Hidden(16, 96));
        Assert.True(f.Hidden(30, 110));
        Assert.True(f.Hidden(115, 155));
        Assert.False(f.Hidden(15, 96));
        Assert.Equal(9, f.At(30, 110));
    }

    [Fact]
    public void TheSameSpriteDrawnAgainInItsPlaceIsPlacedOnce()
    {
        var f = Frame();
        var paper = Paper();
        f.Blit(paper, 16, 96);
        f.Blit(Stroke(), 30, 110);
        int v = f.PlacedVersion;
        f.Blit(paper, 16, 96);
        var p = Assert.Single(f.Placed);
        Assert.Same(paper, p.Sprite);
        Assert.NotEqual(v, f.PlacedVersion);
        Assert.True(f.Hidden(30, 110));
    }

    [Fact]
    public void APaperStaysWhileAnyOfItIsLeftAndGoesWithItsBox()
    {
        var f = Frame();
        f.Blit(Paper(), 16, 96);
        // a corner put back (a row redrawn): the paper stays, that corner is the background's again
        f.Restore(16, 96, 10, 10);
        Assert.Single(f.Placed);
        Assert.False(f.Hidden(16, 96));
        Assert.True(f.Hidden(26, 106));
        // a fill over it the same
        f.Fill(100, 140, 5, 5, 0x0f);
        Assert.Single(f.Placed);
        Assert.False(f.Hidden(100, 140));
        // the whole box restored (the pop-up closed): gone, nothing hidden
        f.Restore(16, 96, 100, 60);
        Assert.Empty(f.Placed);
        Assert.False(f.Hidden(60, 120));
    }

    [Fact]
    public void ASpriteWithoutAnUpscaleOverThePaperTakesOnlyItsOwnPixels()
    {
        var f = Frame();
        var paper = Paper();
        f.HasHdSprite = s => s == paper;
        f.Blit(paper, 16, 96);
        f.Blit(Stroke(), 30, 110);
        Assert.Same(paper, Assert.Single(f.Placed).Sprite);
        Assert.False(f.Hidden(30, 110));
        Assert.True(f.Hidden(30, 109));
        Assert.True(f.Hidden(30, 113));
    }

    [Fact]
    public void ANewBackgroundTakesEveryPlacedSpriteOff()
    {
        var f = Frame();
        f.Blit(Paper(), 16, 96);
        f.Blit(Stroke(), 30, 110);
        f.LoadBackground(Pcx.Create(ShellFrame.W, ShellFrame.H, new byte[ShellFrame.W * ShellFrame.H], new byte[768]), black: false);
        Assert.Empty(f.Placed);
        Assert.False(f.Hidden(30, 110));
    }

    /// <summary>A pack.json with sections the game does not read (a pack's own notes on its pages) is still the pack's.</summary>
    [Fact]
    public void APackJsonWithSectionsTheGameDoesNotKnowStillParses()
    {
        var json = """
            {"name": "HD Menu Pack", "version": "0.1.0", "date": "2026-11-03", "min_engine": "1.1",
             "text_redraws": [{"texture": "shellbg16", "spec": "tools/text-redraws/shellbg16.json", "fonts": ["Arial"]}],
             "rules": {"date": "2026-11-08", "tool": "tools/menu-rules.py", "pages": [1, 2, 16], "note": "lossless"},
             "flat_art": {"date": "2026-11-08", "pages": [14, 26]}}
            """u8.ToArray();
        var info = HdPackInfo.Parse(json);
        Assert.NotNull(info);
        Assert.Equal("HD Menu Pack", info.Name);
        Assert.Equal("0.1.0", info.Version);
        Assert.Equal("1.1", info.MinEngine);
        Assert.False(info.Partial);
    }
}
