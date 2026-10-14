import pytest

from conftest import auth


def claim(client, card_id, branch="night/card-1", actor="builder"):
    return client.post(f"/api/cards/{card_id}/claim", json={"branch": branch}, headers=auth(actor))


def review(client, card_id, verdict, notes="", actor="reviewer", **extra):
    body = {"verdict": verdict, "notes": notes, **extra}
    return client.post(f"/api/cards/{card_id}/review", json=body, headers=auth(actor))


def ready(client):
    return [(c["title"], c["position"]) for c in client.get("/api/cards?status=ready", headers=auth("oliver")).json()]


# ---- claim


def test_claim_moves_to_building_and_counts_attempts(client, make_card):
    card = make_card(status="ready")
    r = claim(client, card["id"], "night/card-7")
    assert r.status_code == 200
    assert r.json()["status"] == "building"
    assert r.json()["branch"] == "night/card-7"
    assert r.json()["attempts"] == 1


def test_claim_needs_a_ready_card(client, make_card):
    card = make_card(status="backlog")
    assert claim(client, card["id"]).status_code == 409
    assert claim(client, 999).status_code == 404


@pytest.mark.parametrize("actor", ["oliver", "reviewer", "groomer"])
def test_only_builder_claims(client, make_card, actor):
    card = make_card(status="ready")
    assert claim(client, card["id"], actor=actor).status_code == 403


# ---- review rules


def test_pass_moves_to_review_and_resets_fails(client, make_card):
    card = make_card(status="ready")
    claim(client, card["id"])
    review(client, card["id"], "FAIL", "nope")
    claim(client, card["id"])
    r = review(client, card["id"], "PASS", "VERDICT: PASS", prUrl="https://x/pull/9")
    assert r.status_code == 200
    body = r.json()
    assert body["status"] == "review"
    assert body["consecutiveFails"] == 0
    assert body["lastReview"] == "VERDICT: PASS"
    assert body["prUrl"] == "https://x/pull/9"
    assert body["attempts"] == 2


def test_fail_puts_card_back_at_top_of_ready(client, make_card):
    card = make_card(title="tried", status="ready")
    claim(client, card["id"])
    make_card(title="other-1", status="ready")
    make_card(title="other-2", status="ready")
    r = review(client, card["id"], "FAIL", "VERDICT: FAIL\nno test")
    assert r.json()["status"] == "ready"
    assert r.json()["consecutiveFails"] == 1
    assert r.json()["lastReview"] == "VERDICT: FAIL\nno test"
    assert ready(client) == [("tried", 1), ("other-1", 2), ("other-2", 3)]
    assert r.json()["branch"] == "night/card-1"  # retried on the same branch


def test_second_fail_in_a_row_blocks(client, make_card):
    card = make_card(status="ready")
    claim(client, card["id"])
    review(client, card["id"], "FAIL")
    claim(client, card["id"])
    r = review(client, card["id"], "FAIL")
    assert r.json()["status"] == "blocked"
    assert r.json()["consecutiveFails"] == 2


def test_blocked_verdict_blocks(client, make_card):
    card = make_card(status="ready")
    claim(client, card["id"])
    assert review(client, card["id"], "BLOCKED", "needs a scene edit").json()["status"] == "blocked"


def test_review_works_from_verifying(client, make_card):
    card = make_card(status="ready")
    claim(client, card["id"])
    client.patch(f"/api/cards/{card['id']}", json={"status": "verifying"}, headers=auth("builder"))
    assert review(client, card["id"], "PASS").json()["status"] == "review"


def test_review_needs_a_card_in_progress(client, make_card):
    card = make_card(status="ready")
    assert review(client, card["id"], "PASS").status_code == 409


def test_review_rejects_unknown_verdict(client, make_card):
    card = make_card(status="ready")
    claim(client, card["id"])
    assert review(client, card["id"], "MAYBE").status_code == 422


@pytest.mark.parametrize("actor", ["oliver", "builder", "groomer"])
def test_only_reviewer_reviews(client, make_card, actor):
    card = make_card(status="ready")
    claim(client, card["id"])
    assert review(client, card["id"], "PASS", actor=actor).status_code == 403


# ---- events, comments, activity


