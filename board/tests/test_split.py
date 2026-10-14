import json

import pytest

from app import splitter
from conftest import auth

GOOD = """Here you go:
```json
{"cards": [
  {"title": "Fishing rod item", "type": "content", "size": "S", "goal": "A rod to fish with.",
   "criteria": ["ItemDefinition `fishing-rod` exists"], "dependsOn": []},
  {"title": "Cast and reel", "type": "system", "size": "M", "effort": "high",
   "criteria": ["PlayMode test catches a boot"], "dependsOn": [1]},
  {"title": "Suspiciously Damp Boots", "type": "content", "size": "S", "dependsOn": [1, 2, 7]}
]}
```"""


@pytest.fixture
def epic(client):
    r = client.post("/api/epics", json={"slug": "fishing", "title": "Fishing", "pitch": "Catch a boot."},
                    headers=auth("oliver"))
    return r.json()


@pytest.fixture
def fake_claude(monkeypatch):
    calls = []

    def use(reply):
        def run(prompt):
            calls.append(prompt)
            if isinstance(reply, Exception):
                raise reply
            return reply
        monkeypatch.setattr(splitter, "run_claude", run)
        return calls

    return use


def finish(epic_id):
    splitter._threads[epic_id].join(timeout=10)


def test_split_adds_draft_cards_to_backlog(client, epic, fake_claude):
    prompts = fake_claude(GOOD)
    r = client.post(f"/api/epics/{epic['id']}/split", headers=auth("oliver"))
    assert r.status_code == 202
    finish(epic["id"])

    assert "Catch a boot." in prompts[0] and "epic/fishing" in prompts[0]
    full = client.get(f"/api/epics/{epic['id']}", headers=auth("oliver")).json()
    titles = [c["title"] for c in full["cards"]]
    assert titles == ["Fishing rod item", "Cast and reel", "Suspiciously Damp Boots"]
    rod, reel, boots = full["cards"]
    assert all(c["status"] == "backlog" and c["epicId"] == epic["id"] for c in full["cards"])
    assert reel["dependsOn"] == [rod["id"]] and reel["effort"] == "high"
    assert boots["dependsOn"] == [rod["id"], reel["id"]]  # 7 doesn't exist: dropped
    log = client.get("/api/activity", headers=auth("oliver")).json()
    assert {(e["actor"], e["type"]) for e in log} >= {("groomer", "card.created"), ("groomer", "epic.split")}
    status = client.get(f"/api/epics/{epic['id']}/split", headers=auth("builder")).json()
    assert status["state"] == "done" and "3 draft cards" in status["message"]


def test_nothing_is_ready_after_a_split(client, epic, fake_claude):
    fake_claude(GOOD)
    client.post(f"/api/epics/{epic['id']}/split", headers=auth("oliver"))
    finish(epic["id"])
    assert client.get("/api/cards/next", headers=auth("builder")).status_code == 204


@pytest.mark.parametrize("reply", ["no json here", '{"cards": []}', RuntimeError("claude exited with 1")])
def test_failures_are_shown_and_add_nothing(client, epic, fake_claude, reply):
    fake_claude(reply)
    client.post(f"/api/epics/{epic['id']}/split", headers=auth("oliver"))
    finish(epic["id"])
    assert client.get("/api/cards", headers=auth("oliver")).json() == []
    assert client.get(f"/api/epics/{epic['id']}/split", headers=auth("oliver")).json()["state"] == "failed"


def test_one_bad_draft_adds_none_of_them(client, epic, fake_claude):
    bad = json.dumps({"cards": [{"title": "ok", "type": "fix", "size": "S"}, {"title": "huge", "type": "fix", "size": "L"}]})
    fake_claude(bad)
    client.post(f"/api/epics/{epic['id']}/split", headers=auth("oliver"))
    finish(epic["id"])
    assert client.get("/api/cards", headers=auth("oliver")).json() == []
    assert "Draft 2" in client.get(f"/api/epics/{epic['id']}/split", headers=auth("oliver")).json()["message"]


@pytest.mark.parametrize("actor", ["builder", "reviewer", "groomer"])
def test_only_oliver_starts_a_split(client, epic, actor):
    assert client.post(f"/api/epics/{epic['id']}/split", headers=auth(actor)).status_code == 403


def test_split_unknown_epic_is_404(client):
    assert client.post("/api/epics/9/split", headers=auth("oliver")).status_code == 404


def test_prompt_lists_cards_already_in_the_epic(client, epic, make_card):
    make_card(title="Bait shop", epicId=epic["id"])
    conn = client.app.state.connect()
    assert "#1 Bait shop (backlog)" in splitter.build_prompt(conn, epic["id"])
    conn.close()


def test_split_button_on_epic_page(browser, epic, fake_claude):
    fake_claude(GOOD)
    assert "Split into cards" in browser.get(f"/epics/{epic['id']}").text
    r = browser.post(f"/epics/{epic['id']}/split", follow_redirects=False)
    assert r.status_code == 303
    finish(epic["id"])
    html = browser.get(f"/epics/{epic['id']}").text
    assert "Added 3 draft cards" in html and "Fishing rod item" in html


def test_run_claude_calls_the_groomer_read_only(monkeypatch, tmp_path):
    seen = {}

    class Done:
        returncode = 0
        stdout = json.dumps({"result": "hello"})
        stderr = ""

    def fake_run(cmd, **kw):
        seen["cmd"], seen["cwd"], seen["input"] = cmd, kw["cwd"], kw["input"]
        return Done()

    monkeypatch.setattr(splitter.subprocess, "run", fake_run)
    monkeypatch.setenv("BOARD_REPO_DIR", str(tmp_path))
    assert splitter.run_claude("split it") == "hello"
    assert seen["cwd"] == str(tmp_path)
    assert seen["cmd"][:4] == ["claude", "-p", "--agent", "groomer"]
    assert "Write" not in seen["cmd"] and "Bash" not in seen["cmd"]
    assert seen["input"] == "split it" and "split it" not in seen["cmd"]  # on stdin, not swallowed by --allowedTools
