# Functional repair and ONLYOFFICE integration contract for Anti

Status: implementation contract and source audit; rejected pilot is not accepted or deployable.
Audited frontend/pilot: `deb0656baa8537c06a30d924c73db1b601fbbb7f`.
Isolated backend branch: `codex/dak-matter-foundation-20261010`, starting at
`41f892ec32f5024e72854de59e6f44d560882836`. Backend source is identical between
these two revisions. This contract supplements `MATTER_NOTING_API_2026-10-11.md`.
The current user request governs scope; the earlier attached specifications are
functional references, not permission to reconstruct editors, deploy, merge,
change existing databases, or modify the rejected pilot.

## 1. Village and Directory records

Actual routes:

* `GET /api/matters/?villageId={villageId}&page=1&pageSize=200&status=all`
  returns `{items,totalCount,page,pageSize}`. Follow all pages for set comparison.
* `GET /api/villages/{villageId}/matters` returns an array, without pagination.

Both select active Matter records using `MatterAuthorizationService.AuthorizeListQueryAsync`
with `Matter.View`. The Village route adds the Village predicate before authorization;
Directory adds it afterwards. Neither filters by Matter type. Compare IDs, VillageId,
MatterType, workstream and revision, not response envelopes or optional Directory counts.
Disable Directory search/status/workstream filters when comparing the complete sets.
No `General` conversion, permission expansion, data migration, or backend type enum is needed.
`MatterType` is an open string, not a closed UI enum. Unauthenticated requests are 401;
missing Matter.View is 403; a scoped user receives only authorized records. A nonexistent
Village returns 404 on the Village route; the Directory filter returns an empty page.
Officer read authority intentionally remains broader than action allocations; do not
infer draft/send permission from Directory visibility. Existing assistant narrowing gates apply.

Verified frontend cause at rejected SHA: `App.tsx`, `VillageMatters`, lines 961–974
builds groups for every type, but lines 997 onward iterate only
`Court Case, Compensation, Land Acquisition, Demarcation, Possession, Other`.
`General` and any custom type are never rendered. Adding just `General` is insufficient.
The current view also treats API errors as an empty list.

Required minimal functional fix (no visual changes): use a `Map<string, Matter[]>`,
group every returned item by `matterType?.trim() || "Other"`, and iterate the actual
groups. Known types may have preferred ordering, but append every unknown key.
Use Map rather than `{}` so names such as `__proto__` and `constructor` remain safe.
Show the existing ErrorState on `matters.error` before the empty state. A pure tested
grouping helper and the App wiring will be supplied on the isolated branch; Anti must
apply these changes to its frontend. Preserve every item exactly once and its workspace
link `/matters/{matterId}`. Creation choices and their labels do not restrict list display.

## 2. Official note selection: precise frontend repair

Verified cause at rejected SHA: `NotingWorkspaceView.tsx:562–568` casts the DOM
`range.startContainer` (usually a Text node) to HTMLElement. The loop does not ascend
when `getAttribute` is absent, then calls that absent method. Observed exception:
`TypeError: a.getAttribute is not a function`. Fixing this cast alone is insufficient:
the `trim()` and `indexOf()` fallbacks can anchor a different occurrence of repeated text;
cross-paragraph browser selection text does not reliably match canonical LF delimiters.
The header Add Remark currently manufactures a first-word anchor. It must instead
request/use an explicit selection and must not imply that it anchors arbitrary feedback.

Anti must implement an exact endpoint-to-canonical mapping:

1. Capture one noncollapsed DOM Range contained wholly within one official note body.
   Reject selections crossing notes, metadata, remark popovers, citation buttons or tools.
2. For each endpoint use the node if `nodeType === Node.ELEMENT_NODE`, otherwise its
   `parentElement`; ascend with `closest` to a canonical paragraph/text-run wrapper.
   Text-node offsets count UTF-16 units. Element-node offsets count child boundaries,
   so sum only preceding canonical text descendants. Never treat an element child
   index as a character count.
