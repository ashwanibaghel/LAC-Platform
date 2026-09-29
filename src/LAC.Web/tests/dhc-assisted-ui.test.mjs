import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const here = path.dirname(fileURLToPath(import.meta.url));
const page = readFileSync(path.join(here, "../src/court/DhcAssistedPage.tsx"), "utf8");
const panel = readFileSync(path.join(here, "../src/court/DhcSyncPanel.tsx"), "utf8");
const workspace = readFileSync(path.join(here, "../src/court/DhcOfficialVerification.tsx"), "utf8");
const css = readFileSync(path.join(here, "../src/court/court.css"), "utf8");

test("DHC panel leads with daily status and hides technical work under More", () => {
  assert.match(panel, /Automatic updates active/);
  assert.match(panel, /one-time history check/i);
  assert.match(panel, /Technical and history details/);
});

test("queue preview and progress expose meaningful counts", () => {
  assert.match(page, /preview\.recommendedCount/);
  assert.match(page, /preview\.noNdohCount/);
  assert.match(page, /preview\.overdueCount/);
  assert.match(page, /run\.completedCases/);
  assert.match(page, /run\.needsReviewCases/);
});

test("start verification shows the official-site failure instead of blaming another officer", () => {
  assert.match(page, /problem\?\.detail \|\| problem\?\.title/);
  assert.doesNotMatch(page, /Another officer may have an active session/);
});

test("official challenge remains human-entered, blank, and cleared after submission", () => {
  assert.match(page, /useState\(""\)/);
  assert.match(page, /setAnswer\(""\); \/\/ Never retain a submitted answer/);
  assert.match(page, /Official DHC verification code/);
  assert.match(page, /code is entered manually by the officer/);
  assert.match(page, />Continue</);
  assert.doesNotMatch(page, /setAnswer\(challenge\.officialText/);
});

test("case workspace shows plain-language official status and order evidence", () => {
  assert.match(workspace, /Last checked:/);
  assert.match(workspace, /Delhi High Court status:/);
  assert.match(workspace, /Next date:/);
  assert.match(workspace, /Latest official DHC order:/);
  assert.match(workspace, /Office status and Delhi High Court status differ/);
});

test("status batch completes before the separately chosen order phase", () => {
  assert.match(page, /Checking case status/);
  assert.match(page, /Checking order links/);
  assert.match(page, /run\.status === "ReadyForOrders"/);
  assert.match(page, /Check order links/);
  assert.match(page, />Done</);
});

test("completed status interruption reopens at the order-or-finish choice", () => {
  assert.match(page, /Return to order\/finish choice/);
  assert.match(page, /run\.items\.every\(item => \["StatusCaptured", "Completed", "NeedsReview", "NotFound", "Skipped"\]/);
  assert.match(page, /Could not reopen this verification run/);
});

test("canonical status confirmation is distinct from accepting external evidence", () => {
  assert.match(page, /Accept evidence/);
  assert.match(page, /Confirm LAC status as Disposed/);
  assert.match(page, /confirm-canonical-status/);
  assert.match(page, /Reason for decision/);
});

test("assisted queue keeps narrow office screens inside the page width", () => {
  assert.match(page, /court-assisted-page/);
  assert.match(css, /\.court-assisted-page \{[^}]*max-width: 1220px; min-width: 0/);
  assert.match(css, /\.court-assisted-table-wrap \{[^}]*overflow: auto/);
  assert.match(css, /@media \(max-width: 850px\) \{ \.court-assisted-preview, \.court-assisted-run-layout \{ grid-template-columns: 1fr/);
});
