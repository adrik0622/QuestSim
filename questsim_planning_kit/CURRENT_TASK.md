# Current task: F20 / faction relations and daily decisions

Status: implemented 2026-10-26 (STATE.md has the evidence). Director: "go ahead and carry on"
after the depth plan. Changes from the plan, from the soak: thresholds scaled to 3-6 day stocks
(act under 3 days, raid in desperation under 1.5, a partner spares from 4 days); trades are
faction-scale (drawn from all settlements, delivered at the border); trade goodwill +2. Experimental: measurements and playtests decide what is kept.

## Question

Given the F19 economy, do factions make one sensible, visible move per game day (trade, raid,
truce or hold) that moves goods, changes relations, and lets short settlements recover, without
the history turning degenerate (endless war, one faction eating the rest, or nothing happening)?

## Scope

1. **Relations.** A grid between the five factions, -100..100 (zombie hordes fixed at -100).
   Starting values: Union and Traders +20, the Choir -60 with everyone. Relations drift 1 point
   per day back toward the start. Saved (save v5 fields, additive; worlds saved before F20 get them
   on the first economy hour).
2. **Temperaments** (until F22 leaders bring traits): Union cautious, Traders mercantile, Choir
   aggressive. They weight the scores.
3. **Daily decision** per human faction at its own hour (06:00, 10:00, 14:00): from faction-wide
   days of stock per good, score:
   - **Trade** with a faction at relation >= -10: barter a good we have plenty of (12+ days) for
     one we lack (under 6 days) that they have plenty of and need less; equal value at base
     prices; at most 2 days of the receiver's need. Between adjacent border settlements.
   - **Raid** a neighbour at relation <= -20 (the Choir raids anyone) when a good is under 3
     days: the raiding settlement attacks an adjacent enemy settlement with the most of it.
     Success odds from residents on each side; loot 30% of that good (10% of others); a few
     residents die on both sides (more on the losing side); relations fall; order falls at
     the target.
   - **Truce** when at relation <= -40 with a non-Choir faction: both rise by 20.
   - **Hold** when nothing scores.
4. **Delays.** A decision is a pending action saved in the world; trades arrive after 12 game
   hours, raids strike after 6. (F21 turns pending actions into travelling groups players can
   meet.)
5. **Facts and rumors.** trade, raid (success or repelled) and truce become facts and rumors,
   so NPCs talk about them.
6. **Rumor trust follows relations** (factions believe allies more than enemies).
7. **Soak.** Faction moves run in the soak too; the debug map shows relations and the last move.

## Acceptance

- Same seed, same decisions; save/reload mid-run continues identically (domain check).
- Goods conserved: trades move goods, raids move loot; nothing created.
- Soak (30 days, 100k agents): every human faction makes moves; trades happen; at least one
  settlement recovers from a shortage; raids happen but relations are not pinned at -100 for
  all pairs; no faction loses more than half its people; shortage share stays under half.
- A raid or trade becomes a rumor an NPC repeats.
- test_all green on Linux and Windows.

## Not in this task

Travelling caravans and raid parties players can meet (F21), leaders and succession (F22),
order-driven danger zones (F18), player trading (F23).
