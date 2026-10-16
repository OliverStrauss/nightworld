import re

import pytest

from conftest import auth

PAGES = ["/", "/cards/new", "/cards/1", "/cards/1/edit", "/settings", "/epics", "/epics/1", "/activity"]


@pytest.fixture
def seeded(client, make_card):
    client.post("/api/epics", json={"slug": "fishing", "title": "Fishing minigame"}, headers=auth("oliver"))
    make_card(title="Bog slimes", status="ready", epicId=1, criteria=["slimes exist"], goal="Squishy.")
    client.post("/api/cards/1/comments", json={"body": "looks fun"}, headers=auth("reviewer"))


# ---- login (phase 3)


@pytest.mark.parametrize("path", PAGES)
def test_pages_redirect_to_login_when_logged_out(client, seeded, path):
    r = client.get(path, follow_redirects=False)
    assert r.status_code == 303
    assert r.headers["location"].startswith("/login?next=")


@pytest.mark.parametrize("path", PAGES)
def test_pages_load_when_logged_in(browser, seeded, path):
    r = browser.get(path)
    assert r.status_code == 200, r.text
    assert "<html" in r.text


def test_login_page_and_wrong_password(client):
    assert client.get("/login").status_code == 200
    r = client.post("/login", data={"password": "nope"}, follow_redirects=False)
    assert r.status_code == 401 and "board_session" not in r.cookies


def test_login_redirects_back_but_only_inside_the_board(client):
    from conftest import PASSWORD

    r = client.post("/login", data={"password": PASSWORD, "next": "/settings"}, follow_redirects=False)
    assert r.headers["location"] == "/settings"
    r = client.post("/login", data={"password": PASSWORD, "next": "//evil.example"}, follow_redirects=False)
    assert r.headers["location"] == "/"


def test_forged_cookie_is_rejected(client):
    client.cookies.set("board_session", "oliver.9999999999.deadbeef")
    assert client.get("/", follow_redirects=False).status_code == 303


def test_no_password_hash_means_no_login(client, monkeypatch):
    monkeypatch.delenv("BOARD_PASSWORD_HASH")
    from conftest import PASSWORD

    assert client.post("/login", data={"password": PASSWORD}, follow_redirects=False).status_code == 401


def test_logout(browser):
    browser.post("/logout")
    assert browser.get("/", follow_redirects=False).status_code == 303


def test_board_shows_cards_in_their_columns(browser, seeded):
    html = browser.get("/").text
    assert "Bog slimes" in html
    assert re.search(r'data-status="ready" data-sort="true">\s*<a class="tile', html)
    assert "Next to build" in html


def test_board_has_three_stages_with_runner_states_inside(browser, client, make_card):
    make_card(title="idea")
    make_card(title="queued", status="ready")
    make_card(title="cooking", status="building")
    make_card(title="wedged", status="blocked")
    make_card(title="merge me", status="review")
    make_card(title="shipped", status="done")
    html = browser.get("/").text
    assert html.count('class="stage ') == 3
    backlog, ready, done = (html.index(f"stage-{s}") for s in ("backlog", "ready", "done"))
    assert backlog < html.index("idea") < ready
    for title in ("queued", "cooking", "wedged"):
        assert ready < html.index(title) < done
    for title in ("merge me", "shipped"):
        assert done < html.index(title)
    assert "Building now" in html and "Stuck, needs you" in html and "Built, PR to merge" in html


def test_card_page_shows_details(browser, seeded):
    html = browser.get("/cards/1").text
    for text in ["Bog slimes", "Squishy.", "slimes exist", "looks fun", "Fishing minigame", "Created"]:
        assert text in html


def test_manifest_and_icons_are_served(client):
    manifest = client.get("/static/manifest.webmanifest")
    assert manifest.status_code == 200 and manifest.json()["display"] == "standalone"
    for icon in manifest.json()["icons"]:
        assert client.get(icon["src"]).headers["content-type"] == "image/png"


def test_pr_link_is_only_a_link_when_https(browser, client, make_card):
    make_card(prUrl="javascript:alert(1)")
    assert 'href="javascript:' not in browser.get("/cards/1").text


# ---- editing (phase 4)


def card(client, card_id):
    return client.get(f"/api/cards/{card_id}", headers=auth("oliver")).json()


