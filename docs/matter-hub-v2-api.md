# Matter Hub V2 canonical context API

Base: `5770cbaedcdefa8293cac85d662696693abcee2b`.

Matter is an operational file/context. Court, Award, Khasra, Dak, WorkItem, and Outward remain authoritative in their own modules. Nothing is inferred from titles, reference text, Village proximity, Court links, or Khasra links. No Court metadata or WorkItem assignment fields are added to Matter.

## Domain and migration

Migration: `20261004222119_AddMatterCanonicalContext`.

- Adds `MatterKhasras` with `id`, `matterId`, `khasraId`, restricted foreign keys, and unique `(MatterId, KhasraId)`.
- Reuses `MatterAward`, `CourtCaseMatter`, `DakMatterLink`, `WorkItemMatterLink`, `MatterDocument`, `MatterDraft`, and `Outward.MatterId`.
- Adds a filtered unique `MatterAward.MatterId` index where `IsPrimary = TRUE`. No default primary is chosen for a new `awardIds` list.
- Adds nullable `MatterEvent.ContextEntityType` / `ContextEntityId` audit identifiers. They retain identity after unlinking and do not duplicate canonical facts.
- Adds `AwardLinked`, `AwardUnlinked`, `KhasraLinked`, `KhasraUnlinked`, `CourtCaseLinked`, `CourtCaseUnlinked`, `DakLinked`, `DakUnlinked`, and `PrimaryAwardChanged` actions. Existing action values are unchanged; the journal remains append-only.

There is no data backfill or relationship rewrite. If an existing database already has multiple primary Awards for a Matter, the unique index fails rather than guessing which Award should be primary; an officer must explicitly resolve those relationships before migration.

## Creation lookups

`GET /api/matters/context` retains the existing creation lookup shape:

```json
{
  "workstreams": [{ "id": "11111111-1111-1111-1111-111111111111", "name": "Court References", "code": "COURT_REFERENCES" }],
  "matterTypes": ["Court Case", "Compensation", "Land Acquisition", "General", "Other"]
}
```

Workstreams are filtered by live `CanCreateMatterInWorkstreamAsync` authorization, including active user, role, Workstream, and memberships. An active officer with no allowed Workstreams gets an empty list. An inactive officer receives `403`; unauthenticated requests receive `401`. There is no default Workstream. Canonical record selectors should use the existing authorized module lookup/directory APIs.

## Create Matter

`POST /api/matters`

```json
{
  "villageId": "22222222-2222-2222-2222-222222222222",
  "title": "Prepare response and compile evidence",
  "matterType": "Court Case",
  "workstreamId": "11111111-1111-1111-1111-111111111111",
  "referenceNumber": "Office/2026/41",
  "remarks": "Operational file notes",
  "awardIds": ["33333333-3333-3333-3333-333333333333"],
  "primaryAwardId": "33333333-3333-3333-3333-333333333333",
  "khasraIds": ["44444444-4444-4444-4444-444444444444"],
  "courtCaseIds": ["55555555-5555-5555-5555-555555555555"]
}
```

Required: active primary Village, nonblank operational `title`, explicit non-empty `workstreamId`. Omitted/blank `matterType` defaults to `Other`, preserving the existing API convention. Optional metadata: office/internal `referenceNumber`, `remarks`, legacy `khasraReferenceText`. A Court-linked Matter requires no manually copied Court case number, case title, or Court status. `title` is the operational Matter title.

Optional relationship arrays accept canonical IDs only, deduplicate repeated IDs, and allow at most 250 entries per type. Empty UUIDs are rejected. No Award is inferred from a Khasra or Court. Awards require an existing explicit Award–Village relationship; Khasras must belong to the primary Village. Court cases can span context without requiring an inferred Village relation. Every supplied target requires its canonical view permission. The Matter, links, and events are created atomically.

`primaryAwardId`, when non-empty, must be explicitly included in `awardIds`. Omitting it creates no primary. Response: `201`, `Location: /api/matters/{id}`, body `{ "id": "..." }`. Initial Matter revision is `0`, including its initial explicit links.

The existing `POST /api/villages/{villageId}/matters` supports these same optional canonical fields, with Village taken from the URL and its existing response retained.

## Update metadata and context

`PUT /api/matters/{id}` retains existing fields and adds:

```json
{
  "title": "Updated operational title",
  "matterType": "Court Case",
  "referenceNumber": "Office/2026/41",
  "remarks": "Updated notes",
  "khasraReferenceText": null,
  "expectedRevision": 3,
  "awardIds": ["33333333-3333-3333-3333-333333333333"],
  "primaryAwardId": "33333333-3333-3333-3333-333333333333",
  "khasraIds": [],
  "courtCaseIds": ["55555555-5555-5555-5555-555555555555"],
  "villageId": "22222222-2222-2222-2222-222222222222"
}
```

