from datetime import datetime
import hashlib

import jwt
from fastapi import APIRouter, Depends, Header, Request, status
from pydantic import BaseModel, Field
from redis.asyncio import Redis
from sqlalchemy import func, select, update
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth.dependencies import SessionUser, require_session_user
from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error
from app.core.security import create_run_token, decode_session_token, hash_api_key
from app.db.models import (
    ActionPermit,
    AuditLog,
    AutomationRun,
    Device,
    License,
    Quota,
    Subscription,
    UsageLog,
    User,
)
from app.db.session import get_db
from app.metering.quota import QuotaMeter
from app.metering.rate_limit import RateLimiter

router = APIRouter(prefix="/v1/client", tags=["desktop-client"])


class ActivateRequest(BaseModel):
    license_key: str
    device_fingerprint: str
    app_version: str
    device_name: str | None = None


class RunCreateRequest(BaseModel):
    license_id: str
    device_id: str
    workflow: str
    estimated_items: int = 0


class ReserveRequest(BaseModel):
    action: str
    units: int = Field(default=1, ge=1)
    idempotency_key: str


class CompletePermitRequest(BaseModel):
    permit_id: str


class CompleteRunRequest(BaseModel):
    status: str = "succeeded"
    processed_items: int = 0
    failed_items: int = 0


async def _active_subscription_and_quota(db: AsyncSession, user_id: str):
    stmt = (
        select(Subscription, Quota)
        .join(Quota, Quota.subscription_id == Subscription.id)
        .where(Subscription.user_id == user_id)
        .where(Subscription.status == "active")
        .order_by(Subscription.current_period_end.desc())
        .limit(1)
    )
    row = (await db.execute(stmt)).first()
    if not row:
        return None, None
    return row[0], row[1]


async def _require_active_run(db: AsyncSession, run_id: str) -> AutomationRun:
    run = await db.scalar(select(AutomationRun).where(AutomationRun.id == run_id))
    if not run or run.status not in {"running", "paused"}:
        api_error(status.HTTP_404_NOT_FOUND, ErrorCode.INVALID_INPUT, "Run is not active")
    license_ok = await db.scalar(
        select(License.id)
        .where(License.id == run.license_id)
        .where(License.status == "active")
        .where(License.revoked_at.is_(None))
    )
    device_ok = await db.scalar(
        select(Device.id)
        .where(Device.id == run.device_id)
        .where(Device.status == "active")
    )
    if not license_ok or not device_ok:
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.UNAUTHORIZED, "License or device is inactive")
    return run


def _require_run_token(authorization: str | None, run_id: str) -> None:
    if not authorization or not authorization.startswith("Bearer "):
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Missing run token")
    try:
        payload = decode_session_token(authorization.removeprefix("Bearer ").strip())
    except jwt.ExpiredSignatureError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Run token expired")
    except jwt.InvalidTokenError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid run token")
    if payload.get("scope") != "automation_run" or payload.get("sub") != run_id:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Run token mismatch")


@router.get("/licenses")
async def list_my_licenses(
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(License).where(License.user_id == user.id))).scalars().all()
    return [
        {
            "id": row.id,
            "license_prefix": row.license_prefix,
            "status": row.status,
            "max_devices": row.max_devices,
            "created_at": row.created_at,
            "revoked_at": row.revoked_at,
        }
        for row in rows
    ]


