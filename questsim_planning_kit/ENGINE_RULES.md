# QuestSim implementation agreement

Apply to all agents working on this project, including delegated agents. Director instructions and actual repository constraints take precedence. This is a proposed agreement for the implementation repository, not an instruction to start building every feature immediately.

## Outcome

Build a small, portable, moddable multiplayer zombie game that proves a living world. Human and zombie identities persist. NPCs and players use the same world rules. Factions act independently. Story opportunities arise from real state and history. Construction, persistence and multiplayer authority are integrated from the beginning.

## YAGNI and simple solutions

- Implement only the current task's smallest observable outcome.
- Prefer an existing engine/library call or direct readable function. “One line” means less machinery, not compressed code or cleverness.
- Do not create a custom renderer, ECS, general-purpose engine, scripting language, universal plugin bus, microservice architecture, distributed shard system, or GPU abstraction for a hypothetical future requirement.
- Start with plain records, lists, arrays, dictionaries and explicit functions. A second implementation or demonstrated integration need must justify an abstraction.
- Add dependencies only when they replace necessary work and satisfy actual platform/version requirements.
- Profile before optimizing. Correct algorithms, bounded work and useful instrumentation belong in the foundation; speculative tuning does not.
- Stop when the acceptance condition is met. Extra features do not compensate for an unproved core.

## Provisional architecture

- Godot 4 .NET/C# client and initial Godot dedicated server; pin versions per milestone.
- Plain C# domain state independent of Node lifetimes, visual resources and engine instance IDs.
- One authoritative server and fixed simulation ticks. Server validates commands; events describe committed outcomes.
- Clients submit intent, render relevant state and interpolate. Introduce prediction/reconciliation as actual network play requires.
- Content definitions and first-party gameplay use the supported mod surface.
- CPU-only authority is required. A GPU experiment does not become a production dependency automatically.

Useful functions can initially be `ApplyCommand`, `StepWorld`, `GenerateChunk` and `SelectRelevantState`. Do not build a framework around their names.

## World invariants

1. IDs never change merely because an entity leaves an active area. Never use scene object IDs as persistent identity.
2. Inventory transfers and building costs are atomic. Retried requests cannot duplicate effects.
3. Deaths/conversions/arrivals have explicit rules and provenance. Unloading cannot resurrect or silently delete an actor.
4. Coarse groups preserve individual membership. Distant positions may be approximate; vital state and major transactions remain accountable.
5. Every entity has one authoritative state, including when several players observe it.
6. Simulation work covers the union of relevant player areas. Test separated players.
7. State visible to clients can differ by knowledge/relevance, but underlying facts cannot conflict.
8. Save seed, generator version, content manifest, actor state, world edits and scheduled events. A seed alone is insufficient.
9. Missing required mods cannot silently remove data or overwrite an incompatible save.
10. The director may not invent resources, resurrect actors or reverse another player's action to protect a story.

## Mod support

Begin with validated data definitions: items, recipes, building pieces, behavior parameters, faction settings, room templates and story templates. Use namespaced IDs and a manifest with version, API version, dependencies, hashes and load order. Reject ambiguous conflicts.

Write one independent example mod early. Add code hooks when an actual behavior cannot be expressed with the current surface. Trusted executable mods are a separate tier; a matching hash does not make code safe. Never automatically execute arbitrary server-supplied code on joining clients. Keep network object decoding disabled. Do not claim an embedded interpreter is automatically sandboxed.

Do not build a shader compiler for arbitrary mods. An optional GPU kernel exposes supported parameters and keeps a usable CPU route.

## Compatibility

For changes to IDs, saves, generator code, content definitions or wire messages, state what existing data/builds remain compatible. Back up saves before the first actual migration. Implement the migration needed, not a speculative universal framework.

Use canonical server-generated geometry until deterministic client regeneration has been proved on all claimed platforms. Pin the PRNG algorithm for durable regeneration, not only a seed.

## Working procedure

1. Read the current task, scope contract and latest decision record.
2. State the observable outcome, dependencies and shortest implementation.
3. Agree shared data contracts with the integration owner before parallel changes.
4. Implement a narrow end-to-end path through authority, persistence and presentation.
5. Run the meaningful correctness checks and required platform/network tests.
6. If performance is in scope, measure a release build on named hardware and preserve raw results.
7. Report observed results, assumptions, failed checks and the next decision enabled.

Parallelize independent work. Assign one owner per interface or shared subsystem. Integrate often; do not have several agents redesign persistence or messages independently.

## Definition of done

- The requested behavior is observable in a reproducible scene or test.
- Relevant invalid inputs, duplicate requests and multiplayer races have correct outcomes.
- State survives save/reload when the feature persists.
- Two clients converge, including reconnect/late join when relevant.
- The supported mod surface is exercised where content is added.
- Required exports run on actual target machines, or untested targets are explicitly recorded.
- Documentation describes real behavior and limitations, not ambitions.

Tests should protect meaningful invariants, not mirror implementation. Do not write exhaustive boilerplate tests for reversible cosmetic edits. Do not label a test “multiplayer verified” merely because two controllers ran in one local scene.

## Measurement discipline

Record commit, seed, manifest hash, OS, CPU/RAM/GPU/driver, engine/runtime version, build type, player distribution, persistent/detailed counts, tick/frame distributions, memory and bandwidth. Separate CPU simulation, navigation, rendering, serialization and saving.

For GPU work, include uploads, synchronization, readback, integration and required networking/save output. Compare equivalent algorithms. Record both idle-graphics and graphics-loaded cases when applicable. Do not claim a million complete NPCs from a million point updates.

## Escalate decisions, continue useful bounded work

Director decisions include camera, digging, player cap, absence/death policy, story intervention, world recovery and acceptable coarse-model error. Report those choices clearly; do not silently make them to bypass a failing test. Continue independent authorized work while awaiting a decision.

If shared authority loses state, fix it before adding features. If a GPU/voxel spike fails its gate, retain the simpler working path and record why. Failed experiments are useful evidence. Do not keep expanding infrastructure to avoid reporting a negative result.

## Source evidence rule

For video evidence, record timestamps and distinguish sampled visuals, transcribed narration, developer claims and measured performance. Never infer throughput from texture dimensions or an executable planner from a diagram. VIDEO_REVIEW.md records the present limits.
