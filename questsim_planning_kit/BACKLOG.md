# QuestSim proof-of-concept backlog

All timeboxes are approximate experienced-engineer effort, with placeholder assets. They are probes, not additive calendar commitments. Integration and rework account for the larger milestone ranges in the brief. Each task ends with a runnable result, raw evidence and a keep/change/stop decision.

## v1.0 foundation plan (director, 2026-10-26)

v1.0 is the **foundation** we can add content to for months without rewriting what's
underneath. It isn't a content-complete game. This plan supersedes the ordering of the
older Q/X entries below, which remain as reference. Everything is experimental: each step
is a bounded experiment or increment with checks, measurements and a keep/change/stop
note. Results go in `*_RESULTS.md`; one step at a time goes into CURRENT_TASK.md.

**Order (revisited after every step):** F1 → F2 → F3/F4 → F5/F6 → F7 → F8 → F9 → F10 → F11 → F12,
with F13 tooling and F14 platform/CI growing alongside from F2 onward.

### F1 - E1b: population locality
- Outcome: sort agents (or a cell-ordered copy of hot fields) by grid cell each tick so neighbour reads are contiguous.
- Acceptance: still bit-identical to the CPU reference; measure 10⁷ and 10⁸ at constant density against E1 (15.2 s/tick at 10⁸).
- Note: 64-bit integers allowed for sort keys and transient math; per-agent stored data stays ≤ 32-bit.

### F2 - E2: detail tiers and NPC data layout
- Outcome: agents move between the coarse GPU tick (far) and full 20 Hz detail (near players) without loss, duplication or reset.
- Data layout: **generated** (seed + ID, 0 B: name, looks, personality, backstory, base stats); **hot** (GPU, 32–64 B: position, target, faction, activity, rumor bits, a few urgent needs); **warm** (CPU, 128–512 B: needs, job, stats/skills, inventory summary, ≤ 8 strong ties); **cold** (disk, sparse: real items, memories, debts, injuries). Store only deltas from generated defaults. No N×N relationships; group attitudes plus a few personal ties.
- Acceptance: repeated promote/demote round-trips are identical; two players far apart each get local detail; save/reload keeps tiers consistent.

### F3 - World space and rudimentary generation
- Outcome: a seeded, versioned region (settlements, roads, biome tiles) split into chunks the client streams. Editable coarse layers, with procedural detail on top (OnceLost-style).
- Acceptance: the same seed produces the same world; two players can be kilometres apart; generator version is stored in saves.
- v1.0 world (director 2026-10-26): one city plus outskirt towns, population roughly **1 million** agents (humans + zombies; ratio TBD). Proposed footprint ~40 × 40 km, revisited once generation exists. At this scale even the CPU path keeps up (E1: ~39 ms/tick at 10⁶), so a GPU raises the ceiling rather than being required.

### F4 - Persistence at scale
- Outcome: chunked, incremental binary saves (no full rewrite per autosave), crash recovery, versioned migration, carrying forward the Q01/Q02 guarantees (atomic writes, fail closed, backups before migration).
- Acceptance: 10⁷-agent world autosaves without stalling the authority tick; kill -9 mid-save recovers; one real migration.

### F5 - Integration: GPU population inside the server
- Outcome: the Vulkan population tick runs in the server process beside the 20 Hz authority; nearby agents appear in clients.
- Acceptance: the authority tick budget holds with the population running; CPU fallback for hosts without a GPU (smaller world).

### F6 - Networking at scale
- Outcome: interest management (clients receive only relevant nearby state), binary snapshots, protocol versioning.
- Acceptance: 2–8 players over LAN with bandwidth and tick-time measurements; late join and reconnect converge.

### F7 - Living actors, rudimentary RPG stats, basic gunplay and melee
- Outcome: zombies notice, chase, attack and die; humans run simple routines. A small shared stat block for players and NPCs (for example health, stamina, strength, agility, perception, plus a few skills), using the same rules for everyone. NPC base stats are generated from seed + ID; changes are stored as deltas.
- Acceptance: deterministic combat outcomes in domain checks; stats persist through tiers, reconnects and restarts; deaths are facts with provenance.
- Keep stats data-defined (namespaced IDs) so later systems can add stats without schema rewrites.
- **Super basic combat, to improve later:** guns as server-validated hitscan shots (range, damage, fire rate, ammo as items consumed exactly once); melee as a short-range swing (reach, arc, damage, stamina cost). Same rules for players, humans and zombies. Damage and accuracy read the shared stat block. Lag compensation, recoil, ballistics, armour and animation polish are later work.
- Acceptance: domain checks for hits, misses, out-of-range and ammo/stamina limits; two real clients can shoot and melee zombies and each other with consistent results; no duplicated or lost ammo across retries and restarts.

### F8 - Facts and knowledge
- Outcome: a persisted fact graph (who, what, where, typed links) plus rumor beliefs; NPCs answer "what have you heard?" from their actual beliefs (template text; dialogue never owns state).
- Acceptance: beliefs never alter facts; a player-caused event becomes a fact and is heard elsewhere later; save/reload preserves both.

### F9 - Factions (humans and zombies)
- Outcome: one generic faction model: members, real resources, territory, attitudes, simple goals. Zombie hordes use the same model (factions and internal politics). One consequence loop.
- Acceptance: resources are conserved; a faction responds to a fact using real members and resources; works while players are elsewhere (subject to pause-when-empty).

