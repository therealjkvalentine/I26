using Godot;
using I76.Formats.Input;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The trip's bookmarks ("Bookmarks"): what
/// savegame.dir lists for each — the scene (mission) number, the name, the
/// file stem save%03d, the game state at saving (1 = between a won mission
/// and the next) and whether salvage management was off. Kept in
/// user://saves/bookmarks.cfg. The car and its inventory (the .cmp body) join
/// with the garage. The port's checkpoint bookmarks (<see cref="CheckpointBookmarks"/>)
/// keep a mission's checkpoint beside them.
/// </summary>
public sealed class Bookmarks
{
    private const string Path = "user://saves/bookmarks.cfg";

    /// <param name="Checkpoint">The port's: saved in the mission with its checkpoint (bookmark_saveNNN.snap); Load resumes there.</param>
    /// <param name="Lap">The play fork's Trip+ lap the bookmark was saved on (bookmarks.cfg "lap", written past lap 1); Load plays on by that lap's rules.</param>
    public sealed record Entry(int Mission, string Name, string Stem, int State, bool NoSalvage, bool Checkpoint = false, int Lap = 1)
    {
        /// <summary>"Scene %d. " — one on when the entry was saved after a win.</summary>
        public int Scene => Mission + (State == 1 ? 1 : 0);

        /// <summary>What the list shows after "Scene N. " (<see cref="Title"/>).</summary>
        public string Shown => Title(Scene, Name, Checkpoint);

        /// <summary>
        /// <see cref="Shown"/> after <paramref name="lead"/>, cut with ".." to <paramref name="room"/> pixels as the garage's lists cut
        /// theirs; a checkpoint bookmark's " - Checkpoint" is kept whole and its title cut, so the row still says what it is.
        /// </summary>
        public string Fitted(string lead, Func<string, int> measure, int room)
        {
            const string tail = " - " + CheckpointBookmarks.Name;
            string shown = Shown;
            if (!Checkpoint || !shown.EndsWith(tail, StringComparison.Ordinal) || measure(lead + shown) <= room) return Fit(lead + shown, measure, room);
            string title = shown[..^tail.Length];
            return Fit(lead + title, measure, room - measure(tail)) + tail;
        }
    }

    /// <summary>
    /// A bookmark's words after "Scene N. " (the port's, docs/deviations.md "Bookmark names"): a name the player typed, as typed; one
    /// saved with no name (the original writes an empty name too, and its board shows only "Scene 5. ") by the title of the scene it
    /// plays, "Save Taurus", as is one All Scenes made under a scene's former name ("The Bar-D" reads "Bar-D"); a checkpoint bookmark (saved as "Checkpoint") "Save Taurus - Checkpoint". Worked out when the list is
    /// drawn, so a bookmark saved by any build (or imported from the original) reads so with nothing rewritten. A scene past the
    /// trip's keeps its name as it is.
    /// </summary>
    public static string Title(int scene, string name, bool checkpoint = false)
    {
        bool known = scene >= 1 && scene <= I76.Formats.Mission.MissionTitles.SceneNames.Length;
        if (!known) return name;
        string title = I76.Formats.Mission.MissionTitles.SceneName(scene);
        // no name, or the name All Scenes gave the scene in an earlier build ("The Bar-D")
        if (string.IsNullOrWhiteSpace(name) || name == I76.Formats.Mission.MissionTitles.FormerSceneName(scene)) return title;
        if (checkpoint && name == CheckpointBookmarks.Name) return $"{title} - {CheckpointBookmarks.Name}";
        return name;
    }

    /// <summary>The list row, "Scene 5. Save Taurus", cut to <paramref name="room"/> pixels (<see cref="Entry.Fitted"/>).</summary>
    public static string Row(Entry e, Func<string, int> measure, int room) => e.Fitted($"Scene {e.Scene}. ", measure, room);

    /// <summary>The text, or its longest start with ".." that fits <paramref name="room"/>.</summary>
    public static string Fit(string text, Func<string, int> measure, int room)
    {
        if (measure(text) <= room) return text;
        while (text.Length > 1 && measure(text + "..") > room) text = text[..^1];
        return text.TrimEnd() + "..";
    }

