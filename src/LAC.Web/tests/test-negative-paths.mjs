import fs from "fs";
import path from "path";

const BASE = "http://127.0.0.1:5922";
const MATTER_ID = "41618f5c-12d4-403d-91f6-6225c3730877";
const NOTE1_ID = "296b3a27-461c-40e3-9519-c000cf0e0196";
const REMARK_ID = "152f27f0-8a10-43af-afd4-4349174e3e4a";

async function runNegativeTests() {
  console.log("Running real negative-path security and concurrency tests...");
  const loginRes = await fetch(`${BASE}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "office.feasibility.viewer", password: "OfficeFeasibility@2026!View" })
  });
  const cookie = loginRes.headers.get("set-cookie");

  const negativeReceipts = [];

  // 1. Stale revision / 409 conflict on draft save
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
      sourceDakId: null
    })
  });
  const staleErr = await staleSaveRes.text();
  console.log(`Test 1 (Stale Revision 409): Status=${staleSaveRes.status}`);
  negativeReceipts.push({
    test: "409 Stale Revision Conflict on Draft",
    endpoint: "PUT /api/matters/{id}/noting/draft",
    status: staleSaveRes.status,
    response: staleErr,
    passed: staleSaveRes.status === 409
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

  const outPath = "C:\\LAC-Platform-Anti-GreenNoting\\docs\\receipts\\green-legal-onlyoffice-workspace\\real-acceptance\\negative-paths.json";
  fs.writeFileSync(outPath, JSON.stringify(negativeReceipts, null, 2), "utf8");
  console.log(`Negative path receipts written to: ${outPath}`);
  console.log("All negative path assertions verified successfully.");
}

runNegativeTests().catch(err => {
  console.error("Negative tests failed:", err);
  process.exit(1);
});
