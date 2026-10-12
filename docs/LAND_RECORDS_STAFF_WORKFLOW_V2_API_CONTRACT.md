# Land Records staff workflow V2 — audited API contract

Audit base: `37db57e5fa40cd353e70c3161abe62e23eba4cc5` (fetched from origin/main on 2026-10-12). Branch: `codex/land-records-staff-workflow-v2`. Backend integration contract for Anti; no deployment or main merge is authorized.

## Architecture and audit findings

Canonical identities remain Award, AwardVillage, Notification, AwardNotification, Document, DocumentAward, DocumentNotification and PossessionEvent. DocumentAward already permits many PDFs per role; its existing unique (DocumentId, AwardId) constraint is retained. PP evidence gains a nullable PossessionEvent FK on this relationship. Attachment metadata belongs to this relationship, never to a globally reused Document. NM remains in NmDocument and its existing review/extraction graph. AuditLog retains before/after values and actor identity.

The old core upload stored arbitrary bytes and created duplicate documents on retry. The extraction upload already stored without analysis but could reuse a hash attached to a different Award. The existing Notification model supports arbitrary sections, while the LR registration helper allowed only 4/6. No canonical Village legal chronology endpoint exists. Award has no revision token or correction lifecycle. Historical dates and ambiguous PP links cannot be inferred safely.

No hard-delete endpoint is added. No RBAC definitions, court, Dak, Matter or ONLYOFFICE contract is changed. Existing live IAccessControlService is reused with Award ownership context; possession evidence additionally requires live Possession workstream authorization. A temporary Offer is represented by a TemporaryAcquisitionProceeding linked to Village, Document and optional existing AcquisitionProject/actual Award. There is no existing canonical temporary-occupation model to reuse; making an Offer an Award would fabricate an official fact.

## Security and errors

Authenticated actor comes from ICurrentUserContext; body actor names are ignored. Permissions are checked from the live database, including ownership and workstream scopes. 401 unauthenticated, 403 unauthorized, 404 missing source/context, 400 invalid input/PDF/date, 409 revision/identity/idempotency/context conflict, 413 file limit. V2 errors contain `code`, `message`, and optional `conflicts`. Permissions below refer to existing codes; no new staff grants are made.

JSON mutations use `mutationId` (UUID), `expectedRevision`, and mandatory correction `reason`. UUID is a durable idempotency key scoped to the operation and record. Same key/payload returns the committed response; changed payload returns 409. Uploads use SHA-256 plus canonical context for retry deduplication. New Notification/temporary upload also requires `mutationId` in query. Same bytes for another Award require an explicit reviewed correction rather than silently linking.

## Award metadata and lifecycle

- GET `/api/awards/{id}/staff-workflow`: Award.View. Returns metadata, revision, recordStatus, archiveKind, attachments, notifications and manual/source verification provenance. Archived records require Audit.View in addition to Award.View.
- POST `/api/awards/{id}/metadata/preview`: Award.Edit. Read-only duplicate preview for the following DTO.
- PUT `/api/awards/{id}/metadata`: Award.Edit. Full replacement of editable metadata only for active Draft/NeedsReview records. Preserve IDs and all associations. Verified canonical metadata is protected from generic edits; use a reviewed correction process.
- POST `/api/awards/{id}/lifecycle`: Award.Edit + Audit.View. `{ mutationId, expectedRevision, action: "Archive"|"Void"|"Restore", reason }`. Archive/Void set RecordStatus.Archived; retain business Status and archiveKind. Restore sets active without rewriting linked history. No cascade or destructive deletion. Ingestion into archived records is rejected.

Metadata DTO: `{ mutationId, expectedRevision, awardNumber, awardDate?: "YYYY-MM-DD"|null, awardType?: string|null, actRegime?: string|null, purpose?: string|null, acquisitionProjectId?: UUID|null, remarks?: string|null, reason }`. Response `{ awardId, revision, recordStatus, archiveKind }`. Duplicate preview `{ conflicts: [{ awardId, awardNumber, awardDate, actRegime }] }`; commit returns 409 if conflicts exist. Identity compares existing AwardNumberIdentity equivalence within intersecting AwardVillages and matching normalized regime/project context. Number reuse in another Village or regime remains legitimate. New Village creation/foundation uses the same duplicate check.

