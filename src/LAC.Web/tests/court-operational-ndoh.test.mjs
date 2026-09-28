import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import { fileURLToPath } from "node:url";
import path from "node:path";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import ts from "typescript";

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const overviewPath = path.join(here, "../src/court/CourtOverviewTab.tsx");
const workspacePath = path.join(here, "../src/court/CourtCaseWorkspace.tsx");
const proceedingsPath = path.join(here, "../src/court/CourtProceedingsTab.tsx");
const overviewSource = readFileSync(overviewPath, "utf8");
const compiled = ts.transpileModule(overviewSource, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.React, esModuleInterop: true },
}).outputText;
const module = { exports: {} };
new Function("require", "module", "exports", compiled)(require, module, module.exports);
const { CourtOverviewTab } = module.exports;

function render(currentDate, currentSource) {
  return renderToStaticMarkup(React.createElement(CourtOverviewTab, {
    courtCase: {
      operationalNdoh: currentDate,
      operationalNdohSource: currentSource,
      authoritativeNextDate: "2026-10-02",
      activeScheduleNextDate: "2026-10-02",
      nextHearingDate: "2026-10-02",
      isProjectedToCalendar: true,
      currentStatus: "Pending",
      proceedingsCount: 1,
      documentsCount: 0,
      awardsCount: 0,
      mattersCount: 0,
    },
  }));
}

test("overview presents later DHC evidence as current operational NDOH, with proceeding only as context", () => {
  const html = render("2026-09-30", "DHC Cause List");
  assert.match(html, /Current operational NDOH/);
  assert.match(html, /2026-09-30/);
  assert.match(html, /Official DHC cause list/);
  assert.match(html, /Prior proceeding or office-register date: 2026-10-02/);
  assert.doesNotMatch(html, /Authoritative Proceeding/);
});

test("overview returns to newer proceeding date and source", () => {
  const html = render("2026-10-02", "Court proceeding");
  assert.match(html, /Current operational NDOH/);
  assert.match(html, /Court proceeding/);
  assert.doesNotMatch(html, /Prior proceeding or office-register date/);
});

test("historical catch-up remains a listing, retains original register context", () => {
  const html = render("2026-09-05", "DHC historical cause list");
  assert.match(html, /DHC historical cause list/);
  assert.match(html, /Prior proceeding or office-register date: 2026-10-02/);
  const proceedings = readFileSync(proceedingsPath, "utf8");
  assert.match(proceedings, /operationalNdohSource === "DHC historical cause list"/);
});

test("workspace primary NDOH reads operational fields, not legacy fallback fields", () => {
  const workspace = readFileSync(workspacePath, "utf8");
  const primary = workspace.match(/<span className="court-meta-label">Current operational NDOH<\/span>([\s\S]*?)<\/div>/)?.[1];
  assert.ok(primary, "primary case metadata NDOH must exist");
  assert.match(primary, /courtCase\.operationalNdoh/);
  assert.match(primary, /courtCase\.operationalNdohSource/);
  assert.doesNotMatch(primary, /authoritativeNextDate|activeScheduleNextDate|nextHearingDate/);
});

test("proceeding rows identify their date as recorded context when DHC is operational", () => {
  const proceedings = readFileSync(proceedingsPath, "utf8");
  assert.match(proceedings, /courtCase\.operationalNdohSource === "DHC Cause List"/);
  assert.match(proceedings, /Selected proceeding or register record \(prior context\)/);
  assert.match(proceedings, /Recorded next date:/);
});
