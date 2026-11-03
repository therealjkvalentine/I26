using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using I76.Sim.Missions;
using I76.Sim.Weapons;

namespace I76.Game.Shell;

/// <summary>
/// The rules that differ between the original's builds in a way a player feels, each a key of user://build-rules.cfg with
/// the 1998 build's value (GOG's) the default (docs/playing.md "Rules that differ between the original's builds"). The file
/// is written once with every key's facts in comments above it, since ConfigFile drops comments: the game never rewrites it,
/// only appends the block of a key it does not find, so the player's values and the notes stay. Engine-free but for the
/// path, so the game tests can call it.
/// </summary>
public sealed record BuildRules(bool TurretSlew1997, bool Tactics1997)
{
    public const string FileName = "build-rules.cfg";
    public static readonly BuildRules Default = new(false, false);

    public const string Header = """
        ; I26: the rules that differ between the original's builds (build-rules.cfg, beside shell.cfg in your user folder).
        ;
        ; Interstate '76 shipped as several builds. A few rules differ between them in a way a player feels; each is a key
        ; below, with what each build does and how that is known. I26 follows the 1998 build by default: Interstate '76 Gold
        ; 1.2, the build GOG sells (i76.exe md5 9a232dcc), whose rules the Nitro Pack's nitro.exe (md5 28b8ae27) shares here.
        ; Set a key to 1997 for the March 1997 CD release (retail 1.0, I76.EXE md5 6cc509ad). The 1997 patches 1.05 to 1.083
        ; change none of these rules.
        ;
        ; The game reads this file at each mission start and when the Esc menu closes. It never rewrites a line you changed;
        ; it only adds the block of a key it does not find. Single player only: a network game plays the 1998 rules.
        ;
        ; Checked and the same in every build, so no key: a turret's lead (the target predictor at the round's closest
        ; approach, plus 0.09 s), the AI droppers' gate (the same predictor; drops under a pursuer at any speed), who is
        ; credited with a ram, the radar's sweep, the cockpit needles, the flamer (the stream, its damage and the mirror's
        ; second update) and the vehicle physics, landings and the T05 canyon jump included. The 1.05 patch and later stop a
        ; car's step once its own landing has wrecked it; 1.0 finishes that step (no difference to a car that survives).
        ; Elsewhere: the player's damage factor is Play Options' Difficulty Level. The 1997 build has none (Champion's x1.0);
        ; the 1998 build deals x2.0 / x1.0 / x0.75 at Wimp / Champion / Badass with the player's rounds and flame, Wimp a fresh
        ; install's. Set Champion for the 1997 damage.

        [rules]
        """;

    /// <summary>Each key's block: its comment, then the default line.</summary>
    public static readonly (string Key, string Block)[] Keys =
    [
        ("turret_slew", """
            ; turret_slew: how fast a turret turns toward its target: car turrets, gun emplacements and helicopters, the player's
            ; and the AI's.
            ;   1998 (default): 1.7 rad/s, a helicopter's 6.8, times 0.75 + 0.25 x the turret's health ratio: a half-dead turret
            ;     turns at 88 %.
            ;   1997: 1.5 rad/s, a helicopter's 6.0, times the health ratio itself: 12 % slower when whole, half speed at half
            ;     health.
            ;   Read in both builds' code: the same turret update, these constants differ. Here, a car's 7.62 turret on a car
            ;   circling it 25 m off at 25 m/s hits with 77 % of its rounds whole and 75 % at half health at 1998, 75 % and 7 %
            ;   at 1997; 15 m off, whole, 64 % and 38 %. A whole turret on a passing car hits alike under both.
            turret_slew=1998
            """),
        ("ai_tactics", """
            ; ai_tactics: how an attacking AI car picks its tactic (ram, ride alongside on the road, gun from off road, mine,
            ; stand off, flee).
            ;   1998 (default): a draw from the tactics table weighted by the car's aggression, a candidate whose conditions
            ;     fail (on the same road, a gun ready) struck and drawn again.
            ;   1997: the chooser always returns dirt_gunner (gun from off road), no draw and no condition. Measured in that
            ;     build (T14, n = 7): the lead cop circles an idle player 10 to 90 m off, mostly out of sight, and fires 3 to 19
            ;     rounds in its first 20 s, then a few a minute.
            ;   Measured in the 1998 build on the same scene (n = 2): the cop takes road_side from its first attack and keeps
            ;   it, patrolling the road, the player in view. The Nitro Pack's chooser is the 1998 build's.
            ai_tactics=1998
            """),
    ];

    /// <summary>The file's text: the whole template for a missing file, else the text with any key it lacks appended.</summary>
    public static string Complete(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Header + "\n\n" + string.Join("\n\n", Keys.Select(k => k.Block)) + "\n";
        var have = Values(text);
        var missing = Keys.Where(k => !have.ContainsKey(k.Key)).ToList();
        if (missing.Count == 0) return text;
        return text.TrimEnd() + "\n\n" + string.Join("\n\n", missing.Select(k => k.Block)) + "\n";
    }

    /// <summary>The key = value lines; ';' and '#' start a comment, a section header is skipped, quotes are dropped.</summary>
    public static Dictionary<string, string> Values(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l[0] is ';' or '#' or '[') continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            d[l[..eq].Trim()] = l[(eq + 1)..].Trim().Trim('"');
        }
        return d;
    }

    /// <summary>The rules a text sets; a key absent or set to anything but 1997 keeps the 1998 build's.</summary>
    public static BuildRules Parse(string? text)
    {
        if (text is null) return Default;
        var v = Values(text);
        bool Is1997(string key) => v.TryGetValue(key, out var s) && s == "1997";
        return new BuildRules(Is1997("turret_slew"), Is1997("ai_tactics"));
    }

    /// <summary>The user folder's file, written or completed first; the defaults when it cannot be read.</summary>
    public static BuildRules Load()
    {
        try
        {
            var path = Godot.ProjectSettings.GlobalizePath("user://" + FileName);
            string? text = File.Exists(path) ? File.ReadAllText(path) : null;
            var full = Complete(text);
            if (full != text) File.WriteAllText(path, full);
            return Parse(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Godot.GD.PushWarning($"{FileName}: {e.Message}");
            return Default;
        }
    }

    /// <summary>Sets the session's rules: single player only, a network game keeps the 1998 build's.</summary>
    public void Apply(Combat c, ScriptHost? host)
    {
        bool offline = !c.NetworkGame;
        c.TurretSlew1997 = TurretSlew1997 && offline;
        if (host is not null) host.TacticStub1997 = Tactics1997 && offline;
    }
}
