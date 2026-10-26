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
`git log --oneline` now verifies real checkpoints. GitHub origin/main synchronization
was authorized afterward; see the GitHub checkpoint section below.

## Build and launch

| Command | Result / purpose |
| --- | --- |
| `bash scripts/install_godot.sh` | Pinned official archive, SHA-256 checked, local extraction; preserves existing installation. Initial download/extraction steps verified manually; preservation branch verified through script. |
| `bash scripts/check.sh` | Offline Release build, 38 domain checks including seven original fixture checks; real network checks are separate below. |
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

2D uses a fixed overhead camera: Q/E, arrows and C are inactive. In 3D, Q/E
turns/orbits, Up/Down tilts, and C switches first/third person. WASD moves in both;
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
Q00 proved desktop access only. Q01 server/client/persistence verification is
recorded below; standalone exports and other platforms remain untested.

## Q01 — authoritative shared room

Historical v1 behavior below; current v2 item behavior and migration are at the end.

The low-poly 3D shared game is `src/QuestSim.Game/`. The presentation lab above
remains a separate disposable comparison. Q01 has no NPCs or shared construction
transactions; its yellow module is a static server-owned obstacle.

Run from QuestSim, each launch in a **separate terminal**:

```bash
bash scripts/build_game.sh
bash scripts/run_server.sh
bash scripts/run_client.sh alice
bash scripts/run_client.sh bob
```

The server uses CPU/headless Godot with no RoomView/Node3D creation. Default bind:
`127.0.0.1`, UDP port `27840`, maximum 8 connections (only **two validated**).
Override with `--port=...`; clients also accept `--host=...`. For an explicit LAN
test, server can use `--bind=0.0.0.0`; no firewall/router changes are made by scripts.
LAN/WAN behavior has not been verified. No public service/authentication built.

WASD moves relative to camera; Q/E turns/orbits; Up/Down tilts; C switches
first/third person; R resets **camera only**. Movement always comes from the server.
Blue is your character, orange is another player. Close a client window to leave;
relaunch the same profile name to reconnect. On server loss the client clears
active visuals and shows disconnected status; restart it to retry (no auto-retry).

Default data: `.artifacts/play/world.json` and `.artifacts/play/profiles/{name}.json`.
Profile files retain local identity tokens; preserve them along with the world.
Server-owned character GUIDs differ from temporary network peer IDs. Profiles are
co-op identity, not authenticated accounts. Copying an active identity is rejected;
there is no account ownership protection against impersonation after logout.
No credentials or actual play saves are committed.

World save format **1**, content ID `questsim:q01-room-v1`, wire protocol **1**:
no earlier game save/wire format existed. Required fields and version/content are
validated; unsupported/incomplete saves fail without reset/overwrite. Saves contain
characters, positions, inventory dictionaries, experience, canonical block geometry
and simulation ticks. Active connections/held intents are not saved. Initial
inventory/progression are empty/zero; there is no item or progression gameplay yet.
The acceptance suite seeds non-empty values offline to prove round-trip retention.

The server holds an exclusive `.lock` file handle while owning a world; the lock
file remaining on disk is normal. Saves flush a `.tmp` file, then atomically replace
the previous save on join/leave, every 2s while active and normal shutdown. A failed
startup never writes loaded state; failed final persistence exits nonzero. Forced
kill/power loss can lose changes since the last checkpoint (up to ~2s under ordinary
running); directory-fsync/power-loss durability and migrations are not claimed.

