# Task board: build plan

How to build the board described in [README.md](README.md), one agent session per phase.
README.md is the API contract; this file is the build order, the stack, and the decisions
behind them. Each phase below is a self-contained task: hand an agent the phase heading and
its criteria, it builds that phase on its own branch, and opens one PR.

## Decisions already made (2026-10-14)

- **Same repo.** The app lives in `board/`, next to the game. It never touches `game/`.
- **Game cards only.** The board holds NightWorld work, not personal todos, so the nightly
  builder can never pick up something that isn't a game task.
- **Points per card.** Each card has `points` (it replaced `size` and `effort` on 2026-10-14);
  the runner maps it to Claude Code `--effort`, max turns and a time limit.
- **Several cards per night.** A night budget (points, card cap, stop time) decides how many
  cards the runner builds, one after another.
- **Epics.** A big feature is an epic with one branch `epic/<slug>` and a draft PR into `main`.
  Each card in it gets its own branch and a PR into the epic branch. When the reviewer agent
  passes a card PR, the runner merges it into the epic branch. Nothing auto-merges into `main`.
- **Seamless world map** comes later (phase 6), drawn from a `world.json` that CI exports.

## Stack

| Piece | Choice | Why |
|---|---|---|
| Language | Python 3.11+ | Already on Raspberry Pi OS; easy to read |
| Web framework | FastAPI + Uvicorn | Small, typed, built-in test client |
| Storage | SQLite via the standard `sqlite3` module | One file, no server, easy backup |
| Templates | Jinja2, server-rendered | No JavaScript build step |
| Interactivity | Plain HTML forms + SortableJS (vendored into `board/app/static/`) and a few lines of `fetch` | Drag to reorder without a frontend framework; works offline on the Pi. htmx turned out not to be needed |
| Tests | pytest + FastAPI `TestClient` | Fast; no network or browser needed |
| Run | systemd service, bound to `127.0.0.1:8787` | Restarts on crash and reboot |
| Access from phone | Tailscale (`tailscale serve`) | Never on the public internet |

Keep dependencies to that list. No ORM, no Node toolchain.

## Layout

```
board/
  README.md            API contract (source of truth for endpoints and rules)
  PLAN.md              this file
  pyproject.toml       deps + pytest config
  app/
    main.py            FastAPI app, mounts api + ui routers
    db.py              connection, schema creation, migrations (schema_version table)
    auth.py            bearer tokens, login cookie, actor resolution
    cards.py           card rules: transitions, next-card selection, review rules
    api.py             JSON endpoints under /api
    ui.py              HTML pages
    templates/         Jinja2 templates
    static/            CSS, Sortable.min.js, manifest, icons
  tests/
    test_*.py
  deploy/
    nightworld-board.service   systemd unit
    board.env.example          tokens, db path, password hash
    backup.sh                  nightly sqlite3 .backup
```

