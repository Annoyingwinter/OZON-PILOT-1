import uuid

from fastapi import Depends, Header, Request, status
from redis.asyncio import Redis
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.cache.redis import get_redis
from app.core.errors import ErrorCode, api_error
from app.core.request_context import ApiClientContext
from app.core.security import hash_api_key
from app.db.models import ApiKey, Plan, Quota, RequestReservation, Subscription, UsageLog
from app.db.session import get_db
from app.metering.quota import QuotaMeter
from app.metering.rate_limit import RateLimiter


async def require_api_client(
    request: Request,
    authorization: str | None = Header(default=None),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
) -> ApiClientContext:
    request_id = request.headers.get("X-Request-Id") or str(uuid.uuid4())
    request.state.request_id = request_id

    if not authorization or not authorization.startswith("Bearer "):
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Missing API key", request_id)

    raw_key = authorization.removeprefix("Bearer ").strip()
    key_hash = hash_api_key(raw_key)
    cache_key = f"api_key:{key_hash}"
    cached = await redis.hgetall(cache_key)

    if cached:
        return ApiClientContext(
            user_id=cached["user_id"],
            api_key_id=cached["api_key_id"],
            subscription_id=cached["subscription_id"],
            plan_code=cached["plan_code"],
            quota_period_key=cached["quota_period_key"],
            rate_limit_per_minute=int(cached["rate_limit_per_minute"]),
        )

    stmt = (
        select(ApiKey, Subscription, Plan, Quota)
        .join(Subscription, Subscription.user_id == ApiKey.user_id)
        .join(Plan, Plan.id == Subscription.plan_id)
        .join(Quota, Quota.subscription_id == Subscription.id)
        .where(ApiKey.key_hash == key_hash)
        .where(ApiKey.revoked_at.is_(None))
        .where(Subscription.status == "active")
        .where(Plan.is_active.is_(True))
        .order_by(Subscription.current_period_end.desc())
        .limit(1)
    )
    row = (await db.execute(stmt)).first()
    if not row:
        api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, "Invalid API key", request_id)

    api_key, subscription, plan, quota = row
    context = ApiClientContext(
        user_id=api_key.user_id,
        api_key_id=api_key.id,
        subscription_id=subscription.id,
        plan_code=plan.code,
        quota_period_key=quota.period_key,
        rate_limit_per_minute=plan.qps_limit * 60,
    )
    await redis.hset(
        cache_key,
        mapping={
            "user_id": context.user_id,
            "api_key_id": context.api_key_id,
            "subscription_id": context.subscription_id,
            "plan_code": context.plan_code,
            "quota_period_key": context.quota_period_key,
            "rate_limit_per_minute": str(context.rate_limit_per_minute),
        },
    )
    await redis.expire(cache_key, 60)
    await redis.setnx(
        f"quota:{context.subscription_id}:{context.quota_period_key}",
        max(0, quota.quota_total + quota.quota_extra - quota.quota_used),
    )
    return context


async def enforce_metering(
    request: Request,
    context: ApiClientContext = Depends(require_api_client),
    redis: Redis = Depends(get_redis),
    db: AsyncSession = Depends(get_db),
) -> ApiClientContext:
    request_id = request.state.request_id
    limiter = RateLimiter(redis)

    api_allowed, _ = await limiter.allow(
        f"rl:api_key:{context.api_key_id}:60s", context.rate_limit_per_minute
    )
    user_allowed, _ = await limiter.allow(
        f"rl:user:{context.user_id}:60s", int(context.rate_limit_per_minute * 1.5)
    )
    if not api_allowed or not user_allowed:
        db.add(
            UsageLog(
                user_id=context.user_id,
                api_key_id=context.api_key_id,
                endpoint=str(request.url.path),
                status_code=429,
                billable_units=0,
                request_id=request_id,
                ip=request.client.host if request.client else None,
                error_code=ErrorCode.RATE_LIMITED,
            )
        )
        await db.commit()
        api_error(status.HTTP_429_TOO_MANY_REQUESTS, ErrorCode.RATE_LIMITED, "Rate limited", request_id)

    units = int(request.headers.get("X-Billable-Units", "1"))
    quota_key = f"quota:{context.subscription_id}:{context.quota_period_key}"
    reservation_key = f"quota_reservation:{request_id}"
    db.add(
        RequestReservation(
            id=request_id,
            user_id=context.user_id,
            api_key_id=context.api_key_id,
            subscription_id=context.subscription_id,
            period_key=context.quota_period_key,
            endpoint=str(request.url.path),
            units=units,
            status="creating",
        )
    )
    await db.commit()

    allowed, _remaining = await QuotaMeter(redis).reserve(quota_key, reservation_key, units)
    request.state.quota_key = quota_key
    request.state.reservation_key = reservation_key
    request.state.billable_units = units
    if not allowed:
        await db.execute(
            update(RequestReservation)
            .where(RequestReservation.id == request_id)
            .where(RequestReservation.status == "creating")
            .values(status="rejected")
        )
        db.add(
            UsageLog(
                user_id=context.user_id,
                api_key_id=context.api_key_id,
                endpoint=str(request.url.path),
                status_code=402,
                billable_units=0,
                request_id=request_id,
                ip=request.client.host if request.client else None,
                error_code=ErrorCode.QUOTA_EXCEEDED,
            )
        )
        await db.commit()
        api_error(status.HTTP_402_PAYMENT_REQUIRED, ErrorCode.QUOTA_EXCEEDED, "Quota exceeded", request_id)

    await db.execute(
        update(RequestReservation)
        .where(RequestReservation.id == request_id)
        .where(RequestReservation.status == "creating")
        .values(status="reserved")
    )
    await db.commit()

    return context
