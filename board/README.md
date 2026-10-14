# Task board

A small web app on the Pi: a kanban board you use from your phone, plus a JSON API that the
nightly runner and the agents use. **Not built yet** — this file is the contract it has to meet,
so the runner and agents can be written against it now.

## Requirements

- Mobile-first kanban: columns `backlog`, `ready`, `building`, `verifying`, `review`, `done`,
  `blocked`. Drag to reorder `ready` (top card is built next).
- Card editor following `docs/task-cards.md` (title, type, size, goal, acceptance criteria list,
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
  "size": "S",                  // S | M
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
| GET | `/api/cards/next` | builder | Top `ready` card whose dependencies are `done`. **204** if none |
| GET | `/api/cards/:id` | all | One card |
| POST | `/api/cards` | oliver, groomer* | Create (groomer may only create in `backlog`) |
| PATCH | `/api/cards/:id` | oliver, builder* | Edit fields / status / position (builder may only set `status: verifying` and `prUrl`) |
| POST | `/api/cards/:id/claim` | builder | `{branch}` → status `building`, `attempts += 1`, returns the card |
| POST | `/api/cards/:id/review` | reviewer | `{verdict, notes, prUrl}` → applies the rules below |
| POST | `/api/cards/:id/events` | all | `{type, message}` → activity entry only |
| POST | `/api/cards/:id/comments` | all | `{body}` |
| GET | `/api/activity?since=<iso>&card=<id>` | all | Audit log |

## Review rules (`POST /review`)

| Verdict | Result |
|---|---|
| `PASS` | status → `review`, `consecutiveFails = 0` |
| `FAIL` | `consecutiveFails += 1`; if it's now 2 → `blocked`, else → `ready` at **position 1** (retried tomorrow on the same branch) |
| `BLOCKED` | status → `blocked` |

`notes` is stored in `lastReview` and the builder sees it on the next attempt.

Only `oliver` can move a card to `done` (after merging) or out of `blocked`.
