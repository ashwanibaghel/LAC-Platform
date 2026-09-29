import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "../src");
const read = relative => readFileSync(path.join(root, relative), "utf8");
const panel = read("court/DhcSyncPanel.tsx");
const assisted = read("court/DhcAssistedPage.tsx");
const workspace = read("court/DhcOfficialVerification.tsx");
const home = read("home/Home.tsx");

test("daily DHC panel reports proven completed-cycle counts and offers watched cases", () => {
  for (const field of ["sourceDocumentsProcessed", "observationsCreated", "observationsAccepted", "reviewCount"])
    assert.match(panel, new RegExp(`completed\\.${field}`));
  assert.match(panel, /watchedCount/);
  assert.match(panel, /DHC cases being watched/);
  assert.match(panel, /No current DHC cause-list date recorded/);
  assert.match(panel, /Technical and history details/);
  assert.doesNotMatch(panel, /completed\.targetCaseMatches/);
  assert.match(panel, /dhc-assisted\/runs\/\$\{id\}/);
  assert.match(panel, /run\.phase === "OrderLookup"/);
  assert.match(panel, /Resume order check/);
});

test("assisted results show each current-run official status and date only after an individual lookup", () => {
  assert.match(assisted, /dhc-status-observations/);
  assert.match(assisted, /requestedResults\.current\.has\(key\)/);
  assert.match(assisted, /new Date\(row\.observedAt\).*new Date\(run\.startedAt\)/);
  assert.match(assisted, /DHC: \{result\.rawStatus/);
  assert.match(assisted, /Next date:/);
  assert.match(assisted, /No official case result found in this check/);
  assert.match(assisted, /Action needed/);
});

test("status difference uses backend-validated update or keep actions with standard audit reasons", () => {
  assert.match(assisted, /reviewReason === "StatusDifference"/);
  assert.match(assisted, /rawStatus\?\.trim\(\)\.toLowerCase\(\) === "disposed"/);
  assert.match(assisted, /canonicalStatus\?\.trim\(\)\.toLowerCase\(\) === "pending"/);
  assert.match(assisted, /Update LAC status to Disposed/);
  assert.match(assisted, /Keep LAC as Pending/);
  assert.match(assisted, /Officer confirmed the exact official Delhi High Court status/);
  assert.match(assisted, /Officer reviewed the official Delhi High Court result/);
  assert.match(assisted, /View official result \/ Technical details/);
  assert.match(workspace, /View official result \/ Technical details/);
  const simpleDecision = assisted.match(/safeStatusDecision \? <>[\s\S]*?<\/> : <>/);
  assert.ok(simpleDecision, "normal status-decision branch missing");
  assert.doesNotMatch(simpleDecision[0], /Reason for decision/);
});

test("home derives a small next-seven-days Court card from the existing API", () => {
  assert.match(home, /hasPermission\("Court.View"\)/);
  assert.match(home, /\/api\/court-cases\?ndohFilter=Next7Days&page=1&pageSize=5/);
  assert.match(home, /upcomingCourt\.items\.slice\(0, 5\)/);
  assert.match(home, /Verified from DHC cause list/);
  assert.match(home, /\/court-cases\?ndohFilter=Next7Days/);
});
