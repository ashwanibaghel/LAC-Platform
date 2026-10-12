# Land Records V2 integrated checkpoint — API contract for Anti

Integration branch: `codex/land-records-v2-integration`. Backend parent: `af795fe843b22efdcf6d1eb50e524a2b378b0e50`. Common/main base at audit: `37db57e5fa40cd353e70c3161abe62e23eba4cc5`. Pinned Anti milestone: `d25bf6bdcb5f22b16b6cdfd245a0d88d783070b8`; selectively audited Land Records source includes its P0 follow-up `8089517c1181cb442abf1654dfcc7df51a17b95a`. No wholesale Anti branch merge or unrelated Court changes are included.

This document is the integration entry point. The [staff V2 contract](LAND_RECORDS_STAFF_WORKFLOW_V2_API_CONTRACT.md) remains authoritative for metadata, lifecycle, canonical notification registration/completion, temporary proceedings, explicit analysis and officer correction payloads. This document reconciles the existing Smart Intake and premium Village consumers with those endpoints. Earlier Smart Intake documentation is historical where its ownership/storage/error policy differs below.

## One canonical registration policy

`Document`, `DocumentAward`, `DocumentVillage`, `PossessionEvent` and the existing NM review remain canonical. `CoreDocumentIntake` is a proposal/history record. It does not independently register final Award attachments. Confirmation calls the same V2 attachment service as Direct Upload, inside one PostgreSQL transaction with hash/identity/intake/Award locks.

All routes reuse the same preserved SHA-matched PDF, including a source initially staged by Smart Intake. Separate Village proposals may reference the same Document; the unique `(VillageId, Sha256Hash)` proposal key remains. Historical duplicate Document rows are preserved, checked across the entire hash, and never consolidated or physically deleted. A stored source is hash/size verified before reuse. A staged unclassified source becomes active only through canonical attachment confirmation; its proposal, native text and original bytes remain.

Canonical attachment registration/recovery/Restore and officer resolution take the common identity lock before Award/document/hash locks. This prevents a late existing-Award selection during Smart creation from reversing the Direct Upload lock order. Restore is hash-serialized with new uploads. Stored recovery computes/verifies the actual source hash before ownership lookup, including hashless legacy documents. An explicitly officer-confirmed correction may fill a missing legacy hash only after byte verification, in the same audited transaction; a nonempty existing hash is never replaced. Missing historical hashes cannot be globally inferred without reading their source evidence; no migration guesses or rewrites them.

Direct attachment stores and links the explicit Award/role, without classifier, OCR or genre confirmation. `UploadAndAnalyze` attaches through the identical policy and explicitly starts the existing Award review job. Smart Intake staging explicitly runs bounded local native text classification, then asks for officer selection; it does not start an Award OCR job. Smart confirmation attaches in Direct mode. Later full analysis uses the same Document ID through the existing Analyze endpoint.

## Existing Smart Intake routes, retained

All require authentication and live authorization; permission checks occur in the service with actual source/record ownership context, including Own scopes. No grants or RBAC definitions are changed. Unresolved historical creator names are not treated as the current user.

| Route | Contract |
| --- | --- |
| POST `/api/villages/{villageId}/core-document-intake` | Multipart `file` for single 201 response, or `files`/multiple files for 200 batch. Maximum 20 files; each file limited to min(50 MiB, configured V2 limit), with full PDF/size/hash validation. Requires Award.CoreDocument.Upload and access to reused source. |
| GET `/api/core-document-intakes/{id}` | Live Award.View on the staged owner or actual linked source context; returns proposal/history. |
| GET `/api/villages/{villageId}/core-document-intakes?skip=0&take=50` | Live Award.View; inaccessible entries omitted. Paging retained. |
| GET `/api/core-document-intakes/{id}/source-evidence` | Same read authorization; `{ intakeId, documentId, sha256Hash, classifierVersion, pages: [{pageNumber,text}] }`. Historical version/text preserved. New classifier is `core-native-v2`, including the temporary Offer guard. |
| GET `/api/core-document-intakes/{id}/file?download=true` | Same live source authorization; original bytes, existing PDF MIME and optional filename download. |
| POST `/api/core-document-intakes/{id}/confirm` | 200 confirmation or structured 4xx. Existing Award must be active and directly linked to the intake Village. Source, selected Award and PP workstream permissions are checked live. New Award requires Award.Create and explicit officer creation confirmation. |

