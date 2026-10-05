import assert from "node:assert/strict";

const baseUrl = process.env.LAC_SMOKE_BASE_URL || "http://127.0.0.1:5088";
const username = process.env.LAC_SMOKE_USERNAME;
const password = process.env.LAC_SMOKE_PASSWORD;

async function runSmokeTest() {
  if (!username || !password) {
    console.log("SKIPPED: LAC_SMOKE_USERNAME and LAC_SMOKE_PASSWORD environment variables are required to run authenticated smoke test.");
    process.exit(0);
  }

  console.log("Starting authenticated smoke test against target server...");

  // 1. Authenticate with environment credentials
  const loginRes = await fetch(`${baseUrl}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });

  if (!loginRes.ok) {
    throw new Error(`Authentication failed with status ${loginRes.status}. Ensure valid LAC_SMOKE_USERNAME and LAC_SMOKE_PASSWORD environment variables are provided.`);
  }

  // Extract session cookies securely without printing
  const rawCookies = loginRes.headers.getSetCookie ? loginRes.headers.getSetCookie() : [loginRes.headers.get("set-cookie")].filter(Boolean);
  const cookieHeader = rawCookies.map((c) => c.split(";")[0]).join("; ");

  const headers = {
    "Content-Type": "application/json",
    ...(cookieHeader ? { Cookie: cookieHeader } : {})
  };

  // 2. Fetch existing matters
  const mattersRes = await fetch(`${baseUrl}/api/matters?pageSize=50`, { headers });
  assert.equal(mattersRes.status, 200, "Fetch matters should return 200");
  const mattersData = await mattersRes.json();
  const matterItems = mattersData.items || mattersData;
  assert.ok(matterItems.length > 0, "Should have at least one matter");

  // Fetch available Court Cases
  const courtCasesRes = await fetch(`${baseUrl}/api/court-cases?search=&pageSize=50`, { headers });
  assert.equal(courtCasesRes.status, 200, "Fetch court cases should return 200");
  const courtCasesData = await courtCasesRes.json();
  const availableCourtCases = courtCasesData.items || courtCasesData;
  assert.ok(availableCourtCases.length > 0, "Should have available court cases");

  // Find a target matter and an unlinked Court Case candidate
  let targetMatter = null;
  let targetContext = null;
  let candidateCourtCaseId = null;

  for (const m of matterItems) {
    const ctxRes = await fetch(`${baseUrl}/api/matters/${m.id}/context`, { headers });
    if (!ctxRes.ok) continue;
    const ctx = await ctxRes.json();

    const linkedCourtCaseIds = (ctx.courtCases || []).map((c) => c.courtCaseId || c.id);
    const unlinkedCourtCase = availableCourtCases.find((cc) => !linkedCourtCaseIds.includes(cc.id || cc.courtCaseId));

    if (unlinkedCourtCase) {
      targetMatter = m;
      targetContext = ctx;
      candidateCourtCaseId = unlinkedCourtCase.id || unlinkedCourtCase.courtCaseId;
      console.log(`Selected Target Matter ID=${m.id}, Unlinked Court Case ID=${candidateCourtCaseId}`);
      break;
    }
  }

  assert.ok(targetMatter && candidateCourtCaseId, "Should find a matter and an unlinked court case");

  const initialRevision = targetContext.matter.revision;
  const initialLinkedCount = targetContext.courtCases.length;
  console.log(`Initial State: Matter ID=${targetMatter.id}, Revision=${initialRevision}, Linked Court Cases Count=${initialLinkedCount}`);

  // 3. Link the Court Case (PUT /api/matters/{id}/court-cases/{courtCaseId})
  console.log(`Executing Link mutation (PUT /api/matters/${targetMatter.id}/court-cases/${candidateCourtCaseId})...`);
  const linkRes = await fetch(`${baseUrl}/api/matters/${targetMatter.id}/court-cases/${candidateCourtCaseId}`, {
    method: "PUT",
    headers,
    body: JSON.stringify({ expectedRevision: initialRevision })
  });
  assert.equal(linkRes.status, 200, "Link Court Case mutation should succeed with HTTP 200");

  // 4. Confirm it appears immediately in fresh matter context (simulating frontend setRefresh(r => r + 1))
  const ctxRes2 = await fetch(`${baseUrl}/api/matters/${targetMatter.id}/context`, { headers });
  assert.equal(ctxRes2.status, 200);
  const ctx2 = await ctxRes2.json();
  const isNowLinked = (ctx2.courtCases || []).some((item) => (item.courtCaseId || item.id) === candidateCourtCaseId);
  console.log(`Post-Link Context: Revision=${ctx2.matter.revision}, RecordLinked=${isNowLinked}, Linked Count=${ctx2.courtCases.length}`);
  assert.ok(isNowLinked, "Newly linked Court Case MUST appear immediately in matter context without full page reload");
  assert.ok(ctx2.matter.revision > initialRevision, "Revision MUST increment on link mutation");

  // 5. Unlink the Court Case (DELETE /api/matters/{id}/court-cases/{courtCaseId}?expectedRevision={rev})
  console.log(`Executing Unlink mutation (DELETE /api/matters/${targetMatter.id}/court-cases/${candidateCourtCaseId}?expectedRevision=${ctx2.matter.revision})...`);
  const unlinkRes = await fetch(`${baseUrl}/api/matters/${targetMatter.id}/court-cases/${candidateCourtCaseId}?expectedRevision=${ctx2.matter.revision}`, {
    method: "DELETE",
    headers
  });
  assert.equal(unlinkRes.status, 200, "Unlink Court Case mutation should succeed with HTTP 200");

  // 6. Confirm it disappears immediately from fresh matter context (simulating frontend setRefresh(r => r + 1))
  const ctxRes3 = await fetch(`${baseUrl}/api/matters/${targetMatter.id}/context`, { headers });
  assert.equal(ctxRes3.status, 200);
  const ctx3 = await ctxRes3.json();
  const isStillLinked = (ctx3.courtCases || []).some((item) => (item.courtCaseId || item.id) === candidateCourtCaseId);
  console.log(`Post-Unlink Context: Revision=${ctx3.matter.revision}, RecordLinked=${isStillLinked}, Linked Count=${ctx3.courtCases.length}`);
  assert.ok(!isStillLinked, "Unlinked Court Case MUST disappear immediately from matter context without full page reload");
  assert.equal(ctx3.courtCases.length, initialLinkedCount, "Final linked count must match initial count");

  console.log("REAL AUTHENTICATED SMOKE TEST PASSED PERFECTLY! Original relationship state fully restored.");
}

runSmokeTest().catch((err) => {
  console.error("SMOKE TEST FAILED:", err.message || err);
  process.exit(1);
});
