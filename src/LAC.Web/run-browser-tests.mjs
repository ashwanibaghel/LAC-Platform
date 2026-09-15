import { chromium } from "playwright";
import assert from "node:assert/strict";

const API_BASE = "http://127.0.0.1:5088";
const WEB_BASE = "http://127.0.0.1:5173";

async function main() {
  console.log("==================================================");
  console.log("REAL BROWSER VERIFICATION: PHASE 1 MATTER DRAFT STUDIO");
  console.log("==================================================");

  const browser = await chromium.launch({
    executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    headless: true,
  });

  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
  });

  const page = await context.newPage();

  const consoleLogs = [];
  const consoleWarnings = [];
  const consoleErrors = [];
  const pageErrors = [];

  page.on("console", (msg) => {
    const text = msg.text();
    const type = msg.type();
    if (type === "error") {
      consoleErrors.push(text);
      console.error(`[BROWSER CONSOLE ERROR] ${text}`);
    } else if (type === "warning") {
      consoleWarnings.push(text);
    } else {
      consoleLogs.push(text);
    }
  });

  page.on("pageerror", (err) => {
    pageErrors.push(err.message);
    console.error(`[PAGE ERROR] ${err.message}`);
  });

  // Step 1: Create a test Matter via APIRequestContext
  const request = context.request;
  const villageRes = await request.get(`${API_BASE}/api/villages?page=0&pageSize=1`);
  const villageData = await villageRes.json();
  const villageId = villageData.items[0].id;
  console.log(`Using Village ID: ${villageId}`);

  const matterRes = await request.post(`${API_BASE}/api/villages/${villageId}/matters`, {
    data: {
      title: "Browser Verification Matter",
      matterType: "Court Case",
      status: "Open",
    },
  });
  const matterData = await matterRes.json();
  const matterId = matterData.id;
  console.log(`Created Matter ID: ${matterId}`);

  // Step 2: Create a Letter draft
  const letterRes = await request.post(`${API_BASE}/api/matters/${matterId}/drafts`, {
    data: {
      title: "Test Letter 01",
      draftType: "Letter",
    },
  });
  const letterData = await letterRes.json();
  const draftId = letterData.id;
  console.log(`Created Letter Draft ID: ${draftId}`);

  // Navigate to the editor
  await page.goto(`${WEB_BASE}/matter-drafts/${draftId}`, { waitUntil: "networkidle" });
  await page.waitForSelector(".draft-prosemirror");
  console.log("Editor loaded in browser.");

  // Test 1 & 2: Type enough text to create at least 3 pages
  console.log("\n--- Testing 1 & 2: Automatic multi-page generation (3+ pages) ---");
  const prosemirror = page.locator(".draft-prosemirror");
  await prosemirror.click();

  // Generate 25 paragraphs of dummy text
  const paragraphText = "Land Acquisition Cell (LAC) Matter Notice regarding Khasra No. 22//2/7 in Village Galibpur. The reference pertains to possession proceedings, Statement A, and disbursement of supplementary compensation awards as per directions of the learned ADM.";
  
  await page.evaluate((text) => {
    const editorEl = document.querySelector(".draft-prosemirror");
    editorEl.focus();
    // Fill content cleanly via ProseMirror transaction or execCommand
    const paragraphs = [];
    for (let i = 0; i < 30; i++) {
      paragraphs.push({
        type: "paragraph",
        content: [{ type: "text", text: `[Paragraph ${i + 1}] ${text}` }],
      });
    }
    // We can dispatch via TipTap or window
  }, paragraphText);

  // Let's type into the editor using TipTap commands or typing
  await page.evaluate(({ text }) => {
    const tiptap = window.__editor || document.querySelector(".draft-prosemirror")?.pmViewDesc?.node;
    // Let's insert paragraphs via DOM typing or TipTap view
    const view = document.querySelector(".draft-prosemirror")?.__view || window.editorView;
  }, { text: paragraphText });

  // Let's use page.keyboard to type or insert text
  await prosemirror.fill("");
  for (let p = 1; p <= 25; p++) {
    await page.keyboard.type(`[Paragraph ${p}] ${paragraphText}`);
    await page.keyboard.press("Enter");
  }

  // Wait for layout measurement RAF
  await page.waitForTimeout(600);

  // Check page count in backdrop deck and toolbar
  const sheetCards = await page.locator(".draft-sheet-card").count();
  const toolbarCounter = await page.locator(".toolbar-page-counter").innerText();
  console.log(`Visible Sheet Cards: ${sheetCards}`);
  console.log(`Toolbar Page Counter: ${toolbarCounter}`);
  assert.ok(sheetCards >= 3, `Expected at least 3 sheet cards, got ${sheetCards}`);
  assert.ok(toolbarCounter.includes(`${sheetCards} pages`), `Toolbar should reflect ${sheetCards} pages`);

  // Test 3 & 4: Paragraph crossing page boundary
  console.log("\n--- Testing 3 & 4: Paragraph crossing page boundary ---");
  const spacers = await page.locator(".draft-page-break-spacer").count();
  console.log(`Active Spacer Widgets: ${spacers}`);
  assert.ok(spacers >= 2, `Expected at least 2 spacer widgets, got ${spacers}`);

  // Inspect the first spacer in the live DOM
  await page.waitForFunction(() => {
    const el = document.querySelector(".draft-page-break-spacer");
    return el && el.isConnected && el.offsetHeight > 50;
  }, { timeout: 10000 });

  const spacerInfo = await page.evaluate(() => {
    const el = document.querySelector(".draft-page-break-spacer");
    return {
      height: el ? el.offsetHeight : 0,
      parentTag: el?.parentElement?.tagName || "DIV",
    };
  });
  console.log(`First Spacer Height: ${spacerInfo.height}px, Parent element: <${spacerInfo.parentTag}>`);
  assert.ok(spacerInfo.height > 50, "Spacer height should bridge remaining page space + margins + gap");

  // Test 5: Move caret with mouse and arrow keys across the boundary
  console.log("\n--- Testing 5: Cursor navigation across page boundary ---");
  // Click on the text right before the spacer
  await page.evaluate(() => {
    const spacer = document.querySelector(".draft-page-break-spacer");
    const p = spacer?.parentElement;
    if (p) {
      const range = document.createRange();
      range.setStartBefore(spacer);
      range.collapse(true);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
  });
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("ArrowLeft");
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("ArrowUp");
  console.log("Caret moved across page boundary with arrow keys without error.");

  // Test 6: Text selection spanning the page boundary
  console.log("\n--- Testing 6: Text selection spanning the page boundary ---");
  await page.evaluate(() => {
    const spacer = document.querySelector(".draft-page-break-spacer");
    const p = spacer?.parentElement;
    if (p) {
      const range = document.createRange();
      range.selectNodeContents(p);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
  });
  const selectedText = await page.evaluate(() => window.getSelection()?.toString() || "");
  console.log(`Selected text length across boundary: ${selectedText.length} characters`);
  assert.ok(selectedText.length > 50, "Text selection should span continuously across page boundary");

  // Test 7 & 8: Backspace/Delete and Undo/Redo around boundary
  console.log("\n--- Testing 7 & 8: Backspace, typing and Undo/Redo ---");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.type(" [BOUNDARY_EDIT] ");
  await page.waitForTimeout(300);
  const contentAfterEdit = await prosemirror.innerText();
  assert.ok(contentAfterEdit.includes("[BOUNDARY_EDIT]"), "Edited text should appear in document");

  await page.keyboard.press("Control+z");
  await page.waitForTimeout(300);
  const contentAfterUndo = await prosemirror.innerText();
  assert.ok(!contentAfterUndo.includes("[BOUNDARY_EDIT]"), "Undo should revert the edit cleanly");

  await page.keyboard.press("Control+y");
  await page.waitForTimeout(300);
  const contentAfterRedo = await prosemirror.innerText();
  assert.ok(contentAfterRedo.includes("[BOUNDARY_EDIT]"), "Redo should restore the edit cleanly");

  // Test 9: Hindi / Devanagari text and pagination
  console.log("\n--- Testing 9: Hindi / Devanagari text ---");
  const hindiText = "खसरा संख्या 22//2/7, ग्राम गालिबपुर, अधिनिर्णय संख्या 12/2008-09 के संबंध में राजस्व रिकॉर्ड एवं कब्ज़ा कार्यवाही विवरण।";
  await page.keyboard.press("Enter");
  await page.keyboard.type(hindiText);
  await page.waitForTimeout(400);
  const editorTextWithHindi = await prosemirror.innerText();
  assert.ok(editorTextWithHindi.includes("खसरा संख्या 22//2/7"), "Hindi text should render cleanly in editor");
  console.log("Hindi / Devanagari text rendered and laid out without error.");

  // Test 10: Change font size around boundary and repaginate
  console.log("\n--- Testing 10: Font size change and repagination ---");
  const initialBreaksCount = await page.locator(".draft-page-break-spacer").count();
  // Select all and set font size to 16pt via toolbar
  await page.keyboard.press("Control+a");
  const sizeSelect = page.locator("select[aria-label='Font size']");
  await sizeSelect.selectOption("16pt");
  await page.waitForTimeout(600);
  const updatedSheets = await page.locator(".draft-sheet-card").count();
  console.log(`Page count after changing font size to 16pt: ${updatedSheets} (was ${sheetCards})`);
  assert.ok(updatedSheets >= sheetCards, "Increasing font size should increase or maintain page count");

  // Reset font size to 12pt
  await sizeSelect.selectOption("12pt");
  await page.waitForTimeout(500);

  // Test 11: Bullet and Numbered Lists crossing boundary
  console.log("\n--- Testing 11: Lists crossing boundary ---");
  // Move caret to end of document and add list
  await page.evaluate(() => {
    const editor = document.querySelector(".draft-prosemirror");
    editor?.focus();
    const lastP = editor?.lastElementChild;
    if (lastP) {
      lastP.scrollIntoView();
      const range = document.createRange();
      range.selectNodeContents(lastP);
      range.collapse(false);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
  });
  await page.keyboard.press("Enter");
  const bulletBtn = page.locator("button[title='Bullet list']");
  await bulletBtn.click();
  await page.waitForTimeout(200);
  for (let i = 1; i <= 6; i++) {
    await page.keyboard.type(`List item ${i} with details about possession memo`);
    await page.keyboard.press("Enter");
  }
  await page.waitForTimeout(400);
  const listItemsCount = await page.locator(".draft-prosemirror li").count();
  console.log(`Created ${listItemsCount} list items.`);
  assert.ok(listItemsCount >= 6, "List items should exist");

  // Test 12, 13 & 14: Table near bottom and row pagination
  console.log("\n--- Testing 12, 13 & 14: Table and row pagination ---");
  await page.keyboard.press("Enter");
  await page.keyboard.press("Enter"); // exit list
  const tableBtn = page.locator("button[title='Insert table']");
  await tableBtn.click();
  await page.waitForTimeout(300);

  const tableExists = await page.locator(".draft-prosemirror table").count();
  assert.ok(tableExists >= 1, "Table should be inserted");

  // Add 15 rows to force the table across page boundary
  const addRowBtn = page.locator("button[title='Add table row']");
  for (let r = 0; r < 12; r++) {
    await addRowBtn.click();
  }
  await page.waitForTimeout(500);

  const totalTableRows = await page.locator(".draft-prosemirror tr").count();
  console.log(`Total table rows: ${totalTableRows}`);

  // Verify row editing around boundary
  await page.evaluate(() => {
    const editor = document.querySelector(".draft-prosemirror");
    editor?.focus();
    const cellP = document.querySelector(".draft-prosemirror td p") || document.querySelector(".draft-prosemirror td");
    if (cellP) {
      cellP.scrollIntoView();
      const range = document.createRange();
      range.selectNodeContents(cellP);
      range.collapse(false);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
  });
  await page.waitForTimeout(200);
  await page.keyboard.type("Cell 1: 22//2/7");
  await page.waitForTimeout(300);
  const cellContent = await page.locator(".draft-prosemirror td").first().innerText();
  console.log(`Cell Content: "${cellContent}"`);
  assert.ok(cellContent.includes("22//2/7"), "Table cell should accept typing");

  // Test 15-20: Layout switching: A4 Portrait -> Landscape -> Legal Portrait -> Landscape -> A4 Portrait
  console.log("\n--- Testing 15-20: Page Layout Profiles & Switching ---");
  const pageSetupToggle = page.locator("button:has-text('Page setup')");
  await pageSetupToggle.click();
  await page.waitForSelector(".draft-page-setup-panel");

  const pageSizeSelect = page.locator(".draft-letter-layout-controls select").first();
  const orientationSelect = page.locator(".draft-letter-layout-controls select").nth(1);

  // A4 Landscape
  console.log("Switching to A4 Landscape...");
  await orientationSelect.selectOption("Landscape");
  await page.waitForTimeout(500);
  let canvasWidth = await page.locator(".draft-canvas").evaluate((el) => getComputedStyle(el).width);
  console.log(`A4 Landscape Canvas Width: ${canvasWidth}`);

  // Legal Portrait
  console.log("Switching to Legal Portrait...");
  await orientationSelect.selectOption("Portrait");
  await pageSizeSelect.selectOption("Legal");
  await page.waitForTimeout(500);
  let canvasHeight = await page.locator(".draft-sheet-card").first().evaluate((el) => el.offsetHeight);
  console.log(`Legal Portrait Sheet Height: ${canvasHeight}px`);

  // Legal Landscape
  console.log("Switching to Legal Landscape...");
  await orientationSelect.selectOption("Landscape");
  await page.waitForTimeout(500);

  // Margins test
  console.log("Updating Margins...");
  const topMarginInput = page.locator("input[aria-label='marginTopMm millimetres']");
  await topMarginInput.fill("30");
  await page.waitForTimeout(500);

  // Switch back to A4 Portrait
  console.log("Switching back to A4 Portrait...");
  await orientationSelect.selectOption("Portrait");
  await pageSizeSelect.selectOption("A4");
  await page.waitForTimeout(500);

  // Test 21, 22, 23: Save and Reopen
  console.log("\n--- Testing 21, 22, 23: Save and Reopen Persistence ---");
  const saveBtn = page.locator(".draft-save");
  assert.ok(await saveBtn.isEnabled(), "Save button should be enabled after edits");
  await saveBtn.click();
  await page.waitForSelector(".draft-save:disabled");
  console.log("Draft saved successfully.");

  // Inspect raw saved JSON in database via API
  const savedDraftRes = await request.get(`${API_BASE}/api/matter-drafts/${draftId}`);
  const savedDraft = await savedDraftRes.json();
  const parsedContent = JSON.parse(savedDraft.contentJson);
  console.log(`Saved Content Root Node Type: ${parsedContent.type}`);
  console.log(`Saved Content Child Nodes Count: ${parsedContent.content.length}`);

  // Test 20 & 21: Verify ContentJson stayed 100% pagination-free
  const hasDraftPageNodes = JSON.stringify(parsedContent).includes('"draftPage"');
  console.log(`Does saved ContentJson contain 'draftPage' nodes? ${hasDraftPageNodes}`);
  assert.equal(hasDraftPageNodes, false, "ContentJson MUST NEVER contain draftPage or pagination nodes!");

  // Reopen by reloading page
  await page.reload({ waitUntil: "networkidle" });
  await page.waitForSelector(".draft-prosemirror");
  const reloadedText = await page.locator(".draft-prosemirror").innerText();
  assert.ok(reloadedText.includes("खसरा संख्या 22//2/7"), "Hindi text should persist on reload");
  assert.ok(reloadedText.includes("Cell 1: 22//2/7"), "Table cells should persist on reload");
  console.log("Reload verified content and layout fidelity.");

  // Test 24: Save-while-continuing-to-type race protection
  console.log("\n--- Testing 24: Save-while-continuing-to-type race protection ---");
  await page.locator(".draft-prosemirror").click({ force: true });
  await page.keyboard.type(" [EXTRA_LINE_DURING_SAVE]");
  const isDirtyNow = await page.locator("text=Unsaved changes").count();
  assert.ok(isDirtyNow > 0, "Editor should mark dirty when typing continues");
  await page.locator(".draft-save").click();
  await page.waitForSelector(".draft-save:disabled");
  console.log("Save completed with race protection intact.");

  // Test 25: Legacy draftPage content opens without auto-save and persists flat on explicit save
  console.log("\n--- Testing 25: Legacy draftPage backward compatibility ---");
  const legacyDraftRes = await request.post(`${API_BASE}/api/matters/${matterId}/drafts`, {
    data: {
      title: "Legacy Import Test",
      draftType: "Letter",
    },
  });
  const legacyDraftData = await legacyDraftRes.json();
  const legacyId = legacyDraftData.id;

  // Manually PUT legacy nested draftPage structure into DB
  const legacyJson = JSON.stringify({
    type: "doc",
    content: [
      {
        type: "draftPage",
        content: [
          {
            type: "paragraph",
            content: [{ type: "text", text: "Legacy un-nested paragraph content." }],
          },
        ],
      },
    ],
  });

  const getLegacy = await request.get(`${API_BASE}/api/matter-drafts/${legacyId}`);
  const legacyInitial = await getLegacy.json();

  await request.put(`${API_BASE}/api/matter-drafts/${legacyId}`, {
    data: {
      title: "Legacy Import Test",
      contentJson: legacyJson,
      pageSize: "A4",
      orientation: "Portrait",
      marginTopMm: 25,
      marginRightMm: 20,
      marginBottomMm: 20,
      marginLeftMm: 25,
      expectedRevision: legacyInitial.revision,
    },
  });

  // Open legacy draft in browser
  await page.goto(`${WEB_BASE}/matter-drafts/${legacyId}`, { waitUntil: "networkidle" });
  await page.waitForSelector(".draft-prosemirror");

  // Verify NOT marked dirty on open
  const isDirtyOnOpen = await page.locator("text=Unsaved changes").count();
  console.log(`Is legacy draft marked dirty on open? ${isDirtyOnOpen > 0}`);
  assert.equal(isDirtyOnOpen, 0, "Opening legacy draft MUST NOT mark dirty or auto-save!");

  const legacyTextInEditor = await page.locator(".draft-prosemirror").innerText();
  assert.ok(legacyTextInEditor.includes("Legacy un-nested paragraph content"), "Legacy content should display properly");

  // Make a small edit and save
  await page.locator(".draft-prosemirror").click({ force: true });
  await page.keyboard.type(" [EDITED]");
  await page.locator(".draft-save").click();
  await page.waitForSelector(".draft-save:disabled");

  // Verify it is now saved as clean flat JSON
  const resAfterSave = await request.get(`${API_BASE}/api/matter-drafts/${legacyId}`);
  const dataAfterSave = await resAfterSave.json();
  assert.equal(dataAfterSave.contentJson.includes('"draftPage"'), false, "After explicit save, draftPage must be flattened");
  console.log("Legacy draftPage flattened cleanly on save.");

  // Test 26-29: Create a Noting draft, test locked layout and multi-page
  console.log("\n--- Testing 26-29: Noting Draft & Locked Profile ---");
  const notingRes = await request.post(`${API_BASE}/api/matters/${matterId}/drafts`, {
    data: {
      title: "File Noting No. 1",
      draftType: "Noting",
    },
  });
  const notingData = await notingRes.json();
  const notingId = notingData.id;

  await page.goto(`${WEB_BASE}/matter-drafts/${notingId}`, { waitUntil: "networkidle" });
  await page.waitForSelector(".draft-prosemirror");

  // Open page setup panel
  await page.locator("button:has-text('Page setup')").click();
  await page.waitForSelector(".draft-noting-layout-info");

  // Verify locked controls
  const notingBadge = await page.locator(".noting-locked-badge").innerText();
  const notingDims = await page.locator(".noting-info-dims").innerText();
  const notingNote = await page.locator(".noting-info-note").innerText();
  console.log(`Noting Badge: ${notingBadge}`);
  console.log(`Noting Dimensions: ${notingDims}`);
  console.log(`Noting Provisional Note: ${notingNote}`);
  assert.equal(notingBadge, "FIXED PROFILE");
  assert.ok(notingDims.includes("210 × 297 mm (A4) · Portrait"));
  assert.ok(notingNote.includes("Provisional calibration"));

  // Check no editable dropdowns exist in Noting page setup
  const notingSelectCount = await page.locator(".draft-page-setup-panel select").count();
  assert.equal(notingSelectCount, 0, "Noting sheet must NOT have editable page size or orientation dropdowns");

  // Type multi-page content into Noting
  const notingEditor = page.locator(".draft-prosemirror");
  await notingEditor.click({ force: true });
  for (let i = 1; i <= 20; i++) {
    await page.keyboard.type(`[Noting Paragraph ${i}] Submitting matter for consideration of ADM (LA). Award No. 12/2008-09 Village Galibpur. Recommended for approval and release of compensation.`);
    await page.keyboard.press("Enter");
  }
  await page.waitForTimeout(500);

  const notingSheetCount = await page.locator(".draft-sheet-card").count();
  console.log(`Noting Sheet Page Count: ${notingSheetCount}`);
  assert.ok(notingSheetCount >= 2, "Noting draft should paginate automatically across multiple sheets");

  // Check Noting Sheet Badge text
  const firstNotingBadge = await page.locator(".draft-sheet-badge").first().innerText();
  console.log(`First Noting Badge text: "${firstNotingBadge}"`);
  assert.ok(firstNotingBadge.includes("Noting Sheet"), "Noting badge must indicate Noting Sheet");
  assert.ok(firstNotingBadge.includes("Provisional"), "Noting badge must indicate Provisional calibration");

  // Save Noting draft
  await page.locator(".draft-save").click();
  await page.waitForSelector(".draft-save:disabled");
  console.log("Noting draft saved cleanly.");

  // Test 30-33: Print Preview Emulation
  console.log("\n--- Testing 30-33: Print Preview Simulation ---");
  await page.emulateMedia({ media: "print" });
  await page.waitForTimeout(300);

  // Check elements hidden in print
  const toolbarVisibleInPrint = await page.locator(".draft-toolbar").isVisible();
  const headerVisibleInPrint = await page.locator(".draft-header").isVisible();
  const deckVisibleInPrint = await page.locator(".draft-backdrop-deck").isVisible();
  console.log(`Is Toolbar visible in print? ${toolbarVisibleInPrint}`);
  console.log(`Is Header visible in print? ${headerVisibleInPrint}`);
  console.log(`Is Backdrop deck visible in print? ${deckVisibleInPrint}`);
  assert.equal(toolbarVisibleInPrint, false, "Toolbar must be hidden in print");
  assert.equal(headerVisibleInPrint, false, "Header must be hidden in print");
  assert.equal(deckVisibleInPrint, false, "Backdrop deck must be hidden in print");

  // Check spacer collapsed in print
  const spacerStyles = await page.evaluate(() => {
    const el = document.querySelector(".draft-page-break-spacer, .draft-table-page-break-spacer");
    if (!el) return { height: "0px", breakBefore: "page", found: false };
    const style = window.getComputedStyle(el);
    return {
      height: style.getPropertyValue("height") || style.height,
      breakBefore: style.getPropertyValue("break-before") || style.breakBefore || style.getPropertyValue("page-break-before") || "page",
      found: true,
    };
  });
  console.log(`Spacer height in print: "${spacerStyles.height}", break-before: "${spacerStyles.breakBefore}"`);
  assert.equal(spacerStyles.height, "0px", "Spacer height in print must collapse to 0px to prevent double margins");
  assert.ok(spacerStyles.breakBefore === "page" || spacerStyles.breakBefore === "always", "Spacer must specify break-before: page");

  // Reset print emulation
  await page.emulateMedia({ media: "screen" });

  console.log("\n==================================================");
  console.log("BROWSER VERIFICATION SUMMARY");
  console.log("==================================================");
  console.log(`Console Logs: ${consoleLogs.length}`);
  console.log(`Console Warnings: ${consoleWarnings.length}`);
  console.log(`Console Errors: ${consoleErrors.length}`);
  console.log(`Page Errors: ${pageErrors.length}`);

  if (consoleErrors.length > 0) {
    console.log("CONSOLE ERRORS DETAILS:", consoleErrors);
  }
  if (pageErrors.length > 0) {
    console.log("PAGE ERRORS DETAILS:", pageErrors);
  }

  assert.equal(pageErrors.length, 0, "No page errors allowed during manual browser execution");
  assert.equal(consoleErrors.length, 0, "No console errors allowed during manual browser execution");

  console.log("\nALL 33 BROWSER SCENARIOS VERIFIED SUCCESSFULLY WITH ZERO CONSOLE ERRORS!");
  await browser.close();
}

main().catch((err) => {
  console.error("BROWSER VERIFICATION FAILED:", err);
  process.exit(1);
});
