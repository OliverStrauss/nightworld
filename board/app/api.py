"""JSON endpoints under /api. Contract: board/README.md."""

import sqlite3
from typing import Literal

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, ConfigDict, Field

from . import cards
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
    branch: str | None = None
    prUrl: str | None = None
    mergedToEpic: bool | None = None


# Fields that may be cleared with an explicit null; everything else needs a value.
NULLABLE = {"branch", "prUrl"}


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
    changes = body.model_dump(exclude_unset=True)
    cleared = [k for k, v in changes.items() if v is None and k not in NULLABLE]
    if cleared:
        raise HTTPException(status_code=422, detail=f"{cleared} can't be null")
    return cards.update_card(conn, actor, card_id, changes)