3. Render wrappers from the exact server `canonicalText` with stable canonical ranges.
   Formatting/highlight wrappers may split runs but contribute zero additional text.
   Exclude citation/remark UI labels. Blank-paragraph NBSP placeholders contribute zero
   canonical units; each paragraph delimiter contributes exactly one LF, including the
   final LF. Do not normalize or trim the server text after assigning ranges.
4. Map BOTH Range endpoints; a backward mouse/keyboard selection still produces the
   ordered Range. Require `0 <= start < end <= canonicalText.length`. Allow one-character,
   whitespace-containing and multi-paragraph spans. Reject endpoints inside surrogate pairs.
5. Derive `quote = canonicalText.slice(start,end)`, not `selection.toString().trim()`.
   Show this exact quote in the confirmation form. No substring search fallback; if DOM
   mapping fails, ask the officer to select again. Repeated occurrences remain distinct
   because their offsets differ. Preserve offsets across focus/split mode and new notes.
6. Hash the quote with UTF-8 SHA-256, uppercase hex; send the immutable note version.
   Catch async hashing failures and disable the action with an error; never send a guessed anchor.

No backend correction is required for the reported exception or for exact/repeated spans.
The server cannot determine which repeated DOM occurrence the officer selected; it can
only validate the supplied canonical range. Do not weaken validation to compensate for UI errors.

## 3. Canonical text and exact remarks HTTP contract

`NotingText.Canonicalize` accepts a JSON STRING in `contentJson` with root `doc`.
Supported child nodes: paragraph, heading, bulletList, orderedList, listItem, blockquote,
text, hardBreak. Text marks: bold, italic, underline, strike. Only heading attrs `{level:1..6}`
are supported. Marks do not change offsets. Text runs concatenate; hardBreak emits LF;
each paragraph/heading appends LF; list/blockquote containers add no characters or bullets.
No Unicode normalization, whitespace collapsing, byte offsets or ProseMirror positions.
No CR or other controls except LF/tab. Maximum input JSON length is 250000 UTF-16 units;
maximum JSON depth is 24. Tables, images, arbitrary fonts/layout attrs and DOCX are not
official-note JSON. Do not silently strip unsupported office content for submission.

Use the server returned `canonicalText` and immutable `version` (currently 1). Anchors
are `[start,end)` zero-based .NET/JavaScript UTF-16 units. Emoji use two units; combining
characters remain separate units. The server rejects split surrogate pairs but does not
enforce grapheme clusters; UI may snap to grapheme boundaries before forming the range.
`textHash` hashes the entire canonical note; `quoteHash` hashes only the selected quote.

Exact create example for canonical text `Repeat\nRepeat 😀\n`, selecting the SECOND Repeat:

```http
POST /api/matters/{matterId}/notes/{noteId}/remarks
Content-Type: application/json
Idempotency-Key: 91e48229-6c45-43e8-b53b-17cac9c0c4ce

{"anchor":{"version":1,"start":7,"end":13,"quote":"Repeat","quoteHash":"B6B7A0065808A62E7D5781B356C5DDBA4E000433C34B5D561D200158A4443EB1"},"text":"Please clarify this second occurrence."}
```

Returns 200 with `{id,matterId,noteId,anchorJson,text,state,revision,authorUserId,
authorName,designation,createdAt,addressedByUserId,addressedAt}`. `anchorJson` is a
JSON STRING containing the five anchor fields. Initial state Open, revision 0; addressed
fields null. IDs, author/name/designation and times are assigned server-side. The note
must belong to the Matter in the URL. Read with
`GET /api/matters/{matterId}/notes/{noteId}/remarks` (array in creation order).

```http
PUT /api/matters/{matterId}/notes/{noteId}/remarks/{remarkId}
Content-Type: application/json
Idempotency-Key: 5db9c826-03e5-4ea6-a7ae-659f542b46fc

{"state":"Addressed","expectedRevision":0}
```

