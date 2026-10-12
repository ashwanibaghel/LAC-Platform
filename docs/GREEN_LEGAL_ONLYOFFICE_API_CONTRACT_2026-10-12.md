# Implemented Green Legal ONLYOFFICE contract for Anti

This is the backend contract. Anti owns the shared Matter Workspace. There is ONE
official chain: existing MatterWorkingNote -> MatterOfficialNote. ONLYOFFICE edits
the current actor's PRIVATE note. It never receives previous submitted notes in an
editable DOCX. Display the immutable continuous physical PDF alongside that editor.
The acceptance harness is a temporary test client, not the delivered workspace UX.

## Feature and policies

Both OnlyOffice:Enabled and OnlyOfficeNoting:Enabled must be true. The new feature
defaults off. Legacy /matter-drafts/{id}, Letters and DelhiLacNotingV1 retain their
existing behavior. Uploaded Word documents do not acquire an /editor route: use
the existing authorized Matter document download/view workflow, or deliberately
create an authorized Matter draft via the supported draft API. Do not treat an
arbitrary upload as an official working note.

GreenLegalV2 is portrait 215.9 x 355.6 mm, globally mirrored 45/15 mm inside/outside,
top/bottom 25 mm, zero gutter. One section and continuous page parity for the whole
Matter. Each submitted note has server-owned number, author/designation/time and
an unsplit handwritten signature/designation/date/marking reservation. Officer
changes do not insert page breaks. A working-inclusive sheet is visibly unsubmitted.

OfficeNoteV1 supports paragraphs, exact text, tabs, wrapping line breaks and explicit
bold/italic/single underline/strike. No trimming, Unicode normalization or fuzzy
quote matching. Reject tables, pictures, fields, hyperlinks/external relationships,
tracked changes, real footnotes/endnotes, comments, custom XML, headers/footers,
lists, unsupported styles/alignment/fonts, manual page/section breaks and unknown
semantic XML with 422. Docs 9.4 reserved separator-only note parts, empty paragraph
borders, no-highlight metadata and cached rendered page-break hints are verified
nonsemantic exceptions. Normal style IDs may differ; its resolved name/properties
are checked. Font policy is LiberationSerif12-v1; nonapproved inherited marks/font
metadata fail closed. Spacing is composed by the physical policy, not imported as
arbitrary Word layout. Canonical headings/lists/quotes from historical notes retain
visible headings/list markers/quote indentation under this fixed composer policy.

The PDF gate verifies Legal MediaBox, global odd/even rasterized INK clearance
(600 dpi, 0.02 mm integer-twip tolerance), neutral paint/no image decorations, and
every composed printed character in order, allowing only pagination whitespace
differences. Missing glyphs/text prevent publication. Pin Docs version, AllFonts.js
SHA256 and verifier script SHA256; preserve an immutable renderer image and record
actual font binary hashes in provisioning evidence. A mutable/malicious Docs server
is outside the trust boundary; the font map hash is not a remote attestation of every
TTF byte. Broader script/font support needs its own actual print certification.

## Authentication and common behavior

All paths below start /api and use existing cookie login. No designation grants.
Mutations require one UUID Idempotency-Key. Where specified send If-Match: "N".
Same actor/key/payload replays a receipt; changed payload conflicts. Authorization
is checked before replay, and after locks for new mutations. Live Matter scope,
allocation, active workstream, open Matter, Draft.Create/Edit and confirmed explicit
linked Dak custody apply to editing. Native Matters require their actual native owner
and no active controlling Dak. Actor login SessionVersion and lease expiry are checked.

Locks follow existing Dak -> Matter order, with actor/idempotency advisory locks.
HTTP/Docs downloads run outside row locks, followed by locked live revalidation.
Responses use no-store. Typical errors: 401 login/JWT, 403 live authority, 404 private
entity unavailable, 409 stale/revoked/conflict/barrier/citations, 428 missing mutation
headers, 422 unsupported DOCX or failed physical gate, 503 disabled/unavailable
renderer. Invalid JSON/required fields are 400. Revision fields in response bodies
are authoritative; do not infer them from an editing-state event.

## Authoring, save barrier and review

