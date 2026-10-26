#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
profile="${1:-player}"
if [ "$#" -gt 0 ]; then shift; fi
exec .tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --path src/QuestSim.Game -- "--profile=$profile" "$@"
