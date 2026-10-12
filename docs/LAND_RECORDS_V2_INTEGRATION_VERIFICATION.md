# Land Records V2 — isolated integration checkpoint

Date: 2026-10-12. This report separates backend/API acceptance from Anti's unfinished complete V2 UI.

## Exact ancestry and scope

| Reference | SHA |
| --- | --- |
| Fetched main / common ancestor | `37db57e5fa40cd353e70c3161abe62e23eba4cc5` |
| Pushed backend milestone, unchanged | `af795fe843b22efdcf6d1eb50e524a2b378b0e50` |
| User-pinned Anti milestone | `d25bf6bdcb5f22b16b6cdfd245a0d88d783070b8` |
| Anti tip found at audit | `8089517c1181cb442abf1654dfcc7df51a17b95a` |
| Initial integration composition | `8cf2759d94a7863506ffb9b9b93d12f16fcbc511` |
| Immutable Anti UI preservation snapshot | `9ebdad74c58d7b6195b26e2797b73b6554bdd74f` |

The integration branch descends from the backend milestone. Only audited Land Records files/hunks from Anti were imported: premium directories/hierarchy/four Village tabs, Smart Intake/classifier/inventory/migration/tests, explicit grouped history and P0 conflict UI follow-up. Relevant source commits are `f4e0ae2`, `d3ef1a5`, `c3c1499`, `f6db41a`, `21b751d`, `26fcced`, `cb7feb3`, `5770cba`, `d25bf6bd`, `8089517c`. Anti's accumulated Court/intelligence/home/other changes were excluded. Program.cs and App.tsx were reconciled individually; no wholesale merge or unrelated cherry-pick was performed.

Publication SHA is supplied in the final handoff. Contract: [LAND_RECORDS_V2_INTEGRATED_API_CONTRACT.md](LAND_RECORDS_V2_INTEGRATED_API_CONTRACT.md), with the linked exact V2 officer correction DTO/example.

## Exclusive worktrees and preserved ownership

Codex backend checkout: `C:/Users/ashwa/.codex/worktrees/land-records-v2-verify/LAC-Platform`, branch `codex/land-records-v2-integration`.

Original concurrent-write checkout is now the preservation checkout: `C:/Users/ashwa/.codex/worktrees/land-records-v2-integration/LAC-Platform`, branch `codex/land-records-v2-ui-preserved`. The preserved SHA records all six modified/untracked UI files found after the initial integration commit. They are:

- Mixed `src/LAC.Web/src/App.tsx`: Anti's metadata/lifecycle controls, staff-workflow read, document/notification panels and modal wiring. Exact preservation delta against `8cf2759`: 52 additions / 9 deletions.
- Anti `AwardCoreDocumentsSection.tsx`: 741 added lines.
- Anti `AwardLifecycleModal.tsx`: 178 added lines.
- Anti `AwardNotificationsSection.tsx`: 676 added lines.
- Anti `AwardStaffMetadataModal.tsx`: 321 added lines.
- Anti `VillageTemporaryProceedingsSection.tsx`: 322 added lines.

An additional mixed shared-file hunk in `VillageCoreRecordsWorkspace.tsx` imported/rendered the unfinished temporary panel. It was already present in the initial composition and is retained in the preservation snapshot's full tree. It is excluded from the backend-only checkpoint and restored on the frontend continuation branch together with its implementation. No valid UI changes are discarded. Exact original diffs are reproducible with `git diff 8cf2759 9ebdad7`; the full preserved file/hunk content is available from `git show 9ebdad7:<path>`. A byte manifest, copied drafts and tracked patch were additionally saved under ignored `out/concurrent-ui-preserved/` in the preservation checkout.

No reset/clean/overwrite was used on Anti's draft files. The original Anti remote branch was untouched. The external Antigravity writer is not a Codex-managed task; no unsupported claim of remotely interrupting it is made. Backend verification moved to an exclusively controlled checkout with its own dependency installation. Anti must use its dedicated frontend continuation worktree, not the Codex backend directory. The final handoff lists that continuation branch/path and commit based on the exact backend checkpoint.

## Reconciliation results

