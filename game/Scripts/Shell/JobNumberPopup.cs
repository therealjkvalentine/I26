using Godot;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// RANDOM JOB's job number (the port's; docs/remix-missions.md "The director"): asked when ENTER AREA starts a rolled job, on the
/// PASSWORD popup's art and with its typing (PasswordPopup): digits, Backspace, Enter or OK to go, Esc to stay on the form.
/// Left blank, a new job is rolled; a number replays that job exactly (the job's number is its seed). Up and Down step through
/// the last ten jobs rolled (RemixScenarios.History), newest first.
/// </summary>
public sealed class JobNumberPopup : ShellScreen
{
    public required ShellFont Font { get; init; }
    /// <summary>The job number typed (null: a new one), or not called at all on Esc.</summary>
    public required Action<int?> Picked { get; init; }
    public required Action Cancelled { get; init; }
    public IReadOnlyList<int> Recent { get; init; } = [];
    /// <summary>What the number is called ("Job number"; a roguetrip's "Run number").</summary>
    public string Noun { get; init; } = "Job number";
    public override bool EscapeOpensOptions => false;
    public override bool TextEditing => true;
    private static readonly Rect2I Ok = new(119, 61, 42, 16);
    private const int X = 179, Y = 194, MaxDigits = 9;
    private string _text = "";
    private int _recent = -1;

    public override void Enter() => Draw();

    private void Draw()
    {
        var s = Shell.Db.Bank(0x24)[17];
        Frame.Restore(X, Y, 300, 110);
        Frame.Blit(s, X, Y);
        int w = s?.Width ?? 282, h = s?.Height ?? 93;
        Frame.Fill(X + 6, Y + h, w, 6, 0x0f);
        Frame.Fill(X + w, Y + 6, 6, h, 0x0f);
        string ask = _text.Length == 0 ? (Recent.Count > 0 ? $"{Noun}: blank for new, Up for recent" : $"{Noun}: blank for a new one") : Noun;
        Frame.Text(Font, X + w / 2, Y + 14, "~" + AddressBookPopup.Fit(Font, ask, w - 24));
        Frame.Text(Font, X + w / 2, Y + 36, "~" + (_text.Length == 0 ? "NEW_" : _text + "_"));
    }

    public override IReadOnlyList<PadRegion> PadRegions => [PadRegion.Of(new Rect2I(X + Ok.Position.X, Y + Ok.Position.Y, Ok.Size))];

    public override void Tick()
    {
        if (Shell.Keys.Contains(Key.Escape)) { Close(); Cancelled(); return; }
        string was = _text;
        foreach (char c in Shell.Typed) if (char.IsAsciiDigit(c) && _text.Length < MaxDigits) _text += c;
        if (Shell.Keys.Contains(Key.Backspace) && _text.Length > 0) _text = _text[..^1];
        if (Recent.Count > 0 && (Shell.Keys.Contains(Key.Up) || Shell.Keys.Contains(Key.Down)))
        {
            _recent = Math.Clamp(_recent + (Shell.Keys.Contains(Key.Up) ? 1 : -1), -1, Recent.Count - 1);
            _text = _recent < 0 ? "" : Recent[_recent].ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (Shell.Keys.Contains(Key.Enter) || Shell.Keys.Contains(Key.KpEnter)
            || Shell.Clicked && new Rect2I(X + Ok.Position.X, Y + Ok.Position.Y, Ok.Size).HasPoint(Shell.Mouse))
        {
            Close();
            Picked(int.TryParse(_text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null);
            return;
        }
        if (_text != was) Draw();
    }

    private void Close()
    {
        Shell.Sound(82);
        Frame.Restore(X, Y, 300, 110);
        Shell.Modal(null);
    }
}
