# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read first

Everything is experimental right now: planning-kit rules are working hypotheses the director revises freely
(DIRECTOR.md); don't present a planning default as a director prohibition. `AGENTS.md` is authoritative. Before work, read `questsim_planning_kit/{AGENTS,CURRENT_TASK,STATE,RUNBOOK}.md`
(and `HANDOFF_2026-10-25.md` when resuming). `DIRECTOR.md` (2026-10-25 decisions) overrides older
planning defaults. Consult only the relevant sections of `ENGINE_RULES.md` / `BACKLOG.md`; research and
video docs are not required context for ordinary coding.

Working rules that come from those files:
- Implement only the current task in `CURRENT_TASK.md`; stop at its acceptance gate. Next planned task
  is chosen by the director (STATE.md); Q02b's single wall does not authorize a construction system.
- No speculative interfaces, frameworks, ECS, migration/transaction frameworks or new dependencies without a current need.
- One implementation agent; at most one bounded helper; no nested delegation.
- On acceptance, update `STATE.md` with evidence and the next action. Never claim tests/platforms that were not run.
- Commit verified checkpoints; push to `origin main` periodically (authorized). Never force-push; if the
  remote advanced, fetch and preserve it first. After pushing, confirm `git ls-remote origin refs/heads/main` equals `git rev-parse HEAD`.
- Never touch real play data in `.artifacts/play/` (world save + profile identity tokens). Tests use fresh disposable fixtures.

## Toolchain

All tools are local to the repo (no global installs): Godot 4.6.2 mono in `.tools/godot/`, .NET caches in
`.local/`, logs/screenshots/raw evidence in `.artifacts/` (all gitignored). `global.json` pins .NET SDK
10.0.110 exactly (`rollForward: disable`). GUI tests need X11 `DISPLAY=:0`, xdotool, ImageMagick, Pillow.

## Commands

```bash
bash scripts/install_godot.sh          # pinned, SHA-256-checked Godot download into .tools/
bash scripts/check.sh                  # offline Release build + domain checks (no engine); log in .artifacts/logs/checks.log
bash scripts/build_game.sh             # Godot C# build of src/QuestSim.Game; must be zero warnings/errors
bash scripts/run_soak.sh [--days 30]   # F19 soak: fast-forward population + economy, fails on degenerate history

bash scripts/run_server.sh [--port=N] [--bind=IP] [--save=PATH]   # headless authoritative server, default 127.0.0.1:27840
bash scripts/run_client.sh alice [--host=...] [--port=N]          # each client in its own terminal; profile name is arg 1
```

Testing is local-first (director 2026-10-26): `bash scripts/test_all.sh` runs everything with a summary
(`--quick` for domain + core ENet suites, `--all` adds the 8-player load test and the 30-day soak). The GitHub workflow is manual-only.
A Windows test machine is reachable as `ssh kings@whitetop` (Git Bash shell, repo at `~/Projects/QuestSim`, sandbox:
admin rights, safe to install tools); see RUNBOOK "Windows test machine". Test scripts must stay cross-platform
(`pause()`/`gpu_handles()` in verify_multiplayer.py, no POSIX signals, `python` vs `python3`).

Acceptance/regression suites (real server/client processes over ENet; headless unless noted):

```bash
python3 scripts/verify_build.py              # Q02b wall race/replay/recovery/migration
python3 scripts/verify_build_screen.py       # graphical clients on a private Xvfb (self re-execs under xvfb-run)
python3 scripts/verify_items.py              # Q02a item ENet/recovery/migration
python3 scripts/verify_multiplayer.py        # Q01 network regressions
python3 scripts/verify_living.py / verify_defeat.py / verify_content.py   # nightly living world, defeat, mods
python3 scripts/verify_save_failures.py      # persistence fault cases
```

Population experiment (E1, `experiments/population/`, headless, not part of the game): `bash scripts/run_population.sh
check|bench|run|density`. GLSL kernels in `shaders/` must stay integer-only and match the C# reference in
`CpuBackend.cs` exactly; `check` verifies CPU vs Vulkan bit-identical state. Wrap big runs in
`systemd-run --user --scope -p MemoryMax=16G -p MemorySwapMax=0` (no swap on this machine).

GUI testing: prefer the Xvfb pattern in `verify_build_screen.py` — intent-file `keys` go through
`Input.ParseInputEvent`, intent-file `capture` saves a viewport PNG to inspect. Under Xvfb (Mesa llvmpipe)
Godot logs a V-Sync WARNING that clean-log assertions must ignore. `verify_item_desktop.py`,
`verify_shared_desktop.py` and `verify_terminal_shutdown.py` use xdotool on the real desktop; ask before running them.

