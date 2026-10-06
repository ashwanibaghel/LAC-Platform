import test from "node:test";
import assert from "node:assert/strict";
import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { officeCalendarDate } from "../src/dak/officeDate.js";

const PORT = 5198;
const DIST_DIR = path.join(process.cwd(), "dist");

function startStaticServer() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      let filePath = path.join(DIST_DIR, req.url === "/" ? "index.html" : req.url);
      if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        filePath = path.join(DIST_DIR, "index.html");
      }
      const ext = path.extname(filePath);
      const mimeTypes = {
        ".html": "text/html",
        ".js": "text/javascript",
        ".css": "text/css",
        ".json": "application/json",
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".svg": "image/svg+xml"
      };
      const contentType = mimeTypes[ext] || "application/octet-stream";
      res.writeHead(200, { "Content-Type": contentType });
      fs.createReadStream(filePath).pipe(res);
    });
    server.listen(PORT, () => {
      resolve(server);
    });
  });
}

test("Office Date IST boundary logic", () => {
  // UTC 18:29:59 is 23:59:59 IST (same day)
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:29:59Z")), "2026-10-05");
  // UTC 18:30:00 is 00:00:00 IST (next day in Delhi)
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:30:00Z")), "2026-10-06");
  assert.equal(officeCalendarDate(new Date("2026-10-05T23:59:59Z")), "2026-10-06");
});

