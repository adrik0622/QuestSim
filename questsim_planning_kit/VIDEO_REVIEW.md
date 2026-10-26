# Uploaded devlog review - v0.3 (narration transcribed)

Source: `videoplayback.mp4` (user-supplied, The Wayward Realms devlog "New year — new
engine", OnceLost Games, https://www.youtube.com/watch?v=nuNqB2450go), 39:16, 640x360.

## Method and limits

2026-10-25: narration transcribed locally with Whisper large-v3 on GPU; frames sampled
every 10s as contact sheets. Transcript and sheets stay in `.artifacts/video/` (not
versioned: third-party content). Whisper mis-hears names ("Wheel of Drums" = The Wayward
Realms, "Winsloss" = OnceLost). Everything below is **narrated claim** unless marked
*shown*. No performance numbers, hardware or tick rates are given in the video.

| Time | Topic | Claim (narrated) | Shown on screen |
| --- | --- | --- | --- |
| 00:30-04:40 | Context | Left Unreal for own "Realm Engine"; month two; features over visuals. | Placeholder art. |
| 06:24-12:20 | World editing | Rejects "procgen is the only tool for big worlds". Tile map editor: tiles ~10 m, biome painting, scatter mode mimicking pollen spread, civilization layer (huts) whose NPCs are *pulled from an NPC database*, not generated. Whole 500,000 km² archipelago saves/loads fast (<1 s after an edit). | Editor, biome/scatter painting, hut placement, fast load. |
| 17:40-22:30 | Millions of NPCs | One pixel per NPC. Pixel index (row-major) = unique NPC ID. RGB = XYZ position. Processed with Vulkan; "0 to 254.999999" precision claimed as centimetre accuracy over 500,000 km². 1024² = ~1M NPCs per texture; GPU updates all in parallel; NPCs live at all times, near player or not. Range selection by RGB (position) lets an event (e.g. a city head dies) update all nearby NPCs "in the database"; rumors spread by proximity/talking, "if they believe it". Built as an external Vulkan service, easy to move between engines. | Diagram, 1024x1024 noise texture, streaks, a red cluster selection; old Unreal clip of spheres/NPCs walking. No rumor run shown. |
| 22:30-28:50 | Dynamic quests | Graph database in engine. World tiles have unique IDs like pixels. Quest "botched delivery": merchant's tale creates rum + wolves (+ cart, gold) linked to tile 568 in the graph; detail materializes when the player visits. Existing tile facts change what is created (bears passed → wolf corpses). NPCs query the graph around what the player is connected to (thieves want valuables). Merchant also told other NPCs, who may loot first. Facts contract over time (bears don't stay forever), bounding growth; the player's actions become the most connected nodes; rumors about the player spread. | Animated node diagram only. |
| 28:50-32:10 | Dialogue | Structured "location + problem + want" sentences from the graph, rephrased English→English by a translation (NLP) model; claimed "not generative": same facts, varied phrasing. | Template slide; in-build dialogue with a database NPC. |
| 32:20-33:10 | Reputation | Faction reputation computed by walking the player's graph connections. | Placeholder journal/lexicon UI. |
| 33:16-34:30 | Trees/fire | GPU procedural trees from per-species "genetic" rules; fire spreads tree to tree by wind and density. | Forest and fire footage. |
| 34:40-37:50 | Modding | All game logic in AngelScript, open to modify; glTF assets; planned public map editor; Blueprint-like node tool that queries the game database. | VS Code, file tree, node editor. |

## Engineering assessment

- **Texture = structure-of-arrays.** Colour is only storage; a 32-bit-per-channel texture
  or storage buffer is equivalent. Precision check: 500,000 km² ≈ 707 km across; 1 cm needs
  ~7.1e7 steps (~27 bits) per axis. Float32 (24-bit mantissa) gives ~6 cm at 700 km;
  uint32 fixed-point gives sub-millimetre. Supports our integer/fixed-point rule.
- **ID = slot index** ties identity to storage position. Never compact or reuse slots
  without a generation counter, or saves/links break.
- **Movement vs simulation.** Shown: points moving. Not shown: per-NPC decisions, needs,
  conflicts. "1M points updated" is still not "1M complete NPCs" (ENGINE_RULES).
- **Rumors.** The texture does the spatial query; knowledge lives in a database. A naive
  range scan is O(N) per event; many concurrent carriers need spatial binning. The video
  does not address CPU↔GPU readback/latency — our main open design constraint.
- **Story-time invention (adopted for experimentation, director 2026-10-25).** Creating
  rum/wolves/gold when the story is told, shaped by existing tile facts (bears → wolf
  corpses), is the approach to explore. For multiplayer: once created, content is a
  committed fact with provenance, identical for all players and persisted; lazy detail
  is materialized from those facts.
- **Fact decay** fits beliefs/ephemeral traces; material facts (gold exists) end only via
  explicit events.
- **Editable coarse layers + procedural detail** (biome/civilization tiles, NPC database
  independent of loading) is a good model for our "rudimentary procedural generation".

## Documentation consequences

- DIRECTOR.md 2026-10-25: GPU bulk simulation allowed; CPU owns facts/ledgers.
- Simulation experiments (population store, spread, tiers, factions incl. zombies) should
  compare CPU reference vs GPU kernels end to end, including readback latency.
- Q07 (story opportunities) uses story-time creation; created content becomes committed
  shared state and must not contradict existing facts.
- Dialogue never owns state; phrasing variation is presentation only (deferred).
