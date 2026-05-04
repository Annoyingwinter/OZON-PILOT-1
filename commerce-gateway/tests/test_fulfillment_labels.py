import pytest
from fastapi import HTTPException
from starlette.requests import Request

from app.biz import router as biz_router
from app.core.config import get_settings
from app.core.request_context import ApiClientContext


class FakeDb:
    def __init__(self):
        self.executed = []
        self.added = []
        self.commits = 0

    async def execute(self, statement):
        self.executed.append(str(statement))

    def add(self, row):
        self.added.append(row)

    async def commit(self):
        self.commits += 1


class FakeRedis:
    def __init__(self):
        self.eval_calls = []

    async def eval(self, script, numkeys, *args):
        self.eval_calls.append((script, numkeys, args))
        if numkeys == 2:
            return [1, 100]
        return 1


class FakeUpstreamResponse:
    status_code = 200
    content = b"%PDF-1.4"
    headers = {
        "content-type": "application/pdf",
        "set-cookie": "session=leak",
        "server": "upstream",
        "x-powered-by": "test",
        "x-label-id": "safe",
    }


class FakeAsyncClient:
    def __init__(self, timeout):
        self.timeout = timeout

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        return False

    async def post(self, upstream, headers, json):
        self.upstream = upstream
        self.headers = headers
        self.json = json
        return FakeUpstreamResponse()


def make_context():
    return ApiClientContext(
        user_id="user_1",
        api_key_id="key_1",
        subscription_id="sub_1",
        plan_code="starter",
        quota_period_key="202605",
        rate_limit_per_minute=60,
    )


def make_request():
    request = Request({"type": "http", "method": "POST", "path": "/v1/biz/fulfillment/labels/download", "headers": []})
    request.state.request_id = "req_1"
    request.state.billable_units = 1
    request.state.quota_key = "quota:sub_1:202605"
    request.state.reservation_key = "reservation:req_1"
    return request


@pytest.mark.asyncio
async def test_fulfillment_labels_invalid_payload_releases_quota():
    db = FakeDb()
    redis = FakeRedis()

    with pytest.raises(HTTPException) as exc:
        await biz_router.download_fulfillment_labels(
            {"posting_number": []},
            make_request(),
            make_context(),
            db,
            redis,
        )

    assert exc.value.status_code == 400
    assert len(redis.eval_calls) == 1
    assert db.added[-1].billable_units == 0


@pytest.mark.asyncio
async def test_fulfillment_labels_missing_adapter_releases_quota():
    settings = get_settings()
    previous = settings.fulfillment_adapter_base_url
    settings.fulfillment_adapter_base_url = ""
    db = FakeDb()
    redis = FakeRedis()

    try:
        with pytest.raises(HTTPException) as exc:
            await biz_router.download_fulfillment_labels(
                {"posting_number": ["123"]},
                make_request(),
                make_context(),
                db,
                redis,
            )
    finally:
        settings.fulfillment_adapter_base_url = previous

    assert exc.value.status_code == 501
    assert len(redis.eval_calls) == 1
    assert redis.eval_calls[0][1] == 2
    assert db.commits == 1
    assert db.added[-1].billable_units == 0


@pytest.mark.asyncio
async def test_fulfillment_labels_proxy_scrubs_upstream_headers(monkeypatch):
    settings = get_settings()
    previous = settings.fulfillment_adapter_base_url
    settings.fulfillment_adapter_base_url = "http://fulfillment-adapter.local"
    db = FakeDb()
    redis = FakeRedis()
    monkeypatch.setattr(biz_router.httpx, "AsyncClient", FakeAsyncClient)

    try:
        response = await biz_router.download_fulfillment_labels(
            {"posting_number": ["123"]},
            make_request(),
            make_context(),
            db,
            redis,
        )
    finally:
        settings.fulfillment_adapter_base_url = previous

    assert response.status_code == 200
    assert response.body == b"%PDF-1.4"
    assert response.headers["content-type"] == "application/pdf"
    assert response.headers["x-label-id"] == "safe"
    assert "set-cookie" not in response.headers
    assert "server" not in response.headers
    assert "x-powered-by" not in response.headers
    assert db.added[-1].billable_units == 1
