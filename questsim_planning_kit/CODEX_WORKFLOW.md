# QuestSim: implementing with Codex efficiently

Version 0.3 - 25 October 2026. This is an operating guide for the implementation repository. It does not start implementation or claim that a game exists.

## Recommended operating model

Use one implementation agent for one observable outcome at a time. Add a focused reviewer for consequential changes and an extra worker only for independent work. The director specifies the experience and accepts playtests; agents write code, tests, launch scripts, diagnostic tools and accurate documentation.

Optimize total usage per accepted, verified milestone. The shortest prompt, cheapest model, fewest files or fewest lines do not necessarily minimize retries and rework. No workflow can guarantee the mathematical minimum tokens or a fully autonomous finished game.

## Install the kit in the real repository

Extract the ZIP into staging. Put its documentation where the coding agent can read it. In a new repository, place this kit's AGENTS.md at the repository root so it is discovered; put the other kit files alongside it initially. In an existing repository, ask Codex to merge relevant rules with existing instructions rather than overwriting them. Do not copy the PDFs, uploaded video, assets or generated logs into routine prompts.

Run Codex from the actual project directory. Keep source, executable checks and concise state in Git. A repository already backed by Git is the implementation source of truth. Use local agent tools and ordinary Git; no agent SDK, orchestration server, skill package or workflow framework is needed to begin.

## Model and effort policy

| Job | Starting choice, if available | When to escalate |
| --- | --- | --- |
| Most implementation and integration | GPT-6.1 Sol, client-default reasoning | Ambiguous contracts or repeated failures with a diagnosed cause |
| Clear, narrow edits or focused scans | GPT-6 Luna, its supported default effort | If correctness or follow-through is weak |
| Persistence/network races, difficult bugs, architecture decisions | Sol with higher supported effort; Astra for a particularly hard bounded question | Only while the difficult question remains unresolved |
| Repeated builds, format checks, seed suites | Shell/CI | Ask an agent to diagnose failures, not narrate every successful run |

Use Standard speed when minimizing allowance/credit consumption. Avoid keeping every task on Ultra/Max. Select the actual model/effort in the client or supported configuration; instructions in a Markdown file do not enforce routing. Check which models your account exposes. A smaller model may lower cost without lowering raw token count.

## Small context, reliable continuity

The root AGENTS.md contains the durable minimum. ENGINE_RULES.md preserves detailed project constraints. CURRENT_TASK.md holds one outcome and acceptance checks. STATE.md is a short factual handoff. RUNBOOK.md holds actual commands. BACKLOG.md is consulted by relevant task section. Research remains available when a decision needs it.

Do not ask every agent to read the entire backlog, feasibility PDF and conversation. Start with the task and state, search relevant code, and widen only when evidence requires it. Necessary architectural context must still be supplied; starvation causes errors.

Keep the same chat while solving the same problem. Use a new task chat at a completed boundary with a concise handoff; do not restart after every compiler error. Compact an overgrown continuing chat when useful. Summaries must retain failed checks, assumptions, versions and unresolved decisions. References reduce repeated reading only when the agent follows this selective workflow; files still cost context when loaded.

## The implementation loop

1. Select one acceptance scene or invariant. If a backlog task is too large, split it by observable behavior rather than by technical layers.
2. Put outcome, relevant paths, exclusions and proof in CURRENT_TASK.md. Keep it roughly 100-250 words when practical.
3. Agent inspects relevant code, states a short plan for nontrivial work, implements, runs required checks and reviews the diff.
4. Preserve full logs as files; return commands, pass/fail counts, relevant errors and exact limitations.
5. Review shared-state, save and compatibility changes independently. Fix actual findings and rerun affected checks. Avoid unlimited review/polish loops.
6. Update STATE.md and create a checkpoint under the repository's commit policy. The director runs the brief playtest at meaningful milestones.
7. Advance only when acceptance is demonstrated; otherwise return the precise blocker or revise scope explicitly.

Do not request verbose reasoning traces or repeated replanning. Ask for a short decision, implementation and evidence. A plan need only explain the approach and unresolved choices.

## Project order and review gates

| Sequence | Small deliverable | Evidence / review |
| --- | --- | --- |
| Bootstrap Q00/Q01 | Launch a placeholder room; start CPU server; connect two clients in separate steps | Actual commands; platform coverage recorded |
| Q02 | Shared item transfer, then one build action | Race and duplicate-request tests; focused authority review |
| Q03 | Stable IDs/save-load, then coarse/detail transition | No lost/duplicated identities or resurrected deaths; persistence review |
| Q05/Q06 | One valid generated interior; a door changes navigation | Seed validation, edit/reconnect/reload checks |
| Q04/Q07 | One shortage loop; one fact-linked opportunity | Independent world event; second player invalidates a prerequisite |
| Q08/Q09 | Independent content mod; first actual migration/recovery | Manifest mismatch and recovery evidence |
| Q10 | Integrated session and scattered-player workload | Required checks, measured limits and human playtest |

Begin the small data-mod seam alongside the first room; Q08 hardens it. Q04/Q05 may be reordered around actual dependencies. The full BACKLOG.md remains authoritative for acceptance detail. GPU/voxel experiments remain gated; they are not automatic next tasks once coding becomes difficult.

## Delegation policy

Default: one active implementer. Recommended cap: main agent plus one bounded helper. This is a project recommendation, not a Codex product limit.

Delegate a concrete job such as investigating one failing seed, checking one save transition, or reviewing one diff. Give task ID, base commit/diff, allowed paths, necessary facts, acceptance and a short return format. Let the parent retain integration responsibility. When the client supports context isolation, supply only necessary context; do not assume all clients let you disable inherited history.

