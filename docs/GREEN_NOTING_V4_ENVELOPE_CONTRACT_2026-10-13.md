# ContinuousGreenEnvelopeV2 implementation contract

This milestone is additive and experimental. OfficeNoteV1 remains the default.
No V4 functional acceptance or print acceptance is implied by implementation.

Open the existing authenticated office-session endpoint with UUID idempotency key
and working-note If-Match. Add `compositionVersion:"ContinuousGreenEnvelopeV2"`
to `{sourceDakId,resumeSnapshotId}`. Omitted/null/OfficeNoteV1 preserves the old
serialized command shape for existing idempotency replays. Unknown versions fail
422. Session responses add compositionVersion; existing fields retain their meaning.

Under the existing Dak -> Matter transaction locks, capture ordered official note
identities, canonical hashes/content, authors, designation, desk, submission time,
source Dak and citation/document metadata. The immutable lease stores this manifest,
its SHA256 and an HS256 seal bound to session/actor/Matter. Native DOCX metadata is
never the authority. A live-chain mismatch fails 409 before save/review/submission.

One mirrored Legal section contains one locked control per historical note, one
locked unsubmitted context label and exactly one editable current control, all tagged
with server-generated identities. Historical paragraphs include reserved signature
space. Native locks are convenience controls; server validation enforces immutability.
Returned DOCX must retain order/tags/locks, exact historical semantic text/marks and
paragraph geometry. Unknown body elements, nested/extra controls, fields, links,
attachments, relationships, layout, unsupported formatting and nonempty custom XML
fail 422. XML run splitting and empty native export metadata cannot change semantics.
Only current content is canonicalized, retaining UTF-16/repeated-text anchor rules.

Validation runs before callback bytes are accepted, again before frozen preview and
again at transactional official submission. Recovery validates the old captured
manifest and extracts current content, then composes it with the newly captured live
chain; no historical bytes are imported into the new note. Existing actor, custody,
RBAC, expiry, revision, immutable snapshot and idempotency checks remain in force.
No alternative official note chain or editor route is introduced.

V2 Save requires `{clientGeneration,nativeExportUrl}`; missing capture fails 428.
Quiesce input, call the supported `DocEditor.downloadAs("docx")`, receive its
`onDownloadAs` URL and await native synchronization before force-save. V2 configuration
permits DOCX export for its already authorized private editor; V1 download policy is
unchanged. Export permission is needed: the real disabled-permission experiment failed.
The server checks download origin/size, validates the full captured envelope under
lease/chain locks and retains its bytes/hash and semantic content fingerprint as
immutable request evidence. Status-6 can commit only if current canonical text AND
formatting match that capture. History is independently validated. Preview/submission
reverify capture integrity. A concurrent tab/change produces failure, not false Saved.
V1 Save omits the additive nullable URL and preserves the old idempotency fingerprint.

Save command acknowledgement, native synchronization and committed LAC receipt are
distinct. The real native immediate-export experiment reproduced force-save error 4
before the changes-sent event; no save success was inferred. The corrected bounded
native transport tests now pass as recorded below; final V4 UI acceptance remains
review-gated. Red guide alignment is an independent gate and must not hide separately
proven persistence results.

Official page margins remain 2551/850 twips, mirrored, with 1417 twips top/bottom and
12240x20160 twip portrait Legal paper. Conservative paragraph glyph clearance may
inset ink inside those margins; V2 adds 57 twips (1.0054 mm) on each paragraph's left
and right. The strict 45/15/25 mm verifier is never loosened.
Official PDF generation remains the existing canonical Green Sheet pipeline.

## Verified handoff after the real checkpoint

The bounded authenticated Docs 9.4.0.129 transport, officer workflow and print tests
now pass; see [the implementation checkpoint](GREEN_NOTING_V4_ENVELOPE_IMPLEMENTATION_2026-10-13.md).
Accurate native red guides remain blocked. This contract does not enable a V4 route.

The additive client methods are:

```ts
createOfficeSession(matterId, sourceDakId, resumeSnapshotId, workingRevision,
  "ContinuousGreenEnvelopeV2");
requestSave(sessionId, clientGeneration, sessionRevision, stableSaveUuid,
  synchronizedNativeExportUrl);
```

Existing calls omit the new optional arguments and retain OfficeNoteV1. Receive the
bridge from `OnlyOfficeNoteFrame.onNativeBridgeReady`. The parent must block input
before capture, await `quiesceAndExport()`, request save, then await the exact
Committed receipt before preview. Keep the parent blocked throughout Review; destroy
the editor for frozen review. Error/replacement closes the bridge and rejects capture.
The bridge result is an ephemeral native export URL, never a committed save receipt.
Do not cache it, signed config, note text or credentials in persistent browser storage.

| Situation | Actual API behavior |
| --- | --- |
| V2 save without capture / missing UUID header | 428; no save started |
| Unknown composition or unsupported envelope/layout/capture path | 422; no imported content |
| Stale revision, changed official chain, revoked/expired lease | 409; recover last committed content into a new lease |
| Missing permission or unreceived custody | 403 |
| Invalid native callback JWT | 401 with callback error; no snapshot |
| Valid callback with unsupported/mismatched content | Native callback contract returns `200 {error:1}`; no snapshot; Pending is projected as Failed/Timeout after deadline |
| Exact same save UUID and payload/revision | Same immutable committed snapshot; changed payload conflicts |

Native state events are synchronization signals; only the actual matching committed
receipt permits the label Server Saved. A second tab can edit while the first is
quiesced: the server compares the captured canonical text AND marks against returned
callback bytes. The real conflicting-tab test rejected the changed callback.

Do not blindly force-save a fresh UUID when native content has not changed: Docs
can return command error 4. Exact committed UUID replay was verified. Reusing the
verified receipt for unchanged content needs an explicit parent state policy and
concurrent-session checks; it is not implemented as a guessed successful new save.

The final workspace must load the actual authorized source `contentUrl` independently
of noting. Original uploaded Word documents use the existing document/draft workflow;
this milestone creates no `/editor?documentId=...` route. Historical native remarks
must retain note identity plus exact canonical UTF-16 range/version/hash. Native
selection-to-canonical offset mapping remains a separate unimplemented integration;
no first-occurrence quote matching or historical rewrite is allowed.

For owner review, the supported alternative to unverified native guides is an
accurate guide in the owned PDF.js read-only review viewport, outside printable PDF
content, while native editing has no guide. Alternatively obtain a vendor-supported,
version-pinned native page-geometry/event bridge and certify it over scroll/zoom/resize.
Neither alternative has been enabled without that decision.
