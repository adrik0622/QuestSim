# Presentation experiment — initial checkpoint, 2026-10-25

Status: setup blocked; no visual comparison completed. Initial budget remains
capped at 8 engineering hours. Do not expand it or select a pipeline from this report.
The first prerequisite gate failed before any of the three scenes was built.

## Observed evidence

- No existing game project or Godot installation found in inspected executable,
  local application, Steam and package-cache locations. No engine launch occurred.
- .NET 10.0.110 / runtime 10.0.10 builds and runs the BCL-only fixture in Release.
  Seven checks pass, including 20 simulated seconds of four actors crossing a
  two-metre doorway and deterministic replay. This is not a frame-time measurement.
- Godot build fails MSB4236: Godot.NET.Sdk/4.6.2 missing. Shell downloads fail DNS.
- X11 screenshot/input unavailable: both X11 socket forms fail with EPERM.
  No controlled QuestSim window interaction occurred. No frame-time, memory,
  GPU, occlusion, control, animation or equipment-authoring result was collected.

| Candidate | Scene/control test | Measured performance | Decision evidence |
| --- | --- | --- | --- |
| 2D overhead/isometric sprites | Not built/run | None | Insufficient |
| 3D environment + sprite characters; first/third person | Not built/run | None | Insufficient |
| 3D environment + low-poly characters; first/third person | Not built/run | None | Insufficient |

## Bounded comparison once prerequisites work

Budget allocation: setup/launch 1h; 2D 1h; 3D sprites 1.5h; 3D models 1.5h;
control/doorway/placement checks 1.5h; measurements/report 1.5h. Count work already
spent on setup against this ceiling. If incomplete, report untested items first.

Use the shared fixture in src/QuestSim.Domain/PresentationScenario.cs: four stable
actors cross x=0 through y=-1..1, travelling within an 8m span. Create the same
12m room, wall/doorway and single one-metre selectable modular wall piece in each
scene. Built-in primitives and a tiny local placeholder sprite are sufficient.
No collision/AI/network behavior is implied by the moving fixture. Keep scene
code disposable; no rendering interface or production assets.

For each: cross the doorway with overlapping actors; approach from each side;
select/place/reject an overlapping piece; inspect rear-wall occlusion. Test 2D
overhead or isometric control; first/third person in both 3D scenes. Include a
raised platform/stair proxy to investigate vertical readability without deciding
world structure. Record time spent adding one walk motion and held-item stub.

At 960x540 on the same machine, report warm-up and sample duration (suggested
10s + 30s), frame interval median/p95, process RSS, engine/static memory where
available, renderer/driver/hardware and debug/release status. Frame intervals are
not GPU timings. Do not generalize four actors to population or multiplayer scale.

Questions to resolve by observation (not findings): can overhead controls serve
the desired experience; can billboard characters remain readable at eye level
and changing elevations; does simple polygon animation/equipment justify its
asset cost; which camera/occlusion rules make doorways and construction usable?

Director decision after evidence: choose a first presentation, specifying world
structure, representation and supported camera(s) separately; hybrids remain
possible. No preference is recommended from unrun scenes. Before that decision,
next task is restoring engine and desktop test prerequisites, not choosing visuals.
