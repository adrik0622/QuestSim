# QuestSim implementation state

2026-10-26 (nightly run): branch `nightly` carries the v1.0 foundation work (BACKLOG F1–F14);
`main` is unchanged at 956edc6. Full log, results and the director's open decisions:
NIGHTLY_2026-10-26.md. Everything is experimental (DIRECTOR.md).

Implemented on `nightly` (each step checked, committed and pushed):
- F3 world: seeded 40 × 40 km city (22k buildings, 8 towns, roads), chunk streaming in the
  client, building collision on the server; the starter room is an enclosed building.
- F2/F5 population in the server: 1,000,000 agents by default, coarse 1 Hz tick on a
  background task, 20 Hz detail within 120 m of players, per-player snapshots.
- F7 living actors and combat: zombies and hostile factions hunt; rudimentary stats; V melee,
  G hitscan pistol (walls block, ammo counted).
- F10 defeat instead of death + deterministic Virtual Game Master: infection (story-time cure
  stash), turning, capture by named victors who take the actual gear, rescue and revenge
  opportunities (single claim), permadeath setting, online-only clocks, ledgered transfers.
- F8/F9 facts become rumors by word of mouth; H asks an NPC what it has heard; faction
  standing turns factions hostile.
- F4 population snapshots saved in the background and committed by hash.
- F11 data-defined items and data-only mods (manifest, namespaces, dependencies; worlds refuse
  to start without a recorded mod); U uses consumables.
- F12 acceptance chain in domain checks; F13 `scripts/inspect_world.py`; F14 CI on Linux and
  Windows (domain checks, population CPU checks, game compiles).
- F6: binary actor packets + changed-only world JSON; 8 players + 1M agents at 19.9 ticks/s,
  ~68 KiB/s per client on loopback.
- `--populate=N` gives an existing (migrated) world a population; tested on a copy of the play save.

Save/protocol v4, content `questsim:city-v1`; v1–v3 saves migrate with byte-exact backups.

Verified: 110 domain checks; verify_build 11, verify_items 12, verify_multiplayer 12,
verify_save_failures 4, verify_defeat 3, verify_living 6 (1M agents hold 20 ticks/s with
background snapshots), verify_content 2, Xvfb screens (verify_build_screen 5, city screen);
E1 population checks 12; GitHub Actions green on ubuntu and windows.

Not done / limits: F1 locality (deferred: 10⁶ fits the CPU), F6 binary snapshots (JSON with
interest management only), Godot exports and Windows *game* runs (only domain checks run on
Windows), LAN/WAN (8-player loopback load done), item instances/rarity, code mods. verify_item_desktop/verify_shared_desktop/
verify_terminal_shutdown (real desktop) not rerun.

Next action: director reviews NIGHTLY_2026-10-26.md decisions, plays the nightly build, and
decides whether to merge `nightly` into `main`.
