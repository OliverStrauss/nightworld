# NightWorld

A 3D open-world RPG that gets built one task at a time, every night, by Claude.

You plan the game on a task board (web app on the Raspberry Pi, usable from your phone).
Each night the Pi picks the top **Ready** card, a *builder* agent implements it on a branch and
opens a PR, GitHub Actions compiles and tests it, and a separate *reviewer* agent checks the
result against the card before you ever look at it. In the morning you playtest and merge.

> "NightWorld" is a placeholder name. Rename it before your first real commit if you want to:
> search-and-replace `NightWorld` (C# namespaces, asmdefs) and `nightworld` (paths, scripts).

## Repo layout

```
nightworld/
├── CLAUDE.md                 Rules every agent follows (read this first)
├── docs/
│   ├── design.md             The game: pillars, core loop, leveling, zones, art style
│   ├── architecture.md       How the code is organized and why zones are data
│   ├── verification.md       The checks a change must pass, cheapest first
│   ├── task-cards.md         How to write a card the nightly agent can finish
│   ├── content-yaml.md       How agents write content assets without opening Unity
│   └── milestone-1.md        First cards to put on the board
├── game/                     Unity project (Unity 6 LTS, URP)
│   └── Assets/
│       ├── _Project/         Everything we own
│       │   ├── Scripts/Runtime/   Gameplay systems (NightWorld.Runtime.asmdef)
│       │   ├── Scripts/Editor/    Validators + zone screenshotter (editor only)
│       │   ├── Data/              Content: Items, Enemies, Zones, Shops + starter content
│       │   ├── Prefabs/
│       │   ├── Scenes/            Boot scene only; zones are built at runtime from data
│       │   └── Tests/             EditMode + PlayMode tests
│       └── ThirdParty/       Asset packs (Kenney, Synty, ...). Never edited by agents.
├── board/                    Task board web app + API (runs on the Pi)
├── runner/                   Nightly job for the Pi: nightly.sh, crontab, setup
├── tools/                    check_assets.py (meta/GUID/reference checks), new_guid.py
├── .claude/agents/           builder, reviewer, groomer agent definitions
└── .github/workflows/        GameCI: compile, validate, test, screenshot every PR
```

## One-time setup

### 1. Create the Unity project inside `game/`
Unity Hub won't create a project in a folder that already has files, so:

1. In Unity Hub, create a new project with the **Universal 3D** template, Unity **6000.3 LTS**
   (or newer LTS), named `game-tmp`, saved next to this repo.
2. Move these from `game-tmp/` into `game/`:
   - `ProjectSettings/` and `Packages/`
   - `Assets/Settings/` **and** `Assets/Settings.meta` (the URP render pipeline assets;
     without them the project renders pink)
3. Delete `game-tmp`, then in Unity Hub use **Add → Add project from disk** and pick `game/`.
4. Open it once and let Unity import. Check Window → Package Manager has **Test Framework**
   (the template includes it).
5. Run **NightWorld → Setup → Create Boot Scene**, then press Play: you should see Harbor Town
   built from data. Also try **NightWorld → Capture Zone Screenshots** and
   **Window → General → Test Runner → Run All**.
6. Run `python3 tools/check_assets.py`, then commit `ProjectSettings/`, `Packages/`,
   `Assets/Settings*`, the Boot scene, and any `.meta` files Unity generated.

### 2. Git LFS (3D assets are big)
```bash
brew install git-lfs
git lfs install
```
`.gitattributes` already routes models, textures and audio through LFS.

### 3. GitHub secrets for CI
GameCI needs a Unity license. In the repo's Settings → Secrets → Actions add
`UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`. Follow
https://game.ci/docs/github/activation for a Personal license.

### 4. The Pi
See [`runner/README.md`](runner/README.md) for installing Claude Code, `gh`, and the cron job,
and [`board/README.md`](board/README.md) for the task board.

## The nightly loop

1. **You** keep the board's **Ready** column ordered. One card = one night of work.
2. **02:13** — cron on the Pi runs `runner/nightly.sh`.
3. **Builder** (Claude Code, headless) takes the top Ready card, works on `night/<card-id>`,
   opens a PR, moves the card to **Building → Verifying**.
4. **CI** checks metas and scope, compiles, runs the content validator and EditMode + PlayMode
   tests, and screenshots every zone.
5. **Reviewer** (a fresh Claude session that didn't write the code) reads the diff, CI results
   and screenshots against the card's acceptance criteria and posts PASS / FAIL on the PR and
   the board. FAIL sends the card back to Ready with notes for the next night.
6. **You**, in the morning: pull the branch, playtest, merge or comment.