Example correction: `PUT /api/awards/{id}/metadata` with `{"mutationId":"11111111-1111-4111-8111-111111111111","expectedRevision":0,"awardNumber":"12/2001-02","awardDate":"2002-03-12","awardType":"Permanent","actRegime":"LAA1894","purpose":"Drain","acquisitionProjectId":null,"remarks":null,"reason":"Corrected transcription against register"}`.

## Core documents: explicit direct and analysis modes

POST `/api/awards/{id}/core-documents?role=Award&mode=Direct&villageId={id}`; multipart `file`. Award.CoreDocument.Upload, ownership context. `mode` defaults to Direct for legacy clients; Direct never starts OCR/classification, returns no detected type or confidence confirmation. UploadAndAnalyze additionally requires Award.Edit and is supported only for role Award. Actual suggestions stay in existing review staging and must be verified before canonical commit.

Roles: `Award`, `PossessionProceeding`, `StatementA`, `NM`, `TemporaryOffer`, `TemporaryProceedings`. Query metadata: `legalDate`, `referenceNumber`, `partNumber`, `title`, `remarks`, repeated `khasraIds`. Select a linked Village when Award spans several Villages. PossessionProceeding requires legalDate for every new upload and additionally possession authorization. Selected Khasras must belong to this Award and Village; evidence links do not imply possession status. StatementA/NM dates are optional. Many distinct documents per role, including multiple PP on the same date, are valid. NM upload creates/reuses the existing NmDocument review; extraction remains explicit.

Response (legacy fields retained): `{ documentId, role, attachmentId, villageId, possessionEventId?, nmDocumentId?, duplicate, warning?, jobId?, revision }`. Persisted MIME is application/pdf. PDF extension, signature, parser/content validity, configured size limit, stored size/hash and association are checked. Exact retry returns same attachment; conflicting metadata/role or another owning Award returns 409. Historical duplicate Document rows are not consolidated automatically.

- GET `/api/awards/{id}/core-documents`: keeps documentId/role/name/MIME/uploadedAt; adds attachmentId, Village, date, optional metadata, immutable source hash, revision, possessionEventId and state. Ordered by legalDate then creation/document identity; legacy missing dates remain null. Normal lists omit detached evidence; staff-workflow history includes it.
- PUT `/api/awards/{id}/core-document-links/{attachmentId}`: Award.Edit (PP also possession authorization). `{ mutationId, expectedRevision, action: "Update"|"Detach"|"Restore", legalDate?, referenceNumber?, partNumber?, title?, remarks?, reason }`. Update corrects metadata/PP date only, preserving event/evidence ID/hash; detach hides the relationship and its PP event; restore reactivates it. No source deletion, Village/Award mutation or NM review deletion.
- POST `/api/awards/{awardId}/documents/{documentId}/analyze?villageId={id}`: existing explicit later Analyze; Award.Edit. Works for directly uploaded Award role (legacy Award PDF identity accepted). Existing active job returned on retry; staged evidence retained.
- POST `/api/award-pdf-extractions?targetAwardId={id}&selectedVillageId={id}`: backwards-compatible Direct storage default, routed through the same validated core upload. Optional mode=UploadAndAnalyze. Existing `{ documentId, jobId }` response preserved.
- Existing stored-PDF recovery linking stays explicit and rejects other Award ownership. Legacy documents/extraction jobs remain readable. No AI verification label is created by manual upload/edit.

Wrong attachment correction: detach with reason, then explicitly upload to the correct Award. Reuse of the archived source bytes is allowed only after all previous owning relationships are detached and the operator has live access to their Awards. Retain old relationship and audit. Wrong Village on an Award: never change an AwardVillage containing official dependent Khasras/court/notification evidence through generic metadata. Void the mistaken draft, create/reuse the correctly scoped Award, detach/relink only reviewed documents; retain official histories. Multi-village Award expansion uses existing verified ingestion linking with reviewed source context.

## Notification source registration and finalization

