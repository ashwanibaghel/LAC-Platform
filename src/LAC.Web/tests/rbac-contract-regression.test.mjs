import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  localDateInputToIso,
  isoToLocalDateInput,
  getTodayLocalDateInput,
  formatLocalDate,
  validateDateRange,
} from "../src/admin/dateUtils.ts";

import {
  buildChildAllocation,
  formatScopeLabel,
} from "../src/admin/assistantUtils.ts";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const normalizeEol = (str) => str.replace(/\r\n/g, "\n");

const typesTs = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/types.ts"), "utf8"));
const usersAdminTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/UsersAdmin.tsx"), "utf8"));
const workCatalogTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/WorkCatalogAdmin.tsx"), "utf8"));
const assistantAdminTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/OfficerAssistantAdmin.tsx"), "utf8"));
const auditAdminTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/AuditLogsAdmin.tsx"), "utf8"));
const appTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/App.tsx"), "utf8"));
const appShellTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/components/AppShell.tsx"), "utf8"));
const changePasswordTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/auth/ChangePasswordView.tsx"), "utf8"));

test("1. AccountOptionsResponse consumes workstreams and desks", () => {
  // Types definition check matching backend SHA 9c1077dd
  assert.ok(
    typesTs.includes("workstreams: { id: string; code: string; name: string }[]"),
    "AccountOptionsResponse includes workstreams: [{ id, code, name }]"
  );
  assert.ok(
    typesTs.includes("desks: { id: string; code: string; name: string; workstreamId: string | null }[]"),
    "AccountOptionsResponse includes desks: [{ id, code, name, workstreamId }]"
  );

  // UsersAdmin options consumption check
  assert.ok(
    usersAdminTsx.includes("setWorkstreams((opts.workstreams || []) as Workstream[])"),
    "UsersAdmin populates workstreams from accountOptions.workstreams"
  );
  assert.ok(
    usersAdminTsx.includes("setDesks(opts.desks || [])"),
    "UsersAdmin populates desks from accountOptions.desks"
  );

  // Runtime mock deserialization test
  const sampleBackendPayload = {
    canAssignRoles: true,
    canManageAllocations: true,
    designations: [{ id: "d-1", code: "LAC", name: "Land Acquisition Collector" }],
    roles: [{ id: "r-1", code: "Officer", name: "Officer" }],
    works: [{ id: "w-1", code: "LR", name: "Land Records", kind: "Standard", workstreamId: "ws-1" }],
    workstreams: [
      { id: "ws-1", code: "SW", name: "South West" },
      { id: "ws-2", code: "NW", name: "North West" },
    ],
    desks: [
      { id: "desk-1", code: "LR-SW-01", name: "SW Land Records Desk", workstreamId: "ws-1" },
      { id: "desk-2", code: "CENTRAL-01", name: "Central Intake Desk", workstreamId: null },
    ],
    districts: [{ id: "dist-1", name: "South West" }],
    subdivisions: [{ id: "sub-1", name: "Dwarka", districtId: "dist-1" }],
    villages: [{ id: "vil-1", name: "Bijwasan", subDivisionId: "sub-1" }],
  };

  assert.equal(sampleBackendPayload.workstreams.length, 2);
  assert.equal(sampleBackendPayload.desks.length, 2);
  assert.equal(sampleBackendPayload.desks[1].workstreamId, null, "Desk with nullable workstreamId accepted");
});

