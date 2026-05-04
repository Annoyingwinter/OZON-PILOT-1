from redis.asyncio import Redis

RATE_LIMIT_LUA = """
local key = KEYS[1]
local limit = tonumber(ARGV[1])
local window_seconds = tonumber(ARGV[2])

local current = redis.call("INCR", key)
if current == 1 then
  redis.call("EXPIRE", key, window_seconds)
end

if current > limit then
  return {0, current}
end

return {1, current}
"""


class RateLimiter:
    def __init__(self, redis: Redis):
        self.redis = redis

    async def allow(self, key: str, limit: int, window_seconds: int = 60) -> tuple[bool, int]:
        result = await self.redis.eval(RATE_LIMIT_LUA, 1, key, limit, window_seconds)
        return int(result[0]) == 1, int(result[1])