Uses Godot’s [native ENet/RPC API](https://docs.godotengine.org/en/4.6/tutorials/networking/high_level_multiplayer.html).
Authority ticks at 20Hz; input/snapshots at 10Hz. Ticks stop with zero joined
characters, while network/status polling continues. Restart does not add wall time.
Idle quest timing will eventually use this clock; no quest system exists yet.
Input expires after five world ticks (~250ms). Remote loss removes a body when ENet
confirms disconnection; timeout policy is configured, not an instantaneous unplug
promise. Re-entry validates room/blocks/other active players and searches nearby
free grid cells; it refuses if none is safe. Rendering interpolates snapshots;
no client prediction or WAN smoothing work has been done.

Verified checks (fresh disposable `.artifacts/q01/*` directories, never play saves):

```bash
bash scripts/check.sh                         # Q01 baseline: 24; current total: 38
python3 scripts/verify_multiplayer.py          # 12 real ENet checks
python3 scripts/verify_save_failures.py         # 4 startup/shutdown persistence faults
python3 scripts/verify_shared_desktop.py        # 6 real two-window checks + screenshots
python3 scripts/verify_terminal_shutdown.py     # 3 launcher/Ctrl-C/disconnected-state checks
```

The headless network test's explicit `--intent-file` option feeds the same client
movement RPC as keyboard input. It does not inject server state. Changed geometry
and non-empty inventory/progression are fixtures edited only after all processes
stop. `--status=PATH` writes optional atomic diagnostics; `--shutdown-file=PATH`
allows controlled test shutdown. Neither is needed for ordinary play.

Q01_RESULTS.md records measured startup/memory, verified checks and known limits.
Other OS/hardware, 2–8 load, exported binaries, packet-loss/latency behavior and
crash/power-loss durability remain unverified. The old Q00 frame samples are not
measurements of this networked scene. Full raw evidence stays in `.artifacts/`.

## GitHub checkpoints — authorized 2026-10-25

Origin: `git@github.com:adrik0622/QuestSim.git`; `main` tracks `origin/main`.
Director authorized periodic pushes of verified milestones. Initial `237c943` and
Q02 runtime `60d8357` pushes succeeded; final docs/evidence follow. Never force-push.
If the remote advances independently, fetch/inspect/preserve that work before pushing.

```bash
git status --short
git push origin main
git ls-remote origin refs/heads/main
git rev-parse HEAD
```

Confirm remote main equals local HEAD after a claimed sync. Build caches, tools,
raw logs, play saves and profile tokens stay ignored; concise synthetic test
screenshots/results are versioned. No global Git/SSH configuration changed.

## Q02a — one bandage, durable retry

Build and launch commands stay the same. Rebuild **all** game processes for wire
protocol 2; old clients are incompatible. Save schema 2/content
`questsim:q02-room-v1` loads new fields for supply and durable receipts. Valid Q01
saves/profiles migrate as described in Q02_RESULTS.md, with an immutable
`.v1-<guid>.bak` copy before any v2 replacement. Unknown/incomplete saves still refuse.

- **F:** make a new pickup attempt at the green crate (within two metres).
- **T:** resend the saved attempt and target, including after reconnect/restart.
- HUD shows your bandage count and shared stock; crate turns grey when empty.
- Moving closer after a rejected attempt requires **F**, since **T** replays the
  original outcome. No use/drop/trade/refill or general inventory UI exists yet.
- Yellow construction module stays static; only the green crate is interactive.

Server saves inventory, stock and receipt before reporting success. Normal save
and crash/power-loss limits above remain; one atomic file is not a distributed
transaction/power-loss guarantee. Never prune receipts or edit stock to refill this
probe; v2 validation intentionally checks those facts agree. For a fresh test
without altering your play save, stop the current server and use a **new** path:

```bash
bash scripts/run_server.sh --save=.artifacts/play/fresh-item-test.json
# In other terminals: bash scripts/run_client.sh alice / bob
```

Current verified checks:

```bash
bash scripts/check.sh                         # 38 domain/fixture checks
bash scripts/build_game.sh                    # zero warnings/errors
python3 scripts/verify_items.py                # 12 item ENet/recovery/migration checks
python3 scripts/verify_item_desktop.py         # 4 actual F/T/reconnect checks
python3 scripts/verify_multiplayer.py          # 12 Q01 network regression checks
python3 scripts/verify_save_failures.py        # 4 Q01 persistence fault checks
python3 scripts/verify_terminal_shutdown.py    # 3 launcher/Ctrl-C checks
```

All use fresh disposable fixtures; actual play saves are not edited by tests.
The lost-reply test sends SIGSTOP/CONT/KILL only to its tracked test children.
It never suspends user processes. Q02_RESULTS.md contains evidence and limitations.

## Q02b — one wall panel, durable retry

Rebuild **all** processes: save/wire protocol 3, content `questsim:q02b-room-v1`.
Valid v2 saves get an immutable `.v2-<guid>.bak` before v3 replacement.

- **F:** take from the nearest stocked crate in reach (green bandage, blue panels).
- **B:** place a panel wall on the orange build cell (needs a panel, within 2m).
- **T:** resend the last saved attempt, pickup or build.

Current verified checks (Q02b_RESULTS.md has details):

```bash
bash scripts/check.sh                         # 54 domain/fixture checks
bash scripts/build_game.sh                    # zero warnings/errors
python3 scripts/verify_build.py               # 11 wall ENet/recovery/migration checks
python3 scripts/verify_build_screen.py        # 4 F/B/T/C checks on private Xvfb + PNGs
python3 scripts/verify_items.py               # 12 item regressions
python3 scripts/verify_multiplayer.py         # 12 Q01 network regressions
python3 scripts/verify_save_failures.py       # 4 persistence fault checks
```

Prefer `verify_build_screen.py` over real-desktop xdotool suites: it re-executes
itself under `xvfb-run`, sends keys with `Input.ParseInputEvent` (intent-file
`keys`) and saves viewport PNGs (intent-file `capture`). `xvfb-run python3
scripts/verify_terminal_shutdown.py` passes its 3 behavior checks but its
clean-log assertion trips on llvmpipe's V-Sync warning. `verify_item_desktop.py`
and `verify_shared_desktop.py` need the real desktop and were not rerun for Q02b.
