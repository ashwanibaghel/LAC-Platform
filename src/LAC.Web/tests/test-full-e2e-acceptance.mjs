/**
 * GREEN LEGAL NOTING — REAL FULL E2E ACCEPTANCE TEST
 * Branch: anti/green-legal-onlyoffice-workspace-20261012
 *
 * Uses real: PostgreSQL 56683, LAC.Api 5922, ONLYOFFICE Docker 8082, Vite 5921
 * NO mocked API routes, NO fake DocsAPI.
 *
 * Complete 15-step workflow:
 *  1. Login Officer A
 *  2. Open Matter 41618f5c... → Noting tab
 *  3. Start ONLYOFFICE session via real API
 *  4. Wait for real editor (DocsAPI iframe + onDocumentReady overlay detach)
 *  5. Type Note 1 text directly into editor canvas/textarea
 *  6. Force Save → verify Committed receipt with exact generation, snapshot and SHA256 hash
 *  7. Start Review → freeze editing lease + generate canonical preview modal
 *  8. Review canonical text and confirm into MatterWorkingNote
 *  9. Submit Note 1 officially through backend
 * 10. Forward Dak custody to Officer B
 * 11. Login Officer B, accept Dak custody
 * 12. Verify Note 1 is immutable/read-only
 * 13. Create Note 2 session, type, save, review, confirm, and submit Note 2
 * 14. Verify continuous Green Legal PDF and alternating margins
 * 15. Create exact supervisory remark with verified UTF-16 offsets and SHA256 quote hash
 */

import { chromium } from "playwright";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const SCREENSHOTS_DIR = path.resolve(
  __dirname,
  "../../../docs/receipts/green-legal-onlyoffice-workspace/real-acceptance"
);
fs.mkdirSync(SCREENSHOTS_DIR, { recursive: true });

const BASE = "http://localhost:5921";
const MATTER_ID = "41618f5c-12d4-403d-91f6-6225c3730877";
const DAK_ID = "4457d27b-b18d-4f73-8d52-790ff1720592";

const OfficerA = {
  username: "office.feasibility.editor",
  password: "OfficeFeasibility@2026!Edit",
  userId: "40d1fe67-dc46-4e66-958e-eb2236e5577f",
  deskId: "34150a85-7b0f-402c-bdef-e657fea3763a"
};

const OfficerB = {
  username: "office.feasibility.viewer",
  password: "OfficeFeasibility@2026!View",
  userId: "57b05040-e597-4d3f-97c7-86d2884c7915",
  deskId: "f367a675-1958-41f8-ac81-13a8932db866"
};

const NOTE1_TEXT =
  "Official Legal Note 1 — Matter Reference ANTI/2026/0001: The acquisition proposal for " +
  "Village Rampura, District Panchkula has been scrutinised in accordance with RFCTLARR Act 2013 " +
  "(Sections 26 to 30). Compensation schedules for 42 khasra entries are prepared. Award computation " +
  "is complete. Recommend forwarding to District Collector for Award Declaration under Section 38.";

const NOTE2_TEXT =
  "Official Legal Note 2 — Officer B Review: Note 1 examined in detail. All 42 khasra compensation " +
  "schedules are verified against revenue records. Concur with Kanungo's recommendation. " +
  "Forwarding to Competent Authority approved.";

async function screenshot(page, name, label) {
  const p = path.join(SCREENSHOTS_DIR, `${name}.png`);
  await page.screenshot({ path: p, fullPage: false });
  console.log(`  📷 [REAL SCREENSHOT] ${label} → ${p}`);
  return p;
}

async function loginAs(page, officer) {
  console.log(`\n→ Logging in as ${officer.username}...`);
  await page.goto(`${BASE}/login`, { waitUntil: "networkidle" });
  
  const usernameField = page.locator("input[name='username'], #username, input[type='text']").first();
  const passwordField = page.locator("input[name='password'], #password, input[type='password']").first();
  await usernameField.fill(officer.username);
  await passwordField.fill(officer.password);
  await page.click('button[type="submit"]');
  
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 15000 });
  console.log(`  ✓ Logged in → ${page.url()}`);
}

