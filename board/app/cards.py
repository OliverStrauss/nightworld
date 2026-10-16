"""Card rules: who may change what, positions within a column, and next-card selection.

Positions are 1-based and contiguous within each status column. Moving a card closes the gap
it leaves and shifts the cards below its new spot down by one.
"""

import json
import sqlite3

from fastapi import HTTPException

from .db import log_activity, now

STATUSES = ("backlog", "ready", "building", "verifying", "review", "done", "blocked")

# What the builder may change with PATCH (README.md: status → verifying, prUrl, mergedToEpic).
BUILDER_FIELDS = {"status", "prUrl", "mergedToEpic"}


def to_json(conn: sqlite3.Connection, row: sqlite3.Row) -> dict:
    card = dict(row)
    card["criteria"] = json.loads(card["criteria"])
    card["mergedToEpic"] = bool(card["mergedToEpic"])
    card["dependsOn"] = [
        r[0]
        for r in conn.execute(
            "SELECT dependsOn FROM card_deps WHERE cardId = ? ORDER BY dependsOn", (card["id"],)
        )
    ]
    return card


def get_row(conn: sqlite3.Connection, card_id: int) -> sqlite3.Row:
    row = conn.execute("SELECT * FROM cards WHERE id = ?", (card_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail=f"No card {card_id}")
    return row


def list_cards(conn: sqlite3.Connection, status: str | None = None) -> list[dict]:
    order = "CASE status " + " ".join(f"WHEN '{s}' THEN {i}" for i, s in enumerate(STATUSES)) + " END"
    if status:
        rows = conn.execute(
            f"SELECT * FROM cards WHERE status = ? ORDER BY {order}, position", (status,)
        )
    else:
        rows = conn.execute(f"SELECT * FROM cards ORDER BY {order}, position")
    return [to_json(conn, r) for r in rows.fetchall()]


def _check_deps(conn: sqlite3.Connection, card_id: int | None, deps: list[int]) -> None:
    for dep in deps:
        if dep == card_id:
            raise HTTPException(status_code=422, detail="A card can't depend on itself")
        if conn.execute("SELECT 1 FROM cards WHERE id = ?", (dep,)).fetchone() is None:
            raise HTTPException(status_code=422, detail=f"dependsOn: no card {dep}")
    if card_id is None:
        return
    # A cycle would mean none of its cards is ever picked by /next.
    seen, stack = set(), list(deps)
    while stack:
        current = stack.pop()
        if current == card_id:
            raise HTTPException(status_code=422, detail="dependsOn would create a cycle")
        if current in seen:
            continue
        seen.add(current)
        stack.extend(
            r[0] for r in conn.execute("SELECT dependsOn FROM card_deps WHERE cardId = ?", (current,))
        )


def _check_epic(conn: sqlite3.Connection, epic_id: int | None) -> None:
    if epic_id is not None and conn.execute("SELECT 1 FROM epics WHERE id = ?", (epic_id,)).fetchone() is None:
        raise HTTPException(status_code=422, detail=f"epicId: no epic {epic_id}")


def _set_deps(conn: sqlite3.Connection, card_id: int, deps: list[int]) -> None:
    conn.execute("DELETE FROM card_deps WHERE cardId = ?", (card_id,))
    conn.executemany(
        "INSERT INTO card_deps (cardId, dependsOn) VALUES (?, ?)",
        [(card_id, d) for d in sorted(set(deps))],
    )


def _column_size(conn: sqlite3.Connection, status: str, exclude_id: int | None = None) -> int:
    return conn.execute(
        "SELECT COUNT(*) FROM cards WHERE status = ? AND id IS NOT ?", (status, exclude_id)
    ).fetchone()[0]


def _move(conn: sqlite3.Connection, row: sqlite3.Row, status: str, position: int | None) -> int:
    """Move a card to `position` in `status` (end of column if None). Returns the final position."""
    conn.execute(
        "UPDATE cards SET position = position - 1 WHERE status = ? AND position > ?",
        (row["status"], row["position"]),
    )
    size = _column_size(conn, status, exclude_id=row["id"])
    pos = size + 1 if position is None else max(1, min(position, size + 1))
    conn.execute(
        "UPDATE cards SET position = position + 1 WHERE status = ? AND position >= ? AND id != ?",
        (status, pos, row["id"]),
    )
    conn.execute("UPDATE cards SET status = ?, position = ? WHERE id = ?", (status, pos, row["id"]))
    return pos


def create_card(conn: sqlite3.Connection, actor: str, fields: dict) -> dict:
    status = fields.get("status") or "backlog"
    if actor == "groomer" and status != "backlog":
        raise HTTPException(status_code=403, detail="groomer may only create cards in backlog")
    deps = fields.get("dependsOn") or []
    _check_deps(conn, None, deps)
    _check_epic(conn, fields.get("epicId"))

    stamp = now()
    cur = conn.execute(
        """INSERT INTO cards (title, type, points, epicId, status, position, goal, criteria,
                              notes, branch, prUrl, createdAt, updatedAt)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (
            fields["title"],
            fields["type"],
            fields["points"],
            fields.get("epicId"),
            status,
            _column_size(conn, status) + 1,
            fields.get("goal") or "",
            json.dumps(fields.get("criteria") or []),
            fields.get("notes") or "",
            fields.get("branch"),
            fields.get("prUrl"),
            stamp,
            stamp,
        ),
    )
    card_id = cur.lastrowid
    _set_deps(conn, card_id, deps)
    if fields.get("position") is not None:
        _move(conn, get_row(conn, card_id), status, fields["position"])

    card = to_json(conn, get_row(conn, card_id))
    log_activity(conn, actor, "card.created", card_id, f"Created \"{card['title']}\" in {status}", card)
    conn.commit()
    return card


def update_card(conn: sqlite3.Connection, actor: str, card_id: int, changes: dict) -> dict:
    row = get_row(conn, card_id)
    if actor == "builder":
        extra = set(changes) - BUILDER_FIELDS
        if extra:
            raise HTTPException(status_code=403, detail=f"builder may not change {sorted(extra)}")
        if "status" in changes and changes["status"] != "verifying":
            raise HTTPException(status_code=403, detail="builder may only set status to verifying")

    before = to_json(conn, row)
    if "epicId" in changes:
        _check_epic(conn, changes["epicId"])
    if "dependsOn" in changes:
        _check_deps(conn, card_id, changes["dependsOn"])
        _set_deps(conn, card_id, changes["dependsOn"])

    if "status" in changes or "position" in changes:
        status = changes.get("status", row["status"])
        position = changes.get("position")
        if status == row["status"] and position is None:
            position = row["position"]  # same column, no new position: stay put
        _move(conn, row, status, position)

    simple = {k: v for k, v in changes.items() if k not in ("dependsOn", "status", "position")}
    if "criteria" in simple:
        simple["criteria"] = json.dumps(simple["criteria"])
    if "mergedToEpic" in simple:
        simple["mergedToEpic"] = int(simple["mergedToEpic"])
    simple["updatedAt"] = now()
    conn.execute(
        f"UPDATE cards SET {', '.join(f'{k} = ?' for k in simple)} WHERE id = ?",
        (*simple.values(), card_id),
    )

    after = to_json(conn, get_row(conn, card_id))
    diff = {k: {"from": before[k], "to": after[k]} for k in after if k != "updatedAt" and before[k] != after[k]}
    message = (
        f"Moved to {after['status']}" if before["status"] != after["status"] else f"Edited {', '.join(diff) or 'nothing'}"
    )
    log_activity(conn, actor, "card.updated", card_id, message, diff)
    conn.commit()
    return after


def next_card(conn: sqlite3.Connection, points_left: int) -> dict | None:
    """Top `ready` card whose dependencies are all met and whose points fit in `points_left`.

    A dependency is met when it's `done`, when it's in the same epic and its PR has been merged
    into the epic branch (`mergedToEpic`), or when it passed review (`review`) and has a branch:
    then the card is stacked, built on top of that branch instead of waiting for Oliver to merge
    it. `stackOn` lists those branches. A card that doesn't fit is skipped, like one that's
    waiting on a dependency, so a smaller card further down can use up the night.
    """
    row = conn.execute(
        """SELECT * FROM cards c
           WHERE c.status = 'ready'
             AND c.points <= ?
             AND NOT EXISTS (
               SELECT 1 FROM card_deps d JOIN cards dep ON dep.id = d.dependsOn
               WHERE d.cardId = c.id
                 AND dep.status != 'done'
                 AND NOT (dep.status = 'review' AND dep.branch IS NOT NULL)
                 AND NOT (dep.mergedToEpic = 1 AND dep.epicId IS NOT NULL AND dep.epicId IS c.epicId))
           ORDER BY c.position
           LIMIT 1""",
        (points_left,),
    ).fetchone()
    if row is None:
        return None
    card = to_json(conn, row)
    card["stackOn"] = [
        r[0]
        for r in conn.execute(
            """SELECT dep.branch FROM card_deps d JOIN cards dep ON dep.id = d.dependsOn
               WHERE d.cardId = ? AND dep.status = 'review' AND dep.branch IS NOT NULL
                 AND NOT (dep.mergedToEpic = 1 AND dep.epicId IS NOT NULL AND dep.epicId IS ?)
               ORDER BY dep.id""",
            (row["id"], row["epicId"]),
        )
    ]
    return card


# ---- night shift: claim, review, events, comments


def claim(conn: sqlite3.Connection, actor: str, card_id: int, branch: str) -> dict:
    row = get_row(conn, card_id)
    if row["status"] != "ready":
        raise HTTPException(status_code=409, detail=f"Card {card_id} is {row['status']}, not ready")
    _move(conn, row, "building", None)
    conn.execute(
        "UPDATE cards SET branch = ?, attempts = attempts + 1, updatedAt = ? WHERE id = ?",
        (branch, now(), card_id),
    )
    card = to_json(conn, get_row(conn, card_id))
    log_activity(
        conn, actor, "card.claimed", card_id, f"Claimed on {branch} (attempt {card['attempts']})",
        {"branch": branch, "attempts": card["attempts"]},
    )
    conn.commit()
    return card


REVIEWABLE = ("building", "verifying")


def review(conn: sqlite3.Connection, actor: str, card_id: int, verdict: str, notes: str, pr_url: str | None) -> dict:
    """Apply the review rules in README.md."""
    row = get_row(conn, card_id)
    if row["status"] not in REVIEWABLE:
        raise HTTPException(status_code=409, detail=f"Card {card_id} is {row['status']}, nothing to review")

    fails = row["consecutiveFails"]
    if verdict == "PASS":
        fails, status, position = 0, "review", None
    elif verdict == "FAIL":
        fails += 1
        status, position = ("blocked", None) if fails >= 2 else ("ready", 1)
    else:  # BLOCKED
        status, position = "blocked", None

    _move(conn, row, status, position)
    conn.execute(
        "UPDATE cards SET consecutiveFails = ?, lastReview = ?, prUrl = COALESCE(?, prUrl), updatedAt = ? WHERE id = ?",
        (fails, notes, pr_url, now(), card_id),
    )
    card = to_json(conn, get_row(conn, card_id))
    log_activity(
        conn, actor, "card.reviewed", card_id, f"{verdict} → {status}",
        {"verdict": verdict, "status": status, "consecutiveFails": fails, "notes": notes},
    )
    conn.commit()
    return card


def add_event(conn: sqlite3.Connection, actor: str, card_id: int, type: str, message: str) -> dict:
    get_row(conn, card_id)
    log_activity(conn, actor, type, card_id, message)
    conn.commit()
    return {"cardId": card_id, "actor": actor, "type": type, "message": message}


def add_comment(conn: sqlite3.Connection, actor: str, card_id: int, body: str) -> dict:
    get_row(conn, card_id)
    stamp = now()
    cur = conn.execute(
        "INSERT INTO comments (cardId, author, body, createdAt) VALUES (?, ?, ?, ?)",
        (card_id, actor, body, stamp),
    )
    log_activity(conn, actor, "comment.added", card_id, body[:120], {"commentId": cur.lastrowid})
    conn.commit()
    return {"id": cur.lastrowid, "cardId": card_id, "author": actor, "body": body, "createdAt": stamp}


def list_comments(conn: sqlite3.Connection, card_id: int) -> list[dict]:
    rows = conn.execute("SELECT * FROM comments WHERE cardId = ? ORDER BY id", (card_id,))
    return [dict(r) for r in rows]


def list_activity(conn: sqlite3.Connection, since: str | None = None, card_id: int | None = None) -> list[dict]:
    sql, args = "SELECT * FROM activity WHERE 1 = 1", []
    if since:
        sql += " AND createdAt >= ?"
        args.append(since)
    if card_id is not None:
        sql += " AND cardId = ?"
        args.append(card_id)
    rows = conn.execute(sql + " ORDER BY id", args).fetchall()
    return [{**dict(r), "data": json.loads(r["data"])} for r in rows]


def delete_card(conn: sqlite3.Connection, actor: str, card_id: int) -> dict:
    """Remove a card, its comments and every dependency on it. The activity log keeps its history."""
    row = get_row(conn, card_id)
    card = to_json(conn, row)
    conn.execute("DELETE FROM card_deps WHERE cardId = ? OR dependsOn = ?", (card_id, card_id))
    conn.execute("DELETE FROM comments WHERE cardId = ?", (card_id,))
    conn.execute(
        "UPDATE cards SET position = position - 1 WHERE status = ? AND position > ?",
        (row["status"], row["position"]),
    )
    conn.execute("DELETE FROM cards WHERE id = ?", (card_id,))
    log_activity(conn, actor, "card.deleted", card_id, f"Deleted \"{card['title']}\"", card)
    conn.commit()
    return card
