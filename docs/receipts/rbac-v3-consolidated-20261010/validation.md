# RBAC V3 consolidated acceptance fix candidate

Baseline: `833f65eef243e9a99881b28e9bf5f4a98399d24c`.
Branch: `fix/rbac-v3-consolidated-acceptance-20261010`.
This candidate is for independent audit only. It is not merged or deployed.

## Architecture and authorization

The technical directory now returns only SYSTEM_ADMIN identities. ADM sees self and the permitted lower office authority tiers; Supervisor sees self and ordinary officers with their actual attached Helpers. Protected peer/superior and dormant technical identities are excluded server-side. Standard Officers use their own Helpers collection; Helpers have no management/delegation entry point.

The same directory policy scopes simplified and legacy account lists, guessed-ID profile GETs, per-user associations, Dak handler lookup, contributor options and activity actor search. Hidden simplified detail GETs return 404; protected legacy detail/association requests return 403. Allowed actions come from backend capability flags, while every write still independently enforces the frozen authority rules. Administrative self edit/reset/toggle is protected. Existing own operational desk/allocations paths remain available under their original authorization rules. Last-active-System-Admin protection remains intact.

System Admin provisioning is a separate canonical ADM form and one-time creation receipt, not office-directory membership. A separate technical recovery page accepts an exact official username and exposes only the target identity, active state and concurrency revision. It can issue a temporary credential or enable an inactive account; it cannot browse/wildcard-search office accounts, edit their authority or disable them through this new recovery surface. Recovery writes preserve existing technical authority, concurrency checks, audit and session invalidation. Existing targeted legacy technical maintenance APIs retain their frozen write contract; their GET browsing is scoped. Review this intentional distinction between technical authority and directory visibility.

Canonical ADM selection is restricted to System Admin at the API and UI. Custom titles containing ADM never confer authority. Editing a designation never implicitly upgrades authority. Permission presets, authority enum, account schema and migrations are unchanged.

### Reporting hierarchy limitation (unresolved portion)

The baseline has a stored officer-to-Helper parent, but no officer-to-officer reporting parent or office-boundary membership. The frozen authority model permits office-wide lower-tier management. This candidate groups those existing authority tiers and nests Helpers under their actual authorized parent. It does **not** claim that ordinary officers have been assigned to a particular Supervisor, invent an audit-CreatedBy reporting edge, or isolate staff between peer Supervisors. A stricter officer reporting tree cannot be implemented safely from this baseline without separately approved persisted relationships and assignment rules. That portion is stopped; no new authority model or schema was introduced.

## Independently reproduced root causes and corrections

| Handoff issue | Root cause | Candidate behavior |
|---|---|---|
| P0 directory/detail exposure | Lists were global and detail GET lacked corresponding visibility policy | Server-scoped directory, detail, association and actor search; real Helper nesting |
| Protected action buttons | Drawer rendered actions regardless of target manageability | Backend capability flags gate controls; self/peer/superior writes denied |
| Helper Add/Edit layout | Component used an undefined modal class; form flex/scroll selectors were incomplete | Shared opaque shell, bounded height, scrolling body and fixed header/footer |
| Read Only Court actions | First-run actions bypassed effective permission/allocation checks | Live server capability gates import/create; deliberate read-only empty copy |
| Misleading village Matter creation | Create button rendered before permission and authorized creation-context checks | Button requires Matter.Create and an authorized workstream for that village; useful copy otherwise |
| Court import 500 | ReadFormAsync ran on a request without form content | Authorization first, deliberate 400 for missing/non-form/malformed/empty workbook requests |
| Canonical ADM confusion | Nontechnical actors could select canonical ADM while remaining ordinary officers | Canonical option omitted and crafted assignment rejected; custom title stays separate |
| System Admin access mismatch | Detail DTO projected stored defaults rather than authority-derived access | Effective full modules, registry enabled and Land View + Write for top three authority levels |
| Helper navigation | Standard Officer entry existed only in profile/direct route | Home My Helpers entry requires Standard Officer + Assistants.Manage |
| Court empty layout | First-run card occupied a narrow grid column | Full-width responsive empty state only |
| Session rotation UX | API 401 did not clear auth or suppress subsequent requests | One expiry event, auth/cache clearing, dedicated login redirect and notice; stale responses discarded |
| Helper None label | Empty permission list fell through to Read Only | None, Read Only and Read + Write summaries distinguish actual selections |

Temporary credential dialogs now mask passwords by default while preserving existing Copy/acknowledgment controls. No credentials appear in these receipts.

## Verification