    public List<Entry> Entries { get; } = [];

    public static Bookmarks Load()
    {
        var b = new Bookmarks();
        var c = new ConfigFile();
        if (c.Load(Path) != Error.Ok) return b;
        foreach (var sec in c.GetSections())
            b.Entries.Add(new Entry((int)c.GetValue(sec, "mission", 1), (string)c.GetValue(sec, "name", ""), sec,
                (int)c.GetValue(sec, "state", 0), (bool)c.GetValue(sec, "nosalvage", false), (bool)c.GetValue(sec, "checkpoint", false), (int)c.GetValue(sec, "lap", 1)));
        b.Entries.Sort((x, y) => string.CompareOrdinal(x.Stem, y.Stem));
        return b;
    }

    public void Save()
    {
        var c = new ConfigFile();
        foreach (var e in Entries)
        {
            c.SetValue(e.Stem, "mission", e.Mission);
            c.SetValue(e.Stem, "name", e.Name);
            c.SetValue(e.Stem, "state", e.State);
            c.SetValue(e.Stem, "nosalvage", e.NoSalvage);
            if (e.Checkpoint) c.SetValue(e.Stem, "checkpoint", true);
            if (e.Lap > 1) c.SetValue(e.Stem, "lap", e.Lap);
        }
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        c.Save(Path);
    }

    /// <summary>The car and inventory a bookmark keeps (the .cmp body): user://saves/bookmark_save000.json.</summary>
    private static string CarPath(string stem) => $"user://saves/bookmark_{stem}.json";

    /// <summary>The kept car's text, or null.</summary>
    public static string? CarJson(string stem)
    {
        using var f = Godot.FileAccess.Open(CarPath(stem), Godot.FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    public static void WriteCar(string stem, string json)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        using var f = Godot.FileAccess.Open(CarPath(stem), Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(json);
    }

    /// <summary>The kept car, or null for a bookmark with none (one saved before the car was kept).</summary>
    public static I76.Sim.Trip.TripInventory? ReadCar(string stem, I76.Sim.Trip.PartsDatabase db, I76.Formats.Vfs.AssetVfs vfs)
    {
        using var f = Godot.FileAccess.Open(CarPath(stem), Godot.FileAccess.ModeFlags.Read);
        return f is null ? null : I76.Sim.Trip.TripInventory.Load(f.GetAsText(), db, vfs);
    }

    /// <summary>
    /// Drops the entry at <paramref name="index"/> and keeps the others in order with their stems. Returns the row to select
    /// afterwards: the one that moved up into its place, else the new last row, else -1.
    /// </summary>
    public int RemoveAt(int index)
    {
        Entries.RemoveAt(index);
        return Entries.Count == 0 ? -1 : Math.Min(index, Entries.Count - 1);
    }

    /// <summary>Removes a bookmark's kept car from user://saves.</summary>
    public static void DeleteCar(string stem)
    {
        if (Godot.FileAccess.FileExists(CarPath(stem))) DirAccess.RemoveAbsolute(CarPath(stem));
    }

    /// <summary>A checkpoint bookmark's checkpoint (<see cref="CheckpointBookmarks"/>): user://saves/bookmark_save000.snap.</summary>
    private static string CheckpointPath(string stem) => $"user://saves/bookmark_{stem}.snap";

    public static void WriteCheckpoint(string stem, byte[] data)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        using var f = Godot.FileAccess.Open(CheckpointPath(stem), Godot.FileAccess.ModeFlags.Write);
        f?.StoreBuffer(data);
    }

    /// <summary>The kept checkpoint's bytes, or null.</summary>
    public static byte[]? ReadCheckpoint(string stem)
    {
        using var f = Godot.FileAccess.Open(CheckpointPath(stem), Godot.FileAccess.ModeFlags.Read);
        return f?.GetBuffer((long)f.GetLength());
    }

    public static void DeleteCheckpoint(string stem)
    {
        if (Godot.FileAccess.FileExists(CheckpointPath(stem))) DirAccess.RemoveAbsolute(CheckpointPath(stem));
    }

    /// <summary>A new entry's stem: save%03d with the number one past the largest (parsed back from the stem).</summary>
    public string NextStem() => $"save{(Entries.Count == 0 ? 0 : Entries.Max(e => int.TryParse(e.Stem.AsSpan(4), out int n) ? n : 0) + 1):000}";
}

/// <summary>
/// The Save and Load Bookmark overlays' shared frame:
/// "Scene %d. " at (186, 132) and the name at (275, 132) in font 0x46; a
/// ten-row list from y 184 (rows font 0x45's height + 8) with scroll arrows;
/// the action and Cancel at (328, 420), Cancel 25 px after the first.
/// </summary>
public abstract class BookmarkScreen : OverlayScreen
{
    /// <summary>Opened by a screen's own button (the main menu's LOAD BOOKMARK, the trip screens' SAVE BOOKMARK) rather than the Options Menu: Cancel closes it onto that screen. Escape still goes to the Options Menu.</summary>
    public bool Direct { get; init; }
    protected Bookmarks Marks = null!;
    protected int Top, Picked = -1;
    protected ShellFont Rows = null!;
    protected int RowH => Rows.Height + 8;
    protected abstract string Action { get; }
    /// <summary>What an empty list says, or nothing.</summary>
    protected virtual string? EmptyText => null;
    /// <summary>The rows: the kept bookmarks.</summary>
    protected virtual Bookmarks List() => Bookmarks.Load();

