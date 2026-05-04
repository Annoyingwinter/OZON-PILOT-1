from pathlib import Path
from datetime import datetime, timedelta
import secrets

import jwt
from fastapi import APIRouter, Depends, Form, HTTPException, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from jinja2 import select_autoescape
from redis.asyncio import Redis
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth.dependencies import require_admin_from_token
from app.cache.redis import get_redis
from app.core.config import get_settings
from app.core.request_utils import client_ip, mask_email, stable_audit_id
from app.core.security import (
    constant_time_equal,
    create_session_token,
    decode_session_token,
    session_blacklist_key,
    verify_password,
)
from app.db.models import AuditLog, License, Subscription, UsageLog, User
from app.db.session import get_db
from app.metering.rate_limit import RateLimiter

router = APIRouter(prefix="/admin/ui", tags=["admin-ui"])
settings = get_settings()
templates = Jinja2Templates(directory=str(Path(__file__).resolve().parents[1] / "templates"))
templates.env.autoescape = select_autoescape(default_for_string=True, default=True)


def _new_csrf_token() -> str:
    return secrets.token_urlsafe(32)


def _csrf_valid(request: Request, submitted: str) -> bool:
    cookie_value = request.cookies.get("admin_csrf") or ""
    return bool(cookie_value and submitted and constant_time_equal(cookie_value, submitted))


def _set_admin_cookie(response: RedirectResponse, token: str) -> None:
    response.set_cookie(
        "admin_token",
        token,
        httponly=True,
        samesite="lax",
        secure=settings.app_env != "local",
        path="/admin",
        max_age=8 * 60 * 60,
    )


def _set_csrf_cookie(response: HTMLResponse | RedirectResponse, token: str) -> None:
    response.set_cookie(
        "admin_csrf",
        token,
        httponly=False,
        samesite="lax",
        secure=settings.app_env != "local",
        path="/admin",
        max_age=8 * 60 * 60,
    )


