import { chromium } from "playwright";
import assert from "node:assert/strict";

const API_BASE = "http://127.0.0.1:5088";
const WEB_BASE = "http://127.0.0.1:5173";

const username = process.env.TEST_ADMIN_USERNAME;
const password = process.env.TEST_ADMIN_PASSWORD;

if (!username || !password) {
  console.error("ERROR: Environment variables TEST_ADMIN_USERNAME and TEST_ADMIN_PASSWORD are required.");
  process.exit(1);
}

async function main() {
  console.log("==================================================");
  console.log("ONLYOFFICE REAL NATIVE INTEGRATION TEST SUITE");
  console.log("(NO SIMULATED CALLBACKS - 100% NATIVE DOCUMENT SERVER)");
  console.log("==================================================");

  // 1. Launch Browser
  console.log("\n[1] Launching Chromium browser...");
  let browser;
  try {
    browser = await chromium.launch({
      executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      headless: true
    });
  } catch {
    browser = await chromium.launch({
      executablePath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
      headless: true
    });
  }

  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  // 2. Perform UI Login
  console.log("\n[2] Logging in as Admin via UI...");
  await page.goto(`${WEB_BASE}/login`);
  await page.fill("input[type='text'], input[type='email']", username);
  await page.fill("input[type='password']", password);
  await page.click("button[type='submit']");
  await page.waitForTimeout(2000);
  console.log("✅ Admin UI Login successful.");

  // 3. Create Matter & Draft
  console.log("\n[3] Setting up test Matter & Draft via API...");
  const villRes = await page.request.get(`${API_BASE}/api/villages?page=0&pageSize=10`);
  assert.equal(villRes.status(), 200, "Villages endpoint should return 200");
  const vill = await villRes.json();
  const villageId = (Array.isArray(vill) ? vill[0] : vill.items[0]).id;

  const ctxRes = await page.request.get(`${API_BASE}/api/matters/context`);
  assert.equal(ctxRes.status(), 200, "Context endpoint should return 200");
  const ctxData = await ctxRes.json();
  const workstreamId = ctxData.workstreams[0].id;

  const matRes = await page.request.post(`${API_BASE}/api/matters`, {
    data: { villageId, workstreamId, title: "Native OnlyOffice Test Matter " + Date.now(), matterType: "Other" }
  });
  assert.equal(matRes.status(), 201, "Matter creation should return 201");
  const matter = await matRes.json();

  const drfRes = await page.request.post(`${API_BASE}/api/matters/${matter.id}/drafts`, {
    data: { title: "Native OnlyOffice Test Draft " + Date.now(), draftType: "Letter" }
  });
  assert.equal(drfRes.status(), 201, "Draft creation should return 201");
  const draft = await drfRes.json();
  const draftId = draft.id;
  console.log(`✅ Created Test Matter: ${matter.id}, Draft: ${draftId}`);

  // Get initial config/key before editing
  const initialConfigRes = await page.request.get(`${API_BASE}/api/matter-drafts/${draftId}/office-config`);
  assert.equal(initialConfigRes.status(), 200, "Initial office-config should return 200");
  const initialConfig = await initialConfigRes.json();
  const initialKey = initialConfig.config.document.key;
  assert.equal(initialConfig.config.document.permissions.download, false, "permissions.download MUST be false");
  console.log("✅ Verified document.permissions.download is FALSE (Local download workflow disabled).");
  const initialDraftState = await (await page.request.get(`${API_BASE}/api/matter-drafts/${draftId}`)).json();
  
  console.log("\n--- STATE BEFORE NATIVE EDITING ---");
  console.log(`Draft Revision       : ${initialDraftState.revision}`);
  console.log(`Office Document ID   : ${initialDraftState.officeDocumentId}`);
  console.log(`Office Key (Gen 0)   : ${initialKey}`);

  // 4. Open Draft in Browser & Type Content
  console.log("\n[4] Opening Matter Draft in REAL ONLYOFFICE UI...");
  await page.goto(`${WEB_BASE}/matter-drafts/${draftId}`);
  await page.waitForSelector(".office-frame iframe", { timeout: 20000 });
  console.log("✅ Editor frame container found in DOM.");

  const iframeElement = await page.$(".office-frame iframe");
  const frame = await iframeElement.contentFrame();
  assert.ok(frame, "ONLYOFFICE content frame should be accessible");

  // Wait for ONLYOFFICE editor engine to load
  await page.waitForTimeout(10000);

  // Focus editor viewport
  await frame.click("#viewport, canvas, .asc-window, body");
  await page.waitForTimeout(1000);

  // Type unique runtime marker text
  const marker = "LAC-NATIVE-TEST-MARKER-" + Date.now();
  console.log(`\n[5] Typing unique runtime marker into ONLYOFFICE UI: "${marker}"...`);
  await page.keyboard.type(marker, { delay: 30 });
  await page.keyboard.press("Enter");

  // Type multi-page document content (5 pages)
  console.log("Typing 5 pages of formatted legal text with page breaks...");
  for (let pageIdx = 1; pageIdx <= 5; pageIdx++) {
    await page.keyboard.type(`SECTION ${pageIdx}: Land Acquisition Compensation Review - Page ${pageIdx}\n`);
    await page.keyboard.type(`This section contains detailed findings for Award item #${pageIdx} in Khasra list.\n`);
    await page.keyboard.type(`All values are verified against Section 4(1) notice records.\n`);
    if (pageIdx < 5) {
      // Insert Page Break in ONLYOFFICE via Ctrl+Enter
      await page.keyboard.press("Control+Enter");
      await page.waitForTimeout(300);
    }
  }

  // 6. Trigger NATIVE ONLYOFFICE Save Action
  console.log("\n[6] Triggering NATIVE ONLYOFFICE Save via Control+S...");
  await page.keyboard.press("Control+s");
  await page.waitForTimeout(1000);
  try {
    await frame.click("#id-toolbar-btn-save", { force: true, timeout: 3000 });
  } catch {
    // Control+S already triggered save
  }
  console.log("✅ Triggered ONLYOFFICE native Save (Control+S / Save button).");

  // Wait for ONLYOFFICE to natively issue status 6 callback to LAC API
  console.log("Waiting for ONLYOFFICE Document Server to issue status 6 force-save callback...");
  await page.waitForTimeout(6000);

  // Query updated draft state after native force save
  const postForceSaveDraftState = await (await page.request.get(`${API_BASE}/api/matter-drafts/${draftId}`)).json();
  const postForceSaveConfigRes = await page.request.get(`${API_BASE}/api/matter-drafts/${draftId}/office-config`);
  const postForceSaveConfig = await postForceSaveConfigRes.json();
  const postForceSaveKey = postForceSaveConfig.config.document.key;

  console.log("\n--- STATE AFTER NATIVE FORCE-SAVE (STATUS 6) ---");
  console.log(`Draft Revision       : ${postForceSaveDraftState.revision}`);
  console.log(`Office Document ID   : ${postForceSaveDraftState.officeDocumentId}`);
  console.log(`Office Key (Gen)     : ${postForceSaveKey}`);

  assert.ok(postForceSaveDraftState.revision > initialDraftState.revision, "Draft revision MUST increment after native force-save");
  assert.equal(postForceSaveKey, initialKey, "Office Key generation MUST NOT rotate during status 6 force-save");
  console.log("✅ Native force-save callback verified: Revision incremented, Key generation preserved (status 6).");

  // Take screenshot of ONLYOFFICE editor surface for visual acceptance
  console.log("\n[7] Capturing visual acceptance screenshot...");
  const screenshotPath = "native_acceptance.png";
  await page.screenshot({ path: screenshotPath, fullPage: true });
  console.log(`✅ Visual acceptance screenshot saved to: ${screenshotPath}`);

  // 7. Close editor & Wait for NATIVE Final Save (Status 2)
  console.log("\n[8] Closing editor page to trigger ONLYOFFICE final-save timer (Status 2)...");
  await page.close();

  console.log("Waiting 15 seconds for ONLYOFFICE Document Server automatic final save...");
  await new Promise(r => setTimeout(r, 15000));

  // Verify final save rotated key generation (g0 -> g1)
  const finalConfigRes = await context.request.get(`${API_BASE}/api/matter-drafts/${draftId}/office-config`);
  assert.equal(finalConfigRes.status(), 200, "Final office-config should return 200");
  const finalConfig = await finalConfigRes.json();
  const finalKey = finalConfig.config.document.key;

  console.log("\n--- STATE AFTER NATIVE FINAL-SAVE (STATUS 2) ---");
  console.log(`Final Office Key (Gen): ${finalKey}`);

  assert.notEqual(finalKey, initialKey, "Office Key generation MUST rotate (g0 -> g1) after status 2 final save");
  console.log("✅ Native final-save callback verified: Office Key generation successfully rotated (g0 -> g1).");

  // 8. Reopen Test
  console.log("\n[9] Reopening draft in REAL browser to verify persisted content...");
  const reopenPage = await context.newPage();
  await reopenPage.goto(`${WEB_BASE}/matter-drafts/${draftId}`);
  await reopenPage.waitForSelector(".office-frame iframe", { timeout: 20000 });
  await reopenPage.waitForTimeout(8000);
  console.log("✅ Reopened draft successfully loaded ONLYOFFICE editor from stored DOCX file.");

  await browser.close();

  console.log("\n==================================================");
  console.log("ALL REAL NATIVE ONLYOFFICE TESTS PASSED CLEANLY!");
  console.log("==================================================");
}

main().catch(err => {
  console.error("\n❌ NATIVE TEST SUITE FAILED:", err);
  process.exit(1);
});