def test_create_card_from_the_form(browser, client):
    r = browser.post(
        "/cards/new",
        data={
            "title": "Add a dock", "type": "content", "points": "3", "status": "ready",
            "goal": "Somewhere to fish.", "criteria": "- [ ] three crates\n\n* a rope\n", "notes": "", "dependsOn": "",
            "epicId": "",
        },
        follow_redirects=False,
    )
    assert r.status_code == 303 and r.headers["location"] == "/cards/1"
    c = card(client, 1)
    assert c["title"] == "Add a dock" and c["status"] == "ready" and c["points"] == 3
    assert c["criteria"] == ["three crates", "a rope"]
    log = client.get("/api/activity", headers=auth("oliver")).json()
    assert log[-1]["actor"] == "oliver" and log[-1]["type"] == "card.created"


def test_card_form_asks_for_points_not_size_or_effort(browser):
    html = browser.get("/cards/new").text
    assert 'name="points"' in html
    assert 'name="size"' not in html and 'name="effort"' not in html


def test_board_flags_a_card_bigger_than_a_whole_night(browser, make_card):
    make_card(title="fits", status="ready", points=4)  # default night: 4 points
    assert "bigger than a night" not in browser.get("/").text
    make_card(title="huge", status="ready", points=5)
    assert browser.get("/").text.count("bigger than a night") == 1


def test_create_form_shows_errors(browser):
    r = browser.post("/cards/new", data={"title": "", "type": "content", "points": "1", "dependsOn": "99"})
    assert r.status_code == 422 and 'class="error"' in r.text


def test_edit_card_from_the_form(browser, client, make_card):
    make_card()
    dep = make_card(title="dep")
    r = browser.post(
        "/cards/1/edit",
        data={
            "title": "Renamed", "type": "fix", "points": "2", "goal": "g", "criteria": "a\nb",
            "notes": "n", "dependsOn": f"#{dep['id']}", "epicId": "", "branch": "night/card-1", "prUrl": "",
        },
        follow_redirects=False,
    )
    assert r.status_code == 303
    c = card(client, 1)
    assert (c["title"], c["type"], c["points"], c["criteria"], c["dependsOn"], c["branch"]) == (
        "Renamed", "fix", 2, ["a", "b"], [dep["id"]], "night/card-1"
    )


def test_edit_form_rejects_a_dependency_cycle(browser, make_card):
    make_card()
    make_card(dependsOn=[1])
    r = browser.post("/cards/1/edit", data={"title": "x", "type": "fix", "points": "1", "dependsOn": "2"})
    assert r.status_code == 422 and "cycle" in r.text


def test_drag_to_reorder_ready(browser, client, make_card):
    for t in "abc":
        make_card(title=t, status="ready")
    r = browser.post("/cards/3/move", json={"status": "ready", "position": 1})
    assert r.status_code == 200
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [c["title"] for c in ready] == ["c", "a", "b"]
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "c"


def test_drag_between_columns(browser, client, make_card):
    make_card(status="blocked")
    r = browser.post("/cards/1/move", json={"status": "ready", "position": 1})
    assert r.status_code == 200 and card(client, 1)["status"] == "ready"
    r = browser.post("/cards/1/move", json={"status": "nope", "position": 1})
    assert r.status_code == 422


def test_move_form_on_card_page(browser, client, make_card):
    make_card(status="review")
    r = browser.post("/cards/1/move", data={"status": "done"}, follow_redirects=False)
    assert r.status_code == 303 and card(client, 1)["status"] == "done"


def test_move_form_unblocks_but_leaves_building_cards_alone(browser, client, make_card):
    make_card(status="blocked")
    make_card(status="building")
    browser.post("/cards/1/move", data={"status": "ready"})
    browser.post("/cards/2/move", data={"status": "ready"})
    assert card(client, 1)["status"] == "ready"
    assert card(client, 2)["status"] == "building"


def test_drop_into_done_without_position(browser, client, make_card):
    make_card(status="ready")
    assert browser.post("/cards/1/move", json={"status": "done"}).status_code == 200
    assert card(client, 1)["status"] == "done"


def test_move_needs_login(client, make_card):
    make_card()
    assert client.post("/cards/1/move", json={"status": "done", "position": 1}, follow_redirects=False).status_code == 303
    assert card(client, 1)["status"] == "backlog"


def test_comment_form(browser, client, make_card):
    make_card()
    browser.post("/cards/1/comments", data={"body": "  nice one  "})
    browser.post("/cards/1/comments", data={"body": "   "})
    comments = client.get("/api/cards/1/comments", headers=auth("oliver")).json()
    assert [(c["author"], c["body"]) for c in comments] == [("oliver", "nice one")]


