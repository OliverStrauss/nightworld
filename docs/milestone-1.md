# Milestone 1: the core loop

Goal: walk out of Harbor Town into Meadow Fields, kill slimes, level up, come back and buy a
better sword. Starter cards to put on the board, roughly in order. Write each one up with
acceptance criteria using `docs/task-cards.md` before moving it to Ready.

Already in the repo: data definitions, `ZoneBuilder`, enemy spawning, health, XP/gold rewards,
the validator, the screenshotter, and the two starter zones.

| # | Card | Type | Size |
|---|---|---|---|
| 1 | Player controller: capsule placeholder, WASD movement on terrain, spawns at `PlayerSpawn` | system | M |
| 2 | Third-person follow camera with mouse orbit | system | S |
| 3 | Melee attack: button press, short-range hitbox, cooldown, damages `Health` | system | M |
| 4 | Enemy AI: idle wander inside spawn radius, chase and attack the player in range | system | M |
| 5 | Player health + death: respawn at town, lose 10% gold | system | S |
| 6 | HUD: HP bar, level, XP bar, gold | system | M |
| 7 | Zone travel: portal props that load another zone by id | system | M |
| 8 | Inventory: hold items, equip weapon/armor, stats from equipment | system | M |
| 9 | Shop UI: buy/sell from `ShopDefinition` when inside `ShopTrigger` | system | M |
| 10 | Loot drops: roll `lootTable` on enemy death, pick up by walking over | system | S |
| 11 | PlayMode bot test: spawn player, walk to slimes, kill one, assert XP gained | system | M |

After that, most cards can be content: new enemies, zones, items and shops.
