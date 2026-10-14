"""SQLite connection, schema creation and the activity log.

One file, the standard `sqlite3` module, no ORM. `init_db` is safe to call on every start:
it creates missing tables and records the schema version so later phases can migrate.
"""

import json
import os
import sqlite3
from datetime import datetime, timezone

SCHEMA_VERSION = 1

SCHEMA = """
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS epics (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    slug        TEXT NOT NULL UNIQUE,
    title       TEXT NOT NULL,
    pitch       TEXT NOT NULL DEFAULT '',
    status      TEXT NOT NULL DEFAULT 'open',
    branch      TEXT NOT NULL,
    prUrl       TEXT,
    createdAt   TEXT NOT NULL,
    updatedAt   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS cards (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    title            TEXT NOT NULL,
    type             TEXT NOT NULL,
    size             TEXT NOT NULL,
    effort           TEXT NOT NULL,
    epicId           INTEGER REFERENCES epics(id),
    mergedToEpic     INTEGER NOT NULL DEFAULT 0,
    status           TEXT NOT NULL,
    position         INTEGER NOT NULL,
    goal             TEXT NOT NULL DEFAULT '',
    criteria         TEXT NOT NULL DEFAULT '[]',
    notes            TEXT NOT NULL DEFAULT '',
    branch           TEXT,
    prUrl            TEXT,
    attempts         INTEGER NOT NULL DEFAULT 0,
    consecutiveFails INTEGER NOT NULL DEFAULT 0,
    lastReview       TEXT,
    createdAt        TEXT NOT NULL,
    updatedAt        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS card_deps (
    cardId    INTEGER NOT NULL REFERENCES cards(id),
    dependsOn INTEGER NOT NULL REFERENCES cards(id),
    PRIMARY KEY (cardId, dependsOn)
);

CREATE TABLE IF NOT EXISTS comments (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    cardId    INTEGER NOT NULL REFERENCES cards(id),
    author    TEXT NOT NULL,
    body      TEXT NOT NULL,
    createdAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS activity (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    cardId    INTEGER,
    actor     TEXT NOT NULL,
    type      TEXT NOT NULL,
    message   TEXT NOT NULL DEFAULT '',
    data      TEXT NOT NULL DEFAULT '{}',
    createdAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"""


def now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def db_path() -> str:
    return os.environ.get("BOARD_DB", "board.db")


def connect(path: str | None = None) -> sqlite3.Connection:
    # FastAPI may run a request's dependency and handler on different threads.
    conn = sqlite3.connect(path or db_path(), check_same_thread=False)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


def init_db(conn: sqlite3.Connection) -> None:
    conn.executescript(SCHEMA)
    if conn.execute("SELECT COUNT(*) FROM schema_version").fetchone()[0] == 0:
        conn.execute("INSERT INTO schema_version (version) VALUES (?)", (SCHEMA_VERSION,))
    conn.commit()


def log_activity(
    conn: sqlite3.Connection,
    actor: str,
    type: str,
    card_id: int | None = None,
    message: str = "",
    data: dict | None = None,
) -> None:
    """Append one row to the audit log. Every write to the board calls this."""
    conn.execute(
        "INSERT INTO activity (cardId, actor, type, message, data, createdAt) VALUES (?, ?, ?, ?, ?, ?)",
        (card_id, actor, type, message, json.dumps(data or {}), now()),
    )
