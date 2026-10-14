"""HTML pages for Oliver's phone. Every write goes through the same rules as the API, as `oliver`."""

import re
import sqlite3
from datetime import datetime
from pathlib import Path
from urllib.parse import quote

from fastapi import APIRouter, Depends, Form, HTTPException, Request
from fastapi.responses import JSONResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from pydantic import ValidationError

from . import cards, epics, night
from .api import CardCreate, CardPatch, EpicCreate, EpicPatch, NightSettings, get_conn
from .auth import COOKIE, SESSION_DAYS, check_password, make_session, session_actor

router = APIRouter()
templates = Jinja2Templates(directory=Path(__file__).parent / "templates")

# Every status the runner uses, in the words the card page shows.
STATUS_NAMES = {
    "backlog": "Backlog",
    "ready": "Ready",
    "building": "Building now",
    "verifying": "Checking the PR",
    "review": "Built, PR to merge",
    "done": "Done",
    "blocked": "Stuck",
}
# Oliver sees three stages; the runner's in-between statuses show as badges inside them.
STAGES = [("backlog", "Backlog"), ("ready", "Ready"), ("done", "Done")]
STAGE_OF = {"backlog": "backlog", "ready": "ready", "building": "ready", "verifying": "ready",
            "blocked": "ready", "review": "done", "done": "done"}
DONE_SHOWN = 20
templates.env.globals.update(STAGES=STAGES, STAGE_OF=STAGE_OF, STATUS_NAMES=STATUS_NAMES, POINTS=night.POINTS)
# "2026-10-14T18:45:20Z" → "Oct 14 18:45" (UTC, like the stored timestamps)
templates.env.filters["when"] = lambda iso: datetime.fromisoformat(iso.replace("Z", "+00:00")).strftime("%b %d %H:%M")


def login_required(request: Request) -> str:
    actor = session_actor(request.cookies.get(COOKIE))
    if actor is None:
        target = quote(request.url.path)
        raise HTTPException(status_code=303, headers={"Location": f"/login?next={target}"})
    return actor


def page(request: Request, name: str, status_code: int = 200, **context):
    return templates.TemplateResponse(request, name, context, status_code=status_code)


def back_to(url: str) -> RedirectResponse:
    return RedirectResponse(url, status_code=303)


def error_text(exc: Exception) -> str:
    if isinstance(exc, ValidationError):
        return "; ".join(f"{'.'.join(map(str, e['loc'])) or 'form'}: {e['msg']}" for e in exc.errors())
    return str(getattr(exc, "detail", exc))


# ---- login


@router.get("/login")
def login_page(request: Request, next: str = "/"):
    return page(request, "login.html", next=next)


@router.post("/login")
def login(request: Request, password: str = Form(""), next: str = Form("/")):
    if not check_password(password):
        return page(request, "login.html", status_code=401, next=next, error="That's not the password.")
    # Only ever redirect within the board.
    target = next if next.startswith("/") and not next.startswith("//") else "/"
    response = back_to(target)
    response.set_cookie(
        COOKIE, make_session(), max_age=SESSION_DAYS * 86400, httponly=True, samesite="lax"
    )
    return response


@router.post("/logout")
def logout():
    response = back_to("/login")
    response.delete_cookie(COOKIE)
    return response


# ---- board


