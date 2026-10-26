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

- Later in the run: day/night at 48:1 (people head home at night; lighting follows the
  clock), turned players roam as named zombies carrying their gear ("put them to rest"),
  NPC factions fight each other (zombies vs humans, the Choir vs everyone, rival hordes with
  territories), background attrition with `attack` rumors, NPC inspector (I), Shift sprint,
  camera collision, fair detail budget, `--populate=N` for existing worlds.

Save v4, protocol 5 (crowd velocities, NPC bodies), content `questsim:city-v1`; v1–v3 saves migrate with byte-exact backups.

Verified: 120 domain checks (final count; see NIGHTLY log for per-step numbers); verify_build 11, verify_items 12, verify_multiplayer 12,
verify_save_failures 4, verify_defeat 3, verify_living 6 (1M agents hold 20 ticks/s with
background snapshots), verify_content 2, Xvfb screens (verify_build_screen 5, city screen);
E1 population checks 12; GitHub Actions green on ubuntu and windows.

Not done / limits: F1 locality (deferred: 10⁶ fits the CPU), F6 binary snapshots (JSON with
interest management only), Godot exports and Windows *game* runs (only domain checks run on
Windows), LAN/WAN (8-player loopback load done), item instances/rarity, code mods. verify_item_desktop/verify_shared_desktop/
verify_terminal_shutdown (real desktop) not rerun.

2026-10-26 (after the playtest, on main; nightly merged): ground flicker fixed (per-chunk
ground only, fog at the streaming edge); no promotions within 20 m; coarse agents walk at
real-time speed (48:1 applies to the clock); population recovery (dormant reserve, rising
dead, arrivals); crowd placeholders; local-first `scripts/test_all.sh` (GitHub workflow
manual-only). Full local suite green: 124 domain checks, all ENet suites, living, both Xvfb
screens, E1 checks.

2026-10-26 (Windows): native Windows 11 test machine `whitetop` (Ryzen 5 2600, 32 GB, RX 5700,
AMD Vulkan 1.3) set up over SSH (RUNBOOK "Windows test machine"). `test_all.sh --all` green there:
124 domain checks, items 12, build 11, multiplayer 12, save-failures 4, defeat 3, content 2,
living 6 (1M agents, 20.0 ticks/s, no lag), E1 12 (CPU and Vulkan bit-identical on AMD),
8-player load (20.04 ticks/s, ~73 KiB/s per client). Xvfb screens skipped (no Xvfb on Windows).
LAN: a headless Linux client joined a Windows server (192.168.1.x) in 0.7 s and moved at 20 ticks/s.
Fixes found there: (1) Windows refuses to replace a file another process holds open, which
crashed the server when a reader touched the save/status mid-write: `WorldStorage.ReplaceFile`
retries for up to 2 s on Windows, then fails closed. (2) The coarse population step took
~0.7 s (no rumors) and ~4.5 s (one active rumor) per 1 s tick on the 2600: grids were 65.5 m
cells for 5 m / 30 m searches. Grids now sized to each radius, contact grid laid out contiguously:
0.12 s / 0.41 s there, 0.07 s / 0.08 s on the 5950X; state bit-identical to before (fingerprint).
(3) Test scripts made cross-platform (process suspend, GPU-handle check, Python name, LF endings).

2026-10-26 (second playtest feedback): chasers that "vanished and became someone else" had been
killed by other NPCs (5 of 5 disappearances in a replay of a copy of the play world matched
recorded clash deaths) and were removed instantly. Now: bodies lie for 90 s, red damage numbers,
silhouette placeholders walk smoothly (velocity in crowd packets, protocol 5), shown placeholders
may become full NPCs inside 20 m, and people who reached home at night are indoors (the "lots
of ghosts" was a whole district standing in a 40 m square at night). 127 domain checks;
test_all green on Linux (incl. Xvfb screens) and on whitetop. Fight rate unchanged; zones with
more or less violence recorded as F18.

2026-10-26 (depth, F19 settlement economy): director chose the depth plan (DIRECTOR.md "Depth";
BACKLOG F19-F23 and "Horizon"). F19 implemented: four goods per settlement (profiles farm 61,
workshop 31, clinic 22, mixed 115), hourly production/consumption/spoilage, sharing along roads
within a faction (the Choir only with the Choir), order, shortages as facts and rumors ("People
are going hungry at the plaza"), refugees after a hungry day in unrest, prices; save v5 (v4
migrates with backup), protocol 6; M opens a full-transparency debug map; `scripts/run_soak.sh`
fast-forwards 30 game days at 100k agents in ~3 min. 140 domain checks, test_all --all green.
Soak (30 days): food shortages in up to 4% of settlements and medicine in 12% (isolated Choir and
clinic-less pockets), no recoveries yet (nothing brings supplies until trade/raids, F20-F21),
mean order 81. Finding for the director: humans decline from day ~11 (67k -> 59k by day 30 at
100k agents) because zombie attrition outlasts the 10% dormant reserve and dead slots are never
reused; over months a world would empty. Options: births or arrivals that reuse long-dead slots
under a new generation number (IDs like npc:N.g), or a larger reserve.

2026-10-26 (F20 faction relations and daily decisions): implemented (CURRENT_TASK.md, BACKLOG
F19/F20 record the soak-driven changes: faction logistics, 3-6 day buffers, seeded hardships,
a modest world surplus, faction-scale trades). A 30-day soak now tells a coherent story: a food
blight hits the Traders around day 17, 94 of their settlements fall into unrest, they barter with
the Union and raid the Choir, and recover by day 30; the Union retaliates against the Choir; the
Choir stays a medicine-starved raiding cult (~30 raids a month); Union-Traders relations hold
near +20. Rumors such as "The Hollow Choir attacked the settlement at the plaza but were driven
off." 150 domain checks. The debug map shows relations and each faction's last move.

2026-10-26 (F19b births and zombies as natural disasters; director chose births): births reuse
unreferenced dead slots; the dead drift in from just outside the city; great hordes move on
crucial settlements with a rumor first and overrun them 12 hours later; rebuilding uses
materials. Found and fixed: zombie lethality grew with population density (20% of humans a
game day at 1M agents vs 2% at 100k); encounter odds now scale with population (about 1% a
day at any size). 40-day soak (now in test_all --all): the living and the dead both hold
their numbers (67,328 -> 67,331 with 15,252 born; zombies 22,627 -> 22,609), 13 great hordes
and 14 overruns, all rebuilt; a food blight starves the Traders from day 17 (94 settlements in
unrest), they trade and raid and recover by day ~35. 156 domain checks.

2026-10-26 (pills near players): at the 1M-agent plaza 524 people were within 120 m against a
400-NPC budget, so 113 were placeholders (8 within 20 m). Now the nearest people win the budget
(far full NPCs beyond 60 m return to the crowd): none within 40 m at 400. Budget is
--detail-budget; measured with 8 players and 1M agents: 400 -> 97 KiB/s per client, 600 -> 94
KiB/s after moving the settlement table out of the world JSON (it had cost ~24 KB/s per client),
both 20 ticks/s with no lag on Linux and on whitetop (Ryzen 5 2600). At 600 everyone near the
plaza is a full NPC. Director chose 600 as the default (2026-10-26).

Handoff for the next session: HANDOFF_2026-10-26.md (state, machines, lessons, road to v1.0).

Next action: director playtest of F19/F19b/F20 (M map, rumors such as "A great horde is
moving on ...", faction news), or F21 (caravans and raid parties as travelling groups players
can meet). Tuning knobs are constants at the top of Economy.cs and Politics.cs.
