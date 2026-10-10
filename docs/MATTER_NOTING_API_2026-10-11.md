# Shared Matter workflow API — Anti contract v1

Baseline: `14c6f86ffdaa5566b45353cb4cf1637132b9624c`.
Feature branch: `codex/dak-matter-foundation-20261010`.
This contract records the approved functional requirements; it does not authorize deployment,
merging, changing acceptance environments, or messaging another agent. Contract publication
means committing and pushing this file on this feature branch. Implemented status is recorded
separately in the delivery report; a route below is not evidence it is already implemented.

## Architecture and compatibility

Reuse the single `Matter` (mandatory VillageId), `DakVillageLink`, `DakMatterLink`,
`MatterDocument`, provenance helper, existing Matter letter/Noting drafts and ONLYOFFICE.
WorkItem remains optional. New official noting is additive: personal working drafts,
immutable numbered submissions, remarks, citations, PDF annotations and command receipts.
Legacy `MatterDraft`/office documents remain working documents, never retrospectively numbered
or treated as official submitted notes. No automatic conversion or history rewrite.

All routes start `/api`, require the existing authenticated session and return camelCase JSON.
UUIDs identify resources. Every operation rechecks active user, scoped permissions, active
workstream and (for managed office accounts/helpers) current Village work allocations.
Designation is only a snapshot. No new role grants are inferred from designation.

## Concurrency, errors and rendering

Every new POST/PUT mutation requires UUID `Idempotency-Key`. Key is actor scoped; changing
resource, action or payload with a used key returns 409. Replays recheck read/operation access.
Use `If-Match: "<revision>"` for draft saves/submission (0 means no saved draft yet).
Other mutations carry `expectedRevision` in JSON. GET draft returns the quoted revision ETag.
400 invalid input/anchor, 401 no session, 403 unavailable/unauthorized resource, 404 missing
authorized child, 409 stale revision/state/key reuse, 428 missing revision/key header.
New errors have `{message}`. Flush autosave, await its ETag, then submit/send. Retry the same
key and payload after uncertain network outcome; never silently overwrite a conflict.

Rich text v1 is a constrained JSON document:
`{type:"doc",content:[{type:"paragraph",content:[{type:"text",text:"...",marks:[{type:"bold"}]}]}]}`.
Allowed blocks paragraph, heading, bulletList, orderedList, listItem, blockquote; inline text,
hardBreak; marks bold, italic, underline, strike. No HTML, scripts, arbitrary URLs or embedded
images. Canonical text is server-derived: inline text concatenated, hardBreak as LF, paragraph
and heading separated by LF (including final LF). Offsets are UTF-16 code units, end exclusive,
must not split surrogate pairs. Version 1 plus exact quote and uppercase SHA256 of UTF-8 quote
are mandatory for anchors. Frontend renders only this allowlist and uses canonical text mapping.

## Village classification and explicit creation/linking

- PUT `/dak/{dakId}/village-classification` body
  `{classification:"Unclassified"|"General"|"VillageSpecific"|"MultiVillage",villageIds:UUID[],expectedRevision:int}`.
  Requires Dak.Edit on the record; VillageSpecific exactly one active Village, MultiVillage at
  least two, General/Unclassified none. Existing Matter links are retained; incompatible
  reclassification is blocked. Historical links are never rewritten. Old Village-link APIs
  must not silently establish validated classification.
- GET `/dak/{dakId}/matter-options?villageId=UUID&search=text` returns
  `{subject,villageId,dakRevision,matters:[{id,title,revision}]}`. Requires confirmed holder,
  Dak.View and selected classified Village; filters each Matter by exact Village and live access.
- POST `/dak/{dakId}/matter-workspace` body
  `{villageId,workstreamId,existingMatterId?:UUID,title?:string,matterType?:string,expectedRevision:int}`
  returns `{matterId,dakId,dakRevision,matterRevision}`. Confirmed received holder only;
  Dak.View plus Matter.Create in target stream for create, or Matter.Edit for existing link.
  Title defaults to Dak subject. Atomic create/link and receipt; no automatic document copies.
- POST `/villages/{villageId}/matter-workspace` body `{workstreamId,title,matterType?}` returns
  `{matterId,dakId:null,dakRevision:null,matterRevision}`. Same create/Village scope checks;
  creator is the initial native working owner. No synthetic Dak.

All existing linking routes in either direction must validate confirmed holder and classified
Village compatibility. Existing legacy links remain readable but cannot bypass checks for new links.

## Noting and capabilities

- GET `/matters/{matterId}/noting?sourceDakId=UUID` returns
  `{matterId,revision,villageId,sourceDakId,capabilities:{canEditDraft,canSubmit,canRemark,canAnnotate,
  canSend,nativeRoutingPolicyPending},notes:Note[],documents:DocumentSummary[]}`.
  Omit source only for native Matter or read-only history. Never silently choose a controlling Dak.
- GET `/matters/{matterId}/noting/draft` returns actor's saved Draft or
  `{revision:0,contentJson:null,canonicalText:null,sourceDakId:null}` and ETag.
