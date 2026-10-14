import pytest

from conftest import auth


@pytest.fixture
def make_epic(client):
    def make(slug="fishing", title="Fishing minigame", actor="oliver", **fields):
        r = client.post("/api/epics", json={"slug": slug, "title": title, **fields}, headers=auth(actor))
        assert r.status_code == 201, r.text
        return r.json()

    return make


def test_create_epic(make_epic):
    epic = make_epic(pitch="Cast a line, catch a boot.")
    assert epic["slug"] == "fishing"
    assert epic["branch"] == "epic/fishing"
    assert epic["status"] == "open"
    assert epic["cardIds"] == []


def test_groomer_may_create_but_not_edit(client, make_epic):
    epic = make_epic(actor="groomer")
    r = client.patch(f"/api/epics/{epic['id']}", json={"title": "x"}, headers=auth("groomer"))
    assert r.status_code == 403


@pytest.mark.parametrize("actor", ["builder", "reviewer"])
def test_builder_and_reviewer_cannot_create_epics(client, actor):
    r = client.post("/api/epics", json={"slug": "x", "title": "x"}, headers=auth(actor))
    assert r.status_code == 403


@pytest.mark.parametrize("slug", ["Fishing", "fishing minigame", "-fish", "fish--ing", ""])
def test_slug_must_be_kebab_case(client, slug):
    r = client.post("/api/epics", json={"slug": slug, "title": "x"}, headers=auth("oliver"))
    assert r.status_code == 422


def test_slug_is_unique(client, make_epic):
    make_epic()
    r = client.post("/api/epics", json={"slug": "fishing", "title": "again"}, headers=auth("oliver"))
    assert r.status_code == 422


def test_oliver_edits_epic(client, make_epic):
    epic = make_epic()
    r = client.patch(
        f"/api/epics/{epic['id']}", json={"status": "merged", "prUrl": "https://x/pull/30"}, headers=auth("oliver")
    )
    assert r.json()["status"] == "merged" and r.json()["prUrl"] == "https://x/pull/30"
    assert client.patch(f"/api/epics/{epic['id']}", json={"slug": "y"}, headers=auth("oliver")).status_code == 422
    assert client.patch("/api/epics/99", json={"title": "y"}, headers=auth("oliver")).status_code == 404


def test_epic_lists_its_cards_dependencies_first(client, make_card, make_epic):
    epic = make_epic()
    rod = make_card(title="rod", epicId=epic["id"])
    catch = make_card(title="catch", epicId=epic["id"])
    make_card(title="unrelated")
    client.patch(f"/api/cards/{rod['id']}", json={"dependsOn": [catch["id"]]}, headers=auth("oliver"))

    full = client.get(f"/api/epics/{epic['id']}", headers=auth("builder")).json()
    assert full["cardIds"] == [catch["id"], rod["id"]]
    assert [c["title"] for c in full["cards"]] == ["catch", "rod"]
    assert client.get("/api/epics", headers=auth("reviewer")).json()[0]["cardIds"] == full["cardIds"]


def test_card_epic_must_exist(client, make_card):
    r = client.post("/api/cards", json={"title": "t", "type": "fix", "size": "S", "epicId": 5}, headers=auth("oliver"))
    assert r.status_code == 422
    card = make_card()
    assert client.patch(f"/api/cards/{card['id']}", json={"epicId": 5}, headers=auth("oliver")).status_code == 422


# ---- /next with epics


def test_same_epic_dependency_is_met_once_merged_to_epic(client, make_card, make_epic):
    epic = make_epic()
    first = make_card(title="first", status="review", epicId=epic["id"])
    make_card(title="second", status="ready", epicId=epic["id"], dependsOn=[first["id"]])
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204

    client.patch(f"/api/cards/{first['id']}", json={"mergedToEpic": True}, headers=auth("builder"))
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "second"


def test_merged_to_another_epic_does_not_count(client, make_card, make_epic):
    fishing = make_epic()
    boats = make_epic(slug="boats", title="Boats")
    first = make_card(title="first", status="review", epicId=fishing["id"])
    client.patch(f"/api/cards/{first['id']}", json={"mergedToEpic": True}, headers=auth("builder"))
    make_card(title="boat", status="ready", epicId=boats["id"], dependsOn=[first["id"]])
    make_card(title="loose", status="ready", dependsOn=[first["id"]])
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204

    client.patch(f"/api/cards/{first['id']}", json={"status": "done"}, headers=auth("oliver"))
    assert client.get("/api/cards/next", headers=auth("builder")).json()["title"] == "boat"
