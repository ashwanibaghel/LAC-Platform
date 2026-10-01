import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";

const source = file => readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
const timelineJs = ts.transpileModule(source("home/courtHearingTimeline.ts"), { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const timeline = await import(`data:text/javascript;base64,${Buffer.from(timelineJs).toString("base64")}`);
const row = (id, date) => ({ importBatchId: "batch-a", importRowId: id, sourceRowNumber: 7, rawCaseNumber: "RAW 123", rawCaseTitle: "Workbook parties", parsedNdoh: date, source: "Court Excel", reviewPending: true });

export function renderHome(workbook = [], canonical = []) {
  const data = { summary: { urgentTotal: workbook.length, officeToday: "2026-10-01" }, items: workbook };
  const states = [{}, { totalCount: canonical.length, items: canonical }, data];
  let state = 0;
  const mockedReact = { ...React, useState: () => [states[state++], () => {}], useEffect: () => {} };
  const exports = {};
  const require = name => {
    if (name === "react") return mockedReact;
    if (name === "react/jsx-runtime") return { jsx: React.createElement, jsxs: React.createElement };
    if (name === "react-router-dom") return { Link: ({ to, children, ...props }) => React.createElement("a", { href: to, ...props }, children) };
    if (name.includes("AuthProvider")) return { useAuth: () => ({ user: {}, hasPermission: p => p === "Court.View" }) };
    if (name.includes("CalculatorContext")) return { useCalculator: () => ({ openCalculator() {} }) };
    if (name.includes("courtHearingTimeline")) return timeline;
    if (name.includes("Icons")) return new Proxy({}, { get: () => () => null });
    if (name.endsWith(".css")) return {};
    throw new Error(name);
  };
  const js = ts.transpileModule(source("home/Home.tsx"), { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.React, esModuleInterop: true } }).outputText;
  vm.runInNewContext(js, { exports, require, Intl, Date });
  return renderToStaticMarkup(React.createElement(exports.Home));
}

test("individual workbook entry renders source badge and exact review-row link, never a case workspace", () => {
  const html = renderHome([row("row-a", "2026-10-02")]);
  for (const text of ["RAW 123", "Workbook parties", "Review pending", "From Court Excel", "Not yet added to Court Matters", "Tomorrow", "0 confirmed", "1 need review"]) assert.ok(html.includes(text), text);
  assert.ok(html.includes("/court-cases/imports/batch-a?priority=urgent&amp;reviewRow=row-a"));
  assert.ok(!html.includes('href="/court-cases/row-a"'));
});
test("canonical card retains its case link, styling and verified source; zero workbook count adds no pending entry", () => {
  const html = renderHome([], [{ id: "case-a", caseNumber: "W.P.(C) 1/2026", caseTitle: "Confirmed parties", operationalNdoh: "2026-10-01", daysFromToday: 0, operationalNdohSource: "DHC Cause List" }]);
  for (const text of ['href="/court-cases/case-a"', "home-court-card-row", "Verified from DHC cause list", "1 confirmed", "0 need review"]) assert.ok(html.includes(text));
  assert.ok(!html.includes("home-court-review-pending"));
});
test("combined timeline sorts current/future operational dates first and nearest overdue last", () => {
  const items = timeline.courtHearingTimeline([{ id: "canonical", operationalNdoh: "2026-10-03" }], [row("old", "2026-09-25"), row("tomorrow", "2026-10-02"), row("today", "2026-10-01"), row("overdue", "2026-09-30")], "2026-10-01");
  assert.deepEqual(items.map(x => x.key), ["batch-a:today", "batch-a:tomorrow", "canonical", "batch-a:overdue", "batch-a:old"]);
  assert.ok(renderHome([row("overdue", "2026-09-30")]).includes("Overdue · Review pending"));
});
test("priority banner counts and button select pending urgent queue without changing classifications", () => {
  const preview = source("court/CourtImportPreview.tsx");
  for (const text of ["urgent.urgentTotal", "urgent.upcomingNext7Days", "urgent.overduePending", "Review urgent cases first", 'query.set("priority", "urgent")', 'setWorkState("pending")', 'setSearchParams({ priority: "urgent" })', "No unresolved workbook rows have an upcoming or overdue NDOH.", 'setSelected(row)']) assert.ok(preview.includes(text));
  assert.match(preview, /rows\/\$\{reviewRowId\}/);
  assert.ok(source("home/home.css").includes(".home-court-scroll-container"));
});
test("Home render is passive and urgent data uses read-only API, not DHC actions", () => {
  assert.doesNotMatch(source("home/Home.tsx"), /method:\s*["']POST|\/resume|captcha|dhc-assisted/);
  assert.ok(source("home/Home.tsx").includes("/api/court-cases/imports/urgent-entries"));
});
