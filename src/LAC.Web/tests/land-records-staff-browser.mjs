// Invoked by the guarded PostgreSQL acceptance fixture. All network activity is
// loopback; every Award/source below is a synthetic record in a disposable schema.
import assert from "node:assert/strict";
import crypto from "node:crypto";
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
  assert.ok(subdivision.villages.items.length >= 2, "Test environment must provide at least two villages");
  const villageId = subdivision.villages.items[0].id;
  const villageId2 = subdivision.villages.items[1].id;
  assert.notEqual(villageId, villageId2, "Two distinct villages required for multi-village testing");

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

  // ── SCENARIO 6: Multi-Village Notification Selector & Strict Enforcement (1366x768) ──
  await page.goto(`${origin}awards/${target.id}`);
  await page.getByRole("button", { name: "+ Register Notification Source" }).click();
  const notifDrawer = page.getByRole("dialog", { name: "Register Notification Document" });
  await notifDrawer.waitFor();
  const villageSelect = notifDrawer.getByLabel("Target Village *");
  await villageSelect.waitFor();
  await captureEvidence("notification-multi-village-picker.png");

  // 6a. Blank selection blocking: submit button must be disabled
  const submitNotifBtn = notifDrawer.getByRole("button", { name: "Register Source Document" });
  assert.ok(await submitNotifBtn.isDisabled(), "Submission must be blocked when target village is blank");

  // 6b. Wrong-Village rejection check via API
  const unlinkedVillageId = crypto.randomUUID();
  const wrongNotifRes = await request.post(
    `${origin}api/awards/${target.id}/notification-documents?mutationId=${crypto.randomUUID()}&villageId=${unlinkedVillageId}&sectionType=4(1)&notificationDate=2002-01-15&notificationNumber=WRONG-VIL`,
    {
      multipart: { file: { name: "wrong-village.pdf", mimeType: "application/pdf", buffer: bytes } },
    }
  );
  assert.equal(wrongNotifRes.status(), 400);
  const wrongNotifJson = await wrongNotifRes.json();
  assert.equal(wrongNotifJson.code, "VillageContextRequired");

  // ── SCENARIO 7: Statutory Notification Finalization & Server Readback (1366x768) ──
  await villageSelect.selectOption(villageId);
  await notifDrawer.getByLabel("Notification Number *").fill(`NOTIF-4-1-${key.substring(0, 8)}`);
  await notifDrawer.getByLabel("Notification Date (Source Issue Date) *").fill("2002-01-15");
  await notifDrawer.getByPlaceholder("Optional gazette notification remarks").fill("Synthetic preliminary notification for acceptance verification.");
  const notifBytes = Buffer.concat([bytes, Buffer.from(`\n% Synthetic Notification source ${key}\n`)]);
  await notifDrawer.locator('input[type="file"]').setInputFiles({ name: "notification-4-1.pdf", mimeType: "application/pdf", buffer: notifBytes });
  
  assert.ok(!await submitNotifBtn.isDisabled(), "Button enabled when valid village, date, and PDF are provided");
  await submitNotifBtn.click();
  await notifDrawer.waitFor({ state: "hidden" });

  const finalCard = page.getByText(`NOTIF-4-1-${key.substring(0, 8)}`, { exact: false }).first();
  await finalCard.waitFor();
  await finalCard.scrollIntoViewIfNeeded();
  await page.getByText("Finalized", { exact: true }).first().waitFor();
  await captureEvidence("notification-finalized.png");
  const staffWithNotif = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  const registeredNotif = staffWithNotif.notifications?.find((n) => n.notificationNumber?.includes(`NOTIF-4-1-${key.substring(0, 8)}`));
  assert.ok(registeredNotif, "Notification must be found in staff-workflow");
  assert.equal(registeredNotif.isFinalized, true);
  assert.equal(registeredNotif.notificationDate.substring(0, 10), "2002-01-15");
  assert.ok(registeredNotif.documents?.length >= 1);

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

  const timelineAfterSec35 = await json(await request.get(`${origin}api/villages/${villageId}/acquisition-timeline`));
  assert.ok(
    timelineAfterSec35.events?.some((e) => e.eventType === "TemporaryProceeding" && e.title?.includes("Construction of bypass road drainage infrastructure")),
    "Section 35 must appear in village acquisition timeline"
  );

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

  // ── SCENARIO 10: Award Lifecycle Operations (Void, Restore, Archive) via UI & Server Readback ──
  // 10a. Void Award
  await page.getByRole("button", { name: /Lifecycle/ }).click();
  const voidModal = page.getByRole("dialog", { name: "Award Lifecycle Management" });
  await voidModal.waitFor();
  await voidModal.getByLabel("Void Duplicate / Mistaken Entry").check();
  await voidModal.getByPlaceholder("Mandatory administrative justification").fill("Mistaken duplicate synthetic registration.");
  await voidModal.getByRole("button", { name: "Confirm Void" }).click();
  await voidModal.waitFor({ state: "hidden" });
  await page.getByText("Archived (Void)").waitFor();
  let workflowState = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.equal(workflowState.recordStatus, "Archived");
  assert.equal(workflowState.archiveKind, "Void");

  // 10b. Restore Award
  await page.getByRole("button", { name: /Lifecycle/ }).click();
  const restoreModal1 = page.getByRole("dialog", { name: "Award Lifecycle Management" });
  await restoreModal1.waitFor();
  await restoreModal1.getByPlaceholder("Mandatory administrative justification").fill("Restoring voided record to active status after verification.");
  await restoreModal1.getByRole("button", { name: "Restore Award" }).click();
  await restoreModal1.waitFor({ state: "hidden" });
  workflowState = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.equal(workflowState.recordStatus, "Active");

  // 10c. Archive Award
  await page.getByRole("button", { name: /Lifecycle/ }).click();
  const archiveModal = page.getByRole("dialog", { name: "Award Lifecycle Management" });
  await archiveModal.waitFor();
  await archiveModal.getByLabel("Archive Award").check();
  await archiveModal.getByPlaceholder("Mandatory administrative justification").fill("Administrative archiving for test verification.");
  await archiveModal.getByRole("button", { name: "Confirm Archive" }).click();
  await archiveModal.waitFor({ state: "hidden" });
  await page.getByText("Archived (Archive)").waitFor();
  workflowState = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.equal(workflowState.recordStatus, "Archived");
  assert.equal(workflowState.archiveKind, "Archive");

  // 10d. Restore back to Active for subsequent test steps
  await page.getByRole("button", { name: /Lifecycle/ }).click();
  const restoreModal2 = page.getByRole("dialog", { name: "Award Lifecycle Management" });
  await restoreModal2.waitFor();
  await restoreModal2.getByPlaceholder("Mandatory administrative justification").fill("Restoring to active for downstream test steps.");
  await restoreModal2.getByRole("button", { name: "Restore Award" }).click();
  await restoreModal2.waitFor({ state: "hidden" });
  workflowState = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  assert.equal(workflowState.recordStatus, "Active");

  // ── SCENARIO 11: Multiple Core Documents per Role (PP, NM, Statement A) ──
  // 11a. Second Statement A
  const statementABytes2 = Buffer.concat([bytes, Buffer.from(`\n% Synthetic Statement A source #2 ${key}\n`)]);
  await json(
    await request.post(`${origin}api/awards/${target.id}/core-documents?role=StatementA&villageId=${villageId}&title=Statement%20A%20Part%202`, {
      multipart: { file: { name: "statement-a-part2.pdf", mimeType: "application/pdf", buffer: statementABytes2 } },
    }),
    201
  );

  // 11b. Two Naksha Muntazamin (NM) documents (dates optional)
  const nmBytes1 = Buffer.concat([bytes, Buffer.from(`\n% Synthetic NM source #1 ${key}\n`)]);
  const nmBytes2 = Buffer.concat([bytes, Buffer.from(`\n% Synthetic NM source #2 ${key}\n`)]);
  await json(
    await request.post(`${origin}api/awards/${target.id}/core-documents?role=NM&villageId=${villageId}&title=Field%20Map%20Sheet%201`, {
      multipart: { file: { name: "naksha-muntazamin-1.pdf", mimeType: "application/pdf", buffer: nmBytes1 } },
    }),
    201
  );
  await json(
    await request.post(`${origin}api/awards/${target.id}/core-documents?role=NM&villageId=${villageId2}&title=Field%20Map%20Sheet%202`, {
      multipart: { file: { name: "naksha-muntazamin-2.pdf", mimeType: "application/pdf", buffer: nmBytes2 } },
    }),
    201
  );

  // 11c. Possession Proceedings (PP) requiring mandatory legal date
  // Negative check: PP without legal date must fail with 400 PossessionDateRequired
  const ppBytesFail = Buffer.concat([bytes, Buffer.from(`\n% Synthetic PP fail ${key}\n`)]);
  const ppFailRes = await request.post(`${origin}api/awards/${target.id}/core-documents?role=PossessionProceeding&villageId=${villageId}`, {
    multipart: { file: { name: "possession-fail.pdf", mimeType: "application/pdf", buffer: ppBytesFail } },
  });
  assert.equal(ppFailRes.status(), 400);
  const ppFailJson = await ppFailRes.json();
  assert.equal(ppFailJson.code, "PossessionDateRequired");

  // Deterministic check: Two genuinely distinct Possession Proceedings with the same legalDate and correct canonical village/attachment associations
  const commonLegalDate = "2002-04-01";
  const ppBytes1 = Buffer.concat([bytes, Buffer.from(`\n% Synthetic PP #1 distinct source ${key}\n`)]);
  const ppBytes2 = Buffer.concat([bytes, Buffer.from(`\n% Synthetic PP #2 distinct source ${key}\n`)]);
  assert.notEqual(crypto.createHash("sha256").update(ppBytes1).digest("hex"), crypto.createHash("sha256").update(ppBytes2).digest("hex"), "PP byte sources must be distinct");

  const ppRes1 = await json(
    await request.post(`${origin}api/awards/${target.id}/core-documents?role=PossessionProceeding&villageId=${villageId}&legalDate=${commonLegalDate}&title=Kabza%20Karwahi%20Village%201`, {
      multipart: { file: { name: "possession-village1.pdf", mimeType: "application/pdf", buffer: ppBytes1 } },
    }),
    201
  );
  const ppRes2 = await json(
    await request.post(`${origin}api/awards/${target.id}/core-documents?role=PossessionProceeding&villageId=${villageId2}&legalDate=${commonLegalDate}&title=Kabza%20Karwahi%20Village%202`, {
      multipart: { file: { name: "possession-village2.pdf", mimeType: "application/pdf", buffer: ppBytes2 } },
    }),
    201
  );

  assert.notEqual(ppRes1.attachmentId, ppRes2.attachmentId, "Attachments must have distinct IDs");
  assert.notEqual(ppRes1.documentId, ppRes2.documentId, "Attachments must reference distinct documents");
  assert.notEqual(ppRes1.possessionEventId, ppRes2.possessionEventId, "Attachments must create distinct possession events");

  // Read back all core documents and verify counts per role
  const allDocs = await json(await request.get(`${origin}api/awards/${target.id}/core-documents`));
  const statementADocs = allDocs.filter((d) => d.role === "StatementA" && !d.isDetached);
  const nmDocs = allDocs.filter((d) => d.role === "NM" && !d.isDetached);
  const ppDocs = allDocs.filter((d) => d.role === "PossessionProceeding" && !d.isDetached);
  assert.ok(statementADocs.length >= 2, "Expected at least 2 Statement A attachments");
  assert.ok(nmDocs.length >= 2, "Expected at least 2 Naksha Muntazamin attachments");
  assert.ok(ppDocs.length >= 2, "Expected at least 2 Possession Proceeding attachments");

  // Specific canonical village and attachment associations for the two same-date PPs
  const ppDoc1 = ppDocs.find((d) => d.attachmentId === ppRes1.attachmentId);
  const ppDoc2 = ppDocs.find((d) => d.attachmentId === ppRes2.attachmentId);
  assert.ok(ppDoc1, "PP #1 must be found in core documents list");
  assert.ok(ppDoc2, "PP #2 must be found in core documents list");
  assert.equal(ppDoc1.legalDate.substring(0, 10), commonLegalDate);
  assert.equal(ppDoc2.legalDate.substring(0, 10), commonLegalDate);
  assert.equal(ppDoc1.villageId, villageId, "PP #1 must be canonically associated with Village 1");
  assert.equal(ppDoc2.villageId, villageId2, "PP #2 must be canonically associated with Village 2");
  assert.equal(ppDoc1.title, "Kabza Karwahi Village 1");
  assert.equal(ppDoc2.title, "Kabza Karwahi Village 2");

  // ── SCENARIO 12: Attachment Metadata Correction, Detach & Restore ──
  const nmAttachment = nmDocs[0];
  const updateRes = await json(
    await request.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
      data: {
        mutationId: crypto.randomUUID(),
        expectedRevision: nmAttachment.revision,
        action: "Update",
        title: "Verified Revenue Map Sheet 1",
        remarks: "Official boundary verification completed",
        reason: "Administrative title correction per survey register",
      },
    })
  );
  assert.equal(updateRes.isDetached, false);

  // Readback assertion for Update
  let refreshedDocs = await json(await request.get(`${origin}api/awards/${target.id}/core-documents`));
  let updatedAttachment = refreshedDocs.find((d) => d.attachmentId === nmAttachment.attachmentId);
  assert.equal(updatedAttachment.title, "Verified Revenue Map Sheet 1");

  // Detach attachment
  const detachRes = await json(
    await request.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
      data: {
        mutationId: crypto.randomUUID(),
        expectedRevision: updateRes.revision,
        action: "Detach",
        reason: "Detached pending second surveyor confirmation",
      },
    })
  );
  assert.equal(detachRes.isDetached, true);

  // Readback assertion for Detach (active list excludes it)
  refreshedDocs = await json(await request.get(`${origin}api/awards/${target.id}/core-documents`));
  assert.ok(!refreshedDocs.some((d) => d.attachmentId === nmAttachment.attachmentId), "Active list must not include detached document");

  // Restore attachment
  const restoreRes = await json(
    await request.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
      data: {
        mutationId: crypto.randomUUID(),
        expectedRevision: detachRes.revision,
        action: "Restore",
        reason: "Surveyor verification confirmed; restored to active file",
      },
    })
  );
  assert.equal(restoreRes.isDetached, false);

  // Readback assertion for Restore (active list includes it again)
  refreshedDocs = await json(await request.get(`${origin}api/awards/${target.id}/core-documents`));
  assert.ok(refreshedDocs.some((d) => d.attachmentId === nmAttachment.attachmentId), "Active list must include restored document");

  // ── SCENARIO 13: Targeted Negative-Path Checks & Idempotency ──
  // 13a. Stale Revision Conflict on Award Metadata
  const staleMetadataRes = await request.put(`${origin}api/awards/${target.id}/metadata`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: 999,
      awardNumber: targetNumber,
      reason: "Stale update attempt",
    },
  });
  assert.equal(staleMetadataRes.status(), 409);
  const staleMetadataJson = await staleMetadataRes.json();
  assert.equal(staleMetadataJson.code, "RevisionConflict");

  // 13b. Stale Revision Conflict on Award Lifecycle
  const staleLifecycleRes = await request.post(`${origin}api/awards/${target.id}/lifecycle`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: 999,
      action: "Archive",
      reason: "Stale lifecycle attempt",
    },
  });
  assert.equal(staleLifecycleRes.status(), 409);
  const staleLifecycleJson = await staleLifecycleRes.json();
  assert.equal(staleLifecycleJson.code, "RevisionConflict");

  // 13c. Stale Revision Conflict on Attachment Link
  const staleLinkRes = await request.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: 999,
      action: "Update",
      title: "Stale link update",
      reason: "Stale link update attempt",
    },
  });
  assert.equal(staleLinkRes.status(), 409);
  const staleLinkJson = await staleLinkRes.json();
  assert.equal(staleLinkJson.code, "RevisionConflict");

  // 13d(i). Unauthorized Writes: Anonymous unauthenticated client without session cookies (401)
  const unauthContext = await browser.newContext();
  const unauthRequest = unauthContext.request;
  
  const unauthMetadata = await unauthRequest.put(`${origin}api/awards/${target.id}/metadata`, {
    data: { mutationId: crypto.randomUUID(), expectedRevision: 0, awardNumber: "HACK", reason: "Unauthorized" },
  });
  assert.equal(unauthMetadata.status(), 401);

  const unauthLifecycle = await unauthRequest.post(`${origin}api/awards/${target.id}/lifecycle`, {
    data: { mutationId: crypto.randomUUID(), expectedRevision: 0, action: "Void", reason: "Unauthorized" },
  });
  assert.equal(unauthLifecycle.status(), 401);

  const unauthAttachment = await unauthRequest.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
    data: { mutationId: crypto.randomUUID(), expectedRevision: nmAttachment.revision, action: "Update", title: "HACK", reason: "Unauthorized" },
  });
  assert.equal(unauthAttachment.status(), 401);

  const unauthResolution = await unauthRequest.post(`${origin}api/documents/${uploaded.documentId}/award-link-resolutions`, {
    data: { mutationId: crypto.randomUUID(), action: "ShareSource", reason: "Unauthorized" },
  });
  assert.equal(unauthResolution.status(), 401);
  await unauthContext.close();

  // 13d(ii). Authenticated Permission-Limited Officer (403 Forbidden checks)
  // Provision a viewer-only role without edit/lifecycle/attachment/resolution authorities
  const limitedRoleCode = `LIMIT_${key.substring(0, 8).toUpperCase()}`;
  const limitedRole = await json(
    await request.post(`${origin}api/admin/roles`, {
      data: {
        code: limitedRoleCode,
        name: "Permission Limited Officer",
        description: "Authenticated officer with read-only view access but no mutation rights",
        permissions: [
          { permissionCode: "Award.View", scopeMode: 0 },
        ],
      },
    }),
    201
  );

  const limitedUsername = `limited-officer-${key.substring(0, 8)}`;
  const limitedPassword = "SyntheticLimited!791";
  await json(
    await request.post(`${origin}api/admin/users`, {
      data: {
        username: limitedUsername,
        displayName: "Limited Test Officer",
        password: limitedPassword,
        roleIds: [limitedRole.id],
      },
    }),
    201
  );

  const limitedContext = await browser.newContext();
  const limitedRequest = limitedContext.request;
  const limitedLoginRes = await limitedRequest.post(`${origin}api/auth/login`, {
    data: { username: limitedUsername, password: limitedPassword },
  });
  assert.equal(limitedLoginRes.status(), 200, "Limited officer must authenticate successfully");

  // 1. Unauthorized Award metadata edits -> 403 Forbidden
  const targetWorkflow = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  const limitedMetaRes = await limitedRequest.put(`${origin}api/awards/${target.id}/metadata`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: targetWorkflow.revision,
      awardNumber: targetNumber,
      reason: "Attempted metadata edit by limited officer",
    },
  });
  assert.equal(limitedMetaRes.status(), 403, "Limited officer metadata edit must return 403 Forbidden");
  const limitedMetaJson = await limitedMetaRes.json();
  assert.equal(limitedMetaJson.code, "Forbidden");

  // 2. Unauthorized Award lifecycle mutations -> 403 Forbidden
  const limitedLifeRes = await limitedRequest.post(`${origin}api/awards/${target.id}/lifecycle`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: targetWorkflow.revision,
      action: "Archive",
      reason: "Attempted lifecycle mutation by limited officer",
    },
  });
  assert.equal(limitedLifeRes.status(), 403, "Limited officer lifecycle mutation must return 403 Forbidden");
  const limitedLifeJson = await limitedLifeRes.json();
  assert.equal(limitedLifeJson.code, "Forbidden");

  // 3. Unauthorized attachment correction -> 403 Forbidden
  const limitedLinkRes = await limitedRequest.put(`${origin}api/awards/${target.id}/core-document-links/${nmAttachment.attachmentId}`, {
    data: {
      mutationId: crypto.randomUUID(),
      expectedRevision: nmAttachment.revision,
      action: "Update",
      title: "Attempted attachment correction by limited officer",
      reason: "Attempted attachment correction by limited officer",
    },
  });
  assert.equal(limitedLinkRes.status(), 403, "Limited officer attachment correction must return 403 Forbidden");
  const limitedLinkJson = await limitedLinkRes.json();
  assert.equal(limitedLinkJson.code, "Forbidden");

  // 4. Unauthorized officer ownership resolution -> 403 Forbidden
  const limitedResolRes = await limitedRequest.post(`${origin}api/documents/${uploaded.documentId}/award-link-resolutions`, {
    data: {
      mutationId: crypto.randomUUID(),
      action: "ShareSource",
      sourceAwardId: source.id,
      targetAwardId: target.id,
      villageId: villageId,
      expectedSourceAwardRevision: 1,
      expectedTargetAwardRevision: targetWorkflow.revision,
      expectedSourceAttachmentRevision: 1,
      expectedSha256Hash: crypto.createHash("sha256").update(bytes).digest("hex"),
      officerConfirmed: true,
      reason: "Attempted ownership resolution by limited officer",
      sourcePages: [1],
      coverageDescription: "Test coverage description",
    },
  });
  assert.equal(limitedResolRes.status(), 403, "Limited officer ownership resolution must return 403 Forbidden");
  const limitedResolJson = await limitedResolRes.json();
  assert.equal(limitedResolJson.code, "Forbidden");
  await limitedContext.close();

  // 13e. Mutation Idempotency (replay of exact same mutationId returns identical result without duplicate audit log)
  const idemMutationId = crypto.randomUUID();
  const idemReason = `Testing mutation idempotency replay ${key.substring(0, 8)}`;
  const currentAward = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  const auditsBefore = await json(await request.get(`${origin}api/audit-logs?entityType=Award&entityId=${target.id}&page=0&pageSize=100`));
  const countBefore = auditsBefore.items.filter((a) => a.action === "StaffAwardArchive" && a.newValues?.includes(idemReason)).length;
  assert.equal(countBefore, 0, "No audit entry for idempotency reason should exist prior to mutation");

  const idemPayload = {
    mutationId: idemMutationId,
    expectedRevision: currentAward.revision,
    action: "Archive",
    reason: idemReason,
  };
  const firstIdemRes = await json(await request.post(`${origin}api/awards/${target.id}/lifecycle`, { data: idemPayload }));
  const auditsAfterFirst = await json(await request.get(`${origin}api/audit-logs?entityType=Award&entityId=${target.id}&page=0&pageSize=100`));
  const countAfterFirst = auditsAfterFirst.items.filter((a) => a.action === "StaffAwardArchive" && a.newValues?.includes(idemReason)).length;
  assert.equal(countAfterFirst, 1, "Initial mutation must write exactly one audit log for this mutation");

  // Replay identical mutation with matching mutationId
  const secondIdemRes = await json(await request.post(`${origin}api/awards/${target.id}/lifecycle`, { data: idemPayload }));
  const auditsAfterReplay = await json(await request.get(`${origin}api/audit-logs?entityType=Award&entityId=${target.id}&page=0&pageSize=100`));
  const countAfterReplay = auditsAfterReplay.items.filter((a) => a.action === "StaffAwardArchive" && a.newValues?.includes(idemReason)).length;
  assert.equal(countAfterReplay, 1, "Replay of mutation must not create any duplicate audit log");
  assert.equal(auditsAfterReplay.items.length, auditsAfterFirst.items.length, "Total audit log items count must remain unchanged on replay");
  assert.equal(firstIdemRes.revision, secondIdemRes.revision, "Idempotent retry must return identical revision");
  assert.deepEqual(firstIdemRes, secondIdemRes, "Idempotent retry must return identical result object");

  // Restore award after idempotency test
  const postArchiveAward = await json(await request.get(`${origin}api/awards/${target.id}/staff-workflow`));
  await json(
    await request.post(`${origin}api/awards/${target.id}/lifecycle`, {
      data: {
        mutationId: crypto.randomUUID(),
        expectedRevision: postArchiveAward.revision,
        action: "Restore",
        reason: "Restoring after idempotency verification",
      },
    })
  );

  // 13f. Source PDF content retrieval by authoritative documentId and real SHA-256 equality
  const contentRes = await request.get(`${origin}api/documents/${uploaded.documentId}/content`);
  assert.equal(contentRes.status(), 200);
  const downloadedBytes = await contentRes.body();
  const downloadedSha256 = crypto.createHash("sha256").update(downloadedBytes).digest("hex");
  const originalSha256 = crypto.createHash("sha256").update(bytes).digest("hex");
  assert.equal(downloadedSha256, originalSha256, "Downloaded PDF bytes must match original source SHA-256 hash");
  assert.ok(downloadedBytes.equals(bytes), "Downloaded PDF bytes must match original source buffer exactly");

  assert.deepEqual(pageErrors, []);
  console.log("PASS: all Land Records V2 acceptance scenarios, lifecycle transitions, multi-document roles, attachment corrections, and negative-path checks verified.");
} finally {
  await browser.close();
  await server.close();
}
