import json
import sqlite3

import pytest

from app.db import SCHEMA, connect, init_db
from conftest import auth

ALL_TABLES = {"schema_version", "epics", "cards", "card_deps", "comments", "activity", "settings",
              "nights", "night_log", "screenshots"}


def activity(db_path):
    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row
    rows = [dict(r) for r in conn.execute("SELECT * FROM activity ORDER BY id")]
    conn.close()
    return rows


# ---- start-up and auth


def test_start_creates_all_tables(client, db_path):
    conn = sqlite3.connect(db_path)
    tables = {r[0] for r in conn.execute("SELECT name FROM sqlite_master WHERE type = 'table'")}
    assert ALL_TABLES <= tables
    assert conn.execute("SELECT version FROM schema_version").fetchall() == [(3,)]


def test_start_turns_old_size_and_effort_into_points(tmp_path):
    path = str(tmp_path / "old.db")
    old = sqlite3.connect(path)
    # A version-1 board: the same schema, but cards had size and effort instead of points.
    old.executescript(SCHEMA.replace("points           INTEGER NOT NULL DEFAULT 1,", "size TEXT NOT NULL, effort TEXT NOT NULL,"))
    old.executescript("""
        INSERT INTO schema_version VALUES (1);
        INSERT INTO cards (title, type, size, effort, status, position, createdAt, updatedAt) VALUES
            ('low', 'fix', 'S', 'low', 'ready', 1, 'x', 'x'),
            ('medium', 'fix', 'M', 'medium', 'ready', 2, 'x', 'x'),
            ('high', 'fix', 'M', 'high', 'ready', 3, 'x', 'x');
    """)
    old.close()

    conn = connect(path)
    init_db(conn)
    init_db(conn)  # the next start leaves it alone
    assert [tuple(r) for r in conn.execute("SELECT title, points FROM cards ORDER BY id")] == [
        ("low", 1), ("medium", 2), ("high", 4)
    ]
    columns = {r["name"] for r in conn.execute("PRAGMA table_info(cards)")}
    assert "points" in columns and not columns & {"size", "effort"}
    assert conn.execute("SELECT version FROM schema_version").fetchone()[0] == 3


def test_start_adds_night_tables_to_a_v2_board(tmp_path):
    path = str(tmp_path / "v2.db")
    old = sqlite3.connect(path)
    old.executescript(SCHEMA.split("-- v3:")[0] + "INSERT INTO schema_version VALUES (2);")
    old.close()
    conn = connect(path)
    init_db(conn)
    tables = {r[0] for r in conn.execute("SELECT name FROM sqlite_master WHERE type = 'table'")}
    assert {"nights", "night_log", "screenshots"} <= tables
    assert conn.execute("SELECT version FROM schema_version").fetchone()[0] == 3


@pytest.mark.parametrize(
    "headers",
    [{}, {"Authorization": "Bearer nope"}, {"Authorization": "tok-oliver"}, {"Authorization": "Bearer "}],
)
def test_unknown_or_missing_token_is_401(client, headers):
    assert client.get("/api/cards", headers=headers).status_code == 401


def test_actor_without_configured_token_cannot_log_in(client, monkeypatch):
    monkeypatch.setenv("BOARD_TOKEN_GROOMER", "")
    assert client.get("/api/cards", headers={"Authorization": "Bearer "}).status_code == 401
    assert client.get("/api/cards", headers=auth("groomer")).status_code == 401


# ---- GET /api/cards and /api/cards/:id


@pytest.mark.parametrize("actor", ["oliver", "builder", "reviewer", "groomer"])
def test_everyone_can_read(client, make_card, actor):
    card = make_card(title="Bog slimes")
    assert client.get("/api/cards", headers=auth(actor)).json()[0]["title"] == "Bog slimes"
    assert client.get(f"/api/cards/{card['id']}", headers=auth(actor)).json() == card


def test_card_has_every_contract_field(make_card):
    card = make_card(points=2, goal="Slimes!", criteria=["a", "b"], notes="n")
    assert set(card) == {
        "id", "title", "type", "points", "epicId", "mergedToEpic", "status", "position",
        "goal", "criteria", "notes", "dependsOn", "branch", "prUrl", "attempts",
        "consecutiveFails", "lastReview", "createdAt", "updatedAt",
    }
    assert card["status"] == "backlog"
    assert card["points"] == 2
    assert card["criteria"] == ["a", "b"]
    assert card["mergedToEpic"] is False
    assert card["attempts"] == 0


