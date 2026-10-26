# QuestSim

**A living multiplayer world, starting with cooperative zombie survival.**

QuestSim is an experimental game project built in Godot 4 (.NET/C#). The goal is a world that keeps
living whether or not you're watching: persistent humans and zombies with their own factions, rumors
that travel by word of mouth, modular base building, and stories that grow out of what actually
happened in the world rather than out of a fixed quest list.

> **Status: early experimental phase.** Everything here is a working hypothesis. Art is placeholder
> boxes on purpose; the focus is on getting the foundations right.

[Resume handoff](HANDOFF_2026-10-25.md) · [Current task](questsim_planning_kit/CURRENT_TASK.md) ·
[State](questsim_planning_kit/STATE.md) · [Runbook](questsim_planning_kit/RUNBOOK.md) ·
[Director decisions](questsim_planning_kit/DIRECTOR.md)

---

## The idea

Most open-world games freeze everything you aren't looking at. QuestSim tries the opposite:

- **Everyone persists.** Humans and zombies keep their identity, location and history across sessions.
- **The world doesn't revolve around the player.** NPCs and factions (including zombie hordes, which
  get their own factions and internal politics) pursue their own goals under the same rules as players.
- **Information spreads like it does in real life.** Events become facts; facts become rumors that
  travel between people who meet, fade over time, and depend on who trusts whom.
- **Stories come from the world.** A quest-giver's lost cart is a real thing in a real place, and
  what you find there depends on what else has happened nearby.
- **Co-op first.** Friends share one world. Items, inventories and story-created content are the
  same for everyone, so what one player does is what the other player finds.

### Inspiration

A major influence is OnceLost Games' devlog on *The Wayward Realms*: storing millions of NPCs as
numeric data processed on the GPU, spreading rumors by proximity, and building quests from a graph
of world facts that is filled in lazily when a player arrives. QuestSim is not a copy. We adopt
the ideas that fit a multiplayer survival game and test them with our own measurements
(see [video review](questsim_planning_kit/VIDEO_REVIEW.md)).

---

## How it works

### One authoritative server, many clients

```mermaid
flowchart LR
    A[Client: Alice] -- "intents: move, take, build" --> S
    B[Client: Bob] -- "intents" --> S
    S -- "snapshots + replies" --> A
    S -- "snapshots + replies" --> B
