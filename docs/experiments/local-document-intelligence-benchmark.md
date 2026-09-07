# Local document-intelligence benchmark

## Purpose

This is a read-only, offline benchmark of local open-source document-intelligence
engines on a real Award PDF held only on the pilot machine. It does not alter the
production Award extraction pipeline, canonical records, source evidence, or
human-review flow. Real outputs, page images, OCR text, names, and identifiers are
stored under an ignored local directory and are never committed.

## Candidate engines and licensing

| Engine | Intended role | License | Benchmark state |
| --- | --- | --- | --- |
| PaddleOCR / PP-StructureV3 | OCR, page layout, table structure | Apache-2.0 | Initial local CPU test in progress |
| Docling | Reading order, hierarchy, structured document output | MIT | Run only if Paddle evidence is weak/incomplete |
| Table Transformer | Table detection and cell/grid fallback | MIT | Run only if Paddle/Docling table reconstruction is weak |
| TrOCR | Optional targeted handwriting comparison | See model-specific license before use | Not yet justified |

Licenses must be reconfirmed against each downloaded model artifact, not only its
source repository. Model files are not committed.

## Privacy controls

- The input is opened only from a local path.
- Inference is local CPU inference; hosted inference and cloud OCR are prohibited.
- Telemetry environment switches are set where the library supports them.
- Network access is allowed only for dependency/model download during setup, never
  for document inference.
- The benchmark has no database write path.

## Neutral model

Each adapter emits an engine-independent page representation:

```text
DocumentPage
  pageNumber, width, height
  blocks[]: Heading | Paragraph | Table | Image | Other
  words[]: text, boundingBox, confidence?
  tables[]: boundingBox, rows, columns, cells[]
```

The deterministic LAC interpretation pass consumes only this normalized form. It
does not correct numbers, create master data, equate a claimant with an owner, or
infer payment/stay/possession from weak wording.

## Representative sample protocol

The sample includes an identity page, Khasra area table, area reconciliation,
court/CWP table, land classification table, claim table, valuation narrative, and
stay-related narrative when they occur in the local Award. Exact page numbers and
all real content stay in the ignored local report.

## Results

### Pilot result: RapidOCR + Table Transformer

The three-page CPU pilot completed entirely locally. RapidOCR is retained as the
word/box/confidence provider; Table Transformer runs in its own Python environment
with Transformers 4.57.1, Torch 2.14 CPU, and timm 1.0.29. The detector runs first,
then the structure model runs on each detected table crop. A word is assigned only
when its centre lies in exactly one detected row and exactly one detected column.

| Sample | Detected structure | Strict OCR-to-cell join | Interpretation outcome |
| --- | --- | --- | --- |
| Dense Award Khasra table | 1 table; 56 rows; 9 columns | 316 populated cells; 638 assigned words; 88 uncertain words | Repeated Khasra / Total Area / Area Awarded column triplets have usable geometry. OCR text still requires conservative candidate validation. |
| CWP and classification page | 2 table regions | Classification: 12 rows x 6 columns, 0 uncertain words. CWP: 7 rows x 5 columns, 2 uncertain words. | CWP-to-Khasra-to-area/status and Khasra-to-area-to-Block relationships have row/cell geometry. The claims table was not independently detected on this page, so claimant relationships remain Needs Review. |

The joiner does not repair OCR digits, merge a claimant into a Party, infer a stay
from a CWP, infer possession, or create canonical data. Where a table cell lacks
clear OCR or geometry, it remains unassigned for human review.

### Other engine status

- PaddleOCR / PP-StructureV3: Windows CPU inference repeatedly failed before page
  output with `ConvertPirAttribute2RuntimeAttribute ... pir::ArrayAttribute<pir::DoubleAttribute>`.
  It is recorded as incompatible with this pilot environment and is not blocking the
  benchmark.
- Docling: its earlier pilot reached local OCR model setup but its required layout
  model download stalled as a zero-byte incomplete cache artifact. A separate
  Docling environment is reserved. It was not retried after Table Transformer met
  the pilot stop condition.

Metrics distinguish observed geometry from trusted legal facts; no unverified
record count or relationship is promoted to canonical data.

### Full Award read-only run

The real 37-page local Award was processed with RapidOCR (37/37 pages, all OCR
local). Conservative Table Transformer candidates were then processed on pages
1–8 and 16–23; page-level geometry and strict joins are in the ignored
`tools/document-intelligence-benchmark/real-output` directory. The generated
`award-structured-draft.json` contains 987 evidence-linked candidates, all
`NeedsReview`, including 945 conservative Khasra-like tokens. No canonical
database write was performed. Combined page OCR timings were approximately
389.66 seconds. Peak model RAM was not instrumented by the runner, so it is not
reported as an invented value.

### Phase 3 master validation

The local API was started against the user-configured local PostgreSQL connection
for read-only queries and then stopped. Pochan Pur resolved to 261 canonical
Khasras; all had master areas and 3 carried a `min` qualifier. Role-aware v3
validation measured 865 Award-table occurrences, 451 distinct strict identities,
189 Award-table exact master matches, 61 qualifier mismatches, 630 no-match
occurrences, and 87 unreadable slash-bearing fragments. SafeExact remains 0
because all identity, header/area, OCR certainty, and conflict gates are not yet
simultaneously satisfied. Claims remain NarrativeOnly/NeedsReview. No canonical
write was performed.
