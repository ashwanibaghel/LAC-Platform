# Land Records Staff Workflow V2 — Integration, Publication & Acceptance Report

**Branch:** `codex/land-records-v2-ui-continuation`  
**Dedicated Worktree:** `C:/Users/ashwa/.codex/worktrees/land-records-v2-ui/LAC-Platform`  
**Base Codex Milestone:** `codex/land-records-staff-workflow-v2` (`af795fe843b22efdcf6d1eb50e524a2b378b0e50`)  
**Backend Parent Remote HEAD:** `7bbec8053170ad9c710adb312e9e7a2d3810f95d`  
**Continuation Checkpoint Starting SHA:** `7eb3165dae217422a437c8493c59b92d2d62ac85`  
**Current Active SHA:** `Pending final commit`  
**Repository:** `ashwanibaghel/LAC-Platform`  
**Date:** 2026-10-12  

---

## 1. Executive Summary

This report documents the final UI corrections, deterministic multi-Village statutory notification resolution, comprehensive acceptance gap closures, and independent end-to-end acceptance verification of the Land Records Staff Workflow V2 platform.

The delivery reconciles:
- The authoritative Codex V2 staff backend milestone (`af795fe843b22efdcf6d1eb50e524a2b378b0e50`)
- Reconciled backend parent HEAD (`7bbec8053170ad9c710adb312e9e7a2d3810f95d`)
- Preserved Anti premium UI (`7eb3165dae217422a437c8493c59b92d2d62ac85`)
- P0 fix for statutory notification multi-Village selection defect (eliminating unsafe `villages?.[0]` defaulting)
- Deterministic multi-Village enforcement with blank-selection blocking, valid submission, and wrong-Village rejection
- Real authenticated browser acceptance tests for Award Lifecycle (Void, Restore, Archive), multiple core document roles (PP, NM, Statement A), attachment operations (Update, Detach, Restore), and targeted negative paths (stale revisions, unauthorized writes, retry idempotency, PDF hash verification)
- Complete regression validation against isolated loopback PostgreSQL 17 and Kestrel.

---

## 2. Worktree & Agent Separation Audit

Per strict architectural boundary rules, Codex and Anti operate in completely isolated worktrees on separate branches:

| Agent / Workstream | Worktree Filesystem Path | Branch Name | Head SHA / Status | Role & Scope |
| :--- | :--- | :--- | :---: | :--- |
| **Active Frontend Delivery Worktree** | `C:/Users/ashwa/.codex/worktrees/land-records-v2-ui/LAC-Platform` | `codex/land-records-v2-ui-continuation` | Active | Active UI continuation and verification worktree |
| **Codex (V2 Staff Backend Worktree)** | `C:/Users/ashwa/.codex/worktrees/land-records-v2-verify/LAC-Platform` | `codex/land-records-v2-integration` | `7bbec805` | Reconciled backend checkpoint parent |
| **Preservation Snapshot** | `C:/Users/ashwa/.codex/worktrees/land-records-v2-integration/LAC-Platform` | `codex/land-records-v2-ui-preserved` | Archive | Preserved snapshot (unmodified archive reference) |
| **Anti (Original Main Workspace)** | `C:/LAC-Platform` | `anti/land-records-premium-ui-v2` | `8089517` | Read-only reference |

Neither agent shares a dirty working tree or uncommitted files with the other.

---

## 3. P0 Multi-Village Notification Correction & Determinism

### 3.1 Defect Root Cause:
In `src/LAC.Web/src/App.tsx`, `AwardNotificationsSection` previously received:
```tsx
villageId={a.villages?.[0]?.id || ""}
```
When an Award spans multiple Villages, this silently defaulted to the first Village in the array without officer verification, violating statutory accuracy standards.

