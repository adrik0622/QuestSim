# QuestSim

Living multiplayer-world experiment, beginning with cooperative zombie survival.
Current increment: a CPU-authoritative server and two independent low-poly 3D clients.

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
R resets the camera. Close a client to disconnect; relaunch the same profile to
return. Characters retain identity and data while absent. World time pauses when
the last player leaves. Data stays in `.artifacts/play/`; preserve profiles and save.

[Verified results and limits](questsim_planning_kit/Q01_RESULTS.md) ·
[Runbook](questsim_planning_kit/RUNBOOK.md) · [State](questsim_planning_kit/STATE.md)

No shared inventory/building actions, NPC simulation or quests yet. Local Linux
Godot development runs are verified; standalone exports and other machines are not.

The original comparison is preserved:
`bash scripts/run_presentation.sh 2d` (also `sprite3d third`, `poly3d first`).
[Presentation evidence](questsim_planning_kit/PRESENTATION_EXPERIMENT.md) ·
[Director decisions](questsim_planning_kit/DIRECTOR.md)
