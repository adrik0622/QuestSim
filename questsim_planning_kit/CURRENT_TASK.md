# Current task: Q02b / one modular wall placement

Status: authorized by the director 2026-10-25 (material, Xvfb screen tests, start).
Q02a item pickup is complete; Q02_RESULTS.md records its evidence. Push verified
milestones to origin/main occasionally, never force-push. Preserve existing work.

Material: a second fixed crate, `questsim:panel-crate`, holds two
`questsim:wall-panel`. Both players can take one with the existing pickup rules
(F takes from the nearest crate). One fixed build cell, `questsim:build-cell-1`,
beside the spawn area. B places a 1m panel wall there; T retries the last saved
attempt (pickup or build). The server validates actor, site, two-metre reach, free
cell, no active player inside the panel, and a held panel. Success adds one block,
spends exactly one panel and records an actor-scoped receipt in the same save
before replying. The loser of the last-cell race gets `occupied` and keeps its panel.
Retries after a lost reply, reconnect or restart return the original result.

Acceptance: domain race/conservation/duplicates/conflicting IDs/invalid actor,
site, reach, material and body-in-cell; real ENet race and replay; reconnect and
restart; lost reply after commit; save failure never acknowledges success; clients
draw the new wall and HUD panel count. Keys F/B/T exercised through
Input.ParseInputEvent in real graphical clients under Xvfb, with PNG viewport
captures inspected. Rerun Q01/Q02a headless regressions.

Introduces save/protocol v3 and content `questsim:q02b-room-v1`. The single
`Supply` becomes `Supplies`; `BuildReceipts` added. Valid v2 saves are validated
as v3 and copied byte-exact to `.v2-<guid>.bak` before replacement; v1 saves still
migrate (one `.v1-` backup). Migration adds the panel crate and empty build receipts
once; it never restocks. Validation: per crate, successful takes = initial − stock;
per actor, inventory ≥ awarded − spent; total held panels = taken − spent; one placed
wall iff one successful build receipt. Old clients must rebuild; existing profiles
remain readable. Unknown/incomplete saves still fail closed.

No general construction, inventory UI, demolition, refills, doors or navigation.
Checkpoint domain/migration, then RPC/view/headless tests, then screen evidence.
