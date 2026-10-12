# Land Records staff workflow V2 — verification and handoff

Date: 2026-10-12. Repository: `ashwanibaghel/LAC-Platform`.

- Exact fetched main/base commit: `37db57e5fa40cd353e70c3161abe62e23eba4cc5`.
- Isolated branch: `codex/land-records-staff-workflow-v2`.
- Isolated checkout: `C:/Users/ashwa/.codex/worktrees/land-records-staff-workflow-v2/LAC-Platform`.
- Published feature commit SHA is supplied in the delivery message and verified against the remote branch. A commit cannot contain its own SHA.
- API and correction contract: [LAND_RECORDS_STAFF_WORKFLOW_V2_API_CONTRACT.md](LAND_RECORDS_STAFF_WORKFLOW_V2_API_CONTRACT.md).

## Results and boundaries

The complete .NET regression run passed **743 tests, zero failures, zero skipped** (`staff-v2-full-final.trx`, 5 minutes 14 seconds). After the final narrow privacy and historical-hash ownership changes, the staff persistence, migration and actual-browser suite was rerun; its final result is recorded below. The added regression exercises mixed visibility of historical duplicate document rows, safe document references and prevention of correction bypass.

These are real API requests against isolated PostgreSQL with actual cookie authentication, live access checks, transaction locks, durable synthetic PDF bytes and hash readback. The browser test uses a real loopback Kestrel API and Vite UI, not mocked network responses. It verifies the human ownership explanation, authorized source identity, absence of confidence/type confusion, officer-confirmed shared-source workflow and canonical timeline links. Screenshots and result text are saved under ignored `out/browser-checks/`; TRX files are under ignored `out/test-results/`.

`npm run build` has **39 pre-existing TypeScript errors** at the exact base. The feature's normalized diagnostics are compared with a build of that base using the same installed dependencies. Existing errors involve shared shell, court/Matter and editor contracts outside this task. No unrelated fixes were made. The Vite production bundle builds successfully. Therefore the repository's full frontend build is not green, and this delivery does not claim complete staff-ready acceptance.

The actual office PDFs, production migrations, full local OCR/model acceptance and staff UAT were not run. Metadata/lifecycle, notification and temporary-proceeding UI integration still needs Anti's work under the published backend contract. The delivered frontend changes cover core uploads, P0 ownership handling and the canonical Village timeline.

## Feature matrix

| Feature | Backend/persistence result | UI/integration result |
| --- | --- | --- |
| Award number/date/metadata correction | PASS: scoped identity, field provenance, revision, reason and before/after audit | Contract/types ready; complete edit UI remains Anti |
| Void/archive/restore | PASS: dependent evidence and official graph retained; active reads exclude archived records | Complete lifecycle UI remains Anti |
| Direct upload / explicit later Analyze | PASS: Direct creates no OCR job; retry and later Analyze retain document identity | Core upload controls delivered and real browser checked |
| Multiple PP documents | PASS: required actual date, existing PossessionEvent model, selected Village/Khasras, same-date distinct records | Core document upload supports legal date; complete history UX remains Anti |
| Multiple Statement A / NM | PASS: optional dates, multiple records, existing NM review retained | Core upload arrays maintained; richer correction UX remains Anti |
| Canonical Notification source registration | PASS: arbitrary sections, actual date, missing-reference reason, multiple PDFs and explicit reuse | Complete registration/completion UI remains Anti |
| Canonical Village legal chronology | PASS: actual legal dates, source paths, canonical deduplication, later-date review warnings | Delivered and real browser checked |
| Section 35 temporary Offer/proceedings | PASS: distinct proceeding identity, duration without invented dates, no fabricated Award | Complete registration UI remains Anti |
| Authorization/integrity/retry/concurrency | PASS: live scoped checks, revisions, receipts, hash locks and rollback | Typed error/ownership consumers delivered |
| Historical migration/read compatibility | PASS on isolated upgrade; IDs/hash preserved, only unambiguous backfill | Legacy APIs retained; incomplete facts remain NeedsReview |
| P0 ownership conflict/correction | PASS: common policy across routes/modes, explicit officer corrections, redaction, source history | Human conflict panel and confirmed sharing tested in real browser |
| Complete frontend TypeScript build | FAIL at base and feature: 39 existing errors | Baseline limitation; no unrelated refactor |
| Complete office/staff acceptance | PENDING: real source/OCR and office UAT | PENDING: remaining Anti integration |

## Required acceptance scenarios

