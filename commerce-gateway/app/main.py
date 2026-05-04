from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.admin.router import router as admin_router
from app.admin_ui.router import router as admin_ui_router
from app.auth.router import router as auth_router
from app.billing.router import router as billing_router
from app.biz.router import router as biz_router
from app.client.router import router as client_router
from app.keys.router import router as keys_router
from app.workers.runtime import start_background_workers, stop_background_workers


@asynccontextmanager
async def lifespan(app: FastAPI):
    stop_event, tasks = start_background_workers()
    try:
        yield
    finally:
        await stop_background_workers(stop_event, tasks)


def create_app() -> FastAPI:
    app = FastAPI(title="OZON-PILOT Commerce Gateway", version="0.1.0", lifespan=lifespan)

    @app.get("/health")
    async def health():
        return {"ok": True}

    app.include_router(auth_router)
    app.include_router(keys_router)
    app.include_router(billing_router)
    app.include_router(biz_router)
    app.include_router(client_router)
    app.include_router(admin_router)
    app.include_router(admin_ui_router)
    return app


app = create_app()
