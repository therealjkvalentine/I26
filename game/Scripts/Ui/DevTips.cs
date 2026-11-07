using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The Developer panels' tooltips (Godot's own, which wait about half a second and stay on screen): what each key of the Settings
/// section does, in a player's words (docs/playing.md "Settings without a row", docs/dev-hooks.md), and what each button, picker and
/// field of the panels does. Plain printable text, a sentence or two each.
/// </summary>
public static class DevTips
{
    /// <summary>The tip for a key of the Settings section (DevSettings.All, WithRow, DevOnly); null for a key without one.</summary>
    public static string? Of(string key) => key switch
    {
        "mirror_range" => "Rear mirror reach: the original's 100 m, or the whole view (about 0.2 ms more a frame). Look follows the look in use.",
        "brake_lamps" => "Brake lamps light while braking only, as the original's, or stay on 0.15 s after. Look follows the look in use.",
        "mg_tracers" => "A machine gun round in the modern look: the original's thin line, or a glowing bolt. The 1997 look always draws the line.",
        "sky_drift" => "The 1997 look's clouds: the original steps them twice as fast with the mirror up; steady steps them once a tick.",
        "death_orbit" => "The camera's orbit after your car is destroyed: the original's 8 degrees a tick, or a steady 20 degrees a second.",
        "music_tracks" => "Mission music plays disc tracks 2 to 14 in turn, as the original; all goes on into the title and ending tracks.",
        "garage_radio" => "The 3D garage: the menu music goes on, as the original's, or a boombox on the bench plays mission tracks.",
        "death_variety" => "Modern look car deaths: on shows a small first sign of what killed the car (hood, wheel, cab, ammo) before a smaller blast.",
        "menu_warmup" => "While the main menu stands idle, the modern look's effects are made in advance. It measured no saving; off by default.",
        "movie_ignition" => "Modern look ignition: the engine start-up from the INT03 briefing movie, cut from your own install, plays instead of the kit's sound.",
        "briefing_while_loading" => "Mission start: the loading card first (as long as the original shows it), then the briefing; or the briefing movie first.",
        "melee_ai" => "Instant Melee's computer cars open at Normal (the original's), the Difficulty Level's skill, Easy or Hard. From the next mission.",
        "effects_size" => "The modern look's weapon effects (streaks, sparks, flashes, smoke, fireballs) in percent of their drawn size.",
        "effects_brightness" => "The modern look's weapon effects' glow and their brief lights, in percent.",
        "effects_opacity" => "The modern look's weapon effects' opacity, in percent: lower lets more of the scene through dust, smoke and fire.",
        "vegetation_density" => "The modern look's scattered plants, in percent of what the Vegetation level places. Lower thins them; costs frame time above 60.",
        "brush_impacts" => "The modern look's plants answer the fighting: hits shake them, cars lay them down, blasts flatten them, fire can burn dry brush.",
        "ordnance_limit" => "Play Options' Unlim. Ordnance: the original caps live rounds of a kind at 160; unlimited keeps a long oil or fire trail whole.",
        "modern_physics" => "Play Options' Modern Physics: your car as a rigid body with suspension and tyres instead of the original's model. Not online.",
        "coop_rolling_start" => "A co-op game you host: a player who respawns beside a moving teammate comes in at that teammate's speed and heading.",
        "early_tick" => "The 1997 look: a driving or weapon key runs the next tick at once instead of waiting for it, about 20 ms sooner.",
        "draw_ahead" => "Your car and the camera drawn at the newest tick carried forward, not between the last two: steering shows up to a tick sooner.",
        "low_latency_present" => "With VSync on, each refresh shows the newest finished frame: about 11 ms less lag at 180 Hz, more GPU power used.",
        "target_fps" => "Holds this frame rate by drawing the 3D view at a lower Render Scale (down to 67 %) while the graphics card is what keeps the game under it. 0: off.",
        "low_latency_audio" => "A 5 ms sound buffer and a faster physics step: sounds start about 15 ms sooner. Turn off if the sound crackles. Next start.",
        "aa_1997" => "The 1997 look takes Graphic Detail's Anti-Aliasing; false draws its edges unsmoothed, as the 3D card did.",
        "even_flash" => "The 1997 look: a gun's muzzle flash lit at each shot between ticks, so a fast gun flashes evenly.",
        "rumble" => "Force Feedback's pad events: the original's effects only, or enhanced with pulses for nitrous, gears, the starter and shots.",
        "hd_menu_art" => "With an HD menu pack, the modern look's menus and mission map draw its upscaled pictures; false the original ones. Next screen.",
        "lighting_calibration" => "The modern look's lighting matched by day to the 1997 look's colours and brightness; filmic a softer curve; off uncalibrated.",
        "crossover" => "The top of the bass shaker signal in Hz. Set it to your subwoofer's or shaker amplifier's crossover.",
        "lfe_engine" => "The bass shaker's share of the engine and the starter's shudder, in percent (0 leaves it out).",
        "lfe_road" => "The bass shaker's share of the road's rumble, in percent (0 leaves it out).",
        "lfe_hits" => "The bass shaker's share of hits on your car, rams and crashes, in percent (0 leaves it out).",
        "lfe_shots" => "The bass shaker's share of your own shots, in percent (0 leaves it out).",
        "lfe_explosions" => "The bass shaker's share of explosions, near and far, in percent (0 leaves it out).",
        "lfe_offroad" => "The road rumble off the roads (dirt, sand, rough ground), on top of the road's: above 100 sets it further apart.",
        "lfe_bumps" => "The knock as a wheel crosses onto or off a road or bridge, in percent (0 leaves it out).",
        "lfe_cracks" => "For listening only: filled cracks across asphalt ticking through the shaker. 0 by default, 100 recommended to try.",
        "lfe_strip" => "For listening only: a rumble strip buzz while the car runs along a paved road's edge. 0 by default, 100 recommended to try.",
        "developer" => "The Developer panel: Ctrl+F2 or Ctrl+` opens it, and a Developer entry joins the Esc menu. Single player. Next start.",
        "draw_distance" => "Graphic Detail's Visibility Range: look follows the look, 0 the original's 600 m, 1 the whole map, 2 faded at 2.3 km.",
        "dev_fps" => "Graphic Detail's Show FPS: the frame rate and frame time in the window's top right corner, over menus and game.",
        "hood_gloss" => "Graphic Detail's Hood Gloss level by level: the modern look's paint, 0 flat as the original, 100 a full clear coat.",
        "world_textures" => "The world's textures: the 1997 originals or the 4x pack (2x and 3x only from the file or here). From the next mission.",
        "car_textures" => "The cars' and the dash's textures: 1997, 4x or 8x. The 8x needs tex-world-8x beside the 4x and about 0.5 GB more video memory, most of it the dash.",
        "cabins_behind_glass" => "The modern look draws a dim cabin and driver behind other cars' windows; false is plain reflective glass. Live.",
        "log_file" => "Writes logs/godot<date>.log in the user folder from the next start, for bug reports.",
        _ => null,
    };

