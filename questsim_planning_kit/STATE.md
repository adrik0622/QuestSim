# QuestSim implementation state

2026-10-25: Q01 authorized. Low-poly 3D with first/third-person is the working
selection after director review; broader world structure remains open. Q00 evidence
and standalone lab are preserved. Baseline commit: 99e1c81; clean on entry.

Current outcome: implement CPU headless authority and two independent clients,
shared movement/presence, persistent reconnect with absent disconnected characters,
safe re-entry and an empty-world-paused simulation clock. CURRENT_TASK.md bounds it.

Verified prerequisites unchanged: Godot 4.6.2 .NET, SDK 10.0.110/runtime 10.0.10,
Linux/X11 desktop input/screenshots. No new Q01 behavior verified yet. Existing
seven fixture checks and presentation/desktop evidence are recorded in RUNBOOK.md.

Design: native ENet/RPC; plain C# state; fixed 20Hz authority; save/protocol v1,
atomic JSON replace; local profiles independent of transient peer IDs. No existing
saves require migration. Inventory/progression are persisted data, no gameplay UI.

Pending creative questions: long-term world structure, reference hardware and weekly
capacity. No need to block this small room experiment on those choices.
Next action: implement and verify domain authority, save/reload and safe re-entry.
