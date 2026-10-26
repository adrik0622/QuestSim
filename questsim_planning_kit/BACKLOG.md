# QuestSim proof-of-concept backlog

All timeboxes are approximate experienced-engineer effort, with placeholder assets. They are probes, not additive calendar commitments. Integration and rework account for the larger milestone ranges in the brief. Each task ends with a runnable result, raw evidence and a keep/change/stop decision.

## Q00 - Bound the experiment

- Owner: director and integration owner. Dependency: none. Timebox: 1-2 days.
- Outcome: one-page scope contract with camera, excavation, players, target hardware, weekly capacity, player absence policy and acceptable approximation.
- Confirmed: modular building, two clients first/2–8 validation/16 target, server authority (GPU allowed for bulk simulation since 2026-10-25), no digging. Low-poly 3D with first/third-person selected for Q01; broader structure remains open. DIRECTOR.md supersedes old defaults.
- Deliver: contract, initial risk list and measurement scenarios.
- Acceptance: the director can describe the first shared scene and its explicit exclusions.
- Stop: unresolved camera or excavation scope means no large art/terrain investment yet. Research and basic authority work can continue.

## Q01 - Export and connect

Local two-client/persistence increment verified 2026-10-25; see Q01_RESULTS.md.
Standalone exports, other platforms and 2–8 load coverage remain explicit future checks.

- Owner: integration/platform agent. Dependency: Q00. Timebox: 3-5 days.
- Outcome after presentation selection: a tiny shared space runs as two independent clients and a Godot CPU-only headless server. Synchronize presence/movement; persist identity/inventory/progression on reconnect, remove disconnected actors, validate safe re-entry, and pause the simulation clock while empty.
- Implementation: Godot .NET project, plain C# world records, native existing transport, stable actor IDs. No custom protocol transport.
- Acceptance: exported Windows/Linux/macOS clients launch on real machines where available; two connect to a server. Mark missing hardware coverage honestly.
- Measure: startup, basic tick/frame time and memory with build versions.
- Exclude: authentication service, server browser, relay system, GPU server.
- Stop/reduce: resolve a blocking platform dependency before expanding features.

## Q02 - One command, one result

Q02a single bandage pickup verified 2026-10-25: race, durable retry, migration,
restart/lost reply/save-failure and GUI evidence in Q02_RESULTS.md. One modular
build placement and network-condition experiments remain separate bounded tasks.

- Owner: authority agent. Dependency: Q01. Timebox: 5-10 days.
- Outcome: movement, shared inventory and one building action follow server rules.
- Implementation: intent commands, request IDs, validation and atomic state changes. Stable plain-data snapshots/results; engine instance IDs never become world IDs.
- Acceptance: two clients race for the last item and same build cell; exactly one success and one cost. A retried request does not repeat the effect. Reconnect sees current state.
- Measure: traffic and behavior at local baseline then 100 ms RTT/2% packet loss. These are test conditions, not all-network guarantees.
- Exclude: distributed transactions, custom rollback of the entire world, MMO services.
- Stop/reduce: no population expansion while duplication or divergent authority remains.

## Q03 - Population continuity

- Owner: simulation agent. Dependency: Q02. Timebox: 1-2 weeks for a narrow proof.
- Outcome: humans and zombies retain identity across travel, detail transitions, infection/death and reload.
- Implementation: detailed/coarse modes, next scheduled event, region/route state, real member IDs for coarse groups. Plain record storage and a small scheduler.
- Acceptance: repeated boundary crossings preserve identity, inventory and health; an offscreen casualty remains dead. Restart preserves scheduled events. Actor creation is explicit and recorded with provenance (simulation or story-time).
- Measure: 1k/10k/100k persistent records separately from detailed actors; baseline CPU and memory.
- Compare: small all-detailed versus coarse scenarios across seeds. Invariants match; outcome drift is measured and director-approved rather than asserted equivalent.
- Exclude: per-agent LLMs, every-agent continuous physics, all-pairs relationships.
- Stop/reduce: fix transition accounting before increasing population.

## Q04 - Autonomous factions