| Area | Result |
| --- | --- |
| One registration policy | Smart Intake remains proposal/history; confirmation calls the V2 canonical attachment operation atomically. No competing Award/document registrar remains. |
| Source identity | Stage→Direct and Direct→Stage reuse original Document/SHA; two Village proposals share one source. Historical duplicate rows remain intact and hash-wide checked. |
| Direct / UploadAndAnalyze | Explicit modes retain identical ownership policy; Direct has no classifier/OCR/job. Smart native classification is explicit; full Award analysis remains later/explicit. |
| PP/NM/inventory | PP actual date required through both paths, existing event model and NM reviews retained, multi-document role arrays and legal ordering preserved. |
| Ownership/permissions | Structured authorized-only conflicts; live Own scopes/revocation; officer confirmation/reason/revisions; source evidence/history preserved. |
| Concurrency/rollback | Shared lock order for identity/hash/Award registration, Restore versus new upload, existing-Award creation reuse, receipt and failed confirmation/correction rollback verified. |
| Legacy missing hash | Recovery verifies actual bytes before ownership lookup; only a missing hash can be populated through successful verified operation. Conflicts roll back that population; existing hashes never replaced. |
| Timeline | Premium explicit Award grouping retained, source PDFs/warnings added, temporary events separate, detached PP hidden, no invented attribution or legal dates. |
| Main/protected systems | No main merge, production/protected DB/account write or IIS deployment. Original source PDFs were not sent to external AI. |

## Migration verification

Retained historical Smart migration `20261004181412_AddSmartCoreDocumentIntake` and V2 migrations `20261012111643_LandRecordsStaffWorkflowV2`, `20261012114817_AllowSourceIdentifiedNotificationReferences`. V2 designer target models include the additive intake entity; their original Up/Down operations and IDs are unchanged.

New `20261012133435_ReconcileSmartCoreIntakeWithStaffV2` changes only the intake DocumentId index from unique to nonunique. Village/hash uniqueness and restrictive FKs remain. Down explicitly rejects a rollback while multiple proposals reference a preserved source. No data/source rewrite is performed.

Real PostgreSQL tests exercise both Smart-first and V2-first histories. The latter reproduces the actual V2 Up operations/history with Smart initially absent, then applies the missing older Smart migration and reconciliation. Document IDs, original bytes/hash, staged proposal/text/version/creator/revision are preserved. The existing historical single/multi-Village migration test also passes. The combined model check reports no pending changes.

## Test evidence and limitations

The first combined full run aborted after 643 passed tests when its test host crashed; it is not a passing run. A later clean dependency/test launch encountered actual disk exhaustion. Recursive deletion of the old synthetic DB directory was rejected by automatic approval review (only reason returned: “blocked by policy”); it was not retried. The already-pushed clean previous milestone worktree was instead recoverably archived through the worktree lifecycle tool, freeing disk. Source/history remain in Git and the recoverable archive.

Exclusive full regression: **809 passed, one failed, zero skipped, 810 total** (`integration-exclusive-full.trx`, 9m27s). That browser failure identified the dangling unfinished temporary-panel reference above. It was isolated from the backend checkpoint, and the real browser flow was successfully rerun. Subsequent focused verification also added Restore/creation/recovery concurrency and legacy hash regressions. Final focused results are recorded below; the earlier full run is not relabelled as entirely green.

Actual browser tests use loopback Kestrel/Vite, real cookie authentication, isolated PostgreSQL and synthetic PDFs. They cover P0 officer sharing, readable conflict instead of JSON/confidence, grouped canonical source navigation, premium Village Direct attachment and Smart proposal confirmation. API tests independently cover old/new intake/upload modes, retries, simultaneous writers, source integrity, live RBAC, rollback and historical readability.

Frontend diagnostics were freshly checked with forced TypeScript builds and separate dependencies: parent `af795fe8` has **39 errors**, checkpoint has **38 errors**, with no added diagnostics. The difference is the old unused IconAward import now used by the premium hierarchy. The complete TypeScript build remains failing in unrelated legacy shell/Court/Matter/editor code. Earlier transient 49/48/39 counts from composition/concurrent drafts are not used as acceptance evidence. Vite production build passes. Calculator regression: **14 passed**.

No real office PDF/OCR/staff UAT or complete Anti metadata/lifecycle/notification/temporary UI acceptance is claimed. The imported old mocked conflict test is historical UI reference, not evidence of real persistence or a certified complete V2 UI. The preserved frontend continuation must finish its own checks; source backend tests do not certify those draft forms.

## Final focused verification

Final focused suite: **116 passed, zero failures, zero skipped**, `integration-final-verified.trx`, **1m35s**. It covers all changed Land Records/Smart Intake/history paths, both migration histories, extraction/navigation regressions and the actual browser flow. The final legacy hash-conflict regression verifies all historical owners are returned using the byte-verified hash even before it is committed. The earlier 114/115 targeted run exposed that conflict detail omission; it was corrected and rerun successfully. No final broad all-814-tests pass is claimed; unchanged regression evidence remains the 809 passing tests above, with the failed browser case subsequently fixed and passed in this final suite.

TRX/artifacts live under ignored `out/test-results/` and `out/browser-checks/` in the exclusive checkout; test source is committed and reproducible. Tests refuse any DB except loopback `127.0.0.1:55441`, database `lac_staff_workflow_v2_test`, user `staff_workflow_test`, using new random schemas and synthetic source files. The isolated test cluster is stopped after completion.
