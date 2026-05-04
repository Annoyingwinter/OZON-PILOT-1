import hashlib
import hmac
import json

from fastapi import APIRouter, Depends, Header, Request, status
from pydantic import BaseModel
from sqlalchemy.exc import IntegrityError
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth.dependencies import SessionUser, require_session_user
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error
from app.db.models import BillingEvent, Plan, Subscription
from app.db.session import get_db

router = APIRouter(prefix="/v1", tags=["billing"])


class CheckoutRequest(BaseModel):
    plan_code: str
    provider: str = "wechat"


@router.get("/plans")
async def plans(db: AsyncSession = Depends(get_db)):
    rows = (await db.execute(select(Plan).where(Plan.is_active.is_(True)).order_by(Plan.sort_order))).scalars()
    return [
        {
            "code": row.code,
            "name": row.name,
            "price_cents": row.price_cents,
            "currency": row.currency,
            "quota_calls": row.quota_calls,
            "qps_limit": row.qps_limit,
            "features": row.features,
        }
        for row in rows
    ]


@router.get("/subscription")
async def subscription(
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
):
    row = await db.scalar(
        select(Subscription).where(Subscription.user_id == user.id).order_by(Subscription.created_at.desc())
    )
    if not row:
        return {}
    return {
        "id": row.id,
        "user_id": row.user_id,
        "plan_id": row.plan_id,
        "status": row.status,
        "current_period_start": row.current_period_start,
        "current_period_end": row.current_period_end,
        "auto_renew": row.auto_renew,
        "canceled_at": row.canceled_at,
    }


@router.post("/billing/checkout")
async def checkout(payload: CheckoutRequest, user: SessionUser = Depends(require_session_user)):
    return {
        "status": "provider_not_configured",
        "provider": payload.provider,
        "user_id": user.id,
        "message": "Payment provider adapter placeholder. Wire WeChat/Alipay here.",
    }


@router.post("/webhooks/payment/{provider}")
async def payment_webhook(
    provider: str,
    payload: dict,
    request: Request,
    x_signature: str | None = Header(default=None, alias="X-Signature"),
    db: AsyncSession = Depends(get_db),
):
    settings = get_settings()
    raw_body = await request.body()
    if settings.payment_webhook_secret:
        expected = hmac.new(
            settings.payment_webhook_secret.encode("utf-8"), raw_body, hashlib.sha256
        ).hexdigest()
        if not x_signature or not hmac.compare_digest(expected, x_signature):
            api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid webhook signature")

    event_id = str(payload.get("id") or payload.get("event_id") or "")
    event_type = str(payload.get("type") or payload.get("event_type") or "unknown")
    if not event_id:
        stable_payload = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
        event_id = hashlib.sha256(stable_payload).hexdigest()
    event = BillingEvent(provider=provider, event_id=event_id, event_type=event_type, payload=payload)
    db.add(event)
    try:
        await db.commit()
    except IntegrityError:
        await db.rollback()
        return {"received": True, "duplicate": True}
    return {"received": True, "duplicate": False}
