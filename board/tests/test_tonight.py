"""The Tonight page: nights the runner reports, their log, card steps and screenshots."""

import pytest

from conftest import auth

PNG = b"\x89PNG\r\n\x1a\n" + b"\x00" * 32


def start(client, **budget):
    body = {"pointsPerNight": 4, "maxCards": 3, "stopTime": "06:00", "host": "OliversPI", **budget}
    r = client.post("/api/nights", json=body, headers=auth("builder"))
    assert r.status_code == 201, r.text
    return r.json()


def log(client, night_id, *lines):
    return client.post(f"/api/nights/{night_id}/log", json={"lines": list(lines)}, headers=auth("builder"))


def event(client, card_id, type, message=""):
    client.post(f"/api/cards/{card_id}/events", json={"type": type, "message": message}, headers=auth("builder"))


def build_card(client, card_id, *, pr=True, ci="ci_passed", verdict="PASS"):
    """Walk a card through what runner/nightly.sh posts for it."""
    client.post(f"/api/cards/{card_id}/claim", json={"branch": f"night/card-{card_id}"}, headers=auth("builder"))
    event(client, card_id, "builder_started")
    if pr:
        client.patch(f"/api/cards/{card_id}", json={"status": "verifying", "prUrl": "https://github.com/o/r/pull/5"},
                     headers=auth("builder"))
        event(client, card_id, "ci_started")
        if ci:
            event(client, card_id, ci)
            event(client, card_id, "reviewer_started")
    if verdict:
        client.post(f"/api/cards/{card_id}/review", json={"verdict": verdict, "notes": f"VERDICT: {verdict}"},
                    headers=auth("reviewer"))


def steps(night_json, index=0):
    return {s["key"]: s["state"] for s in night_json["cards"][index]["steps"]}


# ---- API


def test_start_log_and_finish_a_night(client):
    night = start(client)
    assert night["status"] == "running" and night["host"] == "OliversPI"
    assert night["budget"] == {"pointsPerNight": 4, "maxCards": 3, "stopTime": "06:00"}
    assert log(client, night["id"], "[01:00:00] Tonight: 4 points", "[01:00:01] Card 1").json()["added"] == 2
    assert log(client, night["id"]).json()["added"] == 0  # heartbeat

    r = client.post(f"/api/nights/{night['id']}/finish", json={"status": "finished"}, headers=auth("builder"))
    assert r.json()["status"] == "finished" and r.json()["finishedAt"]
    body = client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert [l["line"] for l in body["log"]] == ["[01:00:00] Tonight: 4 points", "[01:00:01] Card 1"]
    assert client.get("/api/nights", headers=auth("oliver")).json()[0]["id"] == night["id"]


@pytest.mark.parametrize("actor", ["oliver", "reviewer", "groomer"])
def test_only_the_runner_reports_nights(client, actor):
    assert client.post("/api/nights", json={}, headers=auth(actor)).status_code == 403
    night = start(client)
    assert log(client, night["id"]).status_code == 200
    r = client.post(f"/api/nights/{night['id']}/log", json={"lines": ["x"]}, headers=auth(actor))
    assert r.status_code == 403


def test_unknown_night_is_404(client):
    assert log(client, 99, "x").status_code == 404
    assert client.get("/api/nights/99", headers=auth("oliver")).status_code == 404


def test_long_lines_are_trimmed(client):
    night = start(client)
    log(client, night["id"], "x" * 5000)
    body = client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert len(body["log"][0]["line"]) == 2000


def test_a_quiet_running_night_is_flagged(client, db_path):
    import sqlite3

    night = start(client)
    assert night["quiet"] is False
    conn = sqlite3.connect(db_path)
    conn.execute("UPDATE nights SET lastSeenAt = '2020-01-01T00:00:00Z'")
    conn.commit()
    assert client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()["quiet"] is True


# ---- card steps


def test_card_steps_follow_the_runner(client, make_card):
    card = make_card(status="ready")
    night = start(client)
    client.post(f"/api/cards/{card['id']}/claim", json={"branch": "night/card-1"}, headers=auth("builder"))
    get = lambda: client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert steps(get()) == {"build": "active", "pr": "pending", "ci": "pending", "review": "pending"}

    client.patch(f"/api/cards/{card['id']}", json={"status": "verifying", "prUrl": "https://x/pull/1"},
                 headers=auth("builder"))
    event(client, card["id"], "ci_started")
    assert steps(get()) == {"build": "done", "pr": "done", "ci": "active", "review": "pending"}

    event(client, card["id"], "ci_failed")
    event(client, card["id"], "reviewer_started")
    assert steps(get()) == {"build": "done", "pr": "done", "ci": "fail", "review": "active"}

    client.post(f"/api/cards/{card['id']}/review", json={"verdict": "FAIL", "notes": "no test"},
                headers=auth("reviewer"))
    body = get()
    assert steps(body) == {"build": "done", "pr": "done", "ci": "fail", "review": "fail"}
    assert body["cards"][0]["verdict"] == "FAIL" and body["cards"][0]["review"] == "no test"
    assert body["cards"][0]["prUrl"] == "https://x/pull/1"


