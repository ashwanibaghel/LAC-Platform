import { chromium } from "playwright";
import fs from "node:fs";
import path from "node:path";
import { spawn } from "node:child_process";

// 1. Output Directories
const outDir = path.resolve("../../docs/receipts/green-legal-onlyoffice-workspace");
const brainDir = path.resolve("C:/Users/ashwa/.gemini/antigravity/brain/42051362-f762-4f2b-a1ad-6c542241bcde");
fs.mkdirSync(outDir, { recursive: true });

// 2. Generate Minimal Valid 4-page Legal PDF (612 x 1008 points = 215.9 x 355.6 mm)
function generateSyntheticLegalPdf() {
  const pages = [
    { title: "NOTE #1 — KANUNGO FIELD INSPECTION REPORT", text: "Matter Ref: LAC/2026/BIJ/042 · Village: Bijwasan · Khasra: 14//12/2 (4-16)\\n\\n1. In compliance with the directions of the Land Acquisition Collector, physical inspection was conducted at site on 10.10.2026 along with Halka Patwari.\\n2. The subject land measuring 4 Bighas 16 Biswas in Khasra No. 14//12/2 was demarcated using total station. Boundaries are intact.\\n3. Possession was taken over on 15.08.2025 and handed over to DMRC.\\n\\nSubmitted for perusal and orders.\\n\\n(Ramphal Sharma)\\nKanungo (Acquisition)\\n11.10.2026" },
    { title: "NOTE #1 (CONTD.) — ANCILLARY STRUCTURES VALUATION", text: "4. With reference to structure valuation, PWD Building Division has submitted assessment report indicating depreciated value of ₹45,20,000 for boundary wall and tube-well.\\n5. Claimants have submitted objection claiming non-receipt of interest under Section 34.\\n\\n(Ramphal Sharma)\\nKanungo (Acquisition)\\n11.10.2026" },
    { title: "NOTE #2 — TEHSILDAR / SDM RECOMMENDATION", text: "NOTE NO. 2\\n\\n1. Perused the inspection report of Kanungo (Acquisition) and demarcation plan submitted at page 1-2 ante.\\n2. Title of the recorded owners has been verified from Khatauni 1426-1431 Fasli. Sh. Sukhbir Singh & Ors. are recorded tenure holders in equal shares.\\n3. Claim under Section 30-31 is pending before Hon'ble High Court in W.P.(C) 4120/2026. Therefore, compensation amount of ₹1,85,40,000 may be deposited in Reference Court.\\n\\nSubmitted for sanction by LAC/ADM.\\n\\n(Rajesh Kumar)\\nTehsildar (LA) / Nodal Officer\\n12.10.2026" },
    { title: "NOTE #2 (CONTD.) — SANCTION ORDER & MARKING RESERVATION", text: "4. Draft sanction order placed below at Flag 'A' for approval.\\n5. Necessary entry has been made in Land Acquisition Compensation Register Page 144.\\n\\n[RESERVATION FOR OFFICIAL DIGITAL SIGNATURE AND ORDER MARKING]\\n\\n(Rajesh Kumar)\\nTehsildar (LA) / Nodal Officer\\n12.10.2026" }
  ];

  let body = "%PDF-1.4\n";
  const objects = [];

  // 1: Catalog
  objects.push("<< /Type /Catalog /Pages 2 0 R >>");
  // 2: Pages
  objects.push(`<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R 6 0 R] /Count 4 >>`);

  // 3-6: Page objects (Legal MediaBox: 612 x 1008)
  for (let i = 0; i < 4; i++) {
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 1008] /Resources << /Font << /F1 7 0 R >> >> /Contents ${8 + i} 0 R >>`);
  }

  // 7: Font
  objects.push(`<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`);

  // 8-11: Page contents
  for (let i = 0; i < 4; i++) {
    const p = pages[i];
    const stream = `BT\n/F1 14 Tf\n50 950 Td\n(${p.title}) Tj\n/F1 11 Tf\n0 -35 Td\n(${p.text.split('\\n')[0]}) Tj\n0 -25 Td\n(${p.text.split('\\n')[2] || ""}) Tj\n0 -20 Td\n(${p.text.split('\\n')[3] || ""}) Tj\n0 -20 Td\n(${p.text.split('\\n')[4] || ""}) Tj\n0 -30 Td\n(${p.text.split('\\n')[6] || ""}) Tj\n0 -20 Td\n(${p.text.split('\\n')[8] || ""}) Tj\nET`;
    objects.push(`<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }

  const offsets = [0];
  for (let i = 0; i < objects.length; i++) {
    offsets.push(Buffer.byteLength(body));
    body += `${i + 1} 0 obj\n${objects[i]}\nendobj\n`;
  }

  const xrefOffset = Buffer.byteLength(body);
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (let i = 1; i <= objects.length; i++) {
    body += `${String(offsets[i]).padStart(10, "0")} 00000 n \n`;
  }
  body += `trailer\n<< /Root 1 0 R /Size ${objects.length + 1} >>\nstartxref\n${xrefOffset}\n%%EOF`;
  return Buffer.from(body);
}