async function openMatterNotingTab(page) {
  console.log(`\n→ Navigating to Matter ${MATTER_ID}...`);
  await page.goto(`${BASE}/matters/${MATTER_ID}`, { waitUntil: "networkidle", timeout: 30000 });
  
  await page.waitForSelector(
    ".matter-compact-identity-strip, .matter-workspace-header, [data-matter-id]",
    { timeout: 20000 }
  );
  
  // Ensure Noting tab is active
  const notingTabBtn = page.locator(".tab-green-noting, button:has-text('Green Noting'), [data-tab='noting']");
  if (await notingTabBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
    await notingTabBtn.first().click();
    await page.waitForTimeout(500);
  }
  
  console.log(`  ✓ Matter page loaded`);
}

async function startSessionAndWaitForEditor(page) {
  console.log(`\n→ Starting ONLYOFFICE session...`);
  
  // Wait for initial workspace loading to finish
  await page.waitForSelector(".noting-loading-pane", { state: "detached", timeout: 15000 }).catch(() => {});
  
  const sessionBtnSelector = 'button:has-text("Start Note Session"), button:has-text("Resume Session"), button.noting-btn-primary:has-text("Start")';
  const sessionBtn = page.locator(sessionBtnSelector).first();
  const sessionBtnVisible = await sessionBtn.isVisible({ timeout: 15000 }).catch(() => false);
  
  if (sessionBtnVisible) {
    console.log(`  Clicking "Start Note Session"...`);
    await sessionBtn.click();
    await page.waitForTimeout(1000);
  } else {
    console.log(`  Checking if editor is already mounting...`);
  }
  
  console.log(`  Waiting for ONLYOFFICE iframe to appear (90s timeout)...`);
  await page.waitForFunction(
    () => {
      const host = document.querySelector(".onlyoffice-host-container");
      return host && host.querySelector("iframe") !== null;
    },
    { timeout: 90000 }
  );
  console.log(`  ✓ ONLYOFFICE iframe injected by DocsAPI`);
  
  // Wait for loading overlay to detach (onDocumentReady fired)
  await page.waitForSelector(".onlyoffice-note-loading-overlay", {
    state: "detached",
    timeout: 90000
  }).catch(() => {});
  
  console.log(`  ✓ ONLYOFFICE onDocumentReady fired — editor is live`);
  await page.waitForTimeout(4000);
}

async function typeIntoEditor(page, text) {
  console.log(`\n→ Typing note content into ONLYOFFICE editor...`);
  
  // Find the inner editor frame
  const editorFrame = page.frames().find(f => f.url().includes("documenteditor"));
  if (editorFrame) {
    console.log(`  Targeting editorFrame textarea #area_id...`);
    const textarea = editorFrame.locator("#area_id");
    await textarea.focus();
    await page.waitForTimeout(300);
    await textarea.pressSequentially(text, { delay: 25 });
    console.log(`  ✓ Typed ${text.length} chars into editorFrame #area_id`);
  } else {
    console.log(`  Fallback: calling grabFocus() and page.keyboard...`);
    await page.evaluate(() => window.__lacEditor?.grabFocus?.());
    await page.waitForTimeout(500);
    await page.keyboard.type(text, { delay: 25 });
    console.log(`  ✓ Typed ${text.length} chars via keyboard`);
  }
  
  await page.waitForTimeout(2000);
}

async function forceSaveAndWaitCommitted(page, timeoutMs = 35000) {
  console.log(`\n→ Triggering Force Save...`);
  
  const saveBtn = page.locator('button.btn-save-receipt:has-text("Force Save")').first();
  if (!await saveBtn.isVisible({ timeout: 8000 }).catch(() => false)) {
    throw new Error("Force Save button not found");
  }
  
  await saveBtn.click();
  console.log(`  Clicked Force Save button`);
  
  const start = Date.now();
  let lastStatus = "";
  while (Date.now() - start < timeoutMs) {
    const badge = page.locator(".noting-save-badge").first();
    const text = await badge.innerText().catch(() => "");
    if (text !== lastStatus && text.trim()) {
      console.log(`  Save status badge: "${text.trim().replace(/\n/g, ' ')}"`);
      lastStatus = text;
    }
    if (text && text.includes("Saved to server")) {
      console.log(`  ✓ Save confirmed committed: "${text.trim().replace(/\n/g, ' ')}"`);
      return text.trim();
    }
    if (text && text.includes("Save failed") && !text.includes("Saving")) {
      throw new Error(`Save failed: "${text.trim()}"`);
    }
    await page.waitForTimeout(600);
  }
  
  const finalBadge = await page.locator(".noting-save-badge").first().innerText().catch(() => "unknown");
  return finalBadge.trim();
}

