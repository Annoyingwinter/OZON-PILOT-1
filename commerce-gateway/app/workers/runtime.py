import asyncio
from contextlib import suppress

from app.cache.redis import get_redis
from app.core.config import get_settings
from app.db.session import AsyncSessionLocal
from app.workers.quota_reconciler import reconcile_quotas_once


async def api_key_revocation_listener(stop_event: asyncio.Event):
    redis = get_redis()
    pubsub = redis.pubsub()
    await pubsub.subscribe("api_key_revoked")
    try:
        while not stop_event.is_set():
            message = await pubsub.get_message(ignore_subscribe_messages=True, timeout=1.0)
            if message and message.get("data"):
                await redis.delete(f"api_key:{message['data']}")
    finally:
        with suppress(Exception):
            await pubsub.unsubscribe("api_key_revoked")
            await pubsub.close()


async def quota_reconcile_loop(stop_event: asyncio.Event):
    settings = get_settings()
    while not stop_event.is_set():
        async with AsyncSessionLocal() as db:
            await reconcile_quotas_once(db, get_redis())
        try:
            await asyncio.wait_for(stop_event.wait(), settings.quota_reconcile_interval_seconds)
        except asyncio.TimeoutError:
            pass


def start_background_workers() -> tuple[asyncio.Event, list[asyncio.Task]]:
    settings = get_settings()
    stop_event = asyncio.Event()
    if not settings.enable_background_workers:
        return stop_event, []
    tasks = [
        asyncio.create_task(api_key_revocation_listener(stop_event)),
        asyncio.create_task(quota_reconcile_loop(stop_event)),
    ]
    return stop_event, tasks


async def stop_background_workers(stop_event: asyncio.Event, tasks: list[asyncio.Task]):
    stop_event.set()
    for task in tasks:
        task.cancel()
    for task in tasks:
        with suppress(asyncio.CancelledError):
            await task
