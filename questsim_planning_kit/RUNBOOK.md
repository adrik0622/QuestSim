# QuestSim runbook

Run from `/home/adrikingston/Projects/QuestSim`. No global packages installed.
Downloads are in `.tools/`; .NET caches in `.local/`; full logs, screenshots and
raw metrics in `.artifacts/`. These generated directories are ignored by Git.

## Verified toolchain — 2026-10-25

- Manjaro linux-x64; .NET SDK **10.0.110**, runtime **10.0.10**, MSBuild 18.0.11.
  `global.json` pins the installed SDK exactly.
- Godot **4.6.2.stable.mono.official.71f334935**, Linux x86_64 .NET distribution.
  Keep its GodotSharp directory beside the binary. The local NuGet.Config uses its
  bundled SDK packages; Godot and the net10.0 projects build and launch together.
- X11 `DISPLAY=:0`; Python 3.14, Pillow, xdotool and ImageMagick `import` installed.
- Ryzen 9 5950X, 32 GiB RAM, RTX 3080 Ti, NVIDIA 610.57.04. Desktop was in ordinary
  use during tests; not a controlled or minimum-spec benchmark machine.

Initial sandbox DNS and X11 EPERM failures are **resolved after full access**.
No bridge, privilege change or modification to the sibling project was needed.
Original sandbox checkpoints remain in `.artifacts/checkpoints/000-*` and `001-*`.
After full access .git was absent; a normal local repository was initialized.
`git log --oneline` now verifies real checkpoints. No remote configured or pushed.

## Build and launch

| Command | Result / purpose |
| --- | --- |
| `bash scripts/install_godot.sh` | Pinned official archive, SHA-256 checked, local extraction; preserves existing installation. Initial download/extraction steps verified manually; preservation branch verified through script. |
| `bash scripts/check.sh` | Offline Release build and seven domain fixture checks; no network/engine/persistence claim. |
| `bash scripts/build_presentation.sh` | Godot C# Debug build, zero warnings/errors. |
| `.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --headless --path experiments/presentation --quit-after 2` | Bootstrap smoke; prints `QUESTSIM_BOOTSTRAP_READY actors=4`. This is not an authoritative server. |
| `bash scripts/run_presentation.sh 2d` | Overhead sprites. |
| `bash scripts/run_presentation.sh sprite3d third` | 3D room with billboard sprites; `first` also available. |
| `bash scripts/run_presentation.sh poly3d third` | 3D room with primitive polygon characters; `first` also available. |
| `python3 scripts/desktop_test.py` | Launches bootstrap, verifies owned window, captures before/after and confirms a focused Space press. |
| `python3 scripts/verify_presentation.py` | Real X11 keyboard/mouse acceptance tests and screenshots for all three modes and both 3D cameras. |
| `python3 scripts/benchmark_presentation.py` | Five sequential rendered-window samples: 10s warmup + 30s frame intervals each; writes `.artifacts/benchmarks/*.json`. |

The standalone domain check avoids downloading unused ASP.NET targeting packs via
`DisableTransitiveFrameworkReferenceDownloads=true`. Build scripts disable compiler
servers for reproducibility in restricted sessions; initial diagnosis is retained
in `.artifacts/logs/restore-first-failure.log` and `restore-diagnosis.log`.

Godot source: [official 4.6.2 archive](https://godotengine.org/download/archive/4.6.2-stable/).
Archive SHA-256: `7d53302c31648ad98b620e8ca5b0c869c1066495770e62b2fa770cfeb004f167`,
verified against official GitHub release asset metadata and downloaded bytes.
Installed prerequisites follow the [official C# documentation](https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html).

## Controls and scenario

WASD moves; Q/E turns/orbits in 3D; arrows tilt; C switches first/third person;
R resets player/camera, retaining construction; Escape cancels selection. Click the
yellow cube to select, then a ground cell to move it. Red preview rejects walls,
actors, platforms and bounds. Space logs a state snapshot for acceptance assertions.
These are disposable keyboard camera controls; no mouse-look/gamepad/production feel.

Same 12m room, four deterministic moving actors, 2m doorway, three raised steps and
one 1m construction cube. Blue player plus four NPCs = five visible actors in third
person/overhead; the player's own mesh is hidden in first person. World coordinates
remain independent of rendered nodes. NPCs intentionally overlap; no avoidance or
NPC collision. Basic player wall/piece checks are a presentation proxy, not physics
or authoritative simulation acceptance. Steps do not prove stacked-floor support.

## Desktop method and limits

Read-only inspection found the running sibling `../pokeys-100-days-gone/scripts/desktop_control.py`.
Its Project Zomboid launcher/queue was not invoked or changed. QuestSim's small
`scripts/desktop_test.py` reuses the approach with native subprocess calls:
launch a fixed QuestSim scene, match PID plus exact title (including Godot's DEBUG
suffix), activate and confirm focus, bound input holds and release keys in finally,
then capture only the owned window and inspect the resulting screenshot/log.
No arbitrary window attachment, network listener or background service.

Direct X11 now works. If future sandbox policy blocks it again, these commands
cannot grant permissions. The fallback proposal is a foreground, QuestSim-only
file-queue helper launched from a normal desktop terminal, following the sibling
pattern. It is unnecessary now and has not been implemented/tested.

GUI checks briefly focus QuestSim. Keep unrelated apps out of the foreground during
the bounded suite. Test processes terminate afterward; the sibling helper stays running.
No desktop exports, remote platforms, server, two-client networking, persistence,
reconnect or empty-world clock have been implemented/tested yet.
