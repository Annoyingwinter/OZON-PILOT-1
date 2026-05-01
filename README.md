# OZON-PILOT

`#AttraX_Spring_Hackathon`

This repository is a public stripped recovery workspace for the OZON-PILOT source code.

## What is included

- `src/LitchiOzonRecovery`: OZON-PILOT main application source project
- `src/LitchiAutoUpdate`: OZON-PILOT updater source project
- `build.cmd`: local build and packaging script
- `docs/russian-image-adaptation-module.md`: optional module for generating Russian-market product photos through local CodexManager/Image API

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

## Important boundary

This recovery workspace is for business continuity and source restoration.
It does not include bypassing or removing activation or license checks from the compiled binary.

## Build

Run:

```bat
build.cmd
```

The script uses the machine's built-in .NET Framework MSBuild and assembles a runnable output under `dist`.