- PUT same route body `{contentJson:string,sourceDakId?:UUID}` returns Draft and ETag.
- POST `/matters/{matterId}/noting/submit` body `{sourceDakId?:UUID}` returns Note.
- POST `/matters/{matterId}/noting/send` body
  `{sourceDakId:UUID,expectedDakRevision:int,toDeskId:UUID,toUserId:UUID,
  action:"Forwarded"|"Returned",includesPhysicalOriginal:bool,instructions?:string,remarks?:string}`
  returns `{note:Note,dak:DakCommandResult}`. If-Match is saved draft revision.
  One authoritative transaction finalizes next Note N and routes ONLY the explicitly selected
  active linked Dak. Existing Dak recipient eligibility, formal receipt, paper confirmation,
  pullback/return/complete/reopen remain authoritative. Direct Dak receives are reflected live
  in Matter capabilities; no duplicate responsibility/custody mirror.

Draft: `{id,matterId,authorUserId,revision,contentJson,canonicalText,sourceDakId,updatedAt}`.
Note: `{id,matterId,number,version:1,contentJson,canonicalText,textHash,authorUserId,
authorName,designation,deskId,deskName,submittedAt,sourceDakId,citations:[],documentManifestJson}`.
Draft editing needs Matter.View, scoped Draft.Create and Draft.Edit plus active editor ownership.
Submission uses the same powers; sending additionally requires Dak.Move and confirmed custody.
Linked drafting always specifies the controlling active Dak; native drafting uses initial owner.
No submitted note update/delete route. Database and EF guards prohibit modifications/deletion.
Personal drafts are invisible to other officers; official chain is available to Matter.View.

Native Dak-less send/receive, novel approval authority and automatic completion are gated pending
product policy. `nativeRoutingPolicyPending=true`, `canSend=false`; backend rejects unsupported
transition instead of manufacturing custody. Existing explicit Dak complete/reopen remain intact.

## Remarks, citations, highlights and documents

Anchor: `{version:1,start:int,end:int,quote:string,quoteHash:string}`.
- GET `/matters/{m}/notes/{n}/remarks` returns Remark[].
- POST same route `{anchor:Anchor,text:string}` returns Remark.
- PUT `/matters/{m}/notes/{n}/remarks/{r}` body
  `{state:"Open"|"Addressed",expectedRevision:int}` returns Remark.
  Remark: `{id,matterId,noteId,anchorJson,text,state,revision,authorUserId,authorName,
  designation,createdAt,addressedByUserId?,addressedAt?}`. Matter.Edit plus exact Matter.View;
  state changes audited, never change official note.
- POST `/matters/{m}/noting/draft/citations` `{anchor:Anchor,documentId:UUID,page?:int,
  region?:{x,y,width,height},expectedRevision:int}` returns Draft with citations.
  Citations are finalized with Note; immutable anchor/provenance. Verify exact active
  MatterDocument and document access on create and again at click/stream; unavailable evidence
  stays historical without granting binary access.
- GET `/matters/{m}/documents/{d}/annotations` returns Annotation[].
- POST same route `{page:int,region:{x,y,width,height},quote?:string,text?:string}` returns Annotation.
  Annotation: `{id,matterId,documentId,documentVersion,documentHash,page,regionJson,quote,text,
  authorUserId,authorName,createdAt}`. Normalized top-left coordinates [0,1], positive dimensions
  contained in page, one-based page. PDF only; Matter.Document.Manage and current document access.
  Shared annotations are append-only; source binary never modified. Replacement version/hash
  must invalidate access to old annotations. No external previewer.

Reuse `/matters/{m}/documents` list/upload, `/documents/link`, existing eligible-document,
metadata, extraction, unlink, content and explicit export routes. The rail is the entire active
shared document set, not only citations. DocumentSummary includes id, filename, MIME, version,
hash and local content URL; never storage path. Streaming rechecks active join and permission;
archive/unlink/revocation blocks annotations and binary access. Word uses existing ONLYOFFICE
draft capability where supported, otherwise secure download fallback; no invented Word editor.
Existing validation governs upload size, extension and magic bytes. Virus scanning and content
dedupe availability must be reported explicitly; do not claim either if absent.

## Audit, migration and deferred work

Additive schema only, no backfill of official notes or inferred legacy classification/receipt.
Command receipts and workflow audit retain actor, designation/desk/time, source Dak, payload hash
and result. Each submitted note snapshots active document identities, versions and hashes.
No broad new supervisor or collaborator grants. Existing grants continue to govern document work;
delegated noting needs explicit future policy rather than designation inference.

Secure on-prem full-text indexing requires exact source ACLs, revocation/tombstones, scoped query
filtering and no unauthorized snippets; deferred until separately accepted. Existing metadata
search remains. Existing explicit document ZIP export is manual and audited; production eOffice
packaging/manifest approval is separately staged. No external eOffice connection or automatic send.

## Anti examples (use isolated server only)

```sh
curl -b cookies.txt "$BASE/api/dak/$DAK/matter-options?villageId=$VILLAGE"
curl -b cookies.txt -X PUT "$BASE/api/matters/$MATTER/noting/draft" \
  -H 'Content-Type: application/json' -H 'If-Match: "0"' -H "Idempotency-Key: $UUID" \
  --data '{"contentJson":"{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Submitted for review.\"}]}]}","sourceDakId":"<dak-uuid>"}'
curl -b cookies.txt -X POST "$BASE/api/matters/$MATTER/noting/submit" \
  -H 'Content-Type: application/json' -H 'If-Match: "1"' -H "Idempotency-Key: $NEW_UUID" \
  --data '{"sourceDakId":"<dak-uuid>"}'
```

Acceptance requires real PostgreSQL concurrency/rollback/immutability proofs, direct-API negative
authorization, draft recovery, exact span validation and document revocation, then independent
Anti integration and two-officer 1366×768 browser acceptance. Backend alone is not production-ready.
