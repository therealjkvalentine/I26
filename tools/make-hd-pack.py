#!/usr/bin/env python3
"""Make the 4x HD texture pack in one command: export the install's textures through the game, run
tools/upscale-textures.py over them, then tools/text-redraws.py over the pack (docs/hd-textures.md, "Quick").

    python3 tools/make-hd-pack.py --game PATH/TO/i26 [--install DIR] [--work DIR] [--out DIR] [--device cuda|cpu]
                                  [--only GLOB] [--dry-run]

--game is the I26 program (i26.exe, the i26 binary, I26.app) or the folder it is in; it must be a build with
--export-textures (the fork's play builds). The export is skipped when --work already holds one (its bottom-up.txt), so
a second run goes straight on to the upscale, which itself resumes where it stopped. Every command is printed before it
runs; --dry-run prints them and stops. The pack goes to the game's own tex-world-4x folder unless --out says otherwise.
"""
import argparse, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
NAMES = ("i26.exe", "I26.exe", "i26", "I26", "i26.x86_64", "i26.arm64", "I26.app", "i26.app")

def find_game(path):
    """The program to start: PATH itself, the program inside a folder, or the binary inside a macOS .app."""
    if path.endswith(".app") or path.endswith(".app" + os.sep):
        macos = os.path.join(path, "Contents", "MacOS")
        bins = sorted(os.listdir(macos)) if os.path.isdir(macos) else []
        return os.path.join(macos, bins[0]) if bins else None
    if os.path.isfile(path): return path
    if os.path.isdir(path):
        for n in NAMES:
            p = os.path.join(path, n)
            if os.path.exists(p): return find_game(p)
    return shutil.which(path)

def parse(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--game", help="the I26 program, or its folder (not needed when --work already holds an export)")
    ap.add_argument("--install", help="the Interstate '76 folder (default: the one the game finds itself)")
    ap.add_argument("--work", default="i26-hd-work", help="a scratch folder for the export (default ./i26-hd-work; ~40 MB)")
    ap.add_argument("--out", help="where the pack goes (default: the game's tex-world-4x folder)")
    ap.add_argument("--device", choices=("cuda", "cpu"), help="cuda or cpu (default: cuda when torch has it)")
    ap.add_argument("--only", help="globs of texture names (a trial run: e.g. 'pp11*,cb_1bd*')")
    ap.add_argument("--dry-run", action="store_true", help="print the commands and stop")
    args = ap.parse_args(argv)
    args.export = os.path.join(os.path.abspath(args.work), "export")   # the game is given a full path
    if args.game: args.game = find_game(args.game) or (args.game if args.dry_run else ap.error(f"no I26 program at {args.game}"))
    elif not args.dry_run and not os.path.exists(os.path.join(args.export, "bottom-up.txt")):
        ap.error("--game is needed for the export (the I26 program or its folder)")
    if args.install and not os.path.isdir(args.install): ap.error(f"--install {args.install} is not a folder")
    return args

def commands(args):
    """The steps as (title, argv), the export only when the work folder has none."""
    steps = []
    if not os.path.exists(os.path.join(args.export, "bottom-up.txt")):
        game = ["--install", args.install] if args.install else []
        steps.append(("export the install's textures (the game opens a small window and closes it when done)",
                      [args.game or "i26", "--windowed", "--resolution", "640x480", "--", *game, "--export-textures", args.export]))
    up = [sys.executable, os.path.join(HERE, "upscale-textures.py"), args.export]
    rd = [sys.executable, os.path.join(HERE, "text-redraws.py"), args.export]
    if args.out: up += ["--out", args.out]; rd += ["--pack", args.out]
    if args.device: up += ["--device", args.device]
    if args.only: up += ["--only", args.only]       # text-redraws skips what the pack does not have
    steps.append(("upscale (downloads the model once; resumes if stopped)", up))
    steps.append(("set the small lettering again from fonts", rd))
    return steps

def torch_note():
    """A warning when the model would run on the CPU although an NVIDIA card is there (pip's torch on Windows is CPU only)."""
    try: import torch
    except ImportError: return "torch is not installed: pip install torch spandrel numpy pillow etcpak (docs/hd-textures.md)"
    if torch.cuda.is_available() or not shutil.which("nvidia-smi"): return None
    return (f"torch {torch.__version__} cannot use the NVIDIA card, so the model runs on the CPU (hours). For the card: "
            "pip install --force-reinstall torch torchvision --index-url https://download.pytorch.org/whl/cu126")

def main(argv=None):
    args = parse(argv)
    steps = commands(args)
    if not args.dry_run and args.device != "cpu" and (note := torch_note()): print("note: " + note)
    for i, (title, cmd) in enumerate(steps, 1):
        print(f"\n[{i}/{len(steps)}] {title}\n    " + subprocess.list2cmdline(cmd), flush=True)
        if args.dry_run: continue
        os.makedirs(args.work, exist_ok=True)
        r = subprocess.run(cmd)
        if cmd[0] != sys.executable:   # the game: it writes no exit code worth reading on every platform; check its output
            if not os.path.exists(os.path.join(args.export, "bottom-up.txt")):
                sys.exit(f"the export wrote nothing into {args.export}: is this a build with --export-textures, and did it find the install?")
            print(f"    {sum(n.endswith('.png') for n in os.listdir(args.export))} textures exported")
        elif r.returncode: sys.exit(r.returncode)
    if not args.dry_run: print("\ndone: start the game; Graphic Detail's World Textures and Car & Dash Textures rows read 4x")

if __name__ == "__main__":
    main()
