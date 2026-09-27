# South-West Delhi LAC Award Corpus Discovery Report

## Executive Summary
This report summarizes the comprehensive discovery and inventory pass for the **South-West District Land Acquisition Cell (LAC) Awards** conducted under the official allowed Revenue Department source scope.

All discovered metadata and PDFs remain strictly outside Git and outside application document storage, residing in `C:\LAC-Award-Corpus\SouthWest\`.

---

## 1. Discovered State & Scope Summary

- **Total South-West Villages / Detail Entries:** 68
- **Total Discovered Award Rows:** 476
- **Downloadable PDFs:** 350 awards (~999 MB total declared size)
- **View-Only Entries:** 105 awards (External Delhi Govt / Revenue portal links recorded as view-only)
- **No-Link Rows:** 21 awards (Recorded without usable links)
- **Detail Crawl Health:** 68 detail pages crawled with **0 detail failures**.

---

## 2. Business Fact Presence Analysis

The business fact presence matrix (`award_business_fact_matrix.csv`) evaluates source-grounded presence across all 476 Awards with strict discrete states:
- `YES`: Fact is explicitly present and extractable in the source text.
- `NO`: Fact is confirmed absent in the source document.
- `UNCLEAR`: Fact presence is ambiguous or partially obscured by document quality.
- `NOT_CHECKED`: Reserved for View-Only / No-Link entries or uninspected PDFs where payload was not directly source-inspected.

Key Fact Coverage across Deeply Inspected PDFs:
- **Sec 4 Notification Date/Ref:** ~90% YES
- **Sec 6 Declaration Date/Ref:** ~87.5% YES
- **Sec 9 Public Notice Details:** ~80% YES
- **Total Acquired Land Area:** ~100% YES
- **Compensation Rate per Bigha:** ~83% YES
- **Khasra Schedule Table:** ~100% YES
- **Khatauni / Ownership Schedule (ENM):** ~25% YES, ~25% UNCLEAR
- **Possession Date:** ~33% YES

---

## 3. Heading Catalog & Typography

Observed structural section headings across historical English and Devnagari Award formats include:
- **Preamble:** `BEFORE THE LAND ACQUISITION COLLECTOR, SOUTH WEST DISTRICT, DELHI`
- **Notifications:** `NOTIFICATION UNDER SECTION 4`, `DECLARATION UNDER SECTION 6`
- **Claims:** `NOTICES UNDER SECTION 9 & 10`
- **Valuation:** `MARKET VALUE OF LAND`, `DETERMINATION OF COMPENSATION`, `SOLATIUM U/S 23(2)`
- **Schedules:** `STATEMENT OF LAND UNDER ACQUISITION`, `KHASRA WISE AREA STATEMENT`
- **Apportionment:** `APPORTIONMENT OF COMPENSATION`, `NAQSHA MUTZAMIN (ENM)`

---

## 4. Layout Family Typology

The corpus is categorized into 4 structural layout families + 1 unclassified family:
1. **Family A: Typewritten Standard English (1960s-1980s)** — 140 awards (29.4%)
2. **Family B: Typeset Modern English (1990s-2010s)** — 155 awards (32.6%)
3. **Family C: Hindi / Bilingual Typeset (2000s-Present)** — 35 awards (7.4%)
4. **Family D: Low-Resolution / Cyclostyled / Heavy Scan** — 100 awards (21.0%)
5. **Family FAM-UNKNOWN: Unclassified** — 46 awards (9.6%)

---

## 5. Frozen Corpus Dataset Splits

Corpus dataset splits have been frozen under the **Development / Validation / Hold-out** naming convention:
- **Development Set (60%):** 286 awards
- **Validation Set (20%):** 95 awards
- **Hold-out Set (20%):** 95 awards (Includes 30 TRUE_UNSEEN_HOLDOUT awards kept genuinely unseen for future generalization testing)

---

## Deliverables Index
All small reproducible research artifacts are committed to `tools/document-intelligence-benchmark/corpus/southwest/`:
1. `southwest_award_inventory.csv` & `southwest_award_inventory.json`
2. `award_business_fact_matrix.csv`
3. `award_heading_catalog.json`
4. `award_fact_examples.json`
5. `award_layout_families.json`
6. `award_corpus_split.json`
7. `corpus_discovery_report.md`
8. `corpus_audit_summary.md`
