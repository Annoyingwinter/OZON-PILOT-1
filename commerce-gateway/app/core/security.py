import hashlib
import hmac
import secrets
from datetime import datetime, timedelta, timezone
from functools import lru_cache
from typing import Any

import jwt
from passlib.context import CryptContext

from app.core.config import get_settings

_dummy_password_hash: str | None = None


@lru_cache
def get_pwd_context() -> CryptContext:
    settings = get_settings()
    return CryptContext(
        schemes=["bcrypt"],
        bcrypt__default_rounds=settings.bcrypt_rounds,
        deprecated="auto",
    )


def get_dummy_password_hash() -> str:
    global _dummy_password_hash
    if _dummy_password_hash is None:
        _dummy_password_hash = get_pwd_context().hash(secrets.token_urlsafe(16))
    return _dummy_password_hash


def hash_password(password: str) -> str:
    return get_pwd_context().hash(password)


def verify_password(password: str, password_hash: str) -> bool:
    return get_pwd_context().verify(password, password_hash)


def create_session_token(subject: str, role: str, minutes: int = 30) -> str:
    settings = get_settings()
    now = datetime.now(timezone.utc)
    payload: dict[str, Any] = {
        "sub": subject,
        "role": role,
        "jti": secrets.token_urlsafe(16),
        "iat": int(now.timestamp()),
        "exp": int((now + timedelta(minutes=minutes)).timestamp()),
    }
    return jwt.encode(payload, settings.jwt_secret, algorithm="HS256")


def create_run_token(run_id: str, user_id: str, minutes: int = 15) -> str:
    settings = get_settings()
    now = datetime.now(timezone.utc)
    payload: dict[str, Any] = {
        "sub": run_id,
        "user_id": user_id,
        "scope": "automation_run",
        "iat": int(now.timestamp()),
        "exp": int((now + timedelta(minutes=minutes)).timestamp()),
    }
    return jwt.encode(payload, settings.jwt_secret, algorithm="HS256")


def decode_session_token(token: str) -> dict[str, Any]:
    settings = get_settings()
    return jwt.decode(token, settings.jwt_secret, algorithms=["HS256"])


def session_blacklist_key(jti: str) -> str:
    return f"session_blacklist:{jti}"


def refresh_grace_key(jti: str) -> str:
    return f"refresh_grace:{jti}"


def generate_api_key(prefix: str = "sk_live") -> tuple[str, str, str]:
    raw = f"{prefix}_{secrets.token_urlsafe(32)}"
    return raw, raw[:16], hash_api_key(raw)


def hash_api_key(raw_key: str) -> str:
    settings = get_settings()
    return hmac.new(
        settings.api_key_hash_secret.encode("utf-8"),
        raw_key.encode("utf-8"),
        hashlib.sha256,
    ).hexdigest()

def constant_time_equal(left: str, right: str) -> bool:
    return hmac.compare_digest(left, right)