### F10 - Defeat, not death; Virtual Game Master; first story loop
- Defeat: a downed player's outcome depends on who defeated them, e.g. captured and imprisoned by a raider or insane faction at a real location; dragged off or left for dead by zombies with an infection debuff; waking injured after wildlife or accidents. The defeat is a fact; the victor gets a buff and promotion to a named persistent NPC and takes the player's **actual items** (exactly-once transfer); the player takes a recoverable debuff. NPCs can really die (and may also be captured).
- Virtual Game Master: deterministic and rule-based. Watches the fact graph for story-worthy patterns (defeat, shortage, captured ally, debt), scores them (connection to players, proximity, recency, pacing), connects threads with story-time invention, and commits what it creates as shared facts with single-claim rewards. An LLM may later vary phrasing only.
- First loop: **defeat → capture → escape, teammate rescue, or revenge bounty** for finding and defeating the victor.
- Acceptance: two-player run where B rescues A or claims the revenge reward exactly once; items, buffs and debuffs, facts and rumors persist through reconnect/restart; abuse caps (no capture loops).
- Decisions (director 2026-10-26, subject to change): **zombie infection can turn a player** (it fits the genre; exact mechanics TBD, e.g. an infection timer with a cure opportunity, and what the player continues as after turning). **A captive's clock pauses while that player is offline**, even if others play. **Permadeath is an optional server setting.**

### F11 - Items and building as data (start of the mod surface)
- Outcome: data definitions for item types and building pieces with namespaced IDs, generalizing the Q02 one-offs; exactly-once transfers, costs and receipts carried over.
- **Mindful, not built in v1.0:** items will later have rarity and their own stats. Keep room for it now: distinguish stackable item *types* from unique item *instances* (stable instance ID plus an optional property bag), and don't assume a count-only inventory everywhere. No rarity, affixes or loot tables in v1.0.
- Acceptance: a sample data-only mod adds an item and a wall variant; missing mods preserve saves and fail clearly.

### F12 - v1.0 acceptance run
- The DIRECTOR.md weekly review as the test: two players in a generated region with millions of persistent agents; A acts while B is elsewhere; the event becomes a fact and a rumor travels; a faction responds with real resources; the game master offers an opportunity; B invalidates a prerequisite; both disconnect, the server restarts, everything is consistent; the defeat loop works. Runs on Windows and Linux; every step can be inspected in the tooling.

### F13 - Tooling (continuous)
- World inspector (who knows what, faction stats, agent under cursor), record/replay of inputs for bug reproduction, performance telemetry. Grows with each step.

### F14 - Platform and CI (continuous)
- Windows builds tested; CI runs domain checks and the CPU reference of the population simulation (no GPU needed thanks to bit-identical results) on every push. Discrete GPUs only (NVIDIA, AMD, Intel Arc); no integrated-GPU target. Population budget scales with host hardware; a CPU-only host runs a smaller world.

### F15 - Extensive building system (planned; timing to be decided)
- Outcome: replace the single Q02b cell with grid construction anywhere valid: walls, doors,
  windows, containers and more, with data-defined pieces (mod surface), material costs,
  demolition/refunds, ownership rules and effects on player movement and NPC navigation.
- Keep: exactly-once costs and receipts, last-cell races, persistence and multiplayer convergence
  from Q02b; the BACKLOG Q06 doorway/pathing acceptance applies once zombies path around walls.

### F16 - Population recovery (director 2026-10-26) — first version done
- Done: ~10% of slots start dormant (fresh IDs for newcomers; IDs never reused). 30% of humans
  killed by zombies rise and join the killer's horde. Each coarse tick, a side below its starting
  strength wakes dormant agents (cap ≈ population/50,000 per tick): humans arrive at outlying
  towns, zombies at the map edges. Rates are tunable; reserve exhaustion ends recovery.
- Outcome: losses refill over time so players can't exhaust the world: some zombie victims rise
  as zombies, survivors arrive at outlying towns, zombies migrate in from the map edges; rates
  are tunable and persisted.

### F17 - NPC pop-in and crowd placeholders — done 2026-10-26
- No promotions within 20 m of a player (applied after stepping out of buildings).
- Crowd placeholders: each second the server sends every player the nearest 1,500 background
  agents within 200 m (9 bytes each: ID, relative position, kind/faction; ~13 KB/s per player
  measured with 8 players); clients draw them as simple figures that glide between updates
  and hide when the same person is shown in detail.

### Not in v1.0
Art beyond placeholders, content volume, deep combat, crafting trees, item rarity/affixes/loot tables, polished UI, more than 8 players, macOS, code mods (data-only for now; stay mindful of a later transition).

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
- Acceptance: exported Windows/Linux clients launch on real machines (macOS is a stretch goal); two connect to a server. Mark missing hardware coverage honestly.
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
- Outcome: determine whether one homogeneous operation benefits from Vulkan compute (Windows/Linux).
- Implementation: Godot compute/storage buffers or field texture. Numeric formats, explicit IDs and synchronization. CPU authority validates/commits required outputs.
- Compare: 10k/100k/1M benchmark records, density and output size; upload + dispatch + synchronization + readback + integration, not just kernel time. Include graphics contention and required network/save work.
- Acceptance: provisional 2x end-to-end operation improvement and material total-tick gain (suggested 20%) at actual intended workload, correct invariants, required-platform evidence and usable CPU route.
- Hosting: ordinary Godot --headless disables rendering. Prove actual offscreen/device initialization before claiming GPU server support.
- Exclude: CUDA as required foundation, Metal/D3D12-specific paths, GPU script compiler, client authority, multi-GPU.
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
