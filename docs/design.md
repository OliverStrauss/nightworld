# Game design

> Living document. Oliver owns it; agents read it but only change it when a card says so.
> Lines marked **(decide)** are open questions — fill them in as you go.

## Pitch
A small, stylized 3D open world you explore on foot. Towns are safe and have shops; the
wilderness is split into enemy zones that get harder the further you go. You fight, level up,
earn gold, buy better gear, and push into the next zone.

## Pillars
1. **Always a next step.** There's always a zone just slightly too hard and an item just
   slightly too expensive.
2. **Readable at a glance.** Low-poly, strong silhouettes, clear colour coding for danger.
3. **Grows every night.** The world is built from small pieces that can be added one at a time.

## Core loop
```
explore → fight → earn XP + gold → level up / buy gear → unlock harder zone → explore
```

## Camera and controls
- Third-person, camera follows behind the player. **(decide: free orbit or fixed angle?)**
- WASD / left stick to move, mouse / right stick for camera, one attack button to start.
- **(decide: real-time action combat or tab-target / auto-attack?)** Default assumption for
  early cards: simple real-time melee with a hitbox and a cooldown.

## Progression
- Level cap: 20 for the first version.
- XP to reach the next level: `xpToNext(L) = round(baseXp * L^exponent)` with
  `baseXp = 50`, `exponent = 1.6`. Tuned in `ProgressionSettings`, not in code.
- Each level grants + max HP and + base damage (values in `ProgressionSettings`).

## World structure
- One seamless world: zones sit side by side on a world map and you walk between them, no
  portals. Where zones go, their tiers and names live in `docs/world.md`.
- **Zones** are areas of the world, each with a biome. Each zone has a
  recommended level range and a danger tier (1 = starter).
- **Towns**: safe, have NPCs and shops, no enemy spawns.
- **Enemy zones**: spawn points with enemy tables, maybe a boss later.

First milestone zones:
| ID | Type | Levels | Notes |
|---|---|---|---|
| `harbor-town` | Town | — | Starting town, one weapon/armor shop |
| `meadow-fields` | Enemy | 1–3 | Slow slimes, open grass, a few trees |

## Shops and items
- Item types: Weapon, Armor, Consumable. Rarity: Common, Uncommon, Rare.
- Shops have a fixed inventory list for now; prices come from the item's `baseValue`.
- Selling gives 40% of `baseValue`.

## Art direction
- Low-poly, flat-shaded, saturated palette. Asset packs from Kenney (free, CC0) and
  optionally Synty.
- Danger read: starter zones are green/yellow, mid zones orange, dangerous zones purple/red.
- No realistic textures. If a shape needs explaining, it's too detailed.

## Out of scope (for now)
Multiplayer, crafting, dialogue trees, procedural quests, save-file migration.
