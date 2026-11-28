using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// shell.cfg and trip.cfg shared between builds (ShellSettings.SaveFile, TripState.File). On 2026-11-27 a family build from 19 Nov,
/// started on the same user folder, saved the player's shell.cfg as its Write wrote it: every key it did not know (the Graphic Detail
/// rows added since, Smart A.I., Solid Cliffs, own_smoke) was gone, and words it could not read (terrain_shadows "fine", mirror_rate
/// "smooth4", lighting_calibration "sunlit") went to its defaults; the next save by the current build wrote those defaults. A save now
/// changes only what the run changed and keeps every other key, known or not, as the file has it.
/// </summary>
public class SharedSettingsFileTests
{
    private static readonly (int[] Sizes, bool Plain) Packs = ([8, 4], true);

    /// <summary>The player's file as the current build saved it at 12:45 that day (settings only), before the family build ran.</summary>
    private const string OwnersFile = """
        [shell]

        version=14
        graphics=PackedByteArray(5, 1, 1, 1, 1, 1, 1, 2, 0, 1, 1, 1, 3, 2, 1, 1)
        audio=PackedByteArray(7, 9, 8, 8)
        play=0
        difficulty=2

        [port]

        fork_version=14
        vegetation=1
        draw_distance=1
        checkpoints=false
        force_feedback=false
        lfe_level=100
        tactile_eq="13:4, 18:4, 20:3.7, 22:1.6, 25:0, 28:-1.3, 31:-2.5, 35:-3.5, 40:-2.5, 45:-1.3, 50:0, 80:0, 90:1.6, 100:3.7"
        sim_rate=24
        auto_reverse=false
        terrain_shadows="fine"
        haze=2
        glow=1
        smoke_lighting=true
        smoke_haze=true
        road_detail=true
        heat_shimmer="road"
        palette_grade=true
        lighting_calibration="sunlit"
        mouse_camera=2
        mouse_sensitivity=1.0
        hood_gloss=10
        render_scale=200
        max_fps=0
        vsync=true
        developer=true
        dev_fps=true
        hd_menu_art=true
        world_textures="4x"
        car_textures="8x"
        texture_packs="8x 4x tex-world"
        screen_aa="smaa"
        handicaps="auto"
        turret_slew=1998
        head_track="analog"
        smart_ai="all"
        opentrack_auto=true
        coop_own_car=false
        coop_scale=3
        coop_rolling_start=true
        effects_size=80
        effects_brightness=75
        effects_opacity=70
        solid_cliffs="on"
        mirror_range="full"
        mirror_rate="smooth4"
        mirror_haze="med"
        radar_sweep="smooth"
        mirror_aa="smaa"
        brake_lamps="original"
        mg_tracers="original"
        own_smoke="engine"

        [nitro]

        play=0
        difficulty=2
        """;

