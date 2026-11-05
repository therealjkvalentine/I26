# Getting started on the play fork

This repository is the private integration of a patch stack ahead of upstream I26. Read upstream's
[DEVELOPMENT.md](../DEVELOPMENT.md), [AGENTS.md](../AGENTS.md) and [CHARTER.md](../CHARTER.md) first; this page adds
only what is specific to the fork: how it is organised, how to build a playtest build on Windows or macOS in a few
minutes, and how a change travels from a branch to upstream.

## How the fork is organised

| Branch | What it is |
|---|---|
| `play` | upstream `master` + `play-meta` + every topic branch in `tools/play-branches.txt`, merged in that order. Build and play this one. Never commit to it by hand: `tools/sync-play.ps1` rebuilds it. |
| `play-meta` | the fork's own bookkeeping: this page, `docs/play-tracker.md` (every change with its evidence and PR order), `tools/play-branches.txt` (the merge order) and `tools/sync-play.ps1`. Never offered upstream. |
| `fix/*`, `feat/*`, `perf/*` | one logical change each, branched from upstream `master`, PR-shaped: a test, a CHANGELOG bullet, a `docs/deviations.md` row for a deliberate difference, one squashed commit whose body says what the original does and how it was checked. These are what go upstream, one at a time. |

Remotes, by convention: `origin` is upstream (read-only; set its push URL to something invalid), `play` is this
repository, and a public fork of upstream is used only for the single branch of a pull request being opened.

## Prerequisites (any OS)

1. The .NET 8 SDK (`dotnet --version` prints 8.0.x).
2. Godot 4.7.2, the **.NET (mono) edition**, from https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable
   (`Godot_v4.7.2-stable_mono_win64.zip`, `Godot_v4.7.2-stable_mono_macos.universal.zip`, `..._linux_x86_64.zip`).
   Keep the editor on 4.7.x: the project's `Godot.NET.Sdk` is 4.7.2 and the release templates match it. 4.7.2 is the
   latest stable; a newer major (4.8) is tried on a scratch branch before anyone moves.
3. Your own copy of Interstate '76 (GOG): the folder holding `I76.ZFS` is the install; the Nitro Pack's holds `nitro.zfs`.
   The game only reads it.
