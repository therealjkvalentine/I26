#!/usr/bin/env python3
"""quiet-run: the one way to start the game (or the Godot editor's tools) for a test, capture or benchmark on a Windows desktop
someone is using (docs/dev-hooks.md, "Quiet runs").

  python tools/quiet_run.py [--appdata DIR] [--timeout S] [--log FILE] [GODOT] [engine args] [-- game args]
  tools/quiet-run.sh ...      (the same, from Git Bash)

GODOT is the Godot .NET console executable (default: $GODOT). On Windows the game starts on a desktop of its own (CreateDesktop,
a new one for each run, gone when the run ends): its windows, any console or crash dialog it opens, its focus and its z-order live
there, so it never takes the foreground, never shows over another window, never flashes on the taskbar and gets no keyboard or
mouse input; the GPU draws and presents its frames at full rate (screenshots, frame profiles and benchmarks are unchanged).
The run also gets, whatever the caller forgot:
  * `--audio-driver Dummy` before the `--` (another driver is refused),
  * I76_QUIET_WINDOW=1 unless it is set to another on value (min, behind): the game confines no pointer and reads no pad,
  * a scratch user folder: APPDATA from --appdata, else the caller's APPDATA when it is not the live one, else
    %TEMP%\\i26-quiet\\appdata (the live roaming folder, or one inside it, is refused),
  * stdin from NUL, no error dialogs (SetErrorMode), and a job object: at the timeout (default 900 s) or if this launcher is
    killed, the run's processes (the console wrapper, the engine, anything they started) end with it and nothing else does.
The exit code is the game's (124 on the timeout). Elsewhere than Windows the command runs as it is, with the same arguments and
environment (no separate desktop: tools/shot.sh renders off-screen on Linux).

As a module: quiet_run.run(cmd, env=..., cwd=..., stdout=file, timeout=s) -> exit code (raises subprocess.TimeoutExpired after
killing the run), for scripts that start the game themselves (tools/frame-cost/bench.py).
"""
import os, subprocess, sys

LIVE_HINT = "never the player's live user data (docs/dev-hooks.md, Quiet runs)"
QUIET_OFF = ("", "0", "off", "false", "no")


class Refused(Exception):
    pass


def live_appdata():
    """The roaming application-data folder Windows knows (not the environment's, which the caller may have changed)."""
    if os.name != "nt":
        return None
    import ctypes, uuid
    guid = uuid.UUID("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D")   # FOLDERID_RoamingAppData
    buf = ctypes.c_wchar_p()
    g = (ctypes.c_byte * 16).from_buffer_copy(guid.bytes_le)
    if ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(g), 0, None, ctypes.byref(buf)) != 0:
        return None
    path = buf.value
    ctypes.windll.ole32.CoTaskMemFree(buf)
    return path


def _norm(p):
    return os.path.normcase(os.path.normpath(os.path.abspath(p)))


def _inside(p, root):
    p, root = _norm(p), _norm(root)
    return p == root or p.startswith(root.rstrip("\\/") + os.sep)


def choose_appdata(given, env_appdata, live, temp):
    """The scratch user folder for the run: --appdata, else the environment's when it is not the live one, else TEMP\\i26-quiet\\appdata.
    Refused: the live folder or one inside it."""
    pick = given or (env_appdata if env_appdata and not (live and _inside(env_appdata, live)) else None) \
        or os.path.join(temp, "i26-quiet", "appdata")
    if live and _inside(pick, live):
        raise Refused(f"quiet-run: APPDATA {pick} is the live user folder or inside it; give --appdata SCRATCH_DIR ({LIVE_HINT})")
    return os.path.abspath(pick)


def fix_args(args):
    """The engine's arguments (before the first `--`) with `--audio-driver Dummy` first unless it is there; another driver is refused."""
    cut = args.index("--") if "--" in args else len(args)
    engine, game = list(args[:cut]), list(args[cut:])
    for i, a in enumerate(engine):
        drv = a.split("=", 1)[1] if a.startswith("--audio-driver=") else engine[i + 1] if a == "--audio-driver" and i + 1 < len(engine) else None
        if a.startswith("--audio-driver"):
            if drv != "Dummy":
                raise Refused(f"quiet-run: --audio-driver {drv} refused: test runs are silent (Dummy)")
            return engine + game
    return ["--audio-driver", "Dummy"] + engine + game


