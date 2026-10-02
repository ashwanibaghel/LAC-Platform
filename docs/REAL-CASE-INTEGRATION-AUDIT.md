# Real registered Court matter integration audit

Baseline: `0cea02e9b3c1d0fa7623ffb1f69e9681fa71279e`.

Already present: CourtCaseWorkspace renders CourtIntelligence with the actual
CourtCase.Id, keyed by that ID. Both intelligence endpoints perform case-aware
authorization. Ask reads CourtExternalOrderObservations without tracking and
passes case ID, canonical case number and known order metadata to literal
loopback port 8097. The Python runtime has local-only inference, official DHC
URL checks, case/date/caption checks, temporary PDF cleanup, evidence/semantic
validation, atomic JSON writes and exact-date lazy retrieval. Structured view
does not require a live question model. No new visual component is needed.

Missing/incomplete boundaries:

- Generation is manually driven by a jobs JSON; no authorized registered-case
  refresh action connects the persisted order index to the existing processor.
- GET only reads a file. Known-but-unprocessed official sources are not exposed
  until an Ask merges metadata; there is no source-index-driven empty state.
- The artifact reader checks version/GUID but not display structure/evidence
  binding to that case's known sources. Wrong shape can crash the component.
- Existing known-order merge can retain sources absent from the supplied index.
- Q&A response is accepted based only on presence of a claims array; the API
  should validate its case identity and citations against the scoped artifact.
- Fetch abortion and a case-ID ref offer partial navigation protection; Ask
  finally-state and same-ID return races need an explicit request-generation
  guard and cleared state.
- The development demo is already separate. It must stay separate, not be
  copied into production, and needs a routing regression test.

Implementation: one explicit authorized refresh endpoint, using the existing
loopback runtime and one sequential filesystem-locked case refresh; no database
job/schema. Known DB metadata drives every source. GET remains passive. Strict
artifact/source validation and conservative missing/processing/coverage states
keep the Court workspace usable. All proof uses fixtures; no live DHC action,
canonical mutation, office deployment, training change or adapter promotion.
