# OZON-PILOT Commerce Gateway

Independent commercialization shell for OZON-PILOT.

The gateway owns accounts, API keys, plans, subscriptions, quota, metering, payment
webhooks, audit logs, and admin APIs. Business code stays behind adapters. Legacy and
new project versions only need to implement the same HTTP adapter contract.

## V1 Scope

- Full commercial gateway for every exposed capability.
- Image generation endpoint reserved: `POST /v1/biz/images/generate`.
- Legacy image upload adapter wired through `POST /v1/biz/images/upload`.
  Set `IMAGE_ADAPTER_UPLOAD_PATH` to the path exposed by the legacy Nginx site. The
  existing port-80 setup uses `/ozon-upload`.
- OZON-PILOT `image2` can publish through this metered SaaS route by setting
  `OZON_COMMERCE_GATEWAY_BASE_URL` and `OZON_COMMERCE_GATEWAY_API_KEY` in the desktop
  runtime environment. If those are absent, the desktop keeps using the legacy image
  upload URL.
- Generic adapter proxy reserved: `POST /v1/biz/{capability}`.
- Desktop automation commercialization flow:
  - `POST /v1/client/activate`
  - `POST /v1/client/runs`
  - `POST /v1/client/runs/{job_id}/heartbeat`
  - `POST /v1/client/runs/{job_id}/reserve`
  - `POST /v1/client/runs/{job_id}/commit`
  - `POST /v1/client/runs/{job_id}/release`
  - `POST /v1/client/runs/{job_id}/complete`
- On this source branch, the WinForms desktop app only uses the gateway for the
  optional `image2` publishing path. Activation and run metering endpoints remain
  available for the next desktop wiring pass.

## Local Run

```bash
cd commerce-gateway
python -m venv .venv
. .venv/bin/activate
pip install -e .[dev]
cp .env.example .env
alembic upgrade head
python -m app.db.seed_cli
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

## Production Shape

```text
Caddy/Nginx -> FastAPI Gateway -> Postgres
                              -> Redis
                              -> Business adapters on localhost/internal network
```

Production reverse-proxy checklist:

- Configure Caddy/Nginx to overwrite `X-Real-IP` and `X-Forwarded-For` with the
  edge client IP. Do not pass through client-supplied proxy headers.
- Set `TRUST_PROXY_HEADERS=true` only after the proxy is confirmed to clean those
  headers. Otherwise the gateway intentionally uses `request.client.host`.
- Set `TRUSTED_PROXIES` to the CIDR ranges that may supply trusted proxy headers
  (for local Caddy: `127.0.0.1/32,::1/128`; for Docker, include the bridge CIDR).
- Keep direct public access to the FastAPI port closed; expose only the proxy.
- If the gateway is reachable directly from the public internet, do not enable
  `TRUST_PROXY_HEADERS`.
- When running uvicorn behind a proxy, also prefer `--proxy-headers` with a tight
  `--forwarded-allow-ips` value such as `127.0.0.1`.
- After deployment, call `GET /admin/_debug/ip` with an admin bearer token and
  confirm `resolved_client_ip` is the real external client IP.
- Use `/health` for application health checks because it reaches FastAPI. Use
  `/healthz` only for Caddy process liveness.

Recommended `TRUSTED_PROXIES` values:

| Deployment | Value |
| --- | --- |
| Same-host Caddy/Nginx | `127.0.0.1/32,::1/128` |
| Docker default bridge | `172.16.0.0/12,127.0.0.1/32,::1/128` |
| Docker custom network | Your compose network CIDR + loopback |
| Kubernetes | Pod CIDR or ingress controller CIDR + loopback |
| Direct public FastAPI port | Leave `TRUST_PROXY_HEADERS=false` |

## Background Workers

The FastAPI process starts lightweight workers by default:

- `api_key_revoked` Redis Pub/Sub subscriber clears cached revoked keys.
- quota reconciler clamps Redis hot quota to durable Postgres quota and expires stale reservations.

Set `ENABLE_BACKGROUND_WORKERS=false` only when running one-off management commands.

## Admin UI

Open `/admin/ui/login` and sign in with the seeded admin account. The minimal
server-rendered UI includes users, usage, subscriptions, and licenses.

## Real-name Verification

V1 keeps SMS and real-name verification optional to reduce early operating cost.
Defaults:

- `REQUIRE_REALNAME_FOR_API_KEYS=false`
- `REQUIRE_REALNAME_FOR_CLIENT_RUNS=false`

Turn these on only after a SMS/real-name provider is connected and the business
requires stricter compliance.

## Adapter Boundary

All business versions implement HTTP endpoints consumed by `IBusinessAdapter`.
The gateway remains unchanged when a new project version is connected.
