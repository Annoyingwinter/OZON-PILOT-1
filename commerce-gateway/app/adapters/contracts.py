from typing import Any, Literal

from pydantic import BaseModel, Field


class BusinessRequest(BaseModel):
    capability: str
    payload: dict[str, Any] = Field(default_factory=dict)
    request_id: str
    user_id: str
    api_key_id: str | None = None


class BusinessResponse(BaseModel):
    status: Literal["succeeded", "pending", "failed"]
    result: dict[str, Any] = Field(default_factory=dict)
    error_code: str | None = None
    error_message: str | None = None
    billable_units: int = 1


class ImageUploadResponse(BaseModel):
    url: str
    file: str | None = None
