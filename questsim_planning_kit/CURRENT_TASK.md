# Current task: Q02a / one atomic item pickup

Authorized 2026-10-25 after Q01. Origin/main synchronization is authorized; push
verified milestones occasionally, never force-push. Preserve all existing work.

One green supply crate, one bandage. Two independent clients race to take it.
Server validates identity, target and reach; transfers one unit at most once.
Stable request IDs and outcomes persist with inventory and crate count in the
same save. Retry after lost acknowledgement, reconnect or restart returns the
original result. The loser and invalid requests cannot consume/duplicate items.
F attempts a pickup; T retries the last persisted local request. No inventory UI
framework, item use, crafting, shared construction, factions or quest systems.

Acceptance: domain conservation/duplicates/conflicting IDs/invalid actor/target/
reach; real ENet race and replay; reconnect/restart; lost reply via abrupt server
termination after commit; save failure never acknowledges success; desktop F/T
and shared crate/inventory display. Rerun affected Q01 regression checks.

Introduces save/content/protocol v2. Validate and back up valid Q01 saves before
migration; preserve identities, positions, clock, inventory/progression and blocks.
Migration adds one supply and empty receipts once, never restocks a v2 crate.
Old clients must rebuild; local profile IDs remain compatible. Unknown/incomplete
versions still fail closed. No general migration or transaction framework.

Checkpoint domain/migration, then RPC/view, then acceptance evidence. Stop when
this single item transaction is proved; shared building is a separate bounded task.
