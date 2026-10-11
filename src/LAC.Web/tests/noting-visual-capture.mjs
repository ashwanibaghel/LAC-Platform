import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const distDir = path.resolve(__dirname, "../dist");
const outDir = path.resolve(__dirname, "../../../docs/receipts/matter-noting-workspace");
fs.mkdirSync(outDir, { recursive: true });

const MIME_TYPES = {
  ".html": "text/html",
  ".js": "application/javascript",
  ".css": "text/css",
  ".svg": "image/svg+xml",
  ".json": "application/json",
  ".png": "image/png"
};

// 1. In-process static HTTP server for built dist assets
function createServer(port) {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      let reqPath = new URL(req.url, `http://127.0.0.1:${port}`).pathname;
      let filePath = path.join(distDir, reqPath === "/" ? "index.html" : reqPath);

      if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        filePath = path.join(distDir, "index.html");
      }

      const ext = path.extname(filePath);
      const contentType = MIME_TYPES[ext] || "application/octet-stream";

      try {
        const content = fs.readFileSync(filePath);
        res.writeHead(200, { "Content-Type": contentType });
        res.end(content);
      } catch (err) {
        res.writeHead(404);
        res.end("Not found");
      }
    });

    server.listen(port, "127.0.0.1", () => {
      resolve(server);
    });
  });
}

