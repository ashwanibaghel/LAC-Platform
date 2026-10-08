# Dak register and LAN HTTP intake fix

Branch: `fix/dak-register-directory-20261008`

Exact parent/base: `20323e0d7e9387edab4d64c2a4a06731ade53c17`.

## Behavior

`DakRegistration` called `crypto.randomUUID()` during its initial render and after Register & Next. That API is unavailable on an ordinary, non-loopback HTTP origin, so initial rendering threw before the form appeared. `createDakRequestId` uses native randomUUID when available, otherwise generates an RFC4122 UUID v4 with `getRandomValues`, setting the version and variant bits. No dependency or Math.random fallback was added. Failed registration keeps the same idempotency key; successful Register & Next obtains a fresh key. An immediate ref guard prevents duplicate concurrent submit handlers.

The existing `/dak` directory remains the single central Inward Dak Register. Its permitted action is labelled “+ New Dak Entry” and goes to `/dak/register`. Intake keeps Register & Next and Register & Open, and adds Back to Dak Register. Clicking a register row opens `/dak/{id}`; the diary link remains keyboard accessible. All five operational delivery buckets are retained separately from All Register and My Desk.

The table displays diary number, received date, sender/department, subject, inward mode, priority, current desk, current handler, workstream, status and attachment indicator. Main filters remain visible; less-used filters expand under More filters. Changing any filter resets pagination, and superseded fetches are aborted. Directory styles are scoped; the search row remains compact at 1366x768.

## API contract

`GET /api/dak` retains existing `q`, `status`, `priority`, `deskId`, `page`, `pageSize` and `includeArchived`. Existing search matches diary number, subject, sender and sender reference.

New optional parameters:

| Parameter | Meaning |
|---|---|
| `receivedFrom`, `receivedTo` | Inclusive received-date bounds in YYYY-MM-DD format |
| `inwardMode` | Case-insensitive exact mode |
| `categoryId`, `workstreamId` | Canonical UUID classification filters |
| `handlerId` | Officer on the active current assignment |
| `sender` | Case-insensitive sender name or department substring |
| `hasDocument` | `true` for primary scan or an active attachment, `false` for neither; omit for all |

The existing union-of-scopes authorization executes first. Every filter narrows that authorized query before count and pagination. A reversed date range returns 400. Invalid typed dates, UUIDs or booleans return 400 through existing minimal API binding. Ordering adds the record GUID as a stable tie-breaker after received date and creation time. The attachment indicator uses the same predicate as the document filter.

`GET /api/dak/lookups/directory` preserves `desks` and adds `handlers`, `categories` and `workstreams`. All use active canonical database records. Handlers require active desk membership and an active role granting Dak.Receive with an operational scope. Removed memberships, inactive officers/desks/roles and inactive classifications are excluded. No users, desks, designations, categories or workstreams are hard-coded.

No permissions, schema, migration, diary identity rule or custody workflow were changed. Court, Compensation Calculator, Land, Award, Matter and ONLYOFFICE implementation files are untouched.

## Exact changed files

1. `src/LAC.Api/DakEndpoints.cs`
2. `src/LAC.Api/DakDirectoryFilters.cs`
3. `src/LAC.Web/src/dak/DakDirectory.tsx`
4. `src/LAC.Web/src/dak/DakRegistration.tsx`
5. `src/LAC.Web/src/dak/dak.css`
6. `src/LAC.Web/src/dak/requestId.js`
7. `src/LAC.Web/src/dak/requestId.d.ts`
8. `src/LAC.Web/tests/dak-request-id.test.mjs`
9. `src/LAC.Web/tests/dak-directory-browser.test.mjs`
10. `src/LAC.Web/tests/dak-directory-lan.acceptance.mjs`
11. `tests/LAC.Tests/DakDirectoryTests.cs`
12. `docs/dak-register-directory-20261008.md`

## Permission verification

Tests exercise real authenticated API requests against both EF InMemory and disposable PostgreSQL, including combined filters that target another desk.

| Principal | Directory and lookups | Verified visibility |
|---|---|---|
| Dak.Register only | 403 | Registration permission does not grant register read access |
| Dak.View, All | 200 | Both test records; filters narrow results |
| Dak.Move only | 403 | Movement permission does not grant directory read access |
| Dak.Receive only | 403 | Receipt permission does not grant directory read access |
| Dak.View, Assigned, one desk | 200 | Own desk only; another desk filter returns zero |
| Dak.View, Assigned, two desks | 200 | Union of both desks, still narrowed by filters |
| System administrator | 200 | Global register, all filters/counts/pagination and lookups |

