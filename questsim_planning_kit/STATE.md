# QuestSim implementation state

2026-10-25: Q01 authorized. Low-poly 3D with first/third-person is the working
selection after director review; broader world structure remains open. Q00 evidence
and standalone lab are preserved. Baseline commit: 99e1c81; clean on entry.

Current outcome: implement CPU headless authority and two independent clients,
shared movement/presence, persistent reconnect with absent disconnected characters,
safe re-entry and an empty-world-paused simulation clock. CURRENT_TASK.md bounds it.

Verified prerequisites unchanged: Godot 4.6.2 .NET, SDK 10.0.110/runtime 10.0.10,
Linux/X11 desktop input/screenshots. Q01 domain checks and real ENet integration now pass. Existing
seven fixture checks and presentation/desktop evidence are recorded in RUNBOOK.md.

Design: native ENet/RPC; plain C# state; fixed 20Hz authority; save/protocol v1,
atomic JSON replace; local profiles independent of transient peer IDs. No existing
saves require migration. Inventory/progression are persisted data, no gameplay UI.

Pending creative questions: long-term world structure, reference hardware and weekly
capacity. No need to block this small room experiment on those choices.
Verified Q01: 24 domain checks including the original seven fixture checks;
12 real ENet checks for two processes, late join/convergence, rejected duplicate
identity/input, abrupt leave, paused clock, restart, preserved identity/inventory/
progression, changed-world safe re-entry and incompatible-save rejection. Four
fault tests verify exclusive save ownership, failed-startup preservation and
nonzero exit on final-save failure. Build has zero warnings/errors.
Raw evidence: .artifacts/q01/network-1792983860405593992/ and
.artifacts/q01/save-failures-1792983875899224968/. One bounded read-only review
found the startup/final-save risks, now corrected and tested.
Next action: verify two rendered clients with owned-window screenshots and keys.