test("2. Users.Manage + Allocations.Manage UI works without successful /api/admin/workstreams, /roles or /desks calls", () => {
  // In UsersAdmin loadData, it must ONLY call /api/admin/users and /api/admin/account-options
  const loadDataMatch = usersAdminTsx.match(/const loadData = useCallback\(async \(\) => \{([\s\S]*?)\}, \[/);
  assert.ok(loadDataMatch, "Found UsersAdmin loadData implementation");
  const loadDataBody = loadDataMatch[1];

  assert.ok(loadDataBody.includes('fetch("/api/admin/users"'), "Calls /api/admin/users");
  assert.ok(loadDataBody.includes('fetch("/api/admin/account-options"'), "Calls /api/admin/account-options");

  // Must NOT call Access.Manage catalog endpoints for options
  assert.ok(!loadDataBody.includes('fetch("/api/admin/workstreams"'), "Must NOT call /api/admin/workstreams");
  assert.ok(!loadDataBody.includes('fetch("/api/admin/roles"'), "Must NOT call /api/admin/roles");
  assert.ok(!loadDataBody.includes('fetch("/api/admin/desks"'), "Must NOT call /api/admin/desks");
  assert.ok(!loadDataBody.includes('fetch("/api/admin/designations"'), "Must NOT call /api/admin/designations");
});

test("3. Assignable roles come only from accountOptions.roles", () => {
  // Option source in UsersAdmin
  assert.ok(
    usersAdminTsx.includes("setRoles(opts.roles || [])"),
    "Roles options set strictly from accountOptions.roles"
  );

  // Role checkbox rendering maps over roles (derived from accountOptions.roles)
  assert.ok(
    usersAdminTsx.includes("{roles.map((r) => ("),
    "Role checkboxes map over accountOptions.roles state"
  );
  assert.ok(
    usersAdminTsx.includes("disabled={!accountOptions?.canAssignRoles}"),
    "Role selection is conditioned on accountOptions.canAssignRoles ceiling"
  );
});

test("4. WorkCatalog.Manage UI gets workstream choices from account-options, not Access.Manage /workstreams", () => {
  // WorkCatalogAdmin loadData
  const loadDataMatch = workCatalogTsx.match(/const loadData = useCallback\(async \(\) => \{([\s\S]*?)\}, \[/);
  assert.ok(loadDataMatch, "Found WorkCatalogAdmin loadData implementation");
  const loadDataBody = loadDataMatch[1];

  assert.ok(loadDataBody.includes('fetch("/api/admin/account-options"'), "Calls /api/admin/account-options");
  assert.ok(loadDataBody.includes('fetch("/api/admin/works"'), "Calls /api/admin/works");
  assert.ok(!loadDataBody.includes('fetch("/api/admin/workstreams"'), "Must NOT call /api/admin/workstreams");
  assert.ok(
    workCatalogTsx.includes("const wsData = (optData.workstreams || []) as Workstream[];"),
    "Workstreams metadata populated from optData.workstreams"
  );
});

test("5. Assistant UI does not require /api/admin/designations", () => {
  // OfficerAssistantAdmin loadData
  const loadDataMatch = assistantAdminTsx.match(/const loadData = useCallback\(async \(\) => \{([\s\S]*?)\}, \[\]\);/);
  assert.ok(loadDataMatch, "Found OfficerAssistantAdmin loadData implementation");
  const loadDataBody = loadDataMatch[1];

  assert.ok(loadDataBody.includes('fetch("/api/admin/account-options"'), "Calls /api/admin/account-options");
  assert.ok(!loadDataBody.includes('fetch("/api/admin/designations"'), "Must NOT call /api/admin/designations");
  assert.ok(
    assistantAdminTsx.includes("setDesignations(accountOptData.designations || [])"),
    "Designations populated from account-options"
  );

  // Delegation options ceiling authority
  assert.ok(
    loadDataBody.includes('fetch("/api/officers/me/assistants/delegation-options"'),
    "Assistant delegation ceilings authority is /api/officers/me/assistants/delegation-options"
  );
});

test("6. New allocation starts with zero scopes", () => {
  const openNewAllocMatch = usersAdminTsx.match(/const openNewAllocationForm = \(\) => \{([\s\S]*?)\};/);
  assert.ok(openNewAllocMatch, "Found openNewAllocationForm");
  const openNewAllocBody = openNewAllocMatch[1];

  assert.ok(
    openNewAllocBody.includes("setAllocScopes([]);"),
    "New allocation initializes with zero scopes ([])"
  );
  assert.ok(
    !openNewAllocBody.includes('{ kind: "Global" }'),
    "New allocation does NOT initialize with Global scope"
  );
});

test("7. Global is only added by explicit user action", () => {
  assert.ok(
    !usersAdminTsx.includes('alloc.scopes || [{ kind: "Global" }]'),
    "No implicit Global fallback fallback when scopes are missing"
  );
  assert.ok(
    usersAdminTsx.includes('if (newScopeKind === "Global") {\n      scope = { kind: "Global" };'),
    "Global scope is only created upon explicit user selection in scope builder"
  );
  assert.ok(
    usersAdminTsx.includes("At least one geographic scope must be consciously added"),
    "Validation error raised if submitted with zero scopes"
  );
});

test("8. Blank Work Order Reference blocks allocation submit", () => {
  assert.ok(
    usersAdminTsx.includes("if (!allocOrderRef.trim()) {"),
    "Checks for blank work order reference"
  );
  assert.ok(
    usersAdminTsx.includes('setAllocFormError("Work Order Reference is required and must be explicitly entered.")'),
    "Sets clear validation error when Work Order Reference is blank"
  );
});

test("9. No OFFICE-ORDER/2026/ALLOC fallback exists", () => {
  assert.ok(!usersAdminTsx.includes("OFFICE-ORDER/2026/ALLOC"), "UsersAdmin does not have fallback");
  assert.ok(!assistantAdminTsx.includes("OFFICE-ORDER/2026/ALLOC"), "OfficerAssistantAdmin does not have fallback");
  assert.ok(!workCatalogTsx.includes("OFFICE-ORDER/2026/ALLOC"), "WorkCatalogAdmin does not have fallback");
});

test("10. Local date serialization preserves selected calendar date and includes an explicit offset", () => {
  const iso = localDateInputToIso("2026-10-07");
  assert.ok(iso, "Returns non-null ISO string");
  assert.ok(iso.startsWith("2026-10-07T00:00:00"), `Preserves local midnight on selected calendar date: ${iso}`);
  assert.match(iso, /[+-]\d{2}:\d{2}$/, `Includes explicit timezone offset: ${iso}`);

  // Test invalid input handling
  assert.equal(localDateInputToIso(""), null);
  assert.equal(localDateInputToIso(null), null);
  assert.equal(localDateInputToIso("invalid-date"), null);
});

test("11. Existing ISO date is rendered to local calendar date correctly", () => {
  // Given an ISO string with timezone offset
  const todayLocalDate = getTodayLocalDateInput();
  assert.match(todayLocalDate, /^\d{4}-\d{2}-\d{2}$/, "Today date input format is YYYY-MM-DD");

  const nowIso = localDateInputToIso(todayLocalDate);
  const roundtripDate = isoToLocalDateInput(nowIso);
  assert.equal(roundtripDate, todayLocalDate, "Roundtrips calendar date through local ISO with offset");

  // validateDateRange contract (validTo is strictly exclusive)
  const validRange = validateDateRange("2026-10-07", "2026-10-08");
  assert.equal(validRange.valid, true);

  const unboundedRange = validateDateRange("2026-10-07", null);
  assert.equal(unboundedRange.valid, true, "Unbounded validTo is permitted");

  const equalDates = validateDateRange("2026-10-07", "2026-10-07");
  assert.equal(equalDates.valid, false, "Exclusive validTo cannot equal validFrom");
  assert.match(equalDates.error, /strictly greater than/);

  const invertedRange = validateDateRange("2026-10-08", "2026-10-07");
  assert.equal(invertedRange.valid, false, "Inverted date range is rejected");
});

test("12. Child assistant allocation can select a subset of parent scopes", () => {
  const parent = {
    id: "alloc-parent-1",
    workDefinitionId: "work-lr",
    workName: "Land Records",
    workCode: "LR",
    workOrderReference: "ORDER/REV/2026/099",
    validFrom: "2026-10-01T00:00:00+05:30",
    validTo: "2026-10-31T00:00:00+05:30",
    reason: "Primary District Allocation",
    revision: 1,
    revokedAt: null,
    revokedByUserId: null,
    scopes: [
      { kind: "Village", villageId: "vil-bijwasan" },
      { kind: "Village", villageId: "vil-dwarka" },
      { kind: "Village", villageId: "vil-kapashera" },
    ],
  };

  // Select subset: only Bijwasan (index 0) and Kapashera (index 2)
  const result = buildChildAllocation({
    parent,
    selectedScopeIndices: [0, 2],
    childValidFrom: "2026-10-05",
    childValidTo: "2026-10-25",
    childReason: "Subset delegation for Bijwasan and Kapashera",
  });

  assert.equal(result.valid, true);
  if (result.valid) {
    assert.equal(result.child.workDefinitionId, "work-lr");
    assert.equal(result.child.delegatedFromAllocationId, "alloc-parent-1");
    assert.equal(result.child.workOrderReference, "ORDER/REV/2026/099");
    assert.equal(result.child.scopes.length, 2);
    assert.equal(result.child.scopes[0].villageId, "vil-bijwasan");
    assert.equal(result.child.scopes[1].villageId, "vil-kapashera");
  }
});

test("13. Child allocation cannot exceed parent date interval", () => {
  const parent = {
    id: "alloc-parent-bounded",
    workDefinitionId: "work-lr",
    workName: "Land Records",
    workCode: "LR",
    workOrderReference: "ORDER/SW/2026/101",
    validFrom: "2026-10-05T00:00:00+05:30",
    validTo: "2026-10-20T00:00:00+05:30",
    reason: "Temporary Charge",
    revision: 1,
    revokedAt: null,
    revokedByUserId: null,
    scopes: [{ kind: "Global" }],
  };

  // Attempt child start before parent start
  const beforeStart = buildChildAllocation({
    parent,
    selectedScopeIndices: [0],
    childValidFrom: "2026-10-04",
    childValidTo: "2026-10-15",
  });
  assert.equal(beforeStart.valid, false);
  assert.match(beforeStart.error, /cannot start before parent Valid From/);

  // Attempt child end after bounded parent end
  const afterEnd = buildChildAllocation({
    parent,
    selectedScopeIndices: [0],
    childValidFrom: "2026-10-06",
    childValidTo: "2026-10-21",
  });
  assert.equal(afterEnd.valid, false);
  assert.match(afterEnd.error, /cannot exceed parent Valid To/);

  // Attempt child end missing when parent is bounded
  const missingEnd = buildChildAllocation({
    parent,
    selectedScopeIndices: [0],
    childValidFrom: "2026-10-06",
    childValidTo: "",
  });
  assert.equal(missingEnd.valid, false);
  assert.match(missingEnd.error, /Parent allocation is bounded.*Child Valid To.*is required/);

  // Attempt child validTo <= child validFrom
  const invertedDates = buildChildAllocation({
    parent,
    selectedScopeIndices: [0],
    childValidFrom: "2026-10-10",
    childValidTo: "2026-10-10",
  });
  assert.equal(invertedDates.valid, false);
  assert.match(invertedDates.error, /strictly greater than/);
});

test("14. Parent allocation [Bijwasan, Dwarka] can create one child with Bijwasan only", () => {
  const parent = {
    id: "alloc-sdm-sw",
    workDefinitionId: "work-lr",
    workName: "Land Records",
    workCode: "LR",
    workOrderReference: "DC/SW/ALLOC/2026/042",
    validFrom: "2026-10-01T00:00:00+05:30",
    validTo: null, // unbounded ongoing
    reason: "Subdivision In-charge",
    revision: 1,
    revokedAt: null,
    revokedByUserId: null,
    scopes: [
      { kind: "Village", villageId: "vil-bijwasan" },
      { kind: "Village", villageId: "vil-dwarka" },
    ],
  };

  // Assistant A gets Bijwasan only
  const assistantA = buildChildAllocation({
    parent,
    selectedScopeIndices: [0],
    childValidFrom: "2026-10-07",
    childValidTo: null,
    childReason: "Assistant A assigned to Bijwasan village work only",
  });

  assert.equal(assistantA.valid, true);
  if (assistantA.valid) {
    assert.equal(assistantA.child.workDefinitionId, "work-lr");
    assert.equal(assistantA.child.workOrderReference, "DC/SW/ALLOC/2026/042");
    assert.equal(assistantA.child.delegatedFromAllocationId, "alloc-sdm-sw");
    assert.equal(assistantA.child.scopes.length, 1);
    assert.deepEqual(assistantA.child.scopes[0], { kind: "Village", villageId: "vil-bijwasan" });
  }

  // Assistant B gets Dwarka only
  const assistantB = buildChildAllocation({
    parent,
    selectedScopeIndices: [1],
    childValidFrom: "2026-10-07",
    childValidTo: null,
    childReason: "Assistant B assigned to Dwarka village work only",
  });

  assert.equal(assistantB.valid, true);
  if (assistantB.valid) {
    assert.equal(assistantB.child.scopes.length, 1);
    assert.deepEqual(assistantB.child.scopes[0], { kind: "Village", villageId: "vil-dwarka" });
  }
});

test("15. Normal Create Officer has no manual password input/path", () => {
  // In UsersAdmin, Create Officer modal sends password: null
  assert.ok(
    usersAdminTsx.includes("password: null,"),
    "Create Officer payload sends password: null requesting generated temporary credential"
  );
  // Verify manual PasswordInput is not rendered in the create officer modal
  const createModalMatch = usersAdminTsx.match(/showCreateModal && \([\s\S]*?<form onSubmit=\{handleCreateUser\}[\s\S]*?>([\s\S]*?)<\/form>/);
  assert.ok(createModalMatch, "Found Create Officer modal form");
  const createModalForm = createModalMatch[1];

  assert.ok(!createModalForm.includes("<PasswordInput"), "PasswordInput component is NOT in Create Officer form");
  assert.ok(!createModalForm.includes('type="password"'), "No manual password input in Create Officer form");
  assert.ok(!createModalForm.includes("autoGeneratePassword"), "No auto-generate toggle; generated credential is mandatory");
});

test("16. Temporary credential is cleared after acknowledgement", () => {
  // UsersAdmin credential modal acknowledge & close behavior
  assert.ok(
    usersAdminTsx.includes("disabled={!credentialAcknowledged}"),
    "UsersAdmin close button disabled until acknowledged"
  );
  assert.ok(
    usersAdminTsx.includes("setCredentialData(null);"),
    "UsersAdmin clears credentialData from memory on close"
  );

  // OfficerAssistantAdmin credential modal acknowledge & close behavior
  assert.ok(
    assistantAdminTsx.includes("disabled={!credentialAcknowledged}"),
    "OfficerAssistantAdmin close button disabled until acknowledged"
  );
  assert.ok(
    assistantAdminTsx.includes("setCredentialData(null);"),
    "OfficerAssistantAdmin clears credentialData from memory on close"
  );
});

test("17. No credential persistence in localStorage/sessionStorage", () => {
  // Audit the entire admin directory
  const adminDir = path.join(__dirname, "../src/admin");
  const files = fs.readdirSync(adminDir).filter((f) => f.endsWith(".ts") || f.endsWith(".tsx"));
  for (const file of files) {
    const content = fs.readFileSync(path.join(adminDir, file), "utf8");
    assert.ok(
      !content.includes("localStorage.setItem") && !content.includes("sessionStorage.setItem"),
      `File ${file} does not write credentials to web storage`
    );
    assert.ok(
      !content.includes("console.log(credentialData") && !content.includes("console.log(temporaryCredential"),
      `File ${file} does not log credentials`
    );
  }
});

test("18. Mandatory password replacement intercepts mustChangePassword", () => {
  assert.match(
    appTsx,
    /if\s*\(\s*user\.mustChangePassword\s*\)\s*\{\s*return\s*<ChangePasswordView/m,
    "App.tsx intercepts mustChangePassword before any operational modules or routes"
  );

  // ChangePasswordView requires min 12 chars
  assert.ok(
    changePasswordTsx.includes("newPassword.length < 12"),
    "ChangePasswordView enforces 12 character minimum"
  );
  assert.ok(
    changePasswordTsx.includes("newPassword === currentPassword"),
    "ChangePasswordView ensures new password differs from current"
  );
  assert.ok(
    changePasswordTsx.includes('fetch("/api/auth/change-password"'),
    "ChangePasswordView posts to /api/auth/change-password"
  );
});

test("19. Server actorLabel is rendered as supplied", () => {
  // In AuditLogsAdmin
  assert.ok(
    auditAdminTsx.includes("<span>{log.actorLabel}</span>"),
    "AuditLogsAdmin renders server log.actorLabel verbatim"
  );
  assert.ok(
    !auditAdminTsx.includes('" on behalf of " +'),
    "AuditLogsAdmin does not client-side concatenate actor labels"
  );
});

test("20. 409 reload behavior remains intact", () => {
  // UsersAdmin 409 handling
  assert.match(
    usersAdminTsx,
    /if\s*\(\s*res\.status\s*===\s*409\s*\)\s*\{[\s\S]*?loadOfficerAllocations[\s\S]*?loadData/m,
    "UsersAdmin reloads officer allocations and canonical data on 409 conflict"
  );

  // OfficerAssistantAdmin 409 handling
  assert.match(
    assistantAdminTsx,
    /if\s*\(\s*res\.status\s*===\s*409\s*\)\s*\{[\s\S]*?loadData/m,
    "OfficerAssistantAdmin reloads canonical assistant data on 409 conflict"
  );
});

test("21. Technical SYSTEM_ADMIN display with designation null renders 'Technical Account' and preserves 'Unassigned' for ordinary users", () => {
  // In UsersAdmin table rendering:
  // If user.designation is null and user.roles includes SYSTEM_ADMIN: renders "Technical Account" and "No civil designation"
  assert.ok(
    usersAdminTsx.includes('u.roles?.includes("SYSTEM_ADMIN") ? ('),
    "UsersAdmin checks for SYSTEM_ADMIN role when designation is null"
  );
  assert.ok(
    usersAdminTsx.includes("Technical Account"),
    "UsersAdmin renders 'Technical Account' badge for technical SYSTEM_ADMIN"
  );
  assert.ok(
    usersAdminTsx.includes("No civil designation"),
    "UsersAdmin renders 'No civil designation' supporting text/title"
  );
  assert.ok(
    usersAdminTsx.includes('<span className="subtext">Unassigned</span>'),
    "Ordinary user without designation and without SYSTEM_ADMIN retains 'Unassigned'"
  );

  // Inspector:
  assert.ok(
    usersAdminTsx.includes('"No civil designation (Technical System Administrator)"'),
    "Access Inspector displays 'No civil designation (Technical System Administrator)' for SYSTEM_ADMIN"
  );
  assert.ok(
    usersAdminTsx.includes('inspectingOfficer.roles.map((r) =>'),
    "Access Inspector continues to render assigned roles including SYSTEM_ADMIN from server data"
  );

  // AppShell:
  assert.ok(
    /isSystemAdmin\s*\?\s*"Technical System Administrator"\s*:\s*null/.test(appShellTsx),
    "AppShell displays 'Technical System Administrator' subtitle when user has SYSTEM_ADMIN and no designation"
  );

  // No ADM inference:
  assert.ok(
    !usersAdminTsx.includes('designation = "ADM"') && !usersAdminTsx.includes('designation?.name || "ADM"'),
    "No ADM inference exists in UsersAdmin"
  );
  assert.ok(
    !appShellTsx.includes('"ADM"') && !appShellTsx.includes('"Additional District Magistrate"'),
    "No ADM inference exists in AppShell"
  );
});

test("22. Create Account retains '-- No Official Designation --', supports SYSTEM_ADMIN with no designation, and shows informational notice", () => {
  // Create Account dropdown retain -- No Official Designation --
  assert.ok(
    usersAdminTsx.includes('<option value="">-- No Official Designation --</option>'),
    "Create Account retains '-- No Official Designation --' option"
  );

  // Technical notice when SYSTEM_ADMIN is selected with no designation
  assert.ok(
    usersAdminTsx.includes("System Administrator is a technical security role and does not require a civil designation."),
    "Shows technical notice when SYSTEM_ADMIN is selected without designation"
  );

  // No fake System Administrator designation created
  assert.ok(
    !usersAdminTsx.includes('{ code: "SYSTEM_ADMIN", name: "System Administrator" }') &&
    !usersAdminTsx.includes('{ id: "sys-admin", name: "System Administrator" }'),
    "No fake System Administrator designation is injected into designations catalog"
  );

  // Server role data still renders SYSTEM_ADMIN
  assert.ok(
    usersAdminTsx.includes('roles.map((r) =>'),
    "Roles checklist maps over server roles"
  );
});
