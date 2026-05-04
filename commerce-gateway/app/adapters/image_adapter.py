from fastapi import UploadFile
import httpx

from app.adapters.contracts import BusinessResponse, ImageUploadResponse
from app.core.config import get_settings


class ImageAdapter:
    def __init__(self):
        self.settings = get_settings()

    async def upload(self, file: UploadFile, request_id: str) -> BusinessResponse:
        url = self.settings.image_adapter_base_url.rstrip("/") + self.settings.image_adapter_upload_path
        headers = {"X-Request-Id": request_id}
        if self.settings.image_adapter_upload_token:
            headers["X-Upload-Token"] = self.settings.image_adapter_upload_token

        data = await file.read()
        async with httpx.AsyncClient(timeout=self.settings.default_request_timeout_seconds) as client:
            response = await client.post(
                url,
                headers=headers,
                files={"file": (file.filename or "image.jpg", data, file.content_type or "image/jpeg")},
            )
            response.raise_for_status()

        payload = ImageUploadResponse.model_validate(response.json())
        return BusinessResponse(status="succeeded", result=payload.model_dump())

    async def generate_placeholder(self, payload: dict, request_id: str) -> BusinessResponse:
        if not self.settings.image_generation_endpoint:
            return BusinessResponse(
                status="failed",
                error_code="IMAGE_GENERATION_NOT_CONFIGURED",
                error_message="Image generation provider endpoint is not configured.",
                result={"request_id": request_id, "accepted_payload": payload},
            )

        async with httpx.AsyncClient(timeout=self.settings.default_request_timeout_seconds) as client:
            response = await client.post(
                self.settings.image_generation_endpoint,
                headers={"X-Request-Id": request_id},
                json=payload,
            )
            response.raise_for_status()

        return BusinessResponse(status="succeeded", result=response.json())
