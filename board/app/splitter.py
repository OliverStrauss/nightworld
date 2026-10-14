"""Split an epic into draft cards with the groomer agent.

Oliver writes the epic's title and pitch; the groomer (Claude Code, `--agent groomer`) reads the
repo and proposes ordered S/M cards. They land in `backlog` as the `groomer` actor, so nothing is
built until Oliver drags them into Ready. It takes a few minutes, so it runs on a thread and the
epic page shows its progress.

Config: BOARD_CLAUDE_CMD (default `claude`), BOARD_REPO_DIR (default: the repo this file is in),
BOARD_SPLIT_TIMEOUT in seconds (default 900).
"""

import json
import os
import re
import shlex
import subprocess
import threading
from pathlib import Path

from fastapi import HTTPException
from pydantic import ValidationError

from . import cards, epics
from .db import log_activity, now

MAX_CARDS = 8
_threads: dict[int, threading.Thread] = {}

PROMPT = """Split this NightWorld epic into draft task cards.

Epic: {title} (slug `{slug}`, built on branch `{branch}`)
Pitch: {pitch}
{existing}
Read CLAUDE.md, docs/design.md, docs/architecture.md and docs/task-cards.md first, and look at
what already exists in game/Assets/_Project so the cards build on real systems.

Rules:
- At most {max_cards} cards, in build order. Each is one night of work: size S or M, never L.
- Systems before content. Every acceptance criterion is checkable by a test, the validator or a
  screenshot. Name the IDs (kebab-case) you want created.
- Keep the game's vibe: fun beats realistic, light and a bit silly.
- `dependsOn` lists the 1-based numbers of earlier cards in YOUR list that must be built first.

Do not change any files and do not call the board API. Reply with only a JSON object, no prose:
{{"cards": [{{"title": "...", "type": "content|system|fix|chore", "size": "S|M",
  "effort": "low|medium|high", "goal": "...", "criteria": ["...", "..."], "notes": "...",
  "dependsOn": [1]}}]}}
"""


def _key(epic_id: int) -> str:
    return f"split:{epic_id}"


def status(conn, epic_id: int) -> dict | None:
    row = conn.execute("SELECT value FROM settings WHERE key = ?", (_key(epic_id),)).fetchone()
    return json.loads(row["value"]) if row else None


def _set_status(conn, epic_id: int, state: str, message: str) -> None:
    value = json.dumps({"state": state, "message": message, "at": now()})
    conn.execute(
        "INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
        (_key(epic_id), value),
    )
    conn.commit()


def build_prompt(conn, epic_id: int) -> str:
    epic = epics.with_cards(conn, epic_id)
    existing = ""
    if epic["cards"]:
        lines = "\n".join(f"- #{c['id']} {c['title']} ({c['status']})" for c in epic["cards"])
        existing = f"\nCards already in this epic (don't repeat them; add what's missing):\n{lines}\n"
    return PROMPT.format(
        title=epic["title"], slug=epic["slug"], branch=epic["branch"], pitch=epic["pitch"] or "(none)",
        existing=existing, max_cards=MAX_CARDS,
    )


def run_claude(prompt: str) -> str:
    """Run the groomer read-only and return its final text."""
    repo = os.environ.get("BOARD_REPO_DIR") or str(Path(__file__).resolve().parents[2])
    cmd = shlex.split(os.environ.get("BOARD_CLAUDE_CMD", "claude")) + [
        "-p", "--agent", "groomer", "--output-format", "json",
        "--permission-mode", "dontAsk", "--allowedTools", "Read", "Glob", "Grep",
    ]
    # The prompt goes on stdin: --allowedTools takes every argument after it as a tool name.
    done = subprocess.run(
        cmd, input=prompt, cwd=repo, capture_output=True, text=True,
        timeout=int(os.environ.get("BOARD_SPLIT_TIMEOUT", "900")),
    )
    if done.returncode != 0:
        raise RuntimeError(f"claude exited with {done.returncode}: {(done.stderr or done.stdout)[-500:]}")
    try:
        return json.loads(done.stdout)["result"]
    except (ValueError, KeyError):
        return done.stdout


def parse_cards(text: str) -> list[dict]:
    """Find the {"cards": [...]} object in the groomer's reply, even if it wrapped it in prose or a fence."""
    fenced = re.findall(r"```(?:json)?\s*(\{.*?\})\s*```", text, re.S)
    for candidate in fenced + [text[text.find("{"): text.rfind("}") + 1]]:
        try:
            found = json.loads(candidate)["cards"]
        except (ValueError, KeyError, TypeError):
            continue
        if isinstance(found, list) and found:
            return found[:MAX_CARDS]
    raise ValueError("The groomer's reply had no cards in it")


def create_drafts(conn, epic_id: int, drafts: list[dict]) -> list[int]:
    """Add the drafts to backlog as the groomer. `dependsOn` numbers refer to earlier drafts."""
    from .api import CardCreate  # api imports this module's neighbours; import late to avoid a cycle

    checked = []
    for n, draft in enumerate(drafts, start=1):  # check them all before adding any
        deps = [d for d in draft.get("dependsOn") or [] if isinstance(d, int) and 0 < d < n]
        fields = {k: draft.get(k) for k in ("title", "type", "size", "effort", "goal", "notes")}
        fields = {k: v for k, v in fields.items() if v not in (None, "")}
        fields.update(criteria=[str(c) for c in draft.get("criteria") or []], epicId=epic_id)
        try:
            checked.append((CardCreate(**fields).model_dump(), deps))
        except ValidationError as exc:
            raise ValueError(f"Draft {n} ({draft.get('title', '?')}) was invalid: {exc}") from exc

    ids: list[int] = []
    for fields, deps in checked:
        fields["dependsOn"] = [ids[d - 1] for d in deps]
        ids.append(cards.create_card(conn, "groomer", fields)["id"])
    return ids


def split(connect, epic_id: int, runner=None) -> None:
    """The whole job. Records progress in settings so the epic page can show it."""
    conn = connect()
    try:
        prompt = build_prompt(conn, epic_id)
        text = (runner or run_claude)(prompt)
        ids = create_drafts(conn, epic_id, parse_cards(text))
        message = f"Added {len(ids)} draft cards to Backlog: " + ", ".join(f"#{i}" for i in ids)
        _set_status(conn, epic_id, "done", message)
        log_activity(conn, "groomer", "epic.split", None, message, {"epicId": epic_id, "cardIds": ids})
        conn.commit()
    except Exception as exc:  # anything at all: show it on the epic page rather than lose it
        conn.rollback()
        _set_status(conn, epic_id, "failed", str(exc)[:500])
    finally:
        conn.close()


def start(connect, epic_id: int, actor: str) -> dict:
    conn = connect()
    try:
        epics.get_row(conn, epic_id)  # 404 if missing
        running = _threads.get(epic_id)
        if running and running.is_alive():
            raise HTTPException(status_code=409, detail="The groomer is already splitting this epic")
        _set_status(conn, epic_id, "running", "The groomer is reading the repo and drafting cards…")
        log_activity(conn, actor, "epic.split.started", None, "Asked the groomer to split the epic", {"epicId": epic_id})
        conn.commit()
    finally:
        conn.close()
    thread = threading.Thread(target=split, args=(connect, epic_id), daemon=True)
    _threads[epic_id] = thread
    thread.start()
    return {"state": "running"}
