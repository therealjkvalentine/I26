using I76.Game.Shell;
using I76.Game.Sim;
using I76.Game.Ui;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Round 3's control-latency keys ([port] low_latency_present, draw_ahead; docs/1997-parity.md): their defaults, the swap mode, the Developer panel's toggles.</summary>
[Collection("OriginalBehavior")]   // OriginalBehavior.Apply sets a global: not alongside the other settings tests
public class LatencyKeysTests
{
    [Fact]
    public void DrawAheadIsOffByDefaultAndFollowsTheSetting()
    {
        var s = new ShellSettings();
        Assert.False(s.DrawAhead);
        OriginalBehavior.Apply(s);
        Assert.False(OriginalBehavior.DrawAhead);
        OriginalBehavior.Apply(new ShellSettings { DrawAhead = true });
        Assert.True(OriginalBehavior.DrawAhead);
        OriginalBehavior.Apply(new ShellSettings());
    }

    [Fact]
    public void TheDeveloperPanelTogglesDrawAhead()
    {
        var e = Assert.Single(DevSettings.All.Concat(DevSettings.WithRow), d => d.Key == "draw_ahead");
        var s = new ShellSettings();
        Assert.Equal(0, e.Get(s));
        e.Set(s, 1);
        Assert.True(s.DrawAhead);
        Assert.Equal(["false", "true*"], e.Values);
    }

    [Theory]
    [InlineData(true, true, FrameLimit.Swap.Mailbox)]
    [InlineData(true, false, FrameLimit.Swap.Fifo)]
    [InlineData(false, true, FrameLimit.Swap.Immediate)]   // VSync off is off whatever the key says
    [InlineData(false, false, FrameLimit.Swap.Immediate)]
    public void LowLatencyPresentIsTheMailboxSwapUnderVSync(bool vsync, bool lowLatency, FrameLimit.Swap want) =>
        Assert.Equal(want, FrameLimit.SwapFor(vsync, lowLatency));

    [Fact]
    public void LowLatencyPresentIsOnByDefaultAndTheDeveloperPanelTogglesIt()
    {
        var s = new ShellSettings();
        Assert.True(s.LowLatencyPresent);
        var e = Assert.Single(DevSettings.All.Concat(DevSettings.WithRow), d => d.Key == "low_latency_present");
        Assert.Equal(1, e.Get(s));
        e.Set(s, 0);
        Assert.False(s.LowLatencyPresent);
    }

    [Fact]
    public void LowLatencyAudioIsOnByDefaultAndOffWritesTheStockLatencyForTheNextStart()
    {
        var s = new ShellSettings();
        Assert.True(s.LowLatencyAudio);
        Assert.Equal(240, I76.Game.Boot.AudioLatency.PhysicsFor(true));
        Assert.Equal(60, I76.Game.Boot.AudioLatency.PhysicsFor(false));
        Assert.Equal("", I76.Game.Boot.StartupSettings.Text(false, I76.Game.Boot.RendererChoice.Auto, true));
        Assert.Equal("[audio]\n\ndriver/output_latency=15\n", I76.Game.Boot.StartupSettings.Text(false, I76.Game.Boot.RendererChoice.Auto, true, lowLatencyAudio: false));
        Assert.Equal("[audio]\n\ndriver/enable_input=true\ndriver/output_latency=15\n", I76.Game.Boot.StartupSettings.Text(false, I76.Game.Boot.RendererChoice.Auto, true, micInput: true, lowLatencyAudio: false));
        var e = Assert.Single(DevSettings.All.Concat(DevSettings.WithRow), d => d.Key == "low_latency_audio");
        Assert.Equal(DevWhen.NextStart, e.When);
    }

    [Fact]
    public void ProjectGodotAsksForTheShortAudioBuffer()
    {
        string dir = System.AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "project.godot"))) dir = Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        Assert.Contains($"driver/output_latency={I76.Game.Boot.AudioLatency.Ms}", File.ReadAllText(Path.Combine(dir!, "game", "project.godot")));
    }
}
