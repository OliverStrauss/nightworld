import pytest
from fastapi.testclient import TestClient

from app.auth import hash_password
from app.main import create_app

TOKENS = {
    "oliver": "tok-oliver",
    "builder": "tok-builder",
    "reviewer": "tok-reviewer",
    "groomer": "tok-groomer",
}

PASSWORD = "bog-slime-42"
PASSWORD_HASH = hash_password(PASSWORD, iterations=1000)  # few rounds keeps the tests fast


@pytest.fixture
def db_path(tmp_path):
    return str(tmp_path / "board.db")


@pytest.fixture
def client(monkeypatch, db_path):
    for actor, token in TOKENS.items():
        monkeypatch.setenv(f"BOARD_TOKEN_{actor.upper()}", token)
    monkeypatch.setenv("BOARD_PASSWORD_HASH", PASSWORD_HASH)
    monkeypatch.setenv("BOARD_SECRET", "test-secret")
    with TestClient(create_app(db_path)) as c:
        yield c


def auth(actor):
    return {"Authorization": f"Bearer {TOKENS[actor]}"}


@pytest.fixture
def make_card(client):
    """Create a card as oliver and return its JSON."""

    def make(**fields):
        body = {"title": "A card", "type": "content", "size": "S", **fields}
        r = client.post("/api/cards", json=body, headers=auth("oliver"))
        assert r.status_code == 201, r.text
        return r.json()

    return make


@pytest.fixture
def browser(client):
    """The test client, logged in to the web UI as oliver."""
    r = client.post("/login", data={"password": PASSWORD, "next": "/"}, follow_redirects=False)
    assert r.status_code == 303, r.text
    return client
