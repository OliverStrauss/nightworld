# CLAUDE.md — rules for every agent working in this repo

This repo is a 3D open-world RPG in Unity, built mostly by agents: one task card per night from
the Pi, plus tasks Oliver hands you directly. A human (Oliver) reviews every PR. Your job is to
leave him a small, correct, easy-to-review change — not a big impressive one.

Read `docs/design.md` and `docs/architecture.md` before your first change in a session,
and `docs/content-yaml.md` before adding or editing any content asset.

## The repo at a glance

| Path | What it is |
|---|---|
| `game/` | Unity 6 project (`6000.0.72f1`, URP). The only scene is `Boot`; zones are built at runtime from data |
| `game/Assets/_Project/` | Everything we own: scripts, data, prefabs, tests |
| `game/Assets/ThirdParty/` | Asset packs (Kenney etc.). Read-only for agents |
| `docs/design.md` | The game: pitch, pillars, core loop, progression, art direction |
| `docs/architecture.md` | Code layout, assemblies, why zones are data |
| `docs/content-yaml.md` | How to write `.asset` + `.meta` files by hand |
| `docs/task-cards.md` | Card template and sizing (S or M only) |
| `docs/verification.md` | The checks every PR goes through, cheapest first |
| `docs/milestone-1.md` | Current goal: the core loop (walk out of town, fight, level up, buy gear) |
| `tools/` | `check_assets.py` (metas, GUIDs, references), `new_guid.py` |
| `.github/workflows/unity-ci.yml` | CI: asset + scope checks → compile + EditMode/PlayMode tests → zone screenshots |
| `runner/` | `nightly.sh` on the Pi: board card → builder → CI → reviewer |
| `board/` | Task board API contract (not built yet) |
| `.claude/agents/` | `builder`, `reviewer`, `groomer` agent definitions |

## Game direction: the vibe

When Oliver gives you a task, build toward this feel. The vibe decides *how* you do the task,
never *how much*: fun ideas that don't fit the task go under "Follow-ups".

- **Fun beats realistic.** When two options both meet the task, pick the one that's more fun to
  play. Snappy movement, punchy hits, short walks between interesting things.
- **Exploring pays off.** It's an open world: let the player wander off the path, and reward
  them when they do — a chest behind the rocks, a strange NPC on a hilltop, a view worth the
  climb.
- **Don't take it too seriously.** Light, warm, a bit silly. Goofy enemies and playful names
  (`grumpy-mushroom`, "Suspiciously Damp Boots") are welcome. Never edgy, never meme spam.
- **Bright and readable.** Stay inside the art direction in `docs/design.md`: low-poly,
  saturated, danger shown by colour.

## Golden rules

1. **One task, one PR.** Do exactly what the card or issue asks. If you notice other problems,
   list them in the PR under "Follow-ups" instead of fixing them.
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
9. **No secrets in the repo.** Ever. The repo is public.

## Workflow: issue → branch → worktree → PR

Every task Oliver gives you in a session:

1. **Issue.** Unless Oliver gave you one, open it first with `gh issue create`: the goal and
   acceptance criteria in the `docs/task-cards.md` format.
2. **Branch + worktree** from a fresh `main`. Never work in the main checkout; Oliver may have
   uncommitted work there.
   ```bash
   git fetch origin
   git worktree add ../nightworld-<issue> -b <type>/<issue>-<slug> origin/main
   ```
   `<type>` is `feat`, `fix`, `content`, `chore` or `docs`. Untracked files (for example asset
   packs that aren't committed yet) don't exist in a worktree. If you need one, stop and ask
   Oliver to commit it.
3. **Commit** in small steps. Run `python3 tools/check_assets.py` before each commit.
4. **PR.** `git push -u origin <branch>`, then `gh pr create` in the format below. Wait for CI
   (`gh pr checks <pr> --watch`) and fix what fails.
5. **Screenshots** go in the PR (next section).
6. After Oliver merges: `git worktree remove ../nightworld-<issue>`.

The nightly runner is the exception: the board card is the issue, and the runner creates
`night/card-<id>` in its own throwaway clone. The reviewer agent checks CI's screenshots.

## Screenshots in every PR

A PR that changes anything visible gets a `## Screenshots` section with the images inline.
If nothing visible changed, write "No visual change".

**1. Take them.** `ZoneScreenshotter` renders every zone to `<zone-id>/<point>.png`.
- Locally (Oliver's Mac has Unity). Writes to `game/Screenshots/`. The first run in a new
  worktree imports the whole project, so expect several minutes. Never pass `-nographics`.
  ```bash
  /Applications/Unity/Hub/Editor/6000.0.72f1/Unity.app/Contents/MacOS/Unity -batchmode \
    -projectPath game -logFile - \
    -executeMethod NightWorld.EditorTools.ZoneScreenshotter.RunFromCommandLine
  ```
- Or from CI once it has finished: `gh run download <run-id> -n zone-screenshots -D <dir>`.
- For UI or anything zone screenshots can't show, say so and tell Oliver how to see it.

**2. Host them** on the `pr-screenshots` branch (orphan, images only, never merged):
```bash
S=$(mktemp -d)/shots
if git fetch -q origin pr-screenshots 2>/dev/null; then git worktree add -q --detach "$S" FETCH_HEAD
else git worktree add -q --orphan -b pr-screenshots "$S"; fi
mkdir -p "$S/pr-<pr>" && cp -R <screenshots-dir>/. "$S/pr-<pr>/"
git -C "$S" add -A && git -C "$S" commit -qm "Screenshots for #<pr>" && git -C "$S" push -q origin HEAD:pr-screenshots
git worktree remove "$S"
```

**3. Embed them** in the PR body (`gh pr edit <pr> --body-file <file>`):
```
![harbor-town/overview](https://raw.githubusercontent.com/OliverStrauss/nightworld/pr-screenshots/pr-<pr>/harbor-town/overview.png)
```

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

Title: `[#<issue>] <issue title>`, or `[card-<id>] <card title>` for nightly cards.

Body:
```
## What changed
## Screenshots
## How to check it (what Oliver should do in the editor)
## Acceptance criteria
- [x] each criterion from the issue or card, checked only if you verified it
## Follow-ups
## Notes for the reviewer

Closes #<issue>
```

## When you're stuck

Don't guess for an hour. Commit what works, open the PR as a draft, and explain exactly what
blocked you. A clear "I couldn't do X because Y" is a good night's work.