Returns 200 with incremented revision and addressed actor/time. Reopen with
`{"state":"Open","expectedRevision":1}` and a new key. Reopening clears the current
addressed fields but does not delete past immutable command receipt snapshots/audit events.
Remark text, anchor, authorship and creation time cannot be updated/deleted through APIs;
PostgreSQL also guards these fields. No reply-thread or public remark-history endpoint
currently exists; do not invent one. Existing immutable MatterWorkflowReceipts retain
each successful result; MatterEvents/AuditLogs retain actor/action/time/context. A future
authorized history reader may expose these snapshots without rewriting remarks.

Error behavior (noting workflow errors use `{message:string}`):

| Status | Condition / handling |
| --- | --- |
| 400 | Invalid range, empty span, wrong quote/hash/version, split surrogate: `Anchor does not match the immutable canonical text/version/hash.` |
| 400 | Blank/over-4000-character remark: `Remark text is required, up to 4000 characters.`; invalid state: `Invalid remark state.` |
| 400 | Malformed JSON/types or missing JsonRequired expectedRevision: framework binding error; do not assume `{message}` for binding failures. |
| 401 | No valid session. |
| 403 | Matter.View / Matter.Edit missing, scope/allocation revoked, inactive workstream, or assistant ceiling; body may be empty from global filters. |
| 404 | Authorized Matter but note or remark is absent or belongs to a different Matter/note. |
| 409 | Stale remark revision (`Remark changed; refresh.`), concurrency conflict, or same idempotency key with different payload. |
| 428 | Missing/non-UUID/empty Idempotency-Key (`A UUID Idempotency-Key is required.`). |

Remark requests do not use If-Match. Same actor+key+same command replays the original
result after live authorization; changed payload conflicts. Retain the SAME key and body
after an uncertain network result. Allocate a new key only for a new action. UI must
parse problem-details, `{message}`, binding errors and empty 401/403 responses.
Overlapping and repeated-text remarks are valid separate immutable anchors.

## 4. One shared Workspace, one official chain

Keep `/matters/{matterId}` as the canonical shared workspace reached from Dak, Village
and Directory. The official continuous Green Note Sheet reads
`GET /api/matters/{matterId}/noting?sourceDakId={explicitControllingDakId}`. Its `notes`
are ordered numbered MatterOfficialNotes, read-only; numbering belongs to the server.
Its documents are the full authorized explicit shared Matter document set, not just citations.
Remarks are loaded per official note through the route above. Use returned capabilities;
designation never grants permission. Native owner drafts may omit sourceDakId; native
movement remains policy-gated (`nativeRoutingPolicyPending`). Never create a synthetic Dak.

MatterWorkingNote is the private authorized officer's unsent draft. MatterOfficialNote is
the immutable official submitted record. Legacy MatterDraft Letter/Noting + OfficeDocument
is a formatted document draft, not another official numbering system. In the UI, new
official Add Note always goes to MatterWorkingNote; legacy Noting documents must be
labelled legacy/supporting drafts and must not be presented as another official Green Sheet.
Preserve them unchanged, including historical DOCX/JSON, without bulk migration or deletion.
Letter drafting remains a separate workspace panel with existing draft IDs and policy.

## 5. Official autosave, conflicts and transactional Send

```http
GET /api/matters/{matterId}/noting/draft
```

Returns only the caller's draft, ETag `"revision"`, Cache-Control no-store. Absent draft:
`{revision:0,contentJson:null,canonicalText:null,sourceDakId:null}`. Saving:

```http
PUT /api/matters/{matterId}/noting/draft
Content-Type: application/json
If-Match: "0"
Idempotency-Key: 0d92bd84-37ad-4bf2-9d6a-0eb65df36370

{"contentJson":"{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Please review.\"}]}]}","sourceDakId":"{controllingDakGuid}"}
```

