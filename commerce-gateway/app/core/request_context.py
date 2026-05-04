from dataclasses import dataclass


@dataclass(frozen=True)
class ApiClientContext:
    user_id: str
    api_key_id: str
    subscription_id: str
    plan_code: str
    quota_period_key: str
    rate_limit_per_minute: int
