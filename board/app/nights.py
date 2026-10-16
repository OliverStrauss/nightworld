"""Night-shift runs as the runner reports them: when a night starts and ends, its log, and the
screenshots CI took of each card. The Tonight page is built from this plus the card activity log.

A night is `running` until the runner calls finish. If the runner dies without finishing, the
night stays `running`, so the page also shows how long ago the board last heard from it.
"""

import json
import os
import re
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

from fastapi import HTTPException

from .db import log_activity, now

# The runner sends its log every 15 s, so this long without a word means it's stuck or gone.
QUIET_MINUTES = 5
LINE_MAX = 2000           # characters kept per log line
TAIL = 200                # log lines shown on the page
SHOT_MAX = 15 * 1024 * 1024
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"

# Card activity types the runner posts, in the order a card goes through them (runner/nightly.sh).
STEPS = [("build", "Build"), ("pr", "PR"), ("ci", "CI"), ("review", "Review")]


def files_dir(db_path: str) -> Path:
    """Where screenshots live: BOARD_FILES, or a board-files folder next to the database."""
    return Path(os.environ.get("BOARD_FILES") or Path(db_path).resolve().parent / "board-files")


def _parse(iso: str) -> datetime:
    return datetime.fromisoformat(iso.replace("Z", "+00:00"))


def minutes_since(iso: str) -> int:
    return int((datetime.now(timezone.utc) - _parse(iso)).total_seconds() // 60)


def to_json(row: sqlite3.Row) -> dict:
    night = dict(row)
    night["budget"] = json.loads(night["budget"])
    night["quiet"] = night["status"] == "running" and minutes_since(night["lastSeenAt"]) >= QUIET_MINUTES
    return night


def get_row(conn: sqlite3.Connection, night_id: int) -> sqlite3.Row:
    row = conn.execute("SELECT * FROM nights WHERE id = ?", (night_id,)).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail=f"No night {night_id}")
    return row


def latest(conn: sqlite3.Connection) -> dict | None:
    row = conn.execute("SELECT * FROM nights ORDER BY id DESC LIMIT 1").fetchone()
    return to_json(row) if row else None


def list_nights(conn: sqlite3.Connection, limit: int = 30) -> list[dict]:
    rows = conn.execute("SELECT * FROM nights ORDER BY id DESC LIMIT ?", (limit,)).fetchall()
    return [to_json(r) for r in rows]


def start(conn: sqlite3.Connection, actor: str, budget: dict, host: str) -> dict:
    stamp = now()
    cur = conn.execute(
        "INSERT INTO nights (budget, host, startedAt, lastSeenAt) VALUES (?, ?, ?, ?)",
        (json.dumps(budget), host, stamp, stamp),
    )
    log_activity(conn, actor, "night.started", None, f"Night shift started on {host or 'the runner'}", budget)
    conn.execute("UPDATE nights SET fromActivity = last_insert_rowid() WHERE id = ?", (cur.lastrowid,))
    conn.commit()
    return to_json(get_row(conn, cur.lastrowid))


def append_log(conn: sqlite3.Connection, night_id: int, lines: list[str]) -> dict:
    """Add log lines. An empty list is a heartbeat: it only updates lastSeenAt."""
    get_row(conn, night_id)
    stamp = now()
    conn.executemany(
        "INSERT INTO night_log (nightId, line, createdAt) VALUES (?, ?, ?)",
        [(night_id, line[:LINE_MAX], stamp) for line in lines],
    )
    conn.execute("UPDATE nights SET lastSeenAt = ? WHERE id = ?", (stamp, night_id))
    conn.commit()
    return {"nightId": night_id, "added": len(lines)}


def finish(conn: sqlite3.Connection, actor: str, night_id: int, status: str, message: str) -> dict:
    get_row(conn, night_id)
    stamp = now()
    conn.execute(
        "UPDATE nights SET status = ?, message = ?, finishedAt = ?, lastSeenAt = ? WHERE id = ?",
        (status, message, stamp, stamp, night_id),
    )
    log_activity(conn, actor, "night.finished", None, f"Night shift {status}" + (f": {message}" if message else ""))
    conn.execute("UPDATE nights SET toActivity = last_insert_rowid() WHERE id = ?", (night_id,))
    conn.commit()
    return to_json(get_row(conn, night_id))


def log_tail(conn: sqlite3.Connection, night_id: int, limit: int = TAIL) -> list[dict]:
    rows = conn.execute(
        "SELECT id, line, createdAt FROM night_log WHERE nightId = ? ORDER BY id DESC LIMIT ?", (night_id, limit)
    ).fetchall()
    return [dict(r) for r in reversed(rows)]


def log_count(conn: sqlite3.Connection, night_id: int) -> int:
    return conn.execute("SELECT COUNT(*) FROM night_log WHERE nightId = ?", (night_id,)).fetchone()[0]


# ---- what each card went through tonight


