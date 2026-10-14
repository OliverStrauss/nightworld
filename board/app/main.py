"""FastAPI app. Run with `uvicorn app.main:app --host 127.0.0.1 --port 8787` from board/."""

from contextlib import asynccontextmanager
from functools import partial
from pathlib import Path

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from . import api, db, ui


def create_app(db_path: str | None = None) -> FastAPI:
    path = db_path or db.db_path()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        conn = db.connect(path)
        db.init_db(conn)
        conn.close()
        yield

    app = FastAPI(title="NightWorld board", lifespan=lifespan)
    app.state.connect = partial(db.connect, path)
    app.include_router(api.router)
    app.include_router(ui.router)
    app.mount("/static", StaticFiles(directory=Path(__file__).parent / "static"), name="static")
    return app


app = create_app()