    protected override void Build()
    {
        Marks = List();
        Rows = Shell.Db.Font(0x45);
        int done = Small.Measure(Action);
        Add(328, 420, Action, DoAction, Small);
        Add(328 + done + 25, 420, "Cancel", Cancel, Small);   // x = 0x148 + width + 0x19
        Add(201, 158, () => "", () => Top = Math.Max(0, Top - 1), w: 20, h: 18).Repeats = true;                                  // scroll up
        Add(201, 413, () => "", () => Top = Math.Min(Math.Max(0, Marks.Entries.Count - 10), Top + 1), w: 20, h: 17).Repeats = true;   // scroll down
    }

    protected abstract void DoAction();

    protected virtual void Cancel()
    {
        if (Direct) { Shell.Overlay(null); return; }
        Shell.Overlay(new OptionsScreen());
    }

    protected override void Escape() => Shell.Overlay(new OptionsScreen());

    protected override void ClickElsewhere(Vector2I m)
    {
        if (m.X <= 185 || m.X >= 452 || m.Y <= 179 || m.Y >= 408) return;
        int row = Top + (m.Y - 184) / RowH;
        if (row < 0 || row >= Marks.Entries.Count) return;
        Pick(row);
        Redraw();
    }

    /// <summary>
    /// The port's Delete (Save and Load Bookmark alike: the trip screens' SAVE BOOKMARK button is where a full list is
    /// met): asks first; Yes removes the picked bookmark's entry and its kept car and selects the row that took its place.
    /// </summary>
    protected void Delete()
    {
        if (Picked < 0 || Picked >= Marks.Entries.Count) return;
        int row = Picked;
        Shell.Modal(new ConfirmPopup
        {
            Lines = ["Delete this bookmark?"],
            Answer = yes =>
            {
                if (!yes || row >= Marks.Entries.Count) return;
                Bookmarks.DeleteCar(Marks.Entries[row].Stem);
                Bookmarks.DeleteCheckpoint(Marks.Entries[row].Stem);
                Picked = Marks.RemoveAt(row);
                Marks.Save();
                Top = Math.Clamp(Top, 0, Math.Max(0, Marks.Entries.Count - 10));
                Redraw();
            },
        });
    }

    protected virtual void Pick(int row) => Picked = row;

    /// <summary>
    /// Where Delete goes on the sheet (both looks share the art), Save and Load Bookmark alike, base game and Nitro: on the
    /// buttons' line at the foot, under the rows, left of the action with Cancel's 25 px between (to the right of Cancel it
    /// would run off the sheet). The pad's Down from the list lands on the action, the nearer.
    /// </summary>
    public static (int X, int Y) DeletePlace(Func<string, int> measure) => (328 - 25 - measure("Delete"), 420);

