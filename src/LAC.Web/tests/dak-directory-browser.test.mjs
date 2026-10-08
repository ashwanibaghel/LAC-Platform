import test from "node:test";
import assert from "node:assert/strict";
import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

test("LAN HTTP intake fallback, rapid entry, retries and directory server-query controls", async () => {
  const server = http.createServer((req, res) => {
    const url = new URL(req.url, "http://dak.test");
    const asset = path.join(process.cwd(), "dist", url.pathname);
    const file = fs.existsSync(asset) && fs.statSync(asset).isFile() ? asset : path.join(process.cwd(), "dist/index.html");
    res.setHeader("Content-Type", file.endsWith(".js") ? "application/javascript" : file.endsWith(".css") ? "text/css" : "text/html");
    res.end(fs.readFileSync(file));
  });
  await new Promise((resolve) => server.listen(0, "0.0.0.0", resolve));
  const browser = await chromium.launch({ executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true, args: ["--no-proxy-server", "--host-resolver-rules=MAP 10.0.0.99 127.0.0.1"] });
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const page = await context.newPage();
  const errors = []; page.on("pageerror", (e) => errors.push(e.message));
  const base = `http://10.0.0.99:${server.address().port}`;
  const json = (route, body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
  const permissions = ["Dak.View", "Dak.Register"];
  const rows = [], keys = [], queries = [], buckets = [];
  let fail = false, delay = false;
  try {
    await page.route("**/api/auth/me", (route) => json(route, { id: "officer", username: "officer", displayName: "Test officer", roles: [], permissions: permissions.map((code) => ({ code, scope: "All" })), workstreams: [], desks: [] }));
    await page.route("**/api/dak/lookups/*", (route) => json(route, { desks: [{ id: "desk", name: "Test desk", code: "D" }], handlers: [{ id: "handler", displayName: "Test holder" }], categories: [{ id: "category", name: "Test category", code: "C" }], workstreams: [{ id: "stream", name: "Test workstream", code: "W" }] }));
    await page.route("**/api/dak/delivery-queue?*", (route) => { buckets.push(new URL(route.request().url()).searchParams.get("bucket")); return json(route, { items: [], totalCount: 0 }); });
    await page.route(/\/api\/dak(?:\?.*)?$/, async (route) => {
      if (route.request().method() === "POST") {
        keys.push(route.request().headers()["idempotency-key"]);
        if (delay) await new Promise((r) => setTimeout(r, 200));
        if (fail) return json(route, { detail: "Temporary registration error" }, 500);
        const diary = route.request().postData().match(/name="diaryNumber"\r\n\r\n([^\r]+)/)[1];
        const row = { id: `record-${rows.length + 1}`, diaryNumber: diary, receivedDate: "2026-10-08", senderName: "Department", subject: "Office receipt", inwardMode: "Physical / By Hand", priority: "Routine", status: "Registered", createdAt: new Date().toISOString(), hasDocument: false };
        rows.push(row); return json(route, row, 201);
      }
      queries.push(new URL(route.request().url()).searchParams);
      return json(route, { items: rows, totalCount: rows.length, page: 0, pageSize: 25 });
    });
    await page.goto(base + "/dak/register");
    assert.deepEqual(await page.evaluate(() => ({ secure: isSecureContext, uuid: typeof crypto.randomUUID, values: typeof crypto.getRandomValues })), { secure: false, uuid: "undefined", values: "function" });
    await page.getByRole("heading", { name: "New Dak Entry" }).waitFor();
    const fill = async (diary) => {
      await page.getByPlaceholder("e.g. DAK/2026/00142").fill(diary);
      await page.getByPlaceholder("Sender Name / Entity").fill("Department");
      await page.getByPlaceholder("Subject or title of incoming correspondence...").fill("Office receipt");
    };
    await fill("LAN-001"); delay = true;
    await page.locator(".btn-next").evaluate((button) => { button.click(); button.click(); });
    await page.getByRole("status").waitFor(); assert.equal(keys.length, 1);
    assert.equal(await page.getByPlaceholder("e.g. DAK/2026/00142").inputValue(), "");
    await fill("LAN-002"); fail = true;
    await page.locator(".btn-next").click(); await page.getByRole("alert").waitFor();
    const failedKey = keys.at(-1); fail = false;
    await page.locator(".btn-next").click(); await page.getByRole("status").waitFor();
    assert.equal(keys.at(-1), failedKey); assert.notEqual(keys[0], failedKey);
    for (const key of keys) assert.match(key, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    await page.getByRole("link", { name: "Back to Dak Register" }).click();
    await page.getByRole("heading", { name: "Inward Dak Register" }).waitFor();
    assert.ok(await page.locator(".search-form").evaluate((element) => element.getBoundingClientRect().height <= 48), "Register search row must remain compact at 1366x768");
    await page.getByRole("link", { name: "LAN-001", exact: true }).waitFor();
    await page.getByRole("button", { name: "+ New Dak Entry", exact: true }).waitFor();
    const expectQuery = async (key, value, action) => {
      const before = queries.length; await action();
      await page.waitForFunction(() => !document.querySelector(".state")?.textContent?.includes("Loading"));
      for (let i = 0; i < 50 && queries.length === before; i++) await new Promise((r) => setTimeout(r, 20));
      assert.equal(queries.at(-1).get(key), value); assert.equal(queries.at(-1).get("page"), "0");
    };
    await expectQuery("q", "LAN", async () => { await page.getByPlaceholder("Search by diary no, subject, sender, ref...").fill("LAN"); await page.getByRole("button", { name: "Search", exact: true }).click(); });
    await expectQuery("status", "Registered", () => page.getByLabel("Status:", { exact: true }).selectOption("Registered"));
    await expectQuery("priority", "Routine", () => page.getByLabel("Priority:", { exact: true }).selectOption("Routine"));
    await expectQuery("deskId", "desk", () => page.getByLabel("Current Desk:", { exact: true }).selectOption("desk"));
    await expectQuery("receivedFrom", "2026-10-01", () => page.getByLabel("Received From:").fill("2026-10-01"));
    await expectQuery("receivedTo", "2026-10-08", () => page.getByLabel("Received To:").fill("2026-10-08"));
    await page.getByRole("button", { name: "More filters" }).click();
    for (const [label, key, value] of [["Inward Mode:", "inwardMode", "Email"], ["Category:", "categoryId", "category"], ["Workstream:", "workstreamId", "stream"], ["Current Handler / Officer:", "handlerId", "handler"], ["Has document:", "hasDocument", "true"]])
      await expectQuery(key, value, () => page.getByLabel(label, { exact: true }).selectOption(value));
    await expectQuery("sender", "Department", () => page.getByLabel("Sender / Department:", { exact: true }).fill("Department"));
    assert.equal(queries.at(-1).get("categoryId"), "category"); assert.equal(queries.at(-1).get("receivedFrom"), "2026-10-01");
    for (const [label, bucket] of [["Incoming Dispatches", "incoming"], ["Sent / In-Transit", "sent"], ["With Me", "with-me"], ["Resolved", "resolved"], ["Attention Needed", "attention"]]) {
      await page.getByRole("button", { name: label, exact: false }).click();
      for (let i = 0; i < 50 && buckets.at(-1) !== bucket; i++) await new Promise((r) => setTimeout(r, 20));
      assert.equal(buckets.at(-1), bucket);
    }
    await page.getByRole("button", { name: "All Register", exact: true }).click();
    await page.locator("tbody tr").first().click(); await page.waitForURL("**/dak/record-1");
    await page.goto(base + "/dak/register"); await fill("LAN-003");
    await page.locator(".btn-open").click(); await page.waitForURL("**/dak/record-3");
    assert.deepEqual(errors, []);
  } finally { await context.close(); await browser.close(); await new Promise((resolve) => server.close(resolve)); }
});
