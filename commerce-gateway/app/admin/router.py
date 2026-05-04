from datetime import datetime

from fastapi import APIRouter, Depends, Request
from pydantic import BaseModel
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth.dependencies import SessionUser, require_admin_action
from app.core.request_utils import proxy_debug_info
from app.core.security import generate_api_key
from app.db.models import AuditLog, AutomationRun, Device, License, Subscription, UsageLog, User
from app.db.session import get_db

router = APIRouter(prefix="/admin", tags=["admin"])


class CreateLicenseRequest(BaseModel):
    user_id: str
    max_devices: int = 1


@router.get("/dashboard")
async def dashboard(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    users = (await db.execute(select(User).limit(20))).scalars().all()
    return {"users_sample": len(users), "status": "admin_shell_ready"}


@router.get("/_debug/ip")
async def debug_ip(
    request: Request,
    admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    debug_info = proxy_debug_info(request)
    db.add(
        AuditLog(
            actor_type="admin",
            actor_id=admin.id,
            action="admin.proxy_debug",
            target_type="proxy",
            target_id="client_ip",
            payload=debug_info,
            ip=debug_info["resolved_client_ip"],
        )
    )
    await db.commit()
    return debug_info


@router.get("/users")
async def users(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(User).order_by(User.created_at.desc()).limit(100))).scalars().all()
    return [{"id": row.id, "email": row.email, "status": row.status, "created_at": row.created_at} for row in rows]


@router.get("/subscriptions")
async def subscriptions(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(Subscription).limit(100))).scalars().all()
    return [
        {
            "id": row.id,
            "user_id": row.user_id,
            "plan_id": row.plan_id,
            "status": row.status,
            "current_period_start": row.current_period_start,
            "current_period_end": row.current_period_end,
        }
        for row in rows
    ]


@router.get("/usage")
async def usage(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(UsageLog).order_by(UsageLog.ts.desc()).limit(100))).scalars().all()
    return [
        {
            "id": row.id,
            "user_id": row.user_id,
            "api_key_id": row.api_key_id,
            "endpoint": row.endpoint,
            "status_code": row.status_code,
            "billable_units": row.billable_units,
            "request_id": row.request_id,
            "error_code": row.error_code,
            "ts": row.ts,
        }
        for row in rows
    ]


@router.get("/audit-logs")
async def audit_logs(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(AuditLog).order_by(AuditLog.ts.desc()).limit(100))).scalars().all()
    return [
        {
            "id": row.id,
            "actor_type": row.actor_type,
            "actor_id": row.actor_id,
            "action": row.action,
            "target_type": row.target_type,
            "target_id": row.target_id,
            "payload": row.payload,
            "ip": row.ip,
            "ts": row.ts,
        }
        for row in rows
    ]


@router.get("/licenses")
async def list_licenses(
    _admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(License).order_by(License.created_at.desc()).limit(100))).scalars().all()
    return [
        {
            "id": row.id,
            "user_id": row.user_id,
            "license_prefix": row.license_prefix,
            "status": row.status,
            "max_devices": row.max_devices,
            "created_at": row.created_at,
            "revoked_at": row.revoked_at,
        }
        for row in rows
    ]


@router.post("/licenses")
async def create_license(
    payload: CreateLicenseRequest,
    admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    target_user = await db.scalar(select(User).where(User.id == payload.user_id))
    if not target_user:
        return {"ok": False, "error": "user_not_found"}
    raw_license, license_prefix, license_hash = generate_api_key(prefix="lic_live")
    license_record = License(
        user_id=payload.user_id,
        license_prefix=license_prefix,
        license_hash=license_hash,
        max_devices=payload.max_devices,
    )
    db.add(license_record)
    await db.flush()
    db.add(
        AuditLog(
            actor_type="admin",
            actor_id=admin.id,
            action="license.created",
            target_type="license",
            target_id=license_record.id,
            payload={"user_id": payload.user_id, "max_devices": payload.max_devices},
        )
    )
    await db.commit()
    return {
        "id": license_record.id,
        "user_id": license_record.user_id,
        "license": raw_license,
        "license_prefix": license_prefix,
    }


@router.post("/licenses/{license_id}/revoke")
async def revoke_license(
    license_id: str,
    admin: SessionUser = Depends(require_admin_action),
    db: AsyncSession = Depends(get_db),
):
    license_record = await db.scalar(select(License).where(License.id == license_id))
    if not license_record:
        return {"ok": True}
    if license_record.status == "revoked":
        return {"ok": True}
    license_record.status = "revoked"
    license_record.revoked_at = datetime.utcnow()
    await db.execute(
        update(Device)
        .where(Device.license_id == license_record.id)
        .where(Device.status == "active")
        .values(status="disabled")
    )
    await db.execute(
        update(AutomationRun)
        .where(AutomationRun.license_id == license_record.id)
        .where(AutomationRun.status.in_(["running", "paused"]))
        .values(status="aborted", completed_at=datetime.utcnow())
    )
    db.add(
        AuditLog(
            actor_type="admin",
            actor_id=admin.id,
            action="license.revoked",
            target_type="license",
            target_id=license_record.id,
            payload={"user_id": license_record.user_id},
        )
    )
    await db.commit()
    return {"ok": True}
