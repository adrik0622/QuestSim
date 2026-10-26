# Current task: E1 / population and rumor experiment

Status: written 2026-10-25 for director approval; not started. Q02b is complete
(Q02B_RESULTS.md). Experimental and disposable: measurements decide what is kept.
Push verified checkpoints to origin/main occasionally; never force-push.

## Question

How many persistent agents (humans and zombies, with factions) can one server tick
with movement, spatial contact and rumor spread, on CPU and on GPU, measured end to
end, and does a GPU path stay off the authoritative tick despite readback latency?

## Scope

A headless console experiment in `experiments/population/` (plain C#, no Godot game,
no networking). Separate from `src/`; nothing in the shipped game depends on it.

0. **Vulkan backend spike (≤1 day).** Vulkan is the chosen API (Windows/Linux; macOS a
   stretch goal). Kernels are GLSL compute shaders using 32-bit integers only (two u32
   for the 64-bit rumor mask). Try Godot RenderingDevice on a server without a window
   first; if it cannot create a Vulkan device headless, use Vulkan directly via
   Silk.NET with the same GLSL. Record the evidence and the choice.
1. **World.** Seeded rudimentary generation: settlements on a 2D plane joined by a
   road graph. Pinned PRNG algorithm (implemented in code) plus a generator version.
2. **Agents as arrays.** One slot per agent, never reused: fixed-point uint32 X/Y
   (millimetres), kind (human/zombie), faction, home settlement, movement state, and a
   64-bit "heard" mask for active rumors. Humans: 3 factions; zombies: 2 horde factions.
   Humans travel roads or wander near home; hordes drift toward attractors.
3. **Tick.** Integer-only movement, spatial binning into a uniform grid, contact within
   a radius. Each step reads the previous state and writes the next, so results never
   depend on thread order.
4. **Facts and rumors.** The CPU owns a fact log (id, tick, place, kind, provenance) and a
   rumor table (≤64 active slots → fact id, credibility). Spread: human contacts pass a
   rumor if a deterministic hash test passes (credibility × faction trust). Agents
   forget at a fade rate; a rumor with no carriers retires and its slot is versioned
   before reuse. A scripted event seeds a rumor ("a settlement's leader died").
5. **Readback.** The GPU appends "agent heard rumor R" events to a compact list; the
   CPU consumes it asynchronously one or more ticks later. The authoritative tick never
   waits for it.
6. **Faction aggregates.** Counts per faction per region by reduction, on both paths.
7. **Persistence.** Binary snapshot (header: seed, generator version, counts, tick).
8. **Inspection.** A small density/rumor-coverage image per run (PPM → PNG via ImageMagick).

## Acceptance

- CPU reference and GPU produce bit-identical state hashes every tick for the same seed
  (sorted event lists compared).
- Same seed → same hashes across runs. Save after N ticks, load, run M == run N+M straight.
- Agent count and IDs are conserved; no slot reuse; faction totals match per-agent data.
- Rumor spread is measurable (coverage over time), stops when it fades, and never
  changes the fact log. Retired rumor slots do not leak old beliefs into a new rumor.
- Delaying readback by k ticks leaves authoritative tick time and results unchanged.

## Measure

Agent counts 10⁵, 10⁶, 10⁷ (10⁸ only if memory allows, with jobs memory-capped):
ms/tick split into movement, binning, spread, aggregates, upload, dispatch, sync and
readback; memory (RAM/VRAM); readback latency; snapshot size and save/load time. Record
commit, seed, OS, CPU/GPU/driver, runtime and build type (ENGINE_RULES measurement rules).

## Excluded

Godot game integration, networking, detail tiers and promotion (E2), quests and
story-time creation, rumor distortion/variants, zombie politics beyond a faction tag,
pathfinding, combat, deaths/births. Inventories remain shared and synchronized for all
players (DIRECTOR.md) whenever later work touches them.

## Stop

When the measurements and checks above are recorded in a short E1 results file with a
recommendation (CPU only, GPU, or hybrid; scale reached; latency handling) and the next
experiment (E2: detail tiers). Report negative results honestly.
