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
| **Frontend Production Build** | `npx vite build` | Vite 8 + Rolldown | **PASS** | 1.11 s | Clean production bundle generated in 1.11s |
| **Repository Diagnostics** | `npx tsc -b` | Full project build | **FAIL (Expected Baseline)** | 4.8 s | **37 pre-existing baseline diagnostics** in unrelated modules (`AppShell.tsx`, `court/`, `editor/`, `matter/`). **0 new diagnostics** introduced by Land Records V2 code. |
| **Document Conflict UI Tests** | `node --test tests/land-records-document-conflict.test.mjs` | **Mocked UI Contract** (Playwright routes with synthetic 409 responses) | **PASS** (8/8) | 16.7 s | TAP console report (all 8 scenarios pass) |
| **Full .NET Regression Suite** | `dotnet test tests/LAC.Tests/LAC.Tests.csproj` | **Real Isolated PostgreSQL 17** (`127.0.0.1:55441`, db `lac_staff_workflow_v2_test`) | **PASS** (814/814) | 5 m 49 s | **814 Passed, 0 Failed, 0 Skipped** across all domain, API, infrastructure, intake, and staff persistence suites |
| **Real Browser E2E Acceptance** | `LandRecordsStaffBrowserTests`<br>(executing `land-records-staff-browser.mjs`) | **Real Loopback Kestrel API + Vite Dev Server + PostgreSQL 17 + Chromium (1366×768)** | **PASS** (1/1) | 53 s | All 13 critical acceptance scenarios, lifecycle transitions, multi-docs, detach/restore, and negative checks verified |

---

## 5. Revalidation of Critical Scenarios & Acceptance Gaps

### Scenario 1: Award Workspace Overview (1366×768)
- **Verified via:** `award-overview-premium.png`.
- **State Type:** **Server-Confirmed Post-Submit Result**.
- **Readback Assertion:** Validates rendering of Award overview populated directly from `GET /api/awards/{id}/workspace` and `staff-workflow` (canonical Khasra list, area totals, core document slot badges).

### Scenario 2: Direct Upload without OCR
- **Verified via:** `direct-upload-slot.png`.
- **State Type:** **Pre-Submit Form State** (Attach core PDF dialog with Statement A selected).
- **Readback Assertion:** Following submit, `GET /api/awards/{id}/core-documents` confirms attachment count increments, while `GET /api/awards/{id}/pdf-extractions` confirms extraction job count is strictly 0.

### Scenario 3: Smart Core Intake Proposal & Confirmation
- **Verified via:** `smart-intake-proposal.png`.
- **State Type:** **Server-Confirmed Proposal State** (intake review list showing extracted metadata fields awaiting confirmation).
- **Readback Assertion:** Following "Confirm & Link", badge updates to `.intake-badge-confirmed` and core document list reflects the linked attachment.

### Scenario 4: Real 409 Ownership Conflict & Audited Resolution
- **Verified via:** `ownership-conflict-reassignment.png`.
- **State Type:** **Active Server Conflict Resolution State** (HTTP 409 received from server; officer link correction drawer filled with ShareSource details).
- **Readback Assertion:** Following resolution submit, `GET /api/awards/{id}/core-documents` confirms attachment has `isSharedSource === true` and documentId matches original source document.

### Scenario 5: Grouped Village Acquisition Timeline
- **Verified via:** `village-timeline-canonical.png`.
- **State Type:** **Server-Confirmed Post-Submit Result**.
- **Readback Assertion:** Events loaded from `GET /api/villages/{id}/acquisition-timeline` group strictly by Award identity and provide functional links to source PDFs (`Source PDF 1`).

### Scenario 6: Multi-Village Statutory Notification Selector
- **Verified via:** `notification-multi-village-picker.png`.
- **State Type:** **Pre-Submit Form State** (drawer displaying required `Target Village *` dropdown).
- **Readback Assertion:** Validates blank selection disables submission; wrong-village payload returns HTTP 400 `VillageContextRequired`.

### Scenario 7: Statutory Notification Finalization
- **Verified via:** `notification-finalized.png`.
- **State Type:** **Server-Confirmed Post-Submit Result**.
- **Readback Assertion:** `GET /api/awards/{id}/staff-workflow` confirms `notifications` contains registered record with `isFinalized === true`, date `2002-01-15`, and attached gazette PDF.

### Scenario 8: Section 35 Temporary Occupation Proceedings
- **Verified via:** `temporary-proceedings-section35.png`.
- **State Type:** **Pre-Submit Form State** (Section 35 drawer populated with purpose, legal date, and PDF).
- **Readback Assertion:** `GET /api/villages/{id}/acquisition-timeline` confirms temporary proceeding event with matching title appears in village timeline.

