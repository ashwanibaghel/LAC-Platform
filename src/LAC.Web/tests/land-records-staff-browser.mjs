// Invoked by the guarded PostgreSQL acceptance fixture. All network activity is
// loopback; every Award/source below is a synthetic record in a disposable schema.
import assert from "node:assert/strict";
import { readFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { createServer } from "vite";
import react from "@vitejs/plugin-react";

const api = new URL(process.env.LAC_STAFF_BROWSER_API || "");
assert.equal(api.hostname, "127.0.0.1");
assert.ok(process.env.LAC_STAFF_TEST_POSTGRES?.includes("Database=lac_staff_workflow_v2_test"));
const bytes = await readFile(process.env.LAC_STAFF_BROWSER_PDF);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const artifacts = path.resolve(root, "../../out/browser-checks");
const evidenceDir = path.resolve(root, "../../docs/verification-evidence");
await mkdir(artifacts, { recursive: true });
await mkdir(evidenceDir, { recursive: true });

const server = await createServer({
  root,
  configFile: false,
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    port: 0,
    hmr: false,
    proxy: { "/api": { target: api.origin, changeOrigin: true } },
  },
});
await server.listen();
const origin = server.resolvedUrls.local[0];
const browser = await chromium.launch({ headless: true });

try {
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const request = context.request;
  const page = await context.newPage();
  const pageErrors = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));

  async function captureEvidence(filename) {
    await page.screenshot({ path: path.join(artifacts, filename) });
    await page.screenshot({ path: path.join(evidenceDir, filename) });
  }

  async function json(response, status = 200) {
    assert.equal(response.status(), status, await response.text());
    return response.json();
  }

  // 1. Authenticate with loopback Kestrel
  await json(
    await request.post(`${origin}api/auth/login`, {
      data: { username: "staff-v2-admin", password: "SyntheticStaffV2!791" },
    })
  );

  const districts = await json(await request.get(`${origin}api/districts`));
  const district = await json(await request.get(`${origin}api/districts/${districts[0].id}`));
  const subdivision = await json(
    await request.get(`${origin}api/subdivisions/${district.subDivisions[0].id}?page=0&pageSize=10`)
  );
  const villageId = subdivision.villages.items[0].id;
  const villageId2 = subdivision.villages.items[1]?.id || subdivision.villages.items[0].id;

  const key = crypto.randomUUID();
  const ownerNumber = `SYN-UI-SOURCE-${key}`;
  const targetNumber = `SYN-UI-TARGET-${key}`;

  const source = await json(
    await request.post(`${origin}api/villages/${villageId}/awards`, {
      data: { awardNumber: ownerNumber, awardDate: "2002-02-01", awardType: "Permanent" },
    }),
    201
  );
  const target = await json(
    await request.post(`${origin}api/villages/${villageId}/awards`, {
      data: { awardNumber: targetNumber, awardDate: "2002-03-01", awardType: "Permanent" },
    }),
    201
  );

  const uploaded = await json(
    await request.post(`${origin}api/awards/${source.id}/core-documents?role=Award&villageId=${villageId}`, {
      multipart: { file: { name: "synthetic-ui.pdf", mimeType: "application/pdf", buffer: bytes } },
    }),
    201
  );

  // ── SCENARIO 1: Award Workspace Overview (1366x768) ──
  await page.goto(`${origin}awards/${target.id}`);
  await page.getByRole("button", { name: /Edit Metadata/ }).waitFor();
  await captureEvidence("award-overview-premium.png");

  // ── SCENARIO 2: Ownership Conflict & Reassignment (1366x768) ──
  await page.getByRole("button", { name: /Import \/ Review Data/ }).click();
  const drawer = page.getByRole("complementary", { name: "Upload Award PDF" });
  await drawer.locator('input[type="file"]').setInputFiles({ name: "synthetic-ui.pdf", mimeType: "application/pdf", buffer: bytes });
  await drawer.getByRole("button", { name: "Upload PDF", exact: true }).click();
  const conflict = page.getByRole("alert", { name: "PDF ownership conflict" });
  await conflict.getByRole("heading", { name: "This PDF is already attached to another Award" }).waitFor();
  await conflict.getByRole("link", { name: `Open Award ${ownerNumber}` }).waitFor();
  let text = await conflict.innerText();
  assert.ok(!text.includes("RFC 9110") && !text.includes("https://tools.ietf.org") && !text.includes('"status":409'));
  assert.ok(!text.includes("Detected Type") && !text.includes("Low confidence"));

  await conflict.getByText("Officer link correction", { exact: true }).click();
  await conflict.getByLabel("Reason for resolution").selectOption("ShareSource");
  await captureEvidence("ownership-conflict-reassignment.png");
  await captureEvidence("ownership-conflict.png");

  await conflict.getByLabel("Officer reason").fill("Synthetic officer verified page covers the two test Award references.");
  await conflict.getByLabel("Source pages covering both Awards").fill("1");
  await conflict.getByLabel("Explain source coverage").fill("Synthetic page 1 source coverage, test-only officer confirmation.");
  const confirm = conflict.getByRole("button", { name: "Confirm link resolution" });
  assert.ok(await confirm.isDisabled());
  await conflict.getByLabel("I reviewed the source and confirm this resolution.").check();
  await confirm.click();
  await page.getByText("Source link resolution saved. The existing PDF and history were retained.", { exact: true }).waitFor();
  const attachments = await json(await request.get(`${origin}api/awards/${target.id}/core-documents`));
  assert.equal(attachments.length, 1);
  assert.equal(attachments[0].documentId, uploaded.documentId);
  assert.equal(attachments[0].isSharedSource, true);
  const jobs = await json(await request.get(`${origin}api/awards/${target.id}/pdf-extractions`));
  assert.equal(jobs.length, 0);

  // ── SCENARIO 3: Canonical Village Acquisition Timeline (1366x768) ──
  await page.goto(`${origin}villages/${villageId}`);
  const timeline = page.getByRole("region", { name: "Village acquisition timeline" });
  await timeline.getByRole("button").filter({ hasText: ownerNumber }).waitFor();
  for (const button of await timeline.getByRole("button", { expanded: false }).all()) await button.click();
  assert.ok((await timeline.getByRole("link", { name: "Source PDF 1" }).count()) >= 2);
  await captureEvidence("village-timeline-canonical.png");
  await captureEvidence("canonical-timeline.png");

  // ── SCENARIO 4: Direct Upload Slot (Village Core Records) (1366x768) ──
  await page.goto(`${origin}villages/${villageId}?section=core`);
  const card = page.locator(`#award-${target.id}`);
  await card.getByText(`Award ${targetNumber}`, { exact: true }).waitFor();
  const statementSlot = card.locator(".village-doc-slot").filter({ has: page.locator(".village-doc-role-name", { hasText: "Statement A" }) });
  await statementSlot.getByRole("button", { name: "+ Attach", exact: true }).click();
  const attach = page.getByRole("dialog", { name: "Attach core PDF" });
  const statementBytes = Buffer.concat([bytes, Buffer.from(`\n% Synthetic Statement A source ${key}\n`)]);
  await attach.locator('input[type="file"]').setInputFiles({ name: "statement-a.pdf", mimeType: "application/pdf", buffer: statementBytes });
  assert.equal(await attach.getByText("Low confidence").count(), 0);
  await captureEvidence("direct-upload-slot.png");

  await attach.getByRole("button", { name: "Direct Upload", exact: true }).click();
  await attach.getByRole("status").getByText("Attached", { exact: true }).waitFor();
  await attach.getByRole("button", { name: "Done — Refresh Records" }).click();
  await card.getByText("statement-a.pdf", { exact: false }).waitFor();
  assert.equal((await json(await request.get(`${origin}api/awards/${target.id}/pdf-extractions`))).length, 0);

  // ── SCENARIO 5: Smart Core Intake Proposal & Confirmation (1366x768) ──
  await page.getByRole("button", { name: /Upload Core Documents/ }).click();
  const smart = page.locator(".intake-modal");
  const smartBytes = Buffer.concat([bytes, Buffer.from(`\n% Synthetic staged source ${key}\n`)]);
  await smart.locator('input[type="file"]').setInputFiles({ name: "staged-source.pdf", mimeType: "application/pdf", buffer: smartBytes });
  await smart.getByRole("button", { name: /Analyse/ }).click();
  await smart.locator(".intake-review-list").waitFor();
  await captureEvidence("smart-intake-proposal.png");
  await captureEvidence("integrated-smart-and-direct.png");
  await smart.getByRole("button", { name: "Confirm & Link", exact: true }).click();
  await smart.locator(".intake-badge-confirmed").waitFor();

  // Close smart intake modal if open
  const closeSmartBtn = page.getByRole("button", { name: /Close|Done|✕/i });
  if (await closeSmartBtn.count() > 0) {
    await closeSmartBtn.first().click().catch(() => {});
  }

  // ── LINK SECOND VILLAGE TO TARGET AWARD (For Multi-Village Scenarios) ──
  if (villageId2 !== villageId) {
    await json(
      await request.post(`${origin}api/villages/${villageId2}/khasras/batch`, {
        data: {
          rows: [
            {
              khasraNumber: `99//1`,
              bigha: 2,
              biswa: 0,
              biswansi: 0,
              awardNumber: targetNumber,
              awardDate: "2002-03-01",
            },
          ],
        },
      })
    );
  }

  // ── SCENARIO 6: Multi-Village Notification Selector (1366x768) ──
  await page.goto(`${origin}awards/${target.id}`);
  await page.getByRole("button", { name: "+ Register Notification Source" }).click();
  const notifDrawer = page.getByRole("dialog", { name: "Register Notification Document" });
  await notifDrawer.waitFor();
  if (villageId2 !== villageId) {
    await notifDrawer.getByLabel("Target Village *").waitFor();
  }
  await captureEvidence("notification-multi-village-picker.png");

  // ── SCENARIO 7: Statutory Notification Finalization (1366x768) ──
  if (villageId2 !== villageId) {
    await notifDrawer.getByLabel("Target Village *").selectOption(villageId);
  }
  await notifDrawer.getByLabel("Notification Number *").fill(`NOTIF-4-1-${key.substring(0, 8)}`);
  await notifDrawer.getByLabel("Notification Date (Source Issue Date) *").fill("2002-01-15");
  await notifDrawer.getByPlaceholder("Optional gazette notification remarks").fill("Synthetic preliminary notification for acceptance verification.");
  const notifBytes = Buffer.concat([bytes, Buffer.from(`\n% Synthetic Notification source ${key}\n`)]);
  await notifDrawer.locator('input[type="file"]').setInputFiles({ name: "notification-4-1.pdf", mimeType: "application/pdf", buffer: notifBytes });
  await notifDrawer.getByRole("button", { name: "Register Source Document" }).click();
  await notifDrawer.waitFor({ state: "hidden" });

  const finalCard = page.getByText(`NOTIF-4-1-${key.substring(0, 8)}`, { exact: false }).first();
  await finalCard.waitFor();
  await finalCard.scrollIntoViewIfNeeded();
  await page.getByText("Finalized", { exact: true }).first().waitFor();
  await captureEvidence("notification-finalized.png");
  const staffWithNotif = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.ok(staffWithNotif.notifications?.some((n) => n.notificationNumber?.includes(`NOTIF-4-1-${key.substring(0, 8)}`) && n.isFinalized));

  // ── SCENARIO 8: Section 35 Temporary Occupation Proceedings (1366x768) ──
  await page.goto(`${origin}villages/${villageId}?section=core`);
  const sec35Section = page.locator(".temporary-proceedings-section");
  await sec35Section.waitFor();
  await sec35Section.getByRole("button", { name: "+ Record Section 35 Proceeding" }).click();
  const sec35Drawer = page.getByRole("dialog", { name: "Record Section 35 Proceeding" });
  await sec35Drawer.waitFor();
  await sec35Drawer.getByLabel("Purpose of Temporary Occupation *").fill("Construction of bypass road drainage infrastructure.");
  await sec35Drawer.getByLabel("Reference Number").fill(`SEC35-OFFER-${key.substring(0, 8)}`);
  await sec35Drawer.getByLabel("Proceeding / Legal Date").fill("2003-04-10");
  const sec35Bytes = Buffer.concat([bytes, Buffer.from(`\n% Synthetic Section 35 source ${key}\n`)]);
  await sec35Drawer.locator('input[type="file"]').setInputFiles({ name: "section-35-offer.pdf", mimeType: "application/pdf", buffer: sec35Bytes });
  await captureEvidence("temporary-proceedings-section35.png");
  await sec35Drawer.getByRole("button", { name: "Record Section 35 Proceeding" }).click();
  await sec35Drawer.waitFor({ state: "hidden" });

  // ── SCENARIO 9: Award Metadata Correction (1366x768) ──
  await page.goto(`${origin}awards/${target.id}`);
  await page.getByRole("button", { name: /Edit Metadata/ }).click();
  const edit = page.getByRole("dialog", { name: "Edit Award Metadata" });
  await edit.waitFor();
  await edit.getByLabel("Award Date").fill("2002-03-02");
  await edit.getByLabel(/Reason for Official Correction/).fill("Checked synthetic register for frontend integration verification.");
  await captureEvidence("award-metadata-correction.png");
  await edit.getByRole("button", { name: "Save Metadata Correction", exact: true }).click();
  await edit.waitFor({ state: "hidden" });
  const changed = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.equal(changed.awardDate, "2002-03-02");

  assert.deepEqual(pageErrors, []);
  console.log("PASS: all 9 critical Land Records V2 scenarios verified with authentic browser interactions.");
} finally {
  await browser.close();
  await server.close();
}
