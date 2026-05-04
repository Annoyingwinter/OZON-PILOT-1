from datetime import datetime, timedelta

from redis.asyncio import Redis
from sqlalchemy import and_, func, or_, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models import ActionPermit, AutomationRun, Quota, RequestReservation
from app.metering.quota import QuotaMeter


async def reconcile_quotas_once(db: AsyncSession, redis: Redis):
    """Make Redis hot quota no larger than durable Postgres quota.

    This worker intentionally clamps Redis down from Postgres instead of trusting Redis.
    It prevents Redis restart/OOM from gifting quota after PG already recorded usage.
    """
    reserved_expiry = datetime.utcnow() - timedelta(minutes=15)
    creating_expiry = datetime.utcnow() - timedelta(seconds=60)
    stale_requests = (
        await db.execute(
            select(RequestReservation).where(
                or_(
                    and_(
                        RequestReservation.status == "reserved",
                        RequestReservation.created_at < reserved_expiry,
                    ),
                    and_(
                        RequestReservation.status == "creating",
                        RequestReservation.created_at < creating_expiry,
                    ),
                )
            )
        )
    ).scalars()
    meter = QuotaMeter(redis)
    for reservation in stale_requests:
        await meter.release(
            f"quota:{reservation.subscription_id}:{reservation.period_key}",
            f"quota_reservation:{reservation.id}",
        )
        await db.execute(
            update(RequestReservation)
            .where(RequestReservation.id == reservation.id)
            .where(RequestReservation.status.in_(["reserved", "creating"]))
            .values(status="expired", completed_at=datetime.utcnow())
        )

    dead_run_cutoff = datetime.utcnow() - timedelta(minutes=5)
    stale_permits = (
        await db.execute(
            select(ActionPermit)
            .join(AutomationRun, AutomationRun.id == ActionPermit.run_id)
            .where(
                or_(
                    and_(
                        ActionPermit.status == "creating",
                        ActionPermit.created_at < creating_expiry,
                    ),
                    and_(
                        ActionPermit.status == "reserved",
                        ActionPermit.created_at < reserved_expiry,
                        AutomationRun.heartbeat_at < dead_run_cutoff,
                    ),
                )
            )
        )
    ).scalars()
    for permit in stale_permits:
        if permit.subscription_id and permit.period_key:
            await meter.release(
                f"quota:{permit.subscription_id}:{permit.period_key}",
                f"quota_reservation:permit:{permit.id}",
            )
        else:
            await redis.delete(f"quota_reservation:permit:{permit.id}")
        await db.execute(
            update(ActionPermit)
            .where(ActionPermit.id == permit.id)
            .where(ActionPermit.status.in_(["reserved", "creating"]))
            .values(status="expired", completed_at=datetime.utcnow())
        )

    await db.commit()

    quotas = (await db.execute(select(Quota))).scalars().all()
    for quota in quotas:
        request_inflight = await db.scalar(
            select(func.coalesce(func.sum(RequestReservation.units), 0))
            .where(RequestReservation.subscription_id == quota.subscription_id)
            .where(RequestReservation.period_key == quota.period_key)
            .where(RequestReservation.status == "reserved")
        )
        permit_inflight = await db.scalar(
            select(func.coalesce(func.sum(ActionPermit.units), 0))
            .where(ActionPermit.subscription_id == quota.subscription_id)
            .where(ActionPermit.period_key == quota.period_key)
            .where(ActionPermit.status == "reserved")
        )
        remaining = max(
            0,
            quota.quota_total
            + quota.quota_extra
            - quota.quota_used
            - int(request_inflight or 0)
            - int(permit_inflight or 0),
        )
        await redis.set(f"quota:{quota.subscription_id}:{quota.period_key}", remaining)
