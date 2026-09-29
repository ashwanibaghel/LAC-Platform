import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const here = path.dirname(fileURLToPath(import.meta.url));
const page = readFileSync(path.join(here, "../src/court/DhcAssistedPage.tsx"), "utf8");
const panel = readFileSync(path.join(here, "../src/court/DhcSyncPanel.tsx"), "utf8");
const workspace = readFileSync(path.join(here, "../src/court/DhcOfficialVerification.tsx"), "utf8");

test("DHC panel separates automatic, historical, and human-assisted work", () => {
  assert.match(panel, /AUTOMATIC CAUSE LIST/i);
  assert.match(panel, /HISTORICAL CATCH-UP/i);
  assert.match(panel, /ASSISTED CASE STATUS/i);
});

test("queue preview and progress expose meaningful counts", () => {
  assert.match(page, /preview\.recommendedCount/);
  assert.match(page, /preview\.noNdohCount/);
  assert.match(page, /preview\.overdueCount/);
  assert.match(page, /run\.completedCases/);
  assert.match(page, /run\.needsReviewCases/);
});

test("official challenge remains human-entered, blank, and cleared after submission", () => {
  assert.match(page, /useState\(""\)/);
  assert.match(page, /setAnswer\(""\); \/\/ Never retain a submitted answer/);
  assert.match(page, /Official DHC verification code/);
  assert.match(page, /LAC Platform does not solve or bypass it/);
  assert.match(page, /Verify &amp; continue/);
  assert.doesNotMatch(page, /setAnswer\(challenge\.officialText/);
});

test("case workspace separates official status and order evidence from canonical record", () => {
  assert.match(workspace, /Last checked:/);
  assert.match(workspace, /Official case status:/);
  assert.match(workspace, /Listing date:/);
  assert.match(workspace, /Court:/);
  assert.match(workspace, /Latest official DHC order:/);
  assert.match(workspace, /Official DHC status differs from LAC record/);
});

test("status batch completes before the separately chosen order phase", () => {
  assert.match(page, /Phase 1 of 2 · Case status verification/);
  assert.match(page, /Phase 2 of 2 · Official order links/);
  assert.match(page, /run\.status === "ReadyForOrders"/);
  assert.match(page, /Check official order links/);
  assert.match(page, /Finish session/);
});

test("canonical status confirmation is distinct from accepting external evidence", () => {
  assert.match(page, /Accept evidence/);
  assert.match(page, /Confirm LAC status as Disposed/);
  assert.match(page, /confirm-canonical-status/);
  assert.match(page, /Reason for decision/);
});

test("assisted queue keeps narrow office screens inside the page width", () => {
  assert.match(page, /maxWidth: 1050/);
  assert.match(page, /overflowX: "auto"/);
  assert.match(page, /maxWidth: "100%"/);
});
