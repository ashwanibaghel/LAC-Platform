# Matter functional repair and ONLYOFFICE audit delivery

Rejected candidate: `deb0656baa8537c06a30d924c73db1b601fbbb7f`.
Branch: `codex/dak-matter-foundation-20261010` in `E:/LAC-Dak-Matter-Foundation-20261010`.
No visual redesign, major editor reconstruction, new pilot accounts/environments, merge or deployment.

## Published implementation contract

Contract commit: `176960bca988cf897baf5372ed4476be36c673fa`, published to origin before fixes.
Read [Anti integration contract](MATTER_FUNCTIONAL_ONLYOFFICE_CONTRACT_2026-10-11.md)
for exact routes, payloads, statuses, canonical offsets, UI selection mapping, draft compatibility,
physical layout, server persistence/conflicts, transactional send and integration gates.

## Verified findings and disposition

| Finding | Verification | Disposition |
| --- | --- | --- |
| Village hides General and custom types | Both APIs use the same Matter.View query authorization; rejected App iterates a fixed type list | Fixed on this isolated branch, commit `9d19bb68db510f29ddec80bb5655646b98070b68`; Anti can apply this small functional commit |
| API error appears as empty Village Matters | Village component checked loading/empty but omitted `matters.error` | Existing ErrorState now rendered; no visual changes |
| Arbitrary selected-text remark crashes | Text node is cast to HTMLElement then getAttribute is called; quote search/trim fallbacks also select wrong repeated occurrences | Frontend correction specified for Anti; backend exact range/hash validation retained |
| Invalid uploaded Word link | `/editor?documentId=...` is not registered; supported route is `/matter-drafts/{MatterDraft.Id}` | Anti must remove invalid link; use authorized binary download for ordinary uploads, known draft route for formatted drafts |
| Two potentially conflicting Green Sheet concepts | Legacy MatterDraft Noting is editable document draft; MatterOfficialNote is the numbered immutable chain | Keep one official chain. Label legacy drafts as supporting/legacy, preserve data, use ONLYOFFICE for Letters/formatted drafts |
| Inconsistent legacy physical profile | Backend DOCX enforces Legal portrait + 45/20/25/20 mm mirror policy; legacy frontend still has provisional A4/25 mm inside profile | Exact profile and correction points supplied to Anti; separate Letter policy remains |
| Office save status/authority limitations | Host only ready/error; callbacks persist DOCX, not canonical JSON; no correlated force-save barrier, actor revocation/lease or immutable DOCX revision history | Block stronger autosave/official DOCX submission claims until backend integration gates are implemented and tested |

No production backend API/schema correction is required for the reported Village filtering or
remark exception. There are no new production backend routes or migrations in this delivery.
The current ONLYOFFICE service is not an automatic official-note submission adapter. No live
Document Server installation/connectivity or end-to-end Office save acceptance is claimed.

## Actual pilot database/API read proof

Evidence: `C:/Users/ashwa/.codex/artifacts/matter-functional-audit-20261011/authorized-api-consistency.jsonl`.
Ran the actual unchanged API handlers in an in-process HTTP TestServer, selecting the two
EXISTING pilot identities through test authentication and using their persisted users/roles
and the real authorization services. PostgreSQL connection explicitly set
`Options=-c default_transaction_read_only=on`, bootstrap/workers disabled. No cookie login,
last-login update, seeding, registration or operational write was performed. This is API-handler
and database verification, not a new browser workflow or a new running API environment.

For both existing officers, HTTP 200 Directory filter and Village endpoint returned precisely:

* `3f02025a-a06b-452b-9450-f4d9cdc2e063`, General, SYNTHETIC VERIFY Matter
* `4ff58657-7893-4f4a-bad9-08d6c2e2929f`, General, SYNTHETIC MANUAL Matter

Authoritative VillageId: `2936f2e4-0fe2-40f1-b614-fd9c309702d6`.
Read-only SQL independently confirmed the same records. This isolates the reported discrepancy
to frontend grouping, without rewriting General to Other or expanding permissions.

## Regression validation

Validation artifacts: `C:/Users/ashwa/.codex/artifacts/matter-functional-audit-20261011`.
Only the previously used disposable test cluster `E:/LAC-Noting-Disposable-PG-20261011`,
loopback port 55483, was restarted for regressions. Fixtures create/drop random
`lac_dak_test_*` databases; no existing application database is a test target.
API regression hosts are in-process and bind no application ports. Tests run serially.

