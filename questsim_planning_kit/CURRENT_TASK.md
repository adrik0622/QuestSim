# Current task: F19 / settlement economy and the soak run

Status: implemented 2026-10-26 (STATE.md has the evidence). Director: "let's move forward";
decisions in DIRECTOR.md "Depth". Change from the plan: "at least one settlement recovers" moved
to F20/F21 (nothing resupplies a settlement until trade or raids exist); the recovery path itself
is a domain check. Previous task E1 is complete (E1_RESULTS.md). Experimental: measurements and
playtests decide what is kept.

## Question

Can 229 settlements with four goods produce, consume, share and run short in a way that gives
factions real reasons to trade or fight, stays stable over weeks of history, and shows up for
players (debug map now, rumors always)?

## Scope

1. **Goods and profiles.** Food, medicine, ammo, materials. Each settlement gets a seeded
   profile (towns and about a quarter of districts farm; some workshops, a few clinics; the
   rest mixed) that sets production per resident per game day. Units are person-days; all
   integer arithmetic.
2. **State (CPU-owned, saved).** Stock per good, order 0-100, hours short of food. Residents
   are living humans whose home is the settlement (counted in the population step). Save
   schema 5; v4 saves migrate (byte-exact backup) and get the generated starting economy.
3. **Hourly step (game hour = 75 population ticks at 48:1).** Production minus consumption;
   stock capped at 30 days (surplus spoils); same-faction neighbours along roads share part
   of the difference in days-of-food; the Choir shares only with the Choir. Order falls while
   food or medicine is short and recovers otherwise. Hungry, disorderly settlements send a
   share of residents to the neighbour with the most food per person (refugees travel there).
4. **Facts.** "shortage" when a settlement runs out of food or medicine (a rumor), "recovered"
   when it is supplied again (fact only). Hysteresis so a settlement on the edge does not spam.
5. **Prices.** Price of a good per settlement from days of stock (shown on the map; used by F23).
6. **Soak run.** Headless console tool: a population plus economy fast-forwarded N game days
   from a seed; a timeline (JSON) and map images; fails on degenerate history.
7. **Debug map (M in the client).** Settlements by faction and size, roads, shortages, order
   and stock; the player's position. Full transparency, provisional.

## Acceptance

- Same seed, same economy every run; save after N hours and reload equals running straight.
- Goods are conserved apart from production, consumption, spoilage (all counted per hour).
- Soak (30 game days, 100k agents): no good is short everywhere or nowhere; at least one
  settlement runs short and at least one recovers; the share of short settlements stays below
  half after day 3; no faction loses more than half its residents to migration; order stays
  in 0-100.
- Facts and rumors appear for shortages; a player can hear about one by asking an NPC.
- v4 saves migrate with a byte-exact backup; protocol bumped for the map data.
- test_all green on Linux and Windows.

## Not in this task

Faction decisions, caravans, raids, leaders, player trading (F20-F23); per-person economies
(Horizon).