1. GET /matters/{m}/noting/draft returns own canonical draft and ETag. Revision 0
   means no saved working draft. Drafts are private, with persistent browser caching
   disabled. Unsaved offline typing is not promised to survive restart.
2. POST /matters/{m}/noting/office/sessions, If-Match WORKING revision:
   `{ "sourceDakId": "<linked-dak>", "resumeSnapshotId": null }`.
   Returns `{id,matterId,actorUserId,sourceDakId,workingRevision,revision,state,
   expiresAt,latestSnapshotId}`. Replaces/revokes older live leases. Default recovery
   resumes the prior private session's last committed snapshot in the same context;
   an explicit snapshot must belong to this actor/Matter/Dak. New private key, immutable
   version-0 seed; replacement never overwrites the old bytes. Lease lasts configured
   1..60 minutes (default 30); expiry requires a new session, not in-place extension.
3. GET /noting-office/sessions/{s}/config returns `{session,scriptUrl,config}` for
   the actual DocsAPI.DocEditor. Use returned script and signed configuration; do not
   invent editor URLs. Config is current-note edit only, autosave on, native force-save
   button off, print/download/review/comments off. Prior notes remain in the physical
   viewer. Native autosave means Docs collaboration state, NOT a committed LAC receipt.
4. POST /noting-office/sessions/{s}/saves, If-Match SESSION revision:
   `{ "clientGeneration": 7 }`. This required nonnegative counter must not move
   backwards. Record the exact UUID/payload/revision for a retry. Server creates a
   durable Pending request before sending signed forcesave with userdata=save ID.
   A command acknowledgment is not saved content.
5. GET /noting-office/sessions/{s}/saves/{saveId} returns
   `{id,sessionId,clientGeneration,sessionRevision,state,deadline,snapshotId,sha256,
   version,failureCode}`. sessionRevision is CURRENT lease revision, not the original
   requested revision. Only Committed with exact snapshot/hash/version is saved.
   Display "Saving…", "Saved to server: version/hash" or "Save failed: last committed
   bytes retained". Command failures return HTTP 200 with state Failed, e.g.
   DocumentServerCommandRejected/Unavailable; expired Pending reads Failed/Timeout.
   Never show Saved on the basis of onDocumentStateChange or a toolbar status.
6. Quiesce editor input before the final save/review transition. Client generations
   alone do not certify that no more keystrokes occurred. Do not allow typing between
   the final barrier and freeze. POST /noting-office/sessions/{s}/previews,
   If-Match CURRENT SESSION revision: `{ "saveRequestId": "<committed-save>" }`.
   Exact current correlated snapshot required; creates immutable canonical preview
   `{id,matterId,sessionId,actorUserId,snapshotId,sourceDakId,workingRevision,
   converterVersion,contentJson,canonicalText,canonicalHash,createdAt}` and freezes
   the lease. Destroy the editor; every subsequent write callback is rejected.
7. Show exact canonical preview for officer review. POST
   /matters/{m}/noting/office/confirm, If-Match WORKING revision:
   `{ "previewId": "<preview>", "canonicalHash": "<exact-uppercase-hash>",
      "resetCitations": false }`. Returns updated existing working note. Unchanged
   text retains citations verbatim and validates all anchors. Changed text with
   citations returns 409: officer must deliberately choose resetCitations:true and
   re-anchor citations, never silently shift ranges. Citation edits in Reviewed
   state increment both working revision and lease revision coherently.
8. Existing POST /matters/{m}/noting/submit, If-Match latest WORKING revision:
   `{ "sourceDakId": "<dak>", "officePreviewId": "<preview>",
      "canonicalHash": "<reviewed-hash>" }`.
   Existing /noting/send adds the same officePreviewId/canonicalHash to its existing
   Dak destination/action/revision body. It atomically inserts the same numbered
   MatterOfficialNote, immutable Office provenance, citations and audit, and performs
   the existing Dak dispatch transaction. Failed dispatch rolls EVERYTHING back.
   The note's canonical data must exactly equal its reviewed preview; actual source
   DOCX size/hash is rechecked. Lease becomes Submitted and working pointers clear.

## Failure, retry and recovery

