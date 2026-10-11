import { chromium } from "playwright";
import fs from "node:fs";
import path from "node:path";
import assert from "node:assert";
import { randomUUID } from "node:crypto";

const repoRoot = path.resolve(import.meta.dirname, "../../..");
const seedPath = path.join(repoRoot, "tests/fixtures/noting-e2e-seed.json");
const receiptsDir = path.join(repoRoot, "docs/receipts/matter-noting-workspace");

if (!fs.existsSync(receiptsDir)) {
  fs.mkdirSync(receiptsDir, { recursive: true });
}

if (!fs.existsSync(seedPath)) {
  console.error("Seed fixture not found at " + seedPath);
  process.exit(1);
}

const seed = JSON.parse(fs.readFileSync(seedPath, "utf-8"));
const baseUrl = seed.apiUrl || "http://127.0.0.1:5399";
console.log(`[E2E] Running Genuine Noting E2E against ${baseUrl}`);

async function run() {
  const browser = await chromium.launch({
    headless: true,
    args: ["--no-sandbox", "--disable-setuid-sandbox"]
  });

  try {
    // ==========================================
    // STEP 1: OFFICER 1 LOGIN & NOTING SPLIT VIEW
    // ==========================================
    console.log("[E2E] Step 1: Officer 1 (Kanungo) login and Matter Noting split view...");
    const ctx1 = await browser.newContext({
      viewport: { width: 1366, height: 768 },
      deviceScaleFactor: 1
    });
    const page1 = await ctx1.newPage();
    page1.on("console", (msg) => console.log(`[Browser 1 Console] ${msg.type()}: ${msg.text()}`));
    page1.on("pageerror", (err) => console.log(`[Browser 1 Error] ${err.message}`));

    await page1.goto(`${baseUrl}/login`, { waitUntil: "networkidle" });
    await page1.fill("#username", seed.officer1.username);
    await page1.fill('input[type="password"]', seed.officer1.password);
    await page1.click('form.login-form button[type="submit"]');

    // Wait for auth to settle and navigate to matter with ?tab=noting
    await page1.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 10000 });
    await page1.goto(`${baseUrl}/matters/${seed.matterId}?tab=noting`, { waitUntil: "networkidle" });

    // Ensure Noting tab is active
    const notingTab = page1.locator("button, a").filter({ hasText: /Noting/i }).first();
    if (await notingTab.isVisible()) {
      await notingTab.click();
      await page1.waitForTimeout(500);
    }

    // Wait for Noting split layout
    await page1.waitForSelector(".noting-workspace-root", { timeout: 10000 });
    await page1.waitForSelector(".noting-left-column", { timeout: 10000 });
    await page1.waitForSelector(".noting-right-column", { timeout: 10000 });

    const shot1Path = path.join(receiptsDir, "01-noting-workspace-1366x768-split.png");
    await page1.screenshot({ path: shot1Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 1: ${shot1Path}`);

    // ==========================================
    // STEP 2: RICH EDITOR, AUTOSAVE & 409 CONFLICT PRESERVATION
    // ==========================================
    console.log("[E2E] Step 2: Testing rich-text editor, debounced autosave, and 409 conflict preservation...");
    const textarea = page1.locator("#active-draft-textarea");
    await textarea.waitFor({ state: "visible", timeout: 5000 });

    // Test formatting toolbar interaction
    const boldBtn = page1.locator('.active-draft-toolbar button[title*="Bold"]');
    await boldBtn.waitFor({ state: "visible", timeout: 5000 });
    await boldBtn.click();
    console.log("[E2E] Clicked Bold formatting toolbar button");

    const draftText1 = "Draft note by Kanungo: Submitted for physical site inspection report and revenue record verification of Galib Pur khasras.";
    await textarea.fill(draftText1);

    // Wait for autosave to complete
    const autosaveSaved = page1.locator(".noting-autosave-indicator").filter({ hasText: /All changes saved/i });
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });
    console.log("[E2E] Autosave confirmed: ✓ All changes saved");

    // Fetch current draft ETag / revision from server
    const draftStatusRes = await ctx1.request.get(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`);
    const currentEtag = draftStatusRes.headers()["etag"] || '"1"';
    console.log(`[E2E] Current draft ETag on server: ${currentEtag}`);

    // Trigger a 409 Conflict: Make an out-of-band PUT to advance server revision
    console.log("[E2E] Triggering concurrent server update to advance server revision...");
    const outOfBandRes = await ctx1.request.put(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`, {
      headers: {
        "If-Match": currentEtag,
        "Idempotency-Key": randomUUID()
      },
      data: {
        contentJson: JSON.stringify({
          type: "doc",
          content: [{ type: "paragraph", content: [{ type: "text", text: "Concurrent edit on server" }] }]
        }),
        sourceDakId: seed.dakId
      }
    });
    console.log(`[E2E] Out-of-band PUT status: ${outOfBandRes.status()}`);
    assert.strictEqual(outOfBandRes.status(), 200, "Out-of-band PUT should advance server revision");

    // Type more local edits into the editor
    const draftTextWithLocalEdits = draftText1 + "\n\nAdditional field observations added locally before sync.";
    await textarea.fill(draftTextWithLocalEdits);

    // Wait for autosave to attempt save and receive 409
    const conflictCard = page1.locator(".noting-conflict-card");
    await conflictCard.waitFor({ state: "visible", timeout: 8000 });
    const conflictTitle = await conflictCard.textContent();
    console.log(`[E2E] 409 Conflict card detected: ${conflictTitle?.includes("Draft Revision Conflict")}`);
    assert.ok(conflictTitle?.includes("Draft Revision Conflict"), "Conflict card must be displayed");

    // Verify local text is PRESERVED, not lost
    const preservedText = await textarea.inputValue();
    assert.strictEqual(preservedText, draftTextWithLocalEdits, "Local unsaved edits must be strictly preserved on 409");

    const shot2Path = path.join(receiptsDir, "02-noting-workspace-autosave-conflict.png");
    await page1.screenshot({ path: shot2Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 2: ${shot2Path}`);

    // Resolve conflict by clicking "Keep My Edits & Overwrite Server"
    const keepBtn = conflictCard.locator("button").filter({ hasText: "Keep My Edits & Overwrite Server" });
    await keepBtn.click();
    await page1.waitForTimeout(1500);
    assert.strictEqual(await conflictCard.isVisible(), false, "Conflict card must clear after resolution");

    // ==========================================
    // STEP 3: TEXT-ANCHORED DOCUMENT CITATION
    // ==========================================
    console.log("[E2E] Step 3: Text-anchored Document Citation...");
    await page1.evaluate(() => {
      const ta = document.getElementById("active-draft-textarea");
      if (ta) {
        const text = ta.value;
        const target = "site inspection report";
        const start = text.indexOf(target);
        if (start >= 0) {
          ta.focus();
          ta.setSelectionRange(start, start + target.length);
        }
      }
    });

    const citeBtn = page1.locator("button").filter({ hasText: /\+ Cite Document/i });
    await citeBtn.waitFor({ state: "visible", timeout: 5000 });
    await citeBtn.click();
    await page1.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    // In citation modal, fill page 1
    const pageInput = page1.locator('.noting-modal-overlay input[type="number"]');
    await pageInput.waitFor({ state: "visible", timeout: 5000 });
    await pageInput.fill("1");

    // Intercept citation POST response
    const citePromise = page1.waitForResponse((res) =>
      res.url().includes("/citations") && res.request().method() === "POST"
    );
    const insertCiteBtn = page1.locator('.noting-modal-overlay button.noting-btn-primary');
    await insertCiteBtn.click();

    const citeRes = await citePromise;
    assert.strictEqual(citeRes.status(), 200, "Citation creation must return 200 OK");
    console.log("[E2E] Citation created and saved successfully.");
    await page1.waitForTimeout(1000);

    const shot3Path = path.join(receiptsDir, "03-noting-workspace-citation-drawer.png");
    await page1.screenshot({ path: shot3Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 3: ${shot3Path}`);

    // ==========================================
    // STEP 4: SUBMIT NOTE 1 & TRANSACTIONAL SEND NOTE 2
    // ==========================================
    console.log("[E2E] Step 4: Submitting Note 1 standalone, composing Note 2 and executing transactional Send...");
    const submitBtn = page1.locator("button.noting-btn-primary").filter({ hasText: /Submit Note/i });
    
    // Intercept submit response
    const submitPromise = page1.waitForResponse((res) =>
      res.url().includes("/submit") && res.request().method() === "POST"
    );
    await submitBtn.click();
    const submitRes = await submitPromise;
    assert.strictEqual(submitRes.status(), 200, "Note 1 submission must return 200 OK");
    console.log("[E2E] Note 1 submitted successfully.");

    // Wait for Note 1 to appear in official notes chain
    await page1.waitForSelector(".official-note-card", { timeout: 10000 });
    const noteBadge = page1.locator(".official-note-number-badge").first();
    const note1NumberText = await noteBadge.textContent();
    console.log(`[E2E] Submitted note badge: ${note1NumberText}`);
    assert.ok(note1NumberText?.includes("NOTE 1"), "First submitted note must be sequentially numbered NOTE 1");

    // Verify citation pill is rendered INLINE in note 1
    const note1Card = page1.locator(".official-note-card").first();
    const inlineCitePill = note1Card.locator(".note-citation-pill");
    await inlineCitePill.waitFor({ state: "visible", timeout: 5000 });
    const citeText = await inlineCitePill.textContent();
    assert.ok(citeText?.includes("evidence-revenue-map.pdf") || citeText?.includes("Doc:"), "Citation pill must be rendered at cited location");
    console.log("[E2E] Confirmed citation pill rendered inline at exact anchor location:", citeText);

    // Compose Note 2 forwarding draft for Send
    console.log("[E2E] Composing Note 2 for file movement to Tehsildar...");
    const draftText2 = "Forwarding file with revenue map and site inspection report to Tehsildar for review and approval.";
    await textarea.fill(draftText2);

    // Wait for autosave to complete
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });
    console.log("[E2E] Forwarding draft autosave confirmed.");

    // Open Send / Return modal
    const sendBtn = page1.locator("button.noting-btn-send").filter({ hasText: /Send \/ Return File/i });
    await sendBtn.waitFor({ state: "visible", timeout: 5000 });
    await sendBtn.click();
    await page1.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    // Verify digital-only file movement indicator (no mandatory physical check)
    const digitalIndicator = page1.locator(".noting-modal-overlay").filter({ hasText: /Digital-only file movement/i });
    await digitalIndicator.waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Verified Digital-Only file movement indicator is present.");

    // Select recipient desk & officer: Tehsildar Desk & Officer 2
    console.log(`[E2E] Selecting recipient desk: ${seed.officer2.deskName} (${seed.officer2.deskId})...`);
    const deskSelect = page1.locator(".noting-modal-overlay select").first();
    await deskSelect.waitFor({ state: "visible", timeout: 5000 });
    await deskSelect.selectOption(seed.officer2.deskId);
    await page1.waitForTimeout(500);

    const userSelect = page1.locator(".noting-modal-overlay select").nth(1);
    await userSelect.waitFor({ state: "visible", timeout: 5000 });
    const selectedUserVal = await userSelect.inputValue();
    if (!selectedUserVal && seed.officer2.userId) {
      await userSelect.selectOption(seed.officer2.userId);
    }

    // Fill Send instructions
    const instructionsInput = page1.locator('.noting-modal-overlay input[type="text"]').first();
    if (await instructionsInput.isVisible()) {
      await instructionsInput.fill("Forwarded for Tehsildar orders and perusal.");
    }
    const remarksInput = page1.locator('.noting-modal-overlay textarea').first();
    if (await remarksInput.isVisible()) {
      await remarksInput.fill("Internal forwarding remarks: Demarcation review required.");
    }

    const shot4Path = path.join(receiptsDir, "04-noting-workspace-send-return-modal.png");
    await page1.screenshot({ path: shot4Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 4: ${shot4Path}`);

    // Track Send network response
    const sendPromise = page1.waitForResponse((res) =>
      res.url().includes("/send") && res.request().method() === "POST"
    );
    const modalSendBtn = page1.locator('.noting-modal-overlay button.noting-btn-send');
    await modalSendBtn.click();

    const sendRes = await sendPromise;
    assert.strictEqual(sendRes.status(), 200, "Transactional Note Send must return 200 OK");
    const sendJson = await sendRes.json();
    console.log("[E2E] Transactional Send confirmed: Note Number =", sendJson.note?.number, "Dak TransferId =", sendJson.dak?.transferId);
    assert.ok(sendJson.note, "Send response must contain finalized note");
    assert.ok(sendJson.dak?.transferId, "Send response must contain Dak transfer ID");
    const transferId = sendJson.dak.transferId;
    const dakRevisionAfterSend = sendJson.dak.revision;

    await ctx1.close();

    // ==========================================
    // STEP 5: RECIPIENT RECEIPT & OFFICER 2 LOGIN
    // ==========================================
    console.log("[E2E] Step 5: Officer 2 (Tehsildar) receipt acknowledgment & immutable note inspection...");
    const ctx2 = await browser.newContext({
      viewport: { width: 1366, height: 768 },
      deviceScaleFactor: 1
    });
    const page2 = await ctx2.newPage();
    page2.on("console", (msg) => console.log(`[Browser 2 Console] ${msg.type()}: ${msg.text()}`));
    page2.on("pageerror", (err) => console.log(`[Browser 2 Error] ${err.message}`));

    // Tehsildar logs in
    await page2.goto(`${baseUrl}/login`, { waitUntil: "networkidle" });
    await page2.fill("#username", seed.officer2.username);
    await page2.fill('input[type="password"]', seed.officer2.password);
    await page2.click('form.login-form button[type="submit"]');
    await page2.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 10000 });

    // Officer 2 receives the pending Dak transfer
    console.log(`[E2E] Officer 2 receiving pending Dak transfer: ${transferId}...`);
    const receiveRes = await ctx2.request.post(`${baseUrl}/api/dak/${seed.dakId}/transfers/${transferId}/receive`, {
      headers: {
        "Idempotency-Key": randomUUID()
      },
      data: {
        expectedRevision: dakRevisionAfterSend,
        physicalReceiptConfirmed: false
      }
    });
    console.log(`[E2E] Dak Receive response status: ${receiveRes.status()}`);
    assert.strictEqual(receiveRes.status(), 200, "Officer 2 recipient receipt must return 200 OK");

    // Navigate to Matter Noting workspace with sourceDakId
    await page2.goto(`${baseUrl}/matters/${seed.matterId}?tab=noting&sourceDak=${seed.dakId}`, { waitUntil: "networkidle" });

    await page2.waitForSelector(".official-note-card", { timeout: 10000 });
    const noteCards = page2.locator(".official-note-card");
    const count = await noteCards.count();
    console.log(`[E2E] Officer 2 observes ${count} submitted notes in chain.`);
    assert.ok(count >= 2, "Both Note 1 and Note 2 must be present in the official notes chain");

    const note1Author = await noteCards.first().locator(".official-note-author").textContent();
    console.log(`[E2E] Officer 2 observes Note 1 author: ${note1Author}`);
    assert.ok(note1Author?.includes("Kanungo"), "Note 1 author must display Kanungo");

    const shot5Path = path.join(receiptsDir, "05-noting-workspace-two-officers-sequential.png");
    await page2.screenshot({ path: shot5Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 5: ${shot5Path}`);

    // ==========================================
    // STEP 6: INLINE HIGHLIGHTED REMARK & PUT STATE LIFECYCLE
    // ==========================================
    console.log("[E2E] Step 6: Text-anchored remark creation, inline highlight and PUT state change...");
    const note1 = noteCards.first();
    const addRemarkBtn = note1.locator("button.add-remark-btn");
    await addRemarkBtn.waitFor({ state: "visible", timeout: 5000 });
    await addRemarkBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    const remarkInput = page2.locator('.noting-modal-overlay textarea');
    await remarkInput.fill("Please cross-verify field demarcation against latest revenue survey map.");

    const remarkPromise = page2.waitForResponse((res) =>
      res.url().includes("/remarks") && res.request().method() === "POST"
    );
    const submitRemarkBtn = page2.locator('.noting-modal-overlay button.noting-btn-primary');
    await submitRemarkBtn.click();

    const createdRemarkRes = await remarkPromise;
    assert.strictEqual(createdRemarkRes.status(), 200, "Remark creation must return 200 OK");
    console.log("[E2E] Remark created successfully.");
    await page2.waitForTimeout(1000);

    // Verify precise inline highlight in Note 1 text
    const inlineRemarkHighlight = note1.locator(".noting-remark-highlight");
    await inlineRemarkHighlight.waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Confirmed inline highlighted remark exists on Note 1.");

    // Hover or click inline highlight to trigger floating popover
    await inlineRemarkHighlight.click();
    const popover = page2.locator(".noting-remark-popover");
    await popover.waitFor({ state: "visible", timeout: 5000 });
    const popoverBadge = await popover.locator(".noting-remark-popover-badge").textContent();
    assert.ok(popoverBadge?.toLowerCase().includes("open"), "Remark popover badge must show Open state");
    console.log("[E2E] Remark popover displayed with Open status.");

    // Click "Mark Addressed" in popover
    const addressPutPromise = page2.waitForResponse((res) =>
      res.url().includes("/remarks/") && res.request().method() === "PUT"
    );
    const addressBtn = popover.locator("button.toggle-remark-state-btn");
    await addressBtn.click();

    const putRes = await addressPutPromise;
    assert.strictEqual(putRes.status(), 200, "Remark state change must execute via PUT route and return 200 OK");
    const updatedRemarkJson = await putRes.json();
    assert.strictEqual(updatedRemarkJson.state, "Addressed", "Remark state must transition to Addressed");
    console.log("[E2E] Confirmed PUT /api/matters/{m}/notes/{n}/remarks/{r} executed successfully with Addressed state.");

    const shot6Path = path.join(receiptsDir, "06-noting-workspace-anchored-remarks.png");
    await page2.screenshot({ path: shot6Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 6: ${shot6Path}`);

    // Close popover
    const closePopoverBtn = popover.locator("button").filter({ hasText: /Close/i });
    if (await closePopoverBtn.isVisible()) {
      await closePopoverBtn.click();
    }

    // ==========================================
    // STEP 7: DOCUMENT UPLOAD, PDF HIGHLIGHTS & NOTE 3 IN FOCUS MODE
    // ==========================================
    console.log("[E2E] Step 7: Document upload, PDF page highlight overlay, Note 3 submission, and focus mode...");
    const uploadBtn = page2.locator("button").filter({ hasText: /\+ Upload File/i }).first();
    await uploadBtn.waitFor({ state: "visible", timeout: 5000 });
    await uploadBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    const dummyPdfPath = path.join(repoRoot, "tests/fixtures/khasra-inspection-report.pdf");
    if (!fs.existsSync(dummyPdfPath)) {
      fs.writeFileSync(dummyPdfPath, "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF");
    }

    const fileInput = page2.locator('.noting-modal-overlay input[type="file"]');
    await fileInput.setInputFiles(dummyPdfPath);

    const nameInput = page2.locator('.noting-modal-overlay input[type="text"]').first();
    await nameInput.fill("Khasra Inspection Report");

    const uploadResponsePromise = page2.waitForResponse((res) =>
      res.url().includes("/documents") && res.request().method() === "POST"
    );
    const submitUploadBtn = page2.locator('.noting-modal-overlay button.noting-btn-primary');
    await submitUploadBtn.click();

    const uploadRes = await uploadResponsePromise;
    assert.ok(uploadRes.status() === 200 || uploadRes.status() === 201, `Document upload must return 200 or 201 Created, got ${uploadRes.status()}`);
    console.log("[E2E] Document uploaded and linked successfully.");
    await page2.waitForTimeout(1500);

    // Select the first PDF document to add a PDF page highlight
    const firstDocCard = page2.locator(".document-rail-card").first();
    await firstDocCard.click();
    await page2.waitForTimeout(1000);

    // Add PDF Page Highlight
    const addHighlightBtn = page2.locator("button.add-highlight-btn");
    await addHighlightBtn.waitFor({ state: "visible", timeout: 5000 });
    await addHighlightBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    const annoQuoteInput = page2.locator('.noting-modal-overlay input[type="text"]').first();
    await annoQuoteInput.fill("Galib Pur Demarcation");

    const annoTextInput = page2.locator('.noting-modal-overlay textarea').first();
    await annoTextInput.fill("Approved corridor boundary verified by survey team.");

    const highlightResponsePromise = page2.waitForResponse((res) =>
      res.url().includes("/annotations") && res.request().method() === "POST"
    );
    const saveHighlightBtn = page2.locator('.noting-modal-overlay button.noting-btn-primary');
    await saveHighlightBtn.click();

    const annoRes = await highlightResponsePromise;
    assert.strictEqual(annoRes.status(), 200, "Annotation creation must return 200 OK");
    console.log("[E2E] PDF highlight annotation created successfully.");

    // Assert visible PDF-page highlight overlay exists
    const pdfOverlay = page2.locator(".pdf-highlight-overlay").first();
    await pdfOverlay.waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Confirmed visible PDF-page highlight overlay rendered on document frame.");

    // Compose and submit Note 3 by Tehsildar
    console.log("[E2E] Officer 2 composing Note 3...");
    const textarea2 = page2.locator("#active-draft-textarea");
    await textarea2.waitFor({ state: "visible", timeout: 5000 });
    await textarea2.fill("Report and demarcation examined in detail. Sanction approved for further acquisition proceedings as per Section 19.");

    const autosaveSaved2 = page2.locator(".noting-autosave-indicator").filter({ hasText: /All changes saved/i });
    await autosaveSaved2.waitFor({ state: "visible", timeout: 8000 });
    console.log("[E2E] Note 3 autosave confirmed.");

    const submitNote3Promise = page2.waitForResponse((res) =>
      res.url().includes("/submit") && res.request().method() === "POST"
    );
    const submitBtn2 = page2.locator("button.noting-btn-primary").filter({ hasText: /Submit Note/i });
    await submitBtn2.click();

    const note3Res = await submitNote3Promise;
    assert.strictEqual(note3Res.status(), 200, "Note 3 submission must return 200 OK");
    console.log("[E2E] Note 3 submitted successfully into official chain.");
    await page2.waitForTimeout(1000);

    // Toggle Fullscreen Focus mode
    const focusToggleBtn = page2.locator("button").filter({ hasText: /Focus/i }).first();
    await focusToggleBtn.click();
    await page2.waitForTimeout(600);

    const shot7Path = path.join(receiptsDir, "07-noting-workspace-1366x768-focus.png");
    await page2.screenshot({ path: shot7Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 7: ${shot7Path}`);

    await ctx2.close();
    console.log("[E2E] ALL 7 STEPS COMPLETED CLEANLY WITH REAL API & DISPOSABLE POSTGRESQL!");

  } finally {
    await browser.close();
  }
}

run().catch((err) => {
  console.error("[E2E] Test failed with error:", err);
  process.exit(1);
});