Returns full persisted working note and ETag with incremented revision. Current draft,
permission, active allocation, Matter open state and confirmed Dak holder are rechecked.
Use real GUIDs in place of placeholders. Empty/invalid If-Match is 428; stale revision
or source is 409. No automatic overwrite/rebase. Serialize saves, track edit generation,
show Pending/Saving/Server saved/Offline/Error/Conflict; Saved means the latest generation
matches a successful durable server response. Debounce, preserve in-memory text on failure,
GET the current server revision for an explicit conflict decision, retry uncertain commands
with the same key/body. Keep persistent browser draft caching disabled and legacy purge.
Only last successful server save is recovered after refresh/restart; unsaved offline typing
has no restart guarantee.

Before submit/send: stop edits or capture a stable generation, flush, verify server revision
and source, and block if changes remain. Submit without movement is
`POST /api/matters/{matterId}/noting/submit` with If-Match, Idempotency-Key and
`{"sourceDakId":"{guid}"}` (or null for authorized native owner). It consumes the draft.
Atomic submit AND movement is ONLY:

```http
POST /api/matters/{matterId}/noting/send
Content-Type: application/json
If-Match: "1"
Idempotency-Key: 1ad0c9e2-ef6b-4a12-ae7a-c42d5ec434bf

{"sourceDakId":"{guid}","expectedDakRevision":4,"toDeskId":"{deskGuid}","toUserId":"{userGuid}","action":"Forwarded","includesPhysicalOriginal":false,"instructions":"Please review","remarks":null}
```

Action is exactly Forwarded or Returned. Returns `{note,dak}` after the transaction
commits. Locks Dak then Matter, revalidates authority/receipt/source/citations, assigns
next number, captures author/desk/time and document manifest, consumes draft and moves
only the explicit controlling Dak. Failed movement rolls back the note too. Do not call
submit followed by independent Dak send. Preserve formal receipt and paper handoff rules.
Retries with the same key do not create another note. Refresh workspace/custody after send;
do not invent a successor note number or transfer physical originals automatically.

## 6. Supported Word editor/viewer paths

The registered editor route is `/matter-drafts/{MatterDraft.Id}`, not a DocumentId.
There is NO `/editor?documentId=...` route and NO generic uploaded-Word editor endpoint.
`DocumentRailViewer.tsx:751` is therefore broken. Replacing it with
`/matter-drafts/{Document.Id}` is also wrong.

Supported workflow now:

* Existing formatted draft: `GET /api/matters/{matterId}/drafts` -> select its draft ID ->
  `/matter-drafts/{draftId}` -> `GET /api/matter-drafts/{draftId}`. When officeEnabled is
  true, OnlyOfficeDraftEditor fetches `/api/matter-drafts/{draftId}/office-config` and uses
  the returned signed config/scriptUrl. No handcrafted tokens or document URLs.
* New letter: `POST /api/matters/{matterId}/drafts` with
  `{"title":"Reply letter","draftType":"Letter"}` -> returned draft ID -> same route.
* Uploaded Word original: authorized download through
  `/api/matters/{matterId}/documents/{documentId}/content?expectedVersion={version}`.
  Show supported download/open-local fallback. Inline Word editing/viewing is unavailable
  for arbitrary uploads in the present API; omit the invalid editor button. If a record is
  an OfficeDocument of an already known accessible MatterDraft, open that confirmed draft
  ID; do not guess an association or create/overwrite a draft on click.
* Word import/edit-copy would need a separately reviewed provenance-preserving API that
  creates a NEW editable MatterDraft copy and retains source hash/version and permissions.
  Such API is not implemented/authorized by this audit. Never edit the uploaded source.