### 3.2 Resolution Implemented:
1. **Context Pass-through:** `AwardNotificationsSection` in `src/LAC.Web/src/App.tsx` now receives `linkedVillages` extracted from the canonical `staffWorkflow.data?.villages` (fallback to `a.villages`):
   ```tsx
   <AwardNotificationsSection
     key={`notifications-${id}`}
     awardId={id}
     linkedVillages={
       staffWorkflow.data?.villages?.map((v) => ({ id: v.villageId, name: v.name })) ||
       a.villages?.map((v: any) => ({ id: v.id, name: v.name })) ||
       []
     }
     notifications={staffWorkflow.data?.notifications || []}
     onRefresh={() => setRefresh((x) => x + 1)}
   />
   ```
2. **Adaptive Village Selector (`AwardNotificationsSection.tsx`):**
   - **Zero linked Villages:** Renders an explicit alert blocking registration submission: *"No Villages are linked to this Award. A valid linked Village is required before registering statutory notifications."*
   - **Single linked Village:** Displays a read-only badge `Linked Village: {name}` and auto-binds the selection.
   - **Multiple linked Villages:** Renders a mandatory `<select>` dropdown (`Target Village *`) with an initial `(Select Target Village)` prompt.
3. **Double Validation:** Client-side validation blocks submission when no valid linked village is selected; server-side validation in `UploadNotificationAsync` validates `villageId` against `AwardVillages` and returns HTTP 400 `VillageContextRequired` on mismatch.
4. **Context Isolation:** `selectedVillageId` state auto-resets when switching Award or village contexts.

### 3.3 Deterministic Multi-Village Acceptance Verification:
- **Two Distinct Linked Villages Enforced:** Test asserts `subdivision.villages.items.length >= 2` and `assert.notEqual(villageId, villageId2)`; links `villageId2` to the target award via Khasra batch. Fails immediately if fewer than two villages exist.
- **Blank Selection Blocking:** Asserts submit button `Register Source Document` is disabled when target village selection is blank.
- **Wrong-Village Rejection:** Sends upload request with unlinked village ID; asserts server rejects with HTTP 400 `VillageContextRequired`.
- **Valid Submission & Server Readback:** Selects valid village, submits gazette source PDF and date; asserts HTTP 201 Created and verifies finalized status via `GET /api/awards/{id}/staff-workflow`.

---

## 4. Test Suite & Verification Matrix

The test framework clearly separates **mocked UI contract testing** from **actual isolated PostgreSQL 17 & Kestrel API execution**:

| Test Layer | Test Identifier | Environment / Backing Store | Result | Duration | Notes / Status |
| :--- | :--- | :--- | :---: | :---: | :--- |
| **Frontend Type Check** | `npx tsc --noEmit` | `src/LAC.Web` compiler | **PASS** | 2.1 s | 0 errors in entire `src/LAC.Web` project |
| **Frontend Production Build** | `npx vite build` | Vite 8 + Rolldown | **PASS** | 1.11 s | Clean production bundle generated |
| **Full Project Solution Check** | `npx tsc -b` | Root tsconfig project references | **FAIL (Expected Baseline)** | 4.8 s | **37 pre-existing baseline diagnostics** in unrelated modules (`AppShell.tsx`, `court/`, `editor/`, `matter/`). **0 new diagnostics** introduced by Land Records V2 code. |
| **Document Conflict UI Tests** | `node --test tests/land-records-document-conflict.test.mjs` | **Mocked UI Contract** (Playwright routes with synthetic 409 responses against local Vite dev server) | **PASS** (8/8) | 34.6 s | TAP console report (all 8 scenarios pass) |
| **Full .NET Regression Suite** | `dotnet test tests/LAC.Tests/LAC.Tests.csproj` | **Real Isolated PostgreSQL 17** (`127.0.0.1:55441`, db `lac_staff_workflow_v2_test`) | **PASS** (813 Passed, 1 Skipped*) | 7 m 18 s | **813 Passed, 0 Failed, 1 Skipped** (*`LandRecordsStaffBrowserTests` skipped when browser flag unset). Raw execution log preserved in machine-readable TRX: `docs/verification-evidence/lac-tests-regression.trx` (6.55 MB). |
| **Real Browser E2E Acceptance** | `LandRecordsStaffBrowserTests`<br>(executing `land-records-staff-browser.mjs`) | **Real Loopback Kestrel API + Vite Dev Server + PostgreSQL 17 + Chromium (1366×768)** | **PASS** (1/1) | 55 s | All 13 critical scenarios verified with genuine authentication, readbacks, 403 permission tests, and negative checks. Machine-readable TRX: `docs/verification-evidence/lac-browser-acceptance.trx` (1.74 MB). |