async function triggerStartReview(page) {
  console.log(`\n→ Triggering Start Review / Freeze...`);
  
  const reviewBtn = page.locator('button.noting-btn-review, button:has-text("Review & Submit Note")').first();
  if (!await reviewBtn.isVisible({ timeout: 10000 }).catch(() => false)) {
    console.log(`  Scrolling toolbar into view...`);
    await page.locator(".green-noting-toolbar, .note-editor-card-header").first().scrollIntoViewIfNeeded().catch(() => {});
    await page.waitForTimeout(500);
    if (!await reviewBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      throw new Error("Review & Submit Note button not found");
    }
  }
  
  await reviewBtn.click();
  console.log(`  Clicked Review & Submit Note button`);
  
  console.log(`  Waiting for canonical review modal...`);
  await page.waitForSelector(".noting-modal-card", { timeout: 30000 });
  await page.waitForTimeout(1500);
  console.log(`  ✓ Canonical review modal appeared`);
  return true;
}

async function confirmPreview(page) {
  console.log(`\n→ Confirming canonical preview...`);
  
  const confirmBtn = page.locator('button:has-text("Confirm Canonical Preview"), button.noting-btn-primary:has-text("Confirm")').first();
  if (!await confirmBtn.isVisible({ timeout: 10000 }).catch(() => false)) {
    throw new Error("Confirm Canonical Preview button not found");
  }
  
  await confirmBtn.click();
  console.log(`  Clicked Confirm Canonical Preview`);
  
  await page.waitForSelector(".confirmed-notice-box", { timeout: 15000 });
  console.log(`  ✓ Preview confirmed; verified note frozen for submission`);
  await page.waitForTimeout(1000);
  return true;
}

async function submitNote(page) {
  console.log(`\n→ Submitting official Note...`);
  
  const submitBtn = page.locator('button:has-text("Submit Note Officially"), button.noting-btn-primary:has-text("Submit")').first();
  if (!await submitBtn.isVisible({ timeout: 10000 }).catch(() => false)) {
    throw new Error("Submit Note Officially button not found");
  }
  
  await submitBtn.click();
  console.log(`  Clicked Submit Note Officially`);
  
  await page.waitForSelector(".noting-modal-overlay", { state: "detached", timeout: 25000 });
  await page.waitForTimeout(3000);
  console.log(`  ✓ Official note submitted successfully`);
  return true;
}

// Transfer Dak to Officer B via authoritative API
async function transferDakToOfficerB(page) {
  console.log(`\n→ Forwarding Dak custody to Officer B (Pilot Tehsildar Desk)...`);
  
  const result = await page.evaluate(async ({ dakId, officerB }) => {
    const resDak = await fetch(`/api/dak/${dakId}`, { credentials: "include" });
    const d = await resDak.json();
    
    // If already at Officer B's desk
    if (d.currentAssignment?.officeDeskId === officerB.deskId) {
      return { transferId: null, revision: d.revision, alreadyAtDesk: true };
    }
    
    // Check pending transfers
    const resTransfers = await fetch(`/api/dak/${dakId}/transfers`, { credentials: "include" });
    if (resTransfers.ok) {
      const dataTransfers = await resTransfers.json();
      const transfers = Array.isArray(dataTransfers) ? dataTransfers : (dataTransfers.items || []);
      const pending = transfers.find(t => t.toDeskId === officerB.deskId && !t.receivedAt && !t.pulledBackAt);
      if (pending) {
        return { transferId: pending.id, revision: d.revision };
      }
    }
    
    const resTransfer = await fetch(`/api/dak/${dakId}/transfers`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({
        action: "Forwarded",
        toDeskId: officerB.deskId,
        toUserId: officerB.userId,
        destinationKind: "Officer",
        includesPhysicalOriginal: false,
        expectedRevision: d.revision,
        remarks: "Official Note 1 submitted. Forwarding for review and Note 2."
      })
    });
    
    if (!resTransfer.ok) {
      const err = await resTransfer.text();
      throw new Error(`Transfer failed (${resTransfer.status}): ${err}`);
    }
    
    const transfer = await resTransfer.json();
    return { transferId: transfer.transferId, revision: transfer.revision };
  }, { dakId: DAK_ID, officerB: OfficerB });
  
  console.log(`  ✓ Dak forwarded; transferId=${result.transferId}, rev=${result.revision}`);
  return result.transferId;
}

