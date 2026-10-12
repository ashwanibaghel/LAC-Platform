# Smart Core Document Intake API

Backend branch: `codex/smart-core-document-intake`.
Base: `anti/land-records-premium-ui-v2` at `f4e0ae2269ec342be553bab151d15bc22dfe9b66`.

All paths below include `/api`. JSON property names use camelCase. Dates are `YYYY-MM-DD`; timestamps are ISO 8601 UTC. Identifiers are UUIDs. Existing cookie authentication and live database RBAC apply.

## Upload one PDF

`POST /api/villages/{villageId}/core-document-intake`

Send `multipart/form-data`, field `file`, with one PDF. Requires `Award.CoreDocument.Upload` in the Award workstream, or an All scope grant. The selected Village must exist and be active. PDF extension and `%PDF-` signature are checked; MIME supplied by the browser is not trusted.

Returns HTTP **201** and `Location: /api/core-document-intakes/{intakeId}`:

```json
{
  "intakeId": "<uuid>",
  "villageId": "<uuid>",
  "documentId": "<source-pdf-uuid>",
  "fileName": "source.pdf",
  "status": "NeedsConfirmation",
  "detectedRole": "NM",
  "detectedAwardNumber": "42/2010-11",
  "detectedAwardDate": null,
  "detectedVillage": null,
  "detectedAwardType": null,
  "confidence": 0.95,
  "matchState": "MatchedExistingAward",
  "matchedAwardId": "<uuid>",
  "matchedAwardNumber": "42/2010-11",
  "evidence": [
    { "field": "documentRole", "pageNumber": 1, "sourceText": "NM" },
    { "field": "awardNumber", "pageNumber": 1, "sourceText": "Award No. 42/2010-11" }
  ],
  "alternatives": [
    { "kind": "awardNumber", "value": "42/2010-11", "awardId": null, "reason": null },
    { "kind": "documentRole", "value": "NM", "awardId": null, "reason": null },
    { "kind": "existingAward", "value": "42/2010-11", "awardId": "<uuid>", "reason": null }
  ],
  "createdAt": "<ISO-8601>",
  "confirmedAwardId": null,
  "confirmedRole": null,
  "confirmedDocumentId": null,
  "confirmedAt": null,
  "viewRoute": "/api/core-document-intakes/<uuid>/file",
  "downloadRoute": "/api/core-document-intakes/<uuid>/file?download=true",
  "isDuplicate": false
}
```

`detectedRole`: `Award`, `NM`, `StatementA`, `PossessionProceeding`, or `Unknown`. `Unknown` must be corrected before confirmation.

`matchState`:

| Value | Meaning |
| --- | --- |
| `MatchedExistingAward` | One safe exact identity in this Village, a known role, and no detected conflicting dates or Villages. |
| `ProposedNewAward` | Clear Award heading, high-confidence explicit reference, no existing identity in this Village, no detected ambiguity. This creates no Award. |
| `NeedsOfficerReview` | Unknown role, multiple references or existing identities, conflicting evidence, unreadable text, or no safe match. |

`confidence` is a deterministic heuristic in `[0,1]`, not a calibrated probability. All outcomes require an officer decision. The proposal fields remain the original inspection snapshot after confirmation; use `confirmedAwardId`, `confirmedRole`, and `confirmedDocumentId` for the saved outcome.

`alternatives` retains candidate values even when only one was extracted. Entries use `kind`, `value`, nullable `awardId`, and nullable `reason`. Kinds include `awardNumber`, `awardDate`, `village`, `documentRole`, `existingAward`, `villageConflict`, `awardDateConflict`, and `extraction`. Ambiguous references include matching existing Award IDs when available. New kinds can be added as classification expands.

## Batch upload

Same URL, repeated multipart field **`files`**. Maximum **20 files**, each up to **50 MiB**, valid filenames up to 180 characters. Existing server request-body limits also apply (default 250 MiB total). Using `files` always returns the batch envelope, including a one-file batch. More than one uploaded file also selects batch mode.

