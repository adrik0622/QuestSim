# QuestSim director guide

**Experimental phase (director, 2026-10-25).** Everything in this planning kit —
"confirmed" directions, invariants, gates, budgets and rules in ENGINE_RULES.md — is a
current working hypothesis for experimentation, not a fixed requirement. Agents should
treat these as defaults to test, propose changes when evidence or a better idea
suggests one, and never present a planning default as a director prohibition. The
director revises any of it freely.

## Confirmed direction — 2026-10-25

A living multiplayer world; first game is cooperative zombie survival. Persistent
humans/zombies, autonomous factions, modular construction and stories grounded in
world events are the destination, not this milestone. Players share world rules;
the world does not revolve around them. The director connects opportunities to
actions/relationships while respecting persistent facts.

No digging initially. Start with two clients, validate 2–8 for the initial slice,
aim for 16; 32–64 are later experiments. One server authority is for consistency;
competition and anti-cheat are not priorities.

**2026-10-25 (later): CPU-only authority removed.** The server may use the GPU for
bulk simulation (population steps, fields, spread). The CPU remains the single owner
of facts, ledgers and transactions (identity, inventory, deaths, faction resources,
fact log) and validates/commits GPU results at coarse tick boundaries. Kernels use
integer/fixed-point data and keep a CPU reference implementation for tests and
fallback. CPU↔GPU transfer and readback latency is a design constraint: never block
the authoritative tick on readback; consume results asynchronously and measure end
to end. Large-scale world simulation (humans, zombies and their factions, including
zombie factions/politics, rumor propagation) and rudimentary procedural generation
are foundational for v1.0, explored through experiments with explicit compromises.

Disconnected actors disappear and cannot be harmed while absent. Preserve identity,
inventory and progression; validate safe re-entry after world changes. Empty servers
pause simulation by default; a future setting may continue it. Quest deadlines use
authoritative simulation time. Future quests have availability, acceptance and
expiration times; accepting/rejecting/completing/missing deadlines can change
persistent facts, and logout does not reverse consequences. Nearby detailed and
distant scheduled behavior is proposed, subject to testing. Implement only when
the relevant milestone requires it. Keep simple data definitions and useful mod
boundaries as actual features arrive.

## Presentation decision gate

World structure (flat/stacked/continuous 3D), representation (sprites/polygons/voxels/
hybrid) and camera are separate decisions. First- and third-person are preferences
to investigate against cost and gameplay, not architecture requirements. Keep
Godot .NET/C# unless an actual blocker justifies reconsideration; no engine survey.
The one-day disposable comparison is complete; see PRESENTATION_EXPERIMENT.md
for measurements, screenshots and limitations. Low-poly 3D with first/third-person
was the implementation recommendation. The director authorized the two-client
increment after reviewing low-poly 3D on 2026-10-25; use that presentation with
first/third-person for Q01. Broader world structure and production art remain open.

## Remaining creative contract

Choose these before investing in assets or terrain infrastructure:

| Decision | Proposed starting assumption | Why it matters |
| --- | --- | --- |
| Presentation | Q01 low-poly 3D with first/third-person; broader structure open | Comparison and two-client evidence retained. |
| Construction | Modular grid walls/doors/containers | Proves base building without excavation technology. |
| Digging | Deferred | If mandatory, evaluate Luanti/block voxels before committing to Godot terrain. |
| Multiplayer | 2 first, validate 2–8, aim 16 | 32–64 deferred to scalability experiments. |
| Simulation detail | Identity everywhere, coarse far behavior | Defines what “all zombies tracked” means. |
| World time | Pause when empty | Prevents unattended co-op collapse by default. Autonomy tests deliberately override pause. |
| Disconnect | Absent and protected; identity/inventory/progression persist | Validate safe re-entry when world changes. |
| Capacity | Actual weekly engineering hours and target machines | Estimates depend on experience, availability and hardware coverage. |

The confirmed rules above override older planning assumptions. Reference hardware,
weekly capacity and presentation selection remain open; they do not block setup.

## Define meaningful consequences

Replace an untestable promise that every action deeply changes the world with this contract: material actions change persistent state; important changes can propagate across systems; players can discover some causal connections.

Three candidate test stories:

1. Taking depot food changes stockpiles, scavenging assignments, trade requests and trust if witnesses identify the theft.
2. Barricading a route changes travel, delivery timing and where a horde encounters survivors.
3. Helping a rival faction changes resources and relationships, opening one alliance while closing another.

**Story-time invention is allowed and a preferred approach to explore (2026-10-25).** As in the OnceLost devlog (VIDEO_REVIEW.md), when an opportunity is told the story director may create the items, creatures, locations and events it needs (a merchant's lost cart, rum, gold, wolves), shaped by facts already in the world (bears passed through, so wolf corpses instead of wolves) and materialized lazily when someone arrives. Once created, that content is committed shared state: the same for every player, persisted, and subject to the usual exactly-once rules. Faction responses can mix existing members/resources with story-time creation; which balance feels best is an experiment. **Shared world state for initial runs (2026-10-25):** story-created content, world items and inventories stay synchronized for all players, and every player sees the same cart contents; per-player instancing is deferred to a later experiment.

## Long character arcs

Begin with identity, relationships, reputation, commitments, debts, injuries and faction membership that survive sessions. Add authored templates that respond to these facts. An arc can end in failure, retirement or death; decide whether a new character inherits a faction/base legacy.

Shared rules coexist with the confirmed protected-absence policy for players.

## Weekly review

Ask for one runnable build and a short observed/assumed/deferred report. Use the same seed/save to see progress. Spend approximately 30 minutes:

1. Inspect faction wants and resources.
2. Let the world act without intervention.
3. Make one material change with player A while player B is elsewhere.
4. Discover a resulting opportunity; invalidate one prerequisite with the other player.
5. Build at a doorway, inspect navigation, reconnect and reload.
6. Explain what happened without relying only on developer logs.

Judge whether the result creates an interesting choice, remains understandable, respects the other player's actions and persists correctly. A million moving dots does not demonstrate these things.

## Prototype gates

- After the first two-week attempt: is the shared authority loop reliable enough to build on?
- At 6-10 weeks: are persistence, independent activity and a basic consequence loop credible? Revise scope before investing further.
- At the integrated slice: do human playtests justify more content and scale?
- Before adopting GPU or voxels: did the whole integrated workload improve or become uniquely possible, with acceptable portability and maintenance?

For an experienced solo developer at full-time capacity, a bounded slice is a provisional 5-10 month project including contingency. A skilled 2-3 engineer team might reach comparable scope in 3-6 months. These are planning judgments; they exclude a finished commercial game. AI assistance does not eliminate integration and playtesting. Re-estimate from observed throughput.

## Keep, change or stop

A useful experiment can reject a direction. Stop GPU integration if its end-to-end gain is immaterial. Stop terrain expansion if edited navigation is unstable. Reduce story complexity if facts conflict. Fix state duplication before adding any feature.

Do not lower all ambitions at once. Preserve the core feeling: an independent world, shared rules, lasting consequences and understandable opportunities. Reduce map size, actor detail, art complexity and feature count first.

## Decisions to revisit after evidence

- Does offscreen combat feel fair and preserve important outcomes?
- Can factions recover, or can the world become permanently unplayable?
- Should zombies replenish through infection/migration? What are the explicit limits?
- Are many floors or free-camera 3D essential enough to justify added work?
- Is the director allowed to initiate legal events, or only surface existing opportunities?
- Which code mods are trusted, and what compatibility promises can actually be maintained?
