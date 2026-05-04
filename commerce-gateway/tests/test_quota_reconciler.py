from datetime import datetime, timedelta

import pytest

from app.db.models import ActionPermit, AutomationRun, Quota
from app.workers import quota_reconciler


class FakeScalarResult:
    def __init__(self, rows):
        self._rows = rows

    def scalars(self):
        return self

    def all(self):
        return self._rows

    def __iter__(self):
        return iter(self._rows)


class FakeDb:
    def __init__(self, *, requests=None, permits=None, quotas=None, sums=None):
        self.requests = requests or []
        self.permits = permits or []
        self.quotas = quotas or []
        self.sums = sums or {}
        self.updated = []
        self.commits = 0
        self.query_index = 0

    async def execute(self, statement):
        text = str(statement)
        if text.startswith("SELECT request_reservations"):
            return FakeScalarResult(self.requests)
        if text.startswith("SELECT action_permits"):
            return FakeScalarResult(self.permits)
        if text.startswith("SELECT quotas"):
            return FakeScalarResult(self.quotas)
        if "sum(request_reservations.units)" in text:
            return FakeScalarResult([self.sums.get("request", 0)])
        if "sum(action_permits.units)" in text:
            return FakeScalarResult([self.sums.get("permit", 0)])
        self.updated.append(text)
        return FakeScalarResult([])

    async def scalar(self, statement):
        text = str(statement)
        if "sum(request_reservations.units)" in text:
            return self.sums.get("request", 0)
        if "sum(action_permits.units)" in text:
            return self.sums.get("permit", 0)
        return None

    async def commit(self):
        self.commits += 1


class FakeRedis:
    def __init__(self):
        self.values = {}

    async def eval(self, script, numkeys, *args):
        quota_key = args[0]
        reservation_key = args[1]
        units = int(self.values.get(reservation_key, 0))
        if units <= 0:
            return [0, int(self.values.get(quota_key, 0))]
        self.values[quota_key] = int(self.values.get(quota_key, 0)) + units
        self.values.pop(reservation_key, None)
        return [1, self.values[quota_key]]

    async def delete(self, key):
        self.values.pop(key, None)

    async def set(self, key, value):
        self.values[key] = int(value)


@pytest.mark.asyncio
async def test_stale_creating_permit_is_expired_and_not_counted_in_clamp():
    now = datetime.utcnow()
    run = AutomationRun(
        id="run_1",
        user_id="user_1",
        license_id="lic_1",
        device_id="dev_1",
        workflow="test",
        heartbeat_at=now - timedelta(minutes=10),
    )
    permit = ActionPermit(
        id="permit_1",
        run_id=run.id,
        user_id="user_1",
        subscription_id="sub_1",
        period_key="period_1",
        action="image.generate",
        idempotency_key="k",
        units=3,
        status="creating",
        created_at=now - timedelta(minutes=2),
    )
    quota = Quota(
        id="quota_1",
        subscription_id="sub_1",
        period_key="period_1",
        quota_total=100,
        quota_used=10,
        quota_extra=0,
    )
    db = FakeDb(permits=[permit], quotas=[quota], sums={"request": 0, "permit": 0})
    redis = FakeRedis()
    redis.values["quota:sub_1:period_1"] = 90

    await quota_reconciler.reconcile_quotas_once(db, redis)

    assert redis.values["quota:sub_1:period_1"] == 90
    assert db.commits == 1
    assert any("UPDATE action_permits" in statement for statement in db.updated)


@pytest.mark.asyncio
async def test_clamp_subtracts_reserved_request_and_permit_only():
    quota = Quota(
        id="quota_1",
        subscription_id="sub_1",
        period_key="period_1",
        quota_total=100,
        quota_used=10,
        quota_extra=5,
    )
    db = FakeDb(quotas=[quota], sums={"request": 7, "permit": 3})
    redis = FakeRedis()

    await quota_reconciler.reconcile_quotas_once(db, redis)

    assert redis.values["quota:sub_1:period_1"] == 85