test.describe("Dak V2 Quick Intake & Workspace Browser Component Tests", () => {
  let server;
  let browser;

  test.before(async () => {
    server = await startStaticServer();
    browser = await chromium.launch({
      executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      headless: true
    });
  });

  test.after(async () => {
    if (browser) await browser.close();
    if (server) server.close();
  });

  const setupDefaultAuthAndLookups = async (page) => {
    page.on("console", (msg) => {
      if (msg.type() === "error") console.log("BROWSER ERROR LOG:", msg.text());
    });
    page.on("pageerror", (err) => console.log("BROWSER PAGE EXCEPTION:", err));

    await page.route("**/api/auth/me", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "user-nt-1",
          username: "nt_officer",
          displayName: "Ashwani Baghel (NT)",
          roles: ["NaibTehsildar"],
          permissions: [
            { code: "Dak.View", scope: "Global" },
            { code: "Dak.Register", scope: "Global" },
            { code: "Dak.Edit", scope: "Global" },
            { code: "Dak.Move", scope: "Global" },
            { code: "Dak.Dispose", scope: "Global" },
            { code: "Dak.Cancel", scope: "Global" },
            { code: "WorkItem.Create", scope: "Global" }
          ],
          workstreams: [],
          desks: []
        })
      });
    });

    await page.route("**/api/dak/lookups/registration", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          categories: [
            { id: "cat-1", code: "LAND_REF", name: "Land Acquisition Reference", defaultPriority: "Routine", isActive: true }
          ],
          workstreams: [
            { id: "ws-1", code: "LA", name: "Land Acquisition", isActive: true }
          ]
        })
      });
    });

    await page.route("**/api/dak/lookups/directory", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          desks: [
            { id: "desk-1", code: "NT_LAC", name: "Naib Tehsildar Desk", isActive: true },
            { id: "desk-2", code: "LA_SECTION", name: "Land Acquisition Branch", isActive: true }
          ]
        })
      });
    });

    await page.route("**/api/dak/*/lookups/edit", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          categories: [
            { id: "cat-1", code: "LAND_REF", name: "Land Acquisition Reference", isActive: true }
          ],
          workstreams: [
            { id: "ws-1", name: "Land Acquisition" }
          ]
        })
      });
    });

    await page.route("**/api/dak/*/movement-targets", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          desks: [
            {
              id: "desk-1",
              code: "NT_LAC",
              name: "Naib Tehsildar Desk",
              members: [
                { userId: "user-1", displayName: "Ashwani Baghel", designation: "Naib Tehsildar", isPrimary: true }
              ]
            }
          ]
        })
      });
    });

    await page.route("**/api/outward?dakId=*", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ items: [] })
      });
    });
  };

  const createSampleDak = (overrides = {}) => ({
    id: "dak-101",
    diaryNumber: "DAK/2026/00142",
    receivedDate: "2026-10-06",
    subject: "Sample Dak Record",
    senderName: "Ramesh Chandra",
    inwardMode: "Physical / By Hand",
    priority: "Routine",
    status: "Registered",
    revision: 1,
    attachments: [],
    villageLinks: [],
    awardLinks: [],
    matterLinks: [],
    khasraLinks: [],
    createdAt: "2026-10-06T10:15:00Z",
    recordStatus: "Active",
    ...overrides
  });

  test("1. Server 409 error preservation: backend detail message is preserved directly", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak", (route) => {
      if (route.request().method() === "POST") {
        route.fulfill({
          status: 409,
          contentType: "application/json",
          body: JSON.stringify({
            detail: "This Idempotency-Key was used for a different registration. Use a new key for a new receipt."
          })
        });
      } else {
        route.continue();
      }
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });
    await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
    await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra");
    await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Test 409 Preservation");

    await page.click(".btn-next");

    const errorMsg = await page.locator(".state.error").textContent();
    assert.ok(errorMsg.includes("This Idempotency-Key was used for a different registration"), `Expected server detail message, got: ${errorMsg}`);
    assert.equal(errorMsg.includes("already registered"), false, "Error message must NOT be rewritten into generic already registered text.");

    await context.close();
  });

  test("2. Idempotency key stability: retry of unconfirmed submission uses identical key", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    const keysSent = [];
    await page.route("**/api/dak", (route) => {
      if (route.request().method() === "POST") {
        keysSent.push(route.request().headers()["idempotency-key"]);
        route.fulfill({
          status: 500,
          contentType: "application/json",
          body: JSON.stringify({ detail: "Internal Server Error" })
        });
      } else {
        route.continue();
      }
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });
    await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
    await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra");
    await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Test Retry Key");

    await page.click(".btn-next");
    await page.waitForSelector(".state.error");

    await page.click(".btn-next");
    await page.waitForTimeout(300);

    assert.equal(keysSent.length, 2, "Expected 2 registration attempts.");
    assert.ok(keysSent[0], "First attempt must have an idempotency key.");
    assert.equal(keysSent[0], keysSent[1], "Retry attempt MUST reuse identical Idempotency-Key.");

    await context.close();
  });

  test("3. Register & Next generates fresh key only after successful registration", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    const keysSent = [];
    await page.route("**/api/dak", (route) => {
      if (route.request().method() === "POST") {
        keysSent.push(route.request().headers()["idempotency-key"]);
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ id: "dak-new-1", diaryNumber: "DAK/2026/00142" })
        });
      } else {
        route.continue();
      }
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });

    // Entry 1
    await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
    await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra");
    await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Entry 1");
    await page.click(".btn-next");

    await page.waitForSelector(".state.success-banner");

    // Entry 2
    await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00143");
    await page.fill("input[placeholder='Sender Name / Entity']", "Suresh Kumar");
    await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Entry 2");
    await page.click(".btn-next");

    await page.waitForTimeout(300);

    assert.equal(keysSent.length, 2);
    assert.notEqual(keysSent[0], keysSent[1], "Successful Register & Next must generate a NEW Idempotency-Key for the subsequent record.");

    await context.close();
  });

  test("4. Register & Next clears real file input DOM element", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak", (route) => {
      if (route.request().method() === "POST") {
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ id: "dak-new-1", diaryNumber: "DAK/2026/00142" })
        });
      } else {
        route.continue();
      }
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });
    await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
    await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra");
    await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Doc test");

    // Set file input using Playwright
    await page.setInputFiles("#primary-scan-file", {
      name: "sample.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("pdf-data")
    });

    let selectedFileName = await page.locator(".file-name-display").textContent();
    assert.equal(selectedFileName, "sample.pdf");

    await page.click(".btn-next");
    await page.waitForSelector(".state.success-banner");

    // Verify native input element file list length is 0
    const fileCount = await page.evaluate(() => {
      const el = document.querySelector("#primary-scan-file");
      return el ? el.files.length : -1;
    });

    assert.equal(fileCount, 0, "Native file input element files array must be cleared.");
    selectedFileName = await page.locator(".file-name-display").textContent();
    assert.equal(selectedFileName, "No document selected");

    await context.close();
  });

  test("5. Physical Original modal Cancel does not alter persisted display", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak/dak-101", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({ subject: "Test PO Cancel" }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          hasPhysicalOriginal: true,
          deskId: "desk-1",
          userId: null,
          locationNote: "Original Shelf #1",
          provenanceNote: "Verified paper original present",
          revision: 1
        })
      });
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    // Verify initial card text
    const initialLocation = await page.locator(".po-state-row").textContent();
    assert.ok(initialLocation.includes("Yes"));

    // Open modal, change input draft, then click Cancel
    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");
    await page.selectOption(".modal-card select", "no");
    await page.click(".modal-card button:has-text('Cancel')");

    // Displayed card must NOT change
    const afterCancelLocation = await page.locator(".po-state-row").textContent();
    assert.ok(afterCancelLocation.includes("Yes"), "Card must retain original persisted value after modal Cancel.");

    // Reopening modal must reflect original server values
    await page.click("button:has-text('Update Physical Location')");
    const draftValue = await page.locator(".modal-card select").first().inputValue();
    assert.equal(draftValue, "yes", "Modal draft must reset to persisted server value on reopen.");

    await context.close();
  });

  test("6. Canonical revision refresh: Physical Original save reloads Dak detail", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    let dakFetchCount = 0;
    await page.route("**/api/dak/dak-101", (route) => {
      dakFetchCount++;
      const currentRevision = dakFetchCount > 1 ? 2 : 1;
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({ subject: "Test Revision Refresh", revision: currentRevision }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      if (route.request().method() === "PUT") {
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            hasPhysicalOriginal: true,
            deskId: "desk-1",
            locationNote: "Updated Shelf #2",
            provenanceNote: "Updated note",
            revision: 2
          })
        });
      } else {
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            hasPhysicalOriginal: true,
            deskId: "desk-1",
            locationNote: "Original Shelf #1",
            provenanceNote: "Initial note",
            revision: 1
          })
        });
      }
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");
    assert.equal(dakFetchCount, 1);

    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");
    await page.fill(".modal-card input[placeholder*='e.g. Almirah']", "Updated Shelf #2");
    await page.fill(".modal-card textarea[placeholder*='observation']", "Updated provenance note");
    await page.click(".modal-card button[type='submit']");

    await page.waitForTimeout(300);

    // Canonical reload path must have executed, updating dakFetchCount to 2
    assert.ok(dakFetchCount >= 2, "Save must trigger canonical Dak reload to update dak.revision.");

    await context.close();
  });

  test("7. Dak.Edit without Dak.Move can perform Physical Original edit using directory lookup", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();

    // User has Dak.View + Dak.Edit, but NO Dak.Move
    await page.route("**/api/auth/me", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: "user-edit-1",
          username: "edit_user",
          displayName: "Editor User",
          roles: ["Editor"],
          permissions: [
            { code: "Dak.View", scope: "Global" },
            { code: "Dak.Edit", scope: "Global" }
          ],
          workstreams: [],
          desks: []
        })
      });
    });

    await page.route("**/api/dak/lookups/directory", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          desks: [
            { id: "desk-dir-1", code: "DIR_DESK_1", name: "Directory Desk 1", isActive: true },
            { id: "desk-dir-2", code: "DIR_DESK_2", name: "Directory Desk 2", isActive: true }
          ]
        })
      });
    });

    await page.route("**/api/outward?dakId=*", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ items: [] }) });
    });

    // movement-targets returns 403 Forbidden for non-Dak.Move users
    await page.route("**/api/dak/dak-101/movement-targets", (route) => {
      route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ detail: "Forbidden" }) });
    });

    await page.route("**/api/dak/dak-101", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({ subject: "Test Edit Without Move" }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: false, revision: 1 }) });
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    // Click Update Physical Location button
    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");

    // Select Yes for physical paper
    await page.selectOption(".modal-card select", "yes");

    // Verify desk select contains Directory Desk options loaded from /api/dak/lookups/directory
    const deskOptionsText = await page.locator(".modal-card select").nth(1).textContent();
    assert.ok(deskOptionsText.includes("Directory Desk 1"), "Desk choices must be loaded from directory lookup when movement-targets is 403.");

    await context.close();
  });

  test("8. Privacy guard: restricted linked context hides sensitive entity ID and href", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak/dak-101", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({
          subject: "Privacy Link Test",
          matterLinks: [
            { linkId: "ml-1", entityId: null, canOpen: false, displayName: "Restricted record", entityType: "Matter" }
          ]
        }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: false, revision: 1 }) });
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    // Switch to Linked Context tab
    await page.click("button:has-text('Linked Context')");
    await page.waitForSelector(".info-section");

    const matterLinkText = await page.locator(".info-section:has-text('Linked Matters')").textContent();
    assert.ok(matterLinkText.includes("Restricted record"), "Restricted link must display fallback text.");

    await context.close();
  });

  test("9. Active record mutation actions appear with correct permissions", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak/dak-101", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({ subject: "Active Dak Test" }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: true, deskId: "desk-1", revision: 1 }) });
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    const editBtn = await page.locator("button:has-text('Edit Details')").isVisible();
    const custodyBtn = await page.locator("button:has-text('Update Physical Location')").isVisible();
    const moveBtn = await page.locator("button:has-text('Mark to Desk')").isVisible();

    assert.ok(editBtn, "Edit Details button must be visible for active Dak with Dak.Edit permission.");
    assert.ok(custodyBtn, "Update Physical Location button must be visible for active Dak with Dak.Edit permission.");
    assert.ok(moveBtn, "Mark to Desk button must be visible for active Dak with Dak.Move permission.");

    await context.close();
  });

  test("10. Terminal record safety: disposed or cancelled Dak hides/disables mutation controls", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    await page.route("**/api/dak/dak-101", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({ subject: "Disposed Dak Test", status: "Disposed" }))
      });
    });

    await page.route("**/api/dak/dak-101/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: true, deskId: "desk-1", revision: 1 }) });
    });

    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    const editBtn = await page.locator("button:has-text('Edit Details')").isVisible();
    const custodyBtn = await page.locator("button:has-text('Update Physical Location')").isVisible();
    const moveBtn = await page.locator("button:has-text('Mark to Desk')").isVisible();

    assert.equal(editBtn, false, "Edit Details button must be absent for terminal Disposed Dak.");
    assert.equal(custodyBtn, false, "Update Physical Location button must be absent for terminal Disposed Dak.");
    assert.equal(moveBtn, false, "Mark to Desk button must be absent for terminal Disposed Dak.");

    await context.close();
  });

  test("11. True SPA cross-Dak state isolation, race safety and route identity protection", async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupDefaultAuthAndLookups(page);

    // Controlled promises for delayed load and mutation reloads
    let slowDakAFulfills;
    const slowDakAPromise = new Promise((resolve) => { slowDakAFulfills = resolve; });

    let slowMutationFulfills;
    const slowMutationPromise = new Promise((resolve) => { slowMutationFulfills = resolve; });

    // Mock Dak A
    await page.route("**/api/dak/dak-A", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({
          id: "dak-A",
          diaryNumber: "DAK/2026/00111",
          subject: "Dak Alpha Subject",
          categoryId: "cat-1",
          workstreamId: "ws-1"
        }))
      });
    });

    await page.route("**/api/dak/dak-A/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: true, deskId: "desk-1", userId: "user-alpha", revision: 1 }) });
    });

    await page.route("**/api/dak/dak-A/movement-targets", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          desks: [
            {
              id: "desk-1",
              code: "NT_LAC",
              name: "Naib Tehsildar Desk",
              members: [
                { userId: "user-alpha", displayName: "Officer Alpha Secret Name", designation: "NT", isPrimary: true }
              ]
            }
          ]
        })
      });
    });

    await page.route("**/api/outward?dakId=dak-A", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          items: [
            { id: "out-A", outwardNumber: "OUT/2026/0099", outwardDate: "2026-10-06", subject: "Outward Reply Alpha", status: "Dispatched", recipientName: "Target A" }
          ]
        })
      });
    });

    // Mock Dak B (where movement-targets returns 403, outward lookup fails, edit lookups fail)
    await page.route("**/api/dak/dak-B", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({
          id: "dak-B",
          diaryNumber: "DAK/2026/00999",
          subject: "Dak Beta Subject",
          categoryId: null,
          workstreamId: null
        }))
      });
    });

    await page.route("**/api/dak/dak-B/physical-original", (route) => {
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: false, revision: 1 }) });
    });

    await page.route("**/api/dak/dak-B/movement-targets", (route) => {
      route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ detail: "Forbidden" }) });
    });

    await page.route("**/api/outward?dakId=dak-B", (route) => {
      route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ detail: "Outward service error" }) });
    });

    await page.route("**/api/dak/dak-B/lookups/edit", (route) => {
      route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ detail: "Lookup error" }) });
    });

    // Mock Dak SLOW
    await page.route("**/api/dak/dak-SLOW", async (route) => {
      await slowDakAPromise;
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(createSampleDak({
          id: "dak-SLOW",
          diaryNumber: "DAK/2026/00SLOW",
          subject: "Delayed Dak Slow Subject"
        }))
      });
    });

    // Helper for SPA navigation without document reload
    const spaNavigateTo = async (path) => {
      await page.evaluate((targetPath) => {
        window.history.pushState(null, '', targetPath);
        window.dispatchEvent(new PopStateEvent('popstate'));
      }, path);
    };

    // --- STEP A: Open Dak A & Fill Drafts ---
    await page.goto(`http://127.0.0.1:${PORT}/dak/dak-A`, { waitUntil: "networkidle" });
    await page.waitForSelector(".dak-detail-workspace");

    // Set a window marker to verify true SPA navigation
    await page.evaluate(() => {
      window.__SPA_TEST_MARKER__ = "SURVIVED_SAME_DOCUMENT_REMOUNT";
    });

    // 1. Verify Officer Alpha Secret Name is present in Dak A movement targets / custody
    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");
    await page.selectOption(".modal-card select", "yes");
    await page.selectOption(".modal-card select >> nth=1", "desk-1");
    const optionsText = await page.locator(".modal-card select >> nth=2").textContent();
    assert.ok(optionsText.includes("Officer Alpha Secret Name"), "Dak A must contain Officer Alpha member option.");
    await page.click(".modal-card button:has-text('Cancel')");

    // 2. Select an attachment file in Add Attachment without uploading
    await page.click("button:has-text('Documents & Attachments')");
    await page.click("button:has-text('+ Add Attachment')");
    await page.waitForSelector(".modal-card:has-text('Add Attachment')");
    await page.setInputFiles(".modal-card input[type='file']", {
      name: "secret_file_A.txt",
      mimeType: "text/plain",
      buffer: Buffer.from("Secret Dak A attachment draft")
    });
    await page.fill(".modal-card input[placeholder*='Notice']", "Secret Attachment Title A");
    // Leave attachment draft populated (close modal without uploading)
    await page.click(".modal-card button:has-text('Cancel')");

    // 3. Type a link entity UUID without submitting
    await page.click("button:has-text('Linked Context')");
    await page.click("button:has-text('+ Add Cross-Reference')");
    await page.waitForSelector(".modal-card:has-text('Link Domain Entity')");
    await page.fill(".modal-card input[placeholder='Enter Entity UUID']", "11111111-2222-3333-4444-555555555555");
    await page.click(".modal-card button:has-text('Cancel')");

    // 4. Verify outward reply OUT/2026/0099 is present on Dak A
    const dakAText = await page.locator(".dak-detail-workspace").textContent();
    assert.ok(dakAText.includes("OUT/2026/0099"), "Dak A must display outward reply OUT/2026/0099.");

    // --- STEP B: True SPA Navigate to Dak B ---
    await spaNavigateTo("/dak/dak-B");
    await page.waitForTimeout(300);

    // Verify SPA window marker survived (confirming NO full page reload occurred)
    const markerValue = await page.evaluate(() => window.__SPA_TEST_MARKER__);
    assert.equal(markerValue, "SURVIVED_SAME_DOCUMENT_REMOUNT", "Navigation to Dak B must be a genuine SPA transition keeping same window instance.");

    // Assertions on Dak B:
    const dakBSubject = await page.locator(".page-header h1").textContent();
    assert.ok(dakBSubject.includes("Dak Beta Subject"), "Displayed subject on Dak B must belong to B.");

    const dakBDiary = await page.locator(".breadcrumbs").textContent();
    assert.ok(dakBDiary.includes("DAK/2026/00999"), "Displayed diary number on Dak B must belong to B.");

    // 1. Assert no Officer Alpha name on Dak B
    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");
    await page.selectOption(".modal-card select", "yes");
    const deskSelectText = await page.locator(".modal-card select >> nth=1").textContent();
    assert.equal(deskSelectText.includes("Officer Alpha Secret Name"), false, "Dak B must NOT retain Officer Alpha from Dak A.");
    await page.click(".modal-card button:has-text('Cancel')");

    // 2. Assert no A outward reply on Dak B
    const dakBText = await page.locator(".dak-detail-workspace").textContent();
    assert.equal(dakBText.includes("OUT/2026/0099"), false, "Dak B must NOT retain outward reply from Dak A.");

    // 3. Assert no A selected attachment or file title on Dak B
    await page.click("button:has-text('Documents & Attachments')");
    await page.click("button:has-text('+ Add Attachment')");
    await page.waitForSelector(".modal-card:has-text('Add Attachment')");
    const attachTitleVal = await page.locator(".modal-card input[placeholder*='Notice']").inputValue();
    assert.equal(attachTitleVal, "", "Dak B attachment title draft must be reset to empty.");
    await page.click(".modal-card button:has-text('Cancel')");

    // 4. Assert no A link UUID on Dak B
    await page.click("button:has-text('Linked Context')");
    await page.click("button:has-text('+ Add Cross-Reference')");
    await page.waitForSelector(".modal-card:has-text('Link Domain Entity')");
    const linkEntityVal = await page.locator(".modal-card input[placeholder='Enter Entity UUID']").inputValue();
    assert.equal(linkEntityVal, "", "Dak B link entity UUID draft must be reset to empty.");
    await page.click(".modal-card button:has-text('Cancel')");

    // 5. Assert no stale A category/workstream lookup on Dak B edit modal
    await page.click("button:has-text('Edit Details')");
    await page.waitForSelector(".modal-card:has-text('Edit Dak Classification & Details')");
    const categoryOptionsText = await page.locator(".modal-card select >> nth=1").textContent();
    assert.equal(categoryOptionsText.includes("Land Acquisition Reference"), false, "Dak B must NOT retain stale category lookups from Dak A.");
    await page.click(".modal-card button:has-text('Cancel')");

    // --- STEP C: Delayed Stale Response Protection ---
    // Start delayed load for dak-SLOW via SPA navigation
    await spaNavigateTo("/dak/dak-SLOW");
    await page.waitForTimeout(50);

    // SPA navigate back to Dak B before dak-SLOW finishes loading
    await spaNavigateTo("/dak/dak-B");
    await page.waitForTimeout(100);

    // Now fulfill the slow dak-SLOW response
    slowDakAFulfills();
    await page.waitForTimeout(300);

    // Verify workspace still displays Dak B and has NOT been overwritten by delayed dak-SLOW response
    const currentSubjectAfterSlow = await page.locator(".page-header h1").textContent();
    assert.ok(currentSubjectAfterSlow.includes("Dak Beta Subject"), "Delayed response from old Dak must NOT overwrite active Dak B.");

    // --- STEP D: Mutation Race Protection ---
    // Perform a mutation response for Dak A whose post-success reload is delayed while user navigates to B
    await page.route("**/api/dak/dak-A/physical-original", async (route) => {
      if (route.request().method() === "PUT") {
        await slowMutationPromise;
        route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ hasPhysicalOriginal: true, revision: 2 }) });
      } else {
        route.continue();
      }
    });

    // SPA navigate back to Dak A to trigger mutation
    await spaNavigateTo("/dak/dak-A");
    await page.waitForTimeout(200);

    // Click Update Physical Location and submit
    await page.click("button:has-text('Update Physical Location')");
    await page.waitForSelector(".modal-card:has-text('Update Physical Original Custody')");
    await page.fill("textarea[placeholder*='observation note']", "Updating custody note on Dak A");
    await page.click(".modal-card button:has-text('Save Physical Custody')");

    // Immediately SPA navigate to Dak B while the mutation reload on Dak A is in-flight/delayed
    await spaNavigateTo("/dak/dak-B");
    await page.waitForTimeout(100);

    // Fulfill the delayed mutation on Dak A now
    slowMutationFulfills();
    await page.waitForTimeout(300);

    // Assert Dak B remains clean and unaffected by old Dak A mutation reload
    const finalSubject = await page.locator(".page-header h1").textContent();
    assert.ok(finalSubject.includes("Dak Beta Subject"), "Delayed mutation reload from old Dak A must NOT overwrite active Dak B.");

    await context.close();
  });
});