- POST `/api/awards/{id}/notification-documents` multipart file, Award.Create + Award.CoreDocument.Upload + Award.Edit. Query `{ mutationId, villageId, notificationId? }` for explicit reuse, or `{ sectionType, notificationNumber?, numberAbsentReason?, notificationDate, remarks? }` for registration. Date and source PDF required for a newly finalized record. sectionType stores e.g. `4(1)`, `6`, `9`, `17(1)`, `1(custom)` or another source transcription; it is not a restricted enum. Missing number remains empty, with mandatory absence reason and evidence; never invent a number.
- Response `{ notificationId, documentId, finalized, needsReview, revision, warning? }`. Reuse must be explicit; overlapping source hash/identity returns a 409 conflict pointing to canonical notification rather than fabricating another. Multiple documents per notification supported; explicit canonical reuse across Awards/Villages shares physical bytes. Never mutate another Notification's metadata during reuse.
- POST `/api/notifications/{id}/finalize`: Award.Edit with notification ownership; `{ mutationId, expectedRevision, reason }`. Requires actual date and linked active PDF/Award context; incomplete legacy records remain Needs Review.
- Existing POST `/api/notifications` is a metadata-only draft registration route, generalized beyond 4/6. Existing null dates remain readable. Duplicate identity includes date; no silent cross-date reuse. Finalization is exclusively evidence-backed.
- Existing POST `/api/awards/{id}/notifications/{notificationId}` explicitly links the canonical identity; date after Award gives a review warning in timeline, never automatic rejection.

## Temporary Section 35 and legal timeline

POST `/api/villages/{id}/temporary-acquisition-proceedings` multipart file, Award.Create + Award.CoreDocument.Upload with LandRecords context. Query `mutationId`, `documentType=TemporaryOffer|TemporaryProceedings|TemporaryOrder`, `referenceNumber?`, `actRegime`, `legalSection` (e.g. `35(1)`), `purpose`, `durationMonths?`, `commencementDate?`, `expiryDate?`, `legalDate?`, `compensationReference?`, `remarks?`, `acquisitionProjectId?`, `awardId?` (only an actual existing Village-linked Award). Duration does not manufacture commencement/expiry dates. Response `{ proceedingId, documentId, duplicate }`. No Award created by this endpoint.

GET `/api/villages/{id}/acquisition-timeline`: Award.View with LandRecords context (PP also filtered by possession access). Returns `{ events: [{ eventId, eventType, recordId, awardIds, legalDate, title, sourcePath, documents: [{ documentId, contentPath }], needsReview, warnings }] }`. Dates sort ascending, null dates last. Notification paths through Khasras and AwardNotification union and deduplicate by canonical Notification ID. Include actual Award date, all PossessionEvents, temporary legal/commencement/expiry events. Sources are `/awards/{id}`, `/notifications/{id}`, or the proceeding API; PDF paths `/api/documents/{id}/content`. Unknown dates stay unknown. Detached PP evidence and archived Awards are omitted from active chronology. Later notifications get review warning, remain visible.

## Migration and Anti integration

Additive migration introduces revisions, archiveKind, link metadata and nullable PP/Village FK, notification evidence completion fields, temporary proceedings and mutation receipts. Existing values/IDs/source bytes/history are retained. Backfill only unambiguous single-Village links and Award/NM legacy roles. Historical undated PP attachments remain incomplete; do not synthesize dates or auto-match possession events. Existing duplicates are flagged for human correction, never collapsed destructively.

Anti: consume staff-workflow metadata/revision; preview edits, display 409 conflicts, refresh on revision conflict. Keep mutationId stable for identical retries. Render Direct/Upload & Analyze separately, show analysis confidence only in review workbench. Render arrays per role; collect PP actual date and selected Village. Use canonical notification upload/reuse panel, show incomplete legacy state. Timeline consumes acquisition-timeline rather than making separate entries. Render Temporary Offer/Proceedings separately from final Awards. Frontend acceptance requires actual upload/edit/timeline scenarios; backend tests alone do not certify office staff readiness.

