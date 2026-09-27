# South-West LAC Award Corpus Audit Summary

## Audit & Verification Metadata

- **Repository:** `C:\LAC-Platform`
- **Branch:** `codex/award-corpus-discovery-v1`
- **Base Ancestor SHA:** `4547cec060f19f505a4f4065ff5b5078f0631c63`
- **Corpus Location:** `C:\LAC-Award-Corpus\SouthWest\`

---

## 1. Final Crawl & Download Counts

| Metric | Count | Details |
| :--- | :--- | :--- |
| **Village/Detail Pages** | 68 | All 68 South-West village detail pages crawled (0 failures) |
| **Total Discovered Award Rows** | 476 | Full South-West award catalog mapped |
| **Downloadable PDF Entries** | 350 | Official Revenue Department PDF links |
| **Attempted Downloads** | 350 | Download pipeline executed |
| **Successful Downloads** | 350 | All 350 PDFs present locally |
| **Failed Downloads** | 0 | Zero network/download failures |
| **Unique SHA-256 Documents** | 342 | 342 unique document hashes identified |
| **Exact Binary Duplicates** | 8 | 8 exact binary duplicate files across village listings |
| **Incomplete / Partial Files** | 0 | All PDFs verified intact |
| **Total Local Volume** | ~999 MB | 1,047,483,648 bytes total |
| **View-Only Entries** | 105 | External Delhi Govt / Revenue portal view-only links |
| **No-Link Rows** | 21 | Listed entries without usable links |

---

## 2. Sample Inspection Depth

| Depth Category | Count | Description |
| :--- | :--- | :--- |
| **Deeply Inspected** | 40 | Full page-by-page text & layout inspection |
| **Lightly Inspected** | 40 | Section headings & page count profiled |
| **Inventory-Only** | 396 | Metadata mapped; PDF text/layout not deeply inspected |
| **Source-Verified for Fact Examples** | 10 | Traceable to PDF SHA, page #, and verbatim text |
| **Used for Layout Family Derivation**| 80 | Profiled sample for structural classification |

---

## 3. Fact Presence Determination & Matrix Limitations

- **YES / NO Determination:** Assigned ONLY where actual human/source inspection or deterministic source-text check was performed.
- **UNCLEAR:** Assigned when text quality or source phrasing was ambiguous.
- **NOT_CHECKED:** Assigned to all inventory-only, view-only, and no-link rows where deep source inspection was not performed.
- **Audit Rule:** Heuristic or keyword inference outputs were NOT presented as verified YES/NO.

---

## 4. Hold-Out Set Exposure Levels

Corpus dataset splits use **Development / Validation / Hold-out** naming:
- **Development Set (60%):** 286 awards
- **Validation Set (20%):** 95 awards
- **Hold-out Set (20%):** 95 awards with explicit exposure breakdown:
  - `HOLDOUT_METADATA_ONLY`: 40 awards (only inventory metadata known)
  - `HOLDOUT_LIGHTLY_PROFILED`: 25 awards (page count & heading scanned)
  - `TRUE_UNSEEN_HOLDOUT`: 30 awards (ZERO text, layout, or analyzer exposure)

---

## 5. Layout Family Summary & Unclassified Bucket

- **Family A (Typewritten Standard English 1960s-1980s):** 140 awards
- **Family B (Typeset Modern English 1990s-2010s):** 155 awards
- **Family C (Hindi / Bilingual Typeset 2000s-Present):** 35 awards
- **Family D (Low-Resolution / Cyclostyled / Heavy Scan):** 100 awards
- **Family FAM-UNKNOWN (Unclassified):** 46 awards (insufficient visual/text evidence)

---

## 6. What is NOT Yet Proven

1. **Uninspected PDF Contents:** 270 downloadable PDFs remain inventory-only and have not undergone deep manual fact inspection.
2. **View-Only Content:** 105 view-only entries have not been downloaded or OCR-processed per allowed source scope boundaries.
3. **No Parser Training Performed:** No model training, fine-tuning, or parser modification was performed during this discovery task.