---

## 5. Revalidation of Critical Scenarios & Acceptance Gaps

The test execution clearly distinguishes **user flows executed through the actual React UI DOM** from **direct authenticated API validation calls**:

### A. Flows Executed Through Actual React UI (Playwright & Chromium at 1366×768)

1. **Scenario 1: Award Workspace Overview**
   - **Execution Method:** **Actual React UI**. Navigates to `/awards/{id}`, waits for React DOM hydration, and verifies Award overview components loaded directly from `GET /api/awards/{id}/workspace` and `staff-workflow`.
   - **Visual Evidence:** `award-overview-premium.png` (Server-Confirmed Post-Submit Result).

2. **Scenario 2: Real 409 Ownership Conflict & Officer Resolution**
   - **Execution Method:** **Actual React UI**. Clicks `Import / Review Data` in Award workspace, selects duplicate PDF in file drawer, uploads, observes React DOM alert `#pdf-ownership-conflict` with human-readable explanation and authorized existing Award link, clicks `Officer link correction`, selects `ShareSource`, enters source page coverage and justification, and submits.
   - **Visual Evidence:** `ownership-conflict-reassignment.png` (Active Conflict Resolution State) and `ownership-conflict.png`.
   - **Readback Assertion:** `GET /api/awards/{target.id}/core-documents` confirms attachment has `isSharedSource === true` and documentId matches original source document.

3. **Scenario 3: Canonical Village Acquisition Timeline**
   - **Execution Method:** **Actual React UI**. Navigates to `/villages/{id}`, expands timeline event nodes via button clicks, and asserts grouped canonical Award relationships and functional `Source PDF 1` links.
   - **Visual Evidence:** `village-timeline-canonical.png` (Server-Confirmed Post-Submit Result) and `canonical-timeline.png`.

4. **Scenario 4: Direct Upload Slot (Village Core Records)**
   - **Execution Method:** **Actual React UI**. Navigates to `/villages/{id}?section=core`, locates Award card, clicks `+ Attach` on Statement A slot, uploads PDF in dialog, clicks `Direct Upload`, and asserts status transition to `Attached` without extraction jobs.
   - **Visual Evidence:** `direct-upload-slot.png` (Pre-Submit Form State).

5. **Scenario 5: Smart Core Intake Proposal & Confirmation**
   - **Execution Method:** **Actual React UI**. Clicks `Upload Core Documents`, uploads staged PDF in Smart Core Intake modal, clicks `Analyse`, verifies extraction proposal in `.intake-review-list`, and clicks `Confirm & Link`.
   - **Visual Evidence:** `smart-intake-proposal.png` (Server-Confirmed Proposal State) and `integrated-smart-and-direct.png`.

6. **Scenario 6: Multi-Village Statutory Notification Selector**
   - **Execution Method:** **Actual React UI**. Clicks `+ Register Notification Source`, opens drawer, verifies mandatory `Target Village *` select dropdown, asserts blank selection blocks submission, selects valid village, enters date and gazette remarks, and submits.
   - **Visual Evidence:** `notification-multi-village-picker.png` (Pre-Submit Form State).

7. **Scenario 7: Statutory Notification Finalization**
   - **Execution Method:** **Actual React UI**. Submits notification from drawer, verifies card appears in workspace with `Finalized` badge.
   - **Visual Evidence:** `notification-finalized.png` (Server-Confirmed Post-Submit Result).
   - **Readback Assertion:** `GET /api/awards/{id}/staff-workflow` confirms `isFinalized === true` with attached gazette PDF.

