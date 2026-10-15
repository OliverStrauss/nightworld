# Task board

A small web app on the Pi: a kanban board you use from your phone, plus a JSON API that the
nightly runner and the agents use. This file is the contract it meets. The build order, stack
and design decisions are in [PLAN.md](PLAN.md).

## Run it on your computer

```bash
cd board
python3 -m venv .venv && .venv/bin/pip install -e '.[test]'
.venv/bin/pytest                                   # all green?

.venv/bin/python -m app.auth                       # type a password, copy the hash it prints
export BOARD_PASSWORD_HASH='pbkdf2_sha256$…'       # single quotes: the hash contains $
export BOARD_SECRET="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export BOARD_TOKEN_OLIVER=… BOARD_TOKEN_BUILDER=… BOARD_TOKEN_REVIEWER=… BOARD_TOKEN_GROOMER=…
export BOARD_DB=~/nightworld-board.db              # default: board.db in the current folder
.venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8787
```

Open <http://127.0.0.1:8787> and log in. To try it on your phone, either run uvicorn with
`--host 0.0.0.0` and open `http://<your-computer's-LAN-IP>:8787` on the same Wi-Fi, or use
`tailscale serve 8787`. Then "Add to Home Screen". Running it permanently on the Pi is the
"Deploy" step in PLAN.md.

The API docs (every endpoint, try-it-out) are at <http://127.0.0.1:8787/docs>.

## Requirements

- Mobile-first kanban: columns `backlog`, `ready`, `building`, `verifying`, `review`, `done`,
  `blocked`. Drag to reorder `ready` (top card is built next).
- Card editor following `docs/task-cards.md` (title, type, points, goal, acceptance criteria list,
  notes, depends-on).
- Card detail shows the PR link, the reviewer's latest notes, comments, and the full activity
  history.
- Installable to the home screen (web app manifest).
- SQLite, single process, listens on `localhost:8787`. Reached from your phone over Tailscale;
  never exposed to the public internet.
- Every write is recorded in an append-only `activity` table with the actor, so agents and
  you can audit everything that happened to a card.

## Auth

`Authorization: Bearer <token>`. Each actor has its own token: `oliver`, `builder`, `reviewer`,
`groomer`. The token decides the actor recorded in the activity log. The web UI uses a login
cookie for `oliver`.

## Card

```json
{
  "id": 42,
  "title": "Add bog slimes to meadow-fields",
  "type": "content",            // content | system | fix | chore
  "points": 2,                  // how big the card is, a whole number ≥ 1 (default 1)
  "epicId": 3,                  // optional; card is built on the epic's branch
  "mergedToEpic": false,        // true once its PR is merged into the epic branch
  "status": "ready",
  "position": 1,                // order within its column
  "goal": "…",
  "criteria": ["…", "…"],
  "notes": "…",
  "dependsOn": [41],
  "branch": "night/card-42",
  "prUrl": "https://github.com/…/pull/17",
  "attempts": 1,
  "consecutiveFails": 0,
  "lastReview": "VERDICT: FAIL\n…",
  "createdAt": "…", "updatedAt": "…"
}
```

## Endpoints

| Method | Path | Who | What |
|---|---|---|---|
| GET | `/api/cards?status=ready` | all | List cards, ordered by status then position |
| GET | `/api/cards/next?points=<left>` | builder | Top `ready` card whose dependencies are met and whose `points` fit in `left` (default: the whole night's `pointsPerNight`). **204** if none. A dependency is met when it's `done`, or when it's in the same epic and `mergedToEpic` is true |
| GET | `/api/cards/:id` | all | One card |
| POST | `/api/cards` | oliver, groomer* | Create (groomer may only create in `backlog`) |
| PATCH | `/api/cards/:id` | oliver, builder* | Edit fields / status / position (builder may only set `status: verifying`, `prUrl` and `mergedToEpic`) |
| DELETE | `/api/cards/:id` | oliver | **204**. Removes the card, its comments and any dependency on it; the activity log keeps its history |
| POST | `/api/cards/:id/claim` | builder | `{branch}` → status `building`, `attempts += 1`, returns the card |
| POST | `/api/cards/:id/review` | reviewer | `{verdict, notes, prUrl}` → applies the rules below |
| POST | `/api/cards/:id/events` | all | `{type, message}` → activity entry only |
| POST | `/api/cards/:id/comments` | all | `{body}` |
| GET | `/api/activity?since=<iso>&card=<id>` | all | Audit log |
| GET | `/api/epics`, `/api/epics/:id` | all | Epics with their cards in order |
| POST / PATCH | `/api/epics`, `/api/epics/:id` | oliver, groomer* | Create / edit an epic (groomer may only create) |
| POST | `/api/epics/:id/split` | oliver | **202**. Runs the groomer agent (`claude -p --agent groomer`, read-only) to draft the epic's cards into `backlog` as `groomer` |
| GET | `/api/epics/:id/split` | all | `{state: never | running | done | failed, message}` |
| GET | `/api/settings/night` | all | `{pointsPerNight, maxCards, stopTime, paused}` |
| PUT | `/api/settings/night` | oliver | Change night settings |

## Epic

```json
{
  "id": 3,
  "slug": "fishing",
  "title": "Fishing minigame",
  "pitch": "…",
  "status": "open",             // open | merged | dropped
  "branch": "epic/fishing",
  "prUrl": "https://github.com/…/pull/30",   // draft PR epic branch → main
  "cardIds": [21, 22, 23]
}
```

## Points and the night budget

Every card has `points`; Oliver sets `pointsPerNight`. A card is only built if its points fit in
what's left of the night, never rounding up: with 5 points a 2 + 3 night is fine, a 3 + 6 night
is not. `/api/cards/next?points=<left>` skips cards that don't fit, so a smaller card further down
can use up the rest. A card bigger than `pointsPerNight` is never picked (the board flags it).

The runner keeps taking `/api/cards/next?points=<left>` until the night's points or `maxCards` run
out, or `stopTime` passes. When `paused` is true it builds nothing.

## Review rules (`POST /review`)

| Verdict | Result |
|---|---|
| `PASS` | status → `review`, `consecutiveFails = 0`. For an epic card, the runner then merges its PR into the epic branch and sets `mergedToEpic: true` |
| `FAIL` | `consecutiveFails += 1`; if it's now 2 → `blocked`, else → `ready` at **position 1** (retried tomorrow on the same branch) |
| `BLOCKED` | status → `blocked` |

`notes` is stored in `lastReview` and the builder sees it on the next attempt.

Only `oliver` can move a card to `done` (after merging) or out of `blocked`.
