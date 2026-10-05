import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const appTsx = fs.readFileSync(path.join(__dirname, "../src/App.tsx"), "utf8");
const matterDirTsx = fs.readFileSync(path.join(__dirname, "../src/matter/MatterDirectory.tsx"), "utf8");
const matterWsTsx = fs.readFileSync(path.join(__dirname, "../src/matter/MatterWorkspace.tsx"), "utf8");

test("1. Village Create Matter: Court identity manual inputs removed", () => {
  assert.ok(!appTsx.includes('courtCasePrefix: "W.P.(C)"'), "Legacy courtCasePrefix removed from form state");
  assert.ok(!appTsx.includes('courtCaseNumber: ""'), "Legacy courtCaseNumber removed from form state");
  assert.ok(!appTsx.includes("Case No. / Year"), "Manual case number input removed from Village create modal");
});

test("2. Primary Award optional on creation and clearable via contract", () => {
  assert.ok(!appTsx.includes("if (!selectedPrimaryAwardId) setSelectedPrimaryAwardId"), "App.tsx does not auto-set primary award on check");
  assert.ok(!matterDirTsx.includes("if (!selectedPrimaryAwardId) setSelectedPrimaryAwardId"), "MatterDirectory.tsx does not auto-set primary award on check");
  assert.ok(matterWsTsx.includes("handleClearPrimaryAward"), "MatterWorkspace has handleClearPrimaryAward helper");
  assert.ok(matterWsTsx.includes('"00000000-0000-0000-0000-000000000000"'), "MatterWorkspace sends GUID 00000000-... to clear primary award");
});

test("3. Legacy Khasra text is not a new-workflow input", () => {
  assert.ok(!matterDirTsx.includes("createKhasraRef"), "MatterDirectory does not have createKhasraRef editable state for new creation");
  assert.ok(matterDirTsx.includes("Legacy reference:"), "MatterDirectory labels legacy text as Legacy reference:");
  assert.ok(appTsx.includes("Legacy reference:"), "App.tsx labels legacy text as Legacy reference:");
  assert.ok(!appTsx.includes("Khasra {m.khasraReferenceText}"), "App.tsx does not label legacy text as simple Khasra");
  assert.ok(matterWsTsx.includes("khasraReferenceText: contextData.matter.khasraReferenceText"), "Metadata update preserves existing legacy khasra text");
});

test("4. WorkItem assignedUser displayName", () => {
  assert.ok(matterWsTsx.includes("assignedUser?: { userId: string; displayName: string }"), "WorkItem interface uses displayName");
  assert.ok(!matterWsTsx.includes("w.assignedUser.name"), "MatterWorkspace does not read w.assignedUser.name");
  assert.ok(matterWsTsx.includes("w.assignedUser.displayName"), "MatterWorkspace renders w.assignedUser.displayName");
});

test("5. Activity events contract & recent preview order", () => {
  assert.ok(matterWsTsx.includes("formatEventAction"), "MatterWorkspace formats raw event actions");
  assert.ok(matterWsTsx.includes("ev.actionByDisplayNameSnapshot"), "MatterWorkspace renders actionByDisplayNameSnapshot");
  assert.ok(!matterWsTsx.includes("ev.actionByUserName"), "MatterWorkspace does not read ev.actionByUserName");
  assert.ok(matterWsTsx.includes("b.sequenceNumber - a.sequenceNumber"), "Recent activity preview sorts by sequenceNumber descending for newest 5 events");
});

test("6. Workstream reclassification requires explicit selection", () => {
  assert.ok(!matterWsTsx.includes("data.workstreams[0].id"), "Reclassification does not default silently to workstreams[0]");
  assert.ok(matterWsTsx.includes("Select Target Workstream..."), "Reclassification select has explicit placeholder option");
});

test("7. Village matter cards avoid false No Award badge", () => {
  assert.ok(!appTsx.includes('<span className="matter-item-badge">No Award</span>'), "Village cards do not show false No Award badge");
});