| # | Scenario | Result/evidence |
| --- | --- | --- |
| 1 | Manually created Award → Direct PDF → correct link, no OCR | PASS: durable upload, content/hash and zero job assertions |
| 2 | Same PDF → later explicit Analyze | PASS: same document, one active job on retry |
| 3 | Direct mode avoids unnecessary genre reconfirmation | PASS: no detectedType/confidence fields; browser ownership errors contain no genre warning |
| 4 | Mistyped Award number/date correction | PASS: original audit values, scoped duplicate validation, stale revision rejection |
| 5 | Duplicate Award void without evidence loss | PASS: officer duplicate resolution preserves documents, Khasras, court and possession graph |
| 6 | Archive/restore, authorization and 409 revisions | PASS: lifecycle/revision/live access tests |
| 7 | Three PP PDFs and distinct proceedings on same date | PASS: legal-date ordering, event links and corrections; no automatic Khasra possession |
| 8 | Two Statement A and two NM PDFs without mandatory dates | PASS: independent role records and retained NM reviews |
| 9 | Sections 4(1), 6, 9, 17(1), custom dated source | PASS: canonical notification tests |
| 10 | Timeline reflects linked Notifications and PP | PASS: canonical API assertions and real-browser source links |
| 11 | Section 35 temporary proceedings never become final Awards | PASS: temporary upload and extraction-rule regression |
| 12 | Legacy APIs/historical records remain readable | PASS: historical hash rows, legacy NM route, undated notifications and migration upgrade |
| 13 | Retry/wrong ownership/unauthorized write/rollback | PASS: real parallel requests, scoped access/revocation, injected persistence failure and source-byte preservation |

## P0 ownership matrix

| Scenario | Result |
| --- | --- |
| Wrong Award in core upload, extraction upload, stored-document recovery | PASS: structured `PDF_OWNERSHIP_CONFLICT` 409; same policy in Direct and UploadAndAnalyze |
| Exact same-Award retry | PASS: same document/attachment; `AlreadyAttached`, no duplicate job/link |
| Concurrent duplicate upload to different Awards | PASS: one owner succeeds; competing request gets conflict |
| Concurrent same-Award retries | PASS: one logical attachment |
| Staff selected wrong Award | PASS: authorized existing Award navigation; no ownership mutation |
| Previous link incorrect | PASS: confirmed relink retains detached source link, bytes, SHA and extraction history |
| Duplicate Award registration | PASS: validated duplicate void/relink; official dependencies preserved on source |
| Legitimate multi-Award source | PASS: officer confirmation, reason, real page range and coverage; third upload still conflicts |
| Hidden owner / Own-scope access / live revocation | PASS: authorized-only identity fields; unauthorized resolution/write rejected |
| Historical multiple Document rows with one SHA | PASS: all owners checked without consolidating rows; alternate identity cannot bypass ownership |
| Stale token / retry receipt / rollback | PASS: revision checks, stable retries and atomic source/target/audit/receipt rollback |
| Ownership versus document confidence | PASS: separate structured error panel; no RFC JSON or Unknown/Low confidence ownership display |

## Migration evidence

Two additive migrations are included; neither was applied to any office database:

1. `20261012111643_LandRecordsStaffWorkflowV2`: revision/state fields, attachment legal metadata and restrictive nullable PP/Village FKs, temporary proceeding and mutation receipt tables. Only unambiguous single-Village and known-role legacy links are backfilled, with migration audit. No missing legal dates or PP event relationships are invented. Rollback refuses destructive removal after official V2 usage.
2. `20261012114817_AllowSourceIdentifiedNotificationReferences`: filters canonical section/number/date uniqueness to nonempty references. Distinct evidence-backed absent-reference notifications remain valid; no rows or numbers are rewritten.

The migration regression upgrades a real synthetic historical schema from the migration preceding V2, verifies unchanged IDs/hash and preserves ambiguous multi-Village records. `dotnet ef migrations has-pending-model-changes` reports no pending changes.

## Failures encountered and resolved

Initial test runs exposed fixture normalization, a missing synthetic PDF directory and nonunique synthetic Village names; the fixtures were corrected. The first browser run exposed a success message being erased by parent refresh; the UI now retains the resolved document result and refreshes on close. Retests passed. The existing frontend TypeScript errors remain as the explicit baseline limitation above. A manual background API launch was rejected by automatic approval review; browser verification instead uses the guarded isolated test host, and no rejected launch was retried.

## Anti handoff

Use the exact routes/payloads/error examples in the API contract and shared `landRecordsStaffTypes.ts` / `LandRecordApiError.ts`. Obtain live revision and linked Village choices from `staff-workflow`. Keep a mutation UUID for identical network retries, generate a new UUID for a changed decision, and refresh on revision conflict. Do not substitute upload timestamps for legal dates.

Parse `code` before genre/analysis rendering. `PDF_OWNERSHIP_CONFLICT` renders only returned authorized `existingLinks`, with four choices: open existing Award, confirmed wrong-link correction, validated duplicate void/relink, or confirmed source sharing. Use the selected link's documentId and use the documentId returned by correction, because historical same-hash rows may have different identities. `hasRestrictedLinks` means another authorized officer must review inaccessible ownership; it does not permit bypass. Sharing requires real PDF pages and coverage explanation. Reason and explicit confirmation are mandatory.

Finish metadata/lifecycle forms, ordered multi-document correction/history, canonical notification upload/reuse and legacy completion, and temporary-proceeding registration. PP requires actual date; NM/Statement A dates remain optional. Legacy notification completion attaches evidence to the existing canonical ID, corrects eligible unfinalized metadata, then explicitly finalizes. Finalized or source-verified facts remain protected. Temporary Offer/proceedings stay distinct from final Awards.