@router.post("/activate")
async def activate(
    payload: ActivateRequest,
    request: Request,
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    license_hash = hash_api_key(payload.license_key)
    allowed, _ = await RateLimiter(redis).allow(f"rl:license_activate:{license_hash}:60s", 10, 60)
    if not allowed:
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Too many activations")
    license_record = await db.scalar(
        select(License)
        .where(License.license_hash == license_hash)
        .where(License.status == "active")
        .where(License.revoked_at.is_(None))
    )
    if not license_record:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid license")

    device = await db.scalar(
        select(Device)
        .where(Device.license_id == license_record.id)
        .where(Device.device_fingerprint == payload.device_fingerprint)
    )
    if not device:
        active_device_count = await db.scalar(
            select(func.count(Device.id))
            .where(Device.license_id == license_record.id)
            .where(Device.status == "active")
        )
        if int(active_device_count or 0) >= license_record.max_devices:
            api_error(status.HTTP_403_FORBIDDEN, ErrorCode.PLAN_REQUIRED, "Device limit reached")
        device = Device(
            user_id=license_record.user_id,
            license_id=license_record.id,
            device_fingerprint=payload.device_fingerprint,
            device_name=payload.device_name,
            app_version=payload.app_version,
        )
        db.add(device)
    else:
        device.last_seen_at = datetime.utcnow()
        device.app_version = payload.app_version
        if payload.device_name:
            device.device_name = payload.device_name

    db.add(
        AuditLog(
            actor_type="client",
            actor_id=license_record.user_id,
            action="license.activated",
            target_type="device",
            target_id=device.id,
            payload={
                "license_id": license_record.id,
                "device_fingerprint": payload.device_fingerprint,
                "app_version": payload.app_version,
            },
            ip=request.client.host if request.client else None,
        )
    )
    await db.commit()
    return {
        "license_id": license_record.id,
        "device_id": device.id,
        "status": "activated",
        "heartbeat_interval_seconds": 60,
    }


@router.post("/runs")
async def create_run(
    payload: RunCreateRequest,
    request: Request,
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    license_record = await db.scalar(
        select(License)
        .where(License.id == payload.license_id)
        .where(License.status == "active")
        .where(License.revoked_at.is_(None))
    )
    device = await db.scalar(
        select(Device)
        .where(Device.id == payload.device_id)
        .where(Device.license_id == payload.license_id)
        .where(Device.status == "active")
    )
    if not license_record or not device:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid license or device")
    if license_record.user_id != user.id:
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.UNAUTHORIZED, "License does not belong to user")
    settings = get_settings()
    if settings.require_realname_for_client_runs:
        record_user = await db.scalar(select(User).where(User.id == user.id))
        if not record_user or record_user.realname_status != "verified":
            api_error(status.HTTP_403_FORBIDDEN, ErrorCode.PLAN_REQUIRED, "Real-name verification required")

    subscription, quota = await _active_subscription_and_quota(db, license_record.user_id)
    if not subscription or not quota:
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.SUBSCRIPTION_INACTIVE, "Subscription inactive")

    limiter = RateLimiter(redis)
    allowed, _ = await limiter.allow(f"rl:run_start:{license_record.user_id}:60s", 10, 60)
    if not allowed:
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Too many runs started")

    run = AutomationRun(
        user_id=license_record.user_id,
        license_id=license_record.id,
        device_id=device.id,
        workflow=payload.workflow,
        estimated_items=payload.estimated_items,
    )
    db.add(run)
    await db.commit()
    return {
        "job_id": run.id,
        "run_token": create_run_token(run.id, run.user_id),
        "expires_in": 900,
        "limits": {"heartbeat_interval": 60},
    }


@router.post("/runs/{run_id}/heartbeat")
async def heartbeat(
    run_id: str,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
):
    _require_run_token(authorization, run_id)
    run = await _require_active_run(db, run_id)
    run.heartbeat_at = datetime.utcnow()
    await db.commit()
    return {"continue": True, "expires_in": 900}


@router.post("/runs/{run_id}/reserve")
async def reserve_action(
    run_id: str,
    payload: ReserveRequest,
    request: Request,
    authorization: str | None = Header(default=None),
    idempotency_key: str | None = Header(default=None, alias="Idempotency-Key"),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    _require_run_token(authorization, run_id)
    run = await _require_active_run(db, run_id)
    subscription, quota = await _active_subscription_and_quota(db, run.user_id)
    if not subscription or not quota:
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.SUBSCRIPTION_INACTIVE, "Subscription inactive")

    effective_key = idempotency_key or payload.idempotency_key
    stable_permit_id = hashlib.sha256(f"{run_id}:{effective_key}".encode("utf-8")).hexdigest()[:36]

    stmt = (
        insert(ActionPermit)
        .values(
            id=stable_permit_id,
            run_id=run.id,
            user_id=run.user_id,
            subscription_id=subscription.id,
            period_key=quota.period_key,
            action=payload.action,
            idempotency_key=effective_key,
            units=payload.units,
            status="creating",
        )
        .on_conflict_do_nothing(index_elements=["run_id", "idempotency_key"])
        .returning(ActionPermit.id)
    )
    inserted_id = await db.scalar(stmt)
    if not inserted_id:
        existing = await db.scalar(
            select(ActionPermit)
            .where(ActionPermit.run_id == run_id)
            .where(ActionPermit.idempotency_key == effective_key)
        )
        return {
            "allowed": existing.status in {"reserved", "committed"},
            "permit_id": existing.id,
            "status": existing.status,
        }
    await db.commit()

    quota_key = f"quota:{subscription.id}:{quota.period_key}"
    await redis.setnx(quota_key, max(0, quota.quota_total + quota.quota_extra - quota.quota_used))
    reservation_key = f"quota_reservation:permit:{stable_permit_id}"
    allowed, remaining = await QuotaMeter(redis).reserve(quota_key, reservation_key, payload.units)
    if not allowed:
        await db.execute(
            update(ActionPermit)
            .where(ActionPermit.id == stable_permit_id)
            .where(ActionPermit.status == "creating")
            .values(status="rejected", completed_at=datetime.utcnow())
        )
        db.add(
            UsageLog(
                user_id=run.user_id,
                api_key_id=None,
                endpoint=f"client:{payload.action}",
                status_code=402,
                billable_units=0,
                request_id=stable_permit_id,
                ip=request.client.host if request.client else None,
                error_code=ErrorCode.QUOTA_EXCEEDED,
            )
        )
        await db.commit()
        api_error(status.HTTP_402_PAYMENT_REQUIRED, ErrorCode.QUOTA_EXCEEDED, "Quota exceeded")

    await db.execute(
        update(ActionPermit)
        .where(ActionPermit.id == stable_permit_id)
        .where(ActionPermit.status == "creating")
        .values(status="reserved")
    )
    db.add(
        UsageLog(
            user_id=run.user_id,
            api_key_id=None,
            endpoint=f"client:{payload.action}",
            status_code=202,
            billable_units=0,
            request_id=stable_permit_id,
            ip=request.client.host if request.client else None,
        )
    )
    await db.commit()
    return {"allowed": True, "permit_id": stable_permit_id, "remaining": remaining}