Config comes from `~/.config/nightworld/board.env` on the Pi (outside the repo, like the
runner's config). No tokens or passwords in the repo, ever.

## Data model

Contract fields are in README.md. Tables:

- `cards`: every field in README.md's Card.
- `card_deps`: (`cardId`, `dependsOn`).
- `epics`: `id`, `slug`, `title`, `pitch`, `status` (`open | merged | dropped`), `branch`
  (`epic/<slug>`), `prUrl`, timestamps.
- `comments`: `id`, `cardId`, `author`, `body`, `createdAt`.
- `activity`: append-only. `id`, `cardId` (nullable), `actor`, `type`, `message`, `data`
  (JSON of what changed), `createdAt`. Every write goes through one helper that also logs here.
- `settings`: key/value for night settings.
- `schema_version`: single row, for migrations.

## Phases

Each phase ends with its tests passing (`cd board && pytest`) and a PR. Do them in order.

### Phase 1: cards API (M)
Goal: the runner can ask for work and nothing else is needed yet.
- [x] `pyproject.toml`, app skeleton, `db.py` creates all tables on start
- [x] Bearer auth with one token per actor (`oliver`, `builder`, `reviewer`, `groomer`), read from env; unknown token → 401
- [x] `GET /api/cards`, `GET /api/cards/:id`, `POST /api/cards`, `PATCH /api/cards/:id`, with the per-actor limits in README.md (→ 403)
- [x] `GET /api/cards/next` returns 204 on an empty board, and skips cards whose dependencies aren't met
- [x] Every write adds an `activity` row with the actor
- [x] Tests for each endpoint, each actor limit, and `next` with and without dependencies

### Phase 2: night shift endpoints (M)
Goal: `runner/nightly.sh` can run a full card against the board.
- [x] `POST /claim`, `POST /review` (PASS / FAIL / BLOCKED rules, two FAILs → blocked), `POST /events`, `POST /comments`, `GET /api/activity`
- [x] `effort` on cards, defaulting from size (S → low, M → medium)
- [x] `GET/PUT /api/settings/night`
- [x] Tests for every review rule, including FAIL putting the card back at position 1 of `ready`
- [x] A dry run: start the app locally, create a card with curl, and run the board calls `nightly.sh` makes (claim, PATCH to verifying, review). Paste the output in the PR.

### Phase 3: phone UI, read-only (M)
Goal: Oliver can see the board on his phone.
- [x] Login page for `oliver` (password hash from env), session cookie
- [x] Kanban page: one column per status, scrolls sideways on a phone
- [x] Card page: goal, criteria, effort, PR link, latest review notes, comments, activity history
- [x] Web app manifest + icon so it can be added to the home screen
- [x] Tests: pages return 200 when logged in and redirect to login when not

### Phase 4: phone UI, editing (M)
Goal: Oliver can run the board from his phone without curl.
- [x] Create and edit cards using the `docs/task-cards.md` template (title, type, size, effort, goal, criteria list, notes, depends-on)
- [x] Drag to reorder `ready` (SortableJS); top card is built next
- [x] Move cards between columns, with the same rules as the API (only Oliver moves to `done` or out of `blocked`)
- [x] Night settings page: points per night, max cards, stop time, pause
- [x] Tests for each form handler

### Phase 5: epics (M)
Goal: big features can be split into cards that build on each other.
- [x] Epics table, API (`GET/POST/PATCH /api/epics`), `epicId` on cards
- [x] Epic page: its cards in order, a progress bar, branch and PR links
- [x] `/cards/next` treats a dependency as met when it's `done`, **or** in the same epic and its PR has merged into the epic branch (the reviewer's PASS + merge sets `mergedToEpic: true`)
- [x] Tests for epic dependency rules

### Phase 6: world map page (M, after the game exports `world.json`)
Goal: Oliver sees the whole world and taps a zone to see what it holds.
- [ ] Reads the latest `world.json` (downloaded from CI on `main`)
- [ ] One top-down map, zones coloured by biome; tap a zone for its level range, enemies and drops, shops and prices, points of interest, screenshot, and open cards for that zone
- [ ] Shows a friendly "no world data yet" page until `world.json` exists

### Deploy (S, Oliver on the Pi)
- [ ] `deploy/nightworld-board.service`, `board.env.example`, `backup.sh`, and a "Run it on the Pi" section in README.md
- [ ] `tailscale serve` instructions for reaching it from the phone

## Runner changes (separate cards, after phases 2 and 5)

These touch `runner/`, so each needs a card that explicitly says so (see CLAUDE.md rule 8).

1. **Loop until the night budget is spent** (done, #14): read `/api/settings/night`, keep taking
   `next?points=<left>` cards until points, card cap or stop time runs out. Still to do: map
   `points` to `--effort`, `--max-turns` and timeout.
2. **Epic branches**: build a card from its epic branch, open the PR against it, merge
   `main` into the epic branch first, and on PASS merge the card PR into the epic branch.
   Update `CLAUDE.md` and `runner/README.md`, which today say nothing merges automatically.

## CI for the board

`unity-ci.yml` only runs on `game/` changes. Board PRs need their own small workflow
(`board-ci.yml`: Python setup + `pytest`). That's a `.github/` change, so it gets its own card.
Until then, the agent runs `pytest` itself and pastes the result in the PR.

## Out of scope

Personal todos, multiple users beyond Oliver and the agents, public internet access, a
JavaScript framework, Postgres.
