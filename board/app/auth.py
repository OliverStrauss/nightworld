"""Bearer tokens: one per actor, read from the environment.

`BOARD_TOKEN_OLIVER`, `BOARD_TOKEN_BUILDER`, `BOARD_TOKEN_REVIEWER`, `BOARD_TOKEN_GROOMER`.
The token decides which actor is recorded in the activity log. An actor whose variable is
unset or empty has no token and can't log in.
"""

import hashlib
import hmac
import os
import secrets
import time

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


# ---- web UI: Oliver's password and login cookie
#
# BOARD_PASSWORD_HASH  made with `python -m app.auth` (pbkdf2, never the password itself)
# BOARD_SECRET         any long random string; signs the login cookie

COOKIE = "board_session"
SESSION_DAYS = 30
_ITERATIONS = 200_000


def hash_password(password: str, salt: str | None = None, iterations: int = _ITERATIONS) -> str:
    salt = salt or secrets.token_hex(16)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), bytes.fromhex(salt), iterations).hex()
    return f"pbkdf2_sha256${iterations}${salt}${digest}"


def check_password(password: str) -> bool:
    stored = os.environ.get("BOARD_PASSWORD_HASH", "")
    try:
        algo, iterations, salt, _ = stored.split("$")
        return algo == "pbkdf2_sha256" and hmac.compare_digest(
            hash_password(password, salt, int(iterations)), stored
        )
    except ValueError:  # unset or malformed hash
        return False


def _secret() -> bytes:
    secret = os.environ.get("BOARD_SECRET", "")
    if not secret:
        raise HTTPException(status_code=500, detail="BOARD_SECRET is not set")
    return secret.encode()


def _sign(payload: str) -> str:
    return hmac.new(_secret(), payload.encode(), hashlib.sha256).hexdigest()


def make_session() -> str:
    expires = int(time.time()) + SESSION_DAYS * 86400
    return f"oliver.{expires}.{_sign(f'oliver.{expires}')}"


def session_actor(cookie: str | None) -> str | None:
    """The actor a login cookie belongs to, or None if it's missing, forged or expired."""
    try:
        actor, expires, sig = (cookie or "").split(".")
        if not hmac.compare_digest(sig, _sign(f"{actor}.{expires}")) or int(expires) < time.time():
            return None
    except (ValueError, HTTPException):
        return None
    return actor


if __name__ == "__main__":
    import getpass

    print(hash_password(getpass.getpass("Board password: ")))
