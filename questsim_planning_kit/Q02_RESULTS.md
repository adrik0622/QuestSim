# Q02a — one atomic item interaction, 2026-10-25

Complete: a green crate holds one bandage. F makes a new pickup attempt; T retries
the same persisted request/target. The server validates the active character,
crate and two-metre reach. Competing requests transfer one unit to one character.
The empty crate stays empty across disconnect/restart; no refill mechanic exists.

## Verified evidence

73 checks passed on the existing Linux/.NET 10/Godot 4.6.2 development setup:

| Check group | Passed | Evidence |
| --- | ---: | --- |
| Plain domain, including original fixtures/Q01 rules | 38 | Conservation, same-ID/different-actor isolation, winner/loser replay, conflicting payloads, invalid/disconnected actors, reach, overflow, backed-up migration, corrupt receipt/stock/inventory rejection |
| Real ENet item tests | 12 | Concurrent clients, saved requests, identical retries, reconnect/restart, actual lost acknowledgement, save failure, v1 world/profile migration |
| Rendered F/T tests | 4 | Pickup, empty result in other window, retry, reconnect retry, unchanged absence/clock behavior |
| Q01 ENet regression | 12 | Presence/movement, bounds, identity, absence, clock, restart/re-entry |
| Q01 persistence fault regression | 4 | Save lock, startup failures and failed final-save exit status |
| Terminal regression | 3 | Documented launchers, Ctrl-C flush, client disconnected state |

Build: zero warnings/errors. GUI screenshots inspected; successful runtime logs
have no errors/warnings. Injected disk failures deliberately exit nonzero.

The lost-ack test stops the **server**, sends a client request and verifies its
local profile is durable, then stops the **client** before resuming the server.
After the committed tuple appears on disk, it kills the server. The stopped
client has no ITEM_REPLY. Relaunching both and pressing the retry path returns
the saved success with exactly one bandage. This is actual blocked reply delivery,
not just clearing a result cache.

Separate failures block the client's pending-request write (no RPC sent), or the
server's save (no successful reply, old world remains valid). After restoring the
server save path, the same pending request transfers once. Native v1 migration
also preserves a non-empty inventory and unknown namespaced item/block IDs.

A single bounded read-only review found that a success receipt with missing
awarded inventory was accepted. Validation now rejects that state; the corruption
check passes. No unresolved observed failures remain in this task.

## Why the transaction stays small

TakeItem is a synchronous domain function, called on the server's main thread.
It checks replay **before** reevaluating mutable world conditions, then changes
inventory, crate count and the request receipt together. Those fields share one
atomic JSON replacement; the server replies only after that save succeeds.
On persistence error it stops serving requests and exits. No transaction service,
command bus, custom transport or distributed guarantee was introduced.

Receipts are keyed by stable character ID plus canonical GUID request ID, and
retain the original target and result. Reusing an ID for another target is refused.
Success and rejection receipts survive restarts. The client persists ID/target
before sending, so an unknown reply can be retried after reconnect. Inventory comes
only from server replies/welcome; stock comes from authoritative snapshots.

## Compatibility

Save schema and wire protocol are now **2**; content ID `questsim:q02-room-v1`.
Rebuild server and every client. Q01 two-field profile files remain compatible;
new request/target fields are added on the first attempt, without replacing identity.

Valid save v1 / content `questsim:q01-room-v1` is validated, copied byte-for-byte
to `world.json.v1-<guid>.bak`, then upgraded in memory. A later save writes v2.
Migration retains characters/inventory/progression, ticks and canonical geometry,
adding one supply and empty receipt history once. The actual process test proves
backup bytes match and v2 restart never repeats migration/refill. Incomplete,
unknown or inconsistent data still fails closed. No old game save was reset.
Test fixtures alone were migrated; actual play saves were not touched by tests.

This scope has no item use/drop/trade. Validation assumes awarded items cannot yet
be spent, and receipt history is not pruned. These rules must evolve when an actual
consumption/transfer milestone requires it. No general inventory/mod framework.
The yellow construction module remains a static obstacle.

## Artifacts and next step

[Filled crate](../src/QuestSim.Game/evidence/q02/items-filled-crate.png) ·
[Winner](../src/QuestSim.Game/evidence/q02/items-winner.png) ·
[Other client](../src/QuestSim.Game/evidence/q02/items-loser.png) ·
[Reconnect/retry](../src/QuestSim.Game/evidence/q02/items-returned.png)

Runtime source checkpoint: `60d8357`; fingerprints and concise JSON results are
under `src/QuestSim.Game/evidence/q02/`. Full raw logs/fixtures:

- `.artifacts/q01/items-race-1792987371791090143/`
- `.artifacts/q01/items-lost-ack-1792987378817777539/`
- `.artifacts/q01/items-save-failure-1792987382104462615/`
- `.artifacts/q01/items-migration-1792987385121771687/`
- `.artifacts/q01/items-desktop-1792987496321280583/`
- `.artifacts/logs/q02-*.log`, `item-checks.log`, `item-desktop-checks.log`

Only Linux development builds/loopback were tested. No new release, WAN, 2–8 load,
packet-loss, power-loss or directory-fsync durability claim. Previously documented
camera/animation/authentication limits remain. Q01 evidence is historical, not a
benchmark of this changed scene. No performance optimization was attempted.

Next bounded task: one authoritative modular build placement, with a last-cell
race, exactly-once resource cost, retry protection and persisted results. Stop
here; do not expand to a full construction system in this item handoff.
