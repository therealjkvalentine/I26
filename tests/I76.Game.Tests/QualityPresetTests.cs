using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Quality row (QualityPreset): each column read back as its own name, Balanced a first start's defaults, any row
/// changed after a column reads Custom, the texture rows taking the best size the packs offer and left out with none, and the
/// sheet's Reset back to Balanced. Without the engine.
/// </summary>
public class QualityPresetTests
{
    private static readonly int[] Both = [8, 4], FourOnly = [4], None = [];

    [Theory]
    [InlineData(QualityPreset.Original)]
    [InlineData(QualityPreset.Balanced)]
    [InlineData(QualityPreset.High)]
    [InlineData(QualityPreset.Ultra)]
    public void EachColumnReadsBackAsItsOwnName(int column)
    {
        foreach (var (sizes, packs) in new[] { (Both, true), (FourOnly, true), (None, false) })
        {
            var s = new ShellSettings();
            QualityPreset.Apply(s, column, sizes, packs);
            Assert.Equal(column, QualityPreset.Match(s, sizes, packs));
            Assert.Equal(QualityPreset.Names[column], QualityPreset.Word(s, sizes, packs));
        }
    }

    [Fact]
    public void TheColumnsDiffer()
    {
        var words = Enumerable.Range(0, QualityPreset.Names.Length).Select(c =>
        {
            var s = new ShellSettings();
            QualityPreset.Apply(s, c, Both, true);
            return string.Join(",", QualityPreset.Rows.Select(r => QualityPreset.Get(s, r)));
        }).ToList();
        Assert.Equal(words.Count, words.Distinct().Count());
    }

    /// <summary>A first start is Balanced: its column is every row's default, so Reset (the sheet's) gives Balanced back.</summary>
    [Fact]
    public void AFirstStartAndResetAreBalanced()
    {
        Assert.Equal(QualityPreset.Balanced, QualityPreset.Match(new ShellSettings(), Both, true));
        Assert.All(QualityPreset.Table, t => Assert.Equal(QualityPreset.Default, t.Values[QualityPreset.Balanced]));
        var s = new ShellSettings();
        QualityPreset.Apply(s, QualityPreset.Ultra, Both, true);
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.Equal(QualityPreset.Balanced, QualityPreset.Match(s, Both, true));
    }

    /// <summary>Any one row stepped after a column reads Custom.</summary>
    [Fact]
    public void ChangingAnyRowGivesCustom()
    {
        foreach (var (row, _) in QualityPreset.Table)
            for (int c = 0; c < QualityPreset.Names.Length; c++)
            {
                var s = new ShellSettings();
                QualityPreset.Apply(s, c, Both, true);
                Step(s, row);
                Assert.True(QualityPreset.Match(s, Both, true) != c, $"{row} stepped on {QualityPreset.Names[c]}");
            }
        var t = new ShellSettings();
        QualityPreset.Apply(t, QualityPreset.High, Both, true);
        t.Haze = 0; t.Glow = 0;
        Assert.Equal(-1, QualityPreset.Match(t, Both, true));
        Assert.Equal(QualityPreset.Custom, QualityPreset.Word(t, Both, true));
    }

    private static void Step(ShellSettings s, GraphicsRow r)
    {
        switch (r)
        {
            case GraphicsRow.RenderScale: s.RenderScale = (s.RenderScale + 1) % I76.Game.Boot.RenderScale.Names.Length; break;
            case GraphicsRow.AntiAliasing: AntiAliasingRow.Step(s); break;
            case GraphicsRow.ShadowDetail: ShadowDetailRow.Step(s); break;
            case GraphicsRow.TerrainShadows: TerrainShadowsRow.Step(s); break;
            case GraphicsRow.VisibilityRange: VisibilityRange.Step(s, false); break;
            case GraphicsRow.WorldTextures or GraphicsRow.CarTextures: TextureRow.Step(s, r, Both); break;
            case GraphicsRow.Vegetation: s.Vegetation = (s.Vegetation + 1) % 3; break;
            case GraphicsRow.AmbientOcclusion: s.AmbientOcclusion = !s.AmbientOcclusion; break;
            case GraphicsRow.Reflections: s.Reflections = !s.Reflections; break;
            case GraphicsRow.Haze: AirRows.StepHaze(s); break;
            case GraphicsRow.MirrorRate: s.MirrorRate = (I76.Game.Sim.MirrorRate)(((int)s.MirrorRate + 1) % I76.Game.Sim.MirrorRateRow.Names.Length); break;
            case GraphicsRow.Glow: AirRows.StepGlow(s); break;
            case GraphicsRow.SmokeLighting: SmokeLightingRow.Step(s); break;
            case GraphicsRow.HeatShimmer: HeatShimmerRow.Step(s); break;
            default: throw new ArgumentOutOfRangeException(nameof(r));
        }
    }