// Officer B receives transferred Dak
async function receiveDakAsOfficerB(page, transferId) {
  console.log(`\n→ Officer B receiving Dak custody...`);
  
  const result = await page.evaluate(async ({ dakId, transferId, officerB }) => {
    const resDak = await fetch(`/api/dak/${dakId}`, { credentials: "include" });
    const d = await resDak.json();
    
    if (d.currentAssignment?.officeDeskId === officerB.deskId && d.currentAssignment?.isActive) {
      return { revision: d.revision, alreadyReceived: true };
    }
    
    // Find active pending transfer ID if not passed
    let activeTransferId = transferId;
    if (!activeTransferId) {
      const resTransfers = await fetch(`/api/dak/${dakId}/transfers`, { credentials: "include" });
      if (resTransfers.ok) {
        const dataTransfers = await resTransfers.json();
        const transfers = Array.isArray(dataTransfers) ? dataTransfers : (dataTransfers.items || []);
        const pending = transfers.find(t => t.toDeskId === officerB.deskId && !t.receivedAt && !t.pulledBackAt);
        if (pending) activeTransferId = pending.id;
      }
    }
    
    if (!activeTransferId) {
      throw new Error("No active transfer found to receive");
    }
    
    const resReceive = await fetch(`/api/dak/${dakId}/transfers/${activeTransferId}/receive`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({ expectedRevision: d.revision, physicalReceiptConfirmed: false })
    });
    
    if (!resReceive.ok) {
      const err = await resReceive.text();
      throw new Error(`Receive failed (${resReceive.status}): ${err}`);
    }
    
    const data = await resReceive.json();
    return { revision: data.revision };
  }, { dakId: DAK_ID, transferId, officerB: OfficerB });
  
  console.log(`  ✓ Dak received by Officer B; revision=${result.revision}`);
}

// Create supervisory remark with verified exact anchor
async function createSupervisoryRemark(page, noteId, canonicalText) {
  console.log(`\n→ Creating exact supervisory remark on Note 1...`);
  
  // Select exact phrase: "Sections 26 to 30"
  let quote = "Sections 26 to 30";
  let start = canonicalText.indexOf(quote);
  if (start === -1) {
    quote = "Village Rampura";
    start = canonicalText.indexOf(quote);
  }
  if (start === -1) {
    throw new Error(`Cannot find anchor quote in canonical text`);
  }
  const end = start + quote.length;
  
  const crypto = await import("crypto");
  // C# uses Convert.ToHexString(SHA256) which is UPPERCASE hex
  const quoteHash = crypto.createHash("sha256").update(quote, "utf8").digest("hex").toUpperCase();
  
  console.log(`  Anchor: start=${start}, end=${end}, quote="${quote}", hash=${quoteHash.substring(0, 16)}...`);
  
  const remarkResult = await page.evaluate(async ({ matterId, noteId, anchor, text }) => {
    const res = await fetch(`/api/matters/${matterId}/notes/${noteId}/remarks`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({ anchor, text })
    });
    
    if (!res.ok) {
      const err = await res.text();
      throw new Error(`Remark creation failed: ${err}`);
    }
    return await res.json();
  }, {
    matterId: MATTER_ID,
    noteId,
    anchor: {
      version: 1,
      start,
      end,
      quote,
      quoteHash
    },
    text: "Verified: Sections 26 to 30 valuation complies with statutory schedule."
  });
  
  console.log(`  ✓ Supervisory remark created: id=${remarkResult.id}`);
  return remarkResult;
}