def test_night_settings_form(browser, client):
    r = browser.post(
        "/settings", data={"pointsPerNight": "6", "maxCards": "2", "stopTime": "05:15", "paused": "on"},
        follow_redirects=False,
    )
    assert r.status_code == 303
    assert client.get("/api/settings/night", headers=auth("builder")).json() == {
        "pointsPerNight": 6, "maxCards": 2, "stopTime": "05:15", "paused": True
    }
    browser.post("/settings", data={"pointsPerNight": "6", "maxCards": "2", "stopTime": "05:15"})
    assert client.get("/api/settings/night", headers=auth("builder")).json()["paused"] is False
    r = browser.post("/settings", data={"pointsPerNight": "-1", "maxCards": "2", "stopTime": "05:15"})
    assert r.status_code == 422


def test_epic_forms(browser, client, make_card):
    r = browser.post("/epics", data={"title": "Boats", "slug": "boats", "pitch": "Row row row", "prUrl": ""},
                     follow_redirects=False)
    assert r.status_code == 303 and r.headers["location"] == "/epics/1"
    assert browser.post("/epics", data={"title": "Bad", "slug": "Bad Slug"}).status_code == 422

    make_card(title="oar", epicId=1)
    html = browser.get("/epics/1").text
    assert "oar" in html and "epic/boats" in html and "0 of 1 cards done" in html

    browser.post("/epics/1/edit", data={"title": "Boats!", "pitch": "", "status": "merged", "prUrl": ""})
    epic = client.get("/api/epics/1", headers=auth("oliver")).json()
    assert epic["title"] == "Boats!" and epic["status"] == "merged"


def test_delete_button_on_card_page(browser, client, make_card):
    client.post("/api/epics", json={"slug": "boats", "title": "Boats"}, headers=auth("oliver"))
    make_card(title="loose")
    make_card(title="oar", epicId=1)
    assert "🗑 Delete" in browser.get("/cards/1").text
    r = browser.post("/cards/1/delete", follow_redirects=False)
    assert r.status_code == 303 and r.headers["location"] == "/"
    r = browser.post("/cards/2/delete", follow_redirects=False)
    assert r.headers["location"] == "/epics/1"
    assert client.get("/api/cards", headers=auth("oliver")).json() == []


def test_delete_needs_login(client, make_card):
    make_card()
    assert client.post("/cards/1/delete", follow_redirects=False).status_code == 303
    assert client.get("/api/cards/1", headers=auth("oliver")).status_code == 200


def test_backlog_groups_cards_by_epic_in_build_order(browser, client, make_card):
    client.post("/api/epics", json={"slug": "player", "title": "Player"}, headers=auth("oliver"))
    run = make_card(title="Run", epicId=1)
    make_card(title="Loose idea")
    make_card(title="Walk", epicId=1)
    client.patch(f"/api/cards/{run['id']}", json={"dependsOn": [3]}, headers=auth("oliver"))
    make_card(title="Jump", epicId=1, status="ready")  # not in backlog: not in the box

    html = browser.get("/").text
    box = html[html.index('class="epic-group"'):html.index("No epic")]
    assert "⛵ Player" in box and "2 cards, in build order" in box
    assert box.index("Walk") < box.index("Run")  # Run depends on Walk
    assert "Jump" not in box and "Loose idea" not in box
    assert html.index("No epic") < html.index("Loose idea") < html.index("stage-ready")


def test_backlog_without_epics_has_no_group_heading(browser, make_card):
    make_card(title="Loose idea")
    html = browser.get("/").text
    assert 'class="epic-group"' not in html and "No epic" not in html


def test_all_to_ready_moves_an_epics_backlog_in_build_order(browser, client, make_card):
    client.post("/api/epics", json={"slug": "player", "title": "Player"}, headers=auth("oliver"))
    make_card(title="Already ready", status="ready")
    run = make_card(title="Run", epicId=1)
    make_card(title="Walk", epicId=1)
    make_card(title="Loose idea")
    client.patch(f"/api/cards/{run['id']}", json={"dependsOn": [3]}, headers=auth("oliver"))

    r = browser.post("/epics/1/ready", follow_redirects=False)
    assert r.status_code == 303
    ready = client.get("/api/cards?status=ready", headers=auth("oliver")).json()
    assert [c["title"] for c in ready] == ["Already ready", "Walk", "Run"]
    assert [c["title"] for c in client.get("/api/cards?status=backlog", headers=auth("oliver")).json()] == ["Loose idea"]


def test_card_page_has_one_tap_move_buttons(browser, make_card):
    make_card(status="blocked")
    make_card(status="ready")
    stuck = browser.get("/cards/1").text
    assert re.search(r'<button name="status" value="ready" class="retry"\s*>↻ Back to Ready</button>', stuck)
    ready = browser.get("/cards/2").text
    assert re.search(r'<button name="status" value="ready" class="here" disabled>Ready</button>', ready)
    assert re.search(r'<button name="status" value="done" class="ghost"\s*>Done</button>', ready)