    /// <summary>The columns as written down in docs/playing.md.</summary>
    [Fact]
    public void TheColumnsAreAsDocumented()
    {
        var o = new ShellSettings();
        QualityPreset.Apply(o, QualityPreset.Original, Both, true);
        Assert.Equal((4, 0, 1, 0), (o.RenderScale, AntiAliasingRow.Index(o), ShadowDetailRow.Index(o), o.TerrainShadows));
        Assert.Equal((0, 0, 0), (o.WorldTextures, o.CarTextures, VisibilityRange.Index(o, false)));
        Assert.False(o.AmbientOcclusion || o.Reflections || o.SmokeLighting);
        Assert.Equal((0, 0, 0, 0, I76.Game.Sim.MirrorRate.Alternate), (o.Vegetation, o.Haze, o.Glow, o.HeatShimmer, o.MirrorRate));
        var u = new ShellSettings();
        QualityPreset.Apply(u, QualityPreset.Ultra, Both, true);
        Assert.Equal("200%*", I76.Game.Boot.RenderScale.Names[u.RenderScale]);
        Assert.Equal("SMAA*", AntiAliasingRow.Names[AntiAliasingRow.Index(u)]);
        Assert.Equal(ShadowDetailRow.Ultra, ShadowDetailRow.Index(u));
        Assert.Equal(TerrainShadowsRow.Fine, u.TerrainShadows);
        Assert.Equal((4, 8), (u.WorldTextures, u.CarTextures));
        Assert.True(u.AmbientOcclusion && u.Reflections && u.SmokeLighting);
        Assert.Equal((HeatShimmerRow.Road, I76.Game.Sim.MirrorRate.Full, 2, 2), (u.HeatShimmer, u.MirrorRate, u.Haze, u.Vegetation));
    }

    /// <summary>A column asks for a texture size and takes the largest the packs offer up to it; with no pack the texture rows are left alone and do not count.</summary>
    [Fact]
    public void TheTexturesTakeTheBestSizeThePacksOffer()
    {
        Assert.Equal(8, QualityPreset.BestSize(GraphicsRow.CarTextures, 8, Both));
        Assert.Equal(4, QualityPreset.BestSize(GraphicsRow.CarTextures, 8, FourOnly));
        Assert.Equal(4, QualityPreset.BestSize(GraphicsRow.WorldTextures, 8, Both));
        Assert.Equal(0, QualityPreset.BestSize(GraphicsRow.CarTextures, 0, Both));
        var s = new ShellSettings { WorldTextures = 4, CarTextures = 4 };
        QualityPreset.Apply(s, QualityPreset.Ultra, FourOnly, true);
        Assert.Equal((4, 4), (s.WorldTextures, s.CarTextures));
        Assert.Equal(QualityPreset.Ultra, QualityPreset.Match(s, FourOnly, true));
        var n = new ShellSettings { WorldTextures = 4, CarTextures = 8 };
        QualityPreset.Apply(n, QualityPreset.Original, None, false);
        Assert.Equal((4, 8), (n.WorldTextures, n.CarTextures));
        Assert.Equal(QualityPreset.Original, QualityPreset.Match(n, None, false));
    }

    /// <summary>The preset is the sheet's row but has nothing of its own: a page's copy carries its rows, and the pad's row copies them all.</summary>
    [Fact]
    public void ThePresetIsDerived()
    {
        Assert.Equal(GraphicsPages.Sheet, GraphicsPages.PageOf(GraphicsRow.Quality));
        Assert.All(QualityPreset.Rows, r => Assert.NotEqual(GraphicsPages.Sheet, GraphicsPages.PageOf(r)));
        var u = new ShellSettings();
        QualityPreset.Apply(u, QualityPreset.Ultra, Both, true);
        var t = new ShellSettings();
        QualityPreset.Copy(u, t);
        Assert.Equal(QualityPreset.Ultra, QualityPreset.Match(t, Both, true));
        var snap = GraphicsPages.CopyAll(u, new ShellSettings());
        Assert.Equal(QualityPreset.Ultra, QualityPreset.Match(snap, Both, true));
        // a click from Custom goes to Original, from Ultra back to Original
        u.Glow = 0;
        QualityPreset.Step(u, Both, true);
        Assert.Equal(QualityPreset.Original, QualityPreset.Match(u, Both, true));
    }

    [Fact]
    public void EveryRowHasASearchWordAndSaysWhenItApplies()
    {
        Assert.StartsWith("Quality (graphics preset)", GraphicsHelp.Text(GraphicsRow.Quality, false));
        Assert.Contains("(FOV)", GraphicsHelp.Text(GraphicsRow.FieldOfView, false));
        Assert.Contains("(draw distance)", GraphicsHelp.Text(GraphicsRow.VisibilityRange, false));
        Assert.Contains("frame cap", GraphicsHelp.Text(GraphicsRow.MaxFps, false));
        Assert.Contains("(HUD panels)", GraphicsHelp.Text(GraphicsRow.ExtInstruments, false));
        foreach (var r in Enum.GetValues<GraphicsRow>())
        {
            var t = GraphicsHelp.Text(r, false);
            Assert.True(t.Contains("Applies at once") || t.Contains("next mission") || t.Contains("next start"), $"{r}: {t}");
        }
    }
}
