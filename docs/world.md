# The world (canon)

> Oliver owns this file. Every agent reads it before planning, building or reviewing anything
> that adds to the world: zones, enemies, items, shops, NPCs, quests, minigames, vehicles.
> Agents never change it on their own. If your work needs a new fact here, propose it under
> **Canon proposals** in your PR or card; Oliver adds it if he likes it.
>
> `docs/design.md` says how the game *plays*. This file says what the world *is*: where things
> are, how hard they are, what they're called and how they feel. Lines marked **(decide)** are
> open questions; don't invent answers to them, leave them open and mention them in your PR.

## The shape of the world

- One seamless world, no portals. Each zone has a place on the world map and a biome; the map
  is generated from zone data, never drawn by hand.
- **Harbor Town is the centre of safety.** It sits on the coast. Everything gets harder the
  further you walk from it, so a player can read danger from distance and colour alone.
- Every zone touches at least one other zone. No islands you can't walk to.
- **(decide)** Which side is the sea? Default until decided: the sea is south of Harbor Town,
  and the wilderness opens up to the north, east and west.

## Danger rings

Difficulty is set by distance from Harbor Town. Each ring has a tier, a level band, a danger
colour and the biomes that fit it. New zones pick a ring first and take its numbers.

| Tier | Ring | Player levels | Danger colour | Biomes that fit |
|---|---|---|---|---|
| 0 | Town | — | none (safe) | coast, town |
| 1 | Next to town | 1–3 | green / yellow | meadow, beach, light forest |
| 2 | Near | 4–7 **(decide)** | yellow / orange | forest, hills, swamp edge |
| 3 | Middle | 8–12 **(decide)** | orange | swamp, desert, canyon |
| 4 | Far | 13–17 **(decide)** | purple | snow, deep forest, ruins |
| 5 | Edge | 18–20 **(decide)** | red | volcano, the very weird stuff |

Rules of thumb that follow from the table:
- An enemy's level fits the band of every zone it spawns in.
- Gear you can buy in a town is aimed at the ring just outside it.
- A reward off the path (chest, NPC, viewpoint) can be one tier better than the ring it's in.

## What exists today

Keep this list in sync with `game/Assets/_Project/Data/` when a PR adds or removes canon.

| ID | Kind | Where | Notes |
|---|---|---|---|
| `harbor-town` | Zone, town, tier 0 | Centre | Starting town |
| `meadow-fields` | Zone, enemy, tier 1, levels 1–3 | Next to Harbor Town **(decide: which side)** | Open grass, bushes, rocks, slimes |
| `harbor-outfitter` | Shop | `harbor-town` | First weapons and armour |
| `meadow-slime` | Enemy, tier 1 | `meadow-fields` | Slow, harmless-ish, drops `slime-gel` |
| `wooden-sword` | Weapon, Common | `harbor-outfitter` | Starter weapon |
| `leather-tunic` | Armor, Common | `harbor-outfitter` | Starter armour |
| `minor-potion` | Consumable, Common | `harbor-outfitter` | Small heal |
| `slime-gel` | Loot | Drops from `meadow-slime` | Sell fodder |

## Tone and naming

The vibe from `CLAUDE.md`, made concrete so every agent names things the same way.

- **IDs**: kebab-case, `<place-or-trait>-<thing>`: `meadow-slime`, `harbor-outfitter`,
  `grumpy-mushroom`. Never rename one.
- **Display names**: plain and readable first, a little silly second. "Meadow Slime",
  "Suspiciously Damp Boots". One joke per name at most.
- **Enemies** are goofy creatures and grumpy plants, not people. Nothing gory, nothing scary
  enough to make a kid stop playing.
- **Places** are named after what you see there: Meadow Fields, Harbor Town. A player should
  guess the biome from the name.
- **No** real-world brands, memes, pop-culture references or edgy jokes.

## Cast and factions

**(decide)** No NPCs with names or factions yet. Until Oliver adds some, an agent that needs an
NPC proposes one here rather than inventing a backstory in code or data.

## Fitting new work into the world

Every epic and every card that adds to the world answers these in a **World fit** line:

1. **Where?** Which zone, or which ring and which side of town for a new zone.
2. **Which tier and levels?** Taken from the danger rings table.
3. **What does it reuse?** Existing IDs it builds on (`harbor-town`, `meadow-slime`…).
4. **What does it add to canon?** New IDs and names, and any new fact this file would need.

If an epic can't answer 1 and 2, it isn't ready to split into cards.

## Canon proposals

Agents: don't edit this file. Put proposals in your PR body or card notes under a
"Canon proposals" heading, for example "Add `bramble-woods` to the tier 2 ring, west of
`meadow-fields`." Oliver copies the ones he wants into the sections above.
