# V4 final workspace integration checkpoint — owner review required

Verified base: `5bbd843dc4e5de9fc0d38bf8451b6072d8b31a97`.
Only branch: `codex/green-noting-eoffice-workspace-v4`.

| Reviewable implementation commit | SHA |
| --- | --- |
| No-change policy, capture-backed seed review, package safety, PostgreSQL tests and API contract | `6e9e5895f4f9449ed7b363a039db61b1eb95b824` |
| Keep native capture storage evidence out of preview responses | `ea7c13f636cda7e7039d2d670d7c2fe03287d3c5` |
| Real React workspace, original PDF, V2 bridge, custody and frontend contract | `a833cdb9ee94b4bad1d79e44821b359e848b9f2d` |

The accompanying evidence/documentation commit does not replace these implementation
SHAs. No main merge, production deployment, IIS change or shared wwwroot replacement.
ContinuousGreenEnvelopeV2 remains one official ledger and one native working note.

## P0 recovery and bounded validation

The only deleted directory was the explicitly approved completed installer
extraction `C:\Users\ashwa\AppData\Local\Temp\yoyguln3`.
Verified exact resolved path and every ancestor, zero reparse descendants, zero
file locks and zero installer/dependent processes. VS installation was complete.
Deleted 457 regenerable installer packages, logical size 1,520,138,136 bytes.
Observed C: free-space increase: **1,521,225,728 bytes (1.417 GiB)**.

Free C: rose from 2,221,555,712 to 3,742,781,440 bytes. After sequential builds,
retained test schemas and evidence it was 3,467,628,544 bytes (3.23 GiB); D:
244,867,072 bytes (0.228 GiB), E: 74,915,840 bytes (0.070 GiB). These are time-stamped
observations, not guaranteed future capacity. Recovery of 15–20 GiB was not possible
from this narrowly approved safe candidate.

No other cache/folder was deleted. No uv prune or Colab interruption occurred:
no-active-work could not be confirmed. No pagefile settings, WSL, Docker volume,
database, worktree, checkpoint or user document was deleted/reset. Windows reported
C:\pagefile.sys allocated 12,071 MiB; current usage 933 MiB, peak 3,446 MiB.

Headroom guards stopped final compilation when commit capacity fell too low. After
closing only temporary agent browser tabs, full TypeScript and Vite compilation
passed sequentially with a 512 MiB Node heap cap. No dependency install/restore or
parallel heavy workload. The new geometry render was bounded to two short notes
(299 canonical characters) on one page. Previous dense multi-page evidence remains
preserved rather than being rerendered under these constraints.

## Real environment and launch rejection

| Component | Verified isolated origin |
| --- | --- |
| Actual LAC API | `http://127.0.0.1:5940` |
| Actual Vite React application | `http://127.0.0.1:5941` / browser alias `http://lac-v4-isolated.localhost:5941` |
| Existing task callback/file relay | `http://127.0.0.1:5942` |
| Existing ONLYOFFICE Document Server | `http://localhost:8082`, bound `127.0.0.1:8082` |
| Existing synthetic PostgreSQL | `127.0.0.1:56683` |

API/fixture database: `lac_onlyoffice_real_9b0459_20261012`, schema
`lac_office_test_93b2807ee5504e3f948e60bc8ca94634`. Startup bootstrap and background
workers are disabled. Migration `20261013195914_AddNativeNoChangeReview` was applied
only to this named synthetic schema. Existing cloned test identities were reused.
No new accounts, database, schema or LAC host were created for browser acceptance.
Focused PostgreSQL regression tests created separate retained audit test schemas.

Automatic approval review rejected the combined API/frontend start with only
**blocked by policy**. The rejected command did not execute. No alternate launcher
was executed by the agent to bypass it. The user directly ran the syntax-validated
`C:\Users\ashwa\.codex\artifacts\green-noting-v4-envelope-20261013\START-V4-WORKSPACE.cmd`
and replied **running**. It reused the existing relay and launched only API/frontend.