    /// <summary>
    /// Every key this build reads or writes at a value that is not its default (each one as Write writes it back), but the
    /// <see cref="ShellSettings.Migrated"/> keys. A key added to Read or Write fails <see cref="EveryKeyIsInTheList"/> until it is here.
    /// </summary>
    internal const string EveryKey = """
        [shell]

        version=14
        graphics=PackedByteArray(5, 1, 0, 1, 1, 1, 2, 2, 1, 1, 1, 1, 7, 2, 0, 1)
        audio=PackedByteArray(7, 9, 8, 6)
        play=4
        difficulty=2

        [port]

        fork_version=14
        vegetation=2
        draw_distance=0
        checkpoints=true
        force_feedback=false
        ffb_wheel=true
        ffb_strength=60
        ffb_device="Wheel X"
        rumble="enhanced"
        output="5.1"
        lfe_level=75
        lfe_engine=50
        lfe_road=60
        lfe_hits=70
        lfe_shots=80
        lfe_explosions=90
        lfe_offroad=110
        lfe_bumps=120
        lfe_helicopters=130
        lfe_cracks=140
        lfe_strip=150
        tactile_eq="13:4, 18:4"
        game_eq="60:2"
        movie_eq="60:-2"
        crossover=120.0
        rumble_strength=50
        sim_rate=24
        sim_rate_warned=true
        auto_reverse=true
        look_1997=true
        dash_3d=false
        ambient_occlusion=true
        terrain_shadows="fine"
        haze=2
        glow=1
        reflections=true
        smoke_lighting=true
        smoke_haze=true
        road_detail=true
        road_detail_strong=true
        heat_shimmer="road"
        heat_strength="extreme"
        software_renderer=true
        palette_grade=true
        crash_blur="off"
        motion_blur=true
        clouds="volumetric"
        mouse_camera=3
        mouse_sensitivity=1.5
        mouse_invert_y=true
        resolution="1600x900"
        hood_gloss=10
        target_fps=90
        render_scale_cap="1024"
        render_scale=200
        mouse_cockpit=true
        chase_crosshair=true
        crosshair_drawn=false
        wheel_cycle_weapon=true
        skip_intro=true
        assisted_steering=false
        max_fps=144
        vsync=false
        keep_mouse_in_window=true
        developer=true
        dev_fps=true
        menu_fps=false
        hd_menu_art=false
        menu_width="4:3"
        hd_sounds=false
        world_textures="4x"
        car_textures="8x"
        texture_packs="8x 4x tex-world"
        screen_aa="smaa"
        shadow_detail="ultra"
        modern_physics=true
        seed=12345
        telemetry="127.0.0.1:4444"
        telemetry_format="outsim"
        map_downloads=2
        share_maps=false
        car_reset=false
        ignition_stays_on=true
        cb_radio=false
        handicaps="auto"
        host_crosshair=true
        turret_slew=1997
        host_turret_slew=1997
        host_bot_skill_difficulty=true
        cb_voice=false
        equipment_tips=false
        cb_mic="Mic 1"
        hidden_content=true
        unlim_turrets=true
        update_check=false
        head_track="analog"
        head_track_sensitivity=2.0
        head_track_reach=120
        head_track_centre="off"
        thrown_light="bright"
        head_track_port=4243
        head_track_bind="0.0.0.0"
        head_track_position=true
        head_track_source="udp"
        opentrack_path="C:/opentrack/opentrack.exe"
        smart_ai="all"
        opentrack_auto=true
        coop_own_car=false
        coop_scale=3
        coop_damage=0.5
        coop_rolling_start=false
        coop_copy_key_cars=true
        melee_ai=2
        effects_size=80
        effects_brightness=75
        effects_opacity=70
        vegetation_density=100
        garage_3d=false
        menu_warmup="on"
        garage_radio="on"
        movie_ignition="on"
        solid_cliffs="on"
        briefing_while_loading=false
        load_under_briefing=false
        movie_decode="engine"
        briefing_card_hold=2.0
        brush_impacts="off"
        bush_knock="off"
        lighting_calibration="sunlit"
        car_interiors=false
        camera_return=false
        zoom_reset=false
        fov_window=110
        fov=110
        net_names=false
        editor_music=false
        mirror_range="full"
        side_mirrors="left"
        mirror_rate="smooth4"
        mirror_haze="med"
        radar_sweep="smooth"
        mirror_aa="smaa"
        mirror_round=12
        side_mirror_round=50
        side_mirror_rim_out=3
        side_mirror_head="turned"
        side_mirror_tilt_right=4
        side_mirror_tilt_left=6
        side_mirror_face="proud"
        cockpit_trim="perforated"
        side_mirror_face_over_left=3
        side_mirror_face_over_right=4
        mirror_fov=60
        side_mirror_share=80
        side_mirror_angle=30
        side_mirror_drop=150
        side_mirror_overlap=5
        side_mirror_render="shared"
        brake_lamps="held"
        mg_tracers="bolts"
        specials_panel="square"
        helicopter_hearing="far"
        wreck_solid="drawn"
        sky_drift="original"
        death_orbit="smooth"
        early_tick=false
        sky_speed=80
        death_orbit_speed=40
        night_moon=1.2
        night_road=1.2
        night_sky=1.2
        night_chroma=0.8
        night_warm=0.8
        draw_ahead=true
        sway_snap=true
        road_grain=true
        low_latency_present=false
        low_latency_audio=false
        aa_1997=false
        fast_look_switch=false
        even_flash=false
        lamp_ramp="off"
        lamp_halo=50
        dash_light=50
        night_station_light=50
        headlamp_throw=50
        muzzle_reach=50
        smoke_linger=260
        smoke_start_dark=50
        smoke_start_density=100
        smoke_start_width=50
        smoke_fade=60
        own_smoke="engine"
        music_tracks="all"
        ordnance_limit="unlimited"
        log_file=true
        bug_report_inputs=false
        new_game_plus_unlocked=true
        renderer="Vulkan"

        [nitro]

        play=1
        difficulty=1
        """;

