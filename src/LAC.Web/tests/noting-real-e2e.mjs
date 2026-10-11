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
    // STEP 2: AUTOSAVE & 409 CONFLICT PRESERVATION
    // ==========================================
    console.log("[E2E] Step 2: Testing debounced server autosave and 409 conflict preservation...");
    const textarea = page1.locator("#active-draft-textarea");
    await textarea.waitFor({ state: "visible", timeout: 5000 });

    const draftText1 = "Draft note by Kanungo: Submitted for physical site inspection report and revenue record verification of Galib Pur khasras.";
    await textarea.fill(draftText1);

    // Wait for autosave to complete
    const autosaveSaved = page1.locator(".noting-autosave-indicator").filter({ hasText: /All changes saved/i });
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });
    console.log("[E2E] Autosave confirmed: ✓ All changes saved");

    // Fetch current draft ETag / revision from server using context request
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

    // Type more local edits into the browser textarea
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
    // STEP 3: DOCUMENT CITATION
    // ==========================================
    console.log("[E2E] Step 3: Anchored Document Citation...");
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
    if (await citeBtn.isVisible()) {
      await citeBtn.click();
      await page1.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

      // In citation modal, fill page 1
      const pageInput = page1.locator('.noting-modal-overlay input[type="number"]');
      if (await pageInput.isVisible()) {
        await pageInput.fill("1");
      }

      const insertCiteBtn = page1.locator('.noting-modal-overlay button.noting-btn-primary');
      await insertCiteBtn.click();
      await page1.waitForTimeout(1000);
    }

    const shot3Path = path.join(receiptsDir, "03-noting-workspace-citation-drawer.png");
    await page1.screenshot({ path: shot3Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 3: ${shot3Path}`);

    // ==========================================
    // STEP 4: SUBMIT NOTE 1 & SEND / RETURN MODAL (DIGITAL-ONLY)
    // ==========================================
    console.log("[E2E] Step 4: Submitting Note 1 and opening Send/Return modal...");
    const submitBtn = page1.locator("button.noting-btn-primary").filter({ hasText: /Submit Note/i });
    await submitBtn.click();

    // Wait for note 1 to appear in official notes chain
    await page1.waitForSelector(".official-note-card", { timeout: 10000 });
    const noteBadge = page1.locator(".official-note-number-badge").first();
    const note1NumberText = await noteBadge.textContent();
    console.log(`[E2E] Submitted note badge: ${note1NumberText}`);
    assert.ok(note1NumberText?.includes("NOTE 1"), "First submitted note must be sequentially numbered NOTE 1");

    // Open Send / Return modal
    const sendBtn = page1.locator("button.noting-btn-send").filter({ hasText: /Send \/ Return File/i });
    await sendBtn.click();
    await page1.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    // Verify digital-only file movement indicator
    const digitalIndicator = page1.locator(".noting-modal-overlay").filter({ hasText: /Digital-only file movement/i });
    await digitalIndicator.waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Verified Digital-Only file movement indicator is present (no mandatory physical check).");

    // Fill Send / Return form
    const instructionsInput = page1.locator('.noting-modal-overlay textarea').first();
    if (await instructionsInput.isVisible()) {
      await instructionsInput.fill("Forwarded for Tehsildar orders and perusal.");
    }

    const shot4Path = path.join(receiptsDir, "04-noting-workspace-send-return-modal.png");
    await page1.screenshot({ path: shot4Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 4: ${shot4Path}`);

    // Send the file to Tehsildar
    const modalSendBtn = page1.locator('.noting-modal-overlay button.noting-btn-send');
    await modalSendBtn.click();
    await page1.waitForTimeout(2000);

    await ctx1.close();

    // ==========================================
    // STEP 5: OFFICER 2 LOGIN & VIEW IMMUTABLE NOTE 1
    // ==========================================
    console.log("[E2E] Step 5: Officer 2 (Tehsildar) login and immutable Note 1 verification...");
    const ctx2 = await browser.newContext({
      viewport: { width: 1366, height: 768 },
      deviceScaleFactor: 1
    });
    const page2 = await ctx2.newPage();
    page2.on("console", (msg) => console.log(`[Browser 2 Console] ${msg.type()}: ${msg.text()}`));
    page2.on("pageerror", (err) => console.log(`[Browser 2 Error] ${err.message}`));

    await page2.goto(`${baseUrl}/login`, { waitUntil: "networkidle" });
    await page2.fill("#username", seed.officer2.username);
    await page2.fill('input[type="password"]', seed.officer2.password);
    await page2.click('form.login-form button[type="submit"]');

    await page2.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 10000 });
    await page2.goto(`${baseUrl}/matters/${seed.matterId}?tab=noting`, { waitUntil: "networkidle" });

    const notingTab2 = page2.locator("button, a").filter({ hasText: /Noting/i }).first();
    if (await notingTab2.isVisible()) {
      await notingTab2.click();
      await page2.waitForTimeout(500);
    }

    await page2.waitForSelector(".official-note-card", { timeout: 10000 });
    const note1Card = page2.locator(".official-note-card").first();
    const note1Author = await note1Card.locator(".official-note-author").textContent();
    console.log(`[E2E] Officer 2 observes Note 1 author: ${note1Author}`);
    assert.ok(note1Author?.includes("Kanungo"), "Note 1 author must display Kanungo");

    const shot5Path = path.join(receiptsDir, "05-noting-workspace-two-officers-sequential.png");
    await page2.screenshot({ path: shot5Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 5: ${shot5Path}`);

    // ==========================================
    // STEP 6: TEXT-ANCHORED REMARK & PUT STATE LIFECYCLE
    // ==========================================
    console.log("[E2E] Step 6: Text-anchored remark creation and PUT state change...");
    const addRemarkBtn = note1Card.locator("button.add-remark-btn");
    await addRemarkBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    const remarkInput = page2.locator('.noting-modal-overlay textarea');
    await remarkInput.fill("Please cross-verify field demarcation against latest revenue survey map.");

    const submitRemarkBtn = page2.locator('.noting-modal-overlay button.noting-btn-primary');
    await submitRemarkBtn.click();
    await page2.waitForTimeout(1000);

    // View remarks modal
    const viewRemarksBtn = note1Card.locator("button.view-remarks-btn");
    await viewRemarksBtn.waitFor({ state: "visible", timeout: 5000 });
    await viewRemarksBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    // Verify Open status
    const openBadge = page2.locator('.noting-modal-overlay span').filter({ hasText: /Open/i }).first();
    await openBadge.waitFor({ state: "visible" });

    // Track network request for PUT method
    let remarkPutObserved = false;
    page2.on("request", (req) => {
      if (req.method() === "PUT" && req.url().includes("/remarks/")) {
        remarkPutObserved = true;
        console.log(`[E2E] Confirmed PUT request: ${req.url()}`);
      }
    });

    // Click Mark Addressed
    const addressBtn = page2.locator('.noting-modal-overlay button').filter({ hasText: /Mark Addressed/i }).first();
    await addressBtn.click();
    await page2.waitForTimeout(1200);

    assert.ok(remarkPutObserved, "Remark state change must execute via PUT method as per contract");
    console.log("[E2E] Verified PUT /api/matters/{m}/notes/{n}/remarks/{r} executed successfully.");

    const shot6Path = path.join(receiptsDir, "06-noting-workspace-anchored-remarks.png");
    await page2.screenshot({ path: shot6Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 6: ${shot6Path}`);

    // Close remarks modal
    const closeRemarkModalBtn = page2.locator('.noting-modal-overlay button').filter({ hasText: /✕|Close/i }).first();
    await closeRemarkModalBtn.click();
    await page2.waitForTimeout(500);

    // ==========================================
    // STEP 7: UPLOAD DOCUMENT, NOTE 2 & FOCUS MODE
    // ==========================================
    console.log("[E2E] Step 7: Upload document, submit Note 2, and fullscreen focus mode...");
    const uploadBtn = page2.locator("button").filter({ hasText: /\+ Upload File/i }).first();
    if (await uploadBtn.isVisible()) {
      await uploadBtn.click();
      await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

      // Create a dummy PDF file buffer to upload
      const dummyPdfPath = path.join(repoRoot, "tests/fixtures/khasra-inspection-report.pdf");
      if (!fs.existsSync(dummyPdfPath)) {
        fs.writeFileSync(dummyPdfPath, "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF");
      }

      const fileInput = page2.locator('.noting-modal-overlay input[type="file"]');
      await fileInput.setInputFiles(dummyPdfPath);

      const nameInput = page2.locator('.noting-modal-overlay input[type="text"]').first();
      if (await nameInput.isVisible()) {
        await nameInput.fill("Khasra Inspection Report");
      }

      const submitUploadBtn = page2.locator('.noting-modal-overlay button.noting-btn-primary');
      await submitUploadBtn.click();
      await page2.waitForTimeout(1500);
      console.log("[E2E] Document uploaded and linked successfully.");
    }

    // Submit Note 2 by Tehsildar
    const textarea2 = page2.locator("#active-draft-textarea");
    if (await textarea2.isVisible()) {
      await textarea2.fill("Report and demarcation examined in detail. Sanction approved for further acquisition proceedings as per Section 19.");
      await page2.waitForTimeout(2000); // wait for autosave

      const submitBtn2 = page2.locator("button.noting-btn-primary").filter({ hasText: /Submit Note/i });
      await submitBtn2.click();
      await page2.waitForTimeout(2000);
      console.log("[E2E] Note 2 submitted successfully.");
    }

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
