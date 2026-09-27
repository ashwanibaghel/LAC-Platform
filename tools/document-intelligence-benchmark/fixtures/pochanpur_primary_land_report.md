# Pochanpur primary awarded-land schedule: source audit and fresh extraction

Source: the 37-page Pochanpur Award PDF, SHA-256
`72aad9ddfe77ec11df63a0cf79e5250ae39af90b53d389b5d42b837c467b3be9`.
The source-row inventory is in `pochanpur_primary_row_regions.json`; visually
transcribed examples are in `pochanpur_primary_land_manifest.json`. Machine
output was not used as ground truth.

## Printed layout

PDF page 2 introduces **Land Awarded**. Pages 2–6 repeat three side-by-side
schemas with printed columns **RecNo**, **Khasra**, **Total Area**, **Area
Awarded**. RecNo values are separate group-heading lines, not a per-parcel
cell. Khasra entries include `min`, east/west qualifiers and multi-part
subdivisions; areas use bigha–biswa pairs, occasional `.5`, and printed double
dashes. The header wording stays the same across continuation pages. Page 6
ends with a handwritten total of 1497-05. Some OCR cells are blank or
damaged; there is no prose interleaved into the three printed grids.

PDF pages 18–23 instead show Annexure B, **Land Awarded And Taken Over**, with
two repeated Khasra/Total Area/Awarded Area schemas. They describe possession
and must not be interpreted as the main awarded-land schedule.

## Before and after

| Measure | Before | After |
| --- | ---: | ---: |
| Physical primary rows on pages 2–6 | 707 | 707 |
| Primary `AwardKhasra` suggestions | 0 | 640 |
| Annexure B `AwardKhasra` suggestions | 279 | 0 |
| Primary source rows needing identifier review | 707 | 67 |
| Possession source rows preserved as non-actionable evidence | not separately counted | 387 |
| `PossessionEvent` | 2 | 2 |
| `Claim` | 163 | 163 |
| `CourtCase` | 6 | 6 |
| `ValuationRule` | 7 | 7 |

Primary physical rows by PDF page: 2: 133, 3: 145, 4: 148, 5: 146,
6: 135. Primary suggestions by page: 2: 115, 3: 143, 4: 135,
5: 134, 6: 113. All 640 suggestions require individual review; 13 lack
one or more source-cell regions and 60 recorded-area / 79 awarded-area
normalizations are missing. No value is copied across cells, groups or rows.

The 67 unstaged rows keep raw OCR and cell regions in page-level review
evidence. The 387 Annexure B geometry rows likewise keep raw cell and region
evidence. The two existing structured possession events remain, but no
per-Khasra possession linkage is claimed. These 387 evidence rows are not a
one-to-one conversion of the old 279 suggestions.

## Ground-truth check

The source fixture records 707 physical row positions. 56 distinct printed
rows were visually transcribed across all five pages, including the first ten,
the final ten, page transitions, repeated `min`, and unusual subdivisions.
The other 651 row *values* remain `REVIEW`; the printed row counts were checked
separately. On the 56 examples, 48 identifiers matched exactly and 8 were
missing or wrong. Normalized recorded area matched 40/56, normalized awarded
area 43/56; raw OCR area matched 44/56 and 46/56 respectively. The strict
normalizer intentionally rejects some fractional/ambiguous readings that are
legible in the raw OCR.

Source-position comparison found 640 distinct candidate-to-row matches, 67
positions without a candidate, 0 candidate positions outside the source-row
inventory, and 0 duplicate candidate positions. These geometric results do
not prove that all 640 Khasra strings or area associations are semantically
correct; the full population was not manually transcribed. No 100% accuracy
or zero semantic false-positive claim is made.

The full fresh run completed 37 pages with 875 total candidates:
`AwardCore` 1, `AwardVillage` 1, `Notification` 3,
`UnmappedAwardFinding` 44, `PossessionEvent` 2, `AwardKhasra` 640,
`Claim` 163, `CourtCase` 6, `LandClassification` 1,
`ValuationRule` 7, `SupplementaryMatter` 1, `CompensationRule` 6.
Claim source serials remain 1–163. The first Court row remains 4721/2002,
type CWP. The old parser missed the unruled primary grid because table
geometry did not yield its required column cells, while the ruled possession
annexure matched the broad Khasra/area header signature.

The new classifier requires an explicit section heading plus complete printed
header roles for the first page. A continuation may inherit only the schema
when the same header count and normalized x-ranges match and no different
section heading appears. The candidate retains page, table, row, logical
group, each cell region, raw OCR, and source-section interpretation. Unknown
land grids are not promoted to authoritative `AwardKhasra` suggestions.

## Remaining limits

OCR misses or misreads some identifiers and areas; the 67 unresolved rows and
all 640 suggestions still need human verification. The 56-row fixture is a
representative check, not exhaustive transcription of every repeated,
uncertain, or OCR-disagreeing source entry. Printed fractional areas do not
all normalize under the existing strict area rules. The source-context
classifier supports the proven repeated-header layout; other Award layouts
need their own evidence and tests before they can be promoted.
