namespace I76.Game.Shell;

/// <summary>
/// The menus' tooltips other than Graphic Detail's rows (<see cref="GraphicsHelp"/>) and the host form's (<see cref="HostFormHelp"/>),
/// by the row's label as the menus print it, so the shell's sheets and the in-game pad share one text for the same setting (the
/// pad's labels where they differ from the sheet's are listed too). The port's: the original had no help. Every row of the Options
/// Menu, Play Options, Audio Control, Controls, Mouse Options and the Esc menu's pads has one; the tests hold the menus to it.
/// </summary>
public static class MenuTips
{
    /// <summary>The tooltip for a row's label, or null for one without (the buttons: Done, Cancel, Back).</summary>
    public static string? Of(string? label) => label is not null && Tips.TryGetValue(label.Trim(), out var t) ? t : null;

    private static readonly Dictionary<string, string> Tips = new()
    {
        // ---- the Options Menu (the shell's) and the Esc menu's first pad
        ["Play Options"] = "Play Options: physics, the cheats, difficulty, checkpoints and the port's game rules.",
        ["Graphic Detail"] = "Graphic Detail: resolution, detail, the look and the texture packs.",
        ["Audio Control"] = "Audio Control: the music, effects and voice levels, the output and the bass shaker.",
        ["Controls"] = "Controls: force feedback, the mouse, and the key map.",
        ["Load Bookmark"] = "Load Bookmark: go back to a saved point of the trip.",
        ["Save Bookmark"] = "Save Bookmark: save the trip as it is now.",
        ["Credits"] = "Credits: the original game's credits.",
        ["Switch Game"] = "Switch Game: back to the chooser, to play Interstate '76 or the Nitro Pack.",
        ["Exit Interstate '76"] = "Exit: quit to the desktop.",
        ["Exit Trip"] = "Exit Trip: leave the trip for the main menu (bookmarks are kept).",
        ["Abort Mission"] = "Abort Mission: replay, reconfigure the car, or leave the mission.",
        ["Developer Tools"] = "Developer Tools: the port's developer panel (teleport, god mode, state, signals and every setting without a row).",
        ["Report a Bug"] = "Report a Bug: saves a zip in the user folder with the build, your settings, a save state of this moment, the frame you saw and the log, to drag into Discord or a GitHub issue. Nothing personal goes in. Ctrl+F11 does the same in play.",
        ["Open Folder"] = "Open Folder: shows the bug-reports folder in your file browser, with the zip to drag.",
        ["Self Destruct (new car)"] = "Self Destruct: blow up your own car and come back in a new one; counts as a death, -500 points.",
        [I76.Net.Hud.NetMenu.Leave] = "Leave the network game for the main menu.",
        [I76.Net.Hud.NetMenu.Exit] = "Quit the network game and the program.",

        // ---- the Esc menu's other pads
        ["Replay"] = "Replay: start this mission again from its beginning.",
        ["Reconfigure"] = "Reconfigure: back to the garage to change the car, then start the mission again.",
        ["Exit Game"] = "Exit Game: quit to the desktop.",
        ["Retry from Checkpoint"] = "Retry from Checkpoint: back to the last checkpoint kept, with the car and the scene as they were.",
        ["Turn off Cheater Options"] = "Turn off the cheats (Unlimited Ammo, Armor and Chassis) so this win counts.",
        ["I'm a cheater"] = "Keep the cheats on: the trip will not advance.",
        ["I'm a swinger"] = "Back to Play Options to turn the cheats off.",

        // ---- Play Options: the original's rows
        ["Arcade Phys."] = "Arcade physics: the original's easier handling: more grip in turns, and a car that lands on its roof is turned back over instead of wrecked (not online).",
        ["No Salv. Manag."] = "No salvage management: no field salvage is collected from wrecks or left after a win, so there are no salvaged parts to fit.",
        ["Unlim. Ammo"] = "Unlimited ammo: weapons never run dry. A cheat: the trip will not advance.",
        ["Unlim. Armor"] = "Unlimited armor: your armor takes no damage. A cheat: the trip will not advance.",
        ["Unlim. Chas. Rein."] = "Unlimited chassis reinforcement: your chassis takes no damage. A cheat: the trip will not advance.",
        ["Unlim. Wiper Fluid"] = "Unlimited wiper fluid: the windscreen washer never runs out.",
        ["Difficulty Level"] = "Difficulty: how much your own shots and flames do. Wimp more, Champion the original's, Badass less.",
        // ---- Play Options: the port's rows
        ["Hidden Wpns & Cars"] = "Hidden weapons and cars: the original's unfinished weapons and cars offered in the garages.",
        ["Modern Physics"] = "Modern physics: your car as a modern rigid body with suspension and tyres; the other cars, weapons and damage are unchanged. Off is the original's handling. Not online.",
        ["Simulation Rate"] = "Simulation rate: the game's steps a second. 20 Hz is the original's; others can break missions.",
        ["Checkpoints"] = "Checkpoints: the game keeps a checkpoint as you go, so a failed mission can be retried from it.",
        ["Auto Reverse"] = "Auto reverse: holding Brake at a standstill drives backwards, as modern games do.",
        ["Map Downloads"] = "Map downloads: when a network host's custom map is missing here, ask, always fetch it, or never.",
        ["Unlim. Turrets"] = "Unlimited turrets: a car may carry more than one roof turret.",
        ["CB Voice"] = "CB voice: push-to-talk voice chat in network games (the Poem key); the microphone is in Audio Control.",
        ["Check for Updates"] = "Check for updates: at start, ask GitHub once whether a newer release exists. Nothing about you is sent.",
        ["Chase Crosshair"] = "Chase crosshair: an aiming mark in the F2 chase view. Online, only if the host allows it.",
        ["Load During Briefing"] = "Load during briefing: the briefing plays while the mission loads. Off, the original's order: the load, then the briefing. The modern look only: the 1997 look always loads first, as the original.",
        ["Turrets"] = "Turrets: how fast turrets turn, as in the 1998 build (GOG and the Nitro Pack) or the 1997 CD. Online, the host's choice.",
        ["Unlim. Ordnance"] = "Unlimited ordnance: no cap on mines, oil and fire left on the road (the original keeps 160 of a kind). Single player only.",
        ["Head Tracking"] = "Head tracking: an opentrack head tracker turns your view (Analog) or glances at past a threshold (Digital).",
        ["Head Track Sens."] = "Head tracking sensitivity: how far the view turns for a turn of your head.",
        ["Opentrack"] = "Opentrack, the free head tracker: click to start it when Stopped. Tracking: your head turns the view; Missing: not installed.",
        ["Opentrack Auto"] = "Opentrack Auto: a mission with Head Tracking on starts opentrack; the game closes the one it started on exit.",
        ["Opentrack Profile"] = "Opentrack Profile: click to add an I26 profile to opentrack (webcam face tracker, sent to this game).",
        ["Melee A.I."] = "Melee A.I.: how well an Instant Melee's computer cars drive at first: Normal (the original's), your Difficulty Level's, Easy or Hard.",
        ["Equip. Tooltips"] = "Equipment tooltips: rest the pointer on a part in the garage, the inventory or the salvage lists to see what it does in the game. Off shows none there.",

        // ---- Controls (the port's sheet: force feedback, then the mouse and the key map)
        ["Force Feedback"] = "Force feedback: Pad rumbles with the original's effects, Pad Enhanced adds pulses for gears, shots and the engine, Wheel drives a force-feedback wheel (Windows).",
        ["Rumble Strength"] = "Rumble strength: how strong every rumble sent to the pad is, 25 to 150 %.",
        ["Wheel Strength"] = "Wheel strength: the force-feedback wheel's overall strength (Windows).",
        ["Mouse Options..."] = "Mouse Options: the mouse camera, its sensitivity, cockpit mouse look and the mouse buttons.",
        ["Key Map..."] = "Key Map: which key, mouse button or pad input does each command; Calibrate sets up a pad or a joystick.",

        // ---- Audio Control
        ["Music Level"] = "Music level: the soundtrack's volume. Applies at once.",
        ["Sfx Level"] = "Effects level: engines, weapons and the world. Applies at once.",
        ["SFX Level"] = "Effects level: engines, weapons and the world. Applies at once.",
        ["Voice Level"] = "Voice level: the characters' and the radio's voices. Applies at once.",
        ["CB Microphone"] = "CB microphone: the input the CB voice chat uses in network games.",
        ["Output"] = "Output: stereo, or 5.1 / 7.1 with the bass shaker's signal on the LFE channel. Applies at once.",
        ["LFE Level"] = "LFE level: how strong the bass shaker's signal is (0 none). Applies at once.",
        ["HiFi Audio"] = "HiFi Audio: the sound pack's sounds and movie tracks in place of the original's. Sounds started after a change follow it.",

        // ---- Mouse Options (the port's screen: the left table, then the mouse's buttons and wheel)
        ["Mouse Camera"] = "Mouse camera: the mouse turns the F2 chase view.",
        ["Mouse Sensitivity"] = "Mouse sensitivity: how far the view turns for a move of the mouse.",
        ["Invert Y Axis"] = "Invert Y: pushing the mouse forward looks down.",
        ["Cockpit Mouse Look"] = "Cockpit mouse look: the mouse turns your head in the 3D cockpit.",
        ["Camera Return"] = "Camera return: the view swings back behind the car when you stop moving the mouse.",
        ["View Zoom Reset"] = "View zoom reset: the chase view's zoom goes back when you change views.",
        ["Mouse In Window"] = "Mouse in window: the pointer cannot leave the game's window.",
        ["Mouse Button"] = "A mouse button: click to choose the command it does; Delete clears it.",
        ["Mouse Wheel"] = "The mouse wheel: zooms the chase view, or cycles the weapons.",

        // ---- the key map (Control Configuration's art)
        ["Key Binding"] = "Left click: pick a key, mouse button or pad input from the list. Right click: press the key or button.",
        ["Game Key"] = "A network game's key: click, then press the key (Ctrl, Shift and Alt may go with it).",
        ["Calibrate"] = "Calibrate: set up a game pad, wheel or joystick: its sticks, buttons and dead zones.",
        ["Restore"] = "Restore: the keyboard's and the mouse's defaults (a pad's are on its own screen).",
        ["Restore Defaults"] = "Restore defaults: the mouse's settings and buttons back to their defaults.",

        ["Advanced..."] = GraphicsHelp.AdvancedEntry,

        // ---- the pages' buttons
        ["Reset"] = GraphicsHelp.Reset,
    };

    /// <summary>Every label with a tooltip (the tests' coverage list).</summary>
    public static IEnumerable<string> Labels => Tips.Keys;
}
