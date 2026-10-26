# Q02b results — one modular wall placement

Date 2026-10-25. Linux, .NET SDK 10.0.110, Godot 4.6.2 mono. Contract: CURRENT_TASK.md.

## Behavior

- Panel crate `questsim:panel-crate` (2 × `questsim:wall-panel`) at (-3, 1.5),
  mirroring the bandage crate. Build cell `questsim:build-cell-1` at (-4, 1.5).
- **F** takes from the nearest crate that is stocked and within 2m (else nearest).
  **B** places the panel wall. **T** retries the last saved pickup or build.
- Outcomes: `placed`, `occupied`, `no_material`, `out_of_reach`, `blocked`
  (active player in the cell), `unknown_site`; malformed/conflicting → `invalid_request`.
- Block + spent panel + receipt in one atomic save before the reply.

## Compatibility

Save/protocol 3, content `questsim:q02b-room-v1`. `Supply` → `Supplies`, new
`BuildReceipts`. v2 saves: validated as v3, original copied to `.v2-<guid>.bak`,
panel crate added once, never restocked. v1 saves migrate through v2 in one load.
Validation: per crate taken = initial − stock; per actor inventory ≥ awarded − spent;
total held panels = taken − spent; wall present iff one successful build receipt.
A v2 save already holding `questsim:wall-panel` would fail closed (not expected).
Rebuild server and all clients; profiles gain build fields lazily.

## Checks

| Command | Result |
| --- | --- |
| `bash scripts/check.sh` | 54 pass (16 new BuildChecks) |
| `bash scripts/build_game.sh` | 0 warnings, 0 errors |
| `python3 scripts/verify_build.py` | 11 pass |
| `python3 scripts/verify_build_screen.py` | 4 pass (self-runs under `xvfb-run`) |
| `verify_items` / `verify_multiplayer` / `verify_save_failures` | 12 / 12 / 4 pass |

The screen test feeds key events through `Input.ParseInputEvent` (intent-file
`keys`) and saves the viewport with intent-file `capture`; it never touches the
user's desktop. Raw evidence: `.artifacts/q01/build-*`, logs in
`.artifacts/logs/session-2026-10-25b/`.

[Start](../src/QuestSim.Game/evidence/q02b/build-start.png) ·
[Winner](../src/QuestSim.Game/evidence/q02b/build-winner.png) ·
[Loser](../src/QuestSim.Game/evidence/q02b/build-loser.png) ·
[First person](../src/QuestSim.Game/evidence/q02b/build-first-person.png)
(the first-person shot faces the doorway; the wall is behind the player).

## Limits

One fixed cell; no demolition, doors, refill or navigation. Wall faces along X
(thin side toward the default camera). Xvfb uses Mesa llvmpipe software GL, so
screen tests prove input/render paths, not GPU performance. Real-desktop suites
(`verify_item_desktop.py`, `verify_shared_desktop.py`) were not rerun.
