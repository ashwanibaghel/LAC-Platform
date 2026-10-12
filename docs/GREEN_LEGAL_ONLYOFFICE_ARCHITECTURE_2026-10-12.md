# Green Legal ONLYOFFICE backend architecture checkpoint

Status: approved implementation scope, published before adapter implementation.
Implementation/test results and exact enabled routes will be recorded separately.
This checkpoint must not be read as a passing official Green Noting acceptance result.

One official chain remains MatterWorkingNote -> MatterOfficialNote through the existing
numbered atomic submit/send transaction. ONLYOFFICE authors the CURRENT private note only.
Prior official text is never supplied in its editable DOCX. A separate server-owned
continuous physical sheet composes immutable official notes plus an explicitly selected,
authorized committed canonical preview/working note. Anti owns the workspace UX.

## Additive state and immutable boundaries

* MatterOfficeSession: actor, Matter, controlling Dak, current working revision, login
  session version, expiry, private document key, state/revision, latest committed binary.
  One active/frozen lease per actor/Matter; replacements revoke older keys.
* MatterOfficeSaveRequest: actor-scoped idempotency UUID, session revision, client edit
  generation, Pending/Committed/Failed state, deadline, immutable snapshot ID on commit.
* MatterOfficeSnapshot: immutable original DOCX bytes/storage identity, SHA256, file size,
  version, actor/session/request/time. Stored bytes are never overwritten or removed by
  later callbacks. No mutable legacy MatterDraft pointer is official evidence.
* MatterOfficePreview: immutable conversion result tied to exact snapshot/hash, converter
  version, canonical JSON/text/hash, source and working revision. Confirmation requires
  the officer to send its exact hash and saved working revision, then seals the lease.
* MatterOfficeNoteEvidence: immutable link from the EXISTING numbered official note to
  that source snapshot/converter/review, inserted in the same existing note transaction.
* MatterGreenSheetSource / MatterGreenSheet: immutable input manifest/composed DOCX and
  authoritative finished PDF/hash. Pin physical policy, font and actual Docs version.
  Rendering failures never produce a purported finished printable artifact.

Use additive PostgreSQL migrations, foreign keys, uniqueness and immutable database/EF
guards. Lock order remains controlling Dak -> Matter -> session. Network calls are outside
row-lock transactions; reauthorize and recheck state/revision after downloading/rendering.
Storage writes preceding a failed transaction may leave private recoverable orphan bytes;
they must never overwrite or delete committed snapshots.

## Versioned physical and conversion policy

GreenLegalV2: portrait Legal 215.9x355.6 mm; inside/outside 45/15 mm; top/bottom 25 mm;
gutter 0, mirror margins and one global page parity. Keep DelhiLacNotingV1 and historical
Letter/Noting DOCX untouched. The composer owns numbering, author/designation/date and
an unsplit handwritten signature/marking reservation. Officer changes do not insert page
breaks. Text may flow naturally over several pages. Screen guide metadata is separate:
odd x=45 mm, even x=170.9 mm; red/green decoration never enters DOCX/PDF bytes.

Strict OfficeNoteV1 converter initially supports paragraphs, explicit plain text, tabs,
line breaks and explicit bold/italic/underline/strike. Normal style and approved fixed
font/layout metadata are verified. Reject tables, images, hyperlinks, fields, tracked
changes, footnotes/endnotes, headers/footers, lists, unsupported styles/alignment, manual
page/section breaks or unknown semantic XML. Return clear 422 unsupported-content errors;
do not flatten or silently discard them. New structures need versioned verified support.
Canonical output uses the existing NotingText rules: exact UTF-16 offsets, final paragraph
LF, no trim/Unicode normalization. Unchanged text preserves citations byte-for-byte and
revalidates every anchor. Changed text with citations requires explicit re-anchoring before
confirmation; never silently shift an anchor. Existing submitted remarks/history remain
tied to their immutable notes and canonical versions.

## HTTP contract for Anti (planned / capability-gated)

All paths start /api. Existing cookie auth and live Draft.Create/Edit, Matter.View,
allocation, open Matter and confirmed explicit Dak custody apply. No designation grants.
Mutation headers: UUID Idempotency-Key plus quoted nonnegative If-Match where noted.
Private responses are no-store. Standard 401/403/404/409/428; unsupported DOCX 422;
service/force-save failure 503. Request acceptance is NOT a durable save receipt.

