#!/bin/bash
# quiet-run.sh [--appdata DIR] [--timeout S] [--log FILE] [GODOT] [engine args] [-- game args]
# The standard way to start the game for a test, capture or benchmark on a Windows desktop someone is using: see tools/quiet_run.py
# (a desktop of its own, Dummy audio, I76_QUIET_WINDOW, a scratch APPDATA, a job that ends with it) and docs/dev-hooks.md "Quiet runs".
here=$(cd "$(dirname "$0")" && pwd)
for py in python3 python py; do
  if command -v "$py" >/dev/null 2>&1 && "$py" -c 'import sys; sys.exit(sys.version_info < (3, 8))' 2>/dev/null; then
    exec "$py" "$here/quiet_run.py" "$@"
  fi
done
echo "quiet-run: no Python 3 on PATH" >&2
exit 2
