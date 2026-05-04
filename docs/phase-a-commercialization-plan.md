# Phase A Commercialization Plan

## Decision

Use one 4 vCPU / 8 GB RAM Linux server for Phase A staging and early production.

This size is enough for:

- `commerce-gateway` FastAPI service
- PostgreSQL
- Redis
- Caddy or Nginx HTTPS reverse proxy
- image upload adapter
- admin UI and low-volume desktop clients

Do not split into multiple servers yet. Split later when traffic or customer data risk requires it.

## Phase A Goal

Make the chain commercially runnable:

1. Desktop client activates license.
2. Desktop starts an automation run.
3. SaaS reserves quota for actions.
4. Desktop collects 1688 products.
5. Desktop calculates price using the strict margin formula.
6. `image2` generates and publishes localized product images.
7. Desktop uploads products to Ozon.
8. Desktop checks Ozon import/SKU/stock result.
9. Desktop downloads FBS package labels after orders arrive.
10. SaaS records usage, errors, quota, and audit logs.

## Current State

Done:

- Source baseline is `codex/source-snapshot-1.1.2-20260503` at `04e59a7`.
- `image2` module is wired into Ozon upload flow.
- `commerce-gateway` SaaS module is present and tested.
- `image2` can publish through SaaS with:
  - `OZON_COMMERCE_GATEWAY_BASE_URL`
  - `OZON_COMMERCE_GATEWAY_API_KEY`
- Pricing now follows the strict formula:
  - selling price = `(source cost + domestic logistics + international logistics) / (1 - commission - ads - target profit)`
- Missing matched logistics rule now fails closed instead of pricing with zero logistics.
- Commission + ads + target profit must be below 100%.
- FBS package label download exists in the desktop client and has a metered SaaS proxy route.

Not done yet:

- Desktop license activation and run quota calls are not wired to `/v1/client/*`.
- Order sync is not implemented.
- FBS order lifecycle automation still needs confirm/ship/status reconciliation.
- Server deployment files are not final.
- SaaS admin UI is basic and needs operations polish.

## Pricing Rule

Formula:

```text
selling_price = (source_cost + domestic_logistics + international_logistics)
              / (1 - platform_commission - promotion_rate - target_profit_rate)
```

Example:

```text
(35 + 0 + 20) / (1 - 0.10 - 0.30 - 0.30) = 183.33
```

Rules:

- Do not apply legacy multiplier floors over formula output.
- Do not silently use zero logistics when fee rule is missing.
- Keep two decimal places.
- Reject invalid percentage totals at or above 100%.

## Label Download Work

Current desktop and SaaS capability for Ozon FBS labels:

1. List FBS postings awaiting packaging/delivery.
2. Download package label PDF from Ozon Seller API.
3. Save labels by date and posting number.
4. Expose SaaS metered endpoint for label download.
5. Validate `posting_number` input before proxying.
6. Release quota when the fulfillment adapter is missing or returns a server error.
7. Generate a per-run summary file and daily CSV index for downloaded label PDFs.

Current local paths:

- `src/LitchiOzonRecovery/OzonFulfillmentLabelService.cs`
- `commerce-gateway/app/biz/router.py`
- `commerce-gateway/tests/test_fulfillment_labels.py`

Next fulfillment work:

- Confirm or ship posting when required by Ozon flow.
- Bind label requests to a higher-level action permit ledger.
- Add shop/account partitioning above the current date/posting-number index.
- Add admin audit rows that show label request counts and upstream status.

## Differentiated Technical Architecture

To avoid being just a modified listing tool, Phase A should pivot to a decision-and-control architecture:

```text
Signal Layer
  1688 product capture
  Ozon category/attribute metadata
  fee/logistics rules
  order and label status

Decision Layer
  margin formula gate
  logistics rule gate
  marketplace compliance gate
  image localization gate
  quota/license gate

Execution Layer
  Ozon product upload
  image generation and upload
  stock update
  order/label download

Evidence Layer
  run ledger
  screenshots/logs
  pricing proof
  generated image proof
  API request audit
```

Patent-oriented invention theme:

> A cross-border marketplace automation method that combines rule-bound cost pricing, localized product media generation, marketplace compliance validation, and metered execution permits into a traceable run ledger before publishing and fulfillment actions.

Possible patent claim anchors:

- Formula-gated pricing with missing-cost fail-closed behavior.
- Cultural image adaptation tied to product facts and marketplace safety constraints.
- SaaS-issued action permits before durable marketplace writes.
- Evidence ledger that binds product source, pricing inputs, generated media, API upload, stock update, and label download.
- Multi-account execution with quota, license, and audit controls.

Patent caution:

- File before broad public disclosure.
- Use a patent agent/lawyer for claims.
- Do not claim copied UI or generic marketplace upload.
- Focus claims on the combined control method and evidence-backed execution chain.