Exact frontend process command:
`C:\Program Files\nodejs\node.exe node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5941 --strictPort`.
Working directory:
`C:\Users\ashwa\.codex\worktrees\green-noting-eoffice-v4\LAC-Platform\src\LAC.Web`.
Child `VITE_API_TARGET=http://127.0.0.1:5940`. Both HTTP endpoints were verified;
anonymous `/api/auth/me` returns 401. ONLYOFFICE healthcheck returns 200/true,
Docs renderer is 9.4.0.129 and Docker daemon reports 29.8.0.
JWT configuration remains at `C:\LAC-Platform-onlyoffice\.env.onlyoffice` privately;
no secret was printed or published.

## Authenticated actual workflow

New synthetic Matter: `845f2f84-0c53-456d-b8b1-d70d7c0c5415`.
Original PDF document: `b9796d9b-ef87-403d-b3d8-f89c59c1d7bf`.
Its actual authorized bytes match SHA-256
`22e003f244bfa3836f5f3636fdd2e3b46c5e091b595308b31b8f1213a5d6f7e5`.
The private/public receipt records the controlling Dak and real transfer IDs.

1. Existing `office.feasibility.editor` (Kanungo) authenticated through actual React
   login and ASP.NET cookie authentication. An actual native V2 editor opened.
2. Native typing triggered a real autosave/callback commit, then a second real save
   committed the completed paragraph. Committed ZIP bytes, hashes, versions and
   request correlation are retained. Native capture/callback correspondence checks
   exact canonical text AND marks; ZIP metadata need not be byte-identical.
3. Repeat Save showed **No new changes to save** with no fourth/fabricated commit.
   Reopen created a real version-0 recovered seed with no committed receipt.
   Its no-change Save and fresh captured Review worked without command error 4.
4. Repeated Review returned the same frozen preview without another save/preview.
   A confirmed canonical preview submitted **Note 1 and Forwarded the Dak atomically**.
5. Existing `office.feasibility.viewer` (Tehsildar) authenticated through the real UI.
   Before Receive, source PDF viewing worked and editor creation returned 403.
   Genuine Receive updated custody. The native editor continued below protected
   Note 1 after its reserved signature/marking space, with the original PDF alongside.
6. B's native callback committed SHA-256
   `588918f54d397ab131f882c49c71bad73d491bcdb0bd136a7f94b102543a3d53`.
   Canonical Review included B's paragraph only, excluding historical A text/context.
7. A second real tab replaced B's frozen/reviewed lease. The first tab's stale
   Submit was rejected; the official count remained one. The new tab recovered B's
   committed text, captured a new seed preview and submitted actual **Note 2**.
8. After submission, opening the next native working note produced empty current
   content beneath two official notes. A no-change seed Save created no new commit.
   This guards against automatically duplicating a submitted note into the next draft.

| Official record | ID | Canonical SHA-256 |
| --- | --- | --- |
| Note 1 | `c1fc5a5a-5ec3-431a-9bf0-6251dee96fdc` | `1593650601FC256C9C32879C51CA7DAB92F44EE3CBDB188BAF6807F02A068369` |
| Note 2 | `99045101-72ad-4b47-9b9b-1d3df7e88fe5` | `0F03FA4400A544C0809F33A1DFD59E77106CD7C77482D436CF1D99A0412C4B2B` |

Exactly three native callback save requests committed; no-change/reopen/Review
created no extra committed save. Previous two-note checkpoint Matter
`2a7affc3-4885-446d-8d1a-ffeea85d8ed2` retains its original IDs/hashes.

Real negative HTTP checks: anonymous source 401; B editor before receipt 403;
cross-actor session config 404; administrator desks 403; unsigned/invalid-JWT
callbacks 401. B's authorized original source bytes were verified separately.
Ordinary officers were not granted administrator access to make Send work.

## Physical and visual evidence

Published actual official PDF: `ebf769cb-2d16-4e30-8679-e7df59325960`, one page,
SHA-256 `1ce6c16c25549f0af048c9d8ff784361680e61f1796f6331a542c184876264f8`.
Both officers' official notes appear on the same physical page below their reserved
marking/signature spaces. Raw clean PDF has zero non-neutral paint objects and
zero images. Text coverage and 600 dpi visible-ink verification passed.

| New PDF geometry | Measured |
| --- | --- |
| Legal MediaBox | 612 x 1008 pt = 215.9 x 355.6 mm |
| Left / top ink clearance | 46.0163 / 25.9927 mm |
| Right / bottom ink clearance | 21.844 / 217.678 mm |