4. The free CC0 assets (not committed): `tools/fetch-vfx.sh` and `tools/fetch-ground.sh` (bash, curl, python3; Git Bash
   on Windows). Without them the modern look falls back to procedural ground and sprite effects. After fetching, import
   once: `godot --headless --path game --import` (also needed once after a fresh clone, for the project's own assets).

## Windows quick start

```
git clone <this repository> i26-play && cd i26-play && git checkout play
set I76_INSTALL=C:\Games\Interstate 76
dotnet build I76.sln
cd game && dotnet build && cd ..
<godot folder>\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game --import
<godot folder>\Godot_v4.7.2-stable_mono_win64.exe --path game -- --install "%I76_INSTALL%" --mission t01 --drive
```

The `_console.exe` shows the log; the other one does not. Useful game options after the `--`: `--1997` (the original's
look), `--dev` (the Developer panel: Ctrl+F2 or the Escape menu), `--melee --mission m01`, `--skip-movies`,
`--screenshot out.png --quit-after 300` (with `I76_QUIET_WINDOW=1` set and `--windowed --resolution 1280x720` before the `--`, such a run stays behind your other windows: docs/dev-hooks.md). The full list is at the top of `game/Scripts/Boot/InstallLocator.cs`.
User data (settings, bookmarks) lives in `%APPDATA%\I26` (the project sets its own user folder name); to test with a scratch copy set `APPDATA`
to an empty folder for that run.

## macOS quick start (Apple silicon or Intel)

```
brew install dotnet@8
export DOTNET_ROOT=/opt/homebrew/opt/dotnet@8/libexec PATH=/opt/homebrew/opt/dotnet@8/bin:$PATH
git clone <this repository> i26-play && cd i26-play && git checkout play
dotnet build I76.sln && (cd game && dotnet build)
unzip Godot_v4.7.2-stable_mono_macos.universal.zip && xattr -dr com.apple.quarantine Godot_mono.app
Godot_mono.app/Contents/MacOS/Godot --headless --path game --import
Godot_mono.app/Contents/MacOS/Godot --path game -- --install "$HOME/Games/Interstate76" --mission t01 --drive
```

Godot's .NET edition finds the runtime through `DOTNET_ROOT` and `PATH`; a double-clickable `.command` file that exports
the two variables and then runs the last command is the simplest launcher. The signed, exported app is a separate
matter (upstream's macOS notes and PR #144); for playtesting, the editor binary running the project is enough.
User data lives in `~/Library/Application Support/I26`.

## Linux

Upstream's Nix flake has everything (`nix develop`); otherwise the .NET 8 SDK, the Godot 4.7.2 mono zip and the same
commands as macOS; user data lives in `~/.local/share/I26`. `tools/shot.sh` (off-screen captures) and `tools/frame-profile.sh` are Linux-only.

## Tests

```
dotnet test tests/I76.Sim.Tests        # the simulation; no install needed
dotnet test tests/I76.Net.Tests        # the netcode; no install needed
dotnet test tests/I76.Editor.Tests     # the map editor
dotnet test tests/I76.Game.Tests       # the shell and HUD logic that has no Godot dependency
dotnet test tests/I76.Formats.Tests    # needs I76_INSTALL (skipped without it)
```

On Windows a few dozen tests fail on a clean upstream checkout (symlink privilege, path separators, network flakes under
load); record that baseline once (`dotnet test ... --logger trx` and list the failed names) and judge a branch by the
failures that are not in it. Windows Developer Mode removes the symlink ones. The tracker's "Integration notes" lists
the known test moves on `play`.

## Making a change

1. `git worktree add ../wt/<name> -b fix/<name> origin/master` (one worktree per branch; never commit to `play`).
2. Code, a test, a CHANGELOG bullet under `## Unreleased` (`### Fixed` or `### New`), a deviations row if it is a
   deliberate difference. The original's behaviour is the default; a difference goes behind an option.
3. One squashed commit: title in the present tense; body with what the original does and how that is known (in words,
   no reverse-engineering artefacts), what was wrong, what changes; a `Checked:` paragraph with the commands run and
   their counts. Nothing in the repository or a commit names a person, a machine, a private path or an address.
4. `git push play <branch>`; add the branch to `tools/play-branches.txt` on `play-meta` in its place in the order and a
   row to `docs/play-tracker.md`; run `tools/sync-play.ps1` from the `play` worktree (PowerShell 5.1 or 7;
   `-NoPush -NoRebase` for a dry run). Conflicts in CHANGELOG.md and the docs tables are unioned automatically; others
   are listed.
5. Opening a pull request upstream: push that one branch to the public fork and open it; when it merges, the sync
   script drops it from `play` by itself.

## A playtest build on another machine

Copy three things: the Godot 4.7.2 mono folder (or `.app`), the built `game/` folder (with its `.godot/` import cache and
`.godot/mono/temp/bin/Debug` assemblies, without `obj/`) and a launcher that runs
`Godot --path game -- --install <that machine's install>`. No install, no user data and no fetched asset leaves the
repository; the receiving machine supplies its own game copy. An exported release (`tools/release.sh`) is the proper
distribution; it needs the export templates of exactly 4.7.2.

## Online resources

Links worth keeping for later; none of their files belong in this repository (community uploads, no stated licence,
and the logo is the game's trademark), so anything taken from them stays in a player's own user folder.

- SteamGridDB, Interstate '76: clean icons and a high-resolution logo (a possible title-screen logo in a player's HD
  menu-art pack, and app icons for local shortcuts): https://www.steamgriddb.com/game/5247794/icons
- The Local Ditch fan site (strategy, car design, weapons, multiplayer folklore): https://localditch.com/interstate-76/
- OpenModelDB (upscaling models; the texture pack uses 4xTextures_GTAV_rgt-s_dither, CC-BY-4.0):
  https://openmodeldb.info/models/4x-Textures-GTAV-rgt-s-dither
