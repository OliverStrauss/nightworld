"""JSON endpoints under /api. Contract: board/README.md."""

import sqlite3
from typing import Literal

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, ConfigDict, Field

from . import cards, epics, night, splitter
from .auth import current_actor, require

router = APIRouter(prefix="/api")

CardType = Literal["content", "system", "fix", "chore"]
Size = Literal["S", "M"]
Effort = Literal["low", "medium", "high"]
Status = Literal["backlog", "ready", "building", "verifying", "review", "done", "blocked"]


class CardCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1)
    type: CardType
    size: Size
    effort: Effort | None = None
    status: Status | None = None
    position: int | None = Field(default=None, ge=1)
    goal: str = ""
    criteria: list[str] = []
    notes: str = ""
    dependsOn: list[int] = []
    epicId: int | None = None
    branch: str | None = None
    prUrl: str | None = None


class CardPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1)
    type: CardType | None = None
    size: Size | None = None
    effort: Effort | None = None
    status: Status | None = None
    position: int | None = Field(default=None, ge=1)
    goal: str | None = None
    criteria: list[str] | None = None
    notes: str | None = None
    dependsOn: list[int] | None = None
    epicId: int | None = None
    branch: str | None = None
    prUrl: str | None = None
    mergedToEpic: bool | None = None


class Claim(BaseModel):
    model_config = ConfigDict(extra="forbid")

    branch: str = Field(min_length=1)


class Review(BaseModel):
    model_config = ConfigDict(extra="forbid")

    verdict: Literal["PASS", "FAIL", "BLOCKED"]
    notes: str = ""
    prUrl: str | None = None


class Event(BaseModel):
    model_config = ConfigDict(extra="forbid")

    type: str = Field(min_length=1)
    message: str = ""


class Comment(BaseModel):
    model_config = ConfigDict(extra="forbid")

    body: str = Field(min_length=1)


class NightSettings(BaseModel):
    model_config = ConfigDict(extra="forbid")

    pointsPerNight: int = Field(ge=0)
    maxCards: int = Field(ge=0)
    stopTime: str
    paused: bool


class EpicCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    slug: str
    title: str = Field(min_length=1)
    pitch: str = ""
    prUrl: str | None = None


class EpicPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1)
    pitch: str | None = None
    status: Literal["open", "merged", "dropped"] | None = None
    prUrl: str | None = None


# Fields that may be cleared with an explicit null; everything else needs a value.
NULLABLE = {"branch", "prUrl", "epicId"}


def reject_nulls(changes: dict) -> dict:
    cleared = [k for k, v in changes.items() if v is None and k not in NULLABLE]
    if cleared:
        raise HTTPException(status_code=422, detail=f"{cleared} can't be null")
    return changes


def get_conn(request: Request):
    conn = request.app.state.connect()
    try:
        yield conn
    finally:
        conn.close()


@router.get("/cards")
def list_cards(
    status: Status | None = None,
    actor: str = Depends(current_actor),
    conn: sqlite3.Connection = Depends(get_conn),
):
    return cards.list_cards(conn, status)


@router.get("/cards/next")
def next_card(actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    require(actor, "builder")
    card = cards.next_card(conn)
    return card if card else Response(status_code=204)


@router.get("/cards/{card_id}")
def get_card(card_id: int, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return cards.to_json(conn, cards.get_row(conn, card_id))


@router.post("/cards", status_code=201)
def create_card(
    body: CardCreate, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)
):
    require(actor, "oliver", "groomer")
    return cards.create_card(conn, actor, body.model_dump())


@router.patch("/cards/{card_id}")
def update_card(
    card_id: int,
    body: CardPatch,
    actor: str = Depends(current_actor),
    conn: sqlite3.Connection = Depends(get_conn),
):
    require(actor, "oliver", "builder")
    return cards.update_card(conn, actor, card_id, reject_nulls(body.model_dump(exclude_unset=True)))


@router.post("/cards/{card_id}/claim")
def claim(card_id: int, body: Claim, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    require(actor, "builder")
    return cards.claim(conn, actor, card_id, body.branch)


@router.post("/cards/{card_id}/review")
def review(card_id: int, body: Review, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    require(actor, "reviewer")
    return cards.review(conn, actor, card_id, body.verdict, body.notes, body.prUrl)


@router.post("/cards/{card_id}/events", status_code=201)
def event(card_id: int, body: Event, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return cards.add_event(conn, actor, card_id, body.type, body.message)


@router.post("/cards/{card_id}/comments", status_code=201)
def comment(card_id: int, body: Comment, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return cards.add_comment(conn, actor, card_id, body.body)


@router.get("/cards/{card_id}/comments")
def comments(card_id: int, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    cards.get_row(conn, card_id)
    return cards.list_comments(conn, card_id)


@router.get("/activity")
def activity(
    since: str | None = None,
    card: int | None = None,
    actor: str = Depends(current_actor),
    conn: sqlite3.Connection = Depends(get_conn),
):
    return cards.list_activity(conn, since, card)


@router.get("/settings/night")
def get_night(actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return night.get(conn)


@router.put("/settings/night")
def put_night(body: NightSettings, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    require(actor, "oliver")
    return night.put(conn, actor, body.model_dump())


@router.get("/epics")
def list_epics(actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return epics.list_epics(conn)


@router.get("/epics/{epic_id}")
def get_epic(epic_id: int, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    return epics.with_cards(conn, epic_id)


@router.post("/epics", status_code=201)
def create_epic(body: EpicCreate, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    require(actor, "oliver", "groomer")
    return epics.create_epic(conn, actor, body.model_dump())


@router.patch("/epics/{epic_id}")
def update_epic(
    epic_id: int, body: EpicPatch, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)
):
    require(actor, "oliver")
    return epics.update_epic(conn, actor, epic_id, reject_nulls(body.model_dump(exclude_unset=True)))


@router.post("/epics/{epic_id}/split", status_code=202)
def split_epic(epic_id: int, request: Request, actor: str = Depends(current_actor)):
    """Ask the groomer to draft this epic's cards into backlog. Poll GET for `split` progress."""
    require(actor, "oliver")
    return splitter.start(request.app.state.connect, epic_id, actor)


@router.get("/epics/{epic_id}/split")
def split_status(epic_id: int, actor: str = Depends(current_actor), conn: sqlite3.Connection = Depends(get_conn)):
    epics.get_row(conn, epic_id)
    return splitter.status(conn, epic_id) or {"state": "never"}