Returns HTTP **200**:

```json
{
  "items": [
    { "fileName": "nm.pdf", "intake": "<full intake object above>", "errorStatus": null, "error": null },
    { "fileName": "invalid.pdf", "intake": null, "errorStatus": 400, "error": "The uploaded file is not a PDF." }
  ]
}
```

Each file is inspected independently and order is preserved. An ambiguous or corrupt PDF with a PDF signature produces a review intake; a validation failure produces a per-file error. Authorization failure or a nonexistent Village rejects the request. Infrastructure failures are server errors; already completed items are durable and can be found through the list endpoint or retried safely.

Exact byte duplicates are scoped to the selected Village. Re-upload returns the original intake and its original filename with `isDuplicate: true`; it creates no new document or Award. A duplicate of a confirmed intake returns `status: "Confirmed"`. The single-file route still returns 201 with the reused Location. The batch item's outer filename identifies the current upload name.

## Confirm an existing Award link

`POST /api/core-document-intakes/{intakeId}/confirm`

Requires `Award.CoreDocument.Upload` in the Award workstream. The officer must supply the reviewed role and one identity decision; the server never silently substitutes a detected Award ID.

```json
{
  "documentRole": "NM",
  "awardId": "<selected-award-uuid>"
}
```

`documentRole` is one of the four known roles. `awardId` must reference an active Award directly linked to this intake's Village. Officer corrections to the role and Award selection are allowed without changing retained extraction evidence. Returns HTTP **200** with the full intake object and `status: "Confirmed"`.

## Explicitly confirm a new Award

Same confirm URL. Requires **both** `Award.CoreDocument.Upload` and `Award.Create` in the Award workstream or All scope.

```json
{
  "documentRole": "Award",
  "createAward": {
    "awardNumber": "42/2010-11",
    "awardDate": "2010-12-09",
    "awardType": "Main",
    "confirmed": true
  }
}
```

`awardNumber` is required (maximum 128 characters). `awardDate` and `awardType` are nullable; type is maximum 100 characters. Extracted type is only supplied when an explicit Main/Supplementary heading exists. Missing dates/types are not inferred. Corrected creation details are officer assertions, not parser truth.

Supply **exactly one** of `awardId` or `createAward`. Creation requires role `Award` and `createAward.confirmed: true`. Confirmation atomically creates a Draft canonical Award, its Village link, the document link and role, and audit entry.

The server rechecks identities under a Village lock at confirmation. If one compatible Award appeared after staging, it links that Award instead of creating a duplicate. If multiple existing identities match, or the supplied date/type conflicts with the existing identity, it returns **409**; select the correct existing Award explicitly. It never merges Awards. A PDF already linked to another Award or a conflicting core role also returns 409. No existing canonical fields are overwritten by extraction or confirmation.

An identical confirmation retry is idempotent. Selecting the already confirmed Award and role is also idempotent. A different decision after confirmation returns 409. Multiple distinct PDFs can share an Award and role. A byte-identical manually uploaded PDF already linked to the same Award is reused; `confirmedDocumentId` identifies the linked PDF, while `documentId` and the intake file routes retain the newly uploaded original source.

## Read status, list, source, and original PDF

These endpoints require `Award.View` in the Award workstream:

| Method and path | Response |
| --- | --- |
| `GET /api/core-document-intakes/{id}` | Full intake object. |
| `GET /api/villages/{villageId}/core-document-intakes?skip=0&take=50` | Array of intake objects, newest first; take clamped to 1–100. |
| `GET /api/core-document-intakes/{id}/file` | Original source PDF, inline with range support. |
| `GET /api/core-document-intakes/{id}/file?download=true` | Original PDF with its original download filename. |
| `GET /api/core-document-intakes/{id}/source-evidence` | `{ intakeId, documentId, sha256Hash, classifierVersion, pages: [{ pageNumber, text }] }`. |