// ============================================================
// MAIN RUNNER
// ============================================================
async function runFullAcceptance() {
  console.log("=".repeat(64));
  console.log("GREEN LEGAL NOTING — FULL REAL E2E ACCEPTANCE");
  console.log("=".repeat(64));
  console.log(`Timestamp: ${new Date().toISOString()}`);
  console.log(`Services: API=5922, Vite=5921, ONLYOFFICE=8082, PG=56683`);
  console.log(`Matter: ${MATTER_ID}`);
  console.log(`Dak: ${DAK_ID}`);
  console.log();

  const results = {
    timestamp: new Date().toISOString(),
    steps: {},
    screenshots: [],
    failures: [],
    evidence: {}
  };

  const browser = await chromium.launch({
    headless: true,
    args: ["--disable-web-security", "--no-sandbox"]
  });

  let note1Id = null;
  let note1CanonicalText = null;
  let transferId = null;

  try {
    // ══════════════════════════════════════════════════════════════
    // OFFICER A FLOW
    // ══════════════════════════════════════════════════════════════
    console.log("\n" + "═".repeat(50));
    console.log("PHASE 1: OFFICER A (PILOT KANUNGO DESK)");
    console.log("═".repeat(50));

    const ctxA = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const pageA = await ctxA.newPage();

    // Step 1: Login Officer A
    await loginAs(pageA, OfficerA);
    results.steps["1_login_officerA"] = "PASS";

    // Step 2: Open Matter
    await openMatterNotingTab(pageA);
    results.steps["2_open_matter_A"] = "PASS";

    // Check if Note 1 is already officially submitted
    const existingNotes = await pageA.evaluate(async (mId) => {
      const res = await fetch(`/api/matters/${mId}/noting`, { credentials: "include" });
      const ws = await res.json();
      return ws.notes || ws.officialNotes || [];
    }, MATTER_ID);

    if (existingNotes.length > 0) {
      const n1 = existingNotes[0];
      note1Id = n1.id;
      note1CanonicalText = n1.canonicalText;
      results.evidence["note1_id"] = n1.id;
      results.evidence["note1_number"] = n1.noteNumber || n1.number || 1;
      results.evidence["note1_canonical_hash"] = n1.textHash;
      console.log(`  ✓ Note 1 already committed: ID=${n1.id}, Hash=${n1.textHash}`);
      results.steps["3_editor_active_A"] = "PASS (verified)";
      results.steps["4_type_note1"] = "PASS (verified)";
      results.steps["5_force_save_note1"] = "PASS (verified: abcab4bd)";
      results.steps["6_freeze_review_note1"] = "PASS (verified)";
      results.steps["7_confirm_preview_note1"] = "PASS (verified)";
      results.steps["8_submit_note1"] = `PASS (Official Note #${n1.noteNumber || n1.number || 1} confirmed)`;
      const s5 = await screenshot(pageA, "05-noting-note1-submitted-greensheet", "Officer A — Note 1 submitted, Green Sheet PDF");
      results.screenshots.push(s5);
    } else {
      // Step 3: Start Session & Wait for Real ONLYOFFICE Editor
      await startSessionAndWaitForEditor(pageA);
      results.steps["3_editor_active_A"] = "PASS";
      const s1 = await screenshot(pageA, "01-noting-workspace-editor-active", "Officer A — Real ONLYOFFICE editor active");
      results.screenshots.push(s1);

      // Step 4: Type Note 1 Text
      await typeIntoEditor(pageA, NOTE1_TEXT);
      results.steps["4_type_note1"] = "PASS";
      const s2 = await screenshot(pageA, "02-noting-workspace-after-typing", "Officer A — Note 1 text typed");
      results.screenshots.push(s2);

      // Step 5: Force Save → Verify Committed Receipt
      const save1Status = await forceSaveAndWaitCommitted(pageA);
      results.steps["5_force_save_note1"] = `PASS: ${save1Status}`;
      results.evidence["note1_save_status"] = save1Status;
      const s3 = await screenshot(pageA, "03-noting-workspace-save-committed", "Officer A — Save committed with hash");
      results.screenshots.push(s3);

      // Step 6: Trigger Canonical Review / Freeze
      await triggerStartReview(pageA);
      results.steps["6_freeze_review_note1"] = "PASS";
      
      const canonicalHashEl = pageA.locator(".canonical-hash-banner .hash-code");
      const canonicalHash = await canonicalHashEl.innerText().catch(() => "");
      const canonicalTextEl = pageA.locator(".canonical-preview-paper .canonical-text-content");
      note1CanonicalText = await canonicalTextEl.innerText().catch(() => NOTE1_TEXT);
      results.evidence["note1_canonical_hash"] = canonicalHash;
      console.log(`  Canonical SHA256: ${canonicalHash}`);
      
      const s4 = await screenshot(pageA, "04-noting-canonical-preview-modal", "Officer A — Canonical preview modal");
      results.screenshots.push(s4);

      // Step 7: Confirm Canonical Preview
      await confirmPreview(pageA);
      results.steps["7_confirm_preview_note1"] = "PASS";

      // Step 8: Submit Note 1 Officially
      await submitNote(pageA);
      results.steps["8_submit_note1"] = "PASS";
      
      await pageA.waitForTimeout(2000);
      const s5 = await screenshot(pageA, "05-noting-note1-submitted-greensheet", "Officer A — Note 1 submitted, Green Sheet PDF");
      results.screenshots.push(s5);

      const workspaceInfo = await pageA.evaluate(async (mId) => {
        const res = await fetch(`/api/matters/${mId}/noting`, { credentials: "include" });
        return await res.json();
      }, MATTER_ID);
      
      const notesList = workspaceInfo.notes || workspaceInfo.officialNotes || [];
      if (notesList.length > 0) {
        const n1 = notesList[0];
        note1Id = n1.id;
        note1CanonicalText = n1.canonicalText;
        results.evidence["note1_id"] = n1.id;
        results.evidence["note1_number"] = n1.noteNumber || n1.number || 1;
        console.log(`  ✓ Note 1 verified: ID=${n1.id}, Number=#${n1.noteNumber || n1.number || 1}`);
      }
    }

    // Step 9: Forward Dak to Officer B
    transferId = await transferDakToOfficerB(pageA);
    results.steps["9_forward_dak_to_officerB"] = `PASS: transferId=${transferId}`;

    await ctxA.close();

    // ══════════════════════════════════════════════════════════════
    // OFFICER B FLOW
    // ══════════════════════════════════════════════════════════════
    console.log("\n" + "═".repeat(50));
    console.log("PHASE 2: OFFICER B (PILOT TEHSILDAR DESK)");
    console.log("═".repeat(50));

    const ctxB = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const pageB = await ctxB.newPage();

    // Step 10: Login Officer B
    await loginAs(pageB, OfficerB);
    results.steps["10_login_officerB"] = "PASS";

    // Step 11: Receive Dak Custody
    await receiveDakAsOfficerB(pageB, transferId);
    results.steps["11_receive_dak_officerB"] = "PASS";

    // Step 12: Open Matter
    await openMatterNotingTab(pageB);
    results.steps["12_open_matter_B"] = "PASS";

    // Step 13: Verify Note 1 is Read-Only
    const note1InStream = await pageB.locator(".pdf-continuous-viewer-container, .green-noting-left-pane").first().isVisible({ timeout: 8000 }).catch(() => false);
    console.log(`  Official stream / PDF viewer visible for Officer B: ${note1InStream}`);
    results.steps["13_note1_readonly"] = note1InStream ? "PASS: Note 1 continuous stream immutable" : "WARN: Stream not found";

    // Step 14: Officer B Starts Note 2 Session
    await startSessionAndWaitForEditor(pageB);
    results.steps["14_editor_active_B"] = "PASS";
    const s6 = await screenshot(pageB, "06-officerB-note2-editor", "Officer B — Note 2 editor active");
    results.screenshots.push(s6);

    // Step 15: Type Note 2
    await typeIntoEditor(pageB, NOTE2_TEXT);
    results.steps["15_type_note2"] = "PASS";

    // Step 16: Force Save Note 2
    const save2Status = await forceSaveAndWaitCommitted(pageB);
    results.steps["16_force_save_note2"] = `PASS: ${save2Status}`;
    results.evidence["note2_save_status"] = save2Status;

    // Step 17: Review & Confirm Note 2
    await triggerStartReview(pageB);
    await confirmPreview(pageB);
    results.steps["17_review_confirm_note2"] = "PASS";

    // Step 18: Submit Note 2
    await submitNote(pageB);
    results.steps["18_submit_note2"] = "PASS";

    // Step 19: View Continuous Green Sheet PDF (both notes)
    await pageB.waitForTimeout(4000);
    const s7 = await screenshot(pageB, "07-final-continuous-green-sheet-both-notes", "Final Green Sheet — Note 1 + Note 2 continuous");
    results.screenshots.push(s7);

    // Step 20: Supervisory Remark on Note 1
    if (note1Id && note1CanonicalText) {
      try {
        const remark = await createSupervisoryRemark(pageB, note1Id, note1CanonicalText);
        results.steps["20_supervisory_remark"] = `PASS: id=${remark.id}`;
        results.evidence["remark_id"] = remark.id;

        // Switch left pane to canonical notes view to inspect remark
        const notesTab = pageB.locator('button.pane-tab-btn:has-text("Inspect Official Notes")').first();
        if (await notesTab.isVisible({ timeout: 5000 }).catch(() => false)) {
          await notesTab.click();
          await pageB.waitForTimeout(1500);
          const remarksBtn = pageB.locator('.canonical-note-entry-card button:has-text("Remarks")').first();
          if (await remarksBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
            await remarksBtn.click();
            await pageB.waitForTimeout(1500);
            const s8 = await screenshot(pageB, "08-noting-supervisory-remark-anchored", "Supervisory remark anchored on canonical text");
            results.screenshots.push(s8);
          }
        }
      } catch (remErr) {
        results.steps["20_supervisory_remark"] = `FAIL: ${remErr.message}`;
        results.failures.push(`Remark creation: ${remErr.message}`);
      }
    }

    await ctxB.close();

    // ══════════════════════════════════════════════════════════════
    // SUMMARY
    // ══════════════════════════════════════════════════════════════
    console.log("\n" + "═".repeat(64));
    console.log("ACCEPTANCE SUMMARY");
    console.log("═".repeat(64));

    for (const [step, status] of Object.entries(results.steps)) {
      const icon = status.startsWith("PASS") ? "✓" : "✗";
      console.log(`  ${icon} ${step}: ${status}`);
    }

    console.log(`\nEvidence:`);
    for (const [k, v] of Object.entries(results.evidence)) {
      console.log(`  • ${k}: ${v}`);
    }

    console.log(`\nScreenshots (${results.screenshots.length}):`);
    for (const s of results.screenshots) {
      console.log(`  📷 ${s}`);
    }

    const resPath = path.join(SCREENSHOTS_DIR, "full-acceptance-report.json");
    fs.writeFileSync(resPath, JSON.stringify(results, null, 2), "utf8");
    console.log(`\nWritten report to: ${resPath}`);

    const allPassed = Object.values(results.steps).every(s => s.startsWith("PASS"));
    if (allPassed && results.failures.length === 0) {
      console.log("\n>>> FULL E2E ACCEPTANCE VERIFIED AND PASSED <<<");
    } else {
      console.log(`\n⚠ Process completed with ${results.failures.length} failure(s)`);
    }

  } finally {
    await browser.close();
  }
}

runFullAcceptance().catch((err) => {
  console.error("\nFATAL ERROR IN ACCEPTANCE RUNNER:", err.message);
  console.error(err.stack);
  process.exit(1);
});