Exact confirmation DTO (existing fields preserved; added fields optional except PP legalDate):

```typescript
type CoreIntakeConfirmation = {
  documentRole: "Award" | "NM" | "StatementA" | "PossessionProceeding";
  awardId?: string | null;
  createAward?: {
    awardNumber: string;
    awardDate?: string | null;
    awardType?: string | null;
    confirmed: boolean;
    actRegime?: string | null;
    acquisitionProjectId?: string | null;
  } | null;
  legalDate?: string | null; // actual date required for every new PP
  referenceNumber?: string | null;
  partNumber?: string | null;
  title?: string | null;
  remarks?: string | null;
  khasraIds?: string[] | null;
  expectedRevision?: number | null; // new clients should supply returned intake revision
};
```

Select exactly one of awardId/createAward. A new registration can be created only for an officer-confirmed final Award role. Known Offer/Section 35 evidence cannot be confirmed as a final Award; use temporary-proceeding registration. Duplicate identity uses the V2 Village/regime/project policy and never modifies existing metadata based on detection. Existing source ownership must be reviewed against an actual selected target; creating a new registration is not an ownership bypass. A creation conflict may have `selectedAwardId` equal to the empty UUID: select/create an actual target separately before any correction.

`expectedRevision` protects new confirmations. Legacy clients omitting it retain transactional confirmation/replay protection. Exact semantic retries, including historical confirmation JSON without newly added nullable fields, return the original confirmed identity; conflicting decisions return `AlreadyConfirmedDifferentDecision` 409. No detection populates canonical verified fields. Confirmation and optional Award creation, canonical attachment/event/NM review, intake state and actor audit commit together or roll back together. The original durable staged source survives a failed confirmation.

Single/confirm response has these fields:

```typescript
type CoreIntakeView = {
  intakeId: string; villageId: string; documentId: string; fileName: string;
  status: "Staged" | "NeedsConfirmation" | "Confirmed";
  detectedRole: string; detectedAwardNumber: string | null;
  detectedAwardDate: string | null; detectedVillage: string | null;
  detectedAwardType: string | null; confidence: number;
  matchState: "MatchedExistingAward" | "ProposedNewAward" | "NeedsOfficerReview";
  matchedAwardId: string | null; matchedAwardNumber: string | null;
  evidence: {field:string;pageNumber:number;sourceText:string}[];
  alternatives: {kind:string;value:string;awardId:string|null;reason:string|null}[];
  createdAt: string; confirmedAwardId: string | null;
  confirmedRole: string | null; confirmedDocumentId: string | null;
  confirmedAt: string | null; viewRoute: string; downloadRoute: string;
  isDuplicate: boolean; revision: number;
  attachmentStatus: "Attached" | "AlreadyAttached" | null;
};
type CoreIntakeBatch = {
  items: {
    fileName: string; intake: CoreIntakeView | null;
    errorStatus: number | null; error: string | null;
    code: string | null; conflicts: PdfOwnershipConflict | null;
  }[];
};
```

`documentId` is preserved original intake evidence; `confirmedDocumentId` is the canonical attached identity and may differ for historical duplicate rows. Use confirmedDocumentId for canonical navigation after confirmation. `isDuplicate` on an unconfirmed staged proposal indicates a retry of staging, not existing Award ownership. Never infer ownership from detected/matched Award number, PDF filename, role availability or confidence.

## Structured errors and officer correction

Error shape: `{code,message,conflicts?}`. No ownership conflict is RFC ProblemDetails JSON or a document confidence warning. Batch preserves `errorStatus`/`error` and adds the same machine code/conflicts on each failed item.

