from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

    app_env: str = "local"
    public_base_url: str = "http://127.0.0.1:8000"
    trust_proxy_headers: bool = False
    trusted_proxies: str = "127.0.0.1/32,::1/128"

    database_url: str = Field(repr=False)
    redis_url: str = Field(repr=False)

    jwt_secret: str = Field(min_length=24, repr=False)
    api_key_hash_secret: str = Field(min_length=24, repr=False)
    internal_adapter_token: str = Field(min_length=16, repr=False)

    image_adapter_base_url: str = Field(default="http://127.0.0.1:8088", repr=False)
    image_adapter_upload_path: str = Field(default="/ozon-upload", repr=False)
    image_adapter_upload_token: str = Field(default="", repr=False)
    image_generation_endpoint: str = Field(default="", repr=False)
    fulfillment_adapter_base_url: str = Field(default="", repr=False)
    payment_webhook_secret: str = Field(default="", repr=False)
    require_realname_for_api_keys: bool = False
    require_realname_for_client_runs: bool = False
    audit_include_email_hint: bool = False
    bcrypt_rounds: int = 12
    enforce_jti: bool = True

    default_admin_email: str = Field(default="admin@example.com", repr=False)
    default_admin_password: str = Field(default="change-me-now", repr=False)

    api_key_cache_ttl_seconds: int = 60
    default_request_timeout_seconds: float = 60.0
    default_billable_units: int = 1
    enable_background_workers: bool = True
    quota_reconcile_interval_seconds: int = 60


@lru_cache
def get_settings() -> Settings:
    return Settings()
