import os

os.environ.setdefault(
    "DATABASE_URL", "postgresql+asyncpg://test:test@127.0.0.1:5432/test"
)
os.environ.setdefault("REDIS_URL", "redis://127.0.0.1:6379/15")
os.environ.setdefault("JWT_SECRET", "test-jwt-secret-with-enough-length")
os.environ.setdefault("API_KEY_HASH_SECRET", "test-api-key-secret-with-enough-length")
os.environ.setdefault("INTERNAL_ADAPTER_TOKEN", "test-internal-token")