8. **Scenario 8: Section 35 Temporary Occupation Proceedings**
   - **Execution Method:** **Actual React UI**. Clicks `+ Record Section 35 Proceeding` on Village Core tab, completes drawer fields (purpose, reference number, date, PDF), and submits.
   - **Visual Evidence:** `temporary-proceedings-section35.png` (Pre-Submit Form State).
   - **Readback Assertion:** `GET /api/villages/{id}/acquisition-timeline` confirms temporary proceeding event with matching title appears in village timeline.

9. **Scenario 9: Award Metadata Correction**
   - **Execution Method:** **Actual React UI**. Clicks `Edit Metadata` button, updates date to `2002-03-02`, enters mandatory justification, and saves.
   - **Visual Evidence:** `award-metadata-correction.png` (Pre-Submit Form State).
   - **Readback Assertion:** `GET /api/awards/{id}/staff-workflow` confirms `awardDate === "2002-03-02"`.

10. **Scenario 10: Award Lifecycle Operations (Void, Restore, Archive)**
    - **Execution Method:** **Actual React UI**. Executes Void, Restore, Archive, and final Restore directly through `AwardLifecycleModal` dialogs, asserting UI badges (`Archived (Void)`, `Archived (Archive)`) and server readbacks.

---

### B. Validations Executed Via Authenticated Direct API Calls

11. **Scenario 11: Multiple Core Documents & Deterministic Same-Date Possession Proceedings**
    - **Execution Method:** **Authenticated Direct API Calls**.
    - **Multiple Documents:** Uploads second Statement A and two field map schedules (`role=NM`).
    - **Negative Check:** PP upload without possession date rejected with HTTP 400 `PossessionDateRequired`.
    - **Deterministic Same-Date PP Validation:** Uploads two genuinely distinct Possession Proceedings sharing the **exact same legal date** (`2002-04-01`):
      - PP #1 attached to Village 1 (`villageId`) with title *"Kabza Karwahi Village 1"*.
      - PP #2 attached to Village 2 (`villageId2`) with title *"Kabza Karwahi Village 2"*.
      - Asserts distinct document IDs, distinct attachment IDs, distinct possession event IDs, and validates that readback from `GET /api/awards/{target.id}/core-documents` accurately reflects canonical village and attachment associations for both records.

12. **Scenario 12: Attachment Metadata Correction, Detach & Restore**
    - **Execution Method:** **Authenticated Direct API Calls**.
    - Calls `PUT /api/awards/{id}/core-document-links/{attachmentId}` for Update (title and remarks updated), Detach (active list excludes attachment), and Restore (active list includes attachment again).

13. **Scenario 13: Targeted Negative-Path Checks, Authorization & Idempotency**
    - **Execution Method:** **Authenticated Direct API Calls**.
    - **13a-c. Stale Revision Concurrency:** Submitting `expectedRevision: 999` returns HTTP 409 `RevisionConflict` across metadata, lifecycle, and attachment links.
    - **13d(i). Anonymous Unauthenticated Writes:** Requests without session cookies return HTTP 401 across metadata, lifecycle, attachment link, and resolution endpoints.
    - **13d(ii). Authenticated Permission-Limited Officer (403 Forbidden):** Provisions a dedicated authenticated officer account with viewer-only rights (`Award.View` scope, but omitting `Award.Edit`, `Award.CoreDocument.Upload`, and `Audit.View`). Confirms server returns **HTTP 403 `Forbidden`** for:
      - Award metadata correction (`PUT /api/awards/{id}/metadata`)
      - Award lifecycle mutation (`POST /api/awards/{id}/lifecycle`)
      - Attachment link correction (`PUT /api/awards/{id}/core-document-links/{attId}`)
      - Officer ownership resolution (`POST /api/documents/{id}/award-link-resolutions`)
    - **13e. Mutation Retry Idempotency & Audit Log Integrity:** Replays identical lifecycle mutation payload with matching `mutationId`. Asserts:
      - Initial mutation adds exactly 1 audit log entry (`countAfterFirst === 1`).
      - Replay mutation adds **0 duplicate audit entries** (`countAfterReplay === 1` and `auditsAfterReplay.items.length === auditsAfterFirst.items.length`).
      - Returns identical revision (`firstIdemRes.revision === secondIdemRes.revision`) and deeply equal result payload.
    - **13f. Source PDF Real SHA-256 Equality:** Downloads PDF binary stream via `GET /api/documents/{id}/content` and asserts exact SHA-256 hash equality (`crypto.createHash("sha256")`) and buffer equality (`downloadedBytes.equals(bytes)`) against original source.