async function run() {
  const PORT = 5292;
  const server = await createServer(PORT);
  console.log(`Test static server running on http://127.0.0.1:${PORT}`);

  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe"
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1
  });

  const page = await context.newPage();
  page.on("pageerror", (err) => console.log("PAGE ERROR:", err.message));
  page.on("console", (msg) => {
    if (msg.type() === "error") console.log("CONSOLE ERROR:", msg.text());
  });

  const matterId = "00000000-0000-0000-0000-000000000042";
  const dakId = "00000000-0000-0000-0000-000000000101";
  const villageId = "00000000-0000-0000-0000-000000000005";
  const docId1 = "00000000-0000-0000-0000-000000000201";
  const docId2 = "00000000-0000-0000-0000-000000000202";

  let note2Submitted = false;

  // Intercept all API endpoints
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const pathName = url.pathname;
    const method = route.request().method();

    if (pathName === "/api/auth/me") {
      return route.fulfill({
        json: {
          id: "00000000-0000-0000-0000-000000000002",
          username: "tehsildar.south",
          displayName: "Sh. A.K. Verma",
          customDesignation: "Tehsildar (LA)",
          roles: ["Tehsildar", "Officer"],
          permissions: [
            { code: "Matter.View", scope: "All" },
            { code: "Matter.Edit", scope: "All" },
            { code: "Draft.Create", scope: "All" },
            { code: "Draft.Edit", scope: "All" },
            { code: "Dak.View", scope: "All" },
            { code: "Dak.Edit", scope: "All" },
            { code: "Dak.Move", scope: "All" },
            { code: "Matter.Document.Manage", scope: "All" },
            { code: "Village.View", scope: "All" }
          ]
        }
      });
    }

    if (pathName === `/api/dak/${dakId}`) {
      return route.fulfill({
        json: {
          id: dakId,
          diaryNumber: "DAK-1042",
          receivedDate: "2026-10-10",
          subject: "Compensation enhancement claim u/s 28A",
          senderName: "Sh. Prem Singh",
          inwardMode: "Physical",
          priority: "Urgent",
          recordStatus: "Active",
          status: "WithHolder",
          revision: 1,
          routingState: "WithHolder",
          physicalState: "Held",
          processingCycle: 1,
          villageClassification: "VillageSpecific",
          currentAssignment: {
            isActive: true,
            isConfirmed: true,
            assignedUserId: "00000000-0000-0000-0000-000000000002"
          },
          villageLinks: [
            { linkId: "vl-1", entityId: villageId, displayName: "Badarpur Khurd", entityType: "Village", canOpen: true }
          ],
          matterLinks: [],
          awardLinks: [],
          khasraLinks: [],
          attachments: []
        }
      });
    }

    if (pathName.includes("/matter-options")) {
      return route.fulfill({
        json: {
          villageId,
          matters: [
            {
              id: matterId,
              referenceNumber: "MAT-2026-089",
              title: "Compensation Claim u/s 28A for Khasra No. 42/1",
              status: "Open",
              workstreamName: "Dak Correspondence",
              createdDate: "2026-10-10"
            }
          ]
        }
      });
    }

    if (pathName === "/api/workstreams") {
      return route.fulfill({
        json: [
          { id: "ws-1", name: "Dak Correspondence", code: "DakCorrespondence" },
          { id: "ws-2", name: "Section 28A Petitions", code: "Sec28A" }
        ]
      });
    }

    if (pathName.includes(`/api/matters/${matterId}/context`)) {
      return route.fulfill({
        json: {
          matter: {
            id: matterId,
            villageId,
            villageName: "Badarpur Khurd",
            title: "Compensation Claim u/s 28A for Khasra No. 42/1",
            matterType: "Dak Correspondence",
            status: "Open",
            referenceNumber: "MAT-2026-089"
          },
          village: { id: villageId, name: "Badarpur Khurd", code: "BPK-04" },
          awards: [{ id: "aw-1", awardNumber: "12/2024", villageName: "Badarpur Khurd" }],
          khasras: [{ id: "kh-1", khasraNumber: "42/1", villageName: "Badarpur Khurd" }],
          courtCases: [],
          daks: [
            {
              dakId,
              diaryNumber: "DAK-1042",
              subject: "Compensation enhancement claim u/s 28A",
              status: "WithHolder",
              receivedDate: "2026-10-10"
            }
          ],
          workItems: [],
          outwardCount: 0,
          documentCount: 3,
          draftCount: 1
        }
      });
    }

    if (pathName === `/api/matters/${matterId}/noting`) {
      const baseNotes = [
        {
          id: "00000000-0000-0000-0000-000000000301",
          matterId,
          number: 1,
          version: 1,
          contentJson: JSON.stringify({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "Examined field survey records for Khasra No. 42/1. Verified title against Award No. 12/2024 and revenue records." }] }] }),
          canonicalText: "Examined field survey records for Khasra No. 42/1. Verified title against Award No. 12/2024 and revenue records.\n",
          textHash: "ABC12345",
          authorUserId: "00000000-0000-0000-0000-000000000001",
          authorName: "Sh. R.K. Sharma",
          designation: "Kanungo",
          deskId: "desk-1",
          deskName: "Revenue Verification Desk",
          submittedAt: "2026-10-10T11:30:00Z",
          sourceDakId: dakId,
          citations: [
            {
              anchor: { version: 1, start: 57, end: 77, quote: "Award No. 12/2024", quoteHash: "HASH1" },
              documentId: docId1,
              documentVersion: 1,
              documentHash: "HASH_AWARD_PDF",
              page: 4,
              region: null
            }
          ],
          documentManifestJson: "[]"
        }
      ];

      if (note2Submitted) {
        baseNotes.push({
          id: "00000000-0000-0000-0000-000000000302",
          matterId,
          number: 2,
          version: 1,
          contentJson: JSON.stringify({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "Title verification report corroborated. Notice recommended for issue to interested parties under Section 28A." }] }] }),
          canonicalText: "Title verification report corroborated. Notice recommended for issue to interested parties under Section 28A.\n",
          textHash: "DEF67890",
          authorUserId: "00000000-0000-0000-0000-000000000002",
          authorName: "Sh. A.K. Verma",
          designation: "Tehsildar (LA)",
          deskId: "desk-2",
          deskName: "Tehsildar Desk",
          submittedAt: "2026-10-11T10:15:00Z",
          sourceDakId: dakId,
          citations: [],
          documentManifestJson: "[]"
        });
      }

      return route.fulfill({
        json: {
          matterId,
          revision: 2,
          villageId,
          sourceDakId: dakId,
          capabilities: {
            canEditDraft: true,
            canSubmit: true,
            canRemark: true,
            canAnnotate: true,
            canSend: true,
            nativeRoutingPolicyPending: false
          },
          notes: baseNotes,
          documents: [
            {
              id: docId1,
              filename: "Award_12_2024.pdf",
              mime: "application/pdf",
              version: 1,
              hash: "HASH_AWARD_PDF",
              displayName: "Award No. 12/2024 (Official Copy)",
              documentRole: "Award Record",
              contentUrl: `/api/matters/${matterId}/documents/${docId1}/content`
            },
            {
              id: docId2,
              filename: "Draft_Notice_Section28A.docx",
              mime: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
              version: 1,
              hash: "HASH_DOCX",
              displayName: "Draft Notice u/s 28A",
              documentRole: "Draft Notice",
              contentUrl: `/api/matters/${matterId}/documents/${docId2}/content`
            }
          ]
        }
      });
    }

    if (pathName === `/api/matters/${matterId}/noting/draft`) {
      if (method === "GET") {
        return route.fulfill({
          headers: { ETag: '"2"' },
          json: {
            revision: 2,
            contentJson: JSON.stringify({
              type: "doc",
              content: [
                {
                  type: "paragraph",
                  content: [
                    {
                      type: "text",
                      text: "Title verification report corroborated. Notice recommended for issue to interested parties under Section 28A."
                    }
                  ]
                }
              ]
            }),
            canonicalText: "Title verification report corroborated. Notice recommended for issue to interested parties under Section 28A.\n",
            sourceDakId: dakId
          }
        });
      }
      if (method === "PUT") {
        return route.fulfill({
          headers: { ETag: '"3"' },
          json: {
            revision: 3,
            contentJson: route.request().postData(),
            canonicalText: "Saved draft content.\n",
            sourceDakId: dakId
          }
        });
      }
    }

    if (pathName.includes("/remarks")) {
      return route.fulfill({
        json: [
          {
            id: "rem-1",
            matterId,
            noteId: "00000000-0000-0000-0000-000000000301",
            anchorJson: JSON.stringify({
              version: 1,
              start: 0,
              end: 38,
              quote: "Examined field survey records for Khasra No. 42/1"
            }),
            text: "Please verify mutation entry No. 412 of 2023 in Khatoni.",
            state: "Open",
            revision: 1,
            authorUserId: "00000000-0000-0000-0000-000000000003",
            authorName: "Sh. S.P. Singh",
            designation: "SDM (LAC)",
            createdAt: "2026-10-10T14:20:00Z"
          }
        ]
      });
    }

    if (pathName.includes("/annotations")) {
      return route.fulfill({
        json: [
          {
            id: "anno-1",
            matterId,
            documentId: docId1,
            documentVersion: 1,
            documentHash: "HASH_AWARD_PDF",
            page: 4,
            regionJson: JSON.stringify({ x: 0.1, y: 0.1, width: 0.8, height: 0.1 }),
            quote: "Khasra No. 42/1 Area 4 Bigha 16 Biswa",
            text: "Area verified against Khasra Girdawari",
            authorUserId: "00000000-0000-0000-0000-000000000001",
            authorName: "Sh. R.K. Sharma",
            createdAt: "2026-10-10T12:00:00Z"
          }
        ]
      });
    }

    if (pathName.includes("/movement-targets")) {
      return route.fulfill({
        json: {
          desks: [
            {
              id: "desk-lac",
              code: "LAC-BR",
              name: "Land Acquisition Branch",
              isActive: true,
              members: [
                {
                  userId: "00000000-0000-0000-0000-000000000003",
                  displayName: "Sh. S.P. Singh",
                  designation: "SDM / LAC Officer",
                  isPrimary: true
                }
              ]
            },
            {
              id: "desk-kanungo",
              code: "KAN-BR",
              name: "Field Kanungo Branch",
              isActive: true,
              members: [
                {
                  userId: "00000000-0000-0000-0000-000000000001",
                  displayName: "Sh. R.K. Sharma",
                  designation: "Kanungo",
                  isPrimary: true
                }
              ]
            }
          ]
        }
      });
    }

    if (pathName.includes("/content")) {
      return route.fulfill({
        contentType: "application/pdf",
        body: "%PDF-1.4\n1 0 obj\n<< /Title (Award 12/2024) >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF"
      });
    }

    if (pathName.includes("/documents") || pathName.includes("/events") || pathName.includes("/workstreams")) {
      return route.fulfill({ json: [] });
    }

    // Default 200
    return route.fulfill({ json: {} });
  });

  try {
    // Shot 1: Split Screen View (1366 x 768)
    console.log("Navigating to Matter Noting Workspace...");
    await page.goto(
      `http://127.0.0.1:${PORT}/matters/${matterId}?tab=noting&sourceDakId=${dakId}`,
      { waitUntil: "networkidle" }
    );

    await page.waitForSelector(".noting-workspace-root");
    await page.waitForSelector(".official-note-card");
    await page.waitForTimeout(500);

    const shot1 = path.join(outDir, "01-noting-workspace-1366x768-split.png");
    await page.screenshot({ path: shot1 });
    console.log(`Saved screenshot 1: ${shot1}`);

    // Shot 2: Focus Mode (Fullscreen Expanded Noting)
    const focusBtn = page.locator('button:has-text("Expand to Focus")');
    if (await focusBtn.count() > 0) {
      await focusBtn.click();
      await page.waitForTimeout(400);
      const shot2 = path.join(outDir, "02-noting-workspace-1366x768-focus.png");
      await page.screenshot({ path: shot2 });
      console.log(`Saved screenshot 2: ${shot2}`);

      // Shot 3: Focus Mode Document Drawer
      const drawerToggle = page.locator(".focus-mode-drawer-toggle");
      if (await drawerToggle.count() > 0) {
        await drawerToggle.click();
        await page.waitForTimeout(400);
        const shot3 = path.join(outDir, "03-noting-workspace-citation-drawer.png");
        await page.screenshot({ path: shot3 });
        console.log(`Saved screenshot 3: ${shot3}`);
      }

      // Restore split mode
      const restoreBtn = page.locator('button:has-text("Restore Split View")');
      if (await restoreBtn.count() > 0) {
        await restoreBtn.click();
        await page.waitForTimeout(300);
      }
    }

    // Shot 4: Anchored Remarks Modal
    const remarksBtn = page.locator('button:has-text("Remarks")');
    if (await remarksBtn.count() > 0) {
      await remarksBtn.first().click();
      await page.waitForSelector(".noting-modal-card");
      await page.waitForTimeout(300);
      const shot4 = path.join(outDir, "04-noting-workspace-anchored-remarks.png");
      await page.screenshot({ path: shot4 });
      console.log(`Saved screenshot 4: ${shot4}`);

      // Close remarks modal
      await page.locator('button:has-text("Close")').click();
      await page.waitForTimeout(300);
    }

    // Shot 5: Send / Return File Movement Modal (Digital-first, no mandatory physical check)
    const sendBtn = page.locator('button:has-text("Send / Return File")');
    if (await sendBtn.count() > 0) {
      await sendBtn.click();
      await page.waitForSelector(".noting-modal-card");
      await page.waitForTimeout(300);
      const shot5 = path.join(outDir, "05-noting-workspace-send-return-modal.png");
      await page.screenshot({ path: shot5 });
      console.log(`Saved screenshot 5: ${shot5}`);

      // Close modal
      await page.locator('button:has-text("Cancel")').click();
      await page.waitForTimeout(300);
    }

    // Shot 6: Two Officers Sequential Official Notes
    note2Submitted = true;
    await page.reload({ waitUntil: "networkidle" });
    await page.waitForSelector(".official-note-card");
    await page.waitForTimeout(400);
    const shot6 = path.join(outDir, "06-noting-workspace-two-officers-sequential.png");
    await page.screenshot({ path: shot6 });
    console.log(`Saved screenshot 6: ${shot6}`);

    // Shot 7: Dak Detail Workspace -> Create/Link Matter Modal
    console.log("Navigating to Dak Detail Workspace for Create/Link Matter test...");
    await page.goto(`http://127.0.0.1:${PORT}/dak/${dakId}`, { waitUntil: "networkidle" });
    await page.waitForTimeout(500);

    // Switch to Linked Context tab
    const linksTab = page.locator('button.tab-btn:has-text("Linked Context")');
    await linksTab.waitFor({ state: "visible", timeout: 5000 });
    await linksTab.click();
    await page.waitForTimeout(400);

    // Click "+ Open / Link Matter"
    const openLinkBtn = page.locator('button:has-text("+ Open / Link Matter")');
    await openLinkBtn.waitFor({ state: "visible", timeout: 5000 });
    await openLinkBtn.click();
    await page.waitForSelector(".noting-modal-card", { timeout: 5000 });
    await page.waitForTimeout(500);

    const shot7 = path.join(outDir, "07-dak-create-link-matter-modal.png");
    await page.screenshot({ path: shot7 });
    console.log(`Saved screenshot 7: ${shot7}`);

    console.log("All 7 screenshots captured successfully!");
  } finally {
    await context.close();
    await browser.close();
    server.close();
  }
}

run().catch((err) => {
  console.error("Visual capture error:", err);
  process.exit(1);
});
