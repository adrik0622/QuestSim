# QuestSim agent rules

Build the current acceptance scene with the smallest readable change. The director owns experience and scope; agents implement, test and report evidence.

## Context

Read CURRENT_TASK.md and STATE.md. Read RUNBOOK.md for commands. Search relevant source and only the applicable sections of ENGINE_RULES.md; consult the matching BACKLOG.md task if needed. Research/video documents are references for unresolved research, not mandatory context for ordinary coding.

## Implementation

- Reuse an existing engine/library call or direct function. One-line solutions mean less machinery, not code golf.
- Implement the current task only. No speculative interfaces, general frameworks, custom ECS, renderer, compiler or services. Justify new dependencies with a current need.
- Experiment platform: Godot .NET/C#, plain domain data, CPU authority and modular construction. World structure, representation and camera remain separate open decisions. GPU and digging require separate scope decisions.
- Stable IDs survive scene unloading. Clients submit intent; server validates state changes. Preserve resource accounting, life/death, save identity, mod manifests and shared facts.
- Shipped content uses supported mod hooks. Missing mods cannot silently destroy saved data. The story director respects actual actors, resources and other players' changes.
- Read required compatibility rules before changing IDs, saves, generation, messages or mod schemas. Read relevant GPU evidence rules before acceleration work.

## Execution and verification

- Search narrowly with rg; inspect bounded excerpts. Keep full logs in files and surface the relevant errors. Broaden investigation when evidence requires it.
- Run required project checks and meaningful acceptance tests. Protect races, duplicate requests, persistence and client convergence. Avoid tests that only repeat implementation.
- After two attempts at the same failure without new evidence, diagnose the assumption or report the precise blocker. Do not loop blindly.
- Complete authorized reversible implementation autonomously. Record director questions in STATE.md and continue independent authorized work. Do not claim tests or platforms you did not run.
- Preserve unrelated user changes. Keep one owner per shared contract. Use isolated worktrees for concurrent writers and integrate through the owner.
- Default to one implementer. Delegate only when the task authorizes it and a bounded independent job justifies extra work. Review meaningful risk and milestone boundaries.
- Stop at acceptance. Update STATE.md with evidence and the next action. Final report normally <=150 words: outcome, checks, limitations, next step; expand for material failures.
