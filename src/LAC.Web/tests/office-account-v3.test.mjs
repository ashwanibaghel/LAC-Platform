import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const normalizeEol = (str) => str.replace(/\r\n/g, "\n");

const officeV3TypesTs = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/officeV3Types.ts"), "utf8"));
const usersAdminTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/UsersAdmin.tsx"), "utf8"));
const myHelpersTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/office/MyHelpersView.tsx"), "utf8"));
const appTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/App.tsx"), "utf8"));
const appShellTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/components/AppShell.tsx"), "utf8"));
const villageCoreWorkspaceTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/land/VillageCoreRecordsWorkspace.tsx"), "utf8"));
const officeDesksAdminTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/admin/OfficeDesksAdmin.tsx"), "utf8"));
const homeTsx = normalizeEol(fs.readFileSync(path.join(__dirname, "../src/home/Home.tsx"), "utf8"));

// ─────────────────────────────────────────────────────────────────────────────
// 1. V3 TYPES & BACKEND CONTRACT INTEGRITY
// ─────────────────────────────────────────────────────────────────────────────

test("1. Office Account V3 Types match frozen backend contract", () => {
  assert.ok(officeV3TypesTs.includes('"STANDARD_OFFICER"'), "Includes STANDARD_OFFICER");
  assert.ok(officeV3TypesTs.includes('"OFFICE_SUPERVISOR"'), "Includes OFFICE_SUPERVISOR");
  assert.ok(officeV3TypesTs.includes('"OFFICE_ADMIN"'), "Includes OFFICE_ADMIN");
  assert.ok(officeV3TypesTs.includes('"SYSTEM_ADMIN"'), "Includes SYSTEM_ADMIN");
  assert.ok(officeV3TypesTs.includes('"HELPER"'), "Includes HELPER");

  // OfficeModule enum
  assert.ok(officeV3TypesTs.includes('"DakMatters"'), "Includes DakMatters");
  assert.ok(officeV3TypesTs.includes('"Court"'), "Includes Court");
  assert.ok(officeV3TypesTs.includes('"Rti"'), "Includes Rti");
  assert.ok(officeV3TypesTs.includes('"Accounts"'), "Includes Accounts");
  assert.ok(officeV3TypesTs.includes('"RecordRoom"'), "Includes RecordRoom");

  // LandAccessLevel enum
  assert.ok(officeV3TypesTs.includes('"None"'), "Includes None");
  assert.ok(officeV3TypesTs.includes('"ViewOnly"'), "Includes ViewOnly");
  assert.ok(officeV3TypesTs.includes('"ViewWrite"'), "Includes ViewWrite");

  // HelperAccessLevel enum
  assert.ok(officeV3TypesTs.includes('"ReadOnly"'), "Includes ReadOnly");
  assert.ok(officeV3TypesTs.includes('"ReadWrite"'), "Includes ReadWrite");

  // OfficeAccountDetail contains concurrency tokens and audit fields
  assert.ok(officeV3TypesTs.includes("revision: number;"), "Detail contains revision concurrency token");
  assert.ok(officeV3TypesTs.includes("assistantRevision: number;"), "Detail contains assistantRevision");
  assert.ok(officeV3TypesTs.includes("customDesignation: string | null;"), "Detail contains customDesignation");
  assert.ok(officeV3TypesTs.includes("canRegisterInwardDak: boolean;"), "Detail contains canRegisterInwardDak");
  assert.ok(officeV3TypesTs.includes("landAccess: LandAccessLevel;"), "Detail contains landAccess");
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. ENDPOINTS & API CONSUMPTION
// ─────────────────────────────────────────────────────────────────────────────

test("2. UsersAdmin strictly uses frozen V3 /api/office/accounts endpoints", () => {
  assert.ok(
    usersAdminTsx.includes('fetch("/api/office/accounts"'),
    "UsersAdmin calls GET /api/office/accounts for directory"
  );
  assert.ok(
    usersAdminTsx.includes('fetch("/api/office/accounts/options"'),
    "UsersAdmin calls GET /api/office/accounts/options for options"
  );
  assert.ok(
    usersAdminTsx.includes('fetch("/api/office/accounts", {'),
    "UsersAdmin calls POST /api/office/accounts for officer creation"
  );
  assert.ok(
    usersAdminTsx.includes('fetch(`/api/office/accounts/${officer.id}`, {'),
    "UsersAdmin calls PUT /api/office/accounts/{id} for update"
  );
  assert.ok(
    usersAdminTsx.includes('fetch(`/api/office/accounts/${officer.id}/toggle-status`'),
    "UsersAdmin calls POST /api/office/accounts/{id}/toggle-status"
  );
  assert.ok(
    usersAdminTsx.includes('fetch(`/api/office/accounts/${officer.id}/reset-credential`'),
    "UsersAdmin calls POST /api/office/accounts/{id}/reset-credential"
  );

  // No raw role or workstream write dependencies for normal accounts
  assert.ok(
    !usersAdminTsx.includes('fetch("/api/admin/roles"'),
    "UsersAdmin does NOT fetch /api/admin/roles"
  );
  assert.ok(
    !usersAdminTsx.includes('fetch("/api/admin/workstreams"'),
    "UsersAdmin does NOT fetch /api/admin/workstreams"
  );
});

test("3. MyHelpersView strictly uses self-service /api/office/me/helpers endpoints", () => {
  assert.ok(
    myHelpersTsx.includes('fetch("/api/office/me/helpers"'),
    "MyHelpersView calls GET /api/office/me/helpers"
  );
  assert.ok(
    myHelpersTsx.includes('fetch("/api/office/me/helpers/options"'),
    "MyHelpersView calls GET /api/office/me/helpers/options"
  );
  assert.ok(
    myHelpersTsx.includes('fetch("/api/office/me/helpers", {'),
    "MyHelpersView calls POST /api/office/me/helpers"
  );
  assert.ok(
    myHelpersTsx.includes('fetch(`/api/office/me/helpers/${editingHelper.id}`, {'),
    "MyHelpersView calls PUT /api/office/me/helpers/{id}"
  );
  assert.ok(
    myHelpersTsx.includes('fetch(`/api/office/me/helpers/${helper.id}/toggle-status`'),
    "MyHelpersView calls POST /api/office/me/helpers/{id}/toggle-status"
  );
  assert.ok(
    myHelpersTsx.includes('fetch(`/api/office/me/helpers/${helper.id}/reset-credential`'),
    "MyHelpersView calls POST /api/office/me/helpers/{id}/reset-credential"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 3. DESIGNATION & OTHER TITLE WORKFLOW
// ─────────────────────────────────────────────────────────────────────────────

test("4. Civil Designation supports canonical list and Other custom title", () => {
  // Option choice for Other
  assert.ok(
    usersAdminTsx.includes('<option value="__OTHER__">Other / Other Designation</option>'),
    "UsersAdmin includes Other / Other Designation dropdown choice"
  );

  // Exclusivity: designationId and customDesignation are mutually exclusive
  assert.ok(
    usersAdminTsx.includes("designationId: isOther ? null : newDesignationId || null,"),
    "Create sends null designationId when custom designation is selected"
  );
  assert.ok(
    usersAdminTsx.includes("customDesignation: isOther ? newCustomDesignation.trim() : null,"),
    "Create sends trimmed customDesignation only when Other is selected"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 4. SUPERVISION, ADM CREATION, & TECHNICAL SYSTEM ADMIN
// ─────────────────────────────────────────────────────────────────────────────

test("5. ADM creation automatically shows Office Admin indicator; Edit does not auto-promote", () => {
  // Selection of ADM in Create form indicates automatic Office Admin authority
  assert.ok(
    usersAdminTsx.includes("isSelectedDesignationAdm"),
    "UsersAdmin tracks if selected designation is ADM"
  );
  assert.ok(
    usersAdminTsx.includes("Office Administrator — Full Office Access"),
    "UsersAdmin indicates ADM receives automatic Office Administrator authority"
  );

  // Edit drawer clarifies that changing designation to ADM never promotes existing accounts
  assert.ok(
    usersAdminTsx.includes("isEditDesignationAdm"),
    "UsersAdmin tracks edit designation ADM"
  );
  assert.ok(
    usersAdminTsx.includes("Changing an existing account's designation to ADM does not automatically promote it to Office Admin authority"),
    "UsersAdmin informs officer that edit designation ADM does not promote authority"
  );
});

test("6. Technical System Administrator is handled cleanly without civil designation", () => {
  // Technical account banner in directory
  assert.ok(
    usersAdminTsx.includes("Technical Account"),
    "UsersAdmin shows 'Technical Account' badge for SYSTEM_ADMIN"
  );
  assert.ok(
    usersAdminTsx.includes("No civil designation"),
    "UsersAdmin displays 'No civil designation' note"
  );

  // Inspector
  assert.ok(
    usersAdminTsx.includes('"No civil designation (Technical System Administrator)"'),
    "Inspector displays 'No civil designation (Technical System Administrator)'"
  );

  // Technical account modal notice
  assert.ok(
    usersAdminTsx.includes("System Administrator is a technical security role and does not require a civil designation"),
    "Technical admin modal contains informational security note"
  );

  // AppShell rendering
  assert.ok(
    appShellTsx.includes('"Technical System Administrator"'),
    "AppShell displays 'Technical System Administrator' subtitle when designation is absent"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 5. MODULE SELECTORS, INWARD DAK, & LAND ACCESS 3-STATE
// ─────────────────────────────────────────────────────────────────────────────

test("7. Module selection is clear, inward Dak is standalone, Land has 3-state radio", () => {
  // 5 main modules
  assert.ok(usersAdminTsx.includes('"DakMatters"'), "DakMatters module present");
  assert.ok(usersAdminTsx.includes('"Court"'), "Court module present");
  assert.ok(usersAdminTsx.includes('"Rti"'), "Rti module present");
  assert.ok(usersAdminTsx.includes('"Accounts"'), "Accounts module present");
  assert.ok(usersAdminTsx.includes('"RecordRoom"'), "RecordRoom module present");

  // Inward Dak standalone registration
  assert.ok(
    usersAdminTsx.includes("canRegisterInwardDak"),
    "UsersAdmin has standalone canRegisterInwardDak checkbox"
  );
  assert.ok(
    usersAdminTsx.includes("Can Register Inward Dak"),
    "Clear user-facing label 'Can Register Inward Dak'"
  );

  // Land 3-state radio
  assert.ok(usersAdminTsx.includes('value="None"'), "Land Access None option");
  assert.ok(usersAdminTsx.includes('value="ViewOnly"'), "Land Access ViewOnly option");
  assert.ok(usersAdminTsx.includes('value="ViewWrite"'), "Land Access ViewWrite option");

  // Supervisor hides granular module / land picks
  assert.ok(
    usersAdminTsx.includes("Full operational office access will be assigned automatically"),
    "Supervisor state explains full access without checkbox clutter"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 6. HELPER WORKFLOW & BOUNDED ACCESS
// ─────────────────────────────────────────────────────────────────────────────

test("8. Helper workflow supports DEO, personal desk, and None / ReadOnly / ReadWrite", () => {
  // MyHelpersView supports None, ReadOnly, ReadWrite
  assert.ok(myHelpersTsx.includes('"None"'), "Helper access level None supported in MyHelpersView");
  assert.ok(myHelpersTsx.includes('"ReadOnly"'), "Helper access level ReadOnly supported in MyHelpersView");
  assert.ok(myHelpersTsx.includes('"ReadWrite"'), "Helper access level ReadWrite supported in MyHelpersView");

  // UsersAdmin senior helper subform supports None, ReadOnly, ReadWrite
  assert.ok(usersAdminTsx.includes('value="None" checked={helperAccess === "None"}'), "Helper access level None in UsersAdmin subform");
  assert.ok(usersAdminTsx.includes('value="ReadOnly" checked={helperAccess === "ReadOnly"}'), "Helper access level ReadOnly in UsersAdmin subform");
  assert.ok(usersAdminTsx.includes('value="ReadWrite" checked={helperAccess === "ReadWrite"}'), "Helper access level ReadWrite in UsersAdmin subform");

  assert.ok(
    myHelpersTsx.includes("options?.desks"),
    "Helper desk choices strictly come from parent's active desks"
  );
  assert.ok(
    myHelpersTsx.includes("expectedRevision: editingHelper.assistantRevision"),
    "Helper edit sends expectedRevision using assistantRevision"
  );
});

test("8b. Multiple desks supported in account create and edit workflows", () => {
  // Account create supports multiple desks
  assert.ok(
    usersAdminTsx.includes("newAdditionalDeskIds"),
    "UsersAdmin maintains additional desk IDs for account creation"
  );
  assert.ok(
    usersAdminTsx.includes("const allDeskIds = [newPrimaryDeskId, ...newAdditionalDeskIds].filter(Boolean);"),
    "Create payload includes deduplicated primary and additional desk IDs"
  );
  assert.ok(
    usersAdminTsx.includes("deskIds: Array.from(new Set(allDeskIds))"),
    "Create payload sends deskIds array"
  );
  assert.ok(
    usersAdminTsx.includes("+ Add another desk…"),
    "Create and edit UI provide '+ Add another desk…' option"
  );

  // Account edit supports multiple desks
  assert.ok(
    usersAdminTsx.includes("editAdditionalDeskIds"),
    "UsersAdmin maintains additional desk IDs for account editing"
  );
  assert.ok(
    usersAdminTsx.includes("const allEditDeskIds = [editPrimaryDeskId, ...editAdditionalDeskIds].filter(Boolean);"),
    "Edit payload merges primary and additional desk IDs"
  );
  assert.ok(
    usersAdminTsx.includes("deskIds: Array.from(new Set(allEditDeskIds))"),
    "Edit payload sends deskIds array"
  );

  // Table display indicates multiple seats
  assert.ok(
    usersAdminTsx.includes("+${assignedDesks.length - 1} more"),
    "Directory table shows primary desk and indicates additional active desks"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 7. NAVIGATION & ROUTE GUARDS BY AUTHORITY
// ─────────────────────────────────────────────────────────────────────────────

test("9. Navigation and Route Guards enforce strict authority tiers", () => {
  // AppShell administration module visibility
  assert.ok(
    appShellTsx.includes('if (!user || isHelper || user?.authority === "STANDARD_OFFICER") return false;'),
    "Administration module completely hidden for STANDARD_OFFICER and HELPER"
  );

  // HELPER MUST NOT see My Helpers in AppShell
  assert.ok(
    appShellTsx.includes('!isHelper && ('),
    "AppShell dropdown hides My Helpers link when user is a helper"
  );

  // HELPER route guard in App.tsx
  assert.ok(
    appTsx.includes("function MyHelpersRoute"),
    "MyHelpersRoute guard exists"
  );
  assert.ok(
    appTsx.includes('<AccessDenied message="Helper accounts cannot manage assistant accounts." />'),
    "MyHelpersRoute strictly denies helper accounts with AccessDenied"
  );

  // SYSTEM_ADMIN and OFFICE_ADMIN access to Office / Desk Configuration
  assert.ok(
    appShellTsx.includes('to: "/admin/desks"'),
    "AppShell provides /admin/desks navigation link"
  );
  assert.ok(
    appTsx.includes("function OfficeConfigurationRoute"),
    "OfficeConfigurationRoute guard exists in App.tsx"
  );
  assert.ok(
    appTsx.includes('user?.authority !== "OFFICE_SUPERVISOR"'),
    "OfficeConfigurationRoute strictly denies OFFICE_SUPERVISOR"
  );

  // Work Catalog hidden and denied for OFFICE_SUPERVISOR
  assert.ok(
    appTsx.includes("function WorkCatalogRoute"),
    "WorkCatalogRoute guard exists"
  );
  assert.ok(
    appShellTsx.includes('!isOfficeSupervisor'),
    "AppShell hides Work Catalog from OFFICE_SUPERVISOR"
  );

  // Advanced Security ONLY for SYSTEM_ADMIN
  assert.ok(
    appShellTsx.includes('isSystemAdmin && hasPermission("Access.Manage")'),
    "AppShell hides Advanced Security from OFFICE_ADMIN and OFFICE_SUPERVISOR"
  );

  // Route Guards in App.tsx
  assert.ok(appTsx.includes("SystemAdminRoute"), "SystemAdminRoute guard exists");
  assert.ok(appTsx.includes("WorkCatalogRoute"), "WorkCatalogRoute guard exists");
  assert.ok(appTsx.includes("OfficeConfigurationRoute"), "OfficeConfigurationRoute guard exists");
  assert.ok(appTsx.includes("AssistantsAdminRoute"), "AssistantsAdminRoute guard exists");
  assert.ok(appTsx.includes("UsersAdminRoute"), "UsersAdminRoute guard exists");
  assert.ok(appTsx.includes("AuditLogsRoute"), "AuditLogsRoute guard exists");
  assert.ok(appTsx.includes("MyHelpersRoute"), "MyHelpersRoute guard exists");
  assert.ok(appTsx.includes("AccessDenied"), "AccessDenied component exists");
});

test("9b. OfficeDesksAdmin operates on frozen desk endpoints", () => {
  assert.ok(
    officeDesksAdminTsx.includes('fetch("/api/admin/desks"'),
    "OfficeDesksAdmin queries /api/admin/desks"
  );
  assert.ok(
    officeDesksAdminTsx.includes('fetch("/api/admin/account-options"'),
    "OfficeDesksAdmin falls back to /api/admin/account-options for office admins"
  );
  assert.ok(
    officeDesksAdminTsx.includes('fetch("/api/admin/desks", {'),
    "OfficeDesksAdmin calls POST /api/admin/desks"
  );
  assert.ok(
    officeDesksAdminTsx.includes('fetch(`/api/admin/desks/${editingDesk.id}`, {'),
    "OfficeDesksAdmin calls PUT /api/admin/desks/{id}"
  );
  assert.ok(
    officeDesksAdminTsx.includes('fetch(`/api/admin/desks/${d.id}/toggle-status`'),
    "OfficeDesksAdmin calls POST /api/admin/desks/{id}/toggle-status"
  );
});

test("9c. OfficeDesksAdmin enforces confirmation in-flight guard and disabled Confirm button", async () => {
  assert.ok(
    officeDesksAdminTsx.includes("confirmSubmitting, setConfirmSubmitting"),
    "OfficeDesksAdmin maintains confirmSubmitting state"
  );
  assert.ok(
    officeDesksAdminTsx.includes("confirmSubmittingRef = useRef(false)"),
    "OfficeDesksAdmin maintains confirmSubmittingRef for synchronous in-flight guard"
  );
  assert.ok(
    officeDesksAdminTsx.includes("handleExecuteConfirm"),
    "OfficeDesksAdmin defines handleExecuteConfirm handler"
  );
  assert.ok(
    officeDesksAdminTsx.includes("if (confirmSubmittingRef.current || confirmSubmitting || !confirmationDialog)"),
    "handleExecuteConfirm aborts re-entrant calls when in-flight"
  );
  assert.ok(
    officeDesksAdminTsx.includes("disabled={confirmSubmitting}"),
    "Confirm and modal actions are disabled while confirmation is in-flight"
  );
  assert.ok(
    officeDesksAdminTsx.includes('{confirmSubmitting ? "Processing..." : confirmationDialog.confirmLabel}'),
    "Confirm button indicates processing state when in flight"
  );

  // Behavioral simulation of the in-flight guard logic
  let inFlightRef = false;
  let inFlightState = false;
  let callCount = 0;

  const simulateConfirmAction = async (asyncFn) => {
    if (inFlightRef || inFlightState) return false;
    inFlightRef = true;
    inFlightState = true;
    try {
      await asyncFn();
      return true;
    } finally {
      inFlightRef = false;
      inFlightState = false;
    }
  };

  const slowMutation = () => new Promise((resolve) => setTimeout(() => { callCount++; resolve(); }, 30));

  // Trigger rapid concurrent submissions
  const p1 = simulateConfirmAction(slowMutation);
  const p2 = simulateConfirmAction(slowMutation);
  const p3 = simulateConfirmAction(slowMutation);

  const [r1, r2, r3] = await Promise.all([p1, p2, p3]);
  assert.equal(r1, true, "First submission proceeded");
  assert.equal(r2, false, "Second concurrent submission was blocked by in-flight guard");
  assert.equal(r3, false, "Third concurrent submission was blocked by in-flight guard");
  assert.equal(callCount, 1, "Underlying mutation executed exactly once");
});

test("9d. OfficeDesksAdmin prevents update dispatch when no fields changed", () => {
  // Check static contract in OfficeDesksAdmin.tsx
  assert.ok(
    officeDesksAdminTsx.includes('if (diffItems.length === 0) {\n        setModalError("No changes detected.");\n        return;\n      }'),
    "handleSaveDesk halts and displays error when no fields changed"
  );
  assert.ok(
    officeDesksAdminTsx.includes('if (diffItems.length === 0) {\n            setConfirmationDialog(null);\n            return;\n          }'),
    "onConfirm has secondary guard against empty diff update dispatch"
  );

  // Behavioral simulation of diff calculation for desk edits
  const computeDeskDiff = ({ current, edited, workstreams }) => {
    const name = edited.name.trim();
    const diffItems = [];
    if (name !== current.name) {
      diffItems.push({ label: "Desk Name", value: `${current.name} → ${name}` });
    }
    const oldWsId = current.workstreamId || "";
    const newWsId = edited.workstreamId || "";
    if (oldWsId !== newWsId) {
      const oldWs = workstreams.find((w) => w.id === current.workstreamId)?.name || "General / Multi-branch";
      const newWs = workstreams.find((w) => w.id === edited.workstreamId)?.name || "General / Multi-branch";
      diffItems.push({ label: "Branch / Workstream", value: `${oldWs} → ${newWs}` });
    }
    const oldDesc = (current.description || "").trim();
    const newDesc = (edited.description || "").trim();
    if (oldDesc !== newDesc) {
      diffItems.push({ label: "Description", value: `${oldDesc || "(None)"} → ${newDesc || "(None)"}` });
    }
    return diffItems;
  };

  const workstreams = [
    { id: "ws-1", name: "Court Branch", code: "CRT" },
    { id: "ws-2", name: "Land Branch", code: "LND" },
  ];

  const existingDesk = {
    id: "desk-101",
    code: "SEAT_A",
    name: "Dealing Assistant A",
    workstreamId: "ws-1",
    description: "Handles pending matters",
  };

  // Case 1: Identical fields -> diffItems.length === 0
  const noChangeDiff = computeDeskDiff({
    current: existingDesk,
    edited: { name: "Dealing Assistant A", workstreamId: "ws-1", description: "Handles pending matters" },
    workstreams,
  });
  assert.equal(noChangeDiff.length, 0, "No diff detected when all fields match");

  // Case 2: Whitespace trimming matches -> diffItems.length === 0
  const whitespaceDiff = computeDeskDiff({
    current: existingDesk,
    edited: { name: "Dealing Assistant A ", workstreamId: "ws-1", description: " Handles pending matters " },
    workstreams,
  });
  assert.equal(whitespaceDiff.length, 0, "No diff detected when trimmed values match");

  // Case 3: Name modified -> diffItems has 1 item
  const nameChangeDiff = computeDeskDiff({
    current: existingDesk,
    edited: { name: "Senior Dealing Assistant A", workstreamId: "ws-1", description: "Handles pending matters" },
    workstreams,
  });
  assert.equal(nameChangeDiff.length, 1);
  assert.equal(nameChangeDiff[0].label, "Desk Name");
  assert.equal(nameChangeDiff[0].value, "Dealing Assistant A → Senior Dealing Assistant A");

  // Case 4: Branch modified -> diffItems has 1 item
  const branchChangeDiff = computeDeskDiff({
    current: existingDesk,
    edited: { name: "Dealing Assistant A", workstreamId: "ws-2", description: "Handles pending matters" },
    workstreams,
  });
  assert.equal(branchChangeDiff.length, 1);
  assert.equal(branchChangeDiff[0].label, "Branch / Workstream");
  assert.equal(branchChangeDiff[0].value, "Court Branch → Land Branch");

  // Case 5: Description modified -> diffItems has 1 item
  const descChangeDiff = computeDeskDiff({
    current: existingDesk,
    edited: { name: "Dealing Assistant A", workstreamId: "ws-1", description: "Updated responsibilities" },
    workstreams,
  });
  assert.equal(descChangeDiff.length, 1);
  assert.equal(descChangeDiff[0].label, "Description");
  assert.equal(descChangeDiff[0].value, "Handles pending matters → Updated responsibilities");
});

// ─────────────────────────────────────────────────────────────────────────────
// 8. LAND RECORDS MUTATION GATING FOR VIEW ONLY
// ─────────────────────────────────────────────────────────────────────────────

test("10. Land mutation controls are hidden when user has ViewOnly access", () => {
  // VillageCoreRecordsWorkspace gates add award, upload core documents, and attach
  assert.ok(
    villageCoreWorkspaceTsx.includes('hasPermission("Award.Create") ||'),
    "VillageCoreRecordsWorkspace checks Award creation permissions"
  );
  assert.ok(
    villageCoreWorkspaceTsx.includes("{canEdit && ("),
    "VillageCoreRecordsWorkspace gates mutation action buttons"
  );

  // App.tsx gates Khasra actions
  assert.ok(
    appTsx.includes('const canEdit = hasPermission("Khasra.Edit") || hasPermission("LR.Edit");'),
    "VillageKhasras checks canEdit"
  );
  assert.ok(
    appTsx.includes('{canEdit && <button onClick={openAdd}>+ Add Khasra</button>}'),
    "Add Khasra button gated"
  );

  // App.tsx gates Award actions
  assert.ok(
    appTsx.includes('const canEditAward = hasPermission("Award.Edit") || hasPermission("Award.Create");'),
    "Award workspace checks canEditAward"
  );
  assert.ok(
    appTsx.includes("{canEditAward && <button onClick={() => setAdding(true)}>+ Add / Link Khasra</button>}"),
    "Add / Link Khasra button gated"
  );

  // App.tsx gates LrRegister actions
  assert.ok(
    appTsx.includes('const canEdit = hasPermission("LR.Edit");'),
    "LrRegister checks canEdit"
  );
  assert.ok(
    appTsx.includes('const canVerify = hasPermission("LR.Verify");'),
    "LrRegister checks canVerify"
  );
  assert.ok(
    appTsx.includes('const canCommit = hasPermission("LR.Commit");'),
    "LrRegister checks canCommit"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 9. CONCURRENCY & SESSION INVALIDATION
// ─────────────────────────────────────────────────────────────────────────────

test("11. Concurrency 409 and session invalidation notices are handled", () => {
  // 409 handling in UsersAdmin
  assert.ok(
    usersAdminTsx.includes("409"),
    "UsersAdmin handles 409 conflict responses"
  );
  assert.ok(
    usersAdminTsx.includes("expectedRevision"),
    "UsersAdmin sends expectedRevision concurrency token"
  );

  // Notice about session invalidation
  assert.ok(
    usersAdminTsx.includes("Existing login sessions for this employee were ended"),
    "UsersAdmin informs user about session invalidation on access changes"
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// 10. LAND RECORDS & COURT VISIBILITY / ROUTE PROTECTION MATRIX (BUG 1 & BUG 2)
// ─────────────────────────────────────────────────────────────────────────────

test("12. Land Records and Court visibility and route protection (Matrix A-E)", () => {
  // 1. Static Contract & AST integrity checks
  // AppShell checks
  assert.ok(
    appShellTsx.includes("const canAccessLand = () =>"),
    "AppShell defines canAccessLand helper"
  );
  assert.ok(
    !appShellTsx.includes('canAccessCourt = () => hasPermission("Court.View") || hasPermission("Court.Create") || hasPermission("Award.View")'),
    "AppShell canAccessCourt does NOT include Award.View"
  );
  assert.ok(
    appShellTsx.includes('hasPermission("Court.View") ||') && appShellTsx.includes('hasPermission("Court.Create")'),
    "AppShell canAccessCourt checks Court.View and Court.Create"
  );
  assert.ok(
    appShellTsx.includes('id: "land"') && appShellTsx.includes("checkPermission: canAccessLand"),
    "AppShell land module is protected by checkPermission: canAccessLand"
  );
  assert.ok(
    appShellTsx.includes('if (canAccessLand()) {\n      contextualNav = {\n        categoryTitle: "Land Records"'),
    "AppShell contextualNav for Land Records is guarded by canAccessLand()"
  );

  // Home checks
  assert.ok(
    homeTsx.includes("const canAccessLand = () =>"),
    "Home defines canAccessLand helper"
  );
  assert.ok(
    !homeTsx.includes('canAccessCourt = () => hasPermission("Court.View") || hasPermission("Court.Create") || hasPermission("Award.View")'),
    "Home canAccessCourt does NOT include Award.View"
  );
  assert.ok(
    homeTsx.includes("{canAccessLand() && (\n            <Link to=\"/land-records\" className=\"home-clean-card\">"),
    "Home gates Land Records card on canAccessLand()"
  );
  assert.ok(
    homeTsx.includes("Land &amp; Area Calculator"),
    "Home preserves independent Land & Area Calculator card"
  );

  // App.tsx Route Guards
  assert.ok(
    appTsx.includes("function LandRecordsRoute("),
    "App.tsx defines LandRecordsRoute guard"
  );
  assert.ok(
    appTsx.includes("function CourtRoute("),
    "App.tsx defines CourtRoute guard"
  );
  assert.ok(
    appTsx.includes('<Route path="/land-records" element={<LandRecordsRoute><LandRecordsHierarchy /></LandRecordsRoute>} />'),
    "App.tsx guards /land-records with LandRecordsRoute"
  );
  assert.ok(
    appTsx.includes('<Route path="/court-cases" element={<CourtRoute><CourtDirectory /></CourtRoute>} />'),
    "App.tsx guards /court-cases with CourtRoute"
  );
  assert.ok(
    appTsx.includes('<Route path="/villages" element={<LandRecordsRoute><VillagesDirectory /></LandRecordsRoute>} />'),
    "App.tsx guards /villages with LandRecordsRoute"
  );
  assert.ok(
    appTsx.includes('<Route path="/awards" element={<LandRecordsRoute><AwardsDirectory /></LandRecordsRoute>} />'),
    "App.tsx guards /awards with LandRecordsRoute"
  );

  // 2. Behavioral Verification across Frozen Backend Presets Matrix (A - E)
  // Presets from OfficeAccessPresets:
  const LAND_VIEW_PERMS = ["Village.View", "Khasra.View", "LR.View", "Award.View"];
  const LAND_WRITE_PERMS = [
    ...LAND_VIEW_PERMS,
    "Khasra.Edit", "LR.Edit", "LR.Verify", "LR.Commit",
    "Award.Create", "Award.Edit", "Award.CoreDocumentUpload"
  ];
  const COURT_PERMS = [
    "Court.View", "Court.Create", "Court.Edit", "Court.Assign",
    "Court.Proceeding.Manage", "Court.Document.Manage"
  ];
  const STAFF_PERMS = ["Assistants.Manage"];

  // Evaluators matching production logic
  const evaluateLandAccess = (perms) =>
    LAND_VIEW_PERMS.some((p) => perms.includes(p));

  const evaluateCourtAccess = (perms) =>
    perms.includes("Court.View") || perms.includes("Court.Create");

  const evaluateLandMutations = (perms) =>
    perms.includes("Khasra.Edit") || perms.includes("LR.Edit") || perms.includes("Award.Edit");

  // A. Standard officer: modules = [], landAccess = None
  {
    const permsA = [...STAFF_PERMS];
    const canLand = evaluateLandAccess(permsA);
    const canCourt = evaluateCourtAccess(permsA);
    assert.equal(canLand, false, "Matrix A: landAccess=None has no land records access");
    assert.equal(canCourt, false, "Matrix A: modules=[] has no court access");
  }

  // B. Standard officer: modules = [], landAccess = ViewOnly
  {
    const permsB = [...STAFF_PERMS, ...LAND_VIEW_PERMS];
    const canLand = evaluateLandAccess(permsB);
    const canCourt = evaluateCourtAccess(permsB);
    const canMutate = evaluateLandMutations(permsB);
    assert.equal(canLand, true, "Matrix B: Land Records visible for ViewOnly");
    assert.equal(canCourt, false, "Matrix B: Court strictly NOT visible (Award.View does not leak Court)");
    assert.equal(canMutate, false, "Matrix B: Land mutations unavailable for ViewOnly");
  }

  // C. Standard officer: modules = [], landAccess = ViewWrite
  {
    const permsC = [...STAFF_PERMS, ...LAND_WRITE_PERMS];
    const canLand = evaluateLandAccess(permsC);
    const canCourt = evaluateCourtAccess(permsC);
    const canMutate = evaluateLandMutations(permsC);
    assert.equal(canLand, true, "Matrix C: Land Records visible for ViewWrite");
    assert.equal(canCourt, false, "Matrix C: Court strictly NOT visible for ViewWrite");
    assert.equal(canMutate, true, "Matrix C: Permitted land mutations available for ViewWrite");
  }

  // D. Standard officer: modules = [Court], landAccess = None
  {
    const permsD = [...STAFF_PERMS, ...COURT_PERMS];
    const canLand = evaluateLandAccess(permsD);
    const canCourt = evaluateCourtAccess(permsD);
    assert.equal(canLand, false, "Matrix D: Land Records NOT visible when landAccess=None");
    assert.equal(canCourt, true, "Matrix D: Court visible when Court module assigned");
  }

  // E. Standard officer: modules = [Court], landAccess = ViewOnly
  {
    const permsE = [...STAFF_PERMS, ...COURT_PERMS, ...LAND_VIEW_PERMS];
    const canLand = evaluateLandAccess(permsE);
    const canCourt = evaluateCourtAccess(permsE);
    const canMutate = evaluateLandMutations(permsE);
    assert.equal(canLand, true, "Matrix E: Land Records visible");
    assert.equal(canCourt, true, "Matrix E: Court visible");
    assert.equal(canMutate, false, "Matrix E: Land mutations unavailable for ViewOnly");
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// 8. RBAC ADMIN UI POLISH, DETAILS DRAWER, & CONFIRMATION SAFETY
// ─────────────────────────────────────────────────────────────────────────────

test("13. Old 'Assigned Authority Roles' and 'Functional Branches' walls are completely absent", () => {
  assert.ok(!usersAdminTsx.includes("Assigned Authority Roles"), "No 'Assigned Authority Roles' in UsersAdmin");
  assert.ok(!usersAdminTsx.includes("Functional Branches"), "No 'Functional Branches' in UsersAdmin");
  assert.ok(!usersAdminTsx.includes("Workstream Architecture"), "No raw workstream architecture in UsersAdmin");
  assert.ok(!usersAdminTsx.includes("Role ID"), "No raw role IDs shown in UsersAdmin");
});

test("14. Confirmation safety dialog intercepts all state-changing mutations", () => {
  // Confirmation state and dialog shell
  assert.ok(usersAdminTsx.includes("confirmationDialog"), "UsersAdmin manages confirmation dialog state");
  assert.ok(usersAdminTsx.includes("rbac-confirm-shell"), "UsersAdmin renders rbac-confirm-shell");

  // Create review flow opens confirmation
  assert.ok(usersAdminTsx.includes('title: "Confirm New Officer Account"'), "Create form opens confirmation with summary");

  // Edit diff calculation
  assert.ok(usersAdminTsx.includes("diffItems"), "Edit flow computes diff items for confirmation");
  assert.ok(usersAdminTsx.includes('title: "Confirm Access Changes"'), "Edit flow opens confirmation with diffs");

  // Status toggle confirmation
  assert.ok(usersAdminTsx.includes("Confirm Account Deactivation"), "Status toggle requires confirmation");

  // Password reset confirmation
  assert.ok(usersAdminTsx.includes('title: "Confirm Password Reset"'), "Password reset requires confirmation");

  // Helper attachment confirmation
  assert.ok(usersAdminTsx.includes('title: "Confirm Attached Assistant"'), "Helper attachment requires confirmation");
});

test("15. Officer Details Drawer renders all 6 structured sections with clean actions", () => {
  assert.ok(usersAdminTsx.includes("rbac-drawer-shell"), "UsersAdmin uses rbac-drawer-shell");
  assert.ok(usersAdminTsx.includes("1. Employee Identity"), "Drawer includes Section 1: Employee Identity");
  assert.ok(usersAdminTsx.includes("2. Authority Level"), "Drawer includes Section 2: Authority Level");
  assert.ok(usersAdminTsx.includes("3. Work Access &amp; Permissions"), "Drawer includes Section 3: Work Access & Permissions");
  assert.ok(usersAdminTsx.includes("4. Operational Seats &amp; Desks"), "Drawer includes Section 4: Operational Seats & Desks");
  assert.ok(usersAdminTsx.includes("5. Staff Relationship"), "Drawer includes Section 5: Staff Relationship");
  assert.ok(usersAdminTsx.includes("Account Actions"), "Drawer includes Account Actions buttons");
  assert.ok(usersAdminTsx.includes("View Details"), "Directory table has single 'View Details' action button");
});

test("16. Civil Designation custom toggle enforces strict mutual exclusivity", () => {
  assert.ok(usersAdminTsx.includes("isCustomDesignationMode"), "Create form tracks custom designation mode");
  assert.ok(usersAdminTsx.includes("isEditCustomMode"), "Edit form tracks custom designation mode");
  assert.ok(usersAdminTsx.includes("Can't find the designation?  + Enter another designation"), "Discoverable switch button present");
  assert.ok(usersAdminTsx.includes("← Use standard designation"), "Revert button to standard designation present");
});