def test_list_is_ordered_by_status_then_position(client, make_card):
    b1 = make_card(title="b1")
    r1 = make_card(title="r1", status="ready")
    r2 = make_card(title="r2", status="ready")
    r0 = make_card(title="r0", status="ready", position=1)
    titles = [c["title"] for c in client.get("/api/cards", headers=auth("oliver")).json()]
    assert titles == ["b1", "r0", "r1", "r2"]
    assert b1["position"] == 1 and r1["position"] == 1 and r2["position"] == 2 and r0["position"] == 1


def test_list_filters_by_status(client, make_card):
    make_card(title="b")
    make_card(title="r", status="ready")
    cards = client.get("/api/cards?status=ready", headers=auth("reviewer")).json()
    assert [c["title"] for c in cards] == ["r"]
    assert client.get("/api/cards?status=nope", headers=auth("oliver")).status_code == 422


def test_get_missing_card_is_404(client):
    assert client.get("/api/cards/999", headers=auth("oliver")).status_code == 404


# ---- POST /api/cards


def test_oliver_can_create_in_any_status(client):
    r = client.post(
        "/api/cards",
        json={"title": "t", "type": "fix", "status": "ready", "points": 4},
        headers=auth("oliver"),
    )
    assert r.status_code == 201
    assert r.json()["status"] == "ready" and r.json()["points"] == 4


def test_groomer_can_create_in_backlog_only(client):
    body = {"title": "t", "type": "chore"}
    assert client.post("/api/cards", json=body, headers=auth("groomer")).status_code == 201
    r = client.post("/api/cards", json={**body, "status": "backlog"}, headers=auth("groomer"))
    assert r.status_code == 201
    r = client.post("/api/cards", json={**body, "status": "ready"}, headers=auth("groomer"))
    assert r.status_code == 403


@pytest.mark.parametrize("actor", ["builder", "reviewer"])
def test_builder_and_reviewer_cannot_create(client, actor):
    r = client.post("/api/cards", json={"title": "t", "type": "fix"}, headers=auth(actor))
    assert r.status_code == 403


@pytest.mark.parametrize(
    "body",
    [
        {"type": "fix"},
        {"title": "", "type": "fix"},
        {"title": "t", "type": "feature"},
        {"title": "t", "type": "fix", "size": "S"},
        {"title": "t", "type": "fix", "effort": "low"},
        {"title": "t", "type": "fix", "points": 0},
        {"title": "t", "type": "fix", "points": "lots"},
        {"title": "t", "type": "fix", "attempts": 3},
        {"title": "t", "type": "fix", "dependsOn": [999]},
    ],
)
def test_invalid_create_is_422(client, body):
    assert client.post("/api/cards", json=body, headers=auth("oliver")).status_code == 422


# ---- PATCH /api/cards/:id


def test_oliver_can_edit_fields_and_move(client, make_card):
    card = make_card()
    r = client.patch(
        f"/api/cards/{card['id']}",
        json={"title": "New", "criteria": ["x"], "status": "done", "prUrl": "https://x/pull/1"},
        headers=auth("oliver"),
    )
    assert r.status_code == 200
    assert r.json()["title"] == "New" and r.json()["status"] == "done"
    assert r.json()["criteria"] == ["x"]


def test_oliver_can_move_a_card_out_of_blocked(client, make_card):
    card = make_card(status="blocked")
    r = client.patch(f"/api/cards/{card['id']}", json={"status": "ready"}, headers=auth("oliver"))
    assert r.json()["status"] == "ready"


def test_builder_may_set_verifying_pr_url_and_merged_to_epic(client, make_card):
    card = make_card(status="building")
    r = client.patch(
        f"/api/cards/{card['id']}",
        json={"status": "verifying", "prUrl": "https://x/pull/2", "mergedToEpic": True},
        headers=auth("builder"),
    )
    assert r.status_code == 200
    assert r.json()["status"] == "verifying"
    assert r.json()["prUrl"] == "https://x/pull/2"
    assert r.json()["mergedToEpic"] is True


@pytest.mark.parametrize(
    "body",
    [{"status": "done"}, {"status": "review"}, {"status": "ready"}, {"title": "x"}, {"position": 1}],
)
def test_builder_may_not_change_anything_else(client, make_card, body):
    card = make_card(status="building")
    assert client.patch(f"/api/cards/{card['id']}", json=body, headers=auth("builder")).status_code == 403


