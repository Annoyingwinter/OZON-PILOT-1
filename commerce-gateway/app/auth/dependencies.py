from dataclasses import dataclass

import jwt
from fastapi import Depends, Header, Request, status
from redis.asyncio import Redis
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error
from app.core.security import decode_session_token, session_blacklist_key
from app.db.models import User
from app.db.session import get_db
from app.metering.rate_limit import RateLimiter


@dataclass(frozen=True)
class SessionUser:
    id: str
    email: str
    role: str
    jti: str
    exp: int


async def get_session_user_from_token(
    token: str,
    db: AsyncSession,
    redis: Redis | None = None,
) -> SessionUser:
    try:
        payload = decode_session_token(token)
    except jwt.ExpiredSignatureError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Session token expired")
    except jwt.InvalidTokenError:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid session token")

    jti = str(payload.get("jti") or "")
    if not jti and get_settings().enforce_jti:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Token missing jti")
    if jti and redis and await redis.exists(session_blacklist_key(jti)):
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Token revoked")

    user_id = str(payload.get("sub") or "")
    user = await db.scalar(select(User).where(User.id == user_id).where(User.status == "active"))
    if not user:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "User is not active")

    return SessionUser(id=user.id, email=user.email, role=user.role, jti=jti, exp=int(payload.get("exp") or 0))


async def require_session_user(
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
) -> SessionUser:
    if not authorization or not authorization.startswith("Bearer "):
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Missing session token")

    return await get_session_user_from_token(authorization.removeprefix("Bearer ").strip(), db, redis)


async def require_admin(user: SessionUser = Depends(require_session_user)) -> SessionUser:
    if user.role != "admin":
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.UNAUTHORIZED, "Admin role required")
    return user


async def require_admin_action(
    request: Request,
    user: SessionUser = Depends(require_admin),
    redis: Redis = Depends(get_redis),
) -> SessionUser:
    if request.method == "OPTIONS":
        return user
    if request.method in {"POST", "PUT", "PATCH", "DELETE"}:
        allowed, _ = await RateLimiter(redis).allow(f"rl:admin_write:{user.id}:60s", 30, 60)
    else:
        allowed, _ = await RateLimiter(redis).allow(f"rl:admin_read:{user.id}:60s", 200, 60)
    if not allowed:
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Admin action rate limited")
    return user


async def require_admin_from_token(
    token: str,
    db: AsyncSession,
    redis: Redis | None = None,
) -> SessionUser:
    user = await get_session_user_from_token(token, db, redis)
    if user.role != "admin":
        api_error(status.HTTP_403_FORBIDDEN, ErrorCode.UNAUTHORIZED, "Admin role required")
    return user