There is no test framework or single-test filter: `checks/QuestSim.Checks` is a console app where each
`Check(cond, description)` prints `PASS ...` or throws. Add checks to `AuthorityChecks.cs` / `PickupChecks.cs`
(wired up from `Program.cs`). Build scripts deliberately pass `-p:DisableTransitiveFrameworkReferenceDownloads=true`
and disable compiler servers; keep those flags when invoking `dotnet` directly.

## Architecture

- `src/QuestSim.Domain/` — plain C# (no Godot dependency). `SharedWorld.cs` holds everything authoritative:
  save records (`WorldSave` with fixed content `InitialSupplies`/`BuildSite`, `CharacterRecord`, `SupplyState`,
  `PickupReceipt`, `BuildReceipt`), `WorldAuthority` (join/leave, movement intents, fixed 20 Hz `Step`,
  `TakeItem`, `PlaceWall`, snapshots) and `WorldStorage`
  (load/validate/migrate, atomic `.tmp` + replace writes). This is what `checks/` tests offline.
- `src/QuestSim.Game/` — one Godot project used for both roles. `Session.cs` parses user args after `--`;
  `--server` (must be `--headless`) owns a `WorldAuthority`, holds an exclusive `<save>.lock`, and serves ENet RPCs;
  otherwise it is a client that sends intents/requests and renders snapshots via `RoomView.cs` (walls added after the
  first snapshot are drawn incrementally). Server never creates view nodes. Client profiles persist the last
  pickup/build request before sending so T can replay it. Test-only hooks: `--intent-file` (movement, pickup/build
  requests, `keys`, `capture`), `--status=PATH`, `--shutdown-file=PATH`.
- Nightly v1.0 domain files: `CityGen.cs` (seeded integer world, chunks), `Population.cs` (coarse agents, rumor
  spread), `Living.cs` (tiers, NPC behaviour, combat), `Defeat.cs` (defeat outcomes, item ledger, Virtual Game Master),
  `Rumors.cs` (facts → rumors, asking, faction standing), `PopulationStore.cs` (snapshots), `Content.cs` (data items, mods),
  `Economy.cs` (F19: four goods per settlement, hourly, saved; `tools/QuestSim.Soak` fast-forwards it; client `MapView.cs` = M debug map).
  The authority touches population arrays only between coarse ticks (background task). All item movements between
  holders go through `Transfer` (ledgered); never edit inventories of named NPCs or caches directly.
- `experiments/presentation/` — the preserved, disposable Q00 2D/sprite3D/poly3D comparison (`run_presentation.sh`); not the game.
- `src/QuestSim.Game/evidence/`, `experiments/presentation/evidence/` — small versioned results/screenshots; raw evidence stays in `.artifacts/`.

Core invariants (see ENGINE_RULES.md "World invariants"):
- Clients submit intent only; the server validates and commits. Persistent identity is server GUIDs, never peer IDs or node IDs.
- Pickups and wall builds are exactly-once: actor-scoped request IDs produce durable receipts saved atomically with
  inventory, stock and blocks *before* success is acknowledged; retries return the original outcome. `Validate`
  cross-checks stock/inventory/receipts/walls (taken = initial − stock; held panels = taken − spent), so never
  hand-edit stock or prune receipts. A new item, cost or crate needs matching validation rules.
- Fixed content positions matter to tests: Q01 movement suites walk Alice +X along z=0 and Bob ±Z along x=−4.
- World time pauses with zero joined characters. Disconnected characters vanish but keep identity/inventory.
- Versioning: save schema 5, wire `ProtocolVersion` 7, content ID `questsim:city-v1`. Unknown/incomplete
  saves fail closed. Changing IDs, saves or messages requires stating compatibility and backing up saves before
  migration (v1–v4 saves get a byte-exact `.v<n>-<guid>.bak`; `WorldStorage.Load` chains v1→v2→v3→v4→v5). Protocol changes require rebuilding server and all clients.
- Targets: Windows and Linux (macOS stretch goal); Vulkan for GPU compute. Verified so far: Linux and native Windows 11 (whitetop, RX 5700; full `test_all.sh --all` minus Xvfb screens), loopback 2–8 players,
  one Linux client against a Windows server over LAN. Exports, WAN, a graphical Windows client and power-loss durability are unverified.