| Route | Request / result |
| --- | --- |
| POST /matters/{m}/noting/office/sessions | If-Match working revision; {sourceDakId?}; returns actor-bound session |
| GET /noting-office/sessions/{s}/config | Signed actual Docs config/scriptUrl; lease expiry and current revision |
| GET /noting-office/sessions/{s}/file?token=... | Docs-only signed download; revalidate actor/lease/custody live |
| POST /noting-office/sessions/{s}/callback | Docs JWT; signed payload only; private key + actor + live authority + generation checks |
| POST /noting-office/sessions/{s}/saves | If-Match session revision; {clientGeneration}; durable request before Docs forcesave command with userdata=request UUID |
| GET /noting-office/sessions/{s}/saves/{r} | Pending/Failed/Committed; committed snapshot/hash/version/session revision/clientGeneration |
| POST /noting-office/sessions/{s}/previews | If-Match session revision; {saveRequestId}; strict conversion of that correlated committed snapshot, freeze editor lease |
| POST /matters/{m}/noting/office/confirm | If-Match working revision; {previewId,canonicalHash}; officer-reviewed canonical draft through existing command transaction |
| Existing /matters/{m}/noting/submit or /send | Existing body plus officePreviewId/canonicalHash; same authoritative atomic transaction, numbering, citation validation and rollback |
| POST /matters/{m}/noting/green-sheets | {previewId?}; immutable server-owned source manifest, then actual Docs PDF conversion |
| GET /noting-green-sheets/{id} | Authorized metadata including clean PDF URL and separate guide policy |
| GET /noting-green-sheets/{id}/pdf | Live Matter access; working-inclusive sheets remain private to their actor |
| GET /noting-green-sources/{id}/file?token=... | Short-lived actor/source-bound Docs conversion input; live authorization |

Callbacks cannot overwrite official notes. Late/stale/revoked callbacks return an error
and retain all prior bytes. Only a matching pending force-save's userdata may satisfy a
save barrier. Native toolbar/close saves may preserve a private snapshot but never satisfy
a different request/generation. Command error/no-change/timeout cannot manufacture a
receipt. Freeze rejects subsequent editor writes; submission requires the exact reviewed
snapshot and still-current working revision. Retries keep the same UUID/payload; changed
payload conflicts. Existing JSON save/citation edits must not bypass Office confirmation.

The physical renderer uses the actual pinned local ONLYOFFICE version, approved Liberation
Serif 12 pt policy and a single DOCX section. Persist output bytes/hash and manifest, not
an expiring Docs URL. Verify generated PDF MediaBoxes/content before publication; use
the existing 600-DPI ink/color acceptance gate for physical proofs. Font boxes are not ink.
No browser getText/iframe scraping is a canonical source. No invented generic Word route.

## Safety, publication and proof

The unshared credential-bearing 9d581966 ancestor is preserved locally on the old branch,
excluded from the sanitized new branch rooted at remote 9b045900. Scan known test secrets,
JWT patterns, staged content and all proposed new commits before explicit-ref publication.
Private manifest, seed fixture files and signed-request logs are excluded. No force push,
main mutation or credential rotation. Colab reconnect remains separately blocked.

Correct the DHC bridge and court ingestion worker guards through ApiStartupPolicy first;
verify both service registration and host behavior before further database integration.
Use only the existing 56683 synthetic database and Docs service; preserve running 5920,
5921 and protected 5190/5191/5610/5611/IIS. Test fixtures may use isolated schemas within
that existing synthetic database. A temporary callback test host or coordinated isolated
API update must respect the user's selected arrangement; no new operational environment.

Required proof: A half-page submit -> receive -> B same-page continuation and overflow;
immutable note/snapshot database guards; callback revocation/staleness/failures and retries;
exact citation/repeated-text/surrogate anchors; atomic send rollback and idempotency;
unchanged Letters/V1; actual Docs/browser force-save correlation and clean Legal PDF.
Distinguish handler mocks from actual Docs/browser evidence. Anti integration and physical
printer handling remain separate acceptance gates even after backend service tests pass.
