from datetime import datetime, timedelta, timezone

import jwt
from fastapi import APIRouter, Depends, Header, Request, status
from pydantic import BaseModel, EmailStr
from redis.asyncio import Redis
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error, unauthorized
from app.core.request_utils import client_ip, mask_email, stable_audit_id
from app.core.security import (
    create_session_token,
    decode_session_token,
    get_dummy_password_hash,
    hash_password,
    refresh_grace_key,
    session_blacklist_key,
    verify_password,
)
from app.db.models import AuditLog, Plan, Quota, Subscription, User
from app.db.session import get_db
from app.metering.rate_limit import RateLimiter

router = APIRouter(prefix="/v1/auth", tags=["auth"])
settings = get_settings()


class RegisterRequest(BaseModel):
    email: EmailStr
    password: str
    phone: str | None = None


class LoginRequest(BaseModel):
    email: EmailStr
    password: str


def _email_audit_payload(email: str) -> dict:
    if not settings.audit_include_email_hint:
        return {}
    return {"email_hint": mask_email(email)}


@router.post("/register")
async def register(
    payload: RegisterRequest,
    request: Request,
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    ip = client_ip(request)
    allowed, _ = await RateLimiter(redis).allow(f"rl:auth_register:ip:{ip}:60s", 5, 60)
    if not allowed:
        db.add(
            AuditLog(
                actor_type="auth",
                actor_id=stable_audit_id(ip),
                action="auth.register_rate_limited",
                target_type="registration",
                target_id=stable_audit_id(ip),
                payload={},
                ip=ip,
            )
        )
        await db.commit()
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Too many registrations")

    existing = await db.scalar(select(User).where(User.email == payload.email))
    if existing:
        unauthorized("Email already registered")

    user = User(email=payload.email, phone=payload.phone, password_hash=hash_password(payload.password))
    free_plan = await db.scalar(select(Plan).where(Plan.code == "free"))
    db.add(user)
    await db.flush()

    if free_plan:
        now = datetime.utcnow()
        subscription = Subscription(
            user_id=user.id,
            plan_id=free_plan.id,
            status="active",
            current_period_start=now,
            current_period_end=now + timedelta(days=30),
        )
        db.add(subscription)
        await db.flush()
        db.add(
            Quota(
                subscription_id=subscription.id,
                period_key=now.strftime("%Y%m%d") + "-" + subscription.id[:8],
                quota_total=free_plan.quota_calls,
                quota_used=0,
                quota_extra=0,
            )
        )

    await db.commit()
    return {"id": user.id, "email": user.email}


@router.post("/login")
async def login(
    payload: LoginRequest,
    request: Request,
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    email = str(payload.email).lower()
    ip = client_ip(request)
    limiter = RateLimiter(redis)
    ip_allowed, _ = await limiter.allow(f"rl:auth_login:ip:{ip}:60s", 20, 60)
    email_allowed, _ = await limiter.allow(f"rl:auth_login:email:{email}:60s", 10, 60)
    audit_id = stable_audit_id(email)
    audit_payload = _email_audit_payload(email)
    if not ip_allowed or not email_allowed:
        db.add(
            AuditLog(
                actor_type="auth",
                actor_id=audit_id,
                action="auth.login_rate_limited",
                target_type="session",
                target_id=audit_id,
                payload=audit_payload,
                ip=ip,
            )
        )
        await db.commit()
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Too many login attempts")

    user = await db.scalar(select(User).where(User.email == payload.email))
    target_hash = user.password_hash if user else get_dummy_password_hash()
    password_ok = verify_password(payload.password, target_hash)
    if not user or not password_ok:
        db.add(
            AuditLog(
                actor_type="auth",
                actor_id=audit_id,
                action="auth.login_failed",
                target_type="session",
                target_id=audit_id,
                payload=audit_payload,
                ip=ip,
            )
        )
        await db.commit()
        unauthorized("Invalid email or password")
    if user.status != "active":
        unauthorized("User is not active")
    await redis.delete(f"rl:auth_login:email:{email}:60s")
    return {"access_token": create_session_token(user.id, user.role), "token_type": "bearer"}


@router.post("/refresh")
async def refresh(
    request: Request,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    if not authorization or not authorization.startswith("Bearer "):
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Missing session token")
    raw_token = authorization.removeprefix("Bearer ").strip()
    try:
        payload = decode_session_token(raw_token)
    except jwt.ExpiredSignatureError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Session token expired")
    except jwt.InvalidTokenError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid session token")

    jti = str(payload.get("jti") or "")
    if not jti and settings.enforce_jti:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Token missing jti")

    if jti:
        cached_token = await redis.get(refresh_grace_key(jti))
        if cached_token:
            return {"access_token": cached_token, "token_type": "bearer"}
        if await redis.exists(session_blacklist_key(jti)):
            api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Token revoked")

    user_id = str(payload.get("sub") or "")
    user = await db.scalar(select(User).where(User.id == user_id).where(User.status == "active"))
    if not user:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "User is not active")

    allowed, _ = await RateLimiter(redis).allow(f"rl:auth_refresh:{user.id}:60s", 60, 60)
    if not allowed:
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Too many refresh attempts")

    new_token = create_session_token(user.id, user.role)
    new_jti = str(decode_session_token(new_token).get("jti") or "")
    ip = client_ip(request)
    if jti:
        ttl = max(1, int(payload.get("exp") or 0) - int(datetime.now(timezone.utc).timestamp()))
        won_grace = await redis.set(refresh_grace_key(jti), new_token, ex=10, nx=True)
        if not won_grace:
            cached_token = await redis.get(refresh_grace_key(jti))
            if cached_token:
                return {"access_token": cached_token, "token_type": "bearer"}
        await redis.set(session_blacklist_key(jti), "1", ex=ttl)
    db.add(
        AuditLog(
            actor_type="auth",
            actor_id=user.id,
            action="auth.session_refresh",
            target_type="session",
            target_id=new_jti,
            payload={"old_jti": jti, "new_jti": new_jti},
            ip=ip,
        )
    )
    await db.commit()
    return {"access_token": new_token, "token_type": "bearer"}