def test_builder_without_a_pr(client, make_card):
    card = make_card(status="ready")
    night = start(client)
    build_card(client, card["id"], pr=False, verdict="BLOCKED")
    body = client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert steps(body) == {"build": "fail", "pr": "fail", "ci": "pending", "review": "pending"}


def test_unfinished_steps_show_stopped_after_the_night_ends(client, make_card):
    card = make_card(status="ready")
    night = start(client)
    client.post(f"/api/cards/{card['id']}/claim", json={"branch": "night/card-1"}, headers=auth("builder"))
    client.post(f"/api/nights/{night['id']}/finish", json={"status": "failed"}, headers=auth("builder"))
    body = client.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert steps(body)["build"] == "stopped"


def test_only_cards_claimed_that_night_are_listed(client, make_card):
    first, second = make_card(title="first", status="ready"), make_card(title="second", status="ready")
    one = start(client)
    build_card(client, first["id"])
    client.post(f"/api/nights/{one['id']}/finish", json={"status": "finished"}, headers=auth("builder"))
    event(client, first["id"], "comment-ish")  # after the night: ignored
    two = start(client)
    build_card(client, second["id"])
    titles = lambda n: [c["title"] for c in client.get(f"/api/nights/{n}", headers=auth("oliver")).json()["cards"]]
    assert titles(one["id"]) == ["first"]
    assert titles(two["id"]) == ["second"]


# ---- screenshots


def upload(client, card_id, name="harbor-town/overview.png", night=None, data=PNG, actor="builder"):
    params = {"name": name, **({"night": night} if night else {})}
    return client.post(f"/api/cards/{card_id}/screenshots", params=params, content=data,
                       headers={**auth(actor), "Content-Type": "image/png"})


def test_screenshots_upload_and_show(browser, make_card):
    card = make_card(status="ready")
    night = start(browser)
    build_card(browser, card["id"])
    r = upload(browser, card["id"], night=night["id"])
    assert r.status_code == 201, r.text
    shot = r.json()

    body = browser.get(f"/api/nights/{night['id']}", headers=auth("oliver")).json()
    assert [s["name"] for s in body["cards"][0]["shots"]] == ["harbor-town/overview.png"]
    img = browser.get(f"/shots/{shot['id']}.png")
    assert img.status_code == 200 and img.content == PNG and img.headers["content-type"] == "image/png"
    assert f"/shots/{shot['id']}.png" in browser.get("/tonight").text
    assert f"/shots/{shot['id']}.png" in browser.get(f"/cards/{card['id']}").text


@pytest.mark.parametrize(
    "kwargs, code",
    [
        ({"data": b"GIF89a not a png"}, 422),
        ({"name": "../../etc/passwd;"}, 422),
        ({"actor": "reviewer"}, 403),
        ({"night": 99}, 404),
    ],
)
def test_bad_screenshots_are_refused(client, make_card, kwargs, code):
    card = make_card()
    assert upload(client, card["id"], **kwargs).status_code == code


def test_screenshots_need_login(client, make_card):
    card = make_card()
    shot = upload(client, card["id"]).json()
    assert client.get(f"/shots/{shot['id']}.png", follow_redirects=False).status_code == 303


# ---- pages


def test_tonight_with_no_nights(browser):
    r = browser.get("/tonight")
    assert r.status_code == 200 and "No night shift has reported in yet" in r.text


def test_tonight_shows_the_live_night(browser, make_card):
    card = make_card(title="Bog slimes", status="ready")
    night = start(browser)
    log(browser, night["id"], "[01:02:03] builder → Edit game/Assets/_Project/Data/Enemies/bog-slime.asset")
    browser.post(f"/api/cards/{card['id']}/claim", json={"branch": "night/card-1"}, headers=auth("builder"))
    event(browser, card["id"], "builder_started")

    page = browser.get("/tonight").text
    assert "Night 1" in page and "● live" in page and "Bog slimes" in page
    assert "bog-slime.asset" in page
    assert "step-active" in page
    live = browser.get(f"/nights/{night['id']}/live")
    assert live.status_code == 200 and "<html" not in live.text and "bog-slime.asset" in live.text

    board = browser.get("/").text
    assert "Night shift running" in board and "Building “Bog slimes”" in board


def test_finished_night_page_and_no_banner(browser, make_card):
    card = make_card(status="ready")
    night = start(browser)
    build_card(browser, card["id"])
    browser.post(f"/api/nights/{night['id']}/finish", json={"status": "finished", "message": "Good night."},
                 headers=auth("builder"))
    start(browser)
    page = browser.get(f"/nights/{night['id']}").text
    assert "PASS" in page and "Good night." in page and "Earlier nights" in page
    assert "https://github.com/o/r/pull/5" in page
    assert browser.get("/nights/99").status_code == 404


def test_tonight_needs_login(client):
    assert client.get("/tonight", follow_redirects=False).status_code == 303
    assert client.get("/nights/1/live", follow_redirects=False).status_code == 303
