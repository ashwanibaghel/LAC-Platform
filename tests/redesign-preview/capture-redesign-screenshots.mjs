import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "../../src/LAC.Web/node_modules/playwright/index.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const htmlPath = path.resolve(__dirname, "matter-workspace-redesign.html");
const outDir = path.resolve(__dirname, "../../docs/receipts/matter-noting-workspace/redesign-proposal");
fs.mkdirSync(outDir, { recursive: true });

function createServer(port) {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      try {
        const content = fs.readFileSync(htmlPath);
        res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
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
  const PORT = 5489;
  const server = await createServer(PORT);
  console.log(`Design preview server running on http://127.0.0.1:${PORT}`);

  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe"
  });

  const context = await browser.newContext({
    viewport: { width: 1366, height: 768 },
    deviceScaleFactor: 1
  });

  const page = await context.newPage();

  const scenarios = [
    {
      id: "01-empty-note-sheet-1366x768.png",
      view: "empty-note1",
      desc: "1. Empty official Green Note Sheet ready for Note 1"
    },
    {
      id: "02-multiple-notes-active-draft-1366x768.png",
      view: "multiple-notes",
      desc: "2. Multiple submitted notes with a new active draft"
    },
    {
      id: "03-split-noting-pdf-preview-1366x768.png",
      view: "split-pdf",
      desc: "3. Split noting with a readable PDF preview"
    },
    {
      id: "04-supervisory-remark-anchored-1366x768.png",
      view: "supervisory-remark",
      desc: "4. A supervisory remark attached to an exact phrase"
    },
    {
      id: "05-letter-drafting-onlyoffice-entry-1366x768.png",
      view: "letter-drafting",
      desc: "5. Letter drafting and ONLYOFFICE entry"
    },
    {
      id: "06-full-width-focus-editing-1366x768.png",
      view: "focus-mode",
      desc: "6. Full-width distraction-free editing"
    },
    {
      id: "07-documents-tab-context-1366x768.png",
      view: "tab-documents",
      desc: "7. Documents tab with contextual document toolbar"
    },
    {
      id: "08-overview-tab-context-1366x768.png",
      view: "tab-overview",
      desc: "8. Overview tab with contextual management toolbar"
    }
  ];

  try {
    for (const s of scenarios) {
      console.log(`Capturing ${s.desc}...`);
      await page.goto(`http://127.0.0.1:${PORT}/?view=${s.view}`, { waitUntil: "networkidle" });
      await page.waitForTimeout(400);

      const filePath = path.join(outDir, s.id);
      await page.screenshot({ path: filePath, fullPage: false });
      console.log(`✓ Saved: ${filePath}`);
    }

    console.log("\nALL 8 DESIGN GATE SCREENSHOTS CAPTURED AT 1366x768 SUCCESSFULLY!");
  } finally {
    await context.close();
    await browser.close();
    server.close();
  }
}

run().catch((err) => {
  console.error("Screenshot capture failed:", err);
  process.exit(1);
});
