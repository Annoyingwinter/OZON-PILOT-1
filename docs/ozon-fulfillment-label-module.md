# Ozon Fulfillment Label Module

This module adds the first fulfillment-side control surface to OZON-PILOT.

## Desktop Capabilities

- `FBS Orders`: lists recent Ozon FBS postings with status `awaiting_deliver`.
- `Labels`: downloads PDF package labels for pasted `posting_number` values.
- Output path: `ozon-labels/YYYYMMDD/`.
- One package-label request is chunked to at most 20 posting numbers.
- Each download creates `label-batch-YYYYMMDD-HHMMSS.txt` as the run summary.
- Each day keeps `label-downloads.csv` as the searchable daily label index.
- Ozon requests use TLS 1.2 and do not write API keys to logs.

## Ozon API Calls

- `POST /v3/posting/fbs/list`
- `POST /v2/posting/fbs/package-label`

The package-label call expects Ozon Seller `Client-Id` and `Api-Key`.

## SaaS Metering Route

`commerce-gateway` exposes:

```text
POST /v1/biz/fulfillment/labels/download
```

This route enforces API-key metering, quota reservation, usage logging, and then proxies to an internal fulfillment adapter:

```text
FULFILLMENT_ADAPTER_BASE_URL + /ozon/fbs/package-label
```

The route intentionally does not persist Ozon API keys. It forwards the request to a controlled internal adapter and records only request metadata through the existing usage log.

Safety rules:

- `posting_number` must be 1 to 100 non-empty strings.
- Invalid payloads release reserved quota and return `INVALID_INPUT`.
- Missing fulfillment adapter releases reserved quota and returns `NOT_IMPLEMENTED`.
- Hop-by-hop and leak-prone upstream headers are scrubbed before returning the PDF.

## Differentiation Value

The module expands the product from listing automation into a fulfillment-control workflow:

```text
source product -> pricing proof -> Ozon upload -> stock -> FBS order -> package label -> audit ledger
```

This is useful for the patent-oriented architecture because label download creates a durable fulfillment action that can be controlled by SaaS permits and tied back to the listing evidence.
