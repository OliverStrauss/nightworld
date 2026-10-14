"""FastAPI app. Run with `uvicorn app.main:app --host 127.0.0.1 --port 8787` from board/."""

from contextlib import asynccontextmanager
from functools import partial

from fastapi import FastAPI

from . import api, db


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
    return app


app = create_app()