Existing Dak scope/custody tests also run unchanged, including workstream scope, dispatch participants, terminal mutation restrictions, receive/pull-back races and immutable audit evidence.

## Validation

Test evidence is outside the repository at `C:/Users/ashwa/AppData/Local/Temp/lac-dak-directory-results`. Commit and remote HEAD are reported with delivery.

| Check | Final result |
|---|---|
| Dak backend selection, excluding PostgreSQL | 101 passed, 0 failed, 0 skipped, verified in final full-run TRX |
| Dak PostgreSQL | 20 passed, 0 failed, 0 skipped, including receipt/pull-back races and migration/preflight tests |
| New directory tests | 26 InMemory API cases + 1 real PostgreSQL case passed |
| Complete backend primary run | 1,258 passed, 0 failed, 3 skipped; 1,261 total |
| Supplementary RBAC PostgreSQL run | 2 passed, 1 inherited failure, 0 skipped |
| Complete backend unique coverage across both runs | 1,260 passed, 1 inherited failure; all 1,261 tests executed |
| Dak frontend | 36 passed, 0 failed, 0 skipped |
| Complete frontend | 231 passed, 0 failed, 0 skipped |
| Additional final UUID/browser checks | 5 passed, including compact search-row assertion |
| npm build | Passed (existing large-bundle warning) |
| git diff --check | Passed |
| Real LAN HTTP browser | Passed at 1366x768, zero page exceptions |

The inherited failure is `RbacPostgreSqlTests.Upgrade_redacts_historical_credentials_and_schema_downgrade_reupgrade_succeeds`, at its non-empty SessionVersion assertion (line 149). It failed identically in a clean, separate checkout of the exact base SHA, confirmed by `rbac-baseline.trx`. The existing test uses the migration immediately before the latest as its upgrade starting point. The frozen release now ends with `AddCourtStructuredLacIntelligence`, so the test inserts its historical user after the session-security migration has already executed and then upgrades only Court. No RBAC, Court, model or migration change is included in this focused fix. This inherited test failure remains unresolved.

Commands:

```text
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter FullyQualifiedName~Dak
dotnet test tests/LAC.Tests/LAC.Tests.csproj
node --test tests/dak*.test.mjs
node --test --test-concurrency=2 tests/*.test.mjs
npm run build
git diff --check
```

Both backend runs use `LAC_TEST_POSTGRES` pointing at a dedicated cluster on `127.0.0.1:55448`. PostgreSQL tests create and destroy their own isolated databases; the additional directory test verifies all 19 filter scenarios, pre-pagination counts, page boundaries, the permission matrix and live active lookups through real HTTP endpoints. Test fixtures establish confirmed holders through Mark -> Receive to satisfy existing immutable receipt constraints.

The three RBAC PostgreSQL cases additionally ran on their required separate loopback cluster at port 55438. The baseline comparison used that cluster with another independently disposable test database. All temporary test databases, the browser API, both test clusters and the baseline checkout were cleaned up after validation; test logs, TRX evidence and screenshots were retained outside the repository.

The self-contained browser regression uses `http://10.0.0.99:<port>` mapped to its test server with Chrome host resolution. It asserts `isSecureContext === false`, randomUUID is undefined and getRandomValues exists. It verifies initial rendering, valid UUIDs, double-click prevention, same-key failure retry, fresh-key next registration, directory query parameters, combined filters, all queue tabs and row/open navigation.

Real browser acceptance passed on `http://192.168.1.20:65311`, backed by a disposable local PostgreSQL database and API. It exercised both registration actions, reset, created records appearing in the register, individual and combined filters, 31-record pagination (25 + 6), row detail navigation, all five delivery tabs and actual Mark -> InTransit -> Receive. There were zero page exceptions. The test API and database are local disposable fixtures; no office database was used.

Screenshots were captured and visually inspected at 1366x768 outside the repository:

- `C:/Users/ashwa/.codex/visualizations/2026/10/08/dak-register-directory-20261008/01-new-dak-entry-http-1366x768.png`
- `C:/Users/ashwa/.codex/visualizations/2026/10/08/dak-register-directory-20261008/02-created-workspace-http-1366x768.png`
- `C:/Users/ashwa/.codex/visualizations/2026/10/08/dak-register-directory-20261008/03-inward-register-http-1366x768.png`
- `C:/Users/ashwa/.codex/visualizations/2026/10/08/dak-register-directory-20261008/04-combined-filters-http-1366x768.png`

No merge or deployment is part of this change.