Implementation and test evidence is recorded separately in LAND_RECORDS_STAFF_WORKFLOW_V2_VERIFICATION.md after execution.

## P0: PDF ownership conflict and officer resolution — exact contract

All three paths share the same live ownership policy: core-documents, award-pdf-extractions and stored-document `/link`. Direct and UploadAndAnalyze have identical ownership checks. PostgreSQL transaction advisory locks serialize source hashes, record IDs and officer resolutions; relationship unique indexes remain the final guard. Hash comparison also examines older duplicate Document rows. New uploads never consolidate those historical rows or delete their source bytes.

HTTP 409 example (all fields shown are returned; values are illustrative):

```json
{
  "code": "PDF_OWNERSHIP_CONFLICT",
  "message": "This PDF is attached to another Award. Open the existing Award, or request a reviewed link correction.",
  "conflicts": {
    "documentId": "11111111-1111-4111-8111-111111111111",
    "documentReference": "original-award.pdf",
    "contentPath": "/api/documents/11111111-1111-4111-8111-111111111111/content",
    "sha256Hash": "64-lowercase-hex-characters-from-the-source",
    "selectedAwardId": "22222222-2222-4222-8222-222222222222",
    "existingLinks": [{
      "awardId": "33333333-3333-4333-8333-333333333333",
      "awardNumber": "12/2001-02",
      "awardRevision": 0,
      "attachmentId": "44444444-4444-4444-8444-444444444444",
      "attachmentRevision": 0,
      "villageId": "55555555-5555-4555-8555-555555555555",
      "villages": [{ "villageId": "55555555-5555-4555-8555-555555555555", "name": "Synthetic Village" }],
      "canCorrect": true,
      "documentId": "11111111-1111-4111-8111-111111111111",
      "documentReference": "original-award.pdf"
    }],
    "hasRestrictedLinks": false
  }
}
```

If the caller cannot view an existing Award, omit that entry entirely. If no owner is viewable, documentId/documentReference/contentPath are null, existingLinks is empty, and hasRestrictedLinks is true. SHA comes from the file the caller supplied. Do not disclose hidden Award numbers, IDs, Villages or attachment revisions. canCorrect reports live source-side Award.Edit + Award.CoreDocument.Upload + Audit.View; the resolution independently requires all three on the target as well.

For historical duplicate Document rows, root documentId/documentReference/contentPath come from an authorized existing link. Correction checks every active hash-matched owner. An already-owned target returns 409 AlreadyAttached without creating another identity link. Additional exclusive owners or multiple historical source links require review and return 409 UnreviewedSharedOwnership; hidden owners cannot be bypassed through another Document identity.

Same-Award exact retry returns the same Document and attachment, with `attachmentStatus: "AlreadyAttached"`; core upload adds `duplicate: true`, extraction/recovery adds `alreadyAttached: true`. A 201 response is retained for upload-route compatibility. A changed role or metadata is a separate `DuplicatePdfContextConflict`, not a new attachment. No analysis runs in Direct mode. Ownership errors contain no detected type, confidence score, or request to reconfirm document genre.

Staff choices:

| Situation | Action |
| --- | --- |
| Selected the wrong Award | Open `/awards/{existingLinks[i].awardId}`. No ownership mutation. |
| Previous attachment was wrong | Officer-confirmed `RelinkIncorrectAttachment`. |
| Previous Award registration was duplicate | Officer-confirmed `VoidDuplicateAndRelink`; server validates equivalent number/Village/regime/project context. |
| PDF genuinely covers both Awards | Officer-confirmed `ShareSource`, evidence page numbers and coverage description. |

POST `/api/documents/{existingLinks[i].documentId}/award-link-resolutions` accepts:

```typescript
type AwardPdfLinkResolutionInput = {
  mutationId: string;
  action: "RelinkIncorrectAttachment" | "VoidDuplicateAndRelink" | "ShareSource";
  sourceAwardId: string;
  targetAwardId: string;
  villageId: string; // directly linked to target
  expectedSourceAwardRevision: number;
  expectedTargetAwardRevision: number;
  expectedSourceAttachmentRevision: number;
  expectedSha256Hash: string;
  officerConfirmed: boolean; // must be true
  reason: string;
  sourcePages?: number[] | null; // required positive, real PDF pages for ShareSource
  coverageDescription?: string | null; // required for ShareSource
};
type AwardPdfLinkResolutionResult = {
  documentId: string;
  awardId: string;
  attachmentId: string;
  attachmentRevision: number;
  resolution: AwardPdfLinkResolutionInput["action"];
  sourcePath: string;
};
```