No correction silently transfers verified facts, Khasras, court records, possession records or ingestion sessions. Existing extraction history stays on its original Award. NM review retargeting is protected; use reviewed replacement rather than altering the existing review's context. Missing legacy facts remain NeedsReview.

## Isolation and protected boundaries

All database writes used a new loopback-only PostgreSQL 17 cluster at `127.0.0.1:55441`, database `lac_staff_workflow_v2_test`, user `staff_workflow_test`, random `staff_v2_*` schemas and synthetic local PDFs. The factory refuses any other connection. Storage lives in synthetic temporary test directories. No office PDF was transmitted to an external AI service.

The original checkout, main, production/protected databases, office accounts and IIS deployment were untouched. No main merge was performed. Only the dedicated feature branch is published. Court/Dak/Matter/ONLYOFFICE and RBAC definitions were not refactored. The temporary test cluster is stopped after verification.

## Final narrow verification

Final staff persistence/migration/browser suite: **22 passed, zero failures, zero skipped** (`staff-v2-targeted-final.trx`, 1 minute 19 seconds), including the added mixed-visibility legacy hash test. Final TypeScript diagnostics: **39 base / 39 feature, identical normalized diagnostics**, exit 2; final Vite production build: **PASS**, exit 0, with the existing large-chunk warning. The final ownership guard also prevents correction into a target already owning another historical same-hash identity: **6 focused ownership tests passed, zero failures/skips** (`staff-v2-ownership-final.trx`, 55 seconds). The focused run compiled the final backend successfully; migration model and whitespace checks passed.

## Actual changed files

The following inventory is generated from the isolated checkout immediately before staging and publication.

- `.gitignore`
- `docs/LAND_RECORDS_STAFF_WORKFLOW_V2_API_CONTRACT.md`
- `docs/LAND_RECORDS_STAFF_WORKFLOW_V2_VERIFICATION.md`
- `src/LAC.Api/LandRecordsAwardGuard.cs`
- `src/LAC.Api/LandRecordsStaffEndpoints.cs`
- `src/LAC.Api/Program.cs`
- `src/LAC.Domain/Entities.cs`
- `src/LAC.Domain/LandRecordsStaffWorkflow.cs`
- `src/LAC.Infrastructure/AwardExtractionRules.cs`
- `src/LAC.Infrastructure/AwardIngestionService.cs`
- `src/LAC.Infrastructure/AwardPdfExtraction.cs`
- `src/LAC.Infrastructure/AwardVerification.cs`
- `src/LAC.Infrastructure/AwardWorkflowService.cs`
- `src/LAC.Infrastructure/DocumentEvidenceQueries.cs`
- `src/LAC.Infrastructure/DocumentStorage.cs`
- `src/LAC.Infrastructure/LacDbContext.cs`
- `src/LAC.Infrastructure/LandRecordPdfStore.cs`
- `src/LAC.Infrastructure/LandRecordsMutationStore.cs`
- `src/LAC.Infrastructure/LandRecordsPdfOwnership.cs`
- `src/LAC.Infrastructure/LandRecordsStaffContracts.cs`
- `src/LAC.Infrastructure/LandRecordsStaffDocuments.cs`
- `src/LAC.Infrastructure/LandRecordsStaffModel.cs`
- `src/LAC.Infrastructure/LandRecordsStaffNotifications.cs`
- `src/LAC.Infrastructure/LandRecordsStaffQueries.cs`
- `src/LAC.Infrastructure/LandRecordsStaffWorkflowService.cs`
- `src/LAC.Infrastructure/LrWorkflowService.cs`
- `src/LAC.Infrastructure/Migrations/20261012111643_LandRecordsStaffWorkflowV2.cs`
- `src/LAC.Infrastructure/Migrations/20261012111643_LandRecordsStaffWorkflowV2.Designer.cs`
- `src/LAC.Infrastructure/Migrations/20261012114817_AllowSourceIdentifiedNotificationReferences.cs`
- `src/LAC.Infrastructure/Migrations/20261012114817_AllowSourceIdentifiedNotificationReferences.Designer.cs`
- `src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs`
- `src/LAC.Infrastructure/TemporaryInstrumentDetector.cs`
- `src/LAC.Web/src/App.tsx`
- `src/LAC.Web/src/award/AwardPdfOwnershipConflictPanel.tsx`
- `src/LAC.Web/src/award/LandRecordApiError.ts`
- `src/LAC.Web/src/award/landRecordsStaffTypes.ts`
- `src/LAC.Web/src/award/VillageAcquisitionTimeline.tsx`
- `src/LAC.Web/tests/land-records-staff-browser.mjs`
- `tests/LAC.Tests/ApiNavigationTests.cs`
- `tests/LAC.Tests/AwardPdfExtractionTests.cs`
- `tests/LAC.Tests/LandRecordsStaffBrowserTests.cs`
- `tests/LAC.Tests/LandRecordsStaffMigrationTests.cs`
- `tests/LAC.Tests/LandRecordsStaffPersistenceTests.cs`
- `tests/LAC.Tests/StaffWorkflowTestSupport.cs`