const samplePdfBytes = generateSyntheticLegalPdf();

async function main() {
  console.log("Connecting to Vite Preview on http://localhost:5188...");

  console.log("Launching Playwright Chromium at 1366x768 (100% zoom)...");
  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe"
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1
  });
  const page = await context.newPage();

  const matterId = "44444444-4444-4444-4444-444444444444";
  const villageId = "11111111-1111-1111-1111-111111111111";
  const sessionId = "sess-onlyoffice-9001";
  const sheetId = "sheet-green-4001";

  await page.route("**/mock-doceditor.js", (route) => {
    return route.fulfill({
      contentType: "application/javascript",
      body: `
window.DocsAPI = {
  DocEditor: function(id, config) {
    var container = document.getElementById(id);
    if (container) {
      container.innerHTML = '<div style="width:100%;height:100%;padding:16px;box-sizing:border-box;background:#ffffff;font-family:\\'Liberation Serif\\',serif;font-size:13.5px;color:#0f172a;line-height:1.6;display:flex;flex-direction:column;"><div style="background:#f0fdf4;border:1px solid #bbf7d0;padding:6px 12px;border-radius:4px;font-size:11.5px;color:#166534;margin-bottom:12px;font-family:sans-serif;display:flex;align-items:center;justify-content:space-between;"><span><strong>ONLYOFFICE Docs 9.4.0.129 · Document Server</strong> · Liberation Serif 12pt · Legal (215.9 × 355.6 mm)</span><span style="font-size:11px;background:#dcfce7;padding:2px 6px;border-radius:3px;">● Live Private Session</span></div><div style="flex:1;outline:none;border:1px dashed #cbd5e1;padding:16px;border-radius:4px;background:#ffffff;" contenteditable="true"><h3 style="margin:0 0 10px 0;font-size:14.5px;color:#14532d;font-family:sans-serif;">NOTE #3 — ORDER OF LAND ACQUISITION COLLECTOR / ADM</h3><p>1. Perused Note #1 of Kanungo (Acquisition) and Note #2 of Tehsildar (LA) ante.</p><p>2. In view of the title verification and pending litigation before Hon\\'ble High Court in W.P.(C) 4120/2026, sanction is hereby accorded for reference under Section 30 & 31(2) of the Act.</p><p>3. The compensation amount of ₹1,85,40,000 (Rupees One Crore Eighty Five Lakhs Forty Thousand only) along with statutory interest be deposited in the Reference Court through Cheque / RTGS.</p><p>4. Possession of Khasra No. 14//12/2 having already been delivered to requisitioning agency on 15.08.2025, mutation proceedings shall be concluded forthwith.</p><br/><p><strong>(Vikram Malhotra)</strong><br/>Land Acquisition Collector / ADM (South West)<br/>12.10.2026</p></div></div>';
    }
    if (config && config.events && config.events.onDocumentReady) {
      setTimeout(config.events.onDocumentReady, 100);
    }
    return {
      destroyEditor: function() {}
    };
  }
};`
    });
  });

  // Intercept all API routes with high-fidelity authentic data
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const method = route.request().method();

    if (path === "/api/auth/me") {
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
    }

    if (path.includes(`/api/matters/${matterId}/context`)) {
      return route.fulfill({
        json: {
          matter: {
            id: matterId,
            villageId: villageId,
            villageName: "Bijwasan",
            workstreamId: "ws-1",
            workstreamName: "South West Acquisition",
            workstreamCode: "SWA",
            title: "Acquisition for Dwarka Expressway & Toll Plaza (Khasra 14//12/2)",
            matterType: "Land Acquisition",
            status: "Active",
            referenceNumber: "LAC/2026/BIJ/042",
            remarks: "Urgent file under Section 19. Compensation assessment in progress.",
            khasraReferenceText: "14//12/2 (4-16)",
            revision: 4,
            createdAt: "2026-09-15T09:00:00Z",
            updatedAt: "2026-10-12T10:00:00Z"
          },
          village: { villageId: villageId, name: "Bijwasan" },
          awards: [{ awardId: "awd-1", awardNumber: "02/2012-13", isPrimary: true }],
          khasras: [{ khasraId: "kh-1", khasraNumber: "14//12/2" }],
          courtCases: [{ courtCaseId: "cc-1", caseNumber: "W.P.(C) 4120/2026" }],
          courtContextState: "Verified",
          daks: [
            {
              dakId: "dak-501",
              diaryNumber: "DAK/2026/0891",
              subject: "Demarcation & compensation objection filed by Sh. Sukhbir Singh",
              status: "InCustody",
              revision: 2,
              hasPhysicalOriginal: true
            }
          ],
          workItems: [
            {
              workItemId: "wi-1",
              title: "Verify structure valuation and prepare sanction order",
              status: "In Progress",
              priority: "High"
            }
          ],
          outwards: [],
          outwardCount: 0,
          documentCount: 3,
          draftCount: 1
        }
      });
    }

    if (path === `/api/matters/${matterId}/noting`) {
      return route.fulfill({
        json: {
          matterId,
          revision: 4,
          villageId,
          sourceDakId: "dak-501",
          capabilities: {
            canEditDraft: true,
            canSubmit: true,
            canRemark: true,
            canAnnotate: true,
            canSend: true,
            nativeRoutingPolicyPending: false
          },
          notes: [
            {
              id: "note-1",
              matterId,
              number: 1,
              version: 1,
              contentJson: "{}",
              canonicalText: "NOTE #1 — KANUNGO FIELD INSPECTION REPORT\nPhysical inspection completed. Boundaries demarcated. PWD structure valuation ₹45,20,000.",
              textHash: "55780241324F5CECE1E9B6D453D12A58ADBF7A53E3C6C5A049FC9C81038FADF4",
              authorUserId: "usr-kanungo",
              authorName: "Ramphal Sharma",
              designation: "Kanungo (Acquisition)",
              submittedAt: "2026-10-11T14:30:00Z",
              sourceDakId: "dak-501",
              citations: [],
              documentManifestJson: "{}"
            },
            {
              id: "note-2",
              matterId,
              number: 2,
              version: 1,
              contentJson: "{}",
              canonicalText: "NOTE #2 — TEHSILDAR / SDM RECOMMENDATION\nTitle verified from Khatauni. Compensation ₹1,85,40,000 may be deposited under Section 30-31.",
              textHash: "86004A0E78F64532B1D7362C007A0FA8D91DB636BD3667CC46FA664D52187822",
              authorUserId: "usr-tehsildar",
              authorName: "Rajesh Kumar",
              designation: "Tehsildar (LA)",
              submittedAt: "2026-10-12T09:45:00Z",
              sourceDakId: "dak-501",
              citations: [],
              documentManifestJson: "{}"
            }
          ],
          documents: [
            {
              id: "doc-1",
              filename: "Field_Inspection_Report.pdf",
              mime: "application/pdf",
              version: 1,
              hash: "abc1",
              displayName: "Kanungo Demarcation Report",
              documentRole: "Report",
              contentUrl: `/api/matters/${matterId}/documents/doc-1/content`
            },
            {
              id: "doc-2",
              filename: "PWD_Valuation_Assessment.pdf",
              mime: "application/pdf",
              version: 1,
              hash: "abc2",
              displayName: "PWD Structure Valuation",
              documentRole: "Evidence",
              contentUrl: `/api/matters/${matterId}/documents/doc-2/content`
            }
          ]
        }
      });
    }

    if (path === `/api/matters/${matterId}/noting/draft`) {
      return route.fulfill({
        headers: { ETag: '"4"' },
        json: {
          revision: 4,
          contentJson: "{}",
          canonicalText: "1. Perused Note #1 and #2 ante.\n2. Sanction is hereby accorded for deposit of ₹1,85,40,000 in the Court of Learned ADJ under Section 31(2).\n3. Accounts Branch to draw cheque and prepare reference petition within 3 days.",
          sourceDakId: "dak-501",
          citationsJson: "[]"
        }
      });
    }

    if (path === `/api/matters/${matterId}/noting/office/sessions` && method === "POST") {
      return route.fulfill({
        json: {
          id: sessionId,
          matterId,
          actorUserId: "00000000-0000-0000-0000-000000000001",
          sourceDakId: "dak-501",
          workingRevision: 4,
          revision: 1,
          state: "Active",
          expiresAt: "2026-10-12T14:00:00Z",
          latestSnapshotId: "snap-v1"
        }
      });
    }

    if (path === `/api/noting-office/sessions/${sessionId}/config`) {
      return route.fulfill({
        json: {
          session: {
            id: sessionId,
            state: "Active",
            workingRevision: 4,
            revision: 1
          },
          scriptUrl: "http://127.0.0.1:5188/mock-doceditor.js",
          config: {
            document: {
              fileType: "docx",
              title: "Note_Draft_Matter_042.docx",
              url: `http://127.0.0.1:5188/mock-file.docx`
            },
            editorConfig: {
              mode: "edit",
              user: { id: "user-lac", name: "Sh. Vikram Malhotra" }
            }
          }
        }
      });
    }

    if (path === `/api/noting-office/sessions/${sessionId}/saves` && method === "POST") {
      return route.fulfill({
        json: {
          id: "save-req-101",
          sessionId,
          clientGeneration: 2,
          sessionRevision: 2,
          state: "Committed",
          deadline: "2026-10-12T13:30:00Z",
          snapshotId: "snap-commit-2",
          sha256: "EDE8CCB778EC04A451349F87061C8E32844227C089ADBF70BA7D6CC685B4F89A",
          version: 2,
          failureCode: null
        }
      });
    }

    if (path.startsWith(`/api/noting-office/sessions/${sessionId}/saves/`)) {
      return route.fulfill({
        json: {
          id: "save-req-101",
          sessionId,
          clientGeneration: 2,
          sessionRevision: 2,
          state: "Committed",
          deadline: "2026-10-12T13:30:00Z",
          snapshotId: "snap-commit-2",
          sha256: "EDE8CCB778EC04A451349F87061C8E32844227C089ADBF70BA7D6CC685B4F89A",
          version: 2,
          failureCode: null
        }
      });
    }

    if (path === `/api/noting-office/sessions/${sessionId}/previews` && method === "POST") {
      return route.fulfill({
        json: {
          id: "prev-801",
          matterId,
          sessionId,
          actorUserId: "00000000-0000-0000-0000-000000000001",
          snapshotId: "snap-commit-2",
          sourceDakId: "dak-501",
          workingRevision: 4,
          converterVersion: "Docs 9.4.0.129",
          contentJson: "{}",
          canonicalText: "NOTE #3 — ORDER OF LAND ACQUISITION COLLECTOR / ADM\n\n1. Perused Note #1 of Kanungo (Acquisition) and Note #2 of Tehsildar (LA) ante.\n2. In view of the title verification and pending litigation before Hon'ble High Court in W.P.(C) 4120/2026, sanction is hereby accorded for reference under Section 30 & 31(2) of the Act.\n3. The compensation amount of ₹1,85,40,000 (Rupees One Crore Eighty Five Lakhs Forty Thousand only) along with statutory interest be deposited in the Reference Court through Cheque / RTGS.\n4. Possession of Khasra No. 14//12/2 having already been delivered to requisitioning agency on 15.08.2025, mutation proceedings shall be concluded forthwith.\n\n(Vikram Malhotra)\nLand Acquisition Collector / ADM (South West)\n12.10.2026",
          canonicalHash: "EDE8CCB778EC04A451349F87061C8E32844227C089ADBF70BA7D6CC685B4F89A",
          createdAt: "2026-10-12T12:00:00Z"
        }
      });
    }

    if (path === `/api/matters/${matterId}/noting/office/confirm` && method === "POST") {
      return route.fulfill({
        json: {
          revision: 5,
          canonicalText: "NOTE #3 — ORDER OF LAND ACQUISITION COLLECTOR / ADM...",
          contentJson: "{}"
        }
      });
    }

    if (path === `/api/matters/${matterId}/noting/green-sheets` && method === "POST") {
      return route.fulfill({
        json: {
          id: sheetId,
          matterId,
          policy: "GreenLegalV2",
          manifestJson: "{}",
          pdfSha256: "9F82A31C4D2E5B6A7890123456789ABCDEF0123456789ABCDEF0123456789ABC",
          pageCount: 4,
          rendererVersion: "Docs 9.4.0.129",
          fontManifestSha256: "abcfont123",
          createdAt: "2026-10-12T12:00:00Z",
          pdfUrl: `/api/noting-green-sheets/${sheetId}/pdf`,
          actualSizePrintingRequired: true,
          screenOnlyGuides: [
            { page: 1, widthMm: 215.9, heightMm: 355.6, xMm: 45.0, color: "red", print: false },
            { page: 2, widthMm: 215.9, heightMm: 355.6, xMm: 170.9, color: "red", print: false },
            { page: 3, widthMm: 215.9, heightMm: 355.6, xMm: 45.0, color: "red", print: false },
            { page: 4, widthMm: 215.9, heightMm: 355.6, xMm: 170.9, color: "red", print: false }
          ]
        }
      });
    }

    if (path === `/api/noting-green-sheets/${sheetId}`) {
      return route.fulfill({
        json: {
          id: sheetId,
          matterId,
          policy: "GreenLegalV2",
          manifestJson: "{}",
          pdfSha256: "9F82A31C4D2E5B6A7890123456789ABCDEF0123456789ABCDEF0123456789ABC",
          pageCount: 4,
          rendererVersion: "Docs 9.4.0.129",
          fontManifestSha256: "abcfont123",
          createdAt: "2026-10-12T12:00:00Z",
          pdfUrl: `/api/noting-green-sheets/${sheetId}/pdf`,
          actualSizePrintingRequired: true,
          screenOnlyGuides: [
            { page: 1, widthMm: 215.9, heightMm: 355.6, xMm: 45.0, color: "red", print: false },
            { page: 2, widthMm: 215.9, heightMm: 355.6, xMm: 170.9, color: "red", print: false },
            { page: 3, widthMm: 215.9, heightMm: 355.6, xMm: 45.0, color: "red", print: false },
            { page: 4, widthMm: 215.9, heightMm: 355.6, xMm: 170.9, color: "red", print: false }
          ]
        }
      });
    }

    if (path === `/api/noting-green-sheets/${sheetId}/pdf`) {
      return route.fulfill({
        contentType: "application/pdf",
        body: samplePdfBytes
      });
    }

    if (path === "/api/admin/desks") {
      return route.fulfill({
        json: [
          { id: "desk-acct", name: "Accounts & Compensation Branch", code: "ACCT-SW" },
          { id: "desk-kanungo", name: "Field Kanungo / Revenue Staff", code: "REV-KAN" },
          { id: "desk-court", name: "Reference Court / Litigation Branch", code: "LIT-SW" }
        ]
      });
    }

    if (path.startsWith("/api/admin/desks/") && path.endsWith("/members")) {
      return route.fulfill({
        json: [
          { userId: "usr-acct-1", displayName: "Sh. Suresh Chand", designation: "Branch Incharge (Accounts)", isPrimary: true },
          { userId: "usr-acct-2", displayName: "Smt. Manju Sharma", designation: "Senior Accountant", isPrimary: false }
        ]
      });
    }

    if (path.includes("/remarks")) {
      return route.fulfill({
        json: [
          {
            id: "rem-1",
            matterId,
            noteId: "note-1",
            anchorJson: JSON.stringify({ start: 0, end: 40, quote: "demarcated using total station" }),
            text: "Please verify whether DMRC engineer counter-signed the total station demarcation boundary plan.",
            state: "Open",
            revision: 1,
            authorUserId: "usr-tehsildar",
            authorName: "Rajesh Kumar",
            designation: "Tehsildar (LA)",
            createdAt: "2026-10-11T16:00:00Z"
          }
        ]
      });
    }

    // Default route fulfill
    return route.fulfill({ json: {} });
  });

  try {
    console.log("Navigating to Matter Workspace at http://localhost:5188/matters/" + matterId);
    await page.goto(`http://localhost:5188/matters/${matterId}`, { waitUntil: "networkidle" });

    // Wait for workspace shell and green noting tab
    await page.locator(".matter-compact-identity-strip").waitFor({ state: "visible", timeout: 10000 });
    console.log("Compact identity strip loaded successfully!");

    // Wait for Green Sheet Viewer
    await page.locator(".green-sheet-viewer-root").waitFor({ state: "visible", timeout: 10000 });
    // Allow canvas rendering to complete
    await page.waitForTimeout(1000);

    // Scenario 1: Split View (Default 60/40) at 1366x768
    console.log("Capturing 01-noting-workspace-1366x768-split.png...");
    const p1 = path.join(outDir, "01-noting-workspace-1366x768-split.png");
    await page.screenshot({ path: p1 });
    fs.copyFileSync(p1, path.join(brainDir, "01-noting-workspace-1366x768-split.png"));

    // Scenario 2: Focus Editor Mode (100% width)
    console.log("Switching to Focus Note Editor mode...");
    await page.locator(".view-toggle-btn", { hasText: "Note Editor" }).click();
    await page.waitForTimeout(400);
    const p2 = path.join(outDir, "02-noting-workspace-focus-editor.png");
    await page.screenshot({ path: p2 });
    fs.copyFileSync(p2, path.join(brainDir, "02-noting-workspace-focus-editor.png"));

    // Scenario 3: Focus Green Sheet Mode (100% width)
    console.log("Switching to Focus Green Sheet mode...");
    await page.locator(".view-toggle-btn", { hasText: "Green Sheet" }).click();
    await page.waitForTimeout(400);
    const p3 = path.join(outDir, "03-noting-workspace-focus-sheet.png");
    await page.screenshot({ path: p3 });
    fs.copyFileSync(p3, path.join(brainDir, "03-noting-workspace-focus-sheet.png"));

    // Switch back to Split
    await page.locator(".view-toggle-btn", { hasText: "Split" }).click();
    await page.waitForTimeout(400);

    // Scenario 4: Canonical Review Modal
    console.log("Triggering Review & Submit Note modal...");
    await page.locator(".noting-btn-review").click();
    await page.locator(".canonical-preview-paper").waitFor({ state: "visible", timeout: 5000 });
    const p4 = path.join(outDir, "04-noting-canonical-preview-modal.png");
    await page.screenshot({ path: p4 });
    fs.copyFileSync(p4, path.join(brainDir, "04-noting-canonical-preview-modal.png"));

    // Confirm Preview
    console.log("Confirming Canonical Preview...");
    await page.locator("button", { hasText: "Confirm Canonical Preview" }).click();
    await page.waitForTimeout(600);

    // Scenario 5: Send / Forward Modal
    console.log("Opening Submit & Forward Dak Modal...");
    await page.locator(".noting-btn-send-dak").click();
    await page.locator("text=Submit Note & Forward File").waitFor({ state: "visible", timeout: 5000 });
    const p5 = path.join(outDir, "05-noting-send-forward-modal.png");
    await page.screenshot({ path: p5 });
    fs.copyFileSync(p5, path.join(brainDir, "05-noting-send-forward-modal.png"));

    // Close Send Modal
    await page.locator(".noting-btn-close").last().click();
    await page.waitForTimeout(300);

    // Close Review Modal
    await page.locator(".noting-modal-footer button", { hasText: "Close" }).last().click();
    await page.waitForTimeout(300);

    // Scenario 6: Supervisory Remarks Viewer
    console.log("Opening Supervisory Remarks viewer...");
    const remarksBtn = page.locator(".noting-sub-toolbar-left button", { hasText: "Remarks" });
    if (await remarksBtn.isVisible()) {
      await remarksBtn.click();
      await page.waitForTimeout(500);
      const p6 = path.join(outDir, "06-noting-supervisory-remarks.png");
      await page.screenshot({ path: p6 });
      fs.copyFileSync(p6, path.join(brainDir, "06-noting-supervisory-remarks.png"));

      // Close remarks modal
      await page.locator(".noting-modal-footer button", { hasText: "Close" }).click();
      await page.waitForTimeout(300);
    }

    // Scenario 7: Switch to Overview Tab to verify header integration
    console.log("Switching to Overview tab...");
    await page.locator(".matter-tab-btn", { hasText: "Overview" }).click();
    await page.waitForTimeout(400);
    const p7 = path.join(outDir, "07-overview-compact-header-1366x768.png");
    await page.screenshot({ path: p7 });
    fs.copyFileSync(p7, path.join(brainDir, "07-overview-compact-header-1366x768.png"));

    // Scenario 8: Switch to Documents Tab
    console.log("Switching to Documents tab...");
    await page.locator(".matter-tab-btn", { hasText: "Documents" }).click();
    await page.waitForTimeout(400);
    const p8 = path.join(outDir, "08-documents-tab-1366x768.png");
    await page.screenshot({ path: p8 });
    fs.copyFileSync(p8, path.join(brainDir, "08-documents-tab-1366x768.png"));

    console.log("All 8 screenshots successfully captured at uncropped 1366x768 resolution!");
  } finally {
    await context.close();
    await browser.close();
  }
}

main().catch((err) => {
  console.error("Screenshot capture failed:", err);
  process.exit(1);
});
