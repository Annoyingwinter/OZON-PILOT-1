import hashlib
from ipaddress import ip_address, ip_network

from fastapi import Request

from app.core.config import get_settings


def client_ip(request: Request) -> str:
    settings = get_settings()
    if settings.trust_proxy_headers and request_from_trusted_proxy(request, settings.trusted_proxies):
        real_ip = request.headers.get("X-Real-IP")
        if real_ip:
            return real_ip.strip()
        forwarded_for = request.headers.get("X-Forwarded-For")
        if forwarded_for:
            return forwarded_for.split(",", 1)[0].strip()
    return request.client.host if request.client else "unknown"


def request_from_trusted_proxy(request: Request, trusted_proxies: str) -> bool:
    if not request.client:
        return False
    try:
        client_addr = ip_address(request.client.host)
    except ValueError:
        return False
    for item in trusted_proxies.split(","):
        cidr = item.strip()
        if not cidr:
            continue
        try:
            if client_addr in ip_network(cidr, strict=False):
                return True
        except ValueError:
            continue
    return False


def proxy_debug_info(request: Request) -> dict:
    settings = get_settings()
    trusted = request_from_trusted_proxy(request, settings.trusted_proxies)
    return {
        "request_client_host": request.client.host if request.client else None,
        "x_real_ip": request.headers.get("X-Real-IP"),
        "x_forwarded_for": request.headers.get("X-Forwarded-For"),
        "trust_proxy_headers": settings.trust_proxy_headers,
        "trusted_proxies": settings.trusted_proxies,
        "request_from_trusted_proxy": trusted,
        "resolved_client_ip": client_ip(request),
    }


def stable_audit_id(value: str) -> str:
    canonical = value.strip().lower()
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()[:32]


def mask_email(value: str) -> str:
    email = value.strip().lower()
    if "@" not in email:
        return email[:3] + "***" if len(email) > 3 else "***"
    local, domain = email.split("@", 1)
    visible = local[:2] if len(local) > 2 else local[:1]
    return f"{visible}***@{domain}"
