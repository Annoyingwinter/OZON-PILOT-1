import pytest
import jwt
from fastapi import HTTPException
from fastapi.responses import RedirectResponse
from starlette.requests import Request

from app.auth.router import refresh
from app.auth.dependencies import get_session_user_from_token, require_admin_from_token
from app.admin_ui.router import _set_admin_cookie, require_admin_cookie
from app.core.config import get_settings
from app.core.request_utils import client_ip, mask_email, stable_audit_id
from app.core.security import (
    create_session_token,
    decode_session_token,
    get_dummy_password_hash,
    get_pwd_context,
    session_blacklist_key,
)
from app.db.models import User


class FakeDb:
    def __init__(self, user):
        self.user = user
        self.added = []
        self.commits = 0

    async def scalar(self, _statement):
        return self.user

    def add(self, row):
        self.added.append(row)

    async def commit(self):
        self.commits += 1


class FakeRedis:
    def __init__(self, revoked_keys=None):
        self.revoked_keys = set(revoked_keys or [])
        self.values = {}

    async def exists(self, _key):
        return 1 if _key in self.revoked_keys else 0

    async def get(self, key):
        return self.values.get(key)

    async def set(self, key, value, ex=None, nx=False):
        if nx and key in self.values:
            return False
        self.values[key] = value
        if key.startswith("session_blacklist:"):
            self.revoked_keys.add(key)
        return True

    async def eval(self, _script, _numkeys, *_args):
        return [1, 1]


def make_request(*, token=None, forwarded_for=None, real_ip=None, client_host="127.0.0.1"):
    headers = []
    if token:
        headers.append((b"cookie", f"admin_token={token}".encode("utf-8")))
    if forwarded_for:
        headers.append((b"x-forwarded-for", forwarded_for.encode("utf-8")))
    if real_ip:
        headers.append((b"x-real-ip", real_ip.encode("utf-8")))
    return Request({"type": "http", "headers": headers, "client": (client_host, 12345)})


@pytest.mark.asyncio
async def test_require_admin_from_token_accepts_admin():
    user = User(id="u1", email="admin@example.com", password_hash="x", role="admin", status="active")
    token = create_session_token(user.id, user.role)

    session = await require_admin_from_token(token, FakeDb(user))

    assert session.id == "u1"
    assert session.role == "admin"


@pytest.mark.asyncio
async def test_require_admin_from_token_rejects_customer():
    user = User(id="u1", email="user@example.com", password_hash="x", role="customer", status="active")
    token = create_session_token(user.id, user.role)

    with pytest.raises(HTTPException) as exc:
        await require_admin_from_token(token, FakeDb(user))

    assert exc.value.status_code == 403


def test_session_token_has_jti_for_admin_revocation():
    token = create_session_token("u1", "admin")
    payload = decode_session_token(token)

    assert payload["jti"]


def test_admin_cookie_is_scoped_to_admin_path():
    response = RedirectResponse("/admin/ui")

    _set_admin_cookie(response, "token")

    cookie_header = response.headers["set-cookie"]
    assert "admin_token=token" in cookie_header
    assert "HttpOnly" in cookie_header
    assert "Path=/admin" in cookie_header
    assert "SameSite=lax" in cookie_header


@pytest.mark.asyncio
async def test_admin_cookie_without_jti_is_rejected():
    settings = get_settings()
    token = jwt.encode({"sub": "u1", "role": "admin"}, settings.jwt_secret, algorithm="HS256")
    user = User(id="u1", email="admin@example.com", password_hash="x", role="admin", status="active")

    session = await require_admin_cookie(make_request(token=token), FakeDb(user), FakeRedis())

    assert session is None


def test_client_ip_ignores_forwarded_for_by_default():
    request = make_request(forwarded_for="203.0.113.7")

    assert client_ip(request) == "127.0.0.1"


def test_client_ip_uses_real_ip_only_when_proxy_headers_trusted():
    settings = get_settings()
    previous = settings.trust_proxy_headers
    settings.trust_proxy_headers = True
    try:
        request = make_request(forwarded_for="203.0.113.7", real_ip="198.51.100.9")

        assert client_ip(request) == "198.51.100.9"
    finally:
        settings.trust_proxy_headers = previous


def test_client_ip_ignores_proxy_headers_from_untrusted_source():
    settings = get_settings()
    previous = settings.trust_proxy_headers
    settings.trust_proxy_headers = True
    try:
        request = make_request(
            forwarded_for="203.0.113.7",
            real_ip="198.51.100.9",
            client_host="198.51.100.10",
        )

        assert client_ip(request) == "198.51.100.10"
    finally:
        settings.trust_proxy_headers = previous


@pytest.mark.asyncio
async def test_session_blacklist_applies_to_bearer_tokens():
    user = User(id="u1", email="admin@example.com", password_hash="x", role="admin", status="active")
    token = create_session_token(user.id, user.role)
    jti = decode_session_token(token)["jti"]

    with pytest.raises(HTTPException) as exc:
        await get_session_user_from_token(token, FakeDb(user), FakeRedis({session_blacklist_key(jti)}))

    assert exc.value.status_code == 401


@pytest.mark.asyncio
async def test_refresh_grace_returns_same_new_token_for_parallel_retry():
    user = User(id="u1", email="admin@example.com", password_hash="x", role="admin", status="active")
    old_token = create_session_token(user.id, user.role)
    request = make_request()
    db = FakeDb(user)
    redis = FakeRedis()

    first = await refresh(request, f"Bearer {old_token}", db, redis)
    second = await refresh(request, f"Bearer {old_token}", db, redis)

    assert second["access_token"] == first["access_token"]
    old_jti = decode_session_token(old_token)["jti"]
    assert session_blacklist_key(old_jti) in redis.revoked_keys


def test_stable_audit_id_hashes_email_without_plaintext():
    audit_id = stable_audit_id("VeryLongAdminAddress@example.com")

    assert audit_id == stable_audit_id("verylongadminaddress@example.com")
    assert "example" not in audit_id


def test_dummy_password_hash_uses_current_bcrypt_context():
    dummy_hash = get_dummy_password_hash()
    configured_rounds = get_pwd_context().handler("bcrypt").default_rounds

    assert dummy_hash.startswith(f"$2b${configured_rounds:02d}$")


def test_mask_email_edges():
    assert mask_email("a@b.com") == "a***@b.com"
    assert mask_email("longname@example.com") == "lo***@example.com"
    assert mask_email("noatsign") == "noa***"
    assert mask_email("") == "***"
