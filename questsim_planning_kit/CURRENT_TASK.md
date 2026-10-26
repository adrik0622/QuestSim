# Current task: Q01 / two clients and persistent presence

Authorized 2026-10-25 after the low-poly 3D review. Treat moving on as choosing
low-poly 3D with first/third-person for this increment; broader world structure
remains open. One flat 12m room is the fixture, not a stacked-floor commitment.

Acceptance:
- Godot .NET CPU-only headless authoritative server; two independent clients.
- Clients submit bounded movement intent; server owns positions and presence.
- Stable character identity/inventory/progression survive disconnect and restart.
- Disconnected characters are absent; occupied/changed re-entry positions relocate safely.
- Fixed simulation ticks stop while empty, resume on join; no offline wall-time catch-up.
- Verify real ENet processes, late join, convergence, duplicate identity/input,
  reconnect, server restart, safe re-entry, invalid save rejection and desktop control.

Use plain C# state, Godot ENet/RPC, small versioned JSON saves with atomic replace.
No existing game saves/wire format exist; this introduces save/protocol version 1,
rejecting incompatible versions rather than guessing. No new dependency/framework.
Keep the disposable presentation lab intact; create a small game project separately.
Bind localhost by default; no authentication, relay or public hosting work.

Checkpoint domain/persistence first, then network/view, then acceptance evidence.
No inventory gameplay, construction transactions, factions, quests, NPC population,
scale or production art. Report exports/other hardware as untested if unavailable.
Done: meaningful unit checks, real server/two-client checks and bounded GUI evidence,
reproducible launch commands, accurate STATE and one next action. Stop at acceptance.
