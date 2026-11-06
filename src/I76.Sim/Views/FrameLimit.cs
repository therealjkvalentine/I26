namespace I76.Sim.Views;

/// <summary>
/// The port's frame rate limiter (Graphic Detail's Max FPS): a cap on how often the window draws, nothing more. The simulation
/// steps on its own clock (Play Options' Simulation Rate) and the cars are drawn interpolated between its ticks, so a cap below
/// the tick rate leaves the sim running at its rate with several ticks to a frame (docs/frame-rate.md, "The frame rate limiter").
/// </summary>
public static class FrameLimit
{
    /// <summary>The caps offered; 0 is Unlimited.</summary>
    public static readonly int[] Caps = [30, 60, 75, 120, 144, 165, 240, 0];
    public static readonly string[] Names = ["30", "60", "75", "120", "144", "165", "240", "Unlimited"];
    public const int Unlimited = 7;

    /// <summary>The cap, in frames a second, a choice stands for (0: none).</summary>
    public static int Fps(int choice) => Caps[System.Math.Clamp(choice, 0, Caps.Length - 1)];

    /// <summary>
    /// The choice for a shell.cfg [port] max_fps value (frames a second, 0 for none). A missing key or a value that is not one of the
    /// caps (a hand edit) keeps today's behaviour, no cap.
    /// </summary>
    public static int Parse(int? fps) => fps is { } f && System.Array.IndexOf(Caps, f) is var i and >= 0 ? i : Unlimited;

    /// <summary>How the window hands its frames to the display.</summary>
    public enum Swap { Immediate, Fifo, Mailbox }

    /// <summary>
    /// The swap mode for Graphic Detail's VSync and [port] low_latency_present: VSync off draws at once (Immediate); VSync on queues each
    /// frame for a refresh in turn (Fifo), or, with Low Latency Present, shows the newest finished frame at each refresh and drops the
    /// older (Mailbox: no tearing, and a frame drawn just before the refresh is not kept waiting behind the queue; docs/1997-parity.md,
    /// round 3). Where the driver has no mailbox Godot falls back to Fifo.
    /// </summary>
    public static Swap SwapFor(bool vsync, bool lowLatency) => !vsync ? Swap.Immediate : lowLatency ? Swap.Mailbox : Swap.Fifo;
}