The source and target Award tokens plus attachment token are checked under the same transaction. Input hash must match the preserved source. Officer confirmation and reason are audited with actual authenticated actor. Source detach, target association, optional duplicate void, Award revisions, before/after audit and retry receipt commit atomically. A failed transaction leaves old ownership intact. Identical mutation retries return the original result; changed input with that mutationId is 409 IdempotencyConflict.

Relink retains the old DocumentAward row with IsDetached=true and all source evidence/extraction jobs still referencing their original Award. It does not transfer verified facts, Khasras, court records, possession records or existing ingestion sessions to a different Award. Those require separate source-reviewed canonical corrections. Physical PDF and SHA remain unchanged. Duplicate correction voids the original registration while retaining all dependent official relationships. Sharing marks both active links IsSharedSource=true; an upload to a third Award still receives 409 and requires a separate officer source-coverage decision.

Award PDF Detach/Restore through the generic attachment-correction endpoint also requires `officerConfirmed: true`, Award.CoreDocument.Upload and Audit.View in addition to Award.Edit; it cannot act as an unaudited ownership bypass. Legacy stored-document linking returns an AlreadyAttached result without silently changing the existing Village or role.

Anti: read `code` before rendering any genre panel. PDF_OWNERSHIP_CONFLICT opens the ownership panel; render safe existingLinks and the four choices above. Parse `message` first and legacy ProblemDetails `detail` second; never display raw JSON. Keep type-confidence warnings inside explicit analysis review. Shared-source pages are officer source evidence, not AI confidence. Target Award revision and linked Villages come from staff-workflow. Preserve mutationId on network retry; a changed choice/reason gets a new mutationId.

The implementation includes a targeted ownership panel and canonical Village timeline consumer. Complete metadata/lifecycle, notification and temporary-proceeding UI integration remains Anti's responsibility under this backend contract.

### Completing legacy notification metadata safely

PUT `/api/notifications/{id}/metadata` (Award.Edit with live canonical Notification ownership) accepts `{ mutationId, expectedRevision, sectionType, notificationNumber?: string|null, notificationDate?: string|null, numberAbsentReason?: string|null, remarks?: string|null, reason }`. Only active, unfinalized records without verified SourceEvidence are eligible. It preserves Notification ID, Award/Khasra/document links, before/after audit and optimistic revision; it does not finalize the record. Response `{ notificationId, revision, isFinalized: false, needsReview: true }`.

Anti's legacy completion sequence is: attach source to the existing Notification using notificationId; enter the actual source issue date through the metadata correction endpoint; finalize with the updated revision. Finalized or source-verified facts require a separately reviewed official correction, and the generic metadata endpoint returns 409 VerifiedMetadataProtected.

Number absence stays an empty string with a mandatory explanation. Distinct genuine source PDFs may identify different notifications even with the same absent number/section/date; a filtered canonical identity index enforces uniqueness only for nonempty references. The source hash still detects accidental reuse. No synthetic notification number is invented. The additive follow-up migration changes this index without rewriting existing records.

staff-workflow returns `villageIds` and `villages: [{ villageId, name }]` for selectable target context. Shared TypeScript DTOs are in `src/LAC.Web/src/award/landRecordsStaffTypes.ts` and the structured error types in `LandRecordApiError.ts`. A SourceReviewed summary means at least one listed metadata fact has human source review; render the per-field sourceFacts before claiming a particular field was reviewed. No endpoint marks manual fields AI verified.

The named CoreDocumentInventory symbol from planning is absent at the audited base. Its actual consumers are VillageCoreRecords, AwardDocumentsSection, the core-records inventory API and the existing NM review endpoints; these were audited and the relevant upload/read paths reconciled.
