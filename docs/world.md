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
- **Harbor Town is the safe start.** It sits on the coast. The world is a patchwork of
  regions, not circles around the town: a hard region can sit close to town and an easy one
  far away, as long as the player can read the danger from colour before walking in.
- Every zone touches at least one other zone. No islands you can't walk to.
- The sea is south of Harbor Town, and the wilderness opens up to the north, east and west.

## Danger tiers

Every zone has a danger tier. The tier sets its level band and danger colour; it is not tied to
how far the zone is from Harbor Town. New zones pick a tier and take its numbers.

| Tier | Player levels | Danger colour | Biomes that fit |
|---|---|---|---|
| 0 | — | none (safe) | coast, town |
| 1 | 1–3 | green / yellow | meadow, beach, light forest |
| 2 | 4–7 **(decide)** | yellow / orange | forest, hills, swamp edge |
| 3 | 8–12 **(decide)** | orange | swamp, desert, canyon |
| 4 | 13–17 **(decide)** | purple | snow, deep forest, ruins |
| 5 | 18–20 **(decide)** | red | volcano, the very weird stuff |

How tiers sit on the map:
- **Tiers are placed freely.** Regions can be any shape, wind along a valley, or form a dead
  end. Directions don't have to be fair: one road out of town can get hard fast while another
  stays gentle for a long way.
- **No cliff edges.** Zones that touch differ by at most one tier, unless the border is made
  readable on purpose (a sign, a colour change, a scary-looking gate) so walking in is a choice.
- **Always a way up.** From Harbor Town there is a walkable route that climbs one tier at a time
  to every zone.

Rules of thumb that follow from the table:
- An enemy's level fits the band of every zone it spawns in.
- Gear you can buy in a town is aimed at the tiers of the zones next to it.
- A reward off the path (chest, NPC, viewpoint) can be one tier better than the zone it's in.

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

1. **Where?** Which zone, or which existing zones a new zone borders.
2. **Which tier and levels?** Taken from the danger tiers table, within one tier of its neighbours.
3. **What does it reuse?** Existing IDs it builds on (`harbor-town`, `meadow-slime`…).
4. **What does it add to canon?** New IDs and names, and any new fact this file would need.

If an epic can't answer 1 and 2, it isn't ready to split into cards.

## Canon proposals

Agents: don't edit this file. Put proposals in your PR body or card notes under a
"Canon proposals" heading, for example "Add `bramble-woods`, tier 2, bordering
`meadow-fields` on its west side." Oliver copies the ones he wants into the sections above.
