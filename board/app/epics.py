"""Epics: a big feature built over several cards on its own `epic/<slug>` branch."""

import re
import sqlite3

from fastapi import HTTPException

from . import cards
from .db import log_activity, now

EPIC_STATUSES = ("open", "merged", "dropped")


def get_row(conn: sqlite3.Connection, epic_id: int) -> sqlite3.Row:
    row = conn.execute("SELECT * FROM epics WHERE id = ?", (epic_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail=f"No epic {epic_id}")
    return row


def card_ids(conn: sqlite3.Connection, epic_id: int) -> list[int]:
    """The epic's cards in build order: dependencies first, then by id."""
    rows = conn.execute("SELECT id FROM cards WHERE epicId = ? ORDER BY id", (epic_id,)).fetchall()
    ids = [r["id"] for r in rows]
    deps = {
        i: [r[0] for r in conn.execute("SELECT dependsOn FROM card_deps WHERE cardId = ?", (i,)) if r[0] in ids]
        for i in ids
    }
    ordered, seen = [], set()

    def visit(i):
        if i in seen:
            return
        seen.add(i)
        for d in sorted(deps[i]):
            visit(d)
        ordered.append(i)

    for i in ids:
        visit(i)
    return ordered


def to_json(conn: sqlite3.Connection, row: sqlite3.Row) -> dict:
    return {**dict(row), "cardIds": card_ids(conn, row["id"])}


def list_epics(conn: sqlite3.Connection) -> list[dict]:
    rows = conn.execute(
        "SELECT * FROM epics ORDER BY CASE status WHEN 'open' THEN 0 ELSE 1 END, id"
    ).fetchall()
    return [to_json(conn, r) for r in rows]


def with_cards(conn: sqlite3.Connection, epic_id: int) -> dict:
    epic = to_json(conn, get_row(conn, epic_id))
    epic["cards"] = [cards.to_json(conn, cards.get_row(conn, i)) for i in epic["cardIds"]]
    return epic


def create_epic(conn: sqlite3.Connection, actor: str, fields: dict) -> dict:
    slug = fields["slug"]
    if not re.fullmatch(r"[a-z0-9]+(-[a-z0-9]+)*", slug):
        raise HTTPException(status_code=422, detail="slug must be lowercase kebab-case, like fishing-minigame")
    if conn.execute("SELECT 1 FROM epics WHERE slug = ?", (slug,)).fetchone():
        raise HTTPException(status_code=422, detail=f"An epic called {slug} already exists")
    stamp = now()
    cur = conn.execute(
        "INSERT INTO epics (slug, title, pitch, status, branch, prUrl, createdAt, updatedAt) "
        "VALUES (?, ?, ?, 'open', ?, ?, ?, ?)",
        (slug, fields["title"], fields.get("pitch") or "", f"epic/{slug}", fields.get("prUrl"), stamp, stamp),
    )
    epic = to_json(conn, get_row(conn, cur.lastrowid))
    log_activity(conn, actor, "epic.created", None, f"Created epic \"{epic['title']}\"", epic)
    conn.commit()
    return epic


def update_epic(conn: sqlite3.Connection, actor: str, epic_id: int, changes: dict) -> dict:
    before = dict(get_row(conn, epic_id))
    if changes:
        changes = {**changes, "updatedAt": now()}
        conn.execute(
            f"UPDATE epics SET {', '.join(f'{k} = ?' for k in changes)} WHERE id = ?",
            (*changes.values(), epic_id),
        )
    after = dict(get_row(conn, epic_id))
    diff = {k: {"from": before[k], "to": after[k]} for k in after if k != "updatedAt" and before[k] != after[k]}
    log_activity(conn, actor, "epic.updated", None, f"Edited epic {after['slug']}", {"epicId": epic_id, **diff})
    conn.commit()
    return to_json(conn, get_row(conn, epic_id))
