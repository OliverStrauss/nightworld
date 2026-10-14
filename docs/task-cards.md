# Writing task cards

A card is good if a fresh agent with no memory of yesterday could finish it in one night and a
reviewer could tell, from evidence alone, whether it's done.

## Template

```
Title:    Add bog slimes to meadow-fields
Type:     content | system | fix | chore
Size:     S | M          (L is not allowed — split it)
Depends:  card-12        (optional)

Goal
  One or two sentences on what the player will notice.

Acceptance criteria
  - [ ] A new EnemyDefinition `bog-slime` exists with HP 30, damage 4, XP 12, gold 3–6
  - [ ] meadow-fields spawns 4 bog slimes near (20, 0, 35)
  - [ ] A PlayMode test kills a bog slime and checks XP went up by 12
  - [ ] Zone screenshot `meadow-fields/overview` shows the slimes

Notes
  Use the Kenney "slime" model if present in ThirdParty, otherwise a green capsule.
```

## Rules of thumb

- **Concrete beats vague.** "Add a dock with three crates you can break" ✓ —
  "make the harbor nicer" ✗.
- **Every criterion is checkable** by a test, the validator, or a screenshot.
- **Name the IDs** you want so content stays consistent.
- **Systems before content.** Don't ask for a shop UI card before the inventory card is merged.
- **S** ≈ one definition or a small fix. **M** ≈ one new system or a zone with a few parts.

## Statuses on the board

`backlog → ready → building → verifying → review → done`, plus `blocked`.
The nightly builder only ever picks from the top of `ready`.