@router.get("/")
def board(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    all_cards = cards.list_cards(conn)

    def having(*statuses):
        return [c for c in all_cards if c["status"] in statuses]

    finished = sorted(having("done"), key=lambda c: c["updatedAt"], reverse=True)
    return page(
        request, "board.html",
        backlog=having("backlog"),
        working=having("building", "verifying"),
        queue=having("ready"),
        stuck=having("blocked"),
        to_merge=having("review"),
        done=finished[:DONE_SHOWN],
        done_total=len(finished),
        epic_slugs={e["id"]: e["slug"] for e in epics.list_epics(conn)},
        night=night.get(conn),
        next_card=cards.next_card(conn),
    )


@router.post("/cards/{card_id}/move")
async def move(
    request: Request, card_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    """Drag and drop on the board (JSON), or the "Move to" form on the card page."""
    if request.headers.get("content-type", "").startswith("application/json"):
        try:
            changes = CardPatch(**(await request.json())).model_dump(exclude_unset=True)
            card = cards.update_card(conn, actor, card_id, changes)
        except (ValidationError, HTTPException) as exc:
            return JSONResponse({"detail": error_text(exc)}, status_code=getattr(exc, "status_code", 422))
        return card
    form = await request.form()
    current = cards.get_row(conn, card_id)["status"]
    # "Ready" on a card that's building just means "leave it be"; Ready on a stuck card unblocks it,
    # and Done on a built card means its PR is merged.
    if not (form.get("status") == "ready" and current in ("building", "verifying")):
        try:
            cards.update_card(conn, actor, card_id, CardPatch(status=form.get("status")).model_dump(exclude_unset=True))
        except ValidationError:
            pass  # unknown status from a hand-made form: leave the card alone
    return back_to(f"/cards/{card_id}")


# ---- card pages


def parse_card_form(form) -> dict:
    """Turn the card form into API fields. Criteria are one per line; depends-on takes '12, #13, card-14'."""
    criteria = [
        re.sub(r"^\s*(?:[-*]\s*)?(?:\[[ xX]?\]\s*)?", "", line).strip()
        for line in (form.get("criteria") or "").splitlines()
    ]
    deps = [int(n) for n in re.findall(r"\d+", form.get("dependsOn") or "")]
    fields = {
        "title": (form.get("title") or "").strip(),
        "type": form.get("type"),
        "size": form.get("size"),
        "effort": form.get("effort") or None,
        "goal": (form.get("goal") or "").strip(),
        "criteria": [c for c in criteria if c],
        "notes": (form.get("notes") or "").strip(),
        "dependsOn": deps,
        "epicId": int(form["epicId"]) if form.get("epicId") else None,
        "branch": (form.get("branch") or "").strip() or None,
        "prUrl": (form.get("prUrl") or "").strip() or None,
    }
    if form.get("status"):
        fields["status"] = form.get("status")
    return fields


def card_form(request: Request, conn, card: dict | None, form: dict | None = None, error: str | None = None):
    return page(
        request, "card_form.html", status_code=422 if error else 200,
        card=card, form=form or card or {}, error=error, epics=epics.list_epics(conn),
    )


@router.get("/cards/new")
def new_card(
    request: Request, epic: int | None = None, actor: str = Depends(login_required),
    conn: sqlite3.Connection = Depends(get_conn),
):
    return card_form(request, conn, None, {"status": "backlog", "epicId": epic, "size": "S", "type": "content"})


@router.post("/cards/new")
async def create_card(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    form = await request.form()
    fields = parse_card_form(form)
    try:
        card = cards.create_card(conn, actor, CardCreate(**fields).model_dump())
    except (ValidationError, HTTPException) as exc:
        return card_form(request, conn, None, {**fields, "criteriaText": form.get("criteria")}, error_text(exc))
    return back_to(f"/cards/{card['id']}")


@router.get("/cards/{card_id}")
def card_page(
    request: Request, card_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    card = cards.to_json(conn, cards.get_row(conn, card_id))
    deps = [cards.to_json(conn, cards.get_row(conn, d)) for d in card["dependsOn"]]
    epic = epics.to_json(conn, epics.get_row(conn, card["epicId"])) if card["epicId"] else None
    return page(
        request, "card.html", card=card, deps=deps, epic=epic,
        comments=cards.list_comments(conn, card_id),
        activity=list(reversed(cards.list_activity(conn, card_id=card_id))),
    )


@router.get("/cards/{card_id}/edit")
def edit_card(
    request: Request, card_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    card = cards.to_json(conn, cards.get_row(conn, card_id))
    return card_form(request, conn, card)


@router.post("/cards/{card_id}/edit")
async def save_card(
    request: Request, card_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    card = cards.to_json(conn, cards.get_row(conn, card_id))
    form = await request.form()
    fields = parse_card_form(form)
    fields.pop("status", None)  # moving has its own control
    if fields["effort"] is None:
        fields["effort"] = cards.EFFORT_FROM_SIZE.get(fields["size"], card["effort"])
    try:
        cards.update_card(conn, actor, card_id, CardPatch(**fields).model_dump(exclude_unset=True))
    except (ValidationError, HTTPException) as exc:
        return card_form(request, conn, card, {**fields, "criteriaText": form.get("criteria")}, error_text(exc))
    return back_to(f"/cards/{card_id}")


@router.post("/cards/{card_id}/comments")
def comment(
    card_id: int, body: str = Form(""), actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    if body.strip():
        cards.add_comment(conn, actor, card_id, body.strip())
    return back_to(f"/cards/{card_id}#comments")


# ---- night settings


@router.get("/settings")
def settings_page(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    return page(request, "settings.html", night=night.get(conn), saved=request.query_params.get("saved"))


@router.post("/settings")
async def save_settings(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    form = await request.form()
    values = {
        "pointsPerNight": form.get("pointsPerNight"),
        "maxCards": form.get("maxCards"),
        "stopTime": form.get("stopTime"),
        "paused": form.get("paused") == "on",
    }
    try:
        night.put(conn, actor, NightSettings(**values).model_dump())
    except (ValidationError, HTTPException) as exc:
        return page(request, "settings.html", status_code=422, night=values, error=error_text(exc))
    return back_to("/settings?saved=1")


# ---- epics


@router.get("/epics")
def epics_page(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    return epics_list(request, conn)


def epics_list(request: Request, conn, form: dict | None = None, error: str | None = None):
    all_epics = epics.list_epics(conn)
    for epic in all_epics:
        statuses = [cards.get_row(conn, i)["status"] for i in epic["cardIds"]]
        epic["done"] = statuses.count("done")
    return page(request, "epics.html", status_code=422 if error else 200, epics=all_epics, form=form or {}, error=error)


@router.post("/epics")
async def create_epic(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    form = await request.form()
    fields = {k: (form.get(k) or "").strip() for k in ("slug", "title", "pitch", "prUrl")}
    fields["prUrl"] = fields["prUrl"] or None
    try:
        epic = epics.create_epic(conn, actor, EpicCreate(**fields).model_dump())
    except (ValidationError, HTTPException) as exc:
        return epics_list(request, conn, fields, error_text(exc))
    return back_to(f"/epics/{epic['id']}")


@router.get("/epics/{epic_id}")
def epic_page(
    request: Request, epic_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    epic = epics.with_cards(conn, epic_id)
    return page(request, "epic.html", epic=epic, done=sum(c["status"] == "done" for c in epic["cards"]))


@router.post("/epics/{epic_id}/edit")
async def save_epic(
    request: Request, epic_id: int, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)
):
    form = await request.form()
    fields = {k: (form.get(k) or "").strip() for k in ("title", "pitch", "status", "prUrl")}
    fields["prUrl"] = fields["prUrl"] or None
    try:
        epics.update_epic(conn, actor, epic_id, EpicPatch(**fields).model_dump(exclude_unset=True))
    except (ValidationError, HTTPException) as exc:
        epic = epics.with_cards(conn, epic_id)
        return page(
            request, "epic.html", status_code=422, epic=epic, error=error_text(exc),
            done=sum(c["status"] == "done" for c in epic["cards"]),
        )
    return back_to(f"/epics/{epic_id}")


# ---- activity feed


@router.get("/activity")
def activity_page(request: Request, actor: str = Depends(login_required), conn: sqlite3.Connection = Depends(get_conn)):
    log = list(reversed(cards.list_activity(conn)))[:200]
    titles = {r["id"]: r["title"] for r in conn.execute("SELECT id, title FROM cards")}
    return page(request, "activity.html", activity=log, titles=titles)