    /// <summary>Keys and a section no build of today knows: a newer build's, which a save must keep as they are.</summary>
    private const string Unknown = """


        [port]

        future_row="sharp"
        future_level=7

        [future]

        thing=PackedByteArray(1, 2, 3)
        word="kept"
        """;

    private static SettingsFile File(string text) => ShellFileCompatTests.Parse(text);

    private static Dictionary<(string, string), string> Keys(SettingsFile f) =>
        f.Entries.ToDictionary(e => (e.Section, e.Key), e => e.Value is byte[] b ? string.Join(",", b) : new SettingsValue(e.Value).ToString());

    /// <summary>The two files hold the same keys with the same values.</summary>
    private static void SameKeys(SettingsFile before, SettingsFile after)
    {
        var a = Keys(before);
        var b = Keys(after);
        Assert.Equal(a.Keys.OrderBy(k => k).ToList(), b.Keys.OrderBy(k => k).ToList());
        foreach (var (k, v) in a) Assert.True(v == b[k], $"{k.Item1}.{k.Item2}: {v} became {b[k]}");
    }

    /// <summary>A run's save: the settings read from <paramref name="disk"/> (as Load reads them), <paramref name="change"/>d, saved over it.</summary>
    private static SettingsFile Save(SettingsFile disk, Action<ShellSettings>? change = null)
    {
        var s = ShellSettings.Read(disk.Copy(), null, Packs);
        change?.Invoke(s);
        return s.SaveFile(disk, Packs);
    }

    /// <summary>
    /// Every key of <paramref name="before"/> is in <paramref name="after"/> with its value; a key only <paramref name="after"/> has is one
    /// this build writes for every file (the owner's file came from a build without upstream's motion_blur and clouds).
    /// </summary>
    private static void KeptAll(SettingsFile before, SettingsFile after)
    {
        var a = Keys(before);
        var b = Keys(after);
        foreach (var (k, v) in a) Assert.True(b.TryGetValue(k, out var w) && v == w, $"{k.Item1}.{k.Item2}: {v} became {(w ?? "nothing")}");
        var own = Keys(ShellSettings.Read(before.Copy(), null, Packs).Write(before, Packs));
        foreach (var k in b.Keys.Except(a.Keys)) Assert.True(own.ContainsKey(k), $"{k.Item1}.{k.Item2} was added");
    }

    [Fact]
    public void TheOwnersFileSavedUntouchedKeepsEveryKey()
    {
        var disk = File(OwnersFile);
        KeptAll(disk, Save(disk));
    }

    /// <summary>Words a build cannot read, as the family build could not read "fine" or "smooth4": kept until the player changes that row.</summary>
    [Fact]
    public void WordsThisBuildCannotReadAreKeptUntilTheRowIsChanged()
    {
        var text = OwnersFile.Replace("terrain_shadows=\"fine\"", "terrain_shadows=\"finest\"").Replace("mirror_rate=\"smooth4\"", "mirror_rate=\"smooth9\"")
            .Replace("heat_shimmer=\"road\"", "heat_shimmer=\"lava\"").Replace("haze=2", "haze=9");
        var disk = File(text);
        KeptAll(disk, Save(disk));
        // a row the player changes is written as this build writes it, the others still kept
        var changed = Save(disk, s => s.TerrainShadows = TerrainShadowsRow.Fine);
        Assert.Equal("fine", changed.GetValue("port", "terrain_shadows").ToString());
        Assert.Equal("smooth9", changed.GetValue("port", "mirror_rate").ToString());
        Assert.Equal("lava", changed.GetValue("port", "heat_shimmer").ToString());
        Assert.Equal(9L, changed.GetValue("port", "haze").Obj);
    }