Arrays omitted or `null` leave that relationship set unchanged. A provided array replaces that set exactly; `[]` explicitly unlinks all targets of that type. Both supplied and removed targets must be authorized; a hidden target cannot be removed through a bulk replacement. Metadata optional fields retain existing PUT semantics (omission clears the optional metadata field).

An omitted/null `primaryAwardId` preserves the primary if it remains linked. An explicitly removed primary becomes unset; no replacement is guessed. Use the empty UUID (`00000000-0000-0000-0000-000000000000`) to explicitly clear primary designation while retaining the Award link. A non-empty primary must belong to the final explicitly linked set.

Optional `villageId` changes the primary Village only when authorized; retained Award/Khasra links must still satisfy that Village's explicit relationships. Workstream changes continue through the existing revision-checked `POST /api/matters/{id}/reclassify` endpoint with a reason. Status is Matter's operational status; this API never changes Court status.

Requires `Matter.Edit` for the Matter. Successful updates increment revision once and return the existing metadata response with `id`, `title`, `matterType`, `status`, `referenceNumber`, `remarks`, `khasraReferenceText`, `revision`, and `updatedAt`. Context deltas and metadata changes are one transaction with immutable events.

## Link, list, and unlink

For each segment `awards`, `khasras`, `court-cases`, `daks`:

| Method | Path | Contract |
| --- | --- | --- |
| GET | `/api/matters/{id}/{segment}` | `{ "items": [authorized summaries], "revision": 4 }` |
| PUT | `/api/matters/{id}/{segment}/{targetId}` | Body `{ "expectedRevision": 4 }`; Awards optionally accept `"isPrimary": true` |
| DELETE | `/api/matters/{id}/{segment}/{targetId}?expectedRevision=4` | Explicit query revision required |

PUT/DELETE return `{ "id": "matter UUID", "revision": 5, "changed": true }`. A duplicate link/unlink at the current revision returns `changed: false`, keeps the revision, and adds no event. A stale revision returns `409`, including on duplicate requests. `expectedRevision` is required in a PUT body. `isPrimary: true` links/promotes the Award and demotes the previous primary atomically; false/omitted preserves an already-primary link. Non-Award links reject `isPrimary: true`.

Mutations require `Matter.Edit` plus target view authorization (`Award.View`, `Khasra.View`, record-specific `Court.View`, or record-specific `Dak.View`). They modify only the explicit context relationship and Matter revision/history. Court/Dak metadata and module revision are untouched. Court/Dak unlinks archive the existing active relationship. Court relinks reuse the existing pair, matching Court's own workflow; Dak relinks create a fresh active pair, matching Dak's own workflow. Award/Khasra unlink removes the junction row while the immutable Matter event retains target identity.

Unavailable and unauthorized targets produce the same `403` response without metadata or an existence distinction. Invalid authorized Village relationships/IDs/primary selection produce `400`. Stale revisions, concurrent writes, or archival during a mutation produce `409`; an already unavailable/unauthorized Matter fails its authorization gate with `403`. Missing login produces `401`.

## Read-only overview context

`GET /api/matters/{id}/context`

```json
{
  "matter": {
    "id": "66666666-6666-6666-6666-666666666666",
    "title": "Prepare response and compile evidence",
    "matterType": "Court Case",
    "status": "Open",
    "workstreamId": "11111111-1111-1111-1111-111111111111",
    "workstreamName": "Court References",
    "referenceNumber": "Office/2026/41",
    "remarks": "Operational file notes",
    "khasraReferenceText": null,
    "revision": 4
  },
  "village": { "villageId": "22222222-2222-2222-2222-222222222222", "name": "Pochanpur" },
  "awards": [{ "awardId": "33333333-3333-3333-3333-333333333333", "awardNumber": "30/2002-03", "awardDate": "2002-12-09", "isPrimary": true }],
  "khasras": [{ "khasraId": "44444444-4444-4444-4444-444444444444", "villageId": "22222222-2222-2222-2222-222222222222", "displayNumber": "12" }],
  "courtCases": [{ "courtCaseId": "55555555-5555-5555-5555-555555555555", "caseNumber": "W.P.(C) 223/2026", "caseTitle": "Ramesh Kumar", "courtName": "Delhi High Court", "currentStatus": "Pending", "operationalNdoh": "2026-11-01" }],
  "courtContextState": "Linked",
  "daks": [{ "dakId": "77777777-7777-7777-7777-777777777777", "diaryNumber": "D/1", "subject": "Reply requested", "status": "Registered", "receivedDate": "2026-10-01" }],
  "workItems": [{ "workItemId": "88888888-8888-8888-8888-888888888888", "title": "Prepare reply", "status": "Assigned", "priority": "Urgent", "dueAt": "2026-10-10T10:00:00+00:00", "responsibleDesk": { "deskId": "99999999-9999-9999-9999-999999999999", "name": "Court desk" }, "assignedUser": null }],
  "outwards": [],
  "outwardCount": 0,
  "documentCount": 0,
  "draftCount": 0
}
```

