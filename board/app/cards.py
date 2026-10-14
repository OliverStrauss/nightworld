"""Card rules: who may change what, positions within a column, and next-card selection.

Positions are 1-based and contiguous within each status column. Moving a card closes the gap
it leaves and shifts the cards below its new spot down by one.
"""

import json
import sqlite3

from fastapi import HTTPException

from .db import log_activity, now

STATUSES = ("backlog", "ready", "building", "verifying", "review", "done", "blocked")
EFFORT_FROM_SIZE = {"S": "low", "M": "medium"}

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

    stamp = now()
    cur = conn.execute(
        """INSERT INTO cards (title, type, size, effort, status, position, goal, criteria, notes,
                              branch, prUrl, createdAt, updatedAt)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
        (
            fields["title"],
            fields["type"],
            fields["size"],
            fields.get("effort") or EFFORT_FROM_SIZE[fields["size"]],
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


def next_card(conn: sqlite3.Connection) -> dict | None:
    """Top `ready` card whose dependencies are all `done`.

    The epic rule (a same-epic dependency with `mergedToEpic` also counts as met) comes with
    epics in phase 5.
    """
    row = conn.execute(
        """SELECT * FROM cards c
           WHERE c.status = 'ready'
             AND NOT EXISTS (
               SELECT 1 FROM card_deps d JOIN cards dep ON dep.id = d.dependsOn
               WHERE d.cardId = c.id AND dep.status != 'done')
           ORDER BY c.position
           LIMIT 1"""
    ).fetchone()
    return to_json(conn, row) if row else None