    /// <summary>The tip for a control of the panels, by its label (a button's or check box's text, or a picker's or field's name).</summary>
    public static string? Control(string label) => label switch
    {
        "mission" => "The mission Start runs, from every mission the install offers.",
        "car" => "The car Start drives: this car, the mission's own, or any car file.",
        "Start" => "Leaves this mission and starts the one picked in the car picked.",
        "place" => "A place to teleport to: your kept places, the map's landmarks, the script's paths and cars.",
        "Go" => "Teleports your car: to the place picked, or (on the coordinates' line) to the x, z and heading typed.",
        "place name" => "A name for the car's place now, for Keep.",
        "Keep" => "Keeps the car's place under the name typed, for this mission, at the top of the places.",
        "Forget" => "Forgets the kept place picked.",
        "x" => "East-west, in game units.",
        "z" => "North-south, in game units.",
        "heading" => "The car's heading in degrees.",
        "Here" => "Fills x, z and heading with your car's place now.",
        "Repair & refill" => "Repairs your car and refills its ammunition and specials.",
        "God mode" => "Your car takes no damage.",
        "Bring nearest car" => "Puts the nearest other car in front of yours.",
        "loadout" => "The weapon on this hardpoint, from those the garage allows there. Apply puts it on, loaded.",
        "Apply" => "Puts the weapons picked on their hardpoints, loaded.",
        "Refill ammo & specials" => "Refills every weapon's ammunition and the specials' charges; nothing is repaired.",
        "Store" => "Keeps the whole simulation as it is now.",
        "Restore" => "Puts the simulation back as stored.",
        "Freeze" => "Stops the ticks: nothing moves until Freeze is off; Step runs single ticks.",
        "Step 1 tick" => "Runs one simulation tick.",
        "Step 10" => "Runs ten simulation ticks.",
        "speed" => "The pace of the ticks: slower or faster, the ticks themselves unchanged.",
        "show over the game when this panel is closed" => "Keeps the signals' graphs drawn over the game after the panel closes.",
        "Close" => "Closes the panel (Esc, Ctrl+F2 or Ctrl+` too).",
        "Import Saves" => "Lists the original game's bookmarks from its folder, car and parts included, to bring them in.",
        "All Scenes" => "Makes a bookmark for every scene of the trip, each in its own car; asks first.",
        _ => null,
    };

    /// <summary>
    /// The Settings section's tips: each key's name and its value control (DevSettings.Build lays them two to a grid line) get the
    /// key's tip, the name's with when a change shows, the control's with how to step it.
    /// </summary>
    public static void Annotate(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is GridContainer grid)
            {
                var cells = grid.GetChildren();
                for (int i = 0; i + 1 < cells.Count; i += 2)
                {
                    if (cells[i] is not Label name || Of(name.Text.Split(' ')[0]) is not { } tip) continue;
                    name.TooltipText = $"{tip} Shows: {name.TooltipText}.";
                    if (cells[i + 1] is Control value)
                    {
                        value.TooltipText = value.TooltipText.Length > 0 ? $"{tip}\n{value.TooltipText}" : tip;
                        foreach (var inner in value.GetChildren()) if (inner is Control c) c.TooltipText = tip;
                    }
                }
            }
            else Annotate(child);
        }
    }
}
