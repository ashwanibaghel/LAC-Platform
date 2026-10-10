import test from "node:test";
import assert from "node:assert/strict";
import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

test.describe("Premium Dak movement production-bundle states", () => {
  let browser, server, origin;
  test.before(async () => {
    server = http.createServer((req, res) => {
      const asset = path.join(process.cwd(), "dist", new URL(req.url, "http://test.invalid").pathname);
      const file = fs.existsSync(asset) && fs.statSync(asset).isFile() ? asset : path.join(process.cwd(), "dist/index.html");
      res.setHeader("Content-Type", file.endsWith(".js") ? "application/javascript" : file.endsWith(".css") ? "text/css" : "text/html"); res.end(fs.readFileSync(file));
    });
    await new Promise((r) => server.listen(0, "127.0.0.1", r)); origin = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true });
  });
  test.after(async () => { await browser?.close(); await new Promise((r) => server.close(r)); });
  const desk = { id: "desk", code: "REVIEW", name: "Review Office", isActive: true, purpose: "General", members: [{ userId: "receiver", displayName: "Eligible Recipient", isPrimary: true }] };
  const room = { id: "room", code: "ROOM", name: "Record Room", isActive: true, purpose: "RecordRoom", members: [{ userId: "caretaker", displayName: "Caretaker" }] };
  const empty = { id: "empty", code: "EMPTY", name: "Empty Office", isActive: true, members: [] };
  async function setup(targetHandler, { paper = false, mode = "initial" } = {}) {
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 } }); const page = await context.newPage(); const errors = [];
    page.on("pageerror", (e) => errors.push(e.message));
    const json = (route, data, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(data) });
    const dak = { id: "receipt", diaryNumber: "DAK/2026/PREMIUM", receivedDate: "2026-10-10", subject: "Official correspondence for administrative examination", senderName: "Synthetic Department", senderDepartment: "Correspondence Branch", inwardMode: "Physical", priority: "Routine", status: mode === "initial" ? "Registered" : "InProcess", recordStatus: "Active", routingState: mode === "initial" ? "Unassigned" : "WithHolder", physicalState: paper ? "Held" : "Unknown", revision: 1, processingCycle: 1, attachments: [], villageLinks: [], awardLinks: [], matterLinks: [], khasraLinks: [], needsAttention: false, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), currentAssignment: mode === "initial" ? null : { id: "assignment", officeDeskId: "sender-desk", deskName: "Sender desk", deskCode: "S", assignedUserId: "sender", assignedUserDisplayName: "Sender", isActive: true, isDeskActive: true, isUserEligible: true, isConfirmed: true, receivedAt: new Date().toISOString() } };
    await page.route("**/api/auth/me", (route) => json(route, { id: "sender", username: "sender", displayName: "Sender", roles: [], permissions: ["Dak.View", "Dak.Mark", "Dak.Move", "Dak.PullBack", "Dak.Receive"].map((code) => ({ code, scope: "All" })), desks: [{ id: "sender-desk", name: "Sender desk", code: "S" }], workstreams: [] }));
    await page.route("**/api/dak/receipt", (route) => json(route, dak));
    await page.route("**/api/dak/receipt/movements", (route) => json(route, []));
    await page.route("**/api/dak/receipt/transfers", (route) => json(route, []));
    await page.route("**/api/dak/receipt/physical-original", (route) => json(route, { hasPhysicalOriginal: paper ? true : null, userId: paper ? "sender" : null, deskId: paper ? "sender-desk" : null, revision: 1 }));
    await page.route("**/api/dak/lookups/directory", (route) => json(route, { desks: [] }));
    await page.route("**/api/dak/receipt/movement-targets", (route) => targetHandler(route, json));
    await page.route("**/api/outward?*", (route) => json(route, { items: [] }));
    await page.goto(origin + "/dak/receipt"); await page.getByRole("button", { name: mode === "initial" ? /Mark to Officer/ : /Send \/ Forward/ }).click();
    await page.getByRole("dialog").waitFor(); return { context, page, errors, json };
  }

  test("loading is explained and no send is possible before destinations arrive", async () => {
    let release; const gate = new Promise((r) => { release = r; });
    const t = await setup(async (route, json) => { await gate; return json(route, { desks: [desk] }); });
    try { await t.page.getByText("Loading configured desks and eligible officers…", { exact: true }).waitFor(); assert.equal(await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).isDisabled(), true); release(); await t.page.getByLabel("Target Office Desk *", { exact: true }).selectOption("desk"); assert.equal(await t.page.getByLabel("Select Officer / Recipient *", { exact: true }).inputValue(), ""); }
    finally { release(); await t.context.close(); }
  });
  test("zero desks explains office configuration rather than showing blank dropdowns", async () => {
    const t = await setup((route, json) => json(route, { desks: [] }));
    try { await t.page.getByText("No active Office Desks configured", { exact: true }).waitFor(); assert.match(await t.page.getByLabel("Target Office Desk *", { exact: true }).textContent(), /No active desks/); assert.equal(await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).isDisabled(), true); assert.deepEqual(t.errors, []); } finally { await t.context.close(); }
  });
  test("empty membership and search misses have explicit blocked states", async () => {
    const t = await setup((route, json) => json(route, { desks: [desk, empty] }));
    try { await t.page.getByLabel("Target Office Desk *", { exact: true }).selectOption("empty"); await t.page.getByText("No eligible recipient at Empty Office", { exact: true }).waitFor(); assert.equal(await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).isDisabled(), true); await t.page.getByPlaceholder("Search by desk name or code").fill("nonexistent"); await t.page.getByText("No desk matches your search. Clear or change the search.", { exact: true }).waitFor(); assert.equal(await t.page.getByLabel("Select Officer / Recipient *", { exact: true }).inputValue(), ""); } finally { await t.context.close(); }
  });
  test("403 is distinct from a missing desk configuration", async () => {
    const t = await setup((route, json) => json(route, { message: "Forbidden" }, 403));
    try { await t.page.getByText("Movement access unavailable", { exact: true }).waitFor(); await t.page.getByText(/Monitoring access does not grant custody/).waitFor(); assert.equal(await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).isDisabled(), true); assert.equal(await t.page.getByText("No active Office Desks configured", { exact: true }).count(), 0); } finally { await t.context.close(); }
  });
  test("API errors are retryable and retain closed-by-default recipient selection", async () => {
    let fail = true; const t = await setup((route, json) => json(route, fail ? { message: "Unavailable" } : { desks: [desk] }, fail ? 503 : 200));
    try { await t.page.getByRole("button", { name: "Retry destinations" }).waitFor(); fail = false; await t.page.getByRole("button", { name: "Retry destinations" }).click(); await t.page.getByLabel("Target Office Desk *", { exact: true }).selectOption("desk"); assert.equal(await t.page.getByLabel("Select Officer / Recipient *", { exact: true }).inputValue(), ""); } finally { await t.context.close(); }
  });
  test("Record Room choice, keyboard focus, fixed footer and safe idempotent double-click/retry", async () => {
    const t = await setup((route, json) => json(route, { desks: [desk, room] }), { paper: true }); const requests = []; let fail = true;
    try {
      await t.page.route("**/api/dak/receipt/transfers", async (route) => {
        if (route.request().method() !== "POST") return t.json(route, []);
        requests.push({ key: route.request().headers()["idempotency-key"], body: route.request().postDataJSON() }); await new Promise((r) => setTimeout(r, 150));
        return t.json(route, fail ? { detail: "Temporary send error" } : { revision: 2 }, fail ? 503 : 200);
      });
      await t.page.getByPlaceholder("Search by desk name or code").fill("ROOM");
      await t.page.getByLabel("Target Office Desk *", { exact: true }).selectOption("room");
      await t.page.getByLabel("Select Officer / Recipient *", { exact: true }).selectOption("caretaker");
      await t.page.getByRole("checkbox", { name: /Physical file also being sent/ }).check();
      assert.match(await t.page.locator(".dak-movement-confirmation").textContent(), /Caretaker.*Record Room/);
      const bounds = await t.page.locator(".dak-movement-dialog").evaluate((el) => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, overflow: el.scrollWidth > el.clientWidth }; });
      assert.ok(bounds.top >= 0 && bounds.bottom <= 768); assert.equal(bounds.overflow, false);
      const footerBefore = await t.page.locator(".dak-movement-footer").boundingBox(); await t.page.locator(".dak-movement-body").evaluate((el) => { el.scrollTop = el.scrollHeight; });
      assert.deepEqual(await t.page.locator(".dak-movement-footer").boundingBox(), footerBefore);
      await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).evaluate((el) => { el.click(); el.click(); });
      await t.page.getByRole("alert").filter({ hasText: "Temporary send error" }).waitFor(); assert.equal(requests.length, 1);
      fail = false; await t.page.getByRole("button", { name: "Send / Mark Dak", exact: true }).click(); await t.page.getByRole("dialog").waitFor({ state: "hidden" });
      assert.equal(requests.length, 2); assert.equal(requests[0].key, requests[1].key); assert.equal(requests[0].body.destinationKind, "RecordRoom"); assert.equal(requests[0].body.includesPhysicalOriginal, true);
      assert.match(requests[0].key, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      assert.deepEqual(t.errors, []);
    } finally { await t.context.close(); }
  });
  test("Escape restores focus and Tab cannot leave the modal", async () => {
    const t = await setup((route, json) => json(route, { desks: [desk] }));
    try { await t.page.getByRole("button", { name: "Cancel", exact: true }).focus(); await t.page.keyboard.press("Tab"); assert.equal(await t.page.getByRole("button", { name: "Close movement dialog" }).evaluate((el) => document.activeElement === el), true); await t.page.keyboard.press("Escape"); await t.page.getByRole("dialog").waitFor({ state: "hidden" }); assert.match(await t.page.evaluate(() => document.activeElement.textContent), /Mark to Officer/); } finally { await t.context.close(); }
  });
});
