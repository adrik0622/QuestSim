# E1 results — population and rumor experiment

Date 2026-10-26. Contract: CURRENT_TASK.md. Code: `experiments/population/` (headless,
disposable, not used by the game). Measured at commit 5649c13 (Release build).

Machine: Manjaro Linux, Ryzen 9 5950X (32 threads), 32 GiB RAM, RTX 3080 Ti 12 GiB,
NVIDIA 610.57.04, Vulkan 1.4.341, .NET 10.0.10, Silk.NET.Vulkan 2.23.0. Desktop in
ordinary use; not a controlled benchmark machine. Game time 48:1, so one population
tick = 48 game seconds.

## Step 0 — Vulkan backend

Godot 4.6.2 `RenderingServer.create_local_rendering_device()` returns null under
`--headless`, including with `--rendering-driver vulkan` and `forward_plus` (it reports
"vulkan" but uses a dummy renderer). Chosen: Vulkan directly through Silk.NET, with GLSL
compute kernels compiled by `glslc` and 32-bit integers only.

## Model

Agents are plain arrays, with slot index = ID, never reused. Each agent has fixed-point
uint32 positions (mm), a target, and kind/mode/faction/anchor bits. Humans (3
territorial factions) wander near home or travel roads; zombie hordes (2 factions)
drift between attractor settlements. Per tick: move → hashed-grid insert (per-cell
linked lists) → contact within 30 m → rumor spread. Each agent carries a 64-bit
rumor mask. Spread chance = credibility × faction trust, tested by a deterministic hash
per (agent, neighbour, rumor), so results don't depend on thread order. Agents forget
at a fixed rate and credibility decays with age. The CPU owns the fact log and the rumor
slots, and consumes GPU readbacks `ReadbackDelay` ticks late.

## Checks (`bash scripts/run_population.sh check`) — 12 pass

Deterministic generation per seed; agent count, kind and faction conserved; region
aggregates match per-agent totals; same seed → same state, facts and tallies; every event
becomes a fact or a counted drop; spread never edits facts; reused rumor slots start
empty; a single rumor spreads (peak 1,367), fades to zero and retires; snapshot at tick
120 + 80 ticks = 200 straight ticks. **CPU reference and Vulkan GPU bit-identical every
tick** (state hash, counters, aggregates, sorted events, facts) at readback delays 0, 2, 4.
Hearing totals also matched CPU vs GPU in every benchmark up to 10⁷.

## Measurements (ms per population tick; 200 km world unless noted)

| Agents | CPU step (32 threads) | GPU total | of which GPU spread | GPU speed-up |
| --- | --- | --- | --- | --- |
| 10⁵ | 10.2 | 0.84 | 0.48 | ~12× |
| 10⁶ | 38.8 | 8.7 | 8.1 | ~4.5× |
| 10⁷ | 1,623 | 117.7 | 113.0 | ~14× |
| 10⁸ (200 km, 3.8× denser) | not run | 30,001 | 29,954 | — |
| 10⁸ (632 km, same density as 10⁷) | not run | 15,265 | 15,219 | — |

- **Authority never waits:** paced at 1 tick/s with 10⁷ agents on the GPU, the
  authority's step was p50 0.16 ms, max 2.27 ms, readback wait 0.00 ms, while the GPU
  spent ~106 ms per tick. Submit→results round trip ≈ GPU time + <0.3 ms.
- **Headroom:** 10⁷ agents use ~12% of the GPU at 1 tick/s (~8 ticks/s max).
- **Scaling limit:** movement and insert scale linearly (10⁷→10⁸: 1.3→11 ms,
  2.9→32 ms). Spread doesn't: 135× for 10× agents at constant density. The density
  diagnostic (`run_population.sh density`) shows neighbour scans per human of 28
  (10⁷) vs 107 (10⁸, 200 km). The remaining gap is memory locality: neighbours are
  scattered across 4.7 GB because agents stay in ID order.
- **Memory:** 10⁸ agents = 4.73 GiB VRAM, 2.44 GiB peak host RSS; generation 5.7 s,
  upload 1.1 s. Snapshots ~28 bytes/agent (40k agents = 1.14 MB); save/load time was
  not measured at scale.
- Rumor activity hardly changes cost; contact scanning dominates.

Raw JSON: `experiments/population/evidence/bench-*.json`; image:
`experiments/population/evidence/world-1e6.png` (green humans, red hordes, cyan
rumor carriers).

## Recommendation

- **Hybrid, Vulkan.** The CPU owns facts, rumor slots and ledgers; the GPU runs bulk
  population steps. Integer kernels match the CPU reference exactly, so the CPU stays
  the test oracle and fallback.
- **Usable today: ~10⁷ persistent agents at 1 tick/s** on this GPU, with the authority
  unaffected by readback latency (delay ≥ 1).
- **Beyond 10⁷ needs locality:** sort agents by grid cell each tick (or keep a
  cell-ordered copy of the hot fields) so neighbour reads are contiguous. This is the
  next bounded experiment (E1b); expected large but unmeasured.
- Then E2: detail tiers (promote/demote around players without loss or duplication).

## Limits

Synthetic behaviour (square settlements, straight-line roads, no pathfinding, deaths,
combat or zombie politics); 64 concurrent rumors; no rumor distortion. The CPU
reference isn't optimised (insert is single-threaded). Only Linux/NVIDIA was measured;
Windows is untested.
