using Godot;
using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Anti-Aliasing's SMAA* and 2x+SMAA* ([port] screen_aa over the +0x8 MSAA byte) and Shadow Detail's Ultra* ([port] shadow_detail over
/// the +0x6 byte's High): the rows' cycling, the file's keys, what a build without the keys reads, Reset and Copy. Without the engine.
/// </summary>
public class DetailRowsTests
{
    [Fact]
    public void TheAntiAliasingRowCyclesThroughSixAndKeepsTheByteAnMsaa()
    {
        Assert.Equal(["Off", "2x", "4x", "8x*", "SMAA*", "2x+SMAA*"], AntiAliasingRow.Names);
        var s = new ShellSettings();
        Assert.Equal(1, AntiAliasingRow.Index(s));   // the default, 2x, unchanged
        Assert.False(s.Smaa);
        (byte Msaa, bool Smaa)[] seen = [(2, false), (3, false), (0, true), (1, true), (0, false), (1, false)];
        foreach (var (msaa, smaa) in seen)
        {
            AntiAliasingRow.Step(s);
            Assert.Equal(msaa, s.Graphics[0x8]);
            Assert.Equal(smaa, s.Smaa);
            Assert.Equal(smaa, AntiAliasingRow.SmaaOn(s));
        }
        Assert.Equal(1, AntiAliasingRow.Index(s));
        for (int v = 0; v < AntiAliasingRow.Names.Length; v++) { AntiAliasingRow.Put(s, v); Assert.Equal(v, AntiAliasingRow.Index(s)); }
    }

    [Fact]
    public void TheShadowRowCyclesThroughFourAndUltraIsHighInTheByte()
    {
        Assert.Equal(["Off", "Low", "High", "Ultra*"], ShadowDetailRow.Names);
        var s = new ShellSettings();
        Assert.Equal(1, ShadowDetailRow.Index(s));   // Low, the default, unchanged
        ShadowDetailRow.Step(s); Assert.Equal((2, false), (s.Graphics[0x6], s.ShadowUltra));
        ShadowDetailRow.Step(s); Assert.Equal((2, true), (s.Graphics[0x6], s.ShadowUltra));
        Assert.True(ShadowDetailRow.UltraOn(s));
        Assert.Equal((4, 1500f), ShadowReach.For(s.Graphics[0x6]));   // Ultra keeps High's splits and reach
        ShadowDetailRow.Step(s); Assert.Equal((0, false), (s.Graphics[0x6], s.ShadowUltra));
        ShadowDetailRow.Step(s); Assert.Equal(1, ShadowDetailRow.Index(s));
    }

    [Theory]
    [InlineData(4, 0, "smaa", null)]
    [InlineData(5, 1, "smaa", null)]
    [InlineData(1, 1, null, null)]
    [InlineData(3, 3, null, null)]
    public void TheChoiceWritesAndReadsBack(int aa, byte msaaByte, string? screenAa, string? shadow)
    {
        var s = new ShellSettings();
        AntiAliasingRow.Put(s, aa);
        var f = s.Write(null);
        Assert.Equal(msaaByte, ((byte[])f.GetValue("shell", "graphics"))[0x8]);
        Assert.Equal(screenAa, f.HasSectionKey("port", "screen_aa") ? f.GetValue("port", "screen_aa").ToString() : null);
        Assert.Equal(shadow, f.HasSectionKey("port", "shadow_detail") ? f.GetValue("port", "shadow_detail").ToString() : null);
        Assert.Equal(aa, AntiAliasingRow.Index(ShellSettings.Read(f)));
    }

    [Fact]
    public void UltraWritesHighAndTheKeyAndReadsBack()
    {
        var s = new ShellSettings();
        ShadowDetailRow.Put(s, ShadowDetailRow.Ultra);
        var f = s.Write(null);
        Assert.Equal(2, ((byte[])f.GetValue("shell", "graphics"))[0x6]);
        Assert.Equal("ultra", f.GetValue("port", "shadow_detail").ToString());
        Assert.Equal(ShadowDetailRow.Ultra, ShadowDetailRow.Index(ShellSettings.Read(f)));
        // a default file writes neither key
        var plain = new ShellSettings().Write(null);
        Assert.False(plain.HasSectionKey("port", "screen_aa"));
        Assert.False(plain.HasSectionKey("port", "shadow_detail"));
    }

    /// <summary>
    /// A build without the keys reads only the bytes: SMAA as Off, 2x+SMAA as 2x, Ultra as High, each a value it offers (and it drops the
    /// key on its next save). A file from before the keys reads as it did. A key over a byte the older build has since changed to 4x/8x
    /// or Low counts for nothing.
    /// </summary>
    [Fact]
    public void AnOlderBuildReadsTheBytesAsChoicesItHas()
    {
        var s = new ShellSettings();
        AntiAliasingRow.Put(s, 5);
        ShadowDetailRow.Put(s, ShadowDetailRow.Ultra);
        var g = (byte[])s.Write(null).GetValue("shell", "graphics");
        Assert.InRange(g[0x8], 0, I76.Game.Boot.RenderScale.AntiAliasing.Length - 1);
        Assert.Equal("2x", I76.Game.Boot.RenderScale.AntiAliasing[g[0x8]]);
        Assert.Equal(2, g[0x6]);   // High, the top of the older row's Off/Low/High

        var older = new SettingsFile();
        older.SetValue("shell", "version", ShellSettings.WrittenVersion);
        older.SetValue("port", "fork_version", ShellSettings.ForkVersion);
        var bytes = (byte[])ShellSettings.DefaultGraphics.Clone();
        bytes[0x8] = 2; bytes[0x6] = 1;
        older.SetValue("shell", "graphics", bytes);
        older.SetValue("port", "screen_aa", "smaa");
        older.SetValue("port", "shadow_detail", "ultra");
        var r = ShellSettings.Read(older);
        Assert.Equal(2, AntiAliasingRow.Index(r));
        Assert.False(AntiAliasingRow.SmaaOn(r));
        Assert.Equal(1, ShadowDetailRow.Index(r));
        Assert.False(ShadowDetailRow.UltraOn(r));
        Assert.False(r.Write(null).HasSectionKey("port", "screen_aa"));

        var before = new SettingsFile();
        before.SetValue("shell", "version", ShellSettings.WrittenVersion);
        before.SetValue("port", "fork_version", ShellSettings.ForkVersion);
        before.SetValue("shell", "graphics", (byte[])ShellSettings.DefaultGraphics.Clone());
        var b = ShellSettings.Read(before);
        Assert.Equal((1, 1), (AntiAliasingRow.Index(b), ShadowDetailRow.Index(b)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("smaa", true)]
    [InlineData(" SMAA ", true)]
    [InlineData("fxaa", false)]
    [InlineData("", false)]
    public void TheScreenAaWord(string? word, bool on) => Assert.Equal(on, AntiAliasingRow.FromFile(word));

    [Fact]
    public void ResetAndCopyCarryTheKeys()
    {
        var s = new ShellSettings();
        AntiAliasingRow.Put(s, 4);
        ShadowDetailRow.Put(s, 3);
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.AntiAliasing, s, t);
        GraphicsPages.Copy(GraphicsRow.ShadowDetail, s, t);
        Assert.Equal((4, 3), (AntiAliasingRow.Index(t), ShadowDetailRow.Index(t)));
        GraphicsPages.Reset(t, GraphicsRow.AntiAliasing);
        GraphicsPages.Reset(t, GraphicsRow.ShadowDetail);
        Assert.Equal((1, 1), (AntiAliasingRow.Index(t), ShadowDetailRow.Index(t)));
        Assert.False(t.Smaa || t.ShadowUltra);
    }

    [Fact]
    public void SmaaIsTheModernLooksAndNotOnCompatibility()
    {
        Assert.Equal(Viewport.ScreenSpaceAAEnum.Smaa, ScreenAa.Mode(false, true, false));
        Assert.Equal(Viewport.ScreenSpaceAAEnum.Disabled, ScreenAa.Mode(false, false, false));
        Assert.Equal(Viewport.ScreenSpaceAAEnum.Disabled, ScreenAa.Mode(true, true, false));   // the 1997 look draws none
        Assert.Equal(Viewport.ScreenSpaceAAEnum.Disabled, ScreenAa.Mode(false, true, true));   // Compatibility has no SMAA
        Assert.Equal(Viewport.ScreenSpaceAAEnum.Fxaa, ScreenAa.Mode(false, false, false, "fxaa"));
    }

    [Fact]
    public void UltraIsAFinerFilterAndAtlasAndBlendsTheSplits()
    {
        Assert.Equal(RenderingServer.ShadowQuality.SoftLow, ShadowUltra.Filter(false));
        Assert.Equal(4096, ShadowUltra.Atlas(false));
        Assert.False(ShadowUltra.Blend(false));
        Assert.True(ShadowUltra.Filter(true) > RenderingServer.ShadowQuality.SoftLow);
        Assert.Equal(8192, ShadowUltra.Atlas(true));
        Assert.True(ShadowUltra.Blend(true));
        Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.ShadowDetail, false));
    }
}
