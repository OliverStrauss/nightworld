"""Night settings: how much the runner builds in one night. Stored in the `settings` table."""

import json
import re
import sqlite3

from fastapi import HTTPException

from .db import log_activity

DEFAULTS = {"pointsPerNight": 4, "maxCards": 3, "stopTime": "06:00", "paused": False}
POINTS = {"low": 1, "medium": 2, "high": 4}  # README.md: effort → points


def get(conn: sqlite3.Connection) -> dict:
    row = conn.execute("SELECT value FROM settings WHERE key = 'night'").fetchone()
    return {**DEFAULTS, **(json.loads(row["value"]) if row else {})}


def put(conn: sqlite3.Connection, actor: str, values: dict) -> dict:
    if not re.fullmatch(r"([01]\d|2[0-3]):[0-5]\d", values["stopTime"]):
        raise HTTPException(status_code=422, detail="stopTime must be HH:MM (24-hour)")
    before = get(conn)
    after = {k: values[k] for k in DEFAULTS}
    conn.execute(
        "INSERT INTO settings (key, value) VALUES ('night', ?) "
        "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
        (json.dumps(after),),
    )
    diff = {k: {"from": before[k], "to": after[k]} for k in after if before[k] != after[k]}
    log_activity(conn, actor, "settings.night", None, "Changed night settings", diff)
    conn.commit()
    return after
