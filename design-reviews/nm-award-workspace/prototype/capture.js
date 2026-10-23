const path = require('path');
const fs = require('fs');

// Attempt local require or fallback to workspace node_modules
let chromium;
try {
  chromium = require('playwright').chromium;
} catch (e) {
  try {
    chromium = require(path.resolve(__dirname, '../../../src/LAC.Web/node_modules/playwright')).chromium;
  } catch (e2) {
    console.error('Playwright could not be loaded. Please run `npm install playwright` or run from repository root.');
    process.exit(1);
  }
}

const SCREENSHOT_DIR = path.resolve(__dirname, '../screenshots');

if (!fs.existsSync(SCREENSHOT_DIR)) {
  fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
}

const STATES = [
  { id: '1-collapsed-khatas', filename: '1-collapsed-khatas.png', desc: 'NM register with collapsed Khatas showing schedule group counts' },
  { id: '2-expanded-khata-4', filename: '2-expanded-khata-4.png', desc: 'Expanded Khata 4 showing nested Schedule Group 1 and claimant rows with -do- resolution' },
  { id: '3-claimant-financial-details', filename: '3-claimant-financial-details.png', desc: 'Claimant details & financial component breakdown drawer with schedule provenance' },
  { id: '4-item-search', filename: '4-item-search.png', desc: 'Search for Item 25 under Khata 104min Schedule B' },
  { id: '5-mixed-land-class', filename: '5-mixed-land-class.png', desc: 'Khata 7 with distinct soil schedule groups: Rosli Block A & Dakar Block B' },
  { id: '6-red-handwritten-remarks', filename: '6-red-handwritten-remarks.png', desc: 'Red handwritten remarks & archival evidence inspector with administrative legal disclaimer' },
  { id: '7-mismatch-review-state', filename: '7-mismatch-review-state.png', desc: 'Schedule scope & boundary analysis for Khata 104min (Schedule A Gram Sabha vs Schedule B Co-sharers)' },
  { id: '8-nm-entry-actions', filename: '8-nm-entry-actions.png', desc: 'NM register with discoverable primary entry action buttons' },
  { id: '9-manual-nm-khata-schedule', filename: '9-manual-nm-khata-schedule.png', desc: 'Manual NM entry Step 1 (Khata) & Step 2 (Schedule Group selection)' },
  { id: '10-manual-nm-new-schedule', filename: '10-manual-nm-new-schedule.png', desc: 'Manual NM entry Step 2B (Create New Schedule Group with multi-khasra grid)' },
  { id: '11-manual-nm-claimant-financial', filename: '11-manual-nm-claimant-financial.png', desc: 'Manual NM entry Step 3 (Individual claimant financial entry & real-time statutory calculations)' },
  { id: '12-excel-template-download', filename: '12-excel-template-download.png', desc: 'Excel template download dialog with multi-sheet structure and data dictionary' },
  { id: '13-excel-import-preview-valid', filename: '13-excel-import-preview-valid.png', desc: 'Excel bulk import preview with 32 valid rows staged as Draft' },
  { id: '14-excel-import-conflicts', filename: '14-excel-import-conflicts.png', desc: 'Excel bulk import error & conflict matrix with validation blocks and downloadable error report' },
  { id: '15-award-manual-entry', filename: '15-award-manual-entry.png', desc: 'Award Workspace with manual Khasra entry drawer & cadastral cross-check' },
  { id: '16-award-excel-import', filename: '16-award-excel-import.png', desc: 'Award Workspace with Excel bulk import conflict analysis and Village Master reconciliation' }
];

async function captureAll() {
  console.log('Launching headless browser at 1366x768...');
  const browser = await chromium.launch({
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--font-render-hinting=none']
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1
  });

  const page = await context.newPage();
  const fileUrl = 'file:///' + path.resolve(__dirname, 'index.html').replace(/\\/g, '/');
  console.log(`Navigating to ${fileUrl}...`);
  await page.goto(fileUrl, { waitUntil: 'networkidle' });

  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);

  for (const st of STATES) {
    console.log(`Setting state: ${st.id} (${st.desc})...`);
    await page.evaluate((stateName) => {
      window.setDesignState(stateName);
      if (stateName === '7-mismatch-review-state') {
        const container = document.getElementById('tableOuterContainer');
        if (container) container.scrollTop = 110;
      }
    }, st.id);

    await page.waitForTimeout(300);

    const outPath = path.join(SCREENSHOT_DIR, st.filename);
    await page.screenshot({ path: outPath, fullPage: false });

    const stat = fs.statSync(outPath);
    console.log(`  -> Captured ${st.filename} (${stat.size} bytes)`);
  }

  await browser.close();
  console.log('All 16 design states successfully captured at 1366x768.');
}

captureAll().catch(err => {
  console.error('Capture failed:', err);
  process.exit(1);
});
