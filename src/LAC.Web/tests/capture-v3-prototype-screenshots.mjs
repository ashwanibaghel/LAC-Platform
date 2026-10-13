import { chromium } from "playwright";
import fs from "node:fs";
import path from "node:path";

const receiptsDir = path.resolve("../../docs/receipts/green-legal-onlyoffice-workspace/v3-prototype");
const brainDir = path.resolve("C:/Users/ashwa/.gemini/antigravity/brain/42051362-f762-4f2b-a1ad-6c542241bcde");

fs.mkdirSync(receiptsDir, { recursive: true });
fs.mkdirSync(brainDir, { recursive: true });

async function main() {
  console.log("=== GREEN LEGAL NOTING V3 — PLAYWRIGHT 1366x768 SCREENSHOT SUITE ===");
  console.log("Launching Chromium with exact viewport 1366x768, scale 1...");

  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe"
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1
  });

  const page = await context.newPage();

  // Mock standard auth so AppShell renders authenticated state smoothly
  await page.route("**/api/auth/me", async (route) => {
    return route.fulfill({
      json: {
        id: "00000000-0000-0000-0000-000000000001",
        username: "lac.officer",
        displayName: "Sh. Vikram Malhotra",
        designation: "Land Acquisition Collector / ADM",
        roles: ["Officer", "Admin"],
        permissions: [
          { code: "Matter.View", scope: "All" },
          { code: "Matter.Edit", scope: "All" },
          { code: "Draft.Create", scope: "All" },
          { code: "Draft.Edit", scope: "All" },
          { code: "Draft.View", scope: "All" },
          { code: "Dak.View", scope: "All" },
          { code: "Dak.Register", scope: "All" },
          { code: "Matter.Document.Manage", scope: "All" }
        ],
        workstreams: [{ id: "ws-1", name: "South West Acquisition" }],
        desks: [{ id: "desk-lac", name: "LAC / ADM (SW) Desk", code: "LAC-SW" }]
      }
    });
  });

  const baseUrl = "http://localhost:5921/matters/44444444-4444-4444-4444-444444444444/noting-v3";

  const saveBoth = async (filename) => {
    const p1 = path.join(receiptsDir, filename);
    const p2 = path.join(brainDir, filename);
    await page.screenshot({ path: p1, fullPage: false });
    fs.copyFileSync(p1, p2);
    console.log(`✓ Captured ${filename} (1366x768 uncropped)`);
  };

  // -------------------------------------------------------------
  // 1. SCREEN A: Officer A (Kanungo) typing Note 1 on single sheet
  // -------------------------------------------------------------
  console.log("Navigating to Screen A (Officer A - Kanungo draft)...");
  await page.goto(`${baseUrl}?mode=screenA&zoom=100`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-legal-sheet");
  await page.waitForTimeout(1000);
  await saveBoth("v3-01-screenA-officerA-draft.png");

  // -------------------------------------------------------------
  // 2. SCREEN B: Officer B (Tehsildar) continuing Note 2 on same sheet
  // -------------------------------------------------------------
  console.log("Navigating to Screen B (Officer B - Tehsildar continuation)...");
  await page.goto(`${baseUrl}?mode=screenB&zoom=100`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-legal-sheet");
  await page.waitForTimeout(1000);
  await saveBoth("v3-02-screenB-officerB-continuation.png");

  // -------------------------------------------------------------
  // 3. SCREEN C: Two submitted official notes on Page 1
  // -------------------------------------------------------------
  console.log("Navigating to Screen C (Two submitted official notes)...");
  await page.goto(`${baseUrl}?mode=screenC&zoom=100`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-legal-sheet");
  await page.waitForTimeout(1000);
  await saveBoth("v3-03-screenC-two-notes-submitted.png");

  // -------------------------------------------------------------
  // 4. CANONICAL REVIEW MODAL (SHA-256 hash & exact text)
  // -------------------------------------------------------------
  console.log("Navigating to Canonical Review Modal...");
  await page.goto(`${baseUrl}?mode=screenB&zoom=100&review=open`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-modal-card");
  await page.waitForTimeout(1000);
  await saveBoth("v3-04-canonical-review-modal.png");

  // -------------------------------------------------------------
  // 5. SAVE COMMITTED STATE (Toolbar badge with generation/hash)
  // -------------------------------------------------------------
  console.log("Navigating to Save Committed State...");
  await page.goto(`${baseUrl}?mode=screenB&zoom=100`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-save-badge.committed");
  await page.waitForTimeout(800);
  await saveBoth("v3-05-save-committed-state.png");

  // -------------------------------------------------------------
  // 6. ZOOM DEMONSTRATION (85% wide overview on 1366x768)
  // -------------------------------------------------------------
  console.log("Navigating to Zoom 85%...");
  await page.goto(`${baseUrl}?mode=screenB&zoom=85`, { waitUntil: "networkidle" });
  await page.waitForSelector(".v3-legal-sheet");
  await page.waitForTimeout(800);
  await saveBoth("v3-06-zoom-85-percent-overview.png");

  await browser.close();
  console.log("=== ALL V3 SCREENSHOTS CAPTURED SUCCESSFULLY ===");
}

main().catch((err) => {
  console.error("FATAL ERROR capturing V3 screenshots:", err);
  process.exit(1);
});
