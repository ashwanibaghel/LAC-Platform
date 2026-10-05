# Dak V2 Phase A — safe intake implementation

Branch: `codex/dak-phase-a`. Base: `37db57e5fa40cd353e70c3161abe62e23eba4cc5`.

Stamped diary numbers remain manually transcribed and mandatory. Registration does not generate numbers, infer years, create Matter records or require land/court context. Existing DakAssignment, DakMovement and WorkItem responsibility models are preserved. No NT-only marking authority or multi-recipient workflow was introduced.

## Schema and migration

Additive migration: **`20261005194104_HardenDakIntakePhaseA`**. Historical deployed migrations were not edited.

- `DiaryNumber` remains the required display/business value; `Dak.Id` remains the GUID primary key.
- PostgreSQL stores a generated `DiaryNumberKey`: trim surrounding ASCII space/tab/CR/LF and uppercase ASCII letters for comparison. Punctuation, internal whitespace, digits and other Unicode characters are preserved. Existing displayed diary text is not rewritten.
- Unique `IX_Daks_DiaryNumberKey` applies to `RecordStatus = Active`, including active Disposed/Cancelled records. Cancelled numbers cannot be reused merely by changing lifecycle status. Archived legacy records remain preserved and outside the active key reservation; reactivation into a duplicate fails at the database constraint.
- `CK_Daks_ActiveDiaryNumber` rejects blank active keys, including writes bypassing the API. No register/year/counter fields are added.
- Optional `RegistrationRequestId`, `RegistrationRequestHash`, `RegisteredByUserId` persist request identity. Actor/request uniqueness also applies to archived receipts, preserving replay identity after archival.
- Nullable physical-original existence, Desk/User references, legacy location/provenance notes and updated-at/by references are added. No migration backfill guesses physical existence or custody from scan/routing state.

Migration obtains a table write lock and checks active blanks/duplicate comparison keys **before** installing any constraints. It raises an actionable error pointing to the read-only preflight script. It does not renumber, merge, archive or delete receipts. PostgreSQL migration tests verify rollback leaves historical text and schema intact when preflight fails.

## Historical-data preflight and remediation

Executed `scripts/audit-dak-diary.sql` against the configured loopback local runtime PostgreSQL database in a read-only transaction, with default read-only enforcement. At verification time it contained **0 Dak records, 0 active blank diary numbers, 0 duplicate active-key groups**. No application migration or operational data mutation was performed against that database.

This does not establish the contents of another deployed office database. Run the same read-only script there before deployment:

```powershell
psql -X --no-password -v ON_ERROR_STOP=1 -f scripts/audit-dak-diary.sql
```

Use the office's existing secure local connection environment/credential mechanism; do not place passwords in command text. The script reports active collisions/blanks with receipt IDs and original diary text, and historical duplicate groups separately. Its transaction always ends with ROLLBACK.

If conflicts exist, compare each entry to the physical stamp/source register. Obtain an explicit, audited office decision for correction or historical handling. Do not infer years, silently renumber or auto-merge. Retry the migration only after review resolves active conflicts. Archived duplicates can remain as historical evidence. Disposable tests intentionally include duplicates/blanks to demonstrate the failure path; those are synthetic fixtures, not discovered office errors.

## API contract changes

| Surface | Contract |
|---|---|
| `POST /api/dak` | Existing multipart fields retained. Optional `Idempotency-Key` header is a non-empty UUID. Same actor/key and identical normalized intake payload/document bytes return the same receipt ID (`201`, existing response shape). Reusing the key for changed payload returns `409`. A duplicate active canonical diary without a matching replay returns a clear `409`. Invalid keys/required fields return `400`. |
| Request retry | The registration form supplies one stable key per mounted intake form, reused after a lost response. Without a key, existing clients remain compatible and diary uniqueness still prevents duplicate creation, but they receive conflict rather than transparent replay. A key is durable even after receipt archival. No separate idempotency framework was introduced. |
| `GET /api/dak` | Default list is active records only. Explicit `includeArchived=true` includes authorized historical records. List entries expose `recordStatus`. Existing scope filtering remains in effect. |
| `GET /api/dak/{id}` | Historical detail remains readable under existing Dak.View authorization and exposes `recordStatus`. Every context link exposes `canOpen`; restricted links return `entityId: null`, `displayName: "Restricted record"`, `canOpen: false`. Link-row ID is retained for source-side unlinking; target identity/title is redacted. |
| Target permissions | Matter is checked per target with MatterAuthorizationService. Village/Khasra and Award use the permissions/workstream semantics of their canonical read endpoints. Dak access does not grant target access. Context creation also requires the appropriate target read permission. |
| Attachments/context | All existing URL aliases remain. Mutations run under the same locked Dak row as routing/closure, reject archived/Disposed/Cancelled records, increment Dak revision and use existing audit infrastructure. Optional `If-Match: "<revision>"` rejects stale edits (`409`); omitted headers preserve compatibility, with serialized writes and server lifecycle checks. The current UI sends quoted revision headers. |
| Metadata/routing | Existing expected-revision bodies remain. Metadata now shares the intake transaction/lifecycle guard. Archived records fail normal mutation authorization. Routing/disposal/cancellation also check active record status inside their transaction. Existing marking transitions remain unchanged. |
| `GET /api/dak/{id}/physical-original` | Returns nullable `hasPhysicalOriginal`, canonical `deskId`/`userId`, `locationNote`, `provenanceNote`, `updatedAt`, `updatedByUserId`, and revision under Dak.View. |
| `PUT /api/dak/{id}/physical-original` | Body: `hasPhysicalOriginal`, optional `deskId`/`userId`/`locationNote`, required `provenanceNote`, `expectedRevision`. Requires Dak.Edit, active nonterminal Dak, active referenced Desk/User and active desk membership if both are supplied. A location cannot be attached to false/unknown existence. Notes have 1000-character limits. Stale/terminal writes return `409`; invalid input `400`; unauthorized/archived writes `403`. |

