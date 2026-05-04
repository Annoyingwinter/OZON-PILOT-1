import time
from datetime import datetime

import httpx
from fastapi import APIRouter, Depends, File, Request, Response, UploadFile, status
from redis.asyncio import Redis
from sqlalchemy import update
from sqlalchemy.ext.asyncio import AsyncSession

from app.adapters.image_adapter import ImageAdapter
from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error
from app.core.request_context import ApiClientContext
from app.db.models import Quota, RequestReservation, UsageLog
from app.db.session import get_db
from app.metering.dependencies import enforce_metering
from app.metering.quota import QuotaMeter

router = APIRouter(prefix="/v1/biz", tags=["business"])

HOP_BY_HOP_HEADERS = {
    "connection",
    "keep-alive",
    "proxy-authenticate",
    "proxy-authorization",
    "te",
    "trailers",
    "transfer-encoding",
    "upgrade",
    "content-encoding",
    "content-length",
}
LEAK_HEADERS = {
    "set-cookie",
    "server",
    "x-powered-by",
    "x-aspnet-version",
    "x-aspnetmvc-version",
    "via",
    "x-cache",
    "x-cache-lookup",
}


async def finalize_metering(
    request: Request,
    db: AsyncSession,
    redis: Redis,
    context: ApiClientContext,
    status_code: int,
    started_at: float,
    error_code: str | None = None,
    release_quota: bool = False,
):
    request_id = request.state.request_id
    billable_units = int(getattr(request.state, "billable_units", 1))
    quota_key = getattr(request.state, "quota_key", "")
    reservation_key = getattr(request.state, "reservation_key", "")
    meter = QuotaMeter(redis)
    if release_quota:
        await meter.release(quota_key, reservation_key)
        billable_units = 0
        await db.execute(
            update(RequestReservation)
            .where(RequestReservation.id == request_id)
            .where(RequestReservation.status == "reserved")
            .values(status="released", completed_at=datetime.utcnow())
        )
    else:
        await db.execute(
            update(Quota)
            .where(Quota.subscription_id == context.subscription_id)
            .where(Quota.period_key == context.quota_period_key)
            .values(quota_used=Quota.quota_used + billable_units)
        )
        await db.execute(
            update(RequestReservation)
            .where(RequestReservation.id == request_id)
            .where(RequestReservation.status == "reserved")
            .values(status="committed", completed_at=datetime.utcnow())
        )

    db.add(
        UsageLog(
            user_id=context.user_id,
            api_key_id=context.api_key_id,
            endpoint=str(request.url.path),
            status_code=status_code,
            latency_ms=int((time.perf_counter() - started_at) * 1000),
            billable_units=billable_units,
            request_id=request_id,
            ip=request.client.host if request.client else None,
            error_code=error_code,
        )
    )
    await db.commit()
    if not release_quota:
        await meter.commit(reservation_key)


@router.post("/images/generate")
async def generate_image(
    payload: dict,
    request: Request,
    context: ApiClientContext = Depends(enforce_metering),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    started_at = time.perf_counter()
    adapter = ImageAdapter()
    response = await adapter.generate_placeholder(payload, request.state.request_id)
    if response.status == "failed":
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_501_NOT_IMPLEMENTED,
            started_at,
            response.error_code or ErrorCode.NOT_IMPLEMENTED,
            release_quota=True,
        )
        api_error(
            status.HTTP_501_NOT_IMPLEMENTED,
            response.error_code or ErrorCode.NOT_IMPLEMENTED,
            response.error_message or "Image generation is not configured",
            request.state.request_id,
        )

    await finalize_metering(request, db, redis, context, status.HTTP_200_OK, started_at)
    return response.model_dump()


@router.post("/images/upload")
async def upload_image(
    request: Request,
    file: UploadFile = File(...),
    context: ApiClientContext = Depends(enforce_metering),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    started_at = time.perf_counter()
    try:
        response = await ImageAdapter().upload(file, request.state.request_id)
    except httpx.HTTPError as exc:
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_502_BAD_GATEWAY,
            started_at,
            ErrorCode.UPSTREAM_ERROR,
            release_quota=True,
        )
        api_error(status.HTTP_502_BAD_GATEWAY, ErrorCode.UPSTREAM_ERROR, str(exc), request.state.request_id)

    await finalize_metering(request, db, redis, context, status.HTTP_200_OK, started_at)
    return response.model_dump()