@pytest.mark.parametrize("actor", ["oliver", "builder", "reviewer", "groomer"])
def test_anyone_can_post_events_and_comments(client, make_card, actor):
    card = make_card()
    r = client.post(f"/api/cards/{card['id']}/events", json={"type": "ci.failed", "message": "red"}, headers=auth(actor))
    assert r.status_code == 201
    r = client.post(f"/api/cards/{card['id']}/comments", json={"body": "hi"}, headers=auth(actor))
    assert r.status_code == 201 and r.json()["author"] == actor
    comments = client.get(f"/api/cards/{card['id']}/comments", headers=auth("oliver")).json()
    assert [c["body"] for c in comments] == ["hi"]


def test_events_and_comments_need_a_card(client):
    assert client.post("/api/cards/9/events", json={"type": "x"}, headers=auth("builder")).status_code == 404
    assert client.post("/api/cards/9/comments", json={"body": "x"}, headers=auth("builder")).status_code == 404
    assert client.post("/api/cards/9/comments", json={"body": ""}, headers=auth("builder")).status_code == 422


def test_activity_filters_by_card_and_since(client, make_card):
    a = make_card()
    b = make_card()
    client.post(f"/api/cards/{a['id']}/events", json={"type": "note", "message": "on a"}, headers=auth("builder"))
    log = client.get(f"/api/activity?card={a['id']}", headers=auth("reviewer")).json()
    assert [(e["actor"], e["type"]) for e in log] == [("oliver", "card.created"), ("builder", "note")]
    assert all(e["cardId"] == a["id"] for e in log)

    everything = client.get("/api/activity", headers=auth("groomer")).json()
    assert len(everything) == 3 and everything[1]["cardId"] == b["id"]
    assert client.get("/api/activity?since=2999-01-01", headers=auth("oliver")).json() == []
    assert len(client.get("/api/activity?since=2000-01-01", headers=auth("oliver")).json()) == 3


# ---- night settings


def test_night_settings_have_defaults(client):
    r = client.get("/api/settings/night", headers=auth("builder"))
    assert r.json() == {"pointsPerNight": 4, "maxCards": 3, "stopTime": "06:00", "paused": False}


def test_oliver_changes_night_settings(client):
    new = {"pointsPerNight": 6, "maxCards": 5, "stopTime": "05:30", "paused": True}
    assert client.put("/api/settings/night", json=new, headers=auth("oliver")).json() == new
    assert client.get("/api/settings/night", headers=auth("builder")).json() == new
    log = client.get("/api/activity", headers=auth("oliver")).json()
    assert log[-1]["type"] == "settings.night" and log[-1]["data"]["paused"] == {"from": False, "to": True}


@pytest.mark.parametrize("actor", ["builder", "reviewer", "groomer"])
def test_only_oliver_changes_night_settings(client, actor):
    new = {"pointsPerNight": 6, "maxCards": 5, "stopTime": "05:30", "paused": True}
    assert client.put("/api/settings/night", json=new, headers=auth(actor)).status_code == 403


@pytest.mark.parametrize("stop", ["25:00", "6am", "6:00"])
def test_stop_time_must_be_hh_mm(client, stop):
    new = {"pointsPerNight": 6, "maxCards": 5, "stopTime": stop, "paused": False}
    assert client.put("/api/settings/night", json=new, headers=auth("oliver")).status_code == 422


# ---- the calls runner/nightly.sh makes, end to end


def test_a_full_night_for_one_card(client, make_card):
    card = make_card(title="Add bog slimes", status="ready")
    nxt = client.get("/api/cards/next", headers=auth("builder")).json()
    assert nxt["id"] == card["id"]
    claim(client, nxt["id"], "night/card-1")
    client.patch(
        f"/api/cards/{nxt['id']}", json={"status": "verifying", "prUrl": "https://x/pull/3"}, headers=auth("builder")
    )
    assert review(client, nxt["id"], "PASS", "VERDICT: PASS").json()["status"] == "review"
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204
    types = [e["type"] for e in client.get(f"/api/activity?card={card['id']}", headers=auth("oliver")).json()]
    assert types == ["card.created", "card.claimed", "card.updated", "card.reviewed"]
