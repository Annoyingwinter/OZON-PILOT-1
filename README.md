# OZON-PILOT

`#AttraX_Spring_Hackathon`

This folder contains the reconstructed, maintainable source workspace for the published package.

## What is included

- `src/LitchiOzonRecovery`: OZON-PILOT main application source project
- `src/LitchiAutoUpdate`: OZON-PILOT updater source project
- `baseline`: copied runtime assets from the published package
- `lib`: copied managed dependencies from the published package
- `build.cmd`: local build and packaging script

## Recovery scope

This workspace restores:

- configuration loading and saving
- local SQLite database access
- category and fee asset loading
- 1688 browser plugin assets
- updater project source and launch flow
- a new WinForms recovery shell that reconnects these pieces

## Current recovery features

- Overview panel for the recovered asset inventory
- Config editor backed by `SettingConfig.txt`
- Database preview for `SkuTable`, `ShopTable`, and `tb_catch_shop`
- Database import and export for `.txt`, `.csv`, `.tsv`, and `.xlsx`
- Category tree and fee rule search
- Fee rule export to Excel
- Product workbench for fee lookup and profit estimation
- WebView2 shell that loads the recovered `1688` browser plugin

## Important boundary

This recovery workspace is for business continuity and source restoration.
It does not include bypassing or removing activation or license checks from the compiled binary.

## Build

Run:

```bat
build.cmd
```

The script uses the machine's built-in .NET Framework MSBuild and assembles a runnable output under `dist`.
