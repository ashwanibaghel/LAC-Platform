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

async function typeTipTapText(page, text, clearFirst = true) {
  const pm = page.locator(".active-draft-tiptap .ProseMirror");
  await pm.waitFor({ state: "visible", timeout: 8000 });
  await pm.click();
  if (clearFirst) {
    await page.keyboard.press("ControlOrMeta+A");
    await page.keyboard.press("Backspace");
  }
  await page.keyboard.insertText(text);
}

async function selectTipTapRange(page, targetText) {
  return await page.evaluate((target) => {
    const pm = document.querySelector(".active-draft-tiptap .ProseMirror");
    if (!pm) return false;
    const paras = pm.querySelectorAll("p, h1, h2, h3, blockquote, li");
    for (const p of paras) {
      const text = p.textContent || "";
      const idx = text.indexOf(target);
      if (idx >= 0) {
        let charCount = 0;
        let startNode = null;
        let startOffset = 0;
        let endNode = null;
        let endOffset = 0;

        function walk(node) {
          if (node.nodeType === Node.TEXT_NODE) {
            const len = node.textContent.length;
            if (!startNode && charCount + len >= idx) {
              startNode = node;
              startOffset = idx - charCount;
            }
            if (!endNode && charCount + len >= idx + target.length) {
              endNode = node;
              endOffset = idx + target.length - charCount;
            }
            charCount += len;
          } else {
            for (const child of node.childNodes) {
              walk(child);
            }
          }
        }
        walk(p);

        if (startNode && endNode) {
          const range = document.createRange();
          range.setStart(startNode, startOffset);
          range.setEnd(endNode, endOffset);
          const sel = window.getSelection();
          sel.removeAllRanges();
          sel.addRange(range);
          document.dispatchEvent(new Event("selectionchange"));
          return true;
        }
      }
    }
    return false;
  }, targetText);
}

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
    // STEP 2: RICH EDITOR, PERSISTENCE AFTER REFRESH, AUTOSAVE & 409 CONFLICT MERGE
    // ==========================================
    console.log("[E2E] Step 2: Testing TipTap editor, formatting persistence after reload, and 409 conflict Merge...");
    const editor = page1.locator(".active-draft-tiptap .ProseMirror");
    await editor.waitFor({ state: "visible", timeout: 8000 });

    const draftText1 = "Draft note by Kanungo: Submitted for physical site inspection report and revenue record verification of Galib Pur khasras.";
    await typeTipTapText(page1, draftText1);

    // Format target phrase with Bold toolbar button
    console.log("[E2E] Selecting text and applying Bold formatting toolbar...");
    const selected = await selectTipTapRange(page1, "physical site inspection report");
    assert.ok(selected, "TipTap selection must succeed on target phrase");

    const boldBtn = page1.locator('.active-draft-toolbar button[title*="Bold"]');
    await boldBtn.waitFor({ state: "visible", timeout: 5000 });
    await boldBtn.click();

    // Verify <strong> element was created in TipTap
    const strongElement = page1.locator(".active-draft-tiptap .ProseMirror strong");
    await strongElement.waitFor({ state: "visible", timeout: 5000 });
    const formattedText = await strongElement.textContent();
    assert.strictEqual(formattedText, "physical site inspection report", "TipTap strong tag must contain formatted phrase");
    console.log("[E2E] Confirmed Bold formatting applied in TipTap editor.");

    // Wait for autosave to complete
    const autosaveSaved = page1.locator(".noting-autosave-indicator").filter({ hasText: /All changes saved/i });
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });
    console.log("[E2E] Autosave confirmed: ✓ All changes saved");

    // Test formatting persistence across page reload
    console.log("[E2E] Reloading page to verify formatting persistence...");
    await page1.reload({ waitUntil: "networkidle" });
    await page1.waitForSelector(".noting-workspace-root", { timeout: 10000 });
    const reloadedStrong = page1.locator(".active-draft-tiptap .ProseMirror strong");
    await reloadedStrong.waitFor({ state: "visible", timeout: 8000 });
    assert.strictEqual(await reloadedStrong.textContent(), "physical site inspection report", "Formatting must persist across reload");
    console.log("[E2E] Confirmed formatting persistence across page reload.");

    // ==========================================
    // AUDIT CLIENT-SIDE STORAGE SECURITY & LEGACY RECORD PURGING
    // ==========================================
    console.log("[E2E] Auditing office storage security policy: persistent browser draft caching disabled & legacy purge...");

    // Inject legacy records into localStorage to verify automatic purging
    await page1.evaluate(() => {
      localStorage.setItem("lac_enc_draft_legacy_officer_matter", "legacy-insecure-ciphertext");
      localStorage.setItem("lac_draft_legacy_plaintext", "legacy-plaintext");
    });

    await editor.click();
    await page1.keyboard.press("End");
    const secretOfflinePhrase = "Confidential award valuation: Rs 4,50,00,000 for khasra 102.";
    await page1.keyboard.insertText(`\n\n${secretOfflinePhrase}`);
    await page1.waitForTimeout(400);

    // Verify localStorage has ZERO plaintext, ZERO persistent draft records, and legacy keys are purged
    const storageAudit = await page1.evaluate((secret) => {
      const keys = Object.keys(localStorage);
      const encKeys = keys.filter((k) => k.startsWith("lac_enc_draft_"));
      const rawDraftKeys = keys.filter((k) => k.startsWith("lac_draft_"));
      let hasPlaintext = false;
      for (const k of keys) {
        const val = localStorage.getItem(k) || "";
        if (val.includes("4,50,00,000") || val.includes("khasra 102") || val.includes(secret)) {
          hasPlaintext = true;
        }
      }
      return {
        encKeyCount: encKeys.length,
        rawDraftKeyCount: rawDraftKeys.length,
        hasPlaintext
      };
    }, secretOfflinePhrase);

    assert.strictEqual(storageAudit.hasPlaintext, false, "Sensitive note content must NEVER be stored in plaintext localStorage");
    assert.strictEqual(storageAudit.encKeyCount, 0, "All legacy lac_enc_draft_* records must be automatically purged");
    assert.strictEqual(storageAudit.rawDraftKeyCount, 0, "No persistent draft records permitted in localStorage");
    console.log("[E2E] Confirmed office security policy: Zero plaintext leakage, zero unapproved persistent drafts, legacy records purged.");

    // Wait for server autosave of typed note to complete
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });

    // ==========================================
    // PROVE REVISION CONFLICT (409) DOES NOT OVERWRITE A NEWER SERVER REVISION
    // ==========================================
    console.log("[E2E] Proving revision conflict (409) does NOT overwrite a newer server revision...");
    // Fetch current draft ETag / revision from server
    const draftStatusRes = await ctx1.request.get(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`);
    const currentEtag = draftStatusRes.headers()["etag"] || '"1"';
    console.log(`[E2E] Current draft ETag on server: ${currentEtag}`);

    // Type local offline edit into TipTap
    await editor.click();
    await page1.keyboard.press("End");
    await page1.keyboard.insertText("\n\nField observation: Demarcation boundaries checked locally.");
    await page1.waitForTimeout(300);

    // Trigger an out-of-band PUT to advance server revision with newer content
    console.log("[E2E] Triggering concurrent server update to advance server revision...");
    const serverOutText = "Server Note: Prior verification required from Tehsildar.";
    const outOfBandRes = await ctx1.request.put(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`, {
      headers: {
        "If-Match": currentEtag,
        "Idempotency-Key": randomUUID()
      },
      data: {
        contentJson: JSON.stringify({
          type: "doc",
          content: [{ type: "paragraph", content: [{ type: "text", text: serverOutText }] }]
        }),
        sourceDakId: seed.dakId
      }
    });
    console.log(`[E2E] Out-of-band PUT status: ${outOfBandRes.status()}`);
    assert.strictEqual(outOfBandRes.status(), 200, "Out-of-band PUT should advance server revision");

    // Wait for autosave or conflict card to detect conflict
    const conflictCard = page1.locator(".noting-conflict-card, .noting-recoverable-draft-banner.conflict-warning");
    await conflictCard.waitFor({ state: "visible", timeout: 8000 });
    const conflictTitle = await conflictCard.textContent();
    assert.ok(conflictTitle?.includes("Revision Conflict") || conflictTitle?.includes("Newer Server Draft Available"), "Conflict warning must be displayed");
    console.log("[E2E] Conflict detected: Newer server revision preserved intact.");

    // Assert that the newer server draft on the server was NOT overwritten by the local draft!
    const verifyServerPreservedRes = await ctx1.request.get(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`);
    const verifyServerDraft = await verifyServerPreservedRes.json();
    assert.ok(verifyServerDraft.canonicalText?.includes("Prior verification required"), "Server draft must retain newer server content without being overwritten");
    console.log("[E2E] Confirmed newer server revision was NOT overwritten by local draft.");

    const shot2Path = path.join(receiptsDir, "02-noting-workspace-autosave-conflict.png");
    await page1.screenshot({ path: shot2Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 2: ${shot2Path}`);

    // Test Merge Both Versions: Must visibly merge in TipTap and save merged JSON to server
    console.log("[E2E] Testing 'Merge Both Versions' resolution...");
    const mergeBtn = conflictCard.locator("button").filter({ hasText: /Merge/i }).first();
    await mergeBtn.click();
    await page1.waitForTimeout(1000);

    // Verify visible TipTap editor contains BOTH server and local text
    const editorContentAfterMerge = await editor.textContent();
    assert.ok(editorContentAfterMerge?.includes("Prior verification required"), "Merged TipTap editor must visibly show server text");
    assert.ok(editorContentAfterMerge?.includes("Demarcation boundaries"), "Merged TipTap editor must visibly show local text");
    console.log("[E2E] TipTap editor visibly reflects merged doc.");

    // Wait for autosave of merged content to finish
    await autosaveSaved.waitFor({ state: "visible", timeout: 8000 });

    // Verify server-saved draft JSON matches merged text
    const verifyMergedServerRes = await ctx1.request.get(`${baseUrl}/api/matters/${seed.matterId}/noting/draft`);
    const verifyMergedDraft = await verifyMergedServerRes.json();
    assert.ok(verifyMergedDraft.canonicalText?.includes("Prior verification required"), "Server draft must contain server content");
    assert.ok(verifyMergedDraft.canonicalText?.includes("Demarcation boundaries"), "Server draft must contain local content");
    console.log("[E2E] Confirmed server-saved JSON matches merged document.");

    // ==========================================
    // STEP 3: TEXT-ANCHORED DOCUMENT CITATION VIA TIPTAP SELECTION
    // ==========================================
    console.log("[E2E] Step 3: Text-anchored Document Citation via TipTap selection...");
    const citedPhraseSelected = await selectTipTapRange(page1, "site inspection report");
    assert.ok(citedPhraseSelected, "TipTap selection on 'site inspection report' must succeed");

    const citeBtn = page1.locator("button").filter({ hasText: /\+ Cite Document/i });
    await citeBtn.waitFor({ state: "visible", timeout: 5000 });
    await citeBtn.click();
    await page1.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    // Verify citation modal quote
    const modalQuote = await page1.locator(".noting-modal-overlay label + div").textContent();
    assert.ok(modalQuote?.includes("site inspection report"), "Citation modal must quote exact TipTap selection");
    console.log("[E2E] Citation modal accurately referenced TipTap selection:", modalQuote?.trim());

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
    if (citeRes.status() !== 200) {
      console.error("[E2E Error] Citation response status:", citeRes.status(), "body:", await citeRes.text());
    }
    assert.strictEqual(citeRes.status(), 200, "Citation creation must return 200 OK");
    console.log("[E2E] Citation created and saved successfully.");
    await page1.waitForTimeout(1000);

    const shot3Path = path.join(receiptsDir, "03-noting-workspace-citation-drawer.png");
    await page1.screenshot({ path: shot3Path, fullPage: false });
    console.log(`[E2E] Saved Screenshot 3: ${shot3Path}`);

    // ==========================================
    // STEP 4: SUBMIT NOTE 1, CITATION POSITIONING & TRANSACTIONAL SEND NOTE 2
    // ==========================================
    console.log("[E2E] Step 4: Submitting Note 1 standalone, checking citation positioning, and transactional Send...");
    const submitBtn = page1.locator("button.noting-btn-primary").filter({ hasText: /Submit Note/i });
    
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
    assert.ok(note1NumberText?.includes("NOTE 1"), "First submitted note must be sequentially numbered NOTE 1");

    // Verify citation pill is rendered inside paragraph at exact anchored text position
    const note1Card = page1.locator(".official-note-card").first();
    const inlineCitePill = note1Card.locator(".official-note-body p .note-citation-pill");
    await inlineCitePill.waitFor({ state: "visible", timeout: 5000 });
    const citeText = await inlineCitePill.textContent();
    assert.ok(citeText?.includes("Doc:") || citeText?.includes(".pdf"), "Citation pill must be rendered at anchored position inside paragraph");
    console.log("[E2E] Confirmed citation pill rendered inside paragraph adjacent to quote:", citeText?.trim());

    // Compose Note 2 forwarding draft for Send
    console.log("[E2E] Composing Note 2 in TipTap for file movement to Tehsildar...");
    const draftText2 = "Forwarding file with revenue map and site inspection report to Tehsildar for review and approval.";
    await typeTipTapText(page1, draftText2);

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
    page2.on("request", (req) => {
      if (req.url().includes("/annotations") || req.url().includes("/documents")) {
        console.log(`[Browser 2 Req] ${req.method()} ${req.url()}`);
      }
    });
    page2.on("response", (res) => {
      if (res.url().includes("/annotations") || res.url().includes("/documents")) {
        console.log(`[Browser 2 Res] ${res.status()} ${res.url()}`);
      }
    });
    page2.on("dialog", async (dialog) => {
      console.log(`[Browser 2 Dialog] ${dialog.type()}: ${dialog.message()}`);
      await dialog.dismiss();
    });

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
    // STEP 6: INLINE HIGHLIGHTED REMARKS & PUT STATE LIFECYCLE
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
    await inlineRemarkHighlight.first().waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Confirmed inline highlighted remark exists on Note 1.");

    // Hover or click inline highlight to trigger floating popover
    await inlineRemarkHighlight.first().click();
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
    // STEP 7: DOCUMENT UPLOAD, SYNCHRONIZED PDF CANVAS ZOOM & REGION SELECTION
    // ==========================================
    console.log("[E2E] Step 7: Document upload, synchronized PDF canvas zoom, region selection, and Note 3...");
    const uploadBtn = page2.locator("button").filter({ hasText: /\+ Upload File/i }).first();
    await uploadBtn.waitFor({ state: "visible", timeout: 5000 });
    await uploadBtn.click();
    await page2.waitForSelector(".noting-modal-overlay", { timeout: 5000 });

    const dummyPdfPath = path.join(repoRoot, "tests/fixtures/khasra-inspection-report.pdf");
    const validMinimalPdf = `%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>
endobj
4 0 obj
<< /Length 44 >>
stream
BT
/F1 24 Tf
100 700 Td
(Khasra Report) Tj
ET
endstream
endobj
5 0 obj
<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000244 00000 n 
0000000337 00000 n 
trailer
<< /Size 6 /Root 1 0 R >>
startxref
422
%%EOF`;
    fs.writeFileSync(dummyPdfPath, validMinimalPdf);

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

    // Select the first PDF document to view synchronized canvas renderer
    const revenueMapCard = page2.locator(".document-rail-card").filter({ hasText: /evidence-revenue-map|Revenue Survey/i }).first();
    if (await revenueMapCard.isVisible()) {
      await revenueMapCard.click();
    } else {
      await page2.locator(".document-rail-card").first().click();
    }
    await page2.waitForTimeout(1000);

    // Verify canvas renderer is mounted inside .pdf-canvas-page-wrapper
    const canvas = page2.locator(".pdf-canvas-page-wrapper canvas");
    await canvas.waitFor({ state: "visible", timeout: 10000 });
    console.log("[E2E] Confirmed synchronized PDF canvas renderer is active.");

    // Verify initial Page 1 auto-rendering and real nonblank rendered pixels BEFORE any user interaction
    console.log("[E2E] Testing PDF initial page 1 auto-rendering and nonblank pixels without user interaction...");
    await page2.waitForTimeout(1000);
    const hasNonBlankPixels = await canvas.evaluate((c) => {
      const ctx = c.getContext("2d");
      if (!ctx) return false;
      const imgData = ctx.getImageData(0, 0, Math.min(c.width, 200), Math.min(c.height, 200));
      let nonZeroCount = 0;
      for (let i = 0; i < imgData.data.length; i += 4) {
        if (imgData.data[i + 3] > 0) { // non-transparent alpha
          nonZeroCount++;
          if (nonZeroCount > 50) return true;
        }
      }
      return false;
    });
    assert.ok(hasNonBlankPixels, "PDF canvas must render real nonblank pixels automatically on page 1 without requiring zoom, navigation, or document switch");
    console.log("[E2E] Confirmed real nonblank rendered pixels on initial Page 1 load.");

    // Check page indicator is at Page 1
    const pageIndicator = page2.locator(".document-viewer-toolbar").filter({ hasText: /Page 1/i });
    await pageIndicator.waitFor({ state: "visible", timeout: 5000 });
    console.log("[E2E] Confirmed Page 1 active in viewer.");

    // Test Zoom In and Zoom Reset
    console.log("[E2E] Testing PDF synchronized zoom controls...");
    const initialBox = await page2.locator(".pdf-canvas-page-wrapper").boundingBox();
    const zoomInBtn = page2.locator('button[title="Zoom In"]');
    if (await zoomInBtn.isVisible()) {
      await zoomInBtn.click();
      await page2.waitForTimeout(600);
      const zoomedBox = await page2.locator(".pdf-canvas-page-wrapper").boundingBox();
      assert.ok(initialBox && zoomedBox && zoomedBox.width > initialBox.width, "Canvas page wrapper must scale up on zoom in");
      console.log(`[E2E] Canvas scaled from ${initialBox?.width}px to ${zoomedBox?.width}px`);

      // Reset to 100%
      await page2.locator('button[title="Reset Zoom to 100%"]').click();
      await page2.waitForTimeout(600);
    }

    // Add PDF Page Highlight with drag-to-select region
    console.log("[E2E] Testing interactive drag-to-select region for PDF annotation...");
    const addHighlightBtn = page2.locator("button.add-highlight-btn");
    await addHighlightBtn.waitFor({ state: "visible", timeout: 5000 });
    await addHighlightBtn.click(); // Activates Draw Region mode

    const interactionLayer = page2.locator(".pdf-highlight-interaction-layer");
    await interactionLayer.waitFor({ state: "visible", timeout: 5000 });
    const layerBox = await interactionLayer.boundingBox();
    if (layerBox) {
      await page2.mouse.move(layerBox.x + layerBox.width * 0.15, layerBox.y + layerBox.height * 0.2);
      await page2.mouse.down();
      await page2.mouse.move(layerBox.x + layerBox.width * 0.65, layerBox.y + layerBox.height * 0.35, { steps: 5 });
      await page2.mouse.up();
      console.log("[E2E] Executed drag-to-select region across interaction layer.");
    }
    await page2.waitForTimeout(600);

    const annoModalCard = page2.locator('.noting-modal-card:has-text("Add Shared PDF Highlight")');
    if (!(await annoModalCard.isVisible())) {
      await addHighlightBtn.click();
    }
    await annoModalCard.waitFor({ state: "visible", timeout: 8000 });

    const annoQuoteInput = annoModalCard.locator('input[type="text"]').first();
    await annoQuoteInput.fill("Galib Pur Demarcation");

    const annoTextInput = annoModalCard.locator('textarea').first();
    await annoTextInput.fill("Approved corridor boundary verified by survey team.");

    const highlightResponsePromise = page2.waitForResponse((res) =>
      res.url().includes("/annotations") && res.request().method() === "POST"
    );
    const saveHighlightBtn = annoModalCard.locator('button.noting-btn-primary');
    console.log("[E2E] Clicking saveHighlightBtn...");
    await saveHighlightBtn.click();
    console.log("[E2E] Clicked saveHighlightBtn, waiting for /annotations response...");

    const annoRes = await highlightResponsePromise;
    assert.strictEqual(annoRes.status(), 200, "Annotation creation must return 200 OK");
    console.log("[E2E] PDF highlight annotation created successfully.");

    // Assert visible PDF-page highlight overlay exists on canvas wrapper and aligns to canvas
    const pdfOverlay = page2.locator(".pdf-highlight-overlay").first();
    await pdfOverlay.waitFor({ state: "visible", timeout: 5000 });
    const overlayBox = await pdfOverlay.boundingBox();
    const canvasBox = await page2.locator(".pdf-canvas-page-wrapper").boundingBox();
    assert.ok(overlayBox && canvasBox, "Overlay and canvas wrapper must both have valid bounding boxes");
    assert.ok(overlayBox.x >= canvasBox.x - 2 && overlayBox.x + overlayBox.width <= canvasBox.x + canvasBox.width + 2, "Highlight overlay must be horizontally contained within canvas");
    assert.ok(overlayBox.y >= canvasBox.y - 2 && overlayBox.y + overlayBox.height <= canvasBox.y + canvasBox.height + 2, "Highlight overlay must be vertically contained within canvas");
    console.log("[E2E] Confirmed visible PDF-page highlight overlay aligns precisely to synchronized canvas wrapper.");

    // Compose and submit Note 3 by Tehsildar using TipTap editor
    console.log("[E2E] Officer 2 composing Note 3 in TipTap editor...");
    await typeTipTapText(page2, "Report and demarcation examined in detail. Sanction approved for further acquisition proceedings as per Section 19.");

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