- Owner: gameplay/simulation agent. Dependency: Q03. Timebox: 1-2 weeks for one loop.
- Outcome: two factions consume, move and seek food/ammunition/shelter without player triggers.
- Implementation: priorities or utility scores, stockpiles, actual members, a route graph and legal resource transactions. Zombies supply pressure through a few states.
- Acceptance: no-player test override produces explainable deliveries/shortages. One intervention changes resources, faction response and later gameplay. Log actors, deltas and cause IDs.
- Measure: stocks, population and outcomes over multiple seeds; detect negative inventory and unintended extinction spirals.
- Exclude: complete macroeconomics, dynamic social-language simulation, invisible replenishment.
- Stop/reduce: if causes are not understandable, simplify to one resource and one route.

## Q05 - A generated playable place

- Owner: world agent. Dependency: Q01; integrate with Q03/Q04. Timebox: 3-5 days for the first generator.
- Outcome: bounded region and an interior/dungeon with guaranteed accepted-map traversal.
- Implementation: semantic site/route graph, a small modular building kit, connected room graph, corridors, loops and flood-fill validation.
- Acceptance: 1,000 fixed seeds; every accepted map passes required connectivity and clearance. Failed generation repairs/retries within a bound or uses a known-valid fallback. Keep failed seeds.
- Persistence: save generator version, seed, manifest and edits. Use server-generated chunks until cross-platform regeneration hashes agree.
- Exclude: WFC, neural PCG, infinite streaming, generalized constraint framework, locked-key progression until its reachability rules are tested.
- Stop/reduce: prefer fewer valid layouts over a large unreliable generator.

## Q06 - Build, block and reopen

- Owner: world/authority agent. Dependency: Q02, Q05. Timebox: 5-10 days.
- Outcome: walls, doors and containers affect movement and survive multiplayer reload.
- Implementation: stable piece ID, occupied cells, resource cost, chunk revision and local navigation invalidation. Use grid A* first.
- Acceptance: players edit a doorway while zombies pursue. Pathing responds correctly. Late join, unload/reload and restart preserve both geometry and inventory. Essential exits cannot be accidentally invalidated under v0 rules.
- Measure: edit latency, path rebuild/request time and tick spikes.
- Exclude: collapse physics, arbitrary mesh cutting, fluids, unrestricted voxel digging.
- Stop/reduce: reduce edit area/rate or navigation complexity if updates stall the server.

## Q07 - Three honest story opportunities

- Owner: narrative/gameplay agent. Dependency: Q04, Q06. Timebox: 1-2 weeks.
- Outcome: shortage relief, retaliation and rescue reference actual world state and player history.
- Implementation: explicit preconditions, bound entity IDs, scores, cooldowns, shared opportunity state, expiry and validation at each step.
- Acceptance: another player kills a giver, removes an item or changes the destination. The opportunity adapts/fails without contradicting committed facts; story-time creation of new content (with provenance) is allowed. Unique rewards cannot be claimed twice.
- Director test: explain each opportunity's causes and whether it changes a meaningful player choice.
- Exclude: LLM authority, unrestricted natural-language action parser, general narrative planner, private alternate worlds.
- Stop/reduce: remove a complex template if it cannot remain consistent under concurrent play.

## Q08 - A mod another agent can write

- Owner: content agent. Dependency: start a small version alongside Q02; extend through Q07. Timebox: 2-4 days for first proof.
- Outcome: a second agent adds content without changing engine/game core.
- Implementation: validated namespaced data and a manifest. Use identical APIs for shipped content. Add behavior hooks only for a demonstrated need.
- Acceptance: wall variant + faction parameter + opportunity template load, replicate and save. Wrong manifest refuses join clearly. Missing mod preserves the save and reports the problem.
- Exclude: universal script sandbox, native plugin marketplace, arbitrary code auto-download/execution.
- Stop/reduce: if the sample requires a core edit, document the missing seam and add only that seam.

## Q09 - Recovery and one real migration

- Owner: persistence/integration agent. Dependency: Q03/Q08 and the first incompatible schema change. Timebox: 3-5 days.
- Outcome: versioned saves can recover from an interrupted write and one declared schema transition.
- Implementation: snapshot, previous recoverable version, validation, generator/content identity and the actual migration function.
- Acceptance: restore previous save; migrate a fixture; refuse incompatible/missing mods without data loss; reload edited bases and pending events.
- Exclude: full event sourcing, infinite logs, universal migration framework.
- Stop/reduce: resolve corruption before long-session testing.

