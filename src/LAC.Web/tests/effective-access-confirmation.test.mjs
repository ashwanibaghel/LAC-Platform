import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import test from "node:test";
import {
  isCanonicalAdmSelection, getCreateConfirmationAuthority, getEffectiveAccessSummary,
  effectiveAccessSummaryItems, effectiveAccessChanges
} from "../src/admin/effectiveAccessConfirmation.ts";

const full = {
  fullOfficeAccess: true, workAccess: "Full Office Access",
  inwardDakRegistry: "Can Register Inward Dak", landRecords: "View + Write"
};
const none = {
  fullOfficeAccess: false, workAccess: "None",
  inwardDakRegistry: "No Registry Rights", landRecords: "None"
};

test("System Admin creates canonical ADM: confirmation matches effective backend access", () => {
  const canonical = isCanonicalAdmSelection({ code: "ADM" }, false);
  const authority = getCreateConfirmationAuthority("STANDARD_OFFICER", "SYSTEM_ADMIN", canonical);
  assert.equal(authority, "OFFICE_ADMIN");
  assert.deepEqual(getEffectiveAccessSummary(authority, "None", false, "None"), full);
  assert.deepEqual(effectiveAccessSummaryItems(full), [
    { label: "Work Access", value: "Full Office Access" },
    { label: "Inward Dak Registry", value: "Can Register Inward Dak" },
    { label: "Land Records", value: "View + Write" }
  ]);
});

for (const authority of ["SYSTEM_ADMIN", "OFFICE_ADMIN", "OFFICE_SUPERVISOR"]) {
  test(`${authority} always shows full operational access despite default form selections`, () => {
    assert.deepEqual(getEffectiveAccessSummary(authority, "None", false, "None"), full);
    assert.deepEqual(getEffectiveAccessSummary(authority, "Court", false, "ViewOnly"), full);
  });
}

test("Standard Officer with no selections displays no grants and does not change input", () => {
  const selection = Object.freeze({ authority: "STANDARD_OFFICER", modules: "None", registry: false, land: "None" });
  assert.deepEqual(getEffectiveAccessSummary(selection.authority, selection.modules, selection.registry, selection.land), none);
  assert.deepEqual(selection, { authority: "STANDARD_OFFICER", modules: "None", registry: false, land: "None" });
});
test("Standard Officer selected modules, Registry and ViewOnly remain independent", () => {
  assert.deepEqual(getEffectiveAccessSummary("STANDARD_OFFICER", "Court, Accounts", true, "ViewOnly"), {
    fullOfficeAccess: false, workAccess: "Court, Accounts", inwardDakRegistry: "Can Register Inward Dak", landRecords: "View Only"
  });
});
test("Registry without modules or land is still displayed without unrelated grants", () => {
  assert.deepEqual(getEffectiveAccessSummary("STANDARD_OFFICER", "None", true, "None"), { ...none, inwardDakRegistry: "Can Register Inward Dak" });
});
test("ViewOnly without Registry or modules remains ViewOnly", () => {
  assert.deepEqual(getEffectiveAccessSummary("STANDARD_OFFICER", "None", false, "ViewOnly"), { ...none, landRecords: "View Only" });
});
test("Edit promotion to Office Supervisor reviews all effective changes", () => {
  const promoted = getEffectiveAccessSummary("OFFICE_SUPERVISOR", "None", false, "None");
  assert.deepEqual(effectiveAccessChanges(none, promoted), [
    { label: "Work Access Change", value: "None → Full Office Access" },
    { label: "Inward Dak Change", value: "No Registry Rights → Can Register Inward Dak", isAddition: true, isRemoval: false },
    { label: "Land Records Change", value: "None → View + Write" }
  ]);
  assert.deepEqual(effectiveAccessSummaryItems(promoted), effectiveAccessSummaryItems(full));
});
for (const authority of ["OFFICE_ADMIN", "SYSTEM_ADMIN"]) {
  test(`Existing ${authority} edit ignores stale granular values without false revocations`, () => {
    const before = getEffectiveAccessSummary(authority, "Full Office Access", true, "ViewWrite");
    const proposed = getEffectiveAccessSummary(authority, "None", false, "None");
    assert.deepEqual(effectiveAccessChanges(before, proposed), []);
    assert.deepEqual(proposed, full);
  });
}
test("Supervisor demotion review uses explicit Standard Officer selections", () => {
  const after = getEffectiveAccessSummary("STANDARD_OFFICER", "None", false, "None");
  assert.deepEqual(after, none);
  assert.deepEqual(effectiveAccessChanges(full, after).map(x => x.value), [
    "Full Office Access → None", "Can Register Inward Dak → No Registry Rights", "View + Write → None"
  ]);
});
test("Standard Officer edit reviews effective Registry and Land changes", () => {
  const after = getEffectiveAccessSummary("STANDARD_OFFICER", "Court", true, "ViewOnly");
  assert.deepEqual(effectiveAccessChanges(none, after).map(x => x.value), [
    "No Registry Rights → Can Register Inward Dak", "None → View Only"
  ]);
});
for (const title of ["ADM Support", "Additional District Magistrate Assistant"]) {
  test(`Custom designation ${title} never escalates authority`, () => {
    assert.equal(isCanonicalAdmSelection({ code: "ADM", name: title }, true), false);
    assert.equal(isCanonicalAdmSelection({ code: "CUSTOM_ADM", name: title }, false), false);
    assert.equal(getCreateConfirmationAuthority("STANDARD_OFFICER", "SYSTEM_ADMIN", false), "STANDARD_OFFICER");
  });
}
test("Canonical ADM overrides a stale Supervisor checkbox in the displayed creation authority", () => {
  assert.equal(getCreateConfirmationAuthority("OFFICE_SUPERVISOR", "SYSTEM_ADMIN", true), "OFFICE_ADMIN");
});
test("Other callers and edits cannot receive designation-driven authority escalation", () => {
  assert.equal(getCreateConfirmationAuthority("STANDARD_OFFICER", "OFFICE_ADMIN", true), "STANDARD_OFFICER");
  assert.equal(getCreateConfirmationAuthority("OFFICE_SUPERVISOR", "OFFICE_ADMIN", true), "OFFICE_SUPERVISOR");
});

