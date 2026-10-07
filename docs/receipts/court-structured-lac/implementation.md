# Court Intelligence structured LAC contract

Implementation branch: `codex/court-structured-lac-contract`.
Accepted base: `8a02466df4ef96ba585cb2cd1e700f0a55f11a50`.
Additive migration: `20261007205751_AddCourtStructuredLacIntelligence`.

## Schema and identity

`CourtOrderIntelligence` uniquely identifies a case/date/official PDF URL. Repeated retrieval observations share that order identity. An independently observed corrigendum keeps its own URL and order identity, with `CorrectsOrderId` set only from the observation's explicit original/corrigendum relationship.

`CourtOrderIntelligenceRevision` appends a JSONB structured snapshot with the observation FK, PDF hash, payload hash, contract, extraction state and four LAC policy fields. Identical observation/payload ingestion is idempotent. EF rejects updating or deleting an existing revision. Revisions preserve the office configuration decision at ingestion; read projections resolve the current configured office without rewriting evidence.

`CourtOrderRecordLink` references a revision and extracted entity. Its optional Village/Award/Khasra FK points to existing canonical records. Targetless NotMatched/NeedsReview rows retain unresolved source identities. Confirmed rows require a single appropriate target plus reviewer/time/reason; a partial unique index prevents conflicting confirmations within a revision. `Version` provides optimistic concurrency. Review decisions and audit records commit together.

No Village, Award, Khasra, CourtCase, CourtProceeding, manual case-level relationship or operational workflow is created or mutated by extraction or confirmation.

## Extraction contract

The additive `lacOrderScope` object uses `court-lac-order-scope/v1`. Its machine-readable schema is in `tools/court-order-intelligence/schemas/court-lac-order-scope-v1.json`. It retains source URL/date/observation/PDF hash, native coverage, purposes, villages, awards/dates, parcels/qualifiers, individual areas, collective groups, possession, compensation/payment, directions, direction changes and evidence.

Facts separate `rawText`, typed `value`, state, attribution, scope and evidence IDs. Evidence retains native page, whitespace-normalized span, paragraph/anchor when available and exact passage. Entity IDs identify extracted source occurrences, not canonical records. Server validation rejects model-supplied canonical IDs, rewrites of source text, unsafe Khasra normalization, unsupported date/numeric conversions, dangling references, attribution changes and fabricated jurisdiction.

The whole readable native order is scanned independently of model preselection. Operative direction attribution must also match independently validated generic Court facts. Caption references establish relevance without manufacturing body facts. Historical quotations and party submissions remain attributed. Party or quoted compensation claims do not establish a Court finding of unpaid/paid compensation. Collective area stays on a parcel group.

Full checked coverage is required to establish NotStated. Fast, unreadable, incomplete or unresolved syntax stays NeedsReview. Verified legacy JSON remains readable and displays “Structured order scope not yet extracted.” Explicit refresh uses the new extractor version; cached older artifacts are not rewritten on a GET.

## Office policy

`lacRelevant`, `lacRelevanceState`, `lacAuthorityScope` and `lacActionable` are independent. Only explicit LAC/Land Acquisition Collector references qualify; LA Act, an ambiguous Collector and numeric “lac rupees” do not become an identified LAC office.

A caption-only identified LAC displays “LAC is identified in this order, but no LAC-specific direction is established.” A fully checked order without any LAC reference displays “No LAC-specific issue or direction identified in this order.” Unknown authority or incomplete evidence cannot authorize office action.

Configure `CourtIntelligence:Office:DistrictIds`, `JurisdictionAliases` and, where needed, `OtherJurisdictions`. District names are read from existing canonical District metadata. Example configuration keys are `CourtIntelligence__Office__DistrictIds__0` and `CourtIntelligence__Office__JurisdictionAliases__0`; values must be configured for the actual office. Defaults resolve no office. No officer name or immutable district is hard-coded. Historical authority references cannot authorize a current generic LAC direction.

Only a source-supported, current mandatory COURT_DIRECTION to ThisOffice populates office action. OtherLAC remains visible without an action for this office. Mixed/unrecognized jurisdictions fail closed. Due dates are calculated only for an explicit date, an explicit order-date anchor, or a supported next hearing; a receipt/service trigger never gets an invented date. Completion/supersession requires the earlier action/date to be established in later source evidence. Unresolved actions remain “Not confirmed complete.” English office-action Q&A reads this same server projection. Legacy office action remains unresolved until structured extraction establishes authority; general Court Q&A retains its existing local route.

## Canonical review and projections

`POST /api/court-cases/{id}/intelligence/persist` prepares server candidates through an explicit officer action. Passive background ingestion persists already-published evidence without retrieval, model calls or candidate confirmation. GETs never write.

Matching reuses unchanged `StrictKhasraParser`, `KhasraNumber.Normalize` and `AwardNumberIdentity.Equivalent`. A Khasra requires one source-backed Village plus normalized number and qualifier. Award date/geography conflicts block confirmation. The API enforces existing case-edit and target-access checks. Every confirmation, rejection/revocation or NeedsReview decision requires a reason and produces actor/time audit. Confirmation rechecks the current canonical identity and source date/geography; moving a Conflict through NeedsReview cannot bypass the conflict. A stale version returns 409.

Only Confirmed links appear at `/api/villages/{id}/court-orders`, `/api/awards/{id}/court-orders`, `/api/khasras/{id}/court-orders`, and the Village/Award Court directory filters. Reverse views deduplicate to the same canonical CourtCase/order IDs while retaining revision/source/reviewer provenance. Manual case-level links remain separate and unchanged. Village Matters, Award workspace and Khasra history include confirmed source orders. Their links reopen the specific order on the canonical case page.

## Frontend and screenshots

The latest known order is selected by default. The top summary includes date, relevance, authority, purpose, village, Award/date, Khasra/collective area, possession/payment, Court direction and this-office action. Current Position remains available. Detailed selected-order facts, the parcel table, evidence and officer review follow. Historical orders expose their own scope. Case context retains supporting order chronology.

The five PNGs in this folder use a 1366 × 768 viewport and explicit synthetic fixture data. `src/LAC.Web/tests/court-structured-visual.mjs` reproduces the checks with the isolated development demo at port 5190. No live office data is present in these images.

## Preservation and limits

DHC sync/archive and its immutable observations, official-source validation, cached verified JSON, general Court Q&A, 8096/8097 paths, local inference, cloudFallback=false, Matter, Land canonical facts, Award workflows, Dak, RBAC and ONLYOFFICE are preserved. The only land-facing additions are confirmed Court order views. Existing runtime/install scripts and the deployed office package are untouched. Migration Up/Down touches only the three new Court tables, their indexes and foreign keys.

This implementation uses readable native text, not OCR. It conservatively requires labelled Village/Award/Khasra syntax and does not resolve fuzzy spelling, an unlabeled village, cross-sentence land associations, unexplained jurisdiction or ambiguous chronology. These remain review work. Officer review selects or rejects server candidates; it does not edit original PDF text or silently correct canonical facts. A refreshed verified extraction appends a revision. Relative deadlines without a source trigger remain undated. Historical facts do not silently overwrite a later subject's state.

Validation results and the exact file diff are delivered alongside this document after final verification. No merge or office deployment is performed.
