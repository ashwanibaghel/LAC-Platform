import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import ts from "typescript";

const here = path.dirname(fileURLToPath(import.meta.url));
const page = readFileSync(path.join(here, "../src/court/DhcAssistedPage.tsx"), "utf8");
const panel = readFileSync(path.join(here, "../src/court/DhcSyncPanel.tsx"), "utf8");
const workspace = readFileSync(path.join(here, "../src/court/DhcOfficialVerification.tsx"), "utf8");
const css = readFileSync(path.join(here, "../src/court/court.css"), "utf8");
const orderProgressSource = readFileSync(path.join(here, "../src/court/DhcAssistedOrderProgress.ts"), "utf8");
const orderProgressJs = ts.transpileModule(orderProgressSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { orderLinkCheckState } = await import(`data:text/javascript;base64,${Buffer.from(orderProgressJs).toString("base64")}`);

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
  assert.match(page, />Enter verification code</);
  assert.doesNotMatch(page, /setAnswer\(challenge\.officialText/);
});

test("case workspace leads with outcome and keeps technical evidence collapsed", () => {
  assert.match(workspace, /UPDATED/);
  assert.match(workspace, /ACTION NEEDED/);
  assert.match(workspace, /VERIFIED/);
  assert.match(workspace, /Official next date:/);
  assert.match(workspace, /No change needed/);
  assert.match(workspace, /View official result \/ Technical details/);
  assert.match(workspace, /Official order links and details/);
});

test("status batch completes before the separately chosen order phase", () => {
  assert.match(page, /Checking case status/);
  assert.match(page, /Checking order links/);
  assert.match(page, /run\.status === "ReadyForOrders"/);
  assert.match(page, /Check order links/);
  assert.match(page, />Done</);
});

test("completed status interruption explains the separate order check", () => {
  assert.match(page, /Case status checking is complete/);
  assert.match(page, /Order-link checking was interrupted/);
  assert.match(page, /Resume order check/);
  assert.match(page, /run\.items\.every\(item => \["StatusCaptured", "Completed", "NeedsReview", "NotFound", "Skipped"\]/);
  assert.match(page, /Could not reopen this verification run/);
});

test("interrupted order phase leads with its own heading and separate progress", () => {
  assert.match(page, /run\.phase === "OrderLookup" && \["Interrupted", "Failed"\]\.includes\(run\.status\)[\s\S]*?"Order-link check interrupted"/);
  assert.match(page, /Case-status check is complete\. Order-link checking has not finished\./);
  const progress = page.match(/\{run\.phase === "OrderLookup" \? <div className="court-assisted-phase-progress">([\s\S]*?)<\/div> :/);
  assert.ok(progress, "order phase must have a dedicated progress layout");
  assert.match(progress[1], /Case-status check progress[\s\S]*?run\.completedCases\} of \{run\.totalCases\} complete/);
  assert.match(progress[1], /Order-link check progress[\s\S]*?orderLinkCheckState\(run\.status\)/);
  assert.doesNotMatch(page, /order searches completed|orderCount|countCompletedOrderSearches/);
  assert.match(page, /run\.phase === "OrderLookup" \? "Resume order check"/);
  assert.match(page, /onClick=\{\(\) => void resume\(\)\}/);
  assert.match(page, /onClick=\{\(\) => void orderAction\("orders"\)\}>Check order links/);
});

test("order-link progress uses truthful run state instead of a derived search count", () => {
  assert.equal(orderLinkCheckState("WaitingForCaptcha"), "Waiting for verification code");
  assert.equal(orderLinkCheckState("PausedForCaptcha"), "Verification code needed to continue");
  assert.equal(orderLinkCheckState("Running"), "Checking official order links…");
  assert.equal(orderLinkCheckState("Interrupted"), "Order-link check interrupted");
  assert.equal(orderLinkCheckState("Failed"), "Order-link check interrupted");
  assert.equal(orderLinkCheckState("Completed"), "Order-link check complete");
  assert.doesNotMatch(orderProgressSource, /items|failureCode|order searches completed/);
  assert.match(page, /\["CheckingStatus", "CheckingOrders", "CaptchaRequired"\]\.includes\(item\.status\)/);
  assert.match(page, /run\.phase === "OrderLookup" \? "Checking order links for"[\s\S]*?currentItem\.caseNumber/);
  assert.match(page, /<strong>\{run\.completedCases\} of \{run\.totalCases\} complete<\/strong>/);
  assert.match(page, /run\.phase === "OrderLookup" \? "Resume order check"/);
  assert.match(page, /onClick=\{\(\) => void resume\(\)\}/);
  assert.match(page, /onClick=\{\(\) => void orderAction\("orders"\)\}>Check order links/);
});

test("review heading uses local case number while raw official number stays in details", () => {
  assert.match(page, /type Review = \{[^\n]*caseNumber: string;[^\n]*rawCaseNumber: string;/);
  assert.match(page, /court-assisted-review-title"><Link[^>]*>\{row\.caseNumber\}<\/Link>/);
  assert.doesNotMatch(page, /court-assisted-review-title"><Link[^>]*>\{row\.rawCaseNumber\}<\/Link>/);
  assert.match(page, /<details><summary>View official result \/ Technical details<\/summary>[\s\S]*?Official case number as shown: \{row\.rawCaseNumber\}/);
  assert.match(page, /\{row\.rawEvidenceText\}/);
  assert.match(page, /Delhi High Court says: <strong>DISPOSED<\/strong>/);
  assert.match(page, /LAC record says: <strong>PENDING<\/strong>/);
});

test("normal status difference has explicit office actions without a typed reason", () => {
  assert.match(page, /Update LAC status to Disposed/);
  assert.match(page, /Keep LAC as Pending/);
  assert.match(page, /confirm-canonical-status/);
  assert.match(page, /statusDecisionReason/);
  assert.match(page, /keepDecisionReason/);
  assert.match(page, /safeStatusDecision \? <>/);
  assert.match(page, /: <>[\s\S]*Reason for decision/);
});

test("assisted queue keeps narrow office screens inside the page width", () => {
  assert.match(page, /court-assisted-page/);
  assert.match(css, /\.court-assisted-page \{[^}]*max-width: 1220px; min-width: 0/);
  assert.match(css, /\.court-assisted-table-wrap \{[^}]*overflow: auto/);
  assert.match(css, /@media \(max-width: 850px\) \{ \.court-assisted-preview, \.court-assisted-run-layout \{ grid-template-columns: 1fr/);
});
