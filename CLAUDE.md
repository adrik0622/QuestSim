# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read first

`AGENTS.md` is authoritative. Before work, read `questsim_planning_kit/{AGENTS,CURRENT_TASK,STATE,RUNBOOK}.md`
(and `HANDOFF_2026-10-25.md` when resuming). `DIRECTOR.md` (2026-10-25 decisions) overrides older
planning defaults. Consult only the relevant sections of `ENGINE_RULES.md` / `BACKLOG.md`; research and
video docs are not required context for ordinary coding.

Working rules that come from those files:
- Implement only the current task in `CURRENT_TASK.md`; stop at its acceptance gate. Next planned task
  (per STATE.md) is Q02b: one authoritative modular wall placement — not a construction system.
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

bash scripts/run_server.sh [--port=N] [--bind=IP] [--save=PATH]   # headless authoritative server, default 127.0.0.1:27840
bash scripts/run_client.sh alice [--host=...] [--port=N]          # each client in its own terminal; profile name is arg 1
```

Acceptance/regression suites (real processes, ENet, and for desktop ones real X11 input + screenshots):

```bash
python3 scripts/verify_items.py              # Q02 item ENet/recovery/migration
python3 scripts/verify_item_desktop.py       # Q02 F/T GUI checks
python3 scripts/verify_multiplayer.py        # Q01 network regressions
python3 scripts/verify_save_failures.py      # persistence fault cases
python3 scripts/verify_terminal_shutdown.py  # launcher/Ctrl-C shutdown
python3 scripts/verify_shared_desktop.py     # Q01 two-window checks
```

There is no test framework or single-test filter: `checks/QuestSim.Checks` is a console app where each
`Check(cond, description)` prints `PASS ...` or throws. Add checks to `AuthorityChecks.cs` / `PickupChecks.cs`
(wired up from `Program.cs`). Build scripts deliberately pass `-p:DisableTransitiveFrameworkReferenceDownloads=true`
and disable compiler servers; keep those flags when invoking `dotnet` directly.

## Architecture

- `src/QuestSim.Domain/` — plain C# (no Godot dependency). `SharedWorld.cs` holds everything authoritative:
  save records (`WorldSave`, `CharacterRecord`, `SupplyState`, `PickupReceipt`), `WorldAuthority`
  (join/leave, movement intents, fixed 20 Hz `Step`, `TakeItem`, snapshots) and `WorldStorage`
  (load/validate/migrate, atomic `.tmp` + replace writes). This is what `checks/` tests offline.
- `src/QuestSim.Game/` — one Godot project used for both roles. `Session.cs` parses user args after `--`;
  `--server` (must be `--headless`) owns a `WorldAuthority`, holds an exclusive `<save>.lock`, and serves ENet RPCs;
  otherwise it is a client that sends intents/requests and renders snapshots via `RoomView.cs`. Server never creates view nodes.
  Test-only hooks: `--intent-file`, `--status=PATH`, `--shutdown-file=PATH`.
- `experiments/presentation/` — the preserved, disposable Q00 2D/sprite3D/poly3D comparison (`run_presentation.sh`); not the game.
- `src/QuestSim.Game/evidence/`, `experiments/presentation/evidence/` — small versioned results/screenshots; raw evidence stays in `.artifacts/`.

Core invariants (see ENGINE_RULES.md "World invariants"):
- Clients submit intent only; the server validates and commits. Persistent identity is server GUIDs, never peer IDs or node IDs.
- Item transfers are exactly-once: actor-scoped request IDs produce durable receipts saved atomically with
  inventory and stock *before* success is acknowledged; retries return the original outcome. `Validate` cross-checks
  stock/inventory/receipts, so never hand-edit stock or prune receipts.
- World time pauses with zero joined characters. Disconnected characters vanish but keep identity/inventory.
- Versioning: save schema 2, wire `ProtocolVersion` 2, content ID `questsim:q02-room-v1`. Unknown/incomplete
  saves fail closed. Changing IDs, saves or messages requires stating compatibility and backing up saves before
  migration (v1 saves get a byte-exact `.v1-<guid>.bak`). Protocol changes require rebuilding server and all clients.
- Supported only: local Linux, loopback, two clients. Exports, LAN/WAN, 2–8 load and power-loss durability are unverified.
