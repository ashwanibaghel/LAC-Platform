import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const here = path.dirname(fileURLToPath(import.meta.url));
const directory = readFileSync(path.join(here, "../src/court/CourtDirectory.tsx"), "utf8");
const dhc = readFileSync(path.join(here, "../src/court/DhcSyncPanel.tsx"), "utf8");
const assisted = readFileSync(path.join(here, "../src/court/DhcAssistedPage.tsx"), "utf8");
const css = readFileSync(path.join(here, "../src/court/court.css"), "utf8");

test("DHC status has three compact cards and attention details start collapsed", () => {
  assert.equal((dhc.match(/className="dhc-sync-card"/g) ?? []).length, 3);
  assert.match(dhc, /className="dhc-sync-grid"/);
  assert.match(css, /\.dhc-sync-grid \{[^}]*repeat\(3/);
  assert.match(dhc, /<details className="dhc-attention"/);
  assert.doesNotMatch(dhc, /<details className="dhc-attention"[^>]*open/);
});

test("assisted CTA and all six queue tabs are retained", () => {
  assert.match(dhc, /Start verification →/);
  assert.match(dhc, /Continue session →/);
  for (const label of ["All", "Today", "Upcoming", "Overdue", "No NDOH", "Disposed"])
    assert.ok(directory.includes(`"${label}"`), `${label} tab missing`);
  assert.match(directory, /setQuick\(key\)/);
});

test("primary filters are compact; advanced filters are conditional and toggleable", () => {
  for (const part of ["court-primary-filters", "court-search-field", "NDOH filter", "label=\"Court\"", "Case number filter", "More filters", "Clear"])
    assert.ok(directory.includes(part), `${part} missing`);
  assert.match(directory, /advancedOpen && <div className="court-advanced-filters"/);
  assert.match(directory, /onClick=\{\(\) => setAdvancedOpen\(open => !open\)\}/);
  assert.match(directory, /useState\(\(\) => advancedFilterKeys\.some\(key => !!urlParams\.get\(key\)\)\)/);
  assert.doesNotMatch(directory, /<select[^>]*multiple/);
});

test("advanced URL filters and multi-selection retain query bindings", () => {
  for (const key of ["statuses", "courtNames", "caseType", "advocate", "village", "award", "directions", "briefFacts", "sourceOrderLinkState", "deskId", "assignedUserId", "ndohFrom", "ndohTo"])
    assert.ok(directory.includes(`"${key}"`), `${key} binding missing`);
  assert.match(directory, /const current = selected\(name\)/);
  assert.match(directory, /\[\.\.\.current, option\]\.join\(","\)/);
  assert.match(directory, /params\.set\("pageSize"/);
});

test("clear, empty, loading, and pagination actions remain", () => {
  assert.match(directory, /const clearFilters = \(\) => \{ setUrlParams\(new URLSearchParams\(\)\)/);
  assert.match(directory, /No matters match these filters/);
  assert.match(directory, /Clear filters/);
  assert.match(directory, /Updating court queue…/);
  assert.match(directory, /Page \{page\} of \{totalPages\}/);
});

test("assisted preview, manual challenge, order choice and review decisions remain", () => {
  assert.match(assisted, /court-assisted-preview/);
  assert.match(assisted, /court-assisted-summary/);
  assert.match(assisted, /court-assisted-challenge/);
  assert.match(assisted, /Official DHC verification code/);
  assert.match(assisted, /setAnswer\(""\); \/\/ Never retain a submitted answer/);
  assert.match(assisted, /run\.status === "ReadyForOrders"/);
  assert.match(assisted, /Check official order links/);
  assert.match(assisted, /Finish session/);
  assert.match(assisted, /Accept evidence/);
  assert.match(assisted, /Keep LAC record/);
  assert.match(assisted, /Confirm LAC status as Disposed/);
  assert.match(assisted, /window\.confirm\("Confirm LAC status as Disposed\?/);
});