def fix_env(env, appdata):
    env = dict(env)
    env["APPDATA"] = appdata
    if env.get("I76_QUIET_WINDOW", "").strip().lower() in QUIET_OFF:
        env["I76_QUIET_WINDOW"] = "1"
    return env


# --- Windows: a desktop of its own, a job, no dialogs ---------------------------------------------------------------------------

def _win():
    import ctypes, ctypes.wintypes as W
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    u32 = ctypes.WinDLL("user32", use_last_error=True)

    class STARTUPINFOW(ctypes.Structure):
        _fields_ = [("cb", W.DWORD), ("lpReserved", W.LPWSTR), ("lpDesktop", W.LPWSTR), ("lpTitle", W.LPWSTR),
                    ("dwX", W.DWORD), ("dwY", W.DWORD), ("dwXSize", W.DWORD), ("dwYSize", W.DWORD),
                    ("dwXCountChars", W.DWORD), ("dwYCountChars", W.DWORD), ("dwFillAttribute", W.DWORD), ("dwFlags", W.DWORD),
                    ("wShowWindow", W.WORD), ("cbReserved2", W.WORD), ("lpReserved2", ctypes.c_void_p),
                    ("hStdInput", W.HANDLE), ("hStdOutput", W.HANDLE), ("hStdError", W.HANDLE)]

    class PROCESS_INFORMATION(ctypes.Structure):
        _fields_ = [("hProcess", W.HANDLE), ("hThread", W.HANDLE), ("dwProcessId", W.DWORD), ("dwThreadId", W.DWORD)]

    class SECURITY_ATTRIBUTES(ctypes.Structure):
        _fields_ = [("nLength", W.DWORD), ("lpSecurityDescriptor", ctypes.c_void_p), ("bInheritHandle", W.BOOL)]

    class BASIC(ctypes.Structure):
        _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64), ("LimitFlags", W.DWORD),
                    ("MinimumWorkingSetSize", ctypes.c_size_t), ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", W.DWORD),
                    ("Affinity", ctypes.c_size_t), ("PriorityClass", W.DWORD), ("SchedulingClass", W.DWORD)]

    class EXTENDED(ctypes.Structure):
        _fields_ = [("Basic", BASIC), ("Io", ctypes.c_uint64 * 6), ("ProcessMemoryLimit", ctypes.c_size_t),
                    ("JobMemoryLimit", ctypes.c_size_t), ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]

    u32.CreateDesktopW.restype = W.HANDLE
    u32.CreateDesktopW.argtypes = [W.LPCWSTR, W.LPCWSTR, ctypes.c_void_p, W.DWORD, W.DWORD, ctypes.c_void_p]
    u32.CloseDesktop.argtypes = [W.HANDLE]
    k32.CreateJobObjectW.restype = W.HANDLE
    k32.CreateFileW.restype = W.HANDLE
    k32.CreateFileW.argtypes = [W.LPCWSTR, W.DWORD, W.DWORD, ctypes.c_void_p, W.DWORD, W.DWORD, W.HANDLE]
    k32.GetStdHandle.restype = W.HANDLE
    k32.GetStdHandle.argtypes = [W.DWORD]
    k32.TerminateProcess.argtypes = [W.HANDLE, W.UINT]
    k32.SetErrorMode.argtypes = [W.UINT]
    k32.CreateProcessW.argtypes = [W.LPCWSTR, W.LPWSTR, ctypes.c_void_p, ctypes.c_void_p, W.BOOL, W.DWORD, ctypes.c_void_p, W.LPCWSTR,
                                   ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
    k32.SetInformationJobObject.argtypes = [W.HANDLE, ctypes.c_int, ctypes.c_void_p, W.DWORD]
    k32.AssignProcessToJobObject.argtypes = [W.HANDLE, W.HANDLE]
    k32.TerminateJobObject.argtypes = [W.HANDLE, W.UINT]
    k32.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
    k32.ResumeThread.argtypes = [W.HANDLE]
    k32.GetExitCodeProcess.argtypes = [W.HANDLE, ctypes.POINTER(W.DWORD)]
    k32.CloseHandle.argtypes = [W.HANDLE]
    k32.SetHandleInformation.argtypes = [W.HANDLE, W.DWORD, W.DWORD]
    return ctypes, W, k32, u32, STARTUPINFOW, PROCESS_INFORMATION, SECURITY_ATTRIBUTES, EXTENDED


_count = 0


def _run_windows(cmd, env, cwd, stdout, timeout, extra_flags=0):
    global _count
    import msvcrt
    ctypes, W, k32, u32, STARTUPINFOW, PROCESS_INFORMATION, SECURITY_ATTRIBUTES, EXTENDED = _win()
    _count += 1
    name = f"i26-quiet-{os.getpid()}-{_count}"
    # a desktop of the run's own on the interactive window station: the person's desktop never shows it, its windows never take
    # their foreground or input; DESKTOP access for the processes started on it (GENERIC_ALL)
    desk = u32.CreateDesktopW(name, None, None, 0, 0x10000000, None)
    if not desk:
        raise OSError(f"quiet-run: CreateDesktop failed ({ctypes.get_last_error()})")
    handles = []
    try:
        si = STARTUPINFOW()
        si.cb = ctypes.sizeof(si)
        si.lpDesktop = "WinSta0\\" + name
        si.dwFlags = 0x1 | 0x100   # STARTF_USESHOWWINDOW (the first window shown without activating, even there), STARTF_USESTDHANDLES
        si.wShowWindow = 4         # SW_SHOWNOACTIVATE
        sa = SECURITY_ATTRIBUTES(ctypes.sizeof(SECURITY_ATTRIBUTES), None, True)
        nul = k32.CreateFileW("NUL", 0xC0000000, 3, ctypes.byref(sa), 3, 0x80, None)
        handles.append(nul)
        si.hStdInput = nul
        if stdout is not None:
            out = msvcrt.get_osfhandle(stdout.fileno())
        else:
            out = k32.GetStdHandle(ctypes.c_uint32(-11).value)
            if not out or out == W.HANDLE(-1).value:
                out = nul
        k32.SetHandleInformation(out, 1, 1)   # HANDLE_FLAG_INHERIT
        err = out if stdout is not None else (k32.GetStdHandle(ctypes.c_uint32(-12).value) or out)
        k32.SetHandleInformation(err, 1, 1)
        si.hStdOutput, si.hStdError = out, err
        job = k32.CreateJobObjectW(None, None)
        handles.append(job)
        info = EXTENDED()
        info.Basic.LimitFlags = 0x2000 | 0x400   # KILL_ON_JOB_CLOSE, DIE_ON_UNHANDLED_EXCEPTION (no crash dialog waits)
        k32.SetInformationJobObject(job, 9, ctypes.byref(info), ctypes.sizeof(info))
        block = "".join(f"{k}={v}\0" for k, v in sorted(env.items(), key=lambda kv: kv[0].upper())) + "\0"
        envbuf = ctypes.create_unicode_buffer(block, len(block))
        # no error boxes (critical errors, faults, missing files): inherited by the run, so a crash ends the process at once
        old_mode = k32.SetErrorMode(0x1 | 0x2 | 0x8000)
        pi = PROCESS_INFORMATION()
        flags = 0x4 | 0x08000000 | 0x400 | extra_flags   # CREATE_SUSPENDED, CREATE_NO_WINDOW (no console window), CREATE_UNICODE_ENVIRONMENT
        ok = k32.CreateProcessW(None, ctypes.create_unicode_buffer(subprocess.list2cmdline(cmd)), None, None, True, flags,
                                envbuf, cwd, ctypes.byref(si), ctypes.byref(pi))
        k32.SetErrorMode(old_mode)
        if not ok:
            raise OSError(f"quiet-run: cannot start {cmd[0]} ({ctypes.get_last_error()})")
        handles += [pi.hProcess, pi.hThread]
        if not k32.AssignProcessToJobObject(job, pi.hProcess):
            k32.TerminateProcess(pi.hProcess, 1)
            raise OSError(f"quiet-run: cannot put the run in a job ({ctypes.get_last_error()})")
        k32.ResumeThread(pi.hThread)
        ms = 0xFFFFFFFF if timeout is None else int(timeout * 1000)
        if k32.WaitForSingleObject(pi.hProcess, ms) != 0:
            k32.TerminateJobObject(job, 124)
            k32.WaitForSingleObject(pi.hProcess, 10000)
            raise subprocess.TimeoutExpired(cmd, timeout)
        code = W.DWORD()
        k32.GetExitCodeProcess(pi.hProcess, ctypes.byref(code))
        k32.TerminateJobObject(job, code.value)   # whatever the run left behind
        return ctypes.c_int32(code.value).value
    finally:
        for h in reversed(handles):
            k32.CloseHandle(h)
        u32.CloseDesktop(desk)


def prepare(cmd, env=None, appdata=None):
    """The command and environment a quiet run uses (the checks above); raises Refused."""
    env = dict(os.environ if env is None else env)
    live = live_appdata()
    temp = env.get("TEMP") or env.get("TMPDIR") or "/tmp"
    ad = choose_appdata(appdata, env.get("APPDATA") if os.name == "nt" else None, live, temp)
    os.makedirs(ad, exist_ok=True)
    return [cmd[0]] + fix_args(cmd[1:]), fix_env(env, ad)


def run(cmd, env=None, cwd=None, stdout=None, timeout=None, appdata=None, flags=0):
    """Start cmd (GODOT and its arguments) quietly and wait; the exit code. subprocess.TimeoutExpired after the run is killed.
    flags: more process creation flags (Windows: ABOVE_NORMAL_PRIORITY_CLASS)."""
    cmd, env = prepare(cmd, env, appdata)
    if os.name != "nt":
        return subprocess.run(cmd, env=env, cwd=cwd, stdout=stdout, stderr=subprocess.STDOUT if stdout else None,
                              stdin=subprocess.DEVNULL, timeout=timeout).returncode
    return _run_windows(cmd, env, cwd, stdout, timeout, flags)


def main(argv):
    appdata, timeout, log = None, 900.0, None
    a = list(argv)
    while a and a[0] in ("--appdata", "--timeout", "--log", "-h", "--help"):
        k = a.pop(0)
        if k in ("-h", "--help"):
            print(__doc__)
            return 0
        if not a:
            print(f"quiet-run: {k} needs a value", file=sys.stderr)
            return 2
        v = a.pop(0)
        if k == "--appdata": appdata = v
        elif k == "--timeout": timeout = float(v) if float(v) > 0 else None
        else: log = v
    if a and a[0] == "--":   # `quiet_run.py -- GODOT ...` is read as `quiet_run.py GODOT ...`
        a.pop(0)
    if a and not a[0].startswith("-") and (a[0].lower().endswith(".exe") or os.path.isfile(a[0])):
        cmd = a
    elif os.environ.get("GODOT"):
        cmd = [os.environ["GODOT"]] + a
    else:
        print("quiet-run: no Godot executable: give it first or set GODOT", file=sys.stderr)
        return 2
    try:
        if log:
            with open(log, "w", encoding="utf-8", errors="replace") as f:
                return run(cmd, stdout=f, timeout=timeout, appdata=appdata)
        return run(cmd, timeout=timeout, appdata=appdata)
    except Refused as e:
        print(e, file=sys.stderr)
        return 2
    except subprocess.TimeoutExpired:
        print(f"quiet-run: timeout after {timeout:g} s: the run was killed", file=sys.stderr)
        return 124


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
