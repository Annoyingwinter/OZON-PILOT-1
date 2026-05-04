from datetime import datetime

from fastapi import APIRouter, Depends
from pydantic import BaseModel
from redis.asyncio import Redis
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth.dependencies import SessionUser, require_session_user
from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.errors import ErrorCode, api_error
from app.core.security import generate_api_key
from app.db.models import ApiKey, User
from app.db.session import get_db

router = APIRouter(prefix="/v1/keys", tags=["api-keys"])


class CreateKeyRequest(BaseModel):
    name: str = "default"


@router.post("")
async def create_key(
    payload: CreateKeyRequest,
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
):
    settings = get_settings()
    if settings.require_realname_for_api_keys:
        record_user = await db.scalar(select(User).where(User.id == user.id))
        if not record_user or record_user.realname_status != "verified":
            api_error(403, ErrorCode.PLAN_REQUIRED, "Real-name verification required")

    raw_key, key_prefix, key_hash = generate_api_key()
    record = ApiKey(user_id=user.id, key_prefix=key_prefix, key_hash=key_hash, name=payload.name)
    db.add(record)
    await db.commit()
    return {"id": record.id, "name": record.name, "key": raw_key, "key_prefix": key_prefix}


@router.get("")
async def list_keys(
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
):
    rows = (await db.execute(select(ApiKey).where(ApiKey.user_id == user.id))).scalars().all()
    return [
        {
            "id": row.id,
            "name": row.name,
            "key_prefix": row.key_prefix,
            "last_used_at": row.last_used_at,
            "revoked_at": row.revoked_at,
            "created_at": row.created_at,
        }
        for row in rows
    ]


@router.delete("/{key_id}")
async def revoke_key(
    key_id: str,
    user: SessionUser = Depends(require_session_user),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    record = await db.scalar(select(ApiKey).where(ApiKey.id == key_id).where(ApiKey.user_id == user.id))
    if not record:
        return {"ok": True}
    record.revoked_at = datetime.utcnow()
    await db.commit()
    await redis.delete(f"api_key:{record.key_hash}")
    await redis.publish("api_key_revoked", record.key_hash)
    return {"ok": True}