async def require_admin_cookie(
    request: Request,
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    token = request.cookies.get("admin_token")
    if not token:
        return None
    try:
        payload = decode_session_token(token)
        jti = str(payload.get("jti") or "")
        if not jti:
            return None
        if await redis.exists(session_blacklist_key(jti)):
            return None
        return await require_admin_from_token(token, db, redis)
    except (HTTPException, jwt.ExpiredSignatureError, jwt.InvalidTokenError):
        return None


@router.get("", response_class=HTMLResponse)
async def dashboard(request: Request, db: AsyncSession = Depends(get_db), admin=Depends(require_admin_cookie)):
    if not admin:
        return RedirectResponse("/admin/ui/login", status_code=302)
    since = datetime.utcnow() - timedelta(hours=24)
    users_count = await db.scalar(select(func.count(User.id)))
    usage_count = await db.scalar(select(func.count(UsageLog.id)).where(UsageLog.ts >= since))
    usage_units = await db.scalar(
        select(func.coalesce(func.sum(UsageLog.billable_units), 0)).where(UsageLog.ts >= since)
    )
    error_count = await db.scalar(
        select(func.count(UsageLog.id)).where(UsageLog.ts >= since).where(UsageLog.error_code.is_not(None))
    )
    upstream_error_count = await db.scalar(
        select(func.count(UsageLog.id)).where(UsageLog.ts >= since).where(UsageLog.error_code == "UPSTREAM_ERROR")
    )
    license_count = await db.scalar(select(func.count(License.id)))
    subscription_count = await db.scalar(select(func.count(Subscription.id)))
    usage = (await db.execute(select(UsageLog).order_by(UsageLog.ts.desc()).limit(20))).scalars().all()
    usage_count_value = usage_count or 0
    error_count_value = error_count or 0
    return templates.TemplateResponse(
        "admin/dashboard.html",
        {
            "request": request,
            "admin": admin,
            "users_count": users_count or 0,
            "usage_count": usage_count_value,
            "usage_units": usage_units or 0,
            "error_count": error_count_value,
            "error_rate": round((error_count_value / usage_count_value) * 100, 2) if usage_count_value else 0,
            "upstream_error_count": upstream_error_count or 0,
            "license_count": license_count or 0,
            "subscription_count": subscription_count or 0,
            "usage": usage,
        },
    )


@router.get("/login", response_class=HTMLResponse)
async def login_page(request: Request):
    csrf_token = _new_csrf_token()
    response = templates.TemplateResponse(
        "admin/login.html",
        {"request": request, "error": None, "csrf_token": csrf_token},
    )
    _set_csrf_cookie(response, csrf_token)
    return response


@router.post("/login")
async def login_submit(
    request: Request,
    email: str = Form(...),
    password: str = Form(...),
    csrf_token: str = Form(...),
    db: AsyncSession = Depends(get_db),
    redis: Redis = Depends(get_redis),
):
    if not _csrf_valid(request, csrf_token):
        raise HTTPException(status_code=403, detail="Invalid CSRF token")

    ip = client_ip(request)
    limiter = RateLimiter(redis)
    audit_id = stable_audit_id(email)
    audit_payload = {"email_hint": mask_email(email)} if settings.audit_include_email_hint else {}
    ip_allowed, _ = await limiter.allow(f"rl:admin_login:ip:{ip}:60s", 10, 60)
    email_allowed, _ = await limiter.allow(f"rl:admin_login:email:{email.lower()}:60s", 5, 60)
    if not ip_allowed or not email_allowed:
        db.add(
            AuditLog(
                actor_type="admin_ui",
                actor_id=audit_id,
                action="admin.login_rate_limited",
                target_type="admin_session",
                target_id=audit_id,
                payload=audit_payload,
                ip=ip,
            )
        )
        await db.commit()
        return templates.TemplateResponse(
            "admin/login.html",
            {"request": request, "error": "Too many login attempts", "csrf_token": csrf_token},
            status_code=429,
        )

    user = await db.scalar(select(User).where(User.email == email).where(User.role == "admin"))
    if not user or not verify_password(password, user.password_hash):
        db.add(
            AuditLog(
                actor_type="admin_ui",
                actor_id=audit_id,
                action="admin.login_failed",
                target_type="admin_session",
                target_id=audit_id,
                payload=audit_payload,
                ip=ip,
            )
        )
        await db.commit()
        return templates.TemplateResponse(
            "admin/login.html",
            {"request": request, "error": "Invalid admin credentials", "csrf_token": csrf_token},
            status_code=401,
        )
    response = RedirectResponse("/admin/ui", status_code=302)
    _set_admin_cookie(response, create_session_token(user.id, user.role, minutes=480))
    return response


@router.post("/logout")
async def logout(
    request: Request,
    csrf_token: str = Form(...),
    redis: Redis = Depends(get_redis),
):
    if not _csrf_valid(request, csrf_token):
        raise HTTPException(status_code=403, detail="Invalid CSRF token")

    token = request.cookies.get("admin_token")
    if token:
        try:
            payload = decode_session_token(token)
            jti = str(payload.get("jti") or "")
            exp = int(payload.get("exp") or 0)
            ttl = max(1, exp - int(datetime.utcnow().timestamp()))
            if jti:
                await redis.set(session_blacklist_key(jti), "1", ex=ttl)
        except (jwt.ExpiredSignatureError, jwt.InvalidTokenError):
            pass

    response = RedirectResponse("/admin/ui/login", status_code=302)
    response.delete_cookie("admin_token", path="/admin")
    response.delete_cookie("admin_token")
    response.delete_cookie("admin_csrf", path="/admin")
    return response


@router.get("/users", response_class=HTMLResponse)
async def users_page(request: Request, db: AsyncSession = Depends(get_db), admin=Depends(require_admin_cookie)):
    if not admin:
        return RedirectResponse("/admin/ui/login", status_code=302)
    rows = (await db.execute(select(User).order_by(User.created_at.desc()).limit(200))).scalars().all()
    return templates.TemplateResponse("admin/users.html", {"request": request, "admin": admin, "rows": rows})


@router.get("/usage", response_class=HTMLResponse)
async def usage_page(request: Request, db: AsyncSession = Depends(get_db), admin=Depends(require_admin_cookie)):
    if not admin:
        return RedirectResponse("/admin/ui/login", status_code=302)
    rows = (await db.execute(select(UsageLog).order_by(UsageLog.ts.desc()).limit(300))).scalars().all()
    return templates.TemplateResponse("admin/usage.html", {"request": request, "admin": admin, "rows": rows})


@router.get("/subscriptions", response_class=HTMLResponse)
async def subscriptions_page(
    request: Request,
    db: AsyncSession = Depends(get_db),
    admin=Depends(require_admin_cookie),
):
    if not admin:
        return RedirectResponse("/admin/ui/login", status_code=302)
    rows = (await db.execute(select(Subscription).order_by(Subscription.created_at.desc()).limit(200))).scalars().all()
    return templates.TemplateResponse(
        "admin/subscriptions.html", {"request": request, "admin": admin, "rows": rows}
    )


@router.get("/licenses", response_class=HTMLResponse)
async def licenses_page(request: Request, db: AsyncSession = Depends(get_db), admin=Depends(require_admin_cookie)):
    if not admin:
        return RedirectResponse("/admin/ui/login", status_code=302)
    rows = (await db.execute(select(License).order_by(License.created_at.desc()).limit(200))).scalars().all()
    return templates.TemplateResponse("admin/licenses.html", {"request": request, "admin": admin, "rows": rows})