const sourcePath = "src/LAC.Web/src/admin/UsersAdmin.tsx";
const source = readFileSync(new URL("../src/admin/UsersAdmin.tsx", import.meta.url), "utf8").replaceAll("\r\n", "\n");
const baseline = execFileSync("git", ["show", `7d72d0af69c8c190194223a0dcd73327d5939403:${sourcePath}`], { encoding: "utf8" }).replaceAll("\r\n", "\n");
test("Normal account request payloads and all mutation callbacks remain unchanged", () => {
  const payloads = text => Array.from(text.matchAll(/const payload = \{[\s\S]*?\n    \};/g), m => m[0]);
  const callbacks = text => Array.from(text.matchAll(/onConfirm: async \(\) => \{[\s\S]*?\n      \},\n    \}\);/g), m => m[0]);
  assert.ok(payloads(baseline).length >= 3);
  assert.ok(callbacks(baseline).length >= 5);
  assert.deepEqual(payloads(source), payloads(baseline));
  assert.deepEqual(callbacks(source), callbacks(baseline));
});
test("Shared confirmation disabled/cancel guards, helper paths and desk double-click protections stay intact", () => {
  const renderer = text => text.slice(text.indexOf("{confirmationDialog && ("), text.indexOf("TECHNICAL ACCESS INSPECTOR MODAL"));
  assert.ok(renderer(source).includes("disabled={actionLoading}"));
  assert.equal(renderer(source), renderer(baseline));
  const desks = readFileSync(new URL("../src/admin/OfficeDesksAdmin.tsx", import.meta.url), "utf8").replaceAll("\r\n", "\n");
  const oldDesks = execFileSync("git", ["show", "7d72d0af69c8c190194223a0dcd73327d5939403:src/LAC.Web/src/admin/OfficeDesksAdmin.tsx"], { encoding: "utf8" }).replaceAll("\r\n", "\n");
  assert.equal(desks, oldDesks);
  assert.ok(desks.includes("confirmSubmittingRef.current"));
});
test("ADM warning and authority-derived summaries are wired into create, edit and technical admin reviews", () => {
  assert.ok(source.includes('NOTICE: Selecting Additional District Magistrate (ADM)'));
  assert.ok(source.includes('warning: confirmationAuthority === "OFFICE_ADMIN"'));
  assert.ok(source.includes("summaryItems: [...diffItems, ...effectiveAccessSummaryItems(afterAccess)]"));
  assert.ok(source.includes('effectiveAccessSummaryItems(getEffectiveAccessSummary("SYSTEM_ADMIN", "None", false, "None"))'));
});
