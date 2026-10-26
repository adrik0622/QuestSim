#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mode="${1:-2d}"
camera="${2:-third}"
case "$mode" in 2d|sprite3d|poly3d) ;; *) echo 'Mode must be 2d, sprite3d or poly3d' >&2; exit 2 ;; esac
case "$camera" in first|third|overhead) ;; *) echo 'Camera must be first, third or overhead' >&2; exit 2 ;; esac
exec .tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --path experiments/presentation res://Lab.tscn -- "--mode=$mode" "--camera=$camera"
