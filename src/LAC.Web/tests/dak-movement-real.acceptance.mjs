// Production-bundle acceptance. Requires a freshly generated DakMovementFixtures
// database and its private JSON manifest; never use office/manual accounts.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { randomUUID } from "node:crypto";

const fixture = JSON.parse(fs.readFileSync(process.env.DAK_FIXTURE_PATH, "utf8"));
const backend = "http://127.0.0.1:5197";
assert.match(process.env.DAK_BROWSER_CONNECTION || "", /Port=55446;Database=lac_dak_ui_/);
const receiptDir = process.env.DAK_SCREENSHOTS; assert.ok(path.isAbsolute(receiptDir)); fs.mkdirSync(receiptDir, { recursive: true });
const fixtureTool = process.env.DAK_FIXTURE_TOOL; assert.ok(path.isAbsolute(fixtureTool));
const toggleDesks = (active) => execFileSync("dotnet", [fixtureTool, active ? "enable-desks" : "disable-desks"], { env: process.env, stdio: "pipe" });
const server = http.createServer((req, res) => {
  if (req.url.startsWith("/api/")) {
    const upstream = http.request(new URL(req.url, backend), { method: req.method, headers: req.headers }, (response) => { res.writeHead(response.statusCode, response.headers); response.pipe(res); });
    upstream.on("error", (e) => { res.writeHead(502); res.end(e.message); }); req.pipe(upstream); return;
  }
  const asset = path.join(process.cwd(), "dist", new URL(req.url, "http://test.invalid").pathname);
  const file = fs.existsSync(asset) && fs.statSync(asset).isFile() ? asset : path.join(process.cwd(), "dist/index.html");
  res.setHeader("Content-Type", file.endsWith(".js") ? "application/javascript" : file.endsWith(".css") ? "text/css" : "text/html"); res.end(fs.readFileSync(file));
});
await new Promise((resolve) => server.listen(5196, "127.0.0.1", resolve));
const origin = "http://127.0.0.1:5196";
const browser = await chromium.launch({ executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true });
const contexts = [], evidence = [], errors = [];
async function checked(response) { assert.ok(response.ok(), `HTTP ${response.status()}: ${await response.text()}`); return response.json(); }
async function actor(login) {
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, deviceScaleFactor: 1 }); contexts.push(context);
  context.setDefaultTimeout(15000);
  await checked(await context.request.post(origin + "/api/auth/login", { data: { username: login, password: fixture.Password } }));
  const page = await context.newPage(); page.on("pageerror", (e) => errors.push(e.message)); return { context, page };
}
async function capture(page, name) {
  const dimensions = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, scale: devicePixelRatio })); assert.deepEqual(dimensions, { width: 1366, height: 768, scale: 1 });
  const assets = await page.evaluate(() => Array.from(document.scripts).map((s) => s.src).filter((s) => s.includes("/assets/"))); assert.ok(assets.length > 0);
  await page.screenshot({ path: path.join(receiptDir, name + ".png"), fullPage: false }); evidence.push({ name, url: page.url(), dimensions, assets });
}
async function register(page) {
  const diary = "SYNTHETIC/UI/" + randomUUID().slice(0, 8);
  await page.goto(origin + "/dak/register");
  await page.getByPlaceholder("e.g. DAK/2026/00142").fill(diary);
  await page.locator('input[type="date"]').first().fill("2026-10-10");
  await page.getByPlaceholder("Sender Name / Entity").fill("Synthetic government department");
  await page.getByPlaceholder("Subject or title of incoming correspondence...").fill("Official correspondence for review and recorded movement");
  const response = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/api/dak"));
  await page.locator(".btn-open").click(); const receipt = await checked(await response); await page.waitForURL(`**/dak/${receipt.id}`);
  await page.getByRole("button", { name: /Mark to Officer/ }).waitFor(); return receipt;
}
async function send(page, id, desk, recipient, { paper = false, returned = false, screenshot, doubleClick = false } = {}) {
  await page.getByRole("button", { name: /Mark to Officer|Send \/ Forward/ }).click();
  const modal = page.getByRole("dialog"); await modal.waitFor();
  if (returned) await page.getByLabel("Movement Action", { exact: true }).selectOption("Returned");
  await page.getByLabel("Target Office Desk *", { exact: true }).selectOption(desk);
  await page.getByLabel("Select Officer / Recipient *", { exact: true }).selectOption(recipient);
  if (paper) await page.getByRole("checkbox", { name: /Physical file also being sent/ }).check();
  await page.getByPlaceholder("Specific actions requested or directives (e.g. 'Please examine and put up report')...").fill("Please examine and record the required action.");
  await page.getByPlaceholder("File noting or background remarks accompanying this movement...").fill("Synthetic official noting for immutable movement history.");
  const bounds = await modal.evaluate((el) => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, overflow: el.scrollWidth > el.clientWidth }; });
  assert.ok(bounds.top >= 0 && bounds.bottom <= 768); assert.equal(bounds.overflow, false);
  const footer = await page.locator(".dak-movement-footer").boundingBox(); await page.locator(".dak-movement-body").evaluate((el) => { el.scrollTop = el.scrollHeight; }); assert.deepEqual(await page.locator(".dak-movement-footer").boundingBox(), footer);
  if (screenshot) await capture(page, screenshot);
  const response = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith(`/api/dak/${id}/transfers`));
  const button = page.getByRole("button", { name: "Send / Mark Dak", exact: true });
  if (doubleClick) await button.evaluate((el) => { el.click(); el.click(); }); else await button.click();
  const result = await checked(await response); await modal.waitFor({ state: "hidden" }); return result;
}
async function receive(actor, id, paper, screenshot) {
  const { page } = actor; await page.goto(origin + `/dak/${id}`);
  await page.getByRole("button", { name: /Acknowledge.*Receipt|Receive Dak/ }).click();
  const modal = page.getByRole("dialog"); await modal.waitFor();
  const button = page.getByRole("button", { name: "Confirm Receipt", exact: true });
  if (paper) { assert.equal(await button.isDisabled(), true); await page.getByRole("checkbox", { name: /I confirm physical receipt/ }).check(); }
  else assert.equal(await modal.getByRole("checkbox").count(), 0);
  if (screenshot) await capture(page, screenshot);
  const response = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/receive")); await button.click();
  const result = await checked(await response); await modal.waitFor({ state: "hidden" }); return result;
}
async function pullBack(page, id, screenshot) {
  await page.getByRole("button", { name: /Pull Back Dak/ }).click(); await page.getByRole("dialog").waitFor();
  assert.equal(await page.getByRole("button", { name: "Confirm Pull Back", exact: true }).isDisabled(), true);
  await page.getByPlaceholder("State the reason why you are pulling back this unreceived dispatch...").fill("Required further examination before recipient acceptance.");
  if (screenshot) await capture(page, screenshot);
  const response = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/pull-back"));
  await page.getByRole("button", { name: "Confirm Pull Back", exact: true }).click(); const result = await checked(await response); await page.getByRole("dialog").waitFor({ state: "hidden" }); return result;
}
try {
  const sender = await actor(fixture.SenderLogin), receiver = await actor(fixture.ReceiverLogin), caretaker = await actor(fixture.CaretakerLogin);
  const receipt = await register(sender.page);
  await checked(await sender.context.request.put(origin + `/api/dak/${receipt.id}/physical-original`, { data: { hasPhysicalOriginal: true, deskId: fixture.SenderDesk, userId: fixture.Sender, locationNote: "Original retained at sender desk", provenanceNote: "Observed original; digital dispatch only", expectedRevision: receipt.revision } }));
  await sender.page.reload(); await sender.page.getByRole("button", { name: /Mark to Officer/ }).waitFor();
  let detail = await checked(await sender.context.request.get(origin + `/api/dak/${receipt.id}`)); assert.equal(detail.routingState, "Unassigned"); assert.equal(detail.currentAssignment, null);
  await capture(sender.page, "01-new-unmarked-dak");
  toggleDesks(false); await sender.page.getByRole("button", { name: /Mark to Officer/ }).click(); await sender.page.getByText("No active Office Desks configured", { exact: true }).waitFor(); await capture(sender.page, "02-empty-office-configuration"); await sender.page.keyboard.press("Escape"); toggleDesks(true);
  await sender.page.getByRole("button", { name: /Mark to Officer/ }).click(); await sender.page.getByLabel("Target Office Desk *", { exact: true }).selectOption(fixture.EmptyDesk);
  await sender.page.getByText(/No eligible recipient at Correspondence review desk/).waitFor(); await capture(sender.page, "03-desk-without-eligible-member"); await sender.page.keyboard.press("Escape");
  const marked = await send(sender.page, receipt.id, fixture.ReceiverDesk, fixture.Receiver, { screenshot: "04-initial-mark-confirmation", doubleClick: true }); assert.equal(marked.routingState, "InTransit"); assert.equal(marked.confirmedHolderUserId, null);
  const transfers = await checked(await sender.context.request.get(origin + `/api/dak/${receipt.id}/transfers`)); assert.equal(transfers.items.length, 1);
  const invalidTargets = await checked(await sender.context.request.get(origin + `/api/dak/${receipt.id}/movement-targets`)); assert.equal(invalidTargets.desks.flatMap((d) => d.members).some((m) => m.userId === fixture.UnauthorizedUser), false);
  const rejectedReceipt = await sender.context.request.post(origin + `/api/dak/${receipt.id}/transfers/${marked.transferId}/receive`, { headers: { "Idempotency-Key": randomUUID() }, data: { expectedRevision: marked.revision } }); assert.equal(rejectedReceipt.status(), 403);
  const accepted = await receive(receiver, receipt.id, false, "05-receive-digital-dak"); assert.equal(accepted.confirmedHolderUserId, fixture.Receiver); assert.equal(accepted.physicalState, "AtRecordedLocation"); assert.equal(accepted.physicalCustodianUserId, fixture.Sender);
  await receiver.page.getByRole("button", { name: "Movement Timeline", exact: true }).click(); await receiver.page.getByText(/Synthetic official noting for immutable movement history/).first().waitFor(); await capture(receiver.page, "06-received-holder-and-timeline");
  await receiver.page.getByRole("button", { name: "Overview & Details", exact: true }).click();
  const forward = await send(receiver.page, receipt.id, fixture.Room, fixture.Caretaker, { screenshot: "07-forward-to-record-room" }); assert.equal(forward.confirmedHolderUserId, fixture.Receiver);
  await receive(caretaker, receipt.id, false);
  await send(caretaker.page, receipt.id, fixture.ReceiverDesk, fixture.Receiver, { returned: true, screenshot: "08-return-to-officer" });
  await pullBack(caretaker.page, receipt.id, "09-pull-back-before-receipt");
  detail = await checked(await caretaker.context.request.get(origin + `/api/dak/${receipt.id}`)); assert.equal(detail.currentAssignment.assignedUserId, fixture.Caretaker); assert.equal(detail.physicalState, "AtRecordedLocation");
  const paper = await register(sender.page);
  await checked(await sender.context.request.put(origin + `/api/dak/${paper.id}/physical-original`, { data: { hasPhysicalOriginal: true, deskId: fixture.SenderDesk, userId: fixture.Sender, locationNote: "Synthetic sender desk", provenanceNote: "Observed original before dispatch", expectedRevision: paper.revision } }));
  await sender.page.reload(); await sender.page.getByRole("button", { name: /Mark to Officer/ }).waitFor();
  await send(sender.page, paper.id, fixture.ReceiverDesk, fixture.Receiver, { paper: true, screenshot: "10-initial-mark-with-physical-original" });
  const paperAccepted = await receive(receiver, paper.id, true, "11-physical-receipt-attestation"); assert.equal(paperAccepted.physicalCustodianUserId, fixture.Receiver);
  await send(receiver.page, paper.id, fixture.SenderDesk, fixture.Sender, { paper: true });
  const recalled = await pullBack(receiver.page, paper.id); assert.equal(recalled.physicalState, "ReturnPending"); assert.equal(recalled.confirmedHolderUserId, fixture.Receiver);
  await receiver.page.getByRole("button", { name: /Confirm Physical Return/ }).click(); await receiver.page.getByRole("dialog").waitFor();
  assert.equal(await receiver.page.getByRole("button", { name: "Confirm Return", exact: true }).isDisabled(), true);
  await receiver.page.getByPlaceholder("Record where and in what condition the physical paper file was recovered and stored...").fill("Actual paper recovered from the messenger and held at the receiving desk."); await capture(receiver.page, "12-confirm-actual-paper-recovery");
  const recoveryResponse = receiver.page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/confirm-return")); await receiver.page.getByRole("button", { name: "Confirm Return", exact: true }).click(); const recovered = await checked(await recoveryResponse); assert.equal(recovered.physicalState, "Held"); assert.equal(recovered.physicalCustodianUserId, fixture.Receiver);
  const events = await checked(await caretaker.context.request.get(origin + `/api/dak/${receipt.id}/timeline`)); assert.deepEqual(events.map((m) => m.action), ["Registered", "Marked", "Received", "Forwarded", "Received", "Returned", "PulledBack"]);
  assert.deepEqual(errors, []); fs.writeFileSync(path.join(receiptDir, "browser-evidence.json"), JSON.stringify({ passed: true, origin, events: events.map((m) => m.action), physicalReturn: recovered.physicalState, errors, evidence }, null, 2));
  console.log(JSON.stringify({ passed: true, screenshots: evidence.length, digitalWorkflow: "Mark -> Receive -> Record Room -> Receive -> Return -> Pull Back", physicalWorkflow: "Send paper -> confirmed receipt -> send -> pull back -> ReturnPending -> actual recovery", errors }, null, 2));
} finally { toggleDesks(true); await Promise.all(contexts.map((c) => c.close())); await browser.close(); await new Promise((r) => server.close(r)); }
