using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The port's pistol keys: their glances, their default keys and the file-only game keys that free [ and ] (no install needed).</summary>
public class PistolKeysTests
{
    // the original's gears on , and . (KEYBOARD.MAP's), and a glance
    private const string Stock = "# Interstate '76 Input mapping file\n"
        + "shift_up {\n   + keyboard   Period\n\t- Keyboard\tShift\n\t- Keyboard\tControl\n}\n"
        + "shift_down {\n   + keyboard   Comma\n\t- Keyboard\tShift\n\t- Keyboard\tControl\n}\n"
        + "pilot_glance_left {\n   + keyboard   GreyLeftArrow\n}\n";

    [Fact]
    public void EachPistolKeyHoldsItsSideGlance()
    {
        Assert.Equal(PistolKeys.Left, PistolKeys.Of("pilot_glance_left"));
        Assert.Equal(PistolKeys.Right, PistolKeys.Of("PILOT_GLANCE_RIGHT"));
        Assert.Null(PistolKeys.Of("pilot_glance_up"));
        Assert.Null(PistolKeys.Of("pilot_glance_down"));
    }

    [Fact]
    public void TheGearsKeepCommaAndPeriodInTheOriginalsMap()
    {
        var t = BindingTable.Parse(Stock).WithPistolKeys();
        Assert.Empty(t.Of(PistolKeys.Left));
        Assert.Empty(t.Of(PistolKeys.Right));
        Assert.Equal("Comma", t.KeyText("shift_down"));
    }

    [Fact]
    public void FreeKeysTakeThePistolsWithTheShellsExclusions()
    {
        var t = BindingTable.Parse(Stock.Replace("Period", "RightBracket").Replace("Comma", "LeftBracket")).WithPistolKeys();
        Assert.Equal("Comma", t.KeyText(PistolKeys.Left));
        Assert.Equal("Period", t.KeyText(PistolKeys.Right));
        var b = Assert.Single(t.Of(PistolKeys.Left));
        Assert.Equal(["+keyboard Comma", "-Keyboard Shift", "-Keyboard Control"], b.Terms.Select(x => $"{(x.Plus ? '+' : '-')}{x.Device} {x.Channel}"));
        // idempotent, and the text round-trips
        Assert.Equal(t.ToText(), BindingTable.Parse(t.ToText()).WithPistolKeys().ToText());
    }

    [Fact]
    public void APlayersOwnPistolBindingStays()
    {
        var map = Stock.Replace("Comma", "LeftBracket") + "pistol_left {\n   + keyboard   Z\n}\n";
        var t = BindingTable.Parse(map).WithPistolKeys();
        Assert.Equal("Z", t.KeyText(PistolKeys.Left));
        Assert.Equal(BindingTable.None, t.KeyText(PistolKeys.Right));   // . is still the gear's
    }

    [Fact]
    public void ThePistolKeysAreOnTheKeySheet()
    {
        var rows = KeySheet.Groups.Single(g => g.Group == "Weapons").Rows;
        Assert.Contains((PistolKeys.Left, "Pistol Left"), rows);
        Assert.Contains((PistolKeys.Right, "Pistol Right"), rows);
    }

    [Fact]
    public void TheBracketGameKeysMoveByThePortsFile()
    {
        var m = GameKeyMap.Load(GameKeyMap.Shipped, new Dictionary<int, string> { [0x5d] = "-", [0xc] = "=" });
        Assert.Equal((int)GameKey.Toggle2DCockpitExt, m.Lookup('-'));
        Assert.Equal((int)GameKey.ToggleGhostCockpit, m.Lookup('='));
        Assert.Equal(0, m.Lookup('['));
        Assert.Equal(0, m.Lookup(']'));
        // the ghost cockpit does nothing in the port: only the outside instruments' key is the file's to move
        Assert.Equal([0x5d], GameKeyMap.FileOnly.Select(f => f.Id));
        // without the file they stay on the install's keys
        var stock = GameKeyMap.Load(GameKeyMap.Shipped);
        Assert.Equal((int)GameKey.Toggle2DCockpitExt, stock.Lookup('['));
        Assert.Equal((int)GameKey.ToggleGhostCockpit, stock.Lookup(']'));
    }
}