* Frontend grouping tests: **4 passed, 0 failed**, including General/custom types,
  preferred ordering without filtering, prototype-like type names and blank/null fallback.
* Complete isolated frontend production build: **passed**, standard existing bundle-size warning.
* Backend/test build: **passed**, three pre-existing xUnit2031 warnings in AwardExtractionRuleEngineTests;
  no added test warnings after the nullable assertion correction.
* Broad affected Matter + ONLYOFFICE run: **141 passed, 1 failed, 0 skipped, 142 total**.
  The sole failure was a NEW test comparing a pre-persistence 100 ns timestamp with
  PostgreSQL's persisted microsecond timestamp. The assertion now compares exact
  microseconds; no production behavior or business assertion was changed.
* Corrected new PostgreSQL HTTP suite: **3 passed, 0 failed, 0 skipped**. These include
  the formerly failing history case and both other new API cases. Combined case coverage:
  **all 142 affected cases verified**, across the broad run and focused corrected rerun.
  This is not represented as a second clean full-suite execution. All **35 ONLYOFFICE**
  cases passed in the broad run, including the added DOCX/official-note separation case.

Evidence: `results/functional-matter-onlyoffice.trx` and
`results/functional-audit-corrected.trx` in the validation artifact directory.
Regression test commit: `f0fa4276cffa3d64f4648687c897dfe802157eef`.
All generated databases were removed by fixtures. Only postgres/template0/template1
remained on the test cluster before it was stopped; the dedicated test server is stopped.

New PostgreSQL HTTP regressions cover complete paginated Directory/Village set equivalence,
other-Village/archive exclusion, workstream narrowing, revoked membership/permission and
unauthenticated requests; exact repeated/multiline/whitespace/emoji remark spans and negative
range/hash/version/surrogate inputs; replay conflicts and addressed/reopened receipt history.
New ONLYOFFICE test proves callback changes DOCX while leaving stale legacy JSON and both
official/working noting tables untouched. Existing tests cover transactional send, number races,
immutable notes, private drafts, shared documents and callback/layout/error security.

Reproduce with your existing dedicated disposable regression server only:

```powershell
$env:LAC_TEST_POSTGRES='Host=127.0.0.1;Port=55483;Database=postgres;Username=postgres;Pooling=false'
$env:LAC_RBAC_TEST_SERVER=$env:LAC_TEST_POSTGRES
$env:LAC_RBAC_TEST_PORT='55483'
$env:MATTER_CONTEXT_POSTGRES_TESTS='1'
$env:ConnectionStrings__DefaultConnection=$env:LAC_TEST_POSTGRES
dotnet test tests/LAC.Tests -r win-x64 --no-build --no-restore --filter 'FullyQualifiedName~Matter|FullyQualifiedName~OnlyOfficeTests'
# Actual run used external BaseOutputPath and serial.runsettings recorded in the artifact directory.
cd src/LAC.Web
npm run test:village-matters
```

## Changed files and remaining handoff

Production change is limited to `src/LAC.Web/src/App.tsx` and new
`src/LAC.Web/src/matter/matterTypeGroups.ts`; package.json adds a regression command.
Tests: new `MatterFunctionalAuditTests.cs`, shared internal API test factory in
`MatterNotingApiTests.cs`, added callback separation case in `OnlyOfficeTests.cs`, and
`src/LAC.Web/tests/village-matter-groups.test.mjs`. Documentation: this report and Anti contract.

Anti must implement the selected-text DOM mapping and exact quote workflow, correct Word
fallback/known-draft navigation, legacy profile/copy and single-chain integration behavior.
Future backend Office save barrier/session revocation/immutable snapshots/canonical conversion
remain explicitly gated; native Matter routing policy is still pending. No unsupported viewer,
import/conversion or save-status route is represented as existing.

The rejected pilot worktree and processes remain at their original candidate. Main, IIS,
ports 5190/5191/5610/5611 and existing application databases remain untouched by fixes.
Final inspection confirmed a clean pilot worktree at `deb0656baa8537c06a30d924c73db1b601fbbb7f`
and unchanged pilot listener PIDs: API 11440 (5610), frontend 15020 (5611), PostgreSQL
10080 (55683). The final documentation commit SHA accompanies the delivery.