### Scenario 9: Award Metadata Correction
- **Verified via:** `award-metadata-correction.png`.
- **State Type:** **Pre-Submit Form State** (edit drawer with modified date and audit reason).
- **Readback Assertion:** `GET /api/awards/{id}/staff-workflow` confirms `awardDate === "2002-03-02"`.

### Scenario 10: Award Lifecycle Operations (Void, Restore, Archive)
- **Verified via:** Browser UI interaction and API readbacks.
- **State Type:** **Server-Confirmed Mutations**.
- **Readback Assertions:**
  - Void: `recordStatus === "Archived"`, `archiveKind === "Void"`, UI displays `Archived (Void)`.
  - Restore: `recordStatus === "Active"`.
  - Archive: `recordStatus === "Archived"`, `archiveKind === "Archive"`, UI displays `Archived (Archive)`.
  - Final Restore: `recordStatus === "Active"`.

### Scenario 11: Multiple Core Documents per Role (PP, NM, Statement A)
- **Verified via:** Browser test execution against Kestrel.
- **State Type:** **Server-Confirmed Mutations**.
- **Readback Assertions:**
  - Statement A: At least 2 active attachments.
  - NM: At least 2 active attachments under canonical role `NM`.
  - PP: At least 2 active attachments with explicit `legalDate` and village jurisdiction.
  - Negative Check: PP upload without `legalDate` rejected with HTTP 400 `PossessionDateRequired`.

### Scenario 12: Attachment Metadata Correction, Detach & Restore
- **Verified via:** `PUT /api/awards/{id}/core-document-links/{attachmentId}`.
- **State Type:** **Server-Confirmed Mutations**.
- **Readback Assertions:**
  - Update: Title updated to *"Verified Revenue Map Sheet 1"*; revision increments.
  - Detach: `isDetached === true`; active document list excludes attachment.
  - Restore: `isDetached === false`; active document list includes attachment again.

### Scenario 13: Targeted Negative-Path Checks & Idempotency
- **Verified via:** API execution in browser test context.
- **State Type:** **Server-Confirmed Boundary Tests**.
- **Readback Assertions:**
  - Stale revision on metadata (`expectedRevision: 999`) -> HTTP 409 `RevisionConflict`.
  - Stale revision on lifecycle (`expectedRevision: 999`) -> HTTP 409 `RevisionConflict`.
  - Stale revision on attachment link (`expectedRevision: 999`) -> HTTP 409 `RevisionConflict`.
  - Unauthorized writes (unauthenticated context) -> HTTP 401 Unauthorized across metadata, lifecycle, and resolution endpoints.
  - Retry Idempotency: Replaying identical mutation payload with matching `mutationId` returns identical response and revision without duplicate audit logs.
  - Source PDF Integrity: `GET /api/documents/{documentId}/content` returns exact binary stream matching original SHA-256 hash.

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

*Compatibility Aliases & Log:*
- `ownership-conflict.png` (identical to #4)
- `canonical-timeline.png` (identical to #5)
- `integrated-smart-and-direct.png` (identical to #3)
- `result.txt`: Contains raw stdout log: `PASS: all Land Records V2 acceptance scenarios, lifecycle transitions, multi-document roles, attachment corrections, and negative-path checks verified.`

---

## 7. Office UAT Limitations & Scope Boundaries

While the automated regression, integration, persistence, and browser acceptance suites pass at 100%, the following operational conditions must be observed during human Office User Acceptance Testing:

1. **Authentication Mode:** Acceptance tests execute using Cookie/Form-based authentication (`/api/auth/login`) with the bootstrap administrator account. Enterprise Single Sign-On / SAML / OIDC environments require external IdP configuration.
2. **Document Storage Provider:** Acceptance tests run with local disk document storage (`Storage:DocumentRoot`). Cloud Object Storage (e.g. S3 / Azure Blob / GCS) requires testing with provider-specific bucket credentials.
3. **OnlyOffice & Local Document Intelligence:** Background OCR workers and OnlyOffice editors were disabled (`DocumentIntelligence:Enabled=false`, `OnlyOffice:Enabled=false`) in acceptance test configuration to isolate staff workflow business logic. Real OCR verification requires external worker processes.
4. **Isolated PostgreSQL Cluster:** Verification executed against loopback PostgreSQL 17 on port 55441. Production deployment requires applying EF Core migrations against the central production database.