    [Fact]
    public void KeysAndSectionsThisBuildDoesNotKnowAreKept()
    {
        var disk = File(OwnersFile + Unknown);
        var saved = Save(disk, s => s.Haze = 0);
        Assert.Equal("sharp", saved.GetValue("port", "future_row").ToString());
        Assert.Equal(7L, saved.GetValue("port", "future_level").Obj);
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])saved.GetValue("future", "thing"));
        Assert.Equal("kept", saved.GetValue("future", "word").ToString());
        // the change: Haze Off is the default, left out; nothing else moves
        Assert.False(saved.HasSectionKey("port", "haze"));
        KeptAll(File(OwnersFile.Replace("haze=2\n", "") + Unknown), saved);
    }

    /// <summary>Every known key at a value that is not its default, unknown keys and a section beside them: a save keeps the file as it is, twice over.</summary>
    [Fact]
    public void EveryKeyRoundTrips()
    {
        var disk = File(EveryKey + Unknown);
        var once = Save(disk);
        SameKeys(disk, once);
        SameKeys(disk, Save(once));
    }

    /// <summary>A row changed alone changes its own key and no other.</summary>
    [Fact]
    public void OneChangeChangesOneKey()
    {
        var disk = File(EveryKey + Unknown);
        var a = Keys(disk);
        var b = Keys(Save(disk, s => s.SmokeHaze = false));
        Assert.True(a.Remove(("port", "smoke_haze")));
        Assert.Equal(a.OrderBy(k => k.Key).ToList(), b.OrderBy(k => k.Key).ToList());
    }

    /// <summary>
    /// The guard: every key Read asks about or Write writes is in <see cref="EveryKey"/> or is <see cref="ShellSettings.Migrated"/>, and
    /// Write writes each key of the list back as it is (so each is at a value that is not its default, and the round trip covers it).
    /// Read asks about the same keys whatever the file says (but the migrated ones), so a save can tell its own keys from others'.
    /// </summary>
    [Fact]
    public void EveryKeyIsInTheList()
    {
        var list = File(EveryKey);
        var probe = list.Copy().Track();
        var written = ShellSettings.Read(probe, null, Packs).Write(list, Packs);
        var empty = new SettingsFile();
        empty.SetValue("shell", "version", (long)ShellSettings.WrittenVersion);
        empty.SetValue("port", "fork_version", (long)ShellSettings.ForkVersion);
        empty.Track();
        var fresh = ShellSettings.Read(empty, null, Packs).Write(null, Packs);
        var known = probe.Asked!.Concat(empty.Asked!).Concat(written.Entries.Select(e => (e.Section, e.Key))).Concat(fresh.Entries.Select(e => (e.Section, e.Key)))
            .Where(k => k.Item1 is "shell" or "port").Distinct().ToList();
        var missing = known.Where(k => !list.TryGet(k.Item1, k.Item2, out _) && !ShellSettings.Migrated.Contains(k)).Select(k => $"{k.Item1}.{k.Item2}").ToList();
        Assert.True(missing.Count == 0, "keys not in SharedSettingsFileTests.EveryKey (add each at a value that is not its default): " + string.Join(", ", missing));
        foreach (var (s, k, v) in list.Entries)
        {
            Assert.True(written.TryGet(s, k, out var w), $"{s}.{k} is not written back: at its default, or not a key");
            Assert.True(SettingsFile.Same(v, w), $"{s}.{k}: {new SettingsValue(v)} is written back as {new SettingsValue(w)}");
        }
        Assert.DoesNotContain(written.Entries, e => ShellSettings.Migrated.Contains((e.Section, e.Key)));
        var valueDependent = empty.Asked!.Except(probe.Asked!).Concat(probe.Asked!.Except(empty.Asked!)).Where(k => !ShellSettings.Migrated.Contains(k)).ToList();
        Assert.True(valueDependent.Count == 0, "Read asks about these keys only for some values: " + string.Join(", ", valueDependent.Select(k => $"{k.Item1}.{k.Item2}")));
    }

    /// <summary>A file that has not had this build's steps (upstream's, an older fork build's) takes them; its unknown keys stay, withdrawn and migrated ones go.</summary>
    [Fact]
    public void AnOlderFileTakesTheStepsAndKeepsWhatItDoesNotKnow()
    {
        var disk = File("""
            [shell]

            version=11
            graphics=PackedByteArray(5, 1, 0, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1)

            [port]

            map_edge=true
            developer_tools=true
            upstream_new_row="x"
            draw_distance=2
            """);
        var saved = Save(disk);
        Assert.Equal((long)ShellSettings.ForkVersion, saved.GetValue("port", "fork_version").Obj);
        Assert.Equal("x", saved.GetValue("port", "upstream_new_row").ToString());
        Assert.False(saved.HasSectionKey("port", "map_edge"));
        Assert.False(saved.HasSectionKey("port", "developer_tools"));
        Assert.True((bool)saved.GetValue("port", "developer"));
        // the steps' values (version 12 drops the Far 0.11.0 wrote on every save), not the file's
        var stepped = ShellSettings.Read(disk.Copy()).Write(disk, Packs);
        Assert.Equal(stepped.HasSectionKey("port", "draw_distance"), saved.HasSectionKey("port", "draw_distance"));
        Assert.Equal((byte[])stepped.GetValue("shell", "graphics"), (byte[])saved.GetValue("shell", "graphics"));
    }

    /// <summary>An older key name this build reads is dropped at a save: a player who turns the Developer panel off stays off.</summary>
    [Fact]
    public void AMigratedKeyDoesNotComeBack()
    {
        var disk = File(OwnersFile.Replace("developer=true", "developer_tools=true"));
        var saved = Save(disk, s => s.DeveloperTools = false);
        Assert.False(saved.HasSectionKey("port", "developer_tools"));
        Assert.False(saved.HasSectionKey("port", "developer"));
        Assert.False(ShellSettings.Read(saved).DeveloperTools);
    }

    /// <summary>A newer build's step count stays: it does not run its steps again on its own keys.</summary>
    [Fact]
    public void ANewerBuildsVersionStays()
    {
        var disk = File(OwnersFile.Replace("fork_version=14", "fork_version=15") + Unknown);
        var saved = Save(disk, s => s.Glow = 0);
        Assert.Equal(15L, saved.GetValue("port", "fork_version").Obj);
        Assert.Equal("sharp", saved.GetValue("port", "future_row").ToString());
        Assert.False(saved.HasSectionKey("port", "glow"));
    }

    /// <summary>shell.cfg is written in one place, ShellSettings.Save, through SaveFile (every menu's, the pause pad's and the in-game keys' saves call it).</summary>
    [Fact]
    public void ShellCfgIsWrittenOnlyThroughSaveFile()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "game", "Scripts"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var hits = Directory.EnumerateFiles(Path.Combine(dir!, "game", "Scripts"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => System.IO.File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(x => x.Line.Contains("\"user://shell.cfg\"")).ToList();
        Assert.All(hits, h => Assert.True(h.File == "MainBugReport.cs" || h.Line.StartsWith("private const string Path = \"user://shell.cfg\""), $"{h.File}: {h.Line}"));
        var shell = System.IO.File.ReadAllLines(Path.Combine(dir!, "game", "Scripts", "Shell", "Shell.cs"));
        Assert.Contains(shell, l => l.Contains("SaveFile(old.Load(Path)") && l.Contains(".ToConfig().Save(Path)"));
        Assert.DoesNotContain(shell, l => l.Contains("Write(old.Load(Path)"));
    }

    /// <summary>trip.cfg: a key this build does not read is kept; the Trip+ section is this build's whole (START OVER drops it).</summary>
    [Fact]
    public void TripFileKeepsWhatItDoesNotKnow()
    {
        var disk = File("""
            [trip]

            mission=12
            highest=17
            finished=true
            future_trip="kept"

            [trip_plus]

            lap=2
            laps_done=1
            copies=1
            skill=1
            salvage=10

            [future_run]

            run=4
            """);
        var t = new TripState();
        t.ReadFrom(disk.Copy());
        SameKeys(disk, t.File(disk));
        t.Plus = t.Plus.OnLap(1);
        var over = t.File(disk);
        var back = new TripState();
        back.ReadFrom(over.Copy());
        Assert.Equal(1, back.Plus.Lap);
        Assert.Null(back.Plus.Active);
        Assert.False(over.HasSectionKey("trip_plus", "copies"));
        Assert.Equal("kept", over.GetValue("trip", "future_trip").ToString());
        Assert.Equal(4L, over.GetValue("future_run", "run").Obj);
    }
}
