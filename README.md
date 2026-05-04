# OZON-PILOT

`#AttraX_Spring_Hackathon`

This repository is a public stripped recovery workspace for the OZON-PILOT source code.

## What is included

- `src/LitchiOzonRecovery`: OZON-PILOT main application source project
- `src/LitchiAutoUpdate`: OZON-PILOT updater source project
- `commerce-gateway`: SaaS/API gateway module for accounts, licensing, quotas, metering, admin UI, and business adapters
- `build.cmd`: local build and packaging script
- `docs/russian-image-adaptation-module.md`: optional `image2` module for Russian-market product photo adaptation and publishing
- `docs/ozon-fulfillment-label-module.md`: FBS order lookup and package-label download module

## What is intentionally omitted

- packaged runtime assets
- copied third-party binary dependencies
- local browser profiles and caches
- generated build output
- bundled database snapshots and environment-specific data
- production API keys, tokens, and private marketplace credentials

## Notes

- This is a source-first handoff intended for review, collaboration, and continued reconstruction.
- Some features that depend on omitted runtime assets will need local placeholders or restored dependencies before a full build can run.
- AI-assisted enrichment only runs when `DEEPSEEK_API_KEY` is provided through the local environment.
- The `image2` module only runs when `CODEXMANAGER_API_KEY` or `OPENAI_API_KEY` is configured. It publishes generated images through `OZON_IMAGE_UPLOAD_URL`.
- The SaaS gateway is a separate FastAPI service under `commerce-gateway`; configure it from `commerce-gateway/.env.example` and connect adapters through environment variables.
- To route `image2` uploads through SaaS metering, set `OZON_COMMERCE_GATEWAY_BASE_URL` and `OZON_COMMERCE_GATEWAY_API_KEY` in the desktop runtime environment.
- FBS package labels are downloaded into `ozon-labels/YYYYMMDD/` and can be metered through the SaaS fulfillment adapter route.

## Important boundary

This recovery workspace is for business continuity and source restoration.
It does not include bypassing or removing activation or license checks from the compiled binary.

## Build

Run:

```bat
build.cmd
```

The script uses the machine's built-in .NET Framework MSBuild and assembles a runnable output under `dist`.
