#!/usr/bin/env bash
# Local-only, pinned official release; no global installs. Linux, or Windows under Git Bash.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -e .tools/godot ]; then
    echo '.tools/godot already exists; preserving it.'
    exit 0
fi
if [ "${OS:-}" = Windows_NT ]; then
    build=mono_win64 sha=7c530d4b45d9f7aadd788b85a4d9dbdb5f2a239a72999afd7d29fda506fbedb6
    engine=Godot_v4.6.2-stable_mono_win64_console.exe
else
    build=mono_linux_x86_64 sha=7d53302c31648ad98b620e8ca5b0c869c1066495770e62b2fa770cfeb004f167
    engine=Godot_v4.6.2-stable_mono_linux.x86_64
fi
mkdir -p .tools/downloads .artifacts/logs
curl -fL --retry 2 "https://github.com/godotengine/godot/releases/download/4.6.2-stable/Godot_v4.6.2-stable_$build.zip" -o .tools/downloads/godot-4.6.2.zip
printf '%s  %s\n' "$sha" '.tools/downloads/godot-4.6.2.zip' | sha256sum -c -
unzip -q .tools/downloads/godot-4.6.2.zip -d .tools
mv ".tools/Godot_v4.6.2-stable_$build" .tools/godot
chmod u+x ".tools/godot/$engine"
".tools/godot/$engine" --version