---

## 6. Verifiable Visual Evidence (1366×768 Uncropped)

All evidence screenshots are stored in `docs/verification-evidence/` and confirmed to be exactly 1366×768:

| # | Artifact Filename | Dimensions | Form / Result State Type | Description |
| :-: | :--- | :---: | :---: | :--- |
| 1 | `award-overview-premium.png` | 1366×768 | **Server-Confirmed Result** | Premium Award workspace overview with Khasras and evidentiary status |
| 2 | `direct-upload-slot.png` | 1366×768 | **Pre-Submit Form State** | Village Core Records direct upload slots for Statement A, PP, and NM |
| 3 | `smart-intake-proposal.png` | 1366×768 | **Server-Confirmed Proposal** | Smart Core Intake extraction proposal and side-by-side confirmation |
| 4 | `ownership-conflict-reassignment.png` | 1366×768 | **Active Conflict Resolution** | Real HTTP 409 ownership conflict modal with audited ShareSource resolution drawer |
| 5 | `village-timeline-canonical.png` | 1366×768 | **Server-Confirmed Result** | Grouped Village Acquisition Timeline with canonical source PDF links |
| 6 | `notification-multi-village-picker.png` | 1366×768 | **Pre-Submit Form State** | Multi-Village statutory notification drawer displaying required target village select |
| 7 | `notification-finalized.png` | 1366×768 | **Server-Confirmed Result** | Award workspace displaying finalized Section 4(1) statutory notification card |
| 8 | `temporary-proceedings-section35.png` | 1366×768 | **Pre-Submit Form State** | Village Section 35 temporary occupation proceeding drawer with legal purpose & PDF |
| 9 | `award-metadata-correction.png` | 1366×768 | **Pre-Submit Form State** | Official Award metadata editor with duplicate check preview and audit reason |

*Compatibility Aliases & Machine-Readable Evidence:*
- `ownership-conflict.png` (identical to #4)
- `canonical-timeline.png` (identical to #5)
- `integrated-smart-and-direct.png` (identical to #3)
- `result.txt`: Contains raw stdout execution log: `PASS: all Land Records V2 acceptance scenarios, lifecycle transitions, multi-document roles, attachment corrections, and negative-path checks verified.`
- `lac-tests-regression.trx`: Full machine-readable MSTest/xUnit execution log for 814 tests across the .NET regression suite (6.55 MB).
- `lac-browser-acceptance.trx`: Full machine-readable execution log for the real browser/Kestrel acceptance test (1.74 MB).

---

## 7. Office UAT Limitations & Scope Boundaries

While the automated regression, integration, persistence, and browser acceptance suites pass at 100%, the following operational conditions must be observed during human Office User Acceptance Testing:

1. **Authentication Mode:** Acceptance tests execute using Cookie/Form-based authentication (`/api/auth/login`) with the bootstrap administrator account. Enterprise Single Sign-On / SAML / OIDC environments require external IdP configuration.
2. **Document Storage Provider:** Acceptance tests run with local disk document storage (`Storage:DocumentRoot`). Cloud Object Storage (e.g. S3 / Azure Blob / GCS) requires testing with provider-specific bucket credentials.
3. **OnlyOffice & Local Document Intelligence:** Background OCR workers and OnlyOffice editors were disabled (`DocumentIntelligence:Enabled=false`, `OnlyOffice:Enabled=false`) in acceptance test configuration to isolate staff workflow business logic. Real OCR verification requires external worker processes.
4. **Isolated PostgreSQL Cluster:** Verification executed against loopback PostgreSQL 17 on port 55441. Production deployment requires applying EF Core migrations against the central production database.
