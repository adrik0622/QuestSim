# QuestSim

Living multiplayer-world experiment, beginning with cooperative zombie survival.
Current increment: two low-poly 3D clients and one server-authoritative item pickup.

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
R resets the camera. **F** takes a bandage from the green crate; **T** retries
the saved attempt. Only one player can take the single bandage. Close a client to disconnect; relaunch the same profile to
return. Characters retain identity and data while absent. World time pauses when
the last player leaves. Data stays in `.artifacts/play/`; preserve profiles and save.

[Item results and screenshots](questsim_planning_kit/Q02_RESULTS.md) ·
[Two-client baseline](questsim_planning_kit/Q01_RESULTS.md) ·
[Runbook](questsim_planning_kit/RUNBOOK.md) · [State](questsim_planning_kit/STATE.md)

One fixed crate/item; no item use/drop/trade, shared building, NPC simulation or quests.
Save/protocol v2 requires rebuilding server/clients; Q01 saves get a backup before migration. Local Linux
Godot development runs are verified; standalone exports and other machines are not.

The original comparison is preserved:
`bash scripts/run_presentation.sh 2d` (also `sprite3d third`, `poly3d first`).
[Presentation evidence](questsim_planning_kit/PRESENTATION_EXPERIMENT.md) ·
[Director decisions](questsim_planning_kit/DIRECTOR.md)

GitHub origin: `git@github.com:adrik0622/QuestSim.git`; verified checkpoints are pushed to `main`.