Intake mutation transactions reuse the existing retry/success-verification pattern and audit table. Upload files are saved once per operation and compensated if the database mutation fails. Concurrent registration losers remove their own staged upload before returning the winning request receipt or a conflict. PostgreSQL uniqueness remains the final collision guard, not an application-only existence check.

The Delhi calendar-date helper uses `Asia/Kolkata`; UTC ISO slicing is removed. Registered activity now says **“Registered in inward correspondence”**, without claiming marking. Frontend changes are limited to these correctness/API-adaptation needs; no quick-entry redesign was included in this backend phase.

## Physical-original foundation and deferred policy

Implemented: explicit unknown/yes/no existence; optional canonical Desk/User custody; legacy/other location note; mandatory observation/provenance note on update; timestamp/actor; audited before/after values through existing OfficialRecord audit and a physical-update audit action. Marking/forwarding/returning never changes physical-original fields. Responsible officer is not assumed to hold the paper.

Deferred: a dedicated immutable physical movement timeline and its UI, transfer/acceptance/recall rules, multi-recipient handling and post-disposal corrections. Existing generic audit retains changes in this phase; it is not presented as a new immutable physical-history engine. No policy-specific routing or automatic physical movement was invented.

## Verification

- Relevant backend suites plus focused intake/PostgreSQL tests: **287 passed, 0 failed, 0 skipped** (including 8 real PostgreSQL tests), final execution duration 1m49s.
- PostgreSQL 17.11: disposable databases, all migrations applied to fresh fixtures, direct SQL/EF constraint rejection, concurrent normalized duplicates, concurrent same-key requests including uploaded scans, a forced commit between replay/diary lookups, blank/duplicate historical migration rollback, preserved historical display text and closure-versus-upload locking/compensation.
- API tests: required diary, unknown context, duplicate conflict, changed-payload replay conflict, replay after archival, target identity/title redaction, terminal mutation rejection, historical reads/default active register, date/activity correctness and physical custody independent of routing.
- Delhi boundary test (`node --test tests/dak-intake.test.mjs`): **1 passed**.
- Strict targeted TypeScript check of changed Dak components/types: **passed**.
- Vite production bundling: **passed**, with the existing large-chunk advisory.
- Full application TypeScript verification: base commit and implementation both produce the **same 39 diagnostics**, with **0 new diagnostics**. These occur in App/AppShell/Court/editor/land/Matter files outside this change. Therefore the aggregate `npm run build` cannot be reported as passing. Baseline was checked from an isolated source export with the same dependency tree; unrelated type errors were not changed in this intake phase.
- EF migration/model consistency: **no pending model changes**. `git diff --check`: clean. Three existing xUnit2031 warnings in AwardExtractionRuleEngineTests remain.

Run PostgreSQL tests with `LAC_TEST_POSTGRES` set to a disposable **local** administrative connection. The fixture creates uniquely named `lac_dak_test_<GUID>` databases and drops only validated test database names; it never migrates the named runtime/office database.

```powershell
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-restore --filter "FullyQualifiedName~DakTests|FullyQualifiedName~DakIntakeTests|FullyQualifiedName~DakPostgresTests|FullyQualifiedName~MyDeskTests|FullyQualifiedName~WorkItemTests|FullyQualifiedName~OutwardTests|FullyQualifiedName~MatterAuthorizationAndWorkspaceTests" --verbosity minimal
```

No main merge or runtime deployment is part of this change. The completion response supplies the pushed branch and final commit SHA.

## Exact changed files

- `docs/dak-phase-a-implementation.md`
- `scripts/audit-dak-diary.sql`
- `src/LAC.Api/DakEndpoints.cs`
- `src/LAC.Api/DakIntakeEndpoints.cs`
- `src/LAC.Domain/DakEntities.cs`
- `src/LAC.Infrastructure/ActivityProjectionService.cs`
- `src/LAC.Infrastructure/Configurations/DakConfiguration.cs`
- `src/LAC.Infrastructure/DakAuthorizationService.cs`
- `src/LAC.Infrastructure/DakIntakeMutations.cs`
- `src/LAC.Infrastructure/DakRegistrationReplay.cs`
- `src/LAC.Infrastructure/DakWorkflowService.cs`
- `src/LAC.Infrastructure/LacDbContext.cs`
- `src/LAC.Infrastructure/Migrations/20261005194104_HardenDakIntakePhaseA.cs`
- `src/LAC.Infrastructure/Migrations/20261005194104_HardenDakIntakePhaseA.Designer.cs`
- `src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs`
- `src/LAC.Web/src/dak/DakDetailWorkspace.tsx`
- `src/LAC.Web/src/dak/DakRegistration.tsx`
- `src/LAC.Web/src/dak/officeDate.d.ts`
- `src/LAC.Web/src/dak/officeDate.js`
- `src/LAC.Web/src/dak/types.ts`
- `src/LAC.Web/tests/dak-intake.test.mjs`
- `tests/LAC.Tests/DakIntakeTests.cs`
- `tests/LAC.Tests/DakPostgresTests.cs`