All canonical upload routes, Smart confirmation and stored-document recovery share `PDF_OWNERSHIP_CONFLICT` 409 with the exact safe `PdfOwnershipConflict` object in the staff contract. It contains only authorized `existingLinks`, actual per-link document identity, Award number, Village and revision; restricted identities are omitted. Root identity is chosen from a visible link. If none is visible, identity/URL fields are null and `hasRestrictedLinks` is true.

Correction remains one endpoint: POST `/api/documents/{existingLinks[i].documentId}/award-link-resolutions`, with the exact `AwardPdfLinkResolutionInput` from the staff contract. Actions remain `RelinkIncorrectAttachment`, `VoidDuplicateAndRelink` and `ShareSource`; wrong selection navigates without mutation. Confirmation, reason, live Edit+Upload+Audit permissions, hash, source/target Award revisions and attachment revision are mandatory. Sharing requires genuine page coverage. No client-side relink or duplicate registration bypass exists.

After correction, use its returned Document ID. A still-pending intake may be confirmed against its chosen target to complete proposal history. An already-confirmed historical intake remains on its original decision; source correction does not transfer or rewrite its extraction/confirmation history. The new canonical target association already exists after officer correction. A third ordinary upload still conflicts.

## Role inventory and acquisition history compatibility

GET `/api/villages/{id}/core-records` retains the premium nested `roles[].documents` and the old flat `documents` list. Every role is an array; multiple distinct PDFs per role are allowed. Active inventory excludes detached/inactive sources and archived Awards. Each entry adds `attachmentId`, `villageId`, `legalDate`, `revision`, `possessionEventId` and retains source view/download routes. Legal-date ordering does not invent dates for legacy records. PP confirms create/reuse the canonical evidence/event policy; NM confirms create/reuse the existing NM review.

GET `/api/awards/{id}/core-documents` retains the V2 attachment metadata/history contract and canonical source routes. It is registered once, not by both API mappers. Direct, extraction-upload, NM upload and later Analyze remain as documented in the staff contract.

GET `/api/villages/{id}/acquisition-history` preserves the premium explicit Award grouping, sharing metadata and unassigned Notifications. It gains per-event `sourcePath`, `documents: [{documentId,contentPath}]`, `warnings`, plus `temporaryEvents` when the LandRecords workstream is authorized. Notifications are attributed only through AwardNotification; shared parcel/date proximity never invents attribution. PP comes from the same active PossessionEvent; detached PP disappears from both projections. Temporary proceedings are shown separately and do not create Award groups. GET `acquisition-timeline` remains the canonical flat API for V2 consumers; the current premium Overview uses the grouped projection rather than rendering duplicate parallel timelines.

## Migrations and Anti continuation

Preserve the historical Smart migration `20261004181412_AddSmartCoreDocumentIntake` and both V2 Up/Down operations and IDs. V2 target-model designers and the final snapshot include the additive intake entity. New `20261012133435_ReconcileSmartCoreIntakeWithStaffV2` changes only the DocumentId intake index from unique to nonunique; restrictive FKs and Village/hash uniqueness remain. Down refuses rollback while shared-document proposals exist. No source bytes or historical rows are rewritten by reconciliation.

Both Smart-first and V2-first histories were explicitly exercised on an isolated PostgreSQL cluster; see the [integration verification report](LAND_RECORDS_V2_INTEGRATION_VERIFICATION.md). Never apply migrations to a protected database as part of this checkpoint.

Anti must start complete V2 UI work from the published integration SHA in a separate worktree/branch. Keep the premium directories/four Village tabs, the explicit Direct/UploadAndAnalyze attach form, Smart staged proposal flow and shared ownership panel. Finish metadata/lifecycle/notification/temporary UI under the existing typed contract. Preserve mutationId for correction retries and refresh live revisions. Do not merge the old Anti branch wholesale: it contains unrelated accumulated features and obsolete independent registration logic. Uncommitted concurrent UI drafts were preserved separately and are not certified by this integration checkpoint.
