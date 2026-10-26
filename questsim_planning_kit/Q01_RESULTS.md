# Q01 — two-client increment, 2026-10-25

Verified: one CPU-only dedicated authority, two independent Godot clients in a
small low-poly 3D room, shared movement/presence, persistent reconnect and an
empty-server-paused simulation clock. First/third-person cameras are retained.
The presentation comparison remains separately preserved; broader world structure
and production asset work have not been decided by this one-floor scene.

## Evidence

- **24 plain C# checks:** original fixture plus authority clock, duplicate joins,
  safe spawn, rejected invalid/stale input, server movement rate, input expiry,
  disconnect absence, save/reload, stable identity/inventory/progression, changed-
  geometry safe re-entry, missing/incompatible-save refusal.
- **12 real ENet checks:** dedicated server and separate headless client processes;
  late join, exact convergence at rest, independent movement, duplicate identity
  refusal, invalid/stale input rejection, doorway/boundary movement, abrupt leave,
  clock behavior, server restart, reconnect, non-empty character data preservation,
  changed-world re-entry and unsupported-save startup failure.
- **4 persistence fault checks:** second writer rejected even on another port;
  port-conflict and diagnostic-startup failures leave the loaded save untouched;
  failed final persistence returns exit code 1 and preserves the last valid save.
- **3 terminal checks:** the exact shell launchers create separate server and
  client windows; server Ctrl-C saves live state and exits 0; clients clear active
  state and show disconnection afterward.
- **6 desktop checks:** two PID/title-verified windows; actual WASD input in each
  appears in the other; first/third-person switching and heading replication;
  close removes body; same-profile return restores body/position; last close pauses.
  Screenshots captured and inspected. No scripted intent files used for GUI input.

Build: Godot 4.6.2 .NET + SDK 10.0.110/runtime 10.0.10, zero warnings/errors.
Server display backend was `headless`, with no `/dev/nvidia*` or `/dev/dri*` handles.
Client-only RoomView/Node3D objects are not constructed on the server.

[Client A sees B move](../src/QuestSim.Game/evidence/shared-alice-observes-bob.png) ·
[Client B sees A move](../src/QuestSim.Game/evidence/shared-bob-observes-alice.png) ·
[After B leaves](../src/QuestSim.Game/evidence/shared-alice-after-bob-leaves.png) ·
[After B returns](../src/QuestSim.Game/evidence/shared-alice-after-bob-returns.png) ·
[First person](../src/QuestSim.Game/evidence/shared-alice-first-person.png)

Raw evidence directories:
`.artifacts/q01/network-1792984962274675956/`,
`.artifacts/q01/save-failures-1792984978108524350/`,
`.artifacts/q01/desktop-1792984044700872508/`.
Concise metrics/screenshots are versioned in `src/QuestSim.Game/evidence/`.
Desktop captures/measurements use baseline `320becd` (`evidence/source.sha256`).
Network/fault/terminal checks were rerun after the graceful Ctrl-C correction;
`evidence/final-source.sha256` fingerprints that final application source. Terminal
evidence: `.artifacts/q01/terminal-1792984959051614551/`.

## Practical baseline, not performance acceptance

Ryzen 9 5950X, 32 GiB RAM, RTX 3080 Ti, Manjaro/X11, NVIDIA 610.57.04.
Debug development runs, ENet loopback, 20Hz simulation, 10Hz input/snapshots.
One measured headless startup run: server ready about **0.45s**, clients about
**0.70s / 0.65s**, including waiting for diagnostic/first-snapshot readiness.
Process RSS: server **167.2 MiB**, headless clients **167.8 / 166.0 MiB**.
The two actual 960x540 windows reported **60 FPS** at the deliberate 60 FPS cap;
their sampled process RSS was **336.4 / 295.8 MiB**. These are single observations,
not peak memory, frame distributions, minimum specs, throughput or release benchmarks.
No bandwidth or WAN timing measurement. No 8/16-player capacity claim.

## Resolved failures

The bounded read-only review found failed-startup save rewrites and successful
exit codes after failed final saves. Exclusive world ownership and guarded final
writes fixed both; fault injection verified the results. A first duplicate-identity
test exposed rejection text being overwritten by the disconnect notification;
the rejection is now retained. A later telemetry assertion sampled server status
before its next refresh; the test now waits for the documented asynchronous update.
The launch smoke also exposed abrupt default Ctrl-C termination; a cancel handler
now requests shutdown on the main thread, and live-state flush/exit were verified.
Earlier raw failure logs/fixtures remain under `.artifacts/`; current runs pass.

## Limits and next bounded task

World save/content/protocol version 1 is new; no previous game format was migrated.
Required/incompatible data fails closed. Writes atomically replace the save, but
power-loss durability/directory fsync and migrations are not proved. Only one writer
owns a save. Last autosave can lag up to ~2s; graceful join/leave/shutdown saves too.
Profiles are local co-op identities, not authentication. Abrupt absence is applied
once ENet detects lost connectivity; stale held movement expires in ~250ms.

No exported binaries or other target machines tested. 2–8 load, WAN loss/latency,
automatic client reconnect, prediction, camera collision, mouse-look, production
animation and stacked-floor support remain untested/unimplemented. NPCs, combat,
item transfers, shared construction, factions and quests remain deferred. Inventory
and progression are retained fields with offline test fixtures, not gameplay yet.
The yellow module is a fixed collision block; clicking it has no shared action.

Next action: Q02, one authoritative inventory interaction: two clients race for
a single item, a retry cannot duplicate it, and reconnect/restart preserve the
committed result. Do not expand into full inventory/construction systems at once.
