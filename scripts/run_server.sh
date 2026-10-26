#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
exec .tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --headless --path src/QuestSim.Game -- --server "$@"
