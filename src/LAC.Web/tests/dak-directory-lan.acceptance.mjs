// Run against a disposable local API/database only:
// DAK_LAN_HOST, DAK_ACCEPTANCE_API, DAK_ACCEPTANCE_USER, DAK_ACCEPTANCE_PASSWORD, DAK_SCREENSHOTS.
import { chromium } from "playwright";
import assert from "node:assert/strict";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";

const host = process.env.DAK_LAN_HOST;
const backend = new URL(process.env.DAK_ACCEPTANCE_API);
assert.equal(backend.hostname, "127.0.0.1", "Acceptance API must be disposable and local");
assert.ok(host && host !== "localhost" && host !== "127.0.0.1");
const screenshots = process.env.DAK_SCREENSHOTS; assert.ok(path.isAbsolute(screenshots)); fs.mkdirSync(screenshots, { recursive: true });
const server = http.createServer((req, res) => {
  if (req.url.startsWith("/api/")) {
    const proxy = http.request(new URL(req.url, backend), { method: req.method, headers: req.headers }, (upstream) => { res.writeHead(upstream.statusCode, upstream.headers); upstream.pipe(res); });
    proxy.on("error", (e) => { res.writeHead(502); res.end(e.message); }); req.pipe(proxy); return;
  }
  const asset = path.join(process.cwd(), "dist", new URL(req.url, "http://dak.test").pathname);
  const file = fs.existsSync(asset) && fs.statSync(asset).isFile() ? asset : path.join(process.cwd(), "dist/index.html");
  res.setHeader("Content-Type", file.endsWith(".js") ? "application/javascript" : file.endsWith(".css") ? "text/css" : "text/html"); res.end(fs.readFileSync(file));
});
await new Promise((r) => server.listen(0, host, r));
const base = `http://${host}:${server.address().port}`;
const browser = await chromium.launch({ executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true, args: ["--no-proxy-server"] });
const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
const page = await context.newPage(); const errors = []; page.on("pageerror", (e) => errors.push(e.message));
const calls = [];
page.on("response", async (response) => {
  if (response.request().method() === "GET" && /\/api\/dak\?/.test(response.url())) calls.push({ url: new URL(response.url()), data: await response.json() });
});
const prefix = "LAN-" + randomUUID().slice(0, 8);
try {
  const checked = async (response) => { assert.ok(response.ok(), `${response.status()} ${await response.text()}`); return response.json(); };
  const login = await context.request.post(base + "/api/auth/login", { data: { username: process.env.DAK_ACCEPTANCE_USER, password: process.env.DAK_ACCEPTANCE_PASSWORD } }); await checked(login);
  const me = await checked(await context.request.get(base + "/api/auth/me"));
  const lookups = await checked(await context.request.get(base + "/api/dak/lookups/directory"));
  const stream = lookups.workstreams[0]; assert.ok(stream);
  const category = await checked(await context.request.post(base + "/api/admin/dak-categories", { data: { code: prefix, name: "LAN acceptance category", defaultPriority: "Routine", defaultWorkstreamId: stream.id } }));
  const desk = await checked(await context.request.post(base + "/api/admin/desks", { data: { code: prefix, name: "LAN acceptance desk", description: "Disposable test", workstreamId: stream.id } }));
  await checked(await context.request.post(base + `/api/admin/users/${me.id}/desks`, { data: { officeDeskId: desk.id, isPrimary: true } }));
  await page.goto(base + "/dak/register"); await page.getByRole("heading", { name: "New Dak Entry" }).waitFor();
  const crypto = await page.evaluate(() => ({ secure: isSecureContext, uuid: typeof window.crypto.randomUUID, entropy: typeof window.crypto.getRandomValues }));
  assert.deepEqual(crypto, { secure: false, uuid: "undefined", entropy: "function" });
  await page.screenshot({ path: path.join(screenshots, "01-new-dak-entry-http-1366x768.png"), fullPage: false });
  const fill = async (diary) => {
    await page.getByPlaceholder("e.g. DAK/2026/00142").fill(diary);
    await page.locator('input[type="date"]').first().fill("2026-10-08");
    await page.getByPlaceholder("Sender Name / Entity").fill("LAN Office Department");
    await page.getByPlaceholder("Subject or title of incoming correspondence...").fill("LAN acceptance receipt");
  };
  await fill(prefix + "-NEXT");
  const nextResponse = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/api/dak"));
  await page.locator(".btn-next").click(); const next = await checked(await nextResponse);
  await page.getByRole("status").waitFor(); assert.equal(await page.getByPlaceholder("e.g. DAK/2026/00142").inputValue(), "");
  await fill(prefix + "-OPEN");
  const openResponse = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/api/dak"));
  await page.locator(".btn-open").click(); const opened = await checked(await openResponse); await page.waitForURL(`**/dak/${opened.id}`);
  await page.getByText(prefix + "-OPEN", { exact: false }).first().waitFor();
  await page.screenshot({ path: path.join(screenshots, "02-created-workspace-http-1366x768.png"), fullPage: false });
  // Canonical DB IDs; dispatch/receipt use existing custody endpoints.
  const send = await checked(await context.request.post(base + `/api/dak/${opened.id}/transfers`, { headers: { "Idempotency-Key": randomUUID() }, data: { action: "Marked", toDeskId: desk.id, toUserId: me.id, destinationKind: "Officer", includesPhysicalOriginal: false, expectedRevision: opened.revision, remarks: "Acceptance marking" } }));
  await checked(await context.request.post(base + `/api/dak/${opened.id}/transfers/${send.transferId}/receive`, { headers: { "Idempotency-Key": randomUUID() }, data: { expectedRevision: send.revision } }));
  for (let i = 0; i < 29; i++) await checked(await context.request.post(base + "/api/dak", { headers: { "Idempotency-Key": randomUUID() }, multipart: { diaryNumber: `${prefix}-${i}`, receivedDate: "2026-10-08", subject: "LAN acceptance receipt", senderName: "LAN Office Department", senderDepartment: "LAN department", senderReferenceNumber: "LAN-REF", inwardMode: "Email", priority: "Urgent", categoryId: category.id, workstreamId: stream.id } }));
  const query = async (expected, action) => {
    const count = calls.length; await action();
    for (let i = 0; i < 150 && calls.length === count; i++) await new Promise((r) => setTimeout(r, 20));
    assert.ok(calls.length > count); assert.equal(calls.at(-1).data.totalCount, expected);
    await page.getByRole("button", { name: "More filters", exact: true }).waitFor();
    await page.waitForFunction(() => !document.querySelector(".state")?.textContent?.includes("Loading"));
    return calls.at(-1);
  };
  await page.goto(base + "/dak"); await page.getByRole("heading", { name: "Inward Dak Register" }).waitFor();
  await query(31, async () => { await page.getByPlaceholder("Search by diary no, subject, sender, ref...").fill(prefix); await page.getByRole("button", { name: "Search", exact: true }).click(); });
  await page.screenshot({ path: path.join(screenshots, "03-inward-register-http-1366x768.png"), fullPage: false });
  const secondPage = await query(31, () => page.getByRole("button", { name: "Next", exact: true }).click()); assert.equal(secondPage.data.items.length, 6);
  await page.getByRole("button", { name: "More filters", exact: true }).click();
  const reset = async () => { await query(31, () => page.getByRole("button", { name: "Reset filters", exact: true }).click()); await query(31, async () => { await page.getByPlaceholder("Search by diary no, subject, sender, ref...").fill(prefix); await page.getByRole("button", { name: "Search", exact: true }).click(); }); };
  for (const [label, value, expected] of [["Status:", "Registered", 30], ["Priority:", "Urgent", 29], ["Current Desk:", desk.id, 1], ["Inward Mode:", "Email", 29], ["Category:", category.id, 29], ["Workstream:", stream.id, 29], ["Current Handler / Officer:", me.id, 1], ["Has document:", "false", 31], ["Has document:", "true", 0]]) {
    await reset(); await query(expected, () => page.getByLabel(label, { exact: true }).selectOption(value));
  }
  await reset(); await query(31, () => page.getByLabel("Received From:").fill("2026-10-01")); await query(31, () => page.getByLabel("Received To:").fill("2026-10-08"));
  await query(31, () => page.getByLabel("Sender / Department:", { exact: true }).fill("LAN Office"));
  await query(29, () => page.getByLabel("Inward Mode:", { exact: true }).selectOption("Email"));
  await query(29, () => page.getByLabel("Category:", { exact: true }).selectOption(category.id));
  await query(29, () => page.getByLabel("Workstream:", { exact: true }).selectOption(stream.id));
  await query(29, () => page.getByLabel("Priority:", { exact: true }).selectOption("Urgent"));
  await page.screenshot({ path: path.join(screenshots, "04-combined-filters-http-1366x768.png"), fullPage: false });
  for (const name of ["Incoming Dispatches", "Sent / In-Transit", "With Me", "Resolved", "Attention Needed"]) {
    const response = page.waitForResponse((r) => r.url().includes("/api/dak/delivery-queue?")); await page.getByRole("button", { name, exact: false }).click(); assert.equal((await response).status(), 200);
  }
  await page.getByRole("button", { name: "All Register", exact: true }).click(); await page.locator("tbody tr").first().waitFor(); await page.locator("tbody tr").first().click(); await page.waitForURL(/\/dak\/[0-9a-f-]{36}$/);
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ origin: base, viewport: "1366x768", crypto, created: [next.id, opened.id], filterChecks: "all individual + combined", pagination: "31 records / 25 + 6", queues: "all five HTTP 200", custody: "Mark -> InTransit -> Receive exercised", errors, screenshots }, null, 2));
} finally { await context.close(); await browser.close(); await new Promise((r) => server.close(r)); }