    /// <summary>The Delete button, where <see cref="DeletePlace"/> puts it.</summary>
    protected void AddDelete()
    {
        var (x, y) = DeletePlace(Small.Measure);
        Add(x, y, "Delete", Delete, Small);
    }

    protected override bool Wheeled(int rows)
    {
        int to = Math.Clamp(Top + rows, 0, Math.Max(0, Marks.Entries.Count - 10));
        if (to == Top) return false;
        Top = to;
        return true;
    }

    private bool Scroll(int by)
    {
        int to = Math.Clamp(Top + by, 0, Math.Max(0, Marks.Entries.Count - 10));
        if (to == Top) return false;
        Top = to;
        Redraw();
        return true;
    }

    /// <summary>The buttons and the visible rows of the list (a press past the top or bottom row scrolls it).</summary>
    public override IReadOnlyList<PadRegion> PadRegions
    {
        get
        {
            var l = base.PadRegions.ToList();
            int shown = Math.Min(10, Marks.Entries.Count - Top);
            for (int i = 0; i < shown; i++)
            {
                int k = i;
                l.Add(PadRegion.Of(190, 184 + i * RowH, 262, RowH, dir => dir == NavDir.Up && k == 0 ? Scroll(-1) : dir == NavDir.Down && k == 9 ? Scroll(1) : false));
            }
            return l;
        }
    }

    protected abstract (int Scene, string Name) Header();

    protected override void DrawExtra()
    {
        if (Marks.Entries.Count == 0 && EmptyText is { } empty) Frame.Text(Label, 228, 252, empty, ShellFrame.OverlayInk);
        var (scene, name) = Header();
        Frame.Text(Label, 186, 132, $"Scene {scene}. ", ShellFrame.OverlayInk);
        Frame.Text(Label, 275, 132, Bookmarks.Fit(name, Label.Measure, HeaderRoom), ShellFrame.OverlayInk);
        for (int i = 0; i < 10 && Top + i < Marks.Entries.Count; i++)
            Frame.Text(Rows, 190, 184 + i * RowH, Bookmarks.Row(Marks.Entries[Top + i], Rows.Measure, RowRoom), ShellFrame.OverlayInk);
    }

    /// <summary>The header name's room: the name field's 190 px (a typed name stays under it with its cursor).</summary>
    public const int HeaderRoom = 190;
    /// <summary>A row's room: from its x 190 to the list's right edge at 452, less the 4 px the garage's lists keep.</summary>
    public const int RowRoom = 452 - 190 - 4;

    /// <summary>A picked row's header: its scene and the words after it, as the row gives them.</summary>
    protected (int, string) PickedHeader() =>
        Picked >= 0 && Picked < Marks.Entries.Count ? (Marks.Entries[Picked].Scene, Marks.Entries[Picked].Fitted("", Label.Measure, PickedRoom)) : (Shell.Trip.Mission, "");

    /// <summary>A picked row's header room: from the name's x 275 to the field's right edge at 452, less 4 px.</summary>
    public const int PickedRoom = 452 - 275 - 4;
}

/// <summary>Save Bookmark, overlay 1 (record 11): name the bookmark, pick a row to overwrite, Save.</summary>
public sealed class SaveBookmarkScreen : BookmarkScreen
{
    protected override int Record => 11;
    protected override string Action => "Save";
    private string _name = "";
    private bool _editing = true;       // the name is typed as soon as the form is up

    protected override void Build()
    {
        base.Build();
        Add(183, 127, () => "", () => _editing = true, w: 271, h: 23);      // the name field, 32 characters
        AddDelete();
    }

    protected override (int, string) Header() => (Shell.Trip.Mission, _name + (_editing ? "_" : ""));

    public override bool TextEditing => _editing;
    public override void StopEditing() { _editing = false; Redraw(); }

    protected override void Pick(int row)
    {
        base.Pick(row);
        _name = Marks.Entries[row].Name;
    }

