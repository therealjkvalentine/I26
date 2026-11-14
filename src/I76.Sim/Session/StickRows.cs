namespace I76.Sim.Session;

/// <summary>
/// The car's throttle and steer for a tick when a stick is bound. The original reads the stick's two rows only while no driving key is held;
/// any of accelerate, brake, steer left or right held, the keys have both rows, and the stick's steering is dropped while the key is down.
/// The port's axis thresholds (docs/joystick.md) are not keys: a pedal set to hold Accelerate or Brake past a line of its travel takes the
/// throttle row alone, full that way, and the wheel keeps steering (#173: with the gas pedal on such a line, the car stopped steering while it
/// was pressed and the brake on the throttle axis did not).
/// </summary>
public static class StickRows
{
    public const CarButtons Driving = CarButtons.ThrottleUp | CarButtons.ThrottleDown | CarButtons.SteerLeft | CarButtons.SteerRight;

    /// <param name="held">Everything held this tick, the keys' and the axis rules' driving buttons together.</param>
    /// <param name="byRule">The driving buttons only the port's axis rules hold (no key or button also holds them).</param>
    /// <param name="stick">Whether a live stick channel feeds the throttle or steer row.</param>
    /// <param name="throttle">The stick's throttle row (+ accelerates), on the wire's scale.</param>
    /// <param name="steer">The stick's steer row (+ left).</param>
    public static CarInput Mix(CarButtons held, CarButtons byRule, bool stick, short throttle, short steer)
    {
        byRule &= Driving & held;
        var input = new CarInput { Buttons = held };
        if (!stick || (held & Driving & ~byRule) != 0) return input;   // no stick, or a key: the original's keys have both rows
        input.Buttons = (held & ~byRule) | CarButtons.Analog;
        input.Throttle = Row(byRule, CarButtons.ThrottleUp, CarButtons.ThrottleDown) ?? throttle;
        input.Steer = Row(byRule, CarButtons.SteerLeft, CarButtons.SteerRight) ?? steer;
        return input;
    }

    /// <summary>A row a rule holds: full one way, nothing with both ways held; null when no rule holds it.</summary>
    private static short? Row(CarButtons b, CarButtons plus, CarButtons minus) =>
        (b & (plus | minus)) == 0 ? null : (b & (plus | minus)) == (plus | minus) ? (short)0 : (b & plus) != 0 ? (short)32767 : (short)-32767;
}
