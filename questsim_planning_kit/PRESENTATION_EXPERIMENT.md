# Presentation comparison — 2026-10-25

The three disposable modes run on the actual Linux desktop. The initial comparison
is complete within the one-working-day ceiling (under one hour of session work,
including bootstrap); no extension or production pipeline work began.

**Recommendation for director review:** use low-poly 3D for the next small playable
increment if first/third-person remains the preference. It provided volume, facing
and equipment attachment in both cameras without billboard-facing artifacts.
Choose overhead sprites if overview and lower initial asset scope outweigh those
camera options. 3D sprites remain a viable aesthetic choice, but this placeholder
requires a further directional/vertical-view art probe before committing to it.
This is a prototype recommendation, not a decision made on the director's behalf.

## Common scenario and verified interaction

12m room; four moving, stable-ID actors plus a blue controlled player; 2m doorway;
three raised steps; one selectable 1m module. Each mode uses the same plain C#
actor fixture. The 2D candidate is overhead; isometric was not separately tested.
The 3D room is shared, switching only the disposable character representation.
No universal renderer, commissioned art or external placeholder dependencies.

Real keyboard/mouse acceptance passed for all three modes: move through doorway;
stop against wall; climb to the 0.5m step; select construction; reject a wall cell;
place at a valid ground cell. Both 3D modes also passed first-person turning/tilt,
selection, placement and doorway traversal. Third-person orbit/tilt was exercised
separately. Screenshots were captured and inspected, and logs confirmed resulting
positions/selection/placement. No synthetic game input bypass was used.

## Evidence and compromises

| Criterion | 2D overhead sprites | 3D + billboard sprites | 3D + low-poly models |
| --- | --- | --- | --- |
| Readability/control | Whole floor plan and doorway visible; tiny silhouettes rely on color. WASD gives direct plan movement. | Upright silhouettes read at both camera heights; faces always turn toward viewer, losing actual travel-facing cues. | Volumes, nose/facing and attached item show orientation in both views. |
| Occlusion/overlap | No wall-height occlusion; nearby icons can still cover each other. | Perspective actors stack at doorway; wall can obscure them. Billboard facing persists when viewed from behind. | Perspective crowd overlap and wall occlusion remain; volume/shadows give additional depth cues. |
| Camera/verticality | Fixed overhead; step marker and numeric height work, but screenshot gives limited elevation information. | First/third, yaw and tilt work; raised player is visible. Character remains a flat upright plane. | Same camera freedom; model/held item retain volume as view changes. |
| Animation/equipment | Two hand-coded top-down poses, with item painted into both. No imported art pipeline. | Two 32x48 poses, item painted into both; fixed-Y billboard. Directional poses/items would need additional authoring (inference). | Box limbs swing at pivots; one item mesh follows arm rotation. No skinned rig, import/retarget workflow or production animation tested. |
| Construction | Clear footprint picking and invalid-cell preview. | Native 3D ray selection plus ground-grid preview; first-person placement needs looking down. | Same room/selection code and behavior as sprite 3D. |
| Effort/experience fit | Smallest visual setup in this implementation; strongest overview, no first-person candidate. | Reuses 3D environment/cameras; cheap placeholder art, with visible view-dependent compromises. | More primitive character setup, but clearer orientation and natural fit for requested camera preference. Production art cost remains unmeasured. |

Third-person screenshots explicitly show walls between camera and player after
crossing the doorway. There is no camera collision, wall fading or ceiling/roof
handling. Treat this as an unresolved control/occlusion requirement for a selected
3D slice, not evidence of finished gameplay quality. First-person uses keyboard
turning/tilt with free pointer for selection; mouse-look feel and gamepad controls
were not tested. No human playtester preference study was conducted.

The authoring comparison is qualitative: both sprite variants and the procedural
model were built in one small implementation pass, without per-variant time tracking.
It does not establish production asset hours or prove one style cheaper overall.

## Frame intervals and memory

Same machine: Ryzen 9 5950X, 32 GiB RAM, RTX 3080 Ti, NVIDIA 610.57.04, Manjaro/X11.
Godot 4.6.2 .NET, .NET runtime 10.0.10, GL Compatibility, 960x540, VSync disabled,
uncapped. Each fresh process warmed up 10s then sampled 30s. Four NPCs moved while
the player/camera stayed still; first-person hides the player's own mesh. Desktop
apps remained running. No concurrent build/control suite during sampling.

| Mode / camera | Median interval ms | p95 interval ms | Process RSS MiB |
| --- | ---: | ---: | ---: |
| 2D overhead | 0.259 | 0.338 | 278.8 |
| 3D sprites, third | 0.422 | 0.572 | 284.2 |
| 3D sprites, first | 0.408 | 0.545 | 285.8 |
| 3D models, third | 0.542 | 0.733 | 283.2 |
| 3D models, first | 0.472 | 0.632 | 287.0 |

These are **single-run debug/editor-binary baseline samples**, not release/export
performance acceptance or isolated CPU/GPU timings. Frames are `_Process` intervals;
RSS includes runtime/renderer allocations and is sampled at the end, not peak.
Raw JSON also contains engine static/video-memory counters (not total driver memory).
An initial 2D sample logged an unattached 3D camera leak at shutdown. That
initialization was corrected; the reported samples were rerun against the corrected
source, with runtime logs checked for errors/warnings. Original logs are retained.
Performance does not distinguish the choices meaningfully at this tiny workload.
No population, networking, minimum-spec, release-build or frame-budget conclusion.
Release measurements are deferred until an actual playable/export milestone.

## Review artifacts and reproduce

All screenshots below are unmodified window captures, taken at different simulation
times. Raw logs/screenshots remain in `.artifacts/desktop/`; concise captures and
raw metric JSON are retained under `experiments/presentation/evidence/` in Git.

- [Overhead sprites](../experiments/presentation/evidence/initial-2d.png)
- [3D sprites — third](../experiments/presentation/evidence/initial-sprite3d.png), [first](../experiments/presentation/evidence/verify-sprite3d-first.png)
- [3D models — third](../experiments/presentation/evidence/initial-poly3d.png), [first](../experiments/presentation/evidence/verify-poly3d-first.png)
- [Third-person doorway occlusion](../experiments/presentation/evidence/verify-poly3d-doorway.png)

RUNBOOK.md contains launch, desktop acceptance and benchmark commands. Sources are
fingerprinted in `experiments/presentation/evidence/source.sha256` at measurement
commit `a1aacb1`. The subsequent mode-specific HUD clarification does not rebaseline
these measurements.

## Decision and remaining limits

Director decision: choose representation and supported camera(s) for Q01; choose
world structure independently. The experiment proves only a floor plus raised steps,
not stacked floors, continuous vertical gameplay or a persistent 3D world. Proposed
next scope: small flat shared space, modular piece, CPU authority and two clients;
refine controls around the approved presentation before implementation.

Unverified: release exports/other hardware, mouse-look, gamepad, camera collision,
roof/floor occlusion, imported rigs/equipment pipeline, NPC collision/avoidance,
stacked floors, production art cost, networking, saves/reconnect, empty-world clock.
These are explicit limits; no extra art/polishing/renderer work is authorized by
this comparison alone. The existing future gameplay decisions remain in DIRECTOR.md.
