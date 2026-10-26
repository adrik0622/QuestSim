# QuestSim

[Resume handoff — 2026-10-25](HANDOFF_2026-10-25.md)

Living multiplayer-world experiment, beginning with cooperative zombie survival.
Current increment: two low-poly 3D clients, one server-authoritative item pickup and one modular wall placement.

```bash
bash scripts/install_godot.sh
bash scripts/build_game.sh
```

Run each of these in a separate terminal:

```bash
bash scripts/run_server.sh
bash scripts/run_client.sh alice
bash scripts/run_client.sh bob
```

WASD moves; Q/E turns/orbits; Up/Down tilts; C switches first/third person;
R resets the camera. **F** takes from the nearest stocked crate (green bandage, blue
panels); **B** places a panel wall on the orange cell; **T** retries the saved attempt.
Only one player can take the bandage or build the single wall. Close a client to disconnect; relaunch the same profile to
return. Characters retain identity and data while absent. World time pauses when
the last player leaves. Data stays in `.artifacts/play/`; preserve profiles and save.

[Wall results](questsim_planning_kit/Q02B_RESULTS.md) ·
[Item results and screenshots](questsim_planning_kit/Q02_RESULTS.md) ·
[Two-client baseline](questsim_planning_kit/Q01_RESULTS.md) ·
[Runbook](questsim_planning_kit/RUNBOOK.md) · [State](questsim_planning_kit/STATE.md)

Fixed crates and one build cell; no item use/drop/trade, general building, NPC simulation or quests.
Save/protocol v3 requires rebuilding server/clients; v1/v2 saves get a backup before migration. Local Linux
Godot development runs are verified; standalone exports and other machines are not.

The original comparison is preserved:
`bash scripts/run_presentation.sh 2d` (also `sprite3d third`, `poly3d first`).
[Presentation evidence](questsim_planning_kit/PRESENTATION_EXPERIMENT.md) ·
[Director decisions](questsim_planning_kit/DIRECTOR.md)

GitHub origin: `git@github.com:adrik0622/QuestSim.git`; verified checkpoints are pushed to `main`.
