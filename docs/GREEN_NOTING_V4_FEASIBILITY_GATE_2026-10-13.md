# V4 continuous Green Noting feasibility checkpoint

**Status: BLOCKED at the required inline integration gate. V4 is not an accepted
application and no V4 working route has been registered.** Safe fixes to the
preserved real V2 controller are implemented and tested. The V3 prototype remains
available unchanged; its simulated states are not acceptance evidence.

## Ownership and starting point

Dedicated branch: `codex/green-noting-eoffice-workspace-v4`.
Fresh managed worktree: `C:/Users/ashwa/.codex/worktrees/green-noting-eoffice-v4/LAC-Platform`.
Exact starting HEAD: `e2df85b8ff510120c4918bf3234dcabab6298fe8`.
Verified ancestors: real frontend `ea906a9e0e69e13beaa6f16b110fa67be6c6be68`
and backend `33fca9ebb27bb06cb061481710c89b0d7ed479b2`.

The owner supplied the government eOffice reference at
`C:/Users/ashwa/OneDrive/Pictures/Screenshots/Screenshot 2026-10-13 154538.png`.
It was inspected: green noting occupies the left panel and the original scanned
correspondence occupies the right panel, with compact controls. No government
identity or document from that image was imported into test data.

Antigravity's original worktree remains clean at the V3 HEAD above. The earlier
backend worktree remains clean at `33fca9ebb27bb06cb061481710c89b0d7ed479b2`.
Main, IIS, Land Records, RBAC implementation and operational accounts were not
modified. No merge, deployment, acceptance bundle replacement or protected-port
launch occurred.

## Real experiment and its limits

The experiment uses the existing Docs 9.4.0-129 container on loopback 8082 and a
temporary diagnostic host on `http://127.0.0.1:5938`. It is explicitly labelled
**TECHNICAL PROBE — NOT THE LAC APPLICATION / NO OFFICIAL SUBMISSION**.
It has no database connection, LAC login, Matter lease, canonical preview, official
note submission or official save receipt. Its signed native callback must not be
described as a committed LAC save receipt.

One synthetic Legal DOCX contains a historical block content control with
`sdtContentLocked` and a current block with `sdtLocked`. The signed Docs configuration
disables modifying content-control settings and printing/downloading. Historical
content, signature reservation and current content share one global mirrored
section. There is one native editor on the left. The right panel renders the actual
synthetic receipt PDF using PDF.js, rather than another noting file.

Native typing added `NATIVE-V4-CONTINUATION-20261013.`. The first native status-6
callback produced 40,313 bytes, SHA256
`e747c5e94d4d020e0f2aa251319dab68ae6b51ef87e974779242926df95c8bcc`.
Reopening used a new document key seeded from those bytes; the marker remained.
Native insertion of 22 continuation paragraphs flowed to page 2. The later signed,
request-correlated callback persisted 40,941 bytes, SHA256
`58f75f153b46a36a34e27ac8f3079c01e380ea06d56374cc526d89ac2f6047eb`.
The persisted document retains both control tags/locks and the original historical
text. A bounded attempt to type into the historical control showed disabled editing
controls and no historical text change. This does not replace server tamper checks.

The original source identity is `synthetic-receipt-manual.pdf`, SHA256
`22e003f244bfa3836f5f3636fdd2e3b46c5e091b595308b31b8f1213a5d6f7e5`.
This experiment reads that fixed synthetic fixture directly. Authorized Matter/Dak
attachment access is **not** proven by the diagnostic host. Source zoom and fit-width
were exercised independently of the native draft. Multiple-source switching,
scanned-source acceptance and denied document access remain unexecuted in V4.

Pale green was demonstrated by screen-only CSS compositing around the real iframe.
It does not change DOCX content. This is an experiment, not a certified application
theme. No decorative green background or red guide was added to the DOCX.

## Precise unresolved gates

1. **Supported, accurate red guides inside the native editor are unproven.** DocsAPI
   hosts an independent iframe/canvas with its own scrolling, zoom, rulers and page
   offsets. Host CSS cannot insert HTML history into that native pagination.
   Public page-count/current-page information does not establish all rendered page
   rectangles and scrolling/zoom notifications needed for an alternating 45 mm
   overlay. No reliable supported page-rectangle bridge was verified on installed
   9.4. A fixed host overlay, guessed toolbar offsets or undocumented editor globals
   would not meet the acceptance rule. No such guide is presented as correct.
   This is an integration-certification limit, not a claim that all ONLYOFFICE
   customizations or future vendor-supported plugins are impossible.
