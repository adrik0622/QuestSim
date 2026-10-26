#!/usr/bin/env bash
# Local-only, pinned official release; no global installs.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -e .tools/godot ]; then
    echo '.tools/godot already exists; preserving it.'
    exit 0
fi
mkdir -p .tools/downloads .artifacts/logs
curl -fL --retry 2 https://github.com/godotengine/godot/releases/download/4.6.2-stable/Godot_v4.6.2-stable_mono_linux_x86_64.zip -o .tools/downloads/godot-4.6.2.zip
printf '%s  %s\n' '7d53302c31648ad98b620e8ca5b0c869c1066495770e62b2fa770cfeb004f167' '.tools/downloads/godot-4.6.2.zip' | sha256sum -c -
unzip -q .tools/downloads/godot-4.6.2.zip -d .tools
mv .tools/Godot_v4.6.2-stable_mono_linux_x86_64 .tools/godot
chmod u+x .tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64
.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --version
