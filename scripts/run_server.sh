#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
engine=.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64
if [ "${OS:-}" = Windows_NT ]; then engine=.tools/godot/Godot_v4.6.2-stable_mono_win64_console.exe; fi
exec "$engine" --headless --path src/QuestSim.Game -- --server "$@"