Office-config requires Draft.View, sets edit/view by live Draft.Edit, returns 503 problem
when disabled and 409 on opening concurrency. The host falls back to MatterDraftEditor
only when officeEnabled=false AND officeDocumentId=null. An existing OfficeDocument
cannot fall back to stale JSON: legacy PUT correctly returns 409. Disabled existing
Office drafts must show unavailable, without fake save/edit actions.

## 7. ONLYOFFICE save architecture and safety limits

Inspected OnlyOfficeDraftEditor.tsx, MatterDraftEditor.tsx, OnlyOfficeDraftService.cs,
OnlyOfficeEndpoints/Tokens/Options, MatterDraftDocx and official NotingWorkflow.
Current architecture:

* Configuration signs DOCX key `lac-draft-{draftId:N}-g{generation}`; download tokens
  bind purpose/draft/document and expire after 10 minutes. File responses no-store/nosniff.
* Anonymous Document Server callbacks require a verified HS256 JWT; only signed payload
  fields are used. Active Matter/draft/document and current key are checked. Save URL
  must match configured scheme/host/port, no userinfo/fragment; redirects are disabled.
* Status 2 final save / 6 force save stage bytes, enforce bounds, validate DOCX, hash/store,
  atomically update document pointer/version and draft revision. Final save rotates key;
  duplicate final callback replays; identical force-save bytes do not revise again.
* Revision is an EF concurrency token; per-process gates serialize callbacks, but are not
  a distributed editing lease. Failures retain the last committed binary. Old mutable
  draft bytes are deleted after replacement; ONLYOFFICE changesurl/history is not retained.
* Crucially: callbacks do NOT update ContentJson/CanonicalText, create MatterWorkingNote,
  or submit MatterOfficialNote. Legacy JSON remains stale after Office editing.

