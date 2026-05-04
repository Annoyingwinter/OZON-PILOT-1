from datetime import datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import get_settings
from app.core.security import hash_password
from app.db.models import Plan, User


async def seed_defaults(db: AsyncSession):
    plans = [
        ("free", "Free", 0, 100, 1, 10),
        ("starter", "Starter", 9900, 5000, 5, 20),
        ("pro", "Pro", 39900, 30000, 20, 30),
        ("extra_10k", "Extra Pack 10k", 5000, 10000, 0, 100),
    ]
    for code, name, price_cents, quota_calls, qps_limit, sort_order in plans:
        existing = await db.scalar(select(Plan).where(Plan.code == code))
        if not existing:
            db.add(
                Plan(
                    code=code,
                    name=name,
                    price_cents=price_cents,
                    quota_calls=quota_calls,
                    qps_limit=qps_limit,
                    sort_order=sort_order,
                    features={},
                )
            )

    settings = get_settings()
    admin = await db.scalar(select(User).where(User.email == settings.default_admin_email))
    if not admin:
        db.add(
            User(
                email=settings.default_admin_email,
                password_hash=hash_password(settings.default_admin_password),
                role="admin",
                status="active",
                realname_status="verified",
                created_at=datetime.utcnow(),
            )
        )
    await db.commit()