@pytest.mark.parametrize("actor", ["reviewer", "groomer"])
def test_reviewer_and_groomer_cannot_patch(client, make_card, actor):
    card = make_card()
    assert client.patch(f"/api/cards/{card['id']}", json={"notes": "x"}, headers=auth(actor)).status_code == 403


def test_patch_validation(client, make_card):
    card = make_card()
    url = f"/api/cards/{card['id']}"
    assert client.patch(url, json={"title": None}, headers=auth("oliver")).status_code == 422
    assert client.patch(url, json={"status": "nope"}, headers=auth("oliver")).status_code == 422
    assert client.patch(url, json={"attempts": 5}, headers=auth("oliver")).status_code == 422
    assert client.patch(url, json={"dependsOn": [card["id"]]}, headers=auth("oliver")).status_code == 422
    assert client.patch("/api/cards/999", json={"notes": "x"}, headers=auth("oliver")).status_code == 404
    # nullable fields can be cleared
    client.patch(url, json={"prUrl": "https://x"}, headers=auth("oliver"))
    assert client.patch(url, json={"prUrl": None}, headers=auth("oliver")).json()["prUrl"] is None


def test_dependency_cycle_is_rejected(client, make_card):
    a = make_card()
    b = make_card(dependsOn=[a["id"]])
    r = client.patch(f"/api/cards/{a['id']}", json={"dependsOn": [b["id"]]}, headers=auth("oliver"))
    assert r.status_code == 422


def test_reorder_ready_keeps_positions_contiguous(client, make_card):
    a, b, c = (make_card(title=t, status="ready") for t in "abc")
    client.patch(f"/api/cards/{c['id']}", json={"position": 1}, headers=auth("oliver"))
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [(x["title"], x["position"]) for x in ready] == [("c", 1), ("a", 2), ("b", 3)]

    # moving a card out of the column closes the gap; moving in without a position appends
    client.patch(f"/api/cards/{a['id']}", json={"status": "backlog"}, headers=auth("oliver"))
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [(x["title"], x["position"]) for x in ready] == [("c", 1), ("b", 2)]
    client.patch(f"/api/cards/{a['id']}", json={"status": "ready"}, headers=auth("oliver"))
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [(x["title"], x["position"]) for x in ready] == [("c", 1), ("b", 2), ("a", 3)]


def test_editing_other_fields_keeps_position(client, make_card):
    make_card(status="ready")
    b = make_card(status="ready")
    r = client.patch(f"/api/cards/{b['id']}", json={"notes": "hi"}, headers=auth("oliver"))
    assert r.json()["position"] == 2


# ---- GET /api/cards/next


def test_next_is_204_on_an_empty_board(client):
    r = client.get("/api/cards/next", headers=auth("builder"))
    assert r.status_code == 204 and r.content == b""


def test_next_is_204_when_nothing_is_ready(client, make_card):
    make_card(status="backlog")
    make_card(status="building")
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204


@pytest.mark.parametrize("actor", ["oliver", "reviewer", "groomer"])
def test_next_is_builder_only(client, actor):
    assert client.get("/api/cards/next", headers=auth(actor)).status_code == 403


def test_next_returns_top_ready_card(client, make_card):
    make_card(title="first", status="ready")
    make_card(title="second", status="ready")
    make_card(title="jumped", status="ready", position=1)
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "jumped"


def test_next_skips_cards_with_unmet_dependencies(client, make_card):
    dep = make_card(title="dep", status="review")
    make_card(title="waits", status="ready", dependsOn=[dep["id"]])
    make_card(title="free", status="ready")
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "free"


def test_next_is_204_when_every_ready_card_is_waiting(client, make_card):
    dep = make_card(title="dep", status="ready")
    make_card(title="waits", status="ready", dependsOn=[dep["id"]], position=1)
    client.patch(f"/api/cards/{dep['id']}", json={"status": "building"}, headers=auth("oliver"))
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204


def test_next_picks_card_once_dependencies_are_done(client, make_card):
    d1 = make_card(title="d1", status="review")
    d2 = make_card(title="d2", status="done")
    make_card(title="waits", status="ready", dependsOn=[d1["id"], d2["id"]])
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204
    client.patch(f"/api/cards/{d1['id']}", json={"status": "done"}, headers=auth("oliver"))
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "waits"


def test_next_stacks_on_a_dependency_that_passed_review(client, make_card):
    dep = make_card(title="dep", status="review", branch="night/card-1", prUrl="https://github.com/x/y/pull/1")
    done = make_card(title="done", status="done", branch="night/card-2")
    make_card(title="stacked", status="ready", dependsOn=[dep["id"], done["id"]])
    card = client.get("/api/cards/next", headers=auth("builder")).json()
    assert card["title"] == "stacked"
    assert card["stackOn"] == ["night/card-1"]