Only matching status-6 callback userdata, private key, exact actor, live authority,
Pending request/deadline/session revision can commit a barrier. Status 2 can retain
final-close bytes but never satisfy a pending force-save request; a changed final
snapshot retires that key. Late writes after Frozen/Reviewed/Submitted/Revoked fail.
Duplicate already-committed correlated callbacks never overwrite bytes. Signed
status 7 marks its matching Pending request failed. Callback reads signed payload
only (including payload wrapper), ignoring conflicting unsigned outer fields.
Missing/invalid JWT is HTTP 401/error1; rejected valid callbacks return error1/HTTP200.
Signed downloads check purpose, session/actor/snapshot/expiry and live permissions.
Downloads only accept configured Docs origin, without redirects/userinfo/fragments.
Callback JSON is capped at 128 KiB; JWT at 64 KiB; DOCX at 5 MiB with ZIP expansion
and XML bounds. Callback exceptions log IDs/types, never note text or signed tokens.

Retry a Failed save with the ORIGINAL UUID, body and original If-Match: it reuses
the durable request only if no newer save superseded its session revision. A new
request needs the current revision. No-change command errors cannot manufacture
a committed barrier. Open a fresh lease from the last committed snapshot to recover;
edit and request a real new save. Preview/conversion failures preserve last bytes.
POST /noting-office/sessions/{s}/revoke, If-Match SESSION revision, UUID key, retires
the lease and restores structured canonical editing; it does not delete snapshots.
Persisted Office-controlled drafts reject ordinary JSON autosave until explicit revoke.

## Continuous physical file and screen guides

POST /matters/{m}/noting/green-sheets with UUID key:
`{ "previewId": null }` for official chain only; or an actor-owned current frozen/
reviewed preview ID to include visibly UNsubmitted committed working text. Captures
immutable source/manifest under the relevant locks, uses actual Docs conversion,
verifies physical PDF/text, rechecks authority/version/fonts, publishes immutable PDF.
Failures retain source and publish no sheet. Retry same UUID reuses that source.
Limits: 500 notes / 1,000,000 canonical characters, 20 MiB PDF, bounded verification;
over-limit/failed output is explicit, never a partial file.

GET /noting-green-sheets/{id} returns id, Matter, ManifestJson, PdfSha256, PageCount,
RendererVersion, FontManifestSha256, CreatedAt, pdfUrl, actualSizePrintingRequired,
and screenOnlyGuides. GET .../{id}/pdf streams exact hash-checked authorized PDF.
Official-only sheets require current Matter read/allocation; working-inclusive sheets
remain private to their actor with private draft read permission. Immutable historical
output remains as-of its manifest; request a new sheet after later submitted notes.

Each guide: `{page,widthMm:215.9,heightMm:355.6,xMm:45|170.9,color:"red",print:false}`.
Use the actual PDF page viewport for overlays: odd x=45/215.9 * pageWidth, even
x=170.9/215.9 * pageWidth. Account for viewer scale/padding; do not restart parity
per note. Green page background and red guides are SCREEN DOM only. Exclude overlays
with print CSS and make the print action open/download the clean authoritative PDF.
Print Legal portrait at actual size/100%, without fit-to-page or browser decorations.
Backend validates PDF geometry; it cannot control a user's physical printer driver.

Internal signed Docs-only routes: /noting-office/sessions/{s}/file?token=... and
/noting-green-sources/{id}/file?token=.... Do not expose tokens in logs, analytics,
screenshots, issue descriptions, browser draft storage or Git.

## Immutable storage and rollout boundaries

Additive migrations create leases, durable requests, snapshots, previews, official
note evidence and Green Sheet source/output. EF and PostgreSQL prohibit historical
updates/deletes; PostgreSQL also rejects truncation, cross-parent evidence, bad hashes,
receipt identity edits, retired lease resurrection and silent expiry/key changes.
Storage writes are append-only GUID files. A failed SQL transaction can leave private
orphan bytes; it never overwrites/deletes prior snapshots. Garbage collection is a
separate approved retention task. Never downgrade a populated evidence schema.

Tests distinguish mocked Docs HTTP, PostgreSQL invariants/handler authentication
fixtures, and genuine Docs/browser acceptance. Enable this contract in Anti's isolated
workspace after reviewing the acceptance report. No production/main/IIS rollout,
physical-printer certification or full user-facing workspace completion is implied.
