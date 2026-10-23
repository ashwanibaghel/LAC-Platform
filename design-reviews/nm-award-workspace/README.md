# LAC Platform — Naksha Muntazamin (NM) & Award Workspace UI Design

**Repository**: `ashwanibaghel/LAC-Platform`  
**Branch**: `anti/nm-register-workspace-ui`  
**Target Viewport**: 1366 × 768 (High-density officer data desktop)

This directory contains the complete officer-facing UI design, interactive prototype, and visual evidence suite for the **Naksha Muntazamin (NM) Register** and **Award Master Workspace**, featuring a 4-tier financial hierarchy and tri-modal data entry capabilities (PDF-assisted extraction, manual entry wizards, and multi-sheet Excel bulk ingestion).

---

## Directory Structure

```
design-reviews/nm-award-workspace/
├── README.md                      # Operational guide and interaction manual (this file)
├── nm-ui-design-report.md         # Full design specification and 16-state audit report
├── prototype/
│   ├── index.html                 # Complete interactive HTML shell
│   ├── style.css                  # High-density CSS architecture
│   ├── app.js                     # 16-state controller and validation engine
│   └── capture.js                 # Automated Playwright 1366x768 capture suite
└── screenshots/                   # All 16 captured states at native 1366x768
    ├── 1-collapsed-khatas.png
    ├── 2-expanded-khata-4.png
    ├── 3-claimant-financial-details.png
    ├── 4-item-search.png
    ├── 5-mixed-land-class.png
    ├── 6-red-handwritten-remarks.png
    ├── 7-mismatch-review-state.png
    ├── 8-nm-entry-actions.png
    ├── 9-manual-nm-khata-schedule.png
    ├── 10-manual-nm-new-schedule.png
    ├── 11-manual-nm-claimant-financial.png
    ├── 12-excel-template-download.png
    ├── 13-excel-import-preview-valid.png
    ├── 14-excel-import-conflicts.png
    ├── 15-award-manual-entry.png
    └── 16-award-excel-import.png
```

---

## How to Open and Operate the Prototype

### Option 1: Direct File Open (Zero Dependencies)
Open [`prototype/index.html`](./prototype/index.html) directly in any modern desktop browser (Chrome, Edge, Firefox, Safari):
- Double-click `prototype/index.html` in your file explorer, or
- Right-click &rarr; *Open With* &rarr; *Google Chrome* / *Microsoft Edge*.

### Option 2: Local Static Server
From the repository root:
```bash
# Using Node / npx
npx serve design-reviews/nm-award-workspace/prototype

# Or using Python 3
python -m http.server 8080 --directory design-reviews/nm-award-workspace/prototype
```
Navigate to `http://localhost:8080` (or the printed port) in a browser window sized to **1366 × 768**.

### Option 3: Headless Re-Capture Script
To re-run the Playwright automated screenshot suite:
```bash
node design-reviews/nm-award-workspace/prototype/capture.js
```
This launches headless Chromium at 1366 × 768, walks through all 16 states, and writes updated PNG captures to `./screenshots/`.

---

## Operating the Interactive Interface

### 1. The 16-State QA Bar (Bottom Ribbon)
At the bottom of the screen, an interactive toolbar provides 16 quick-switch buttons corresponding to every design state:
1. **1. Collapsed**: Collapsed Khatas view showing total claimants, stated area, and schedule count.
2. **2. Exp. Khata 4**: Expanded Khata showing nested `Schedule 1` header with constituent Khasras and claimant rows with explicit `-do-` inheritance badges.
3. **3. Details**: Flyout drawer (440px) displaying statutory financial component breakdowns for Item 10.
4. **4. Search**: Direct query test searching for `Item 25`.
5. **5. Mixed Soil**: Multi-schedule display for Khata 7 (Rosli Block A @ ₹4,65,000/B vs Dakar Block B @ ₹3,95,000/B).
6. **6. Red Notes**: Red-ink archival remarks inspector with legal advisory.
7. **7. Scope Review**: Khata 104min multi-schedule scope audit (Schedule A Gram Sabha vs. Schedule B Co-sharers).
8. **8. Entry Actions**: Discoverable primary action ribbon (`+ Add Entry`, `📥 Import Excel`, `📄 Download Template`, `⚡ Extract PDF`).
9. **9. Manual: Khata/Sched**: Step 1 (Khata selection) & Step 2 (Schedule Group selection) in the 540px entry drawer.
10. **10. Manual: New Sched**: Step 2B configuring a new multi-Khasra schedule grid with real-time extent summing.
11. **11. Manual: Claimant**: Step 3 claimant financial entry with live LAA 1894 statutory calculation and "Save Draft & Add Next".
12. **12. Excel Template**: Modal dialog showing normalized 3-sheet structure (`NM_Items`, `Khasra_Schedules`, `Instructions_ValidValues`).
13. **13. Import Valid**: Staging validation modal with 32 valid rows grouped by Khata/Schedule in `DRAFT / PENDING REVIEW` state.
14. **14. Import Conflicts**: Error matrix showing quarantined rows (duplicate Item #10, unknown schedule `SCHED-Z`, zero denominator `0/0`, invalid Biswa `5-22-00`) with downloadable CSV error report.
15. **15. Award Manual**: Award Master workspace with manual Khasra entry drawer and real-time Village Master cross-check.
16. **16. Award Import**: Award Khasra bulk import modal validating parcels against Village Master boundaries.

### 2. Workspace Switching
Use the top-right toggle buttons (`NM Register` / `Award Master`) to alternate between the financial claimant ledger and the cadastral Award parcel master.

### 3. Keyboard Navigation
- **`/` Key**: Instantly focuses the primary search field.
- **`Escape` Key**: Closes any open modal dialog or flyout drawer.
- **`Tab` / `Shift+Tab`**: Cycles through form inputs in the manual entry wizard.

---

## Core Business Logic & Legal Hierarchy

The interface enforces the strict 4-tier domain hierarchy discovered during field audits:
$$\text{Award} \longrightarrow \text{Khata} \longrightarrow \text{Khasra Schedule Group} \longrightarrow \text{Individual Claimant Items}$$

1. **No Shared Khata Conflation**:
   A single Khata (e.g. Khata 104min) can contain both public utility lands (Gram Sabha 1/1 share) and proprietary body co-sharer holdings (163 claimants with fractional shares). Constituent Khasras are listed **once per Khasra Schedule Group**, never in the Khata header.
2. **Explicit `-do-` Provenance**:
   Archival ditto marks (`-do-`) are preserved for historical authenticity but visibly resolve their exact schedule and share: `[↳ Schedule B (12/20660)]`.
3. **Safe Staging Guarantee**:
   All manual submissions and bulk Excel uploads stage into a `DRAFT (Unverified)` quarantine mode. No active database records or cadastral boundaries in the Village Master are directly mutated.

---

## Privacy & Synthetic Data Notice

All personal names, claimant titles, parentage relations, and officer designations shown in the prototype and screenshots are **100% synthetic demo personas** (e.g. `Anand Verma (Demo)`, `Rohan Kapoor (Demo)`, `Demo Officer`). Zero actual citizen PII or confidential court-restricted records are present in this repository.