    public override void Tick()
    {
        if (_editing)
        {
            bool changed = false;
            // '~' and characters the font lacks are refused; name + '_' stays under 190 px
            foreach (char c in Shell.Typed) if (_name.Length < 32 && c != '~' && Label.Glyph(c) is not null && Label.Measure(_name + c + '_') < 190) { _name += c; changed = true; }
            if (Shell.Keys.Contains(Key.Backspace) && _name.Length > 0) { _name = _name[..^1]; changed = true; }
            // the first Esc only ends the edit (a second opens Options); Enter and Tab save, as the original's name field does
            // (the edit returns 1 and the bookmark is written)
            if (Shell.Keys.Contains(Key.Escape)) { _editing = false; Redraw(); return; }
            if (Shell.Keys.Contains(Key.Enter) || Shell.Keys.Contains(Key.KpEnter) || Shell.Keys.Contains(Key.Tab)) { _editing = false; Redraw(); DoAction(); return; }
            if (changed) Redraw();
        }
        base.Tick();
    }

    /// <summary>Save: written as named, an empty name too, as the original does.</summary>
    protected override void DoAction()
    {
        int mission = Shell.Trip.Mission;
        var same = Picked >= 0 ? Picked : Marks.Entries.FindIndex(e => e.Mission == mission && e.Name == _name);
        if (same < 0) { Write(Marks.NextStem()); return; }
        Shell.Modal(new ConfirmPopup
        {
            Lines = ["Overwrite an existing bookmark?"],
            Answer = yes => { if (yes) Write(Marks.Entries[same].Stem); else { _editing = true; Redraw(); } },   // No: back to editing the name
        });
    }

    private void Write(string stem)
    {
        Marks.Entries.RemoveAll(e => e.Stem == stem);
        Marks.Entries.Add(new Bookmarks.Entry(Shell.Trip.Mission, _name, stem, 0, (Shell.Settings.PlayOptions & 2) != 0, Lap: Shell.Trip.Plus.Lap));
        Marks.Entries.Sort((x, y) => string.CompareOrdinal(x.Stem, y.Stem));
        // the .cmp: the car, its parts (the garage's current lists, the field salvage still waiting) and its repair order
        if (Shell.Trip.Inventory(Shell) is { } inv) Bookmarks.WriteCar(stem, inv.Save());
        Bookmarks.DeleteCheckpoint(stem);   // a checkpoint bookmark written over is an ordinary one now
        Marks.Save();
        Shell.Overlay(null);    // Back to the screen underneath; only Cancel and Esc return to Options
    }
}

/// <summary>
/// Load Bookmark, overlay 11 (record 12): pick a row, Load resumes the trip
/// through the shell's post-mission routing — mission 17 to CHOOSE YOUR VEHICLE, salvage
/// management off to the record-29 notice, mission 13 to YOU ARE UNCONSCIOUS,
/// otherwise field salvage (the router's own branches).
/// </summary>
public sealed class LoadBookmarkScreen : BookmarkScreen
{
    protected override int Record => 12;
    protected override string Action => "Load";
    protected override string? EmptyText => "(No Games available)";

    /// <summary>
    /// The original's sheet plus the port's Delete. The port's Import Saves and All Scenes are on the shell's Developer
    /// panel (<see cref="ShellDevPanel"/>).
    /// </summary>
    protected override void Build()
    {
        base.Build();
        AddDelete();
    }

    protected override (int, string) Header() => PickedHeader();

