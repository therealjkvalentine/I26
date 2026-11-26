namespace I76.Game.Shell;

/// <summary>
/// New Game Plus (the play fork's; internally Trip+, docs/deviations.md "New Game Plus"): the offer made when the trip is won, and
/// the bookmark it saves. The offer comes once, on the menu the credits return to after a T17 win; YES writes a bookmark that
/// starts Scene 1 on the next lap in the trip car as it stands, beside the others (the finished trip and every other bookmark
/// stay as they are); NO loses nothing: NEW TRIP still offers the lap. A win with the cheat marker set never reaches the shell
/// as a win (the cheater's end menu), so it is not recorded and makes no offer.
/// </summary>
public static class NewGamePlus
{
    /// <summary>The player-facing name.</summary>
    public const string Name = "NEW GAME PLUS";

    /// <summary>The menu's TRIP banner on a lap past the first.</summary>
    public static string Banner(int lap) => $"{Name} LAP {lap}";

    /// <summary>The offer's bookmark's name: Load Bookmark lists it as "Scene 1. New Game+ Lap 2", short enough that the row keeps the lap.</summary>
    public static string BookmarkName(int lap) => $"New Game+ Lap {lap}";

    /// <summary>The offer's bookmark: Scene 1 (not after a win), lap <paramref name="lap"/>, under <paramref name="stem"/>; its car is written beside it.</summary>
    public static Bookmarks.Entry Bookmark(int lap, string stem, bool noSalvage) => new(1, BookmarkName(lap), stem, 0, noSalvage, Lap: lap);

    /// <summary>
    /// The offer on the confirm receipt (about 24 letters a line): what it keeps and what changes, in the player's words.
    /// Lap 2's rules; a later lap has more of each.
    /// </summary>
    public static string[] OfferLines(int lap) =>
        ["YOU FINISHED THE TRIP!", $"SAVE NEW GAME PLUS LAP {lap}?", "KEEPS CAR, PARTS, STOCK.", "MORE ENEMIES, SHARPER", "DRIVERS, BETTER SALVAGE."];

    /// <summary>NEW TRIP's question once the trip is finished.</summary>
    public static string[] NewTripLines(int lap) => ["DRIVE THE TRIP AGAIN?", $"YES: NEW GAME PLUS LAP {lap},", "   YOUR CAR, TOUGHER ROAD.", "NO: START OVER."];

    /// <summary>What the notice after YES says.</summary>
    public static string SavedNotice(int lap) => $"SAVED IN LOAD BOOKMARK: SCENE 1. {BookmarkName(lap).ToUpperInvariant()}";

    /// <summary>
    /// Whether the shell, back from a trip mission, offers the New Game Plus save: after a win of T17 (the trip's mission number
    /// before the win), never after another result or mission. A cheater's win never comes back as <see cref="MissionResult.Won"/>.
    /// </summary>
    public static bool Offers(MissionResult result, int mission) => result == MissionResult.Won && mission >= 17;

    /// <summary>A setting or hook's value under its New Game Plus name, else its older Trip+ spelling (accepted silently).</summary>
    public static string? Env(string name, string oldName) => System.Environment.GetEnvironmentVariable(name) ?? System.Environment.GetEnvironmentVariable(oldName);
}