def _steps(events: list[dict], running: bool) -> list[dict]:
    """Build → PR → CI → Review, each pending / active / done / fail (or stopped if the night ended)."""
    types = {e["type"] for e in events}
    reviewed = next((e for e in reversed(events) if e["type"] == "card.reviewed"), None)
    verdict = reviewed["data"].get("verdict") if reviewed else None
    has_pr = any(
        e["type"] == "card.updated" and e["data"].get("status", {}).get("to") == "verifying" for e in events
    )
    state = {}
    state["build"] = "done" if has_pr else "fail" if verdict else "active"
    state["pr"] = "done" if has_pr else "fail" if verdict else "pending"
    state["ci"] = (
        "done" if "ci_passed" in types else "fail" if "ci_failed" in types
        else "active" if "ci_started" in types else "pending"
    )
    if has_pr and verdict and state["ci"] in ("pending", "active"):
        state["ci"] = "fail"  # the runner gave up waiting
    state["review"] = (
        ("done" if verdict == "PASS" else "fail") if verdict
        else "active" if "reviewer_started" in types else "pending"
    )
    if verdict and not has_pr:
        state["ci"] = state["review"] = "pending"
    if not running:
        state = {k: "stopped" if v == "active" else v for k, v in state.items()}
    return [{"key": k, "label": label, "state": state[k]} for k, label in STEPS]


def night_cards(conn: sqlite3.Connection, night: dict) -> list[dict]:
    """The cards this night claimed, in order, with their steps, verdict and screenshots."""
    rows = conn.execute(
        "SELECT * FROM activity WHERE cardId IS NOT NULL AND id > ? AND id < ? ORDER BY id",
        (night["fromActivity"] or 0, night["toActivity"] or 2**62),
    ).fetchall()
    by_card: dict[int, list[dict]] = {}
    for r in rows:
        event = {**dict(r), "data": json.loads(r["data"])}
        if event["type"] == "card.claimed":
            by_card.setdefault(r["cardId"], [])
        if r["cardId"] in by_card:
            by_card[r["cardId"]].append(event)

    result = []
    for card_id, events in by_card.items():
        card = conn.execute("SELECT * FROM cards WHERE id = ?", (card_id,)).fetchone()
        reviewed = next((e for e in reversed(events) if e["type"] == "card.reviewed"), None)
        result.append({
            "id": card_id,
            "title": card["title"] if card else f"Card {card_id} (deleted)",
            "status": card["status"] if card else None,
            "prUrl": card["prUrl"] if card else None,
            "startedAt": events[0]["createdAt"],
            "verdict": reviewed["data"].get("verdict") if reviewed else None,
            "review": reviewed["data"].get("notes") if reviewed else None,
            "steps": _steps(events, night["status"] == "running"),
            "events": [e for e in events if e["type"] not in ("card.updated",)],
            "shots": screenshots(conn, card_id, night["id"]),
        })
    return result


def now_doing(cards: list[dict]) -> str | None:
    """'Building "Bog slimes"' for the banner, from the last card's first unfinished step."""
    if not cards:
        return None
    card = cards[-1]
    active = next((s for s in card["steps"] if s["state"] == "active"), None)
    if not active:
        return None
    verb = {"build": "Building", "pr": "Opening the PR for", "ci": "Waiting for CI on", "review": "Reviewing"}
    return f"{verb[active['key']]} “{card['title']}”"


# ---- screenshots


NAME_OK = re.compile(r"[A-Za-z0-9_.\-/ ]{1,200}")


def add_screenshot(conn, files: Path, card_id: int, night_id: int | None, name: str, data: bytes) -> dict:
    if conn.execute("SELECT 1 FROM cards WHERE id = ?", (card_id,)).fetchone() is None:
        raise HTTPException(status_code=404, detail=f"No card {card_id}")
    if night_id is not None:
        get_row(conn, night_id)
    if not NAME_OK.fullmatch(name):
        raise HTTPException(status_code=422, detail="name: letters, digits, spaces and _ . - / only")
    if not data.startswith(PNG_MAGIC):
        raise HTTPException(status_code=422, detail="Only PNG images")
    if len(data) > SHOT_MAX:
        raise HTTPException(status_code=413, detail="Screenshot is bigger than 15 MB")
    cur = conn.execute(
        "INSERT INTO screenshots (nightId, cardId, name, createdAt) VALUES (?, ?, ?, ?)",
        (night_id, card_id, name, now()),
    )
    shots = files / "shots"
    shots.mkdir(parents=True, exist_ok=True)
    (shots / f"{cur.lastrowid}.png").write_bytes(data)
    conn.commit()
    return {"id": cur.lastrowid, "cardId": card_id, "nightId": night_id, "name": name}


def screenshots(conn: sqlite3.Connection, card_id: int, night_id: int | None = None) -> list[dict]:
    """A card's screenshots from one night, or from the latest night that has any."""
    if night_id is None:
        row = conn.execute(
            "SELECT nightId FROM screenshots WHERE cardId = ? ORDER BY id DESC LIMIT 1", (card_id,)
        ).fetchone()
        if row is None:
            return []
        night_id = row[0]
    rows = conn.execute(
        "SELECT * FROM screenshots WHERE cardId = ? AND nightId IS ? ORDER BY name", (card_id, night_id)
    ).fetchall()
    return [dict(r) for r in rows]


def screenshot_path(conn: sqlite3.Connection, files: Path, shot_id: int) -> Path:
    path = files / "shots" / f"{shot_id}.png"
    if conn.execute("SELECT 1 FROM screenshots WHERE id = ?", (shot_id,)).fetchone() is None or not path.exists():
        raise HTTPException(status_code=404, detail=f"No screenshot {shot_id}")
    return path
