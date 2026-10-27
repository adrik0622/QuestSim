# Current task: debug console, game speed and always-on worlds

Status: approved by the director 2026-10-26 (DIRECTOR.md "Debug console"). Purpose: let the
director playtest F19/F19b/F20 at speed. Experimental: playtests decide what is kept.

## Question

Can an admin speed the shared world up enough to watch a game day of economy and faction
history in a few minutes, through an in-game console whose commands the server authorizes,
without breaking determinism, saves or the tick rate?

## Scope

1. **Console (client).** The grave/tilde key toggles an overlay with an input line and
   scrolling output; movement and game keys are ignored while it is open. The client only
   sends the text; the server runs it and replies with output lines (protocol 8 -> 9).
2. **Commands (server).** A small table (name, permission, help line, handler); every command
   is permission-checked and logged. Commands: `help` (what you may run), `time` (day, hour,
   speed), `who` (connected players and character IDs), `speed <1|2|4|8|16>` (admin),
   `alwayson <on|off>` (admin).
3. **Permissions.** Two levels: player and admin. Admins are character IDs listed one per line
   in a host-edited file next to the save (`<save>.admins`), re-read when it changes. No file
   or an empty file means nobody is admin. No save-schema change.
4. **Speed.** The server runs `Step()` N times per physics frame; snapshots keep their rate.
   If the coarse population step falls behind, the server drops to the highest speed it holds
   and says so. Speed is world-wide.
5. **Always-on.** A server setting (startup flag and the `alwayson` command) keeps world time
   running with nobody connected. Default stays paused.

## Acceptance

- Domain checks: a non-admin's `speed`/`alwayson` is refused; an admin's works; no admins file
  means no admins; N steps at N x equal N separate steps (same state fingerprint).
- ENet test: console request and reply round trip; speed refused without the admins file and
  applied with it; always-on advances time with zero players.
- Measured: highest speed that holds 20 ticks/s with 1M agents, here and on whitetop.
- Xvfb screen test: the grave key opens the console.
- test_all --quick green on Linux and whitetop.

## Not in this task

More commands (skip, goto, forcing events) until asked for; per-command permission tiers;
NPC navigation (next decision); a headless server stdin console.