def test_next_has_an_empty_stack_when_dependencies_are_merged(client, make_card):
    make_card(title="free", status="ready")
    assert client.get("/api/cards/next", headers=auth("builder")).json()["stackOn"] == []


def test_next_still_waits_on_a_dependency_that_is_building(client, make_card):
    dep = make_card(title="dep", status="ready", branch="night/card-1")
    make_card(title="waits", status="ready", dependsOn=[dep["id"]], position=2)
    client.patch(f"/api/cards/{dep['id']}", json={"status": "verifying"}, headers=auth("oliver"))
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204


def test_next_only_returns_cards_that_fit_the_points_left(client, make_card):
    make_card(title="three", status="ready", points=3)
    make_card(title="six", status="ready", points=6)
    make_card(title="two", status="ready", points=2)

    def next_title(points):
        r = client.get(f"/api/cards/next?points={points}", headers=auth("builder"))
        return r.json()["title"] if r.status_code == 200 else r.status_code

    # A 5-point night: the three, then the two fits in what's left; the six never does.
    assert next_title(5) == "three"
    assert next_title(2) == "two"
    assert next_title(1) == 204
    assert next_title(-1) == 422


def test_next_uses_the_whole_night_by_default(client, make_card):
    make_card(title="big", status="ready", points=5)
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204  # default night: 4 points
    night = {"pointsPerNight": 5, "maxCards": 3, "stopTime": "06:00", "paused": False}
    client.put("/api/settings/night", json=night, headers=auth("oliver"))
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "big"


# ---- activity log


def test_every_write_is_logged_with_its_actor(client, make_card, db_path):
    client.post("/api/cards", json={"title": "g", "type": "chore"}, headers=auth("groomer"))
    card = make_card(status="building")
    client.patch(f"/api/cards/{card['id']}", json={"status": "verifying"}, headers=auth("builder"))
    client.patch(f"/api/cards/{card['id']}", json={"notes": "nice"}, headers=auth("oliver"))

    rows = activity(db_path)
    assert [(r["actor"], r["type"]) for r in rows] == [
        ("groomer", "card.created"),
        ("oliver", "card.created"),
        ("builder", "card.updated"),
        ("oliver", "card.updated"),
    ]
    assert rows[2]["cardId"] == card["id"]
    assert json.loads(rows[2]["data"])["status"] == {"from": "building", "to": "verifying"}
    assert json.loads(rows[3]["data"]) == {"notes": {"from": "", "to": "nice"}}


def test_reads_and_rejected_writes_are_not_logged(client, make_card, db_path):
    card = make_card()
    client.get("/api/cards", headers=auth("oliver"))
    client.get(f"/api/cards/{card['id']}", headers=auth("builder"))
    client.patch(f"/api/cards/{card['id']}", json={"title": "x"}, headers=auth("builder"))
    client.post("/api/cards", json={"title": "t", "type": "fix"}, headers=auth("reviewer"))
    assert len(activity(db_path)) == 1


# ---- DELETE /api/cards/:id


def test_oliver_deletes_a_card_and_everything_hanging_off_it(client, make_card, db_path):
    a = make_card(title="a", status="ready")
    b = make_card(title="b", status="ready", dependsOn=[a["id"]])
    c = make_card(title="c", status="ready")
    client.post(f"/api/cards/{a['id']}/comments", json={"body": "hi"}, headers=auth("reviewer"))

    assert client.delete(f"/api/cards/{a['id']}", headers=auth("oliver")).status_code == 204
    assert client.get(f"/api/cards/{a['id']}", headers=auth("oliver")).status_code == 404
    assert client.get(f"/api/cards/{b['id']}", headers=auth("oliver")).json()["dependsOn"] == []
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [(x["title"], x["position"]) for x in ready] == [("b", 1), ("c", 2)]
    log = activity(db_path)
    assert (log[-1]["actor"], log[-1]["type"], log[-1]["cardId"]) == ("oliver", "card.deleted", a["id"])
    assert client.delete(f"/api/cards/{a['id']}", headers=auth("oliver")).status_code == 404


@pytest.mark.parametrize("actor", ["builder", "reviewer", "groomer"])
def test_only_oliver_deletes_cards(client, make_card, actor):
    card = make_card()
    assert client.delete(f"/api/cards/{card['id']}", headers=auth(actor)).status_code == 403