These are **ink clearance**, not changed margin definitions. Source DOCX retains
45/15 mm mirrored margins and 25 mm top/bottom with integer-twip quantization and
the existing 57-twip inset. No bottom intrusion appears in this short-note sample.
The dense boundary and automatic multi-page flow proof from the base remains
preserved; this short sample does not recertify all scripts/fonts/dense layouts.
Hardcopy printer/tray/scaling acceptance has not been performed.

Option A is implemented: native editing without an unproven native red guide,
plus actual read-only Legal PDF screen guides/background. The original source is
visible beside canonical review. Divider, focus modes, source zoom and page
controls work. On the preserved authorized two-page PDF, measured guide offsets:

| Page / zoom | Canvas width | Guide coordinate |
| --- | --- | --- |
| Odd / 75% | 459 px | 95.6667 px from left |
| Even / 75% | 459 px | 95.6771 px from right |
| Even / 100% | 612 px | 127.5625 px from right |
| Even / 50% | 306 px | 63.7813 px from right |

These match the PDF viewport's 45 mm boundary within CSS pixel quantization.
Scrolling left to 550 px left the original source scroll at 0. Red guides are DOM
overlays; decorative green is a screen background. Neither enters DOCX/PDF bytes.
Option B, native geometry integration, is **unproven and not implemented**.

17 actual uncropped browser screenshots were checked as exactly 1366 x 768.
One intermediate fit-page capture reset to page 1 during HMR; it is retained
privately and explicitly excluded from even-page acceptance. Correct page-2/zoom
measurements and replacement evidence have distinct filenames.

## Verification and remaining boundaries

- Focused backend safety suite: 67 passed, zero skipped.
- Final package/privacy units: 40 passed. The first associated PostgreSQL rerun
  used a wrong private baseline path and failed before fixture work; corrected rerun
  passed. Its original failure receipt was retained.
- Both retained real native A/B fixtures passed tightened package validation.
- Focused frontend tests: 43 passed; full final TypeScript and Vite build passed.
  Existing large-bundle warning and pre-existing xUnit analyzer warnings remain.
- Publication scan: no actual JWT secret, protected officer password or JWT
  capability matches in selected receipts, DOCX ZIP contents and final bundles.
  Artifact SHA-256 manifest matches actual published files.

This backend review was conducted independently of UI wiring by the implementing
agent, not by an external security assessor. The application can quiesce local
input, not atomically stop every native editor tab. Real lease replacement/stale
submission was tested; global native edit-after-capture CAS is not claimed.
Failed-callback/no-change, expiry, chain/identity/permission changes and rollback
have PostgreSQL coverage; deliberate real transport-loss/failed callback injection
and a broad browser matrix were not repeated. Native selected-text remark mapping
is still a separate bridge boundary. Current template can expose native SDT aliases
while editing; no diagnostic test controls remain in the officer route.

Application persistent browser draft caching stays disabled. No unsaved offline
restart survival or new purge of protected browser origins is claimed.

## Evidence and owner handoff

[Frontend contract](GREEN_NOTING_V4_FRONTEND_INTEGRATION_CONTRACT_2026-10-14.md),
[no-change contract](GREEN_NOTING_V4_SAVE_CHECK_CONTRACT_2026-10-14.md),
[backend safety audit](GREEN_NOTING_V4_BACKEND_SAFETY_AUDIT_2026-10-14.md).
Raw synthetic DOCX/PDF, native request receipts, numbered notes, permissions,
geometry, guide measurements, screenshots and hashes are in
`docs/receipts/green-noting-v4/workspace-20261014`.

Open the running real workspace:
`http://lac-v4-isolated.localhost:5941/matters/845f2f84-0c53-456d-b8b1-d70d7c0c5415/noting-v4?greenSheetId=ebf769cb-2d16-4e30-8679-e7df59325960`.
The existing test credentials remain in the private feasibility manifest; they are
not reproduced in this repository. Use **Open Green Noting** to continue a server
draft, **Save**, **Canonical Review**, confirm text and **Submit** or **Submit & Send**.
For clean print, open the actual PDF and choose Legal portrait, actual size 100%.

Owner must approve the actual V4 UI and Option A, or request changes/defer until a
native guide is proven. STOP for owner review. API/frontend/Document Server and the
synthetic database remain running; no further reconstruction, merge or deployment.