The host currently only observes ready/error events. `autosave:true` config does not prove
that LAC PostgreSQL/storage has committed the current editor changes. ONLYOFFICE's
[callback contract](https://api.onlyoffice.com/docs/docs-api/usage-api/callback-handler)
distinguishes final save from force save. Its
[forcesave command](https://api.onlyoffice.com/docs/docs-api/additional-api/command-service/forcesave)
requests saving, and the callback completes persistence. A browser
[onDocumentStateChange event](https://api.onlyoffice.com/docs/docs-api/usage-api/config/events)
is not a LAC transaction receipt. Do not label Office state as Server saved based only
on readiness or document-server state.

Integration gates before claiming reliable Letter autosave or reusing Office for official notes:

1. A backend save-status/force-save barrier correlated to session key + requested generation,
   with durable callback document version/hash/revision, timeout/error/retry state. No such
   API exists now; Anti must not invent or simulate it.
2. Explicit editor lease/session policy and live actor permission/allocation revalidation,
   plus closed/submitted state protection. Current callback authenticates Document Server
   but does not recheck original editor authority or Draft status, and download tokens
   remain valid for their short lifetime despite user permission revocation. Current JWT
   config has no expiry; Verify accepts callbacks without exp. Restrict server origins and
   retain current token validation; session revocation/freshness must be designed before
   stronger official editing claims. Do not weaken expiry or callbacks to make UI work.
3. Durable immutable revision snapshots when freezing a DOCX for approval/official export;
   do not reference a mutable OfficeDocument pointer as official submitted evidence.
4. Test actual local Document Server connectivity (browser->Docs; Docs->LAC file/callback;
   LAC->Docs save origin), JWT, revocation, stale callbacks, parallel instances and failures.
   HTTP handler tests prove service behavior, not an installed Document Server end-to-end.
   This audit does not install/start/reconfigure Document Server or any acceptance environment.

## 8. Safe canonical note production: decision now, future adapter conditions

Use the existing structured official note editor + server autosave + immutable official
chain for Green Noting now. Use existing ONLYOFFICE for formatted Letters and supporting
legacy document drafts in the same Matter workspace. This is one official noting system;
it is not two competing chains. Optional manual transfer of text from a Word draft requires
officer review in the official editor and a normal server save; no automatic claiming of
DOCX formatting, citations, revisions or signature fidelity.

ONLYOFFICE cannot safely submit official notes merely by posting stale ContentJson,
reading iframe DOM, client getText, or flattening DOCX InnerText. Rich DOCX has tables,
fields, tracked changes, headers, footnotes, content controls, breaks and formatting outside
the official JSON schema. Current MatterDraftDocx.Create is one-way lossy materialization
(flattens legacy tables/lists and does not carry all text marks), not a round-trip converter.

A future backend-owned adapter, if approved, must: obtain a correlated committed save;
freeze hash/version/session/custody; convert to the allowed JSON deterministically with
explicit rejection/reporting of unsupported constructs and tracked-change policy; show
the canonical preview for officer confirmation; map citations/anchors to exact canonical
units; revalidate everything under the existing Dak->Matter locks; finalize through the
SAME official numbering/immutability/idempotency transaction. Post-submit late callbacks
must affect only a separate new draft or be rejected, never rewrite an official note.
Retain immutable source DOCX snapshot + converter version/provenance. No adapter routes,
schema or major reconstruction are introduced by this audit.

## 9. Physical layout policy

Approved Green Noting profile is Legal 215.9 x 355.6 mm, portrait, mirrored margins:
inside 45 mm, outside 20 mm, top 25 mm, bottom 20 mm, gutter 0. It already exists in
`MatterDraftOfficeProfiles.DelhiLacNotingV1` and DOCX enforcement on every Noting callback.
DOCX: width 12240 twips, height 20160; margins inside 2551, outside 1134, top 1417,
bottom 1134 twips, `w:mirrorMargins`. Enforce all sections, preserve content/headers/footers.
Odd/even pages swap physical left/right: odd 45/20, even 20/45, with global page parity
across the continuous official sheet. UI zoom does not change millimetres or offsets.

Legacy frontend `editor/pageProfiles.ts` and MatterDraftEditor still say A4/25 mm inside;
the legacy Noting save payload substitutes A4, while backend requires Legal/45. Anti must
consume the approved profile for labels/layout/print/save and remove provisional conflicting
claims. Official chain print/export must derive from immutable notes, not mutable legacy
drafts. Browser print mirror support must be verified or an authoritative print renderer
provided before claiming physical-layout acceptance. Layout cannot alter canonical text.
Letter policy remains separate: A4/Legal, Portrait/Landscape, validated 0–50 mm margins;
no automatic Noting mirror rule or Green Sheet numbering on Letters.

## 10. Regression and delivery requirements

Targeted regression work will cover:

* Real PostgreSQL/in-process HTTP: Directory paginated IDs equal Village IDs for General,
  custom/new types, active/archived, other villages, scoped workstream membership and revoked
  Matter.View. No pilot account or existing database writes.
* Functional grouping helper: every record once, known preferred order + unknown groups,
  null/blank fallback and prototype-like type names. App displays API failure, not false empty.
* Exact remarks HTTP: repeated occurrence, multi-paragraph LF, whitespace, emoji/surrogates,
  wrong quote/hash/version/range, same-key replay/payload conflict, scoped denial, state
  revision conflict, immutable note/text/anchor and retained transition receipt snapshots.
* Existing ONLYOFFICE suite: callback signing/origin/key, stale/duplicate callbacks, failure
  retention, DOCX authority, fixed Legal mirror profile and separate Letter formatting.
  Add proof that Office callback does not submit official notes or sync legacy JSON.

Final validation and exact commit SHAs are recorded in a separate audit report after tests.
Required Anti follow-up: dynamic grouping/error-state fix, exact DOM mapping without search
fallbacks, remove invalid upload editor link, distinguish legacy drafts from official notes,
align legacy physical profile, and implement honest editor/save/conflict/unavailable states.
No visual redesign, new pilot, merge or deployment is part of this delivery.