Read-only investigation/review is usually easier to coordinate. For concurrent writers, use separate worktrees and non-overlapping ownership. Agree shared IDs/messages before splitting. Never let two agents redesign the same contract. Inspect integration and rerun relevant checks; isolated success does not prove combined success.

Do not start a PM/designer/architect/implementer/tester/reviewer swarm for every task. Do not delegate a task cheaper to finish directly. Default to no nested delegation. If subagents are unavailable, run the same bounded review in a separate session against the same commit. A read-only instruction expresses a policy, not a distinct permission sandbox.

## YAGNI review questions

- What current acceptance criterion requires this type, interface, dependency or service?
- Would an existing API or direct function solve it?
- Is this abstraction supporting a real second use, or an imagined future one?
- Does the change preserve server authority, stable identity and accounting?
- Does the test protect observable behavior or merely repeat implementation?
- Is acceleration justified by a profile, and is its end-to-end cost measured?

Prefer the smallest correct domain representation. A deliberate boundary between world state and rendering is useful now because headless multiplayer requires it; a universal interchangeable-engine framework is not.

## Copy/paste: first setup

```text
Work in this repository as QuestSim's implementation agent. Follow AGENTS.md.
Read CURRENT_TASK.md, STATE.md and RUNBOOK.md; inspect only relevant source
and project rules. Preserve existing work. Complete the bootstrap task:
establish the real toolchain and smallest launchable placeholder project,
verify what you can run, and write actual commands into RUNBOOK.md.
Use the provisional scope for reversible setup. Record director decisions
that remain open. Do not implement the whole backlog or spawn subagents.
Update STATE.md with evidence, limitations and one next task. Report briefly.
```

## Copy/paste: ordinary implementation

```text
Implement CURRENT_TASK.md under AGENTS.md. Use STATE.md and RUNBOOK.md;
read only relevant source and rule sections. Choose the smallest readable
implementation, complete required checks, and fix failures within this scope.
No subagents for this task. Stop at acceptance. Update STATE.md. Report the
outcome, exact checks/results and material limitations in <=150 words unless
a failure needs more explanation.
```

## Copy/paste: focused independent review

```text
Use one independent reviewer for the current task diff against <base commit>.
Review only acceptance, authority/accounting, persistence or compatibility
risks touched by this change, and unnecessary machinery. The reviewer must
not edit files or delegate further. Return only actionable findings with
file references and reproduction reasoning, or "no findings" plus residual
test gaps. The main agent fixes valid findings and runs affected checks.
```

Replace the base commit before sending. Use this for consequential changes and milestone boundaries; small local edits can use the implementer's own diff review. If running a separate reviewer session, ask that session to review without edits, then give its findings to the implementer.

## Copy/paste: one independent worker

```text
Delegate exactly one bounded job: <outcome>. It may read <context paths>
and change <owned paths> in an isolated worktree, without nested delegation.
Acceptance: <proof>. Exclude <out of scope>. Return a short summary, commit
or diff, checks and unresolved issues. The main agent owns shared contracts,
integration and final checks. Do not delegate unless these boundaries make
the work independent; otherwise implement sequentially.
```

## Copy/paste: resume after a limit or interrupted session

```text
Continue the current task. Read AGENTS.md, CURRENT_TASK.md and STATE.md,
inspect the actual diff and available logs, and verify the handoff against
the repository. Resume at the first unfinished step; reuse valid existing
results. Do not repeat research or completed checks unless code changes or
uncertainty justify it. Record exact blockers and keep the report concise.
```

## Copy/paste: bounded unattended batch

```text
Complete these already-specified tasks in dependency order: <task IDs>.
Use one implementer, with a focused reviewer only for consequential changes.
Checkpoint each accepted outcome and keep STATE.md current. Implement no
additional features. Continue independent work around a director question;
stop dependent work at a decision gate or exact external blocker. Do not
start optional GPU/voxel work, broaden scope, or perform generic polishing.
Finish with one concise report of completed tasks, checks and blockers.
```

Only use this after tasks and acceptance are settled. Repeated "keep polishing" messages create open-ended demand. A finite queue gives useful autonomy without encouraging endless work.

## Failure recovery and usage measurement

After two attempts at the same failure without new evidence, identify the wrong assumption, produce a minimal reproduction, or hand off the exact evidence. A stronger bounded diagnostic may be cheaper than repeated weak guesses. Do not discard an implementation or restart the project merely because a session ends.

Measure a few comparable accepted tasks. Record task, model/effort/speed, visible usage, helpers, retry/review cycles and acceptance result. Include helper usage when the client exposes it; mark missing measurements unknown. Track raw tokens and credits separately. Prefer the workflow with lower total usage per verified outcome, not the most impressive agent count. No percentage savings is promised.

Prompt instructions and word limits are not enforceable spend caps. Use the client/workspace budget controls available to your account and inspect the actual usage dashboard. Keep required checks; shorten logs, narrow scope and reduce duplication instead.

## What remains the director's work

Choose camera, digging, player absence/death rules, acceptable coarse-simulation behavior and what feels fun. Confirm real builds on machines the agents cannot access. Agents can prepare a short playtest and implement its feedback; they cannot honestly certify subjective experience or unavailable hardware.

## Official sources and boundaries

The workflow above is project advice. Product facts checked 25 October 2026:

- [Codex best practices](https://learn.chatgpt.com/guides/best-practices): practical durable instructions, task context and verification.
- [AGENTS.md discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md): instruction files are discovered and combined by scope.
- [Subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents): helpers have additional model/tool work; model/effort can be inherited; higher effort increases usage. Availability and controls vary by client.
- [Pricing and usage](https://learn.chatgpt.com/docs/pricing): allowance depends on more than prompt length; API pricing and subscription usage differ. Verify current account settings instead of treating this guide as a rate card.
