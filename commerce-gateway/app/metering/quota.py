from redis.asyncio import Redis

RESERVE_QUOTA_LUA = """
local quota_key = KEYS[1]
local reservation_key = KEYS[2]
local units = tonumber(ARGV[1])
local ttl_seconds = tonumber(ARGV[2])

local remaining = tonumber(redis.call("GET", quota_key) or "-1")
if remaining < units then
  return {-1, remaining}
end

remaining = redis.call("DECRBY", quota_key, units)
if remaining < 0 then
  redis.call("INCRBY", quota_key, units)
  return {-1, remaining + units}
end

redis.call("SET", reservation_key, units, "EX", ttl_seconds)
return {1, remaining}
"""

RELEASE_QUOTA_LUA = """
local quota_key = KEYS[1]
local reservation_key = KEYS[2]

local units = tonumber(redis.call("GET", reservation_key) or "0")
if units <= 0 then
  return {0, tonumber(redis.call("GET", quota_key) or "0")}
end

redis.call("INCRBY", quota_key, units)
redis.call("DEL", reservation_key)
return {1, tonumber(redis.call("GET", quota_key) or "0")}
"""

COMMIT_QUOTA_LUA = """
local reservation_key = KEYS[1]
local existed = redis.call("DEL", reservation_key)
return existed
"""


class QuotaMeter:
    def __init__(self, redis: Redis):
        self.redis = redis

    async def reserve(self, quota_key: str, reservation_key: str, units: int, ttl_seconds: int = 3600):
        result = await self.redis.eval(RESERVE_QUOTA_LUA, 2, quota_key, reservation_key, units, ttl_seconds)
        allowed, remaining = int(result[0]), int(result[1])
        return allowed == 1, remaining

    async def release(self, quota_key: str, reservation_key: str):
        result = await self.redis.eval(RELEASE_QUOTA_LUA, 2, quota_key, reservation_key)
        return int(result[0]) == 1, int(result[1])

    async def commit(self, reservation_key: str):
        return int(await self.redis.eval(COMMIT_QUOTA_LUA, 1, reservation_key)) == 1
