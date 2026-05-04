from fastapi import HTTPException, status


class ErrorCode:
    UNAUTHORIZED = "UNAUTHORIZED"
    PLAN_REQUIRED = "PLAN_REQUIRED"
    SUBSCRIPTION_INACTIVE = "SUBSCRIPTION_INACTIVE"
    RATE_LIMITED = "RATE_LIMITED"
    QUOTA_EXCEEDED = "QUOTA_EXCEEDED"
    UPSTREAM_ERROR = "UPSTREAM_ERROR"
    INVALID_INPUT = "INVALID_INPUT"
    NOT_IMPLEMENTED = "NOT_IMPLEMENTED"


def api_error(http_status: int, code: str, message: str, request_id: str | None = None):
    detail = {"error": {"code": code, "message": message, "request_id": request_id}}
    raise HTTPException(status_code=http_status, detail=detail)


def unauthorized(message: str = "Invalid credentials"):
    api_error(status.HTTP_401_UNAUTHORIZED, ErrorCode.UNAUTHORIZED, message)