Complete backend: **1,335 passed, 0 failed, 0 skipped**, 44.3391 minutes. The complete run includes **75 configured real PostgreSQL cases**: Compensation History 28, RBAC V3 24, legacy RBAC 3, Dak 10, Dak custody 9 and Dak directory 1. The new consolidated HTTP matrix has 7 in-memory and 7 PostgreSQL cases. Compensation calculator unit/API/history groups total 206 passing cases. `backend-test-summary.json` gives every class count from the final TRX; counts above are subsets of the complete total.

EF upgrade/model checks passed: existing identities preserved without designation promotion, no pending migrations and no pending model changes. Production compensation sources, Domain and all migration files have no diff. `git diff --check` passed.

An earlier obsolete full run was stopped after identifying eight old-contract assertion failures and a too-broad self-edit guard. Fixtures were corrected to use authorized office actors while retaining explicit technical-account exclusion checks; the product guard was narrowed to preserve own operational desk/allocations APIs. The final complete run above passed all corrected cases. No failed assertion was disabled or skipped.

Focused frontend: **37 passed, 0 failed, 0 skipped**. Complete frontend: **324 passed, 0 failed, 0 skipped**. Production TypeScript/Vite build passed; final assets are `index-CNFStSWR.js` and `index-7y5XhlSp.css`. The existing bundle-size warning is unchanged in nature. Village creation-context responses also reset on navigation and ignore stale responses from the previous village.

Backend build passed with zero errors and three existing xUnit analyzer warnings in AwardExtractionRuleEngineTests. Actual PostgreSQL tests use only a newly initialized PostgreSQL 17 cluster at `127.0.0.1:55442` and generated disposable database names. History, RBAC and other PostgreSQL test connection variables all target this cluster's `postgres` administrative database. The cluster used relaxed disk synchronization for disposable regression speed; these tests do not establish power-loss durability.

The new real-cookie API matrix covers all authority directories and guessed IDs; protected actions; bounded technical recovery; canonical/custom/crafted requests; effective System Admin details; malformed import; Helper Land ceiling; parent module, workstream and allocation revocation without child edits. The same matrix runs against PostgreSQL. Existing suites cover parent desk/time ceilings, helper transitions, login/session rotation, last-admin protection and immutable snapshots/idempotency. Optional external workbook/live-DHC smoke branches were not configured; their test methods return early and are not counted as runner skips. Required PostgreSQL suites above were configured and executed; valid workbook behavior used existing synthetic workbook regressions, with no office workbook uploaded.

Compensation production sources, original compute DTO/API and migration chain are unchanged. Existing regression tests assert the office result **₹7,95,68,513.75**, saved/reopened snapshots, ownership, exact formula normalization, idempotency and database-level immutability.

## Genuine browser evidence

The companion `browser-evidence.json` records 21 verified screenshots, URLs, actual script names and CSS viewport **1366×768, scale 1**. Screenshots are unmodified browser JPEG captures; the browser service may encode them at a slightly smaller raster size. They are not resized mockups. Two superseded directory captures are excluded.

Early captures use production bundle `index-BMQRPI_j.js`; subsequent captures use `index-COPajfdd.js`, whose additional change correctly labels Helper None. Capture 21 was made after restarting only the owned test runtime with an API binary hash equal to the final candidate API binary. That restart reused only its own generated disposable browser database, never the acceptance database. Capture 23 verifies the final `index-CNFStSWR.js` after the village-context stale-response guard, using another freshly generated disposable browser database.

Evidence covers technical-only directory and effective System Admin detail; canonical ADM review and separate recovery; ADM/Supervisor protected self; Supervisor designation ceiling; Standard Officer My Helpers navigation; Helper Add/Edit/Review/masked credential; Court-only Matter controls; Read Only Court empty state; a real revoked Helper session redirected to login; None Helper no delegation; final authorized hierarchy and parent-bound Helper editor. Valid navigation showed no unexpected console errors. Repeated-401 suppression and single expiry notification are explicitly tested at the shared fetch boundary; the browser check verifies the real cookie revocation/redirect rather than claiming unavailable network instrumentation.

## Safety

The accepted integration and feature worktrees were not modified. Acceptance backend 5190 PID 22612, frontend 5191 PID 24168 and its PostgreSQL 55438 PID 13928 remain untouched. Anti 5088 was not controlled. No connection to `lac_platform`, the accepted manual database, office or production databases was used. All mutation fixtures were synthetic and disposable. The owned browser runtime 5194 was stopped, private test credential files removed and its four owned browser databases dropped after verification. All regression databases, including the two leftovers from the canceled obsolete run, were dropped. The owned cluster 55442 is stopped. Automatic approval review rejected recursive filesystem removal with the stated reason "blocked by policy"; the stopped disposable directory remains. The safe database cleanup and service stop succeeded, so no user approval was needed to finish verification.

No merge into integration/main, no office IIS deployment and no production restart. Final local/remote equality and exact changed-file manifest are recorded in the delivery report after the candidate push.
