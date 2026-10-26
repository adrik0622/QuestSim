# QuestSim implementation state

2026-10-25: Q01 complete; Q02a atomic item pickup now authorized.
Baseline: 237c943 pushed and verified at origin/main; clean on entry.
Working presentation: low-poly 3D, first/third-person. One flat room for this test;
broader world structure remains open. Q00 lab/evidence remain intact.

Implemented: CPU headless Godot ENet authority; 20Hz simulation, 10Hz intent/
snapshots; two independent clients; server-owned position/presence; stable saved
identity/inventory/experience; absent disconnected actors; validated safe re-entry;
clock frozen while empty and across restart, resumed on join. Native RPC and plain
C# state; save/content/protocol v1 with required fields and atomic file replacement.
Server exclusively owns its save; incompatible/partial data never silently resets.

Verified: Godot 4.6.2 .NET, SDK 10.0.110/runtime 10.0.10, Manjaro/X11; zero build
warnings/errors. 24 domain checks, 12 ENet checks, four persistence faults, six
real two-window GUI checks and three terminal/Ctrl-C checks passed. Screenshots inspected; no current runtime
errors/warnings in successful runs. Expected injected failure cases exit nonzero.
Headless server has no GPU device handles. Detailed evidence: Q01_RESULTS.md.

Actual debug observations: loopback server ready ~0.45s, clients ~0.70s each;
headless server RSS ~167 MiB, rendered clients ~336/296 MiB and 60 FPS at 60 cap.
These are single observations on Ryzen 5950X/RTX 3080 Ti, not scale/release proof.

Resolved failures: startup/final-save error paths found in one bounded read-only
review are guarded and fault-tested. Duplicate rejection text no longer gets
replaced by a disconnect message. Ctrl-C now requests a graceful main-thread final save; verified with live clients.
Test telemetry now waits for asynchronous server
status refresh; it previously sampled stale status. Raw failed runs retained.

Checkpoints: 63b8ad5 domain/persistence; 320becd real ENet server/clients;
current HEAD records final GUI/docs/evidence (verify with `git log --oneline`). Origin is git@github.com:adrik0622/QuestSim.git; main tracks origin/main.
Periodic verified-checkpoint pushes authorized.
Raw logs and disposable test saves/profiles: .artifacts/q01/ and .artifacts/logs/.
Source/concise evidence versioned; no actual play save modified. Test children closed.

Known limits: Linux development runs only; no exports, other hardware, 2–8 load,
WAN tests, auto-reconnect, prediction, camera collision or mouse-look. No power-loss
or directory-fsync durability guarantee; autosave may lag ~2s on forced termination.
Profiles are co-op identity, not authentication. Abrupt removal waits for ENet loss
detection. No NPCs/combat/quests/item transfers/shared building; yellow module is
static. Future rules remain in DIRECTOR.md. Long-term structure, target hardware
and weekly capacity are still material open questions, not blockers to this room.

One next action: implement and verify one-item conservation, durable request
receipts and backed-up Q01 save migration. CURRENT_TASK.md bounds Q02a.