## X01 - Compare visual production cost

- Owner: presentation agent + director. Dependency: Q00/Q01. Timebox: 3-5 days; can run independently.
- Outcome: choose sprites versus simple 3D/hybrid from the same test street.
- Compare: 100/500/1,000 proxies, overlapping crowds, doorway occlusion, build selection and equipment readability. Record authoring hours, CPU/GPU frame times and memory.
- Acceptance: director chooses one representation and camera with understood limitations.
- Exclude: finished art pipeline and large asset library.
- Stop: avoid producing two complete renderers.

## X02 - Optional GPU operation

- Owner: compute agent. Dependency: an equivalent CPU reference operation (a prior CPU bottleneck is no longer required; director 2026-10-25). Timebox: 5-10 days; deployment may require a separate 3-5 day spike.
- Outcome: determine whether one homogeneous operation benefits from portable compute.
- Implementation: Godot compute/storage buffers or field texture. Numeric formats, explicit IDs and synchronization. CPU authority validates/commits required outputs.
- Compare: 10k/100k/1M benchmark records, density and output size; upload + dispatch + synchronization + readback + integration, not just kernel time. Include graphics contention and required network/save work.
- Acceptance: provisional 2x end-to-end operation improvement and material total-tick gain (suggested 20%) at actual intended workload, correct invariants, required-platform evidence and usable CPU route.
- Hosting: ordinary Godot --headless disables rendering. Prove actual offscreen/device initialization before claiming GPU server support.
- Exclude: CUDA as required portable foundation, GPU script compiler, client authority, multi-GPU.
- Stop: retain CPU if transfer/deployment/compatibility erases the gain.

## X03 - Optional voxel branch

- Owner: world agent + director. Dependency: director declares digging essential. Timebox: 5-10 days.
- Outcome: integrated editable terrain, not merely a renderer demo.
- First evaluate: Luanti reuse versus Godot voxel tooling; choose one experiment.
- Acceptance: two clients edit the same small terrain, zombies navigate after repeated changes, save/restart and late join agree. Measure collision/nav rebuild spikes and bytes transferred.
- Exclude: smooth SDF terrain, fluids, collapse, planet scale and fine-resolution whole-world storage.
- Stop: if the integrated edit loop is not reliable, defer excavation and retain modular building.

## Q10 - Integrated evidence review

- Owner: integration lead and director. Dependency: Q02-Q09; optional spikes only if accepted. Timebox: 2-4 weeks.
- Outcome: decide whether the constrained game earns further investment.
- Scenarios: dense horde at a door; all players together; players in separate regions; repeated edits; late join/reconnect; save/recovery; two-hour soak; human playtest.
- Provisional workload: 8 clients, 10k persistent actors, 1k detailed actors globally, including representative sensing, navigation, combat and replication. A movement-only crowd cannot pass this gate. Reduce honestly if evidence requires. 16 clients is a later probe, not included support.
- Proposed budgets: 20 Hz server; p95 tick work under 35 ms, p99 under 50 ms on declared reference hardware. Client 60 FPS aspiration with measured frame tails. No performance result exists until measured.
- Acceptance: correctness invariants hold, material choices have traceable effects, players understand a consequence, independent factions act, sample mods work and performance envelope is explicit.
- Deliver: build, benchmark CSV, issue list, playtest notes and next-scope decision. No commercial release-date promise.


## v0.2 evidence-driven additions

See VIDEO_REVIEW.md for inspected timestamps and limitations.

Q07: bind a failed delivery, actual cargo, carrier, site, faction need and witness knowledge through IDs and a few typed links. Use records/lists/dictionaries. Test another player relocating cargo before acceptance, expiration, shared reward claims and save/reload. Start with a format-string request composed from location/problem/want fields. Exclude a generic graph database, unrestricted planner and generative-text dependency.

X02: optionally compare a 1024 x 1024 numeric data texture with CPU and storage-buffer variants for the same operation. 1,048,576 allocated cells are capacity, not measured full NPC throughput. Report valid/updated record count, precision, transfers, readback and authoritative integration. Do not expand the GPU timebox without evidence.
