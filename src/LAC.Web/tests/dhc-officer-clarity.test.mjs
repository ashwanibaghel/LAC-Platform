import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import ts from "typescript";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "../src");
const read = relative => readFileSync(path.join(root, relative), "utf8");
const panel = read("court/DhcSyncPanel.tsx");
const assisted = read("court/DhcAssistedPage.tsx");
const workspace = read("court/DhcOfficialVerification.tsx");
const home = read("home/Home.tsx");
const courtCss = read("court/court.css");
const dailyOutcomeJs = ts.transpileModule(read("court/DhcDailyCauseListOutcome.ts"), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { dailyCauseListOutcome } = await import(`data:text/javascript;base64,${Buffer.from(dailyOutcomeJs).toString("base64")}`);

test("daily DHC panel reports proven completed-cycle counts and offers watched cases", () => {
  for (const field of ["sourceDocumentsDiscovered", "sourceDocumentsProcessed", "observationsCreated", "observationsAccepted", "reviewCount"])
    assert.match(panel, new RegExp(`attempt\\.${field}`));
  assert.match(panel, /watchedCount/);
  assert.match(panel, /DHC cases being watched/);
  assert.match(panel, /No current DHC cause-list date recorded/);
  assert.match(panel, /Technical and history details/);
  assert.doesNotMatch(panel, /completed\.targetCaseMatches/);
  assert.match(panel, /dhc-assisted\/runs\/\$\{id\}/);
  assert.match(panel, /run\.phase === "OrderLookup"/);
  assert.match(panel, /Resume order check/);
});

test("DHC modal keeps the polished two-card hub without a plain inline panel", () => {
  assert.match(panel, /dhc-circle-launcher/);
  assert.match(panel, /\{isOpen && \(\s*<div\s+className="dhc-popup-overlay"/);
  assert.match(panel, /role="dialog" aria-modal="true" aria-label="Delhi High Court Intelligence Hub"/);
  assert.match(panel, /Delhi High Court Live Hub/);
  assert.match(panel, /Daily Cause List/);
  assert.match(panel, /Official Case Verification/);
  assert.match(panel, /Orders & Disposal Check/);
  assert.match(panel, /Historical check paused/);
  assert.match(panel, /Technical details & history/);
  assert.equal((panel.match(/className="dhc-hub-card(?: highlight)?"/g) ?? []).length, 2);
  assert.doesNotMatch(panel, /Continue check/);
  assert.match(courtCss, /\.dhc-popup-container \{\s*width: min\(840px, 94vw\)/);
  assert.match(courtCss, /animation: balloonPop 0\.42s/);
  assert.match(courtCss, /\.dhc-hub-cards-grid \{\s*display: grid;\s*grid-template-columns: repeat\(2, minmax\(0, 1fr\)\)/);
});

test("historical progress separates archive pages, publications, and review items", () => {
  assert.match(panel, /Archive pages scanned<\/span>[\s\S]*?historyRun\?\.archivePagesDiscovered/);
  assert.match(panel, /Publications found<\/span>[\s\S]*?historyRun\?\.sourceDocumentsDiscovered/);
  assert.match(panel, /Publications checked<\/span>[\s\S]*?historyRun\?\.sourceDocumentsProcessed/);
  assert.match(panel, /Needs review<\/span>[\s\S]*?historyRun\?\.reviewCount/);
  assert.match(panel, /Checking historical publications… \$\{historyRun\.sourceDocumentsProcessed\} of \$\{historyRun\.sourceDocumentsDiscovered\} checked/);
  assert.match(panel, /publication needs review; continuing with the remaining publications/);
  assert.match(panel, /Historical check complete · \$\{historyRun\.sourceDocumentsProcessed\} of \$\{historyRun\.sourceDocumentsDiscovered\} checked · \$\{historyRun\.reviewCount\} needs review/);
  assert.match(panel, /historyRun\?\.status === "Failed"[\s\S]*?Historical check paused/);
  assert.doesNotMatch(panel, /PUBLICATIONS.*archivePagesDiscovered/);
});

test("technical history labels live and one-time historical counters in separate groups", () => {
  const live = panel.match(/<section className="dhc-tech-run-group" aria-label="Latest automatic check">([\s\S]*?)<\/section>/)?.[1];
  const history = panel.match(/<section className="dhc-tech-run-group" aria-label="One-time historical check">([\s\S]*?)<\/section>/)?.[1];
  assert.ok(live && history, "both technical run groups must be visible as distinct sections");
  for (const field of ["status", "sourceDocumentsDiscovered", "sourceDocumentsProcessed", "observationsCreated", "observationsAccepted", "reviewCount"])
    assert.match(live, new RegExp(`attempt\\?\\.${field}`));
  for (const field of ["status", "archivePagesDiscovered", "sourceDocumentsDiscovered", "sourceDocumentsProcessed", "reviewCount"])
    assert.match(history, new RegExp(`historyRun\\?\\.${field}`));
  assert.match(live, /Processed this cycle/);
  assert.match(history, /Publications checked/);
  assert.doesNotMatch(live, /archivePagesDiscovered|historyRun/);
  assert.doesNotMatch(history, /observationsCreated|attempt/);
  assert.doesNotMatch(panel, /sourceDocumentsProcessed \?\? 0\}\s*\/\s*\{attempt\?\.sourceDocumentsDiscovered/);
  assert.match(panel, /<details className="dhc-raw-json-details">/);
});

test("hub preserves contextual actions and only reports completed-cycle evidence", () => {
  for (const label of ["Check now", "Enter verification code", "Resume DHC check", "Resume order check", "View verification results"])
    assert.ok(panel.includes(label), `${label} action missing`);
  assert.match(panel, /activeAssistedRun \? activeCheck \? checkButton\(activeCheck\) : "View check progress" : "Check now"/);
  assert.match(panel, /\{attempt\?\.status === "Completed" && <details className="dhc-hub-detail"/);
  assert.match(panel, /sourceDocumentsProcessed/);
  assert.match(panel, /observationsCreated/);
  assert.match(panel, /observationsAccepted/);
  assert.match(panel, /reviewCount/);
  assert.match(panel, /DHC cases being watched/);
  assert.match(panel, /operationalNdohSource === "DHC Cause List"/);
  assert.match(panel, /No current DHC cause-list date recorded/);
  assert.match(panel, /status\.canSyncNow && \(/);
});

test("latest completed check with ten found and zero processed reports no new updates, not failure", () => {
  const run = { status: "Completed", sourceDocumentsDiscovered: 10, sourceDocumentsProcessed: 0,
    observationsCreated: 0, observationsAccepted: 0, reviewCount: 0 };
  const outcome = dailyCauseListOutcome(run, 126, false);
  assert.equal(outcome.title, "✓ No new DHC updates found");
  assert.equal(outcome.tone, "success");
  assert.ok(outcome.lines.includes("126 Delhi High Court cases are being monitored."));
  assert.ok(outcome.lines.includes("10 current cause-list publications found."));
  assert.ok(outcome.lines.includes("No publication needed reprocessing."));
  assert.doesNotMatch(JSON.stringify(outcome), /failed|interrupted|126 cases updated/i);
  assert.match(panel, /DHC cases being watched\{watchedCount !== null/);
});

test("new listing evidence, latest review items, and older unresolved attention remain separate", () => {
  const run = { status: "Completed", sourceDocumentsDiscovered: 10, sourceDocumentsProcessed: 3,
    observationsCreated: 2, observationsAccepted: 1, reviewCount: 1 };
  const outcome = dailyCauseListOutcome(run, 126, false);
  assert.equal(outcome.title, "New official cause-list evidence found");
  assert.ok(outcome.lines.includes("2 new listing entries found."));
  assert.ok(outcome.lines.includes("1 official listing entry confirmed."));
  assert.ok(outcome.lines.includes("1 new issue from this check needs attention."));
  assert.match(panel, /attentionCount = reviews\.length \+ sourceReviews\.length/);
  assert.match(panel, /DHC item\{attentionCount === 1[\s\S]*?still need review/);
  assert.doesNotMatch(panel, /attentionCount[^\n]*new issue/);
});

test("manual sync gives immediate dedicated progress without changing historical or assisted actions", () => {
  const outcome = dailyCauseListOutcome(null, 126, true);
  assert.equal(outcome.title, "Checking cause lists…");
  assert.match(panel, /setSyncInProgress\(true\)/);
  assert.match(panel, /disabled=\{syncInProgress \|\| busy\}/);
  assert.match(panel, /syncInProgress \? "Checking cause lists…" : "🔄 Sync now"/);
  assert.match(panel, /setSyncInProgress\(false\)/);
  assert.match(panel, /const runHistorical = async \(\) => \{/);
  assert.match(panel, /const checkButton = \(run: ActiveCheck \| null\)/);
  assert.equal(dailyCauseListOutcome({ status: "Failed" }, 126, false).title, "Cause-list check was interrupted");
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

test("individual result labels follow the current observation, not a stale run item", () => {
  const source = assisted.match(/const caseResultLabel = \(result: CaseResult\) => \{[\s\S]*?\n\};/);
  assert.ok(source, "current-observation result label function missing");
  assert.doesNotMatch(source[0], /item\.status/);
  const label = new Function(`${source[0].replace("(result: CaseResult)", "(result)")}\nreturn caseResultLabel;`)();
  assert.equal(label({ reviewReason: "AutoStatusApplied", status: "Accepted", listingDate: null }), "LAC status updated to Disposed");
  assert.equal(label({ reviewReason: null, status: "NeedsReview", listingDate: null }), "Action needed");
  assert.equal(label({ reviewReason: null, status: "Rejected", listingDate: null }), "Reviewed · LAC record kept");
  assert.equal(label({ reviewReason: null, status: "Accepted", listingDate: null }), "Verified · no action needed");
  assert.equal(label({ reviewReason: null, status: "Accepted", listingDate: "2026-10-01" }), "Official date available");
  assert.match(assisted, /\{result \? <>[\s\S]*?<strong>\{caseResultLabel\(result\)\}<\/strong>/);
  assert.match(assisted, /<\/> : item\.status === "NotFound"/);
  assert.match(assisted, /<small>\{result \? caseResultLabel\(result\) : result === null \? "Official result unavailable"/);
  assert.match(assisted, /\[key\]: null/);
});

test("case workspace offers a direct resolution route only for an unresolved DHC status", () => {
  assert.match(workspace, /const actionNeeded = !!latest && latest\.status === "NeedsReview"/);
  assert.match(workspace, /\{actionNeeded && <p><Link className="dhc-resolve-link" to="\/court-cases\/dhc-assisted">Resolve DHC status →<\/Link><\/p>\}/);
  assert.match(workspace, /<details><summary>View official result \/ Technical details<\/summary>/);
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
