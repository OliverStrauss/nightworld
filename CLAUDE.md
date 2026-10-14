# CLAUDE.md — rules for every agent working in this repo

This repo is a 3D open-world RPG in Unity, built mostly by agents one task card per night.
A human (Oliver) reviews every PR. Your job is to leave him a small, correct, easy-to-review
change — not a big impressive one.

Read `docs/design.md` and `docs/architecture.md` before your first change in a session,
and `docs/content-yaml.md` before adding or editing any content asset.

## Golden rules

1. **One card, one PR.** Do exactly what the card asks. If you notice other problems, list them
   in the PR under "Follow-ups" instead of fixing them.
2. **Content is data.** New items, enemies, shops and zones are ScriptableObject assets in
   `game/Assets/_Project/Data/`. You can't run Unity, so write the `.asset` YAML and its
   `.meta` directly — follow `docs/content-yaml.md` exactly. Do not hard-code content in C#.
3. **Never hand-edit scene files** (`*.unity`) or prefab YAML for layout. Zones are assembled
   at runtime by `ZoneBuilder` from a `ZoneDefinition`. If a card seems to require a scene
   edit, stop and say so in the PR.
4. **Never edit anything under `game/Assets/ThirdParty/`.** Use those assets; don't change them.
5. **Every new file or folder under `game/Assets` needs a `.meta`** with a fresh GUID from
   `python3 tools/new_guid.py` (copy the format of an existing meta of the same kind).
   Never change an existing GUID.
6. **Every behaviour change gets a test.** Logic → EditMode test. Gameplay loop → PlayMode test.
7. **Run `python3 tools/check_assets.py` before every commit** and fix everything it reports.
   It can't check gameplay rules, so also check them yourself: unique IDs, every enemy has a
   loot table, every zone has a `ScreenshotPoint`, positions inside the ground.
8. **Don't touch** `.github/`, `runner/`, `CLAUDE.md`, or `.claude/` unless the card is
   explicitly about them.
9. **No secrets in the repo.** Ever.

## Where things go

| Thing | Location |
|---|---|
| Gameplay systems | `game/Assets/_Project/Scripts/Runtime/<System>/` |
| ScriptableObject definitions | `Scripts/Runtime/Data/` |
| Content assets | `Data/Items`, `Data/Enemies`, `Data/Zones`, `Data/Shops` |
| Editor tooling | `Scripts/Editor/` |
| Tests | `Tests/EditMode/`, `Tests/PlayMode/` |

## Conventions

- C# namespace `NightWorld.<System>` (e.g. `NightWorld.Combat`). Editor code: `NightWorld.EditorTools` (not `NightWorld.Editor`, which clashes with Unity's `Editor` class).
- IDs are lowercase kebab-case strings (`iron-sword`, `bog-slime`, `harbor-town`) and are the
  stable key for save files and references-by-name. Never rename an ID; add a new one.
- Numbers that designers tune (damage, XP, prices) live in data, not in code.
- Units: 1 Unity unit = 1 metre. Player is ~1.8 tall.

## Pull request format

Title: `[card-<id>] <card title>`

Body:
```
## What changed
## How to check it (what Oliver should do in the editor)
## Acceptance criteria
- [x] each criterion from the card, checked only if you verified it
## Follow-ups
## Notes for the reviewer
```

## When you're stuck

Don't guess for an hour. Commit what works, open the PR as a draft, and explain exactly what
blocked you. A clear "I couldn't do X because Y" is a good night's work.