@router.post("/runs/{run_id}/commit")
async def commit_action(
    run_id: str,
    payload: CompletePermitRequest,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    _require_run_token(authorization, run_id)
    run = await _require_active_run(db, run_id)
    permit = await db.scalar(
        select(ActionPermit).where(ActionPermit.id == payload.permit_id).where(ActionPermit.run_id == run.id)
    )
    if not permit:
        api_error(status.HTTP_404_NOT_FOUND, ErrorCode.INVALID_INPUT, "Permit not found")
    if permit.status == "committed":
        return {"ok": True}
    if permit.status != "reserved":
        api_error(status.HTTP_409_CONFLICT, ErrorCode.INVALID_INPUT, "Permit is not reserved")

    subscription, quota = await _active_subscription_and_quota(db, run.user_id)
    if subscription and quota:
        result = await db.execute(
            update(ActionPermit)
            .where(ActionPermit.id == permit.id)
            .where(ActionPermit.status == "reserved")
            .values(status="committed", completed_at=datetime.utcnow())
            .returning(ActionPermit.id)
        )
        if not result.scalar():
            await db.rollback()
            return {"ok": True}
        quota.quota_used += permit.units

    db.add(
        UsageLog(
            user_id=run.user_id,
            api_key_id=None,
            endpoint=f"client:{permit.action}",
            status_code=200,
            billable_units=permit.units,
            request_id=permit.id,
        )
    )
    await db.commit()
    await QuotaMeter(redis).commit(f"quota_reservation:permit:{permit.id}")
    return {"ok": True}


@router.post("/runs/{run_id}/release")
async def release_action(
    run_id: str,
    payload: CompletePermitRequest,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    _require_run_token(authorization, run_id)
    run = await _require_active_run(db, run_id)
    permit = await db.scalar(
        select(ActionPermit).where(ActionPermit.id == payload.permit_id).where(ActionPermit.run_id == run.id)
    )
    if not permit:
        api_error(status.HTTP_404_NOT_FOUND, ErrorCode.INVALID_INPUT, "Permit not found")
    if permit.status == "released":
        return {"ok": True}
    if permit.status != "reserved":
        api_error(status.HTTP_409_CONFLICT, ErrorCode.INVALID_INPUT, "Permit is not reserved")

    subscription, quota = await _active_subscription_and_quota(db, run.user_id)
    if subscription and quota:
        result = await db.execute(
            update(ActionPermit)
            .where(ActionPermit.id == permit.id)
            .where(ActionPermit.status == "reserved")
            .values(status="released", completed_at=datetime.utcnow())
            .returning(ActionPermit.id)
        )
        if not result.scalar():
            await db.rollback()
            return {"ok": True}
    await db.commit()
    if subscription and quota:
        await QuotaMeter(redis).release(
            f"quota:{subscription.id}:{quota.period_key}",
            f"quota_reservation:permit:{permit.id}",
        )
    return {"ok": True}


@router.post("/runs/{run_id}/complete")
async def complete_run(
    run_id: str,
    payload: CompleteRunRequest,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
):
    _require_run_token(authorization, run_id)
    run = await _require_active_run(db, run_id)
    run.status = payload.status
    run.processed_items = payload.processed_items
    run.failed_items = payload.failed_items
    run.completed_at = datetime.utcnow()
    await db.commit()
    return {"ok": True}
