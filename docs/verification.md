# Verification

The agent that builds a change never approves it. Every PR goes through these layers,
cheapest first. A failure at any layer stops the rest.

| # | Layer | Where | Catches |
|---|---|---|---|
| 1 | **Compile** | CI | Code that doesn't build |
| 2 | **Content validator** | CI (`ContentValidator`) | Duplicate IDs, missing references, enemies with no loot, zones with no spawn or screenshot points, missing prefabs |
| 3 | **EditMode tests** | CI | Broken logic: XP curve, damage maths, shop pricing, inventory |
| 4 | **PlayMode tests** | CI | Broken gameplay: the bot spawns, walks into a zone, kills an enemy, gains XP and gold |
| 5 | **Zone screenshots** | CI (`ZoneScreenshotter`) | Renders each zone from its `screenshotPoints` and uploads PNGs as an artifact |
| 6 | **Reviewer agent** | Pi, after CI | Reads card + diff + CI results + screenshots. Looks for: scope creep, criteria not actually met, black or empty renders, pink (missing) materials, objects floating or buried, scene/ThirdParty edits |
| 7 | **Oliver** | Mac, morning | Does it feel good? Is it fun? |

## Reviewer verdicts

- **PASS** — every acceptance criterion is met and evidenced. Card → `review` (Oliver's column).
- **FAIL** — something is missing or broken. Reviewer writes concrete fix notes; card goes back
  to `ready` with the notes attached, and tomorrow's builder starts from the same branch.
- **BLOCKED** — the card can't be done as written (needs a scene edit, needs an asset we don't
  have, contradicts the design doc). Card → `blocked` for Oliver.

After two FAILs in a row on the same card, it goes to `blocked` instead, so a bad card can't
burn a week of nights.

## Screenshot notes

CI runners have no GPU, so Unity renders in software. Images are rougher than on a Mac
(no fancy post-processing, possibly lower resolution) but are fine for spotting missing,
broken or misplaced things. Don't judge lighting quality from them.