    protected override void DoAction()
    {
        if (Picked < 0 || Picked >= Marks.Entries.Count) return;
        var e = Marks.Entries[Picked];
        Shell.Trip.Mission = e.Scene;
        Shell.Trip.Highest = Math.Max(Shell.Trip.Highest, e.Scene);
        Shell.Trip.FromBookmark = true;
        if (e.Lap != Shell.Trip.Plus.Lap) Shell.Trip.Plus = Shell.Trip.Plus.OnLap(e.Lap);   // the bookmark's Trip+ lap and its rules
        // the saved car and parts replace the trip's (a bookmark from before they were kept leaves them as they are)
        var car = Bookmarks.ReadCar(e.Stem, Shell.Parts, Shell.Vfs);
        if (car is not null) Shell.Trip.SetInventory(car);
        Shell.Trip.Save();
        if (e.NoSalvage) Shell.Settings.PlayOptions |= 2; else Shell.Settings.PlayOptions &= ~2;
        Shell.Settings.Save();      // I76PLYR.DEF is written at once
        Shell.Overlay(null);
        // a checkpoint bookmark: its mission at once, in its car, as the trip launches a mission, resuming at the checkpoint
        // (the game drops a checkpoint it cannot use, and the mission starts from its beginning)
        if (e.Checkpoint && car is not null && Bookmarks.ReadCheckpoint(e.Stem) is { } checkpoint)
        {
            Shell.LaunchTrip(car, checkpoint);
            return;
        }
        Shell.Go(new TripRouterScreen());
    }
}

/// <summary>
/// The port's Import Saves (from the shell's Developer panel): the original game's bookmarks (savegame.dir and its saveNNN.cmp files in
/// the install, or in its VirtualStore copy on Windows) listed as the Load board lists them; Import makes an I26
/// bookmark of each (<see cref="OriginalSaveImport"/>), leaving out one already imported, and goes back to Load Bookmark.
/// The original's files are only read.
/// </summary>
public sealed class ImportSavesScreen : BookmarkScreen
{
    protected override int Record => 12;
    protected override string Action => "Import";
    protected override string? EmptyText => "(None in the game folder)";
    private readonly List<(I76.Formats.Save.OriginalSaveEntry Entry, I76.Formats.Save.OriginalSave? Save, string? Error)> _found = [];

    protected override Bookmarks List()
    {
        var local = OperatingSystem.IsWindows() ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) : null;
        // Windows reads an old game's files from its VirtualStore copy when there is one: that folder's rows first, a file listed once
        var folders = I76.Formats.Save.OriginalSaveFolders.Find(Shell.Install.Root, local);
        folders.Reverse();
        foreach (var folder in folders)
        {
            try
            {
                foreach (var r in I76.Formats.Save.OriginalSaveFolders.Read(folder))
                    if (!_found.Any(f => f.Entry.File.Equals(r.Entry.File, StringComparison.OrdinalIgnoreCase))) _found.Add(r);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { GD.Print($"import saves: {folder}: {ex.Message}"); }
        }
        var b = new Bookmarks();
        foreach (var (e, _, _) in _found) b.Entries.Add(new Bookmarks.Entry(e.Scene, e.Name, e.File, e.State, false));
        return b;
    }

    protected override (int, string) Header() => PickedHeader();

    /// <summary>Cancel and Esc go back to Load Bookmark.</summary>
    private void Back() => Shell.Overlay(new LoadBookmarkScreen { Direct = Direct });
    protected override void Cancel() => Back();
    protected override void Escape() => Back();

    protected override void DoAction()
    {
        if (_found.Count == 0) { Back(); return; }
        var marks = Bookmarks.Load();
        int made = 0, had = 0, failed = 0;
        foreach (var (e, save, err) in _found)
        {
            if (save is null || e.Plays is < 1 or > 17) { failed++; GD.Print($"import saves: {e.File}: {err ?? $"scene {e.Plays}"}"); continue; }
            ImportedSave imp;
            try { imp = OriginalSaveImport.Convert(e, save, Shell.Parts, Shell.Vfs); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { failed++; GD.Print($"import saves: {e.File}: {ex.Message}"); continue; }
            var json = imp.Inventory.Save();
            if (marks.Entries.Any(m => m.Mission == imp.Mission && m.Name == imp.Name && m.State == imp.State && Bookmarks.CarJson(m.Stem) == json)) { had++; continue; }
            foreach (var n in imp.Notes) GD.Print($"import saves: {e.File}: {n}");
            var stem = marks.NextStem();
            marks.Entries.Add(new Bookmarks.Entry(imp.Mission, imp.Name, stem, imp.State, imp.NoSalvage));
            Bookmarks.WriteCar(stem, json);
            made++;
        }
        marks.Save();
        var text = $"{made} bookmark{(made == 1 ? "" : "s")} imported." + (had > 0 ? $" {had} already were." : "") + (failed > 0 ? $" {failed} could not be read." : "");
        Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = text, Closed = Back });
    }
}