Intake statuses: `Staged` (stored, inspection pending/interrupted), `NeedsConfirmation` (proposal ready, including officer-review proposals), and `Confirmed`. Confirmation of a `Staged` intake returns 409. Re-uploading identical bytes resumes an interrupted Staged intake. Staged originals are excluded from existing canonical document lists until confirmation.

## Per-Award document inventory

`GET /api/villages/{villageId}/core-records`

Requires both `Village.View` and `Award.View` (Award workstream). Returns an array:

```json
[
  {
    "id": "<award-uuid>",
    "awardNumber": "42/2010-11",
    "awardDate": "2010-12-09",
    "awardType": "Main",
    "roles": [
      {
        "role": "NM",
        "count": 1,
        "available": true,
        "documents": [
          {
            "documentId": "<uuid>",
            "role": "NM",
            "coreDocumentRole": "NM",
            "originalFileName": "nm.pdf",
            "uploadedAt": "<ISO-8601>",
            "status": "Active",
            "mimeType": "application/pdf",
            "viewRoute": "/api/documents/<uuid>/content",
            "downloadRoute": "/api/documents/<uuid>/content?download=true"
          }
        ]
      }
    ],
    "documents": ["<same actual document entries, flattened>"]
  }
]
```

All four roles are always present: `Award`, `NM`, `StatementA`, `PossessionProceeding`. Missing roles have `count: 0`, `available: false`, `documents: []`. Document arrays include every active file for that Award and role, newest first. An NM belonging to one Award never contributes to another Award's inventory. Archived/inactive documents are omitted. Existing aggregate fields and flat `coreDocumentRole` are retained for compatibility.

`GET /api/awards/{awardId}/core-documents` returns the flattened array of the same document-entry shape and requires `Award.View` in the Award workstream. Existing authenticated `/api/documents/{documentId}/content` routes serve linked PDFs.

## Detection and persistence limits

- Inspection runs locally using existing PdfPig word extraction and layout helpers. No cloud, language-model call, or local model server is required.
- Inspect the first six pages, retaining up to 16,000 native-text characters per page and exact matched snippets with page numbers. Image-only/unreadable PDFs require officer review. No inferred OCR digit substitutions or filename-based identities.
- Whitespace around Award separators and typographic dash variants are normalized. A four-digit fiscal start such as `2010-11` can match `2010-2011`. Leading zero differences and two-digit versus four-digit starting years are not equated. Canonical Award text is never rewritten by normalization.
- Original PDF bytes and SHA-256, bounded source text, classifier version, original proposal, officer confirmation, and audit trail are retained. Canonical links and evidence are protected by restrictive foreign keys.
- PostgreSQL Village row locks serialize cross-process duplicate intake/confirmation mutations; a unique `(villageId, sha256Hash)` index and intake revision token enforce duplicate/concurrency safety. The locks govern this intake workflow; unrelated legacy Award creation workflows are not changed.

Errors use Problem Details with `status` and `detail`: 400 invalid input, 401 unauthenticated, 403 permission denied, 404 missing intake/Village, 409 conflicting or premature confirmation. Batch validation errors use `errorStatus`/`error` inside `items`.

Migration: `20261004181412_AddSmartCoreDocumentIntake`. Apply through the existing EF migration/startup process before using the endpoints. No frontend files are changed by this backend branch.

## Verification

```powershell
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter 'FullyQualifiedName~CoreDocument'
dotnet test tests/LAC.Tests/LAC.Tests.csproj
dotnet ef migrations has-pending-model-changes --project src/LAC.Infrastructure --startup-project src/LAC.Api
```

To execute the isolated PostgreSQL migration and concurrency test, set `CORE_INTAKE_POSTGRES_SMOKE=1` and supply the existing `ConnectionStrings__DefaultConnection` for a local PostgreSQL server. That test creates a uniquely named temporary schema, applies the migrations there, tests independent database contexts/gates, and removes that schema in `finally`. It does not apply migrations to the running application's schema.