Authorization:

- `Matter.View` is required; unavailable/unauthorized Matter returns `403`.
- Each linked canonical target is independently filtered by live module authorization. Inaccessible targets are absent entirely: no ID, metadata, placeholder, hidden-target count, or primary hint. Village is `null` if inaccessible. Legacy list/detail primary-Award projections are also permission-filtered.
- `courtContextState` is `Linked` when an authorized active Court link is returned; otherwise `Court context not linked`. The same empty state covers legacy unlinked Matters and inaccessible Court links, avoiding existence disclosure.
- Court summaries read canonical status and the same operational NDOH resolver used by Court, including authoritative proceeding/accepted listing evidence. Dates can be `null`.
- WorkItems require `WorkItem.View`, active link/record, no completion timestamp, and status other than Completed/Cancelled. Responsibility comes from the current active WorkItemAssignment. Active desk/user summaries are returned under the canonical WorkItem.View authority; inactive/missing responsibility records yield `null`. No assignment is stored on Matter.
- Outwards require module list authorization; `outwardCount` counts only returned authorized active records.
- `documentCount` counts active MatterDocument links whose Document is active with `Status = Active`, consistent with Matter document content authorization. `draftCount` counts only active drafts independently authorized by `Draft.View` and Matter.View.
- No writes occur during context reads. Summaries reflect canonical changes on subsequent reads.

`GET /api/matters/{id}/events` returns `{ "items": [...] }`, ordered by journal sequence, with `id`, `sequenceNumber`, string `action`, `actionAt`, `actionByDisplayNameSnapshot`, `contextEntityType`, and `contextEntityId`. Events referring to currently inaccessible canonical targets are omitted. Historical sequence numbers remain unchanged.

## Compatibility and document provenance

Existing metadata/list/detail APIs and legacy unclassified Matters remain available under existing Matter authorization. `W.P.(C) 223/2026 - Ramesh Kumar` remains an ordinary Matter title until an officer explicitly links its canonical CourtCase. `khasraReferenceText` remains text only; no parsing, manufacture of IDs, or backfill occurs.

Legacy `awardId` is still accepted by create/update. Create links that Award as primary. Update replaces/removes only the existing primary association, preserving other links; empty UUID removes the primary association. Legacy Award updates now authorize targets safely. Do not mix `awardId` with `awardIds` or `primaryAwardId` in one request (returns `400`).

Selecting an Award creates no MatterDocument and copies no PDF. Existing authorized source documents become eligible through the current Link Existing / Extract Specific Pages workflows. SHA, source document, exact normalized source pages, extracted-by officer, contextual notes, and extraction lineage are preserved. The frontend text `Select Award (Linked Core Documents Auto-Attach)` is misleading copy only; frontend was not changed in this backend task.

## Validation

The regression suite covers canonical multi-Court/Khasra creation, live Court status/NDOH, legacy unlinked loading, multiple Awards and primary promotion/removal, authorization filtering, live WorkItem responsibility, duplicate handling, revision conflicts, immutable events, no automatic document attachment, explicit Workstream and authorized creation lookups, API routing/JSON shapes, and atomic invalid-context rejection.

`MATTER_CONTEXT_POSTGRES_TESTS=1` enables the PostgreSQL migration/concurrency regression. It creates and cleans a uniquely named isolated schema on local PostgreSQL, applies the full migration chain, checks row-lock concurrency, primary uniqueness, rollback, and SQL Court projection. It does not migrate the application's existing schema.

Verified results: the full .NET test assembly passed 881/881 with both PostgreSQL flags enabled, using serial test collections and a bounded test heap. After the final Court relink compatibility adjustment, all 64 Matter foundation/workspace regressions passed with PostgreSQL enabled. The final build succeeded; its three existing xUnit analyzer warnings are in unrelated Award extraction tests. EF reported no pending model changes. Compiler builds ran separately without the test heap limit.
