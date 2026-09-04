# Award ingestion pipeline

All Award data inputs use the same safe route: an input adapter produces typed candidate contracts, candidates are saved in durable staging, canonical matching and validation produce a review preview, and an explicit human-selected commit invokes canonical services.

`AwardIngestionSession` and `AwardIngestionCandidate` are staging records only. They may retain source locator, raw extraction text, and confidence, but none of that extraction metadata is copied to the operational Award, Khasra, Party, Claim, or court tables.

The strongest invariants are:

- Preview never writes canonical data or audit entries.
- Extractors—including future PDF, OCR, and AI extractors—must implement the candidate contract and never write canonical entities directly.
- Khasra matching is Village-scoped and uses normalized number plus qualifier; display text is not identity.
- A missing Award Khasra creates one canonical Village Khasra only at reviewed commit time, then adds an Award link and review flag.
- Village master area, Award recorded area, Awarded area, possession area, and claim area remain distinct.
- Conflicts and ambiguity are preserved for review; confidence never auto-commits.
- Selected candidates commit transactionally and are revalidated immediately before commit. Sessions can become partially committed while unresolved candidates remain in staging.

The present foundation supports typed storage/review for the Award-domain contracts, plus transactional Award-Khasra and Notification/AwardNotification commit. An exact notification match is reused; multiple exact matches remain ambiguous rather than guessed. A source Document is linked once to the Award only when a reviewed session is committed. PDF parsing, OCR, AI extraction, and automated document classification are deliberately not implemented.
