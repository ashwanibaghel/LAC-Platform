# Compensation history and revenue shorthand

Base: `c728959bea1d35700901f34ab51dc60817cf131c`. Candidate branch: `codex/compensation-history-revenue-v2`.

The existing `POST /api/calculators/compensation/compute` remains authenticated and stateless, with its original bounded scalar decimal contract. Calculator rules and the `lac-delhi-v1` conversion constants are unchanged.

## History API

All routes require the normal application cookie. The owner comes from the authenticated AppUser identity, never from the body. Foreign or nonexistent IDs return 404. Successful responses carry `Cache-Control: no-store`.

| Method/path | Request | Response |
| --- | --- | --- |
| POST `/api/calculators/compensation/history` | `{ "idempotencyKey": "client UUID", "inputs": { /* CompensationFormState */ } }` | `{ id, title, createdAt, originalAreaNotation, calculatorVersion, conversionVersion, inputs, request, response, saved: true }` after commit |
| GET `/api/calculators/compensation/history?page=1&pageSize=20&search=title` | Page 1–100000; size 1–100; title substring at most 200 characters | `{ items, total, page, pageSize }`, newest first with ID tie-breaker; summary, date, original area, rate and final amount |
| GET `/api/calculators/compensation/history/{id}` | Owner-scoped ID | Full stored snapshot; no recomputation |
| PATCH `/api/calculators/compensation/history/{id}` | `{ "title": "up to 200 characters" }`; null/blank clears title | `{ id, title, saved: true }` after metadata commit |

There is no delete/archive endpoint. Original mathematical evidence is never overwritten. Use as New restores the original form inputs and creates a fresh entry on the next successful calculation.

`inputs` contains the existing form fields: `landArea`, `landAreaUnit`, `marketRate`, `marketRateUnit`, `useOfficialEquivalent`, `officialEquivalentArea`, `multiplicationFactor`, `treesAndStructures`, `solatiumPercentage`, `additionalAmountType`, `annualRate`, `durationType`, `durationValue`, `startDate`, `endDate`, `calculatedOn`, `formulaReadable`, `otherDurationMode`, `otherDurationValue`, `otherStartDate`, `otherEndDate`. Numeric form inputs remain strings; booleans remain booleans. Additional type is `interest`/`other`; interest duration is `days`/`months`/`date_range`; Other duration is `None`/`Days`/`Months`/`DateRange`. Dates are ISO dates. A complete original form is retained even for currently inactive fields.

The server normalizes those inputs and calls the unchanged compensation engine. Client result amounts, owners, timestamps and unknown fields are rejected. Bodies are bounded to 16 KiB; strict decimal limits are retained. Invalid computation returns validation errors and creates no row. Persistence failure returns 503 with `saved: false`; authentication, media and oversized-body errors retain their appropriate status codes. The UI never labels a response Saved before the committed server response.

The unique `(OwnerUserId, IdempotencyKey)` index resolves concurrent retries. A repeated identical submission returns its existing snapshot; the same key with different original inputs returns 409. The browser retains its key after an uncertain failure and reuses it for the same inputs; successful or changed submissions use a new key. UUIDs use `getRandomValues` so the office HTTP LAN origin is supported. Refresh retains committed records in PostgreSQL; unsent in-memory form drafts are not durable history.

## Storage and immutability

Migration `20261009092053_AddCompensationHistory` adds only `CompensationHistory`, its owner foreign key (restrict delete), owner/date/ID descending index and owner/idempotency unique index. JSONB stores original form inputs, normalized canonical request and complete precise response/trace. Original notation, final amount, UTC creation time, submission hash and calculator/conversion versions are retained. The unchanged mathematical engine is recorded as `compensation-v1`; conversion is `lac-delhi-v1`.

EF guards and a PostgreSQL UPDATE/DELETE trigger allow only Title changes. The trigger compares all other columns, so a direct SQL update cannot alter evidence. No cloud, external API or authoritative browser localStorage is involved.

## Shorthand

The shared browser parser serves Compensation, Area Converter, Revenue Arithmetic and bulk shorthand. `4-16` = 4.8 Bigha; `2-9-1` = 2.4525; `0-18` = 0.9. Plain `5` and `4.8` remain valid. Interpretation is independent of rate units. Components are integers; Biswa/Biswansi are each 0–19; negative/malformed input and excess precision/magnitude are rejected. BigInt normalization does not round the notation. Revenue arithmetic additionally requires an exact whole Biswansi total. Compensation retains the raw notation and submits a canonical scalar. Non-Bigha compensation units reject shorthand. The existing official equivalent override remains distinct from automatic conversion.

## Verification

Run `npm run build`, `npm run test:calculator` and `node --test tests/*.test.mjs` in `src/LAC.Web`. Run the full .NET suite with `dotnet test tests/LAC.Tests`. The history integration tests require a dedicated disposable loopback PostgreSQL server on **55440**; they create/drop only `lac_comp_history_test_<random>` databases. They never use the application's connection variable or `lac_platform`. Other pre-existing opt-in PostgreSQL suites retain their own prerequisites.

On Windows with PostgreSQL tools available, provision and run the history tests using `tests/scripts/test-compensation-history-postgres.ps1 -DataDirectory <new disposable directory outside the repository>`. The helper refuses an occupied port/existing directory and stops only the cluster it starts. Run full regression with that cluster available when including history integration tests.

The accepted reference stays ₹7,95,68,513.75 for 18 Bigha, official 3.744 Acre, ₹53,00,000/Acre, factor 2, assets 0, solatium 100%, interest 12% for 30 days on Market Value. Real PostgreSQL tests also compare shorthand and decimal response snapshots across Acre, Bigha and square-metre rates; browser/request tests cover all nine rate units and 81 conversion pairs.