2. **The safe existing backend accepts private current notes only.** `OfficeNoteV1`
   rejects block content controls with 422. The added regression isolates that
   behavior without partially importing history. Feeding the experimental shared
   DOCX into that converter would be wrong. A separately versioned server envelope
   validator/current-note extractor is required before any shared DOCX can become
   official evidence. Native control locks alone are not authorization.
3. **The native probe fails exact printed ink clearance.** Actual Docs conversion
   yielded two 612 x 1008 pt Legal pages with mirrored 2551/850 twip margins and
   1417 twip top/bottom. Text coverage passes and both pages have zero non-neutral
   paint objects and zero images. However, page 1 left ink clearance is **44.958 mm**,
   below the 45 mm requirement even with the existing 0.02 mm integer-twip tolerance.
   The strict verifier returns failure. Page 2 passes. This must be corrected under
   a documented composition/font policy and reverified; the gate was not loosened.
   No verified official Green Sheet was published from this experiment.

The owner explicitly required stopping if the embedded editor cannot reliably meet
the inline contract. Accordingly, major shared-editor reconstruction and V4 official
submission are stopped at these gates. No textarea, random save hash, fake officer,
timer-confirmed save or simulated official note has been substituted.

Official sources inspected: [native inline editor hosting](https://github.com/ONLYOFFICE/api.onlyoffice.com/blob/master/site/docs/docs-api/get-started/how-it-works/inline-editors.md),
[content-control permissions](https://api.onlyoffice.com/docs/docs-api/usage-api/config/document/permissions),
[content-control locks](https://api.onlyoffice.com/docs/office-api/usage-api/document-api/Enumeration/SdtLock/),
[document page methods](https://api.onlyoffice.com/docs/office-api/usage-api/document-api/ApiDocument),
and [theme customization](https://api.onlyoffice.com/docs/docs-api/get-started/how-it-works/customizing-themes).
The limitation above is an inference from the verified installed behavior and
documented surface, not a vendor assertion of universal impossibility.

## Demonstrable alternative and exact next contract

The single protected DOCX experiment demonstrates a possible alternative to placing
HTML history above a separate private-note iframe. Native history and current content
can occupy the same physical page and continue to subsequent pages. It still needs
the gates above; it is not a second official noting system.

The next backend milestone must be an additive **ContinuousGreenEnvelopeV2** adapter,
with existing PrivateCurrentNote/OfficeNoteV1 behavior preserved by default:

- Persist server-owned composition version, immutable historical manifest and its
  hash on the lease. Capture ordered official note IDs, numbers, canonical hashes,
  authors/designations/timestamps and reserved signature structure under the existing
  Dak -> Matter locks. Do not infer mode or historical authority from uploaded XML.
- Compose one Legal section with locked historical controls and exactly one current
  control. Label the latter unsubmitted; do not fabricate its official number/time.
  Preserve current-note privacy, login SessionVersion, actual custody and lease checks.
- Before committing a callback, validate the signed private key/actor/request/revision
  as today, then validate the complete envelope against that captured server history
  and the live official chain. Reject changed/missing/reordered/unlocked historical
  controls, altered metadata/signature/layout, extra text outside the current control,
  unexpected relationships, fields, nested controls and unsupported structures.
  Compare semantic content/formatting under a strict whitelist; XML byte equality
  alone is insufficient because native export normalizes nonsemantic XML.
- Extract **only** the current control into canonical note JSON/text/hash. Preserve
  exact UTF-16 offsets, repeated text and marks. Never import the historical prefix
  as new current-note text or overwrite an existing official note.
  Any native selected-text remark integration must identify the exact historical
  control and canonical UTF-16 range; finding the first matching quote is forbidden.
  Until that mapping is supported and verified, retain the existing exact canonical
  anchor workflow and existing remark records rather than inventing native offsets.
- Use the existing correlated Pending -> Committed barrier, immutable snapshots,
  frozen canonical preview, deliberate officer confirmation, and transactional
  submit/send with existing immutable evidence and idempotency receipts. Validate
  the envelope again before submission. A newer official chain invalidates the
  captured lease; recover current committed content into a fresh authorized envelope.
- Keep official printing in the existing verified `GreenSheetService` composition
  pipeline. Independently solve glyph/ink clearance without modifying Legal margins
  or weakening checks. Certification must cover the supported scripts/fonts/marks.
- Implement or obtain a version-pinned, supported native page-rectangle/event bridge
  and verify red-guide alignment over odd/even pages, zoom, scroll, resize, focus modes
  and device-pixel ratios. Guides must stay outside printable document content.

**No new request field or endpoint for that adapter is implemented in this checkpoint.**
The following existing frontend contract remains authoritative:

| Operation | Existing supported response / route |
| --- | --- |
| Read notes/capabilities/source documents | `GET /api/matters/{m}/noting?sourceDakId={d}` |
| Read original authorized attachment | Use each document's returned `contentUrl`, currently `/api/matters/{m}/documents/{documentId}/content` |
| Open private current-note lease | `POST /api/matters/{m}/noting/office/sessions`, UUID key, working `If-Match`, `{sourceDakId,resumeSnapshotId}` |
| Real native editor | Use authenticated `/api/noting-office/sessions/{s}/config` `scriptUrl` and signed `config`; no invented `/editor` route |
| Save / poll / preview | Existing `/api/noting-office/sessions/{s}/saves`, `/saves/{id}`, `/previews` |
| Confirm | Existing `/api/matters/{m}/noting/office/confirm` |
| Submit | `/api/matters/{m}/noting/submit` returns the actual official note, including **`number`** |
| Submit and dispatch | `/api/matters/{m}/noting/send` returns **`{note:{number,...},dak:...}`** |
| Actual clean official PDF | Existing green-sheet generation/metadata/authorized PDF routes |

The eventual workspace should keep one native noting surface on the left and an
independent authorized PDF.js document viewer on the right. V2 controller/API types,
real DocsAPI host, exact anchor utilities and existing official-chain renderer are
reusable. V3's pale-green visual direction is reusable; its fabricated data/save/
review/submit state is not. Divider, focus modes and the V4 route remain unimplemented.

## Implemented safe corrections

- Submit and Send notices now read actual server `number` fields and reject malformed
  responses rather than displaying `#undefined`.
- Save serialization now spans callback completion, validates receipt identity and
  snapshot/hash/version evidence, and keeps Pending/Failed separate from Committed.
- Review uses the receipt returned by its awaited save and the current session
  revision, avoiding stale pre-await React state. Newer dirty edits remain unsaved;
  autosave queues another save after the pending operation completes.
- Manual failed-save retries retain the original UUID/payload/revision. No automatic
  retry silently changes the revision of an existing idempotency command.
- Review makes the editor host inert; frozen review destroys the native editor.
  New leases reset previous receipt/generation state. Aborted native event handlers
  cannot dirty a replacement editor.
- Declared and locked the missing `pdfjs-dist@6.4.299` dependency already imported by
  the real V2 viewer. The real frontend now builds from the fresh checkout.
- Removed embedded test-password defaults from inherited acceptance scripts.
  They require privately supplied environment values before execution. Original
  checkpoints/history were preserved, not rewritten.

## Results and evidence

| Check | Result | Scope |
| --- | --- | --- |
| Fresh exact-base branch / original worktree preservation | PASS | Read-only status and ancestry checks |
| Actual frontend production build | PASS | TypeScript and Vite build from the fresh checkout |
| Focused frontend tests | PASS: 33 unique tests | 12 new production-helper tests; 20 noting tests rerun after final changes |
| Backend focused suite | PASS: 22 tests | 6 real PostgreSQL security tests; Docs transport controlled by test fixtures |
| New expired-lease security test | PASS: 1 test | Real PostgreSQL, advanced test clock; denied config/save/status/preview/callback/download, preserved bytes and new-lease recovery |
| Real native editor typing / same-page continuation / two-page flow | PASS | Diagnostic Docs experiment, not authenticated LAC officer workflow |
| Actual signed native callback / saved-byte reopen | PASS | Exact bytes/hashes above; not a Matter save receipt |
| Source PDF independent readability / zoom / fit width | PASS | Actual fixed synthetic PDF; no LAC document authorization in probe |
| Printed size / text / absence of red and green paint | PASS | Actual Docs conversion; both page images inspected |
| Strict printed ink clearance | FAIL | Page 1 left = 44.958 mm |
| Accurate alternating native red guides | BLOCKED | No supported, tested page-rectangle integration established |
| V4 authenticated A -> Send/Receive B -> official Note 2 / immutable chain / clean official PDF | BLOCKED | Shared envelope adapter and V4 route not enabled |
| Six required official workflow screenshots A–F | BLOCKED | Those states were not reached; no substitute images claimed |

The .NET build retained three pre-existing xUnit analyzer warnings. Vite reported
the large application chunk warning. Neither build was reported as warning-free.

Two actual diagnostic screenshots, each captured as the complete 1366 x 768 viewport
rectangle and verified as JPEG 1366 x 768 without image resizing or retouching:

- [Native locked history, current continuation and original PDF](receipts/green-noting-v4/inline-probe/native-green-history-source-1366x768.jpg)
- [Native even-page continuation and independent original PDF](receipts/green-noting-v4/inline-probe/native-green-even-page-source-1366x768.jpg)

Both show a synthetic native document, not officially submitted Matter notes. They
prove the real native editor/source composition, not LAC authentication, custody,
canonical review or official persistence. The exact source identity is stated above.
Sanitized [callback](receipts/green-noting-v4/inline-probe/native-probe-evidence.json),
[native conversion](receipts/green-noting-v4/inline-probe/native-conversion-evidence.json)
and [failed physical gate](receipts/green-noting-v4/inline-probe/native-print-geometry.json)
receipts are retained separately.

## Environment and preservation

The installed per-user Docker Desktop initially failed at its stale Windows Unix
socket rename. Its failed launcher/backend PIDs were checked against their executable
paths and stopped after no daemon was available. A hidden Win32 process launch of
the same installed executable restored the normal WMI launch context. No sockets,
volumes or distributions were deleted, no factory reset/reinstall/prune/reboot occurred.
Existing container `lac-platform-onlyoffice-onlyoffice-1` was automatically reused.
Healthcheck is HTTP 200 `true`; actual Docs API loaded. Binding remains
`127.0.0.1:8082:80`. Existing JWT configuration was read privately from
`C:/LAC-Platform-onlyoffice/.env.onlyoffice`; its secrets were never published.

The prior synthetic PostgreSQL cluster on `127.0.0.1:56683` completed normal crash
recovery and accepts connections. Tests used only
`lac_onlyoffice_real_9b0459_20261012`, with newly generated isolated schemas cloned
from the private synthetic baseline. Original schemas, documents and accounts remain
preserved. Test schemas are retained for audits; no database/schema was dropped.
No services were launched on 5190/5191/5610/5611 or 5920/5921/5922. The diagnostic
host at 5938 has no LAC/database connection. The latest observed C: headroom was
approximately 3.6 GiB; no unrelated space recovery or pagefile change was performed.

The bundled LibreOffice renderer was unavailable (`soffice.exe` absent). Actual
ONLYOFFICE conversion plus the existing 600 dpi ink verifier and inspection of both
page PNGs supplied the physical evidence instead. The private probe files, binaries,
logs, baseline SQL and any signed configuration remain outside publication.

Persistent LAC browser-draft caching was not enabled. Unsaved offline typing is not
promised to survive a restart. This branch is a reviewable checkpoint, not an
approved V4 pilot. Owner visual and functional approval is required before any merge.

## Changed files

- `docs/GREEN_NOTING_V4_FEASIBILITY_GATE_2026-10-13.md`
- `docs/receipts/green-noting-v4/inline-probe/native-conversion-evidence.json`
- `docs/receipts/green-noting-v4/inline-probe/native-green-even-page-source-1366x768.jpg`
- `docs/receipts/green-noting-v4/inline-probe/native-green-history-source-1366x768.jpg`
- `docs/receipts/green-noting-v4/inline-probe/native-print-geometry.json`
- `docs/receipts/green-noting-v4/inline-probe/native-probe-evidence.json`
- `src/LAC.Web/package-lock.json`
- `src/LAC.Web/package.json`
- `src/LAC.Web/src/matter/GreenNotingWorkspace.tsx`
- `src/LAC.Web/src/matter/OnlyOfficeNoteFrame.tsx`
- `src/LAC.Web/src/matter/SendReturnModal.tsx`
- `src/LAC.Web/src/matter/notingOfficeApi.ts`
- `src/LAC.Web/src/matter/officeSaveBarrier.ts`
- `src/LAC.Web/tests/office-save-barrier.test.mjs`
- `src/LAC.Web/tests/test-full-e2e-acceptance.mjs`
- `src/LAC.Web/tests/test-negative-paths.mjs`
- `tests/LAC.Tests/OfficeNoteDocxTests.cs`
- `tests/LAC.Tests/OfficialOfficeNotingTests.cs`

Executed checks: `npm run build`; the four focused Node test files for save barriers, green noting, Matter contracts and Village groups; and `.NET` filters `OfficeNoteDocxTests|OfficialOfficeNotingTests` followed by the new `Expired_lease_rejects` test. Final expiry verification additionally proves that the latest signed snapshot downloads successfully before the clock advances, then is denied after expiry. Private TRX logs remain under the task artifact directory. No full V4 browser acceptance suite was executed.
