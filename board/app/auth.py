"""Bearer tokens: one per actor, read from the environment.

`BOARD_TOKEN_OLIVER`, `BOARD_TOKEN_BUILDER`, `BOARD_TOKEN_REVIEWER`, `BOARD_TOKEN_GROOMER`.
The token decides which actor is recorded in the activity log. An actor whose variable is
unset or empty has no token and can't log in.
"""

import hmac
import os

from fastapi import Header, HTTPException

ACTORS = ("oliver", "builder", "reviewer", "groomer")


def tokens() -> dict[str, str]:
    """Map token → actor, read fresh so tests and restarts pick up changes."""
    result = {}
    for actor in ACTORS:
        token = os.environ.get(f"BOARD_TOKEN_{actor.upper()}", "")
        if token:
            result[token] = actor
    return result


def current_actor(authorization: str | None = Header(default=None)) -> str:
    scheme, _, token = (authorization or "").partition(" ")
    if scheme.lower() == "bearer" and token:
        for known, actor in tokens().items():
            if hmac.compare_digest(known, token):
                return actor
    raise HTTPException(status_code=401, detail="Unknown or missing token")


def require(actor: str, *allowed: str) -> None:
    if actor not in allowed:
        raise HTTPException(status_code=403, detail=f"{actor} can't do this")
