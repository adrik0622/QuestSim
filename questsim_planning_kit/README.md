# QuestSim planning kit

Research and proposed implementation plan, 25 October 2026. Version 0.3 adds a lean Codex implementation workflow and task handoffs. The v0.2 video visual review is retained; narration remains unverified. Prepared for Adri Kingston. QuestSim is a working project label. No engine prototype or performance benchmark has been implemented by this planning task.

The companion `QuestSim_Feasibility_and_Delivery_Plan.pdf` supplies background for the director. Implementation agents start with the concise task files rather than loading the entire report. This kit is repository documentation, not executable game software.

- `AGENTS.md`: concise rules loaded by implementation agents.
- `ENGINE_RULES.md`: detailed engineering agreement, read by relevant section.
- `CODEX_WORKFLOW.md`: operating plan, delegation policy and copy/paste prompts.
- `CURRENT_TASK.md`, `STATE.md`, `RUNBOOK.md`: initial task, factual handoff and verified commands.
- `BACKLOG.md`: dependent task cards with outcomes, gates, timeboxes and exclusions.
- `DIRECTOR.md`: decisions, review procedures and meaningful playtests.
- `DECISION_TEMPLATE.md`: a compact experiment/architecture decision record.
- `VIDEO_REVIEW.md`: timestamped observations, limits and resulting prototype changes.
- `RESEARCH_SOURCES.md`: primary sources and evidence caveats.
- `benchmark_results.csv`: header-only measurements template. No results are fabricated.

Provisional direction: Godot 4 .NET/C#, CPU authority, fixed camera, grid construction, two clients first and 2-8 for the prototype, persistent actor identities, coarse distant simulation and a bounded story director. Arbitrary digging triggers a separate engine evaluation; GPU compute is optional research.

All sizes and timings are proposed targets or estimates. The director must select actual hardware, working hours and acceptable approximations. Do not turn this plan into a release promise or claim that a paper proves the whole requested game.

Start with tasks Q00-Q02. Keep one integration owner. Begin mod support and networking in the first room; do not postpone them until the final milestone. Do not add speculative infrastructure to satisfy future tasks early.

For implementation, start with CODEX_WORKFLOW.md. The architecture PDF remains v0.2; this operational kit is v0.3. Install the concise AGENTS.md at the actual repository root, merging existing instructions. The project has not yet been implemented.
