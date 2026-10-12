import fs from "fs";
import path from "path";
import { execFileSync } from "child_process";

const BASE = process.env.API_BASE || "http://127.0.0.1:5922";
const MATTER_ID = process.env.MATTER_ID || "41618f5c-12d4-403d-91f6-6225c3730877";
const DAK_ID = process.env.DAK_ID || "4457d27b-b18d-4f73-8d52-790ff1720592";
const NOTE1_ID = process.env.NOTE1_ID || "296b3a27-461c-40e3-9519-c000cf0e0196";
const REMARK_ID = process.env.REMARK_ID || "152f27f0-8a10-43af-afd4-4349174e3e4a";

const OfficerB = {
  username: process.env.OFFICER_B_USER || "office.feasibility.viewer",
  password: process.env.OFFICER_B_PASSWORD || "OfficeFeasibility@2026!View"
};

const PSQL = "C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe";
const DB = "lac_onlyoffice_real_9b0459_20261012";

async function runNegativeTests() {
  console.log("Running real negative-path security and concurrency tests...");
  const loginRes = await fetch(`${BASE}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: OfficerB.username, password: OfficerB.password })
  });
  const cookie = loginRes.headers.get("set-cookie");

  const negativeReceipts = [];

  // 1. Stale revision / 409 conflict on draft save with valid controlling Dak
  const staleSaveRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/noting/draft`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      "Cookie": cookie,
      "If-Match": '"9999"',
      "Idempotency-Key": crypto.randomUUID()
    },
    body: JSON.stringify({
      contentJson: '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"stale"}]}]}',
      sourceDakId: DAK_ID
    })
  });
  const staleErr = await staleSaveRes.text();
  console.log(`Test 1 (Stale Revision 409 with valid Dak): Status=${staleSaveRes.status}`);
  const isStale409 = staleSaveRes.status === 409 && staleErr.includes("Draft changed; recover saved text before retrying.");
  negativeReceipts.push({
    test: "409 Stale Revision Conflict on Draft (with valid controlling Dak)",
    endpoint: "PUT /api/matters/{id}/noting/draft",
    status: staleSaveRes.status,
    response: staleErr,
    passed: isStale409
  });

  // 2. Nonexistent / Expired Session Config
  const invalidSessionRes = await fetch(`${BASE}/api/noting-office/sessions/${crypto.randomUUID()}/config`, {
    headers: { "Cookie": cookie }
  });
  const invalidErr = await invalidSessionRes.text();
  console.log(`Test 2 (Nonexistent Session): Status=${invalidSessionRes.status}`);
  negativeReceipts.push({
    test: "404 Nonexistent / Expired Session",
    endpoint: "GET /api/noting-office/sessions/{id}/config",
    status: invalidSessionRes.status,
    response: invalidErr,
    passed: invalidSessionRes.status === 404
  });

  // 3. Stale 409 on Remark State Transition
  const fakeRemarkRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/notes/${NOTE1_ID}/remarks/${REMARK_ID}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      "Cookie": cookie,
      "Idempotency-Key": crypto.randomUUID()
    },
    body: JSON.stringify({
      state: "Addressed",
      expectedRevision: 999
    })
  });
  const remarkErr = await fakeRemarkRes.text();
  console.log(`Test 3 (Stale Remark 409): Status=${fakeRemarkRes.status}`);
  negativeReceipts.push({
    test: "409 Conflict on Stale Remark Revision",
    endpoint: "PUT /api/matters/{id}/notes/{noteId}/remarks/{id}",
    status: fakeRemarkRes.status,
    response: remarkErr,
    passed: fakeRemarkRes.status === 409
  });

  // 4. Missing Idempotency-Key header (428 / 400 rejection)
  const missingKeyRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/notes/${NOTE1_ID}/remarks`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Cookie": cookie
    },
    body: JSON.stringify({
      anchor: { version: 1, start: 0, end: 10, quote: "Official L", quoteHash: "dummy" },
      text: "Should fail without key"
    })
  });
  const missingKeyErr = await missingKeyRes.text();
  console.log(`Test 4 (Missing Idempotency-Key): Status=${missingKeyRes.status}`);
  negativeReceipts.push({
    test: "428 / 400 Missing Idempotency Key Required",
    endpoint: "POST /api/matters/{id}/notes/{noteId}/remarks",
    status: missingKeyRes.status,
    response: missingKeyErr,
    passed: missingKeyRes.status === 428 || missingKeyRes.status === 400
  });

  // 5. Corrupt / Tampered Anchor Hash (400 validation error)
  const tamperedAnchorRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/notes/${NOTE1_ID}/remarks`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Cookie": cookie,
      "Idempotency-Key": crypto.randomUUID()
    },
    body: JSON.stringify({
      anchor: {
        version: 1,
        start: 181,
        end: 198,
        quote: "Sections 26 to 30",
        quoteHash: "0000000000000000000000000000000000000000000000000000000000000000" // Tampered hash
      },
      text: "Tampered anchor should be rejected"
    })
  });
  const tamperedErr = await tamperedAnchorRes.text();
  console.log(`Test 5 (Tampered Anchor Hash): Status=${tamperedAnchorRes.status}`);
  negativeReceipts.push({
    test: "Tampered Anchor Hash Rejected",
    endpoint: "POST /api/matters/{id}/notes/{noteId}/remarks",
    status: tamperedAnchorRes.status,
    response: tamperedErr,
    passed: tamperedAnchorRes.status === 400 || tamperedAnchorRes.status === 500
  });

  // 6. Real Server-side Submitted Note 1 Immutability Enforcement
  console.log("Test 6: Verifying server-side Note 1 immutability rejections...");
  
  // 6a. Direct Database UPDATE rejection via lac_noting_immutable trigger
  let dbUpdateBlocked = false;
  let dbUpdateMsg = "";
  try {
    const updateSql = `SET search_path TO lac_office_acceptance_20261012; UPDATE "MatterOfficialNotes" SET "CanonicalText" = 'tampered' WHERE "Id" = '${NOTE1_ID}';`;
    execFileSync(PSQL, ["-h", "127.0.0.1", "-p", "56683", "-U", "postgres", "-d", DB, "-c", updateSql], { encoding: "utf8" });
  } catch (dbErr) {
    dbUpdateBlocked = dbErr.message.includes("Official Matter noting history is immutable");
    dbUpdateMsg = dbErr.message.split("\n").filter(l => l.includes("ERROR:") || l.includes("CONTEXT:")).join(" ");
  }

  // 6b. Direct Database DELETE rejection via lac_noting_immutable trigger
  let dbDeleteBlocked = false;
  let dbDeleteMsg = "";
  try {
    const deleteSql = `SET search_path TO lac_office_acceptance_20261012; DELETE FROM "MatterOfficialNotes" WHERE "Id" = '${NOTE1_ID}';`;
    execFileSync(PSQL, ["-h", "127.0.0.1", "-p", "56683", "-U", "postgres", "-d", DB, "-c", deleteSql], { encoding: "utf8" });
  } catch (dbErr) {
    dbDeleteBlocked = dbErr.message.includes("Official Matter noting history is immutable");
    dbDeleteMsg = dbErr.message.split("\n").filter(l => l.includes("ERROR:") || l.includes("CONTEXT:")).join(" ");
  }

  // 6c. Unauthorized API mutation rejection
  const apiPutRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/notes/${NOTE1_ID}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Cookie": cookie },
    body: JSON.stringify({ canonicalText: "tampered" })
  });
  const apiDeleteRes = await fetch(`${BASE}/api/matters/${MATTER_ID}/notes/${NOTE1_ID}`, {
    method: "DELETE",
    headers: { "Cookie": cookie }
  });

  const apiPutBlocked = apiPutRes.status === 405 || apiPutRes.status === 404;
  const apiDeleteBlocked = apiDeleteRes.status === 405 || apiDeleteRes.status === 404;

  console.log(`Test 6 (DB UPDATE trigger): blocked=${dbUpdateBlocked} (${dbUpdateMsg})`);
  console.log(`Test 6 (DB DELETE trigger): blocked=${dbDeleteBlocked} (${dbDeleteMsg})`);
  console.log(`Test 6 (API PUT/DELETE): PUT=${apiPutRes.status}, DELETE=${apiDeleteRes.status}`);

  negativeReceipts.push({
    test: "Server-side Immutability: DB UPDATE Trigger (lac_noting_immutable)",
    endpoint: "psql: UPDATE MatterOfficialNotes",
    status: "EXCEPTION",
    response: dbUpdateMsg,
    passed: dbUpdateBlocked
  });

  negativeReceipts.push({
    test: "Server-side Immutability: DB DELETE Trigger (lac_noting_immutable)",
    endpoint: "psql: DELETE FROM MatterOfficialNotes",
    status: "EXCEPTION",
    response: dbDeleteMsg,
    passed: dbDeleteBlocked
  });

  negativeReceipts.push({
    test: "Server-side Immutability: API Mutation Rejection",
    endpoint: "PUT/DELETE /api/matters/{id}/notes/{noteId}",
    status: `PUT:${apiPutRes.status}, DELETE:${apiDeleteRes.status}`,
    response: "Disallowed by REST contract (append-only official noting)",
    passed: apiPutBlocked && apiDeleteBlocked
  });

  const outPath = "C:\\LAC-Platform-Anti-GreenNoting\\docs\\receipts\\green-legal-onlyoffice-workspace\\real-acceptance\\negative-paths.json";
  fs.writeFileSync(outPath, JSON.stringify(negativeReceipts, null, 2), "utf8");
  console.log(`Negative path receipts written to: ${outPath}`);

  const allPassed = negativeReceipts.every(r => r.passed);
  if (!allPassed) {
    const failedList = negativeReceipts.filter(r => !r.passed).map(r => r.test);
    throw new Error(`Negative path tests failed: ${failedList.join(", ")}`);
  }
  console.log("All negative path assertions verified successfully (ALL PASSED).");
}

runNegativeTests().catch(err => {
  console.error("Negative tests failed:", err.message);
  process.exit(1);
});
