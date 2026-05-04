import asyncio

from app.db.seed import seed_defaults
from app.db.session import AsyncSessionLocal


async def main():
    async with AsyncSessionLocal() as db:
        await seed_defaults(db)


if __name__ == "__main__":
    asyncio.run(main())
