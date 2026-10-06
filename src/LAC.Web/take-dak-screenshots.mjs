import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const PORT = 5199;
const DIST_DIR = path.join(process.cwd(), "dist");

// Helper to serve built static assets from dist
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
      console.log(`Static server running on http://127.0.0.1:${PORT}`);
      resolve(server);
    });
  });
}

async function main() {
  const server = await startStaticServer();

  const browser = await chromium.launch({
    executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    headless: true,
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1,
  });

  const page = await context.newPage();

  // Route API requests with authoritative mock DTOs matching backend contract
  await page.route("**/api/auth/me", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        userId: "11111111-1111-1111-1111-111155555555",
        displayName: "Naib Tehsildar (Intake)",
        permissions: ["Dak.View", "Dak.Register", "Dak.Edit", "Dak.Move", "Dak.Dispose", "Dak.Cancel", "WorkItem.Create"]
      }),
    });
  });

  await page.route("**/api/dak/lookups/registration", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        categories: [
          { id: "cat-1", code: "LAND_REF", name: "Land Acquisition Reference", defaultPriority: "Routine", isActive: true },
          { id: "cat-2", code: "COURT_NOTICE", name: "High Court Summon / Notice", defaultPriority: "Urgent", isActive: true },
          { id: "cat-3", code: "REP_LANDOWNER", name: "Landowner Representation", defaultPriority: "Routine", isActive: true }
        ],
        workstreams: [
          { id: "ws-1", code: "LA", name: "Land Acquisition", isActive: true },
          { id: "ws-2", code: "LIT", name: "Court Litigation", isActive: true },
          { id: "ws-3", code: "PAY", name: "Disbursement & Award", isActive: true }
        ]
      }),
    });
  });

  await page.route("**/api/dak/lookups/directory", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        desks: [
          { id: "desk-1", code: "NT_LAC", name: "Naib Tehsildar Desk", isActive: true },
          { id: "desk-2", code: "LA_SECTION", name: "Land Acquisition Branch", isActive: true },
          { id: "desk-3", code: "LEGAL_CELL", name: "Legal & Court Cell", isActive: true }
        ]
      }),
    });
  });

  await page.route("**/api/dak?*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [
          {
            id: "dak-101",
            diaryNumber: "DAK/2026/00142",
            receivedDate: "2026-10-06",
            subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
            senderName: "Ramesh Chandra & Ors",
            senderDepartment: "Landowners Association",
            inwardMode: "Physical / By Hand",
            priority: "Urgent",
            dueDate: "2026-10-20",
            status: "Registered",
            categoryName: "Landowner Representation",
            workstreamName: "Land Acquisition",
            assignedDeskName: null, // UNMARKED
            assignedUserDisplayName: null,
            hasDocument: true,
            revision: 1,
            createdAt: "2026-10-06T10:15:00Z",
            recordStatus: "Active"
          },
          {
            id: "dak-102",
            diaryNumber: "DAK/2026/00141",
            receivedDate: "2026-10-06",
            subject: "Notice in W.P.(C) 4589/2026 High Court of Delhi vs LAC Delhi",
            senderName: "Registrar, High Court of Delhi",
            senderDepartment: "Judicial Branch",
            inwardMode: "Court Summon / Special Messenger",
            priority: "Immediate",
            dueDate: "2026-10-12",
            status: "InProcess",
            categoryName: "High Court Summon / Notice",
            workstreamName: "Court Litigation",
            assignedDeskName: "Naib Tehsildar Desk",
            assignedUserDisplayName: "Ashwani Baghel (NT)",
            hasDocument: true,
            revision: 3,
            createdAt: "2026-10-06T09:30:00Z",
            recordStatus: "Active"
          },
          {
            id: "dak-103",
            diaryNumber: "DAK/2026/00139",
            receivedDate: "2026-10-05",
            subject: "Requisition of Revenue Records for Award No. 12/2008-09 Village Alipur",
            senderName: "ADM (LA) Office",
            senderDepartment: "Revenue Dept",
            inwardMode: "Speed Post / Registered Post",
            priority: "Routine",
            dueDate: null,
            status: "Disposed",
            categoryName: "Land Acquisition Reference",
            workstreamName: "Land Acquisition",
            assignedDeskName: "Land Acquisition Branch",
            assignedUserDisplayName: "Sunil Kumar (Kanoongo)",
            hasDocument: false,
            revision: 5,
            createdAt: "2026-10-05T14:20:00Z",
            recordStatus: "Active"
          }
        ],
        totalCount: 3,
        page: 0,
        pageSize: 25
      }),
    });
  });

  const sampleDakDetail = {
    id: "dak-101",
    diaryNumber: "DAK/2026/00142",
    receivedDate: "2026-10-06",
    subject: "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur",
    senderName: "Ramesh Chandra & Ors",
    senderDesignation: "Authorized Representative",
    senderDepartment: "Landowners Association",
    senderAddress: "H.No. 42, Village Galibpur, New Delhi - 110073",
    senderReferenceNumber: "REP/GAL/2026/99",
    senderLetterDate: "2026-10-04",
    inwardMode: "Physical / By Hand",
    priority: "Urgent",
    dueDate: "2026-10-20",
    status: "Registered",
    categoryId: "cat-3",
    categoryName: "Landowner Representation",
    workstreamId: "ws-1",
    workstreamName: "Land Acquisition",
    revision: 2,
    mainDocumentId: "doc-999",
    mainDocumentFileName: "Scanned_Representation_DAK_00142.pdf",
    currentAssignment: null, // UNMARKED INTAKE QUEUE
    attachments: [
      { id: "att-1", documentId: "doc-888", originalFileName: "Annexure_A_Khasra_Extract.pdf", title: "Annexure A - Khasra Extract", attachmentType: "Annexure", sequenceOrder: 1, createdAt: "2026-10-06T10:16:00Z" }
    ],
    villageLinks: [
      { linkId: "vl-1", entityId: "v-123", canOpen: true, displayName: "Galibpur", entityType: "Village" }
    ],
    awardLinks: [
      { linkId: "al-1", entityId: "a-456", canOpen: true, displayName: "12/2008-09", entityType: "Award" }
    ],
    matterLinks: [
      { linkId: "ml-1", entityId: null, canOpen: false, displayName: "Restricted record", entityType: "Matter" }
    ],
    khasraLinks: [
      { linkId: "kl-1", entityId: "k-789", canOpen: true, displayName: "45//2", entityType: "Khasra" }
    ],
    createdAt: "2026-10-06T10:15:00Z",
    createdBy: "Intake Operator",
    updatedAt: "2026-10-06T10:15:00Z",
    updatedBy: "Intake Operator",
    recordStatus: "Active"
  };

  await page.route("**/api/dak/dak-101", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(sampleDakDetail),
    });
  });

  await page.route("**/api/dak/dak-101/physical-original", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        hasPhysicalOriginal: true,
        deskId: "desk-1",
        userId: "user-1",
        locationNote: "Almirah #2, Rack B, File #142",
        provenanceNote: "Physical stamped original verified and stored in central dispatch safe.",
        updatedAt: "2026-10-06T10:20:00Z",
        updatedByUserId: "user-1",
        revision: 2
      }),
    });
  });

  await page.route("**/api/dak/dak-101/movement-targets", (route) => {
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
          },
          {
            id: "desk-2",
            code: "LA_SECTION",
            name: "Land Acquisition Branch",
            members: [
              { userId: "user-2", displayName: "Sunil Kumar", designation: "Kanoongo", isPrimary: true }
            ]
          }
        ]
      }),
    });
  });

  await page.route("**/api/outward?dakId=*", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ items: [] }),
    });
  });

  const artifactDir = "C:\\Users\\ashwa\\.gemini\\antigravity\\brain\\46d891e0-f3bc-4271-97ab-04c71a748d70";
  const localDir = path.join(process.cwd(), "screenshots");
  if (!fs.existsSync(localDir)) fs.mkdirSync(localDir, { recursive: true });

  console.log("\n1. Capturing Quick Intake Screenshot at 1366 × 768...");
  await page.goto(`http://127.0.0.1:${PORT}/dak/register`, { waitUntil: "networkidle" });
  await page.waitForSelector(".quick-intake-form");
  // Fill sample data to show realistic quick intake screen
  await page.fill("input[placeholder='e.g. DAK/2026/00142']", "DAK/2026/00142");
  await page.fill("input[placeholder='Sender Name / Entity']", "Ramesh Chandra & Ors");
  await page.fill("input[placeholder='e.g. F.1(23)/2025/L&B']", "REP/GAL/2026/99");
  await page.fill("input[placeholder='Subject or title of incoming correspondence...']", "Representation regarding enhancement of compensation for Khasra 45/2 Galibpur");
  await page.waitForTimeout(300);

  const shot1Path = path.join(artifactDir, "quick_intake_1366x768.png");
  await page.screenshot({ path: shot1Path });
  fs.copyFileSync(shot1Path, path.join(localDir, "quick_intake_1366x768.png"));
  console.log(`Saved: ${shot1Path}`);

  console.log("\n2. Capturing Inward Dak Register Directory Screenshot at 1366 × 768...");
  await page.goto(`http://127.0.0.1:${PORT}/dak`, { waitUntil: "networkidle" });
  await page.waitForSelector(".compact-dak-table");
  await page.waitForTimeout(300);

  const shot2Path = path.join(artifactDir, "dak_directory_unmarked_1366x768.png");
  await page.screenshot({ path: shot2Path });
  fs.copyFileSync(shot2Path, path.join(localDir, "dak_directory_unmarked_1366x768.png"));
  console.log(`Saved: ${shot2Path}`);

  console.log("\n3. Capturing Dak Workspace / Physical Original Screenshot at 1366 × 768...");
  await page.goto(`http://127.0.0.1:${PORT}/dak/dak-101`, { waitUntil: "networkidle" });
  await page.waitForSelector(".dak-custody-card-dual");
  await page.waitForTimeout(300);

  const shot3Path = path.join(artifactDir, "dak_workspace_physical_original_1366x768.png");
  await page.screenshot({ path: shot3Path });
  fs.copyFileSync(shot3Path, path.join(localDir, "dak_workspace_physical_original_1366x768.png"));
  console.log(`Saved: ${shot3Path}`);

  await browser.close();
  server.close();
  console.log("\nALL SCREENSHOTS CAPTURED SUCCESSFULLY AT EXACTLY 1366 × 768!");
}

main().catch((err) => {
  console.error("Screenshot script failed:", err);
  process.exit(1);
});