@router.post("/fulfillment/labels/download")
async def download_fulfillment_labels(
    payload: dict,
    request: Request,
    context: ApiClientContext = Depends(enforce_metering),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    started_at = time.perf_counter()
    settings = get_settings()
    posting_numbers = payload.get("posting_number") if isinstance(payload, dict) else None
    if (
        not isinstance(posting_numbers, list)
        or len(posting_numbers) == 0
        or len(posting_numbers) > 100
        or any(not isinstance(value, str) or not value.strip() for value in posting_numbers)
    ):
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_400_BAD_REQUEST,
            started_at,
            ErrorCode.INVALID_INPUT,
            release_quota=True,
        )
        api_error(
            status.HTTP_400_BAD_REQUEST,
            ErrorCode.INVALID_INPUT,
            "posting_number must contain 1 to 100 non-empty strings",
            request.state.request_id,
        )

    if not settings.fulfillment_adapter_base_url:
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_501_NOT_IMPLEMENTED,
            started_at,
            ErrorCode.NOT_IMPLEMENTED,
            release_quota=True,
        )
        api_error(
            status.HTTP_501_NOT_IMPLEMENTED,
            ErrorCode.NOT_IMPLEMENTED,
            "Fulfillment adapter is not configured",
            request.state.request_id,
        )

    upstream = settings.fulfillment_adapter_base_url.rstrip("/") + "/ozon/fbs/package-label"
    headers = {
        "X-Internal-Token": settings.internal_adapter_token,
        "X-Request-Id": request.state.request_id,
        "X-User-Id": context.user_id,
    }
    try:
        async with httpx.AsyncClient(timeout=settings.default_request_timeout_seconds) as client:
            upstream_response = await client.post(upstream, headers=headers, json=payload)
    except httpx.HTTPError as exc:
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_502_BAD_GATEWAY,
            started_at,
            ErrorCode.UPSTREAM_ERROR,
            release_quota=True,
        )
        api_error(status.HTTP_502_BAD_GATEWAY, ErrorCode.UPSTREAM_ERROR, str(exc), request.state.request_id)

    release = upstream_response.status_code >= 500
    await finalize_metering(
        request,
        db,
        redis,
        context,
        upstream_response.status_code,
        started_at,
        ErrorCode.UPSTREAM_ERROR if release else None,
        release_quota=release,
    )
    headers = {
        key: value
        for key, value in upstream_response.headers.items()
        if key.lower() not in HOP_BY_HOP_HEADERS and key.lower() not in LEAK_HEADERS
    }
    return Response(
        content=upstream_response.content,
        status_code=upstream_response.status_code,
        media_type=upstream_response.headers.get("content-type", "application/pdf"),
        headers=headers,
    )


@router.api_route("/{capability:path}", methods=["GET", "POST", "PUT", "PATCH", "DELETE"])
async def proxy_business_capability(
    capability: str,
    request: Request,
    context: ApiClientContext = Depends(enforce_metering),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    started_at = time.perf_counter()
    settings = get_settings()
    upstream = settings.image_adapter_base_url.rstrip("/") + "/" + capability.lstrip("/")
    headers = {
        "X-Internal-Token": settings.internal_adapter_token,
        "X-Request-Id": request.state.request_id,
        "X-User-Id": context.user_id,
    }
    body = await request.body()
    try:
        async with httpx.AsyncClient(timeout=settings.default_request_timeout_seconds) as client:
            upstream_response = await client.request(
                request.method,
                upstream,
                headers=headers,
                content=body,
                params=dict(request.query_params),
            )
    except httpx.HTTPError as exc:
        await finalize_metering(
            request,
            db,
            redis,
            context,
            status.HTTP_502_BAD_GATEWAY,
            started_at,
            ErrorCode.UPSTREAM_ERROR,
            release_quota=True,
        )
        api_error(status.HTTP_502_BAD_GATEWAY, ErrorCode.UPSTREAM_ERROR, str(exc), request.state.request_id)

    release = upstream_response.status_code >= 500
    await finalize_metering(
        request,
        db,
        redis,
        context,
        upstream_response.status_code,
        started_at,
        ErrorCode.UPSTREAM_ERROR if release else None,
        release_quota=release,
    )
    headers = {
        key: value
        for key, value in upstream_response.headers.items()
        if key.lower() not in HOP_BY_HOP_HEADERS and key.lower() not in LEAK_HEADERS
    }
    return Response(
        content=upstream_response.content,
        status_code=upstream_response.status_code,
        headers=headers,
    )
