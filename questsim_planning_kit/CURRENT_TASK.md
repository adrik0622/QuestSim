# Current task: Q00 / environment and presentation probe

Status 2026-10-25: comparison complete; director selection pending.
One implementer; one bounded desktop-discovery helper used read-only.
See PRESENTATION_EXPERIMENT.md for evidence and STATE.md for one next action.

Order: inspect actual files/tools; launch the smallest Godot .NET project;
compare presentation; report evidence for director selection; continue only
independent environment/desktop/minimal simulation work while selection is pending.

Presentation budget: at most one working day (8 engineering hours), including
setup and verification. No extension without reporting what remains untested.
Use one room/street, several moving actors, a doorway and one selectable modular
piece. Compare 2D sprites, 3D with sprites, 3D with low-poly models; test first-
and third-person where applicable. No production renderer or common render framework.
Evaluate controls/readability, occlusion/doorways, camera/verticality,
animation/equipment cost, placement, practical frame/memory samples and effort.
Record unmeasured criteria honestly; no selection on unrun scenes.

If prerequisites block visual tests, preserve a minimal scaffold, document exact
failures and verify independent offline work. Do not substitute another engine.
Desktop tests require a verified QuestSim target window and inspected screenshots.
Sibling helper discovery is read-only; all new files stay in QuestSim.

After selection, refine Q01: CPU-only authority, two independent clients, shared
presence/movement, persistent reconnect identity with absent disconnected actors,
safe re-entry and world clock paused while empty. Do not implement camera-dependent
controls yet. No factions, quests, scale, GPU, digging or general mod framework.

Done: observed build/launch/comparison evidence or exact prerequisite blockers,
accurate RUNBOOK/STATE, checkpoint and one next action. Stop at this boundary.
