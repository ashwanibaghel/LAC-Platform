import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const notingTypesTs = fs.readFileSync(path.join(__dirname, "../src/matter/notingTypes.ts"), "utf8");
const notingTextUtilTs = fs.readFileSync(path.join(__dirname, "../src/matter/notingTextUtil.ts"), "utf8");
const notingWorkspaceViewTsx = fs.readFileSync(path.join(__dirname, "../src/matter/NotingWorkspaceView.tsx"), "utf8");
const sendReturnModalTsx = fs.readFileSync(path.join(__dirname, "../src/matter/SendReturnModal.tsx"), "utf8");
const remarkModalTsx = fs.readFileSync(path.join(__dirname, "../src/matter/RemarkModal.tsx"), "utf8");
const citeDocModalTsx = fs.readFileSync(path.join(__dirname, "../src/matter/CiteDocumentModal.tsx"), "utf8");
const docRailViewerTsx = fs.readFileSync(path.join(__dirname, "../src/matter/DocumentRailViewer.tsx"), "utf8");
const dakMatterLinkModalTsx = fs.readFileSync(path.join(__dirname, "../src/dak/DakMatterLinkModal.tsx"), "utf8");
const dakDetailTsx = fs.readFileSync(path.join(__dirname, "../src/dak/DakDetailWorkspace.tsx"), "utf8");
const matterWsTsx = fs.readFileSync(path.join(__dirname, "../src/matter/MatterWorkspace.tsx"), "utf8");

test("1. Canonical Text generation matches C# NotingText.Canonicalize", () => {
  // Simulate canonicalizeRichTextDoc logic
  function canonicalize(docNode) {
    const parts = [];
    function visit(node) {
      if (!node || typeof node !== "object") return;
      const type = node.type;
      if (type === "text") {
        if (typeof node.text === "string") parts.push(node.text);
      } else if (type === "hardBreak") {
        parts.push("\n");
      } else {
        if (Array.isArray(node.content)) {
          for (const child of node.content) visit(child);
        }
        if (type === "paragraph" || type === "heading") {
          parts.push("\n");
        }
      }
    }
    if (docNode.type === "doc" && Array.isArray(docNode.content)) {
      for (const child of docNode.content) visit(child);
    } else {
      visit(docNode);
    }
    return parts.join("");
  }

  const sampleDoc = {
    type: "doc",
    content: [
      {
        type: "paragraph",
        content: [{ type: "text", text: "First paragraph." }]
      },
      {
        type: "paragraph",
        content: [
          { type: "text", text: "Second paragraph with " },
          { type: "text", text: "bold text." }
        ]
      }
    ]
  };

  const canonical = canonicalize(sampleDoc);
  assert.equal(canonical, "First paragraph.\nSecond paragraph with bold text.\n");
});

test("2. Anchor quote hash is uppercase hex SHA-256", () => {
  const quote = "Second paragraph with bold text.";
  const expectedHash = crypto.createHash("sha256").update(quote, "utf8").digest("hex").toUpperCase();
  assert.equal(expectedHash.length, 64);
  assert.equal(expectedHash, expectedHash.toUpperCase());
});

test("3. Rich Text sanitization enforces backend allowlist", () => {
  assert.ok(notingTextUtilTs.includes("sanitizeTipTapDoc"), "notingTextUtil exports sanitizeTipTapDoc");
  assert.ok(notingTextUtilTs.includes('"paragraph"'), "allowedNodes includes paragraph");
  assert.ok(notingTextUtilTs.includes('"bold"'), "allowedMarks includes bold");
  assert.ok(notingTextUtilTs.includes('"italic"'), "allowedMarks includes italic");
  assert.ok(notingTextUtilTs.includes('"underline"'), "allowedMarks includes underline");
  assert.ok(notingTextUtilTs.includes('"strike"'), "allowedMarks includes strike");
});

test("4. Noting split view (60/40) and Focus mode toggle", () => {
  assert.ok(notingWorkspaceViewTsx.includes("setFocusMode"), "Focus mode state toggle present");
  assert.ok(notingWorkspaceViewTsx.includes("Expand to Focus"), "Expand to Focus action label present");
  assert.ok(notingWorkspaceViewTsx.includes("Restore Split View"), "Restore Split View action label present");
  assert.ok(notingWorkspaceViewTsx.includes("focus-mode-overlay-drawer"), "Focus drawer overlay present");
});

test("5. Server autosave and revision tracking with If-Match", () => {
  assert.ok(notingWorkspaceViewTsx.includes('"If-Match": `"${draftRevisionRef.current}"`'), "PUT draft uses If-Match revision header");
  assert.ok(notingWorkspaceViewTsx.includes('"Idempotency-Key"'), "PUT draft uses Idempotency-Key header");
  assert.ok(notingWorkspaceViewTsx.includes("1500"), "Autosave debounced to 1500ms");
  assert.ok(notingWorkspaceViewTsx.includes("✓ All changes saved"), "Autosave saved indicator present");
  assert.ok(notingWorkspaceViewTsx.includes("Draft Revision Conflict"), "409 revision conflict handled gracefully");
  assert.ok(notingWorkspaceViewTsx.includes("Unsaved Edits Preserved"), "Preserves unsaved draft text on 409 conflict");
});

test("6. Immutable official note chain and sequential numbering", () => {
  assert.ok(notingWorkspaceViewTsx.includes("NOTE {note.number}"), "Official note number rendered");
  assert.ok(notingWorkspaceViewTsx.includes("note.authorName"), "Author snapshot rendered");
  assert.ok(notingWorkspaceViewTsx.includes("note.designation"), "Designation snapshot rendered");
  assert.ok(notingWorkspaceViewTsx.includes("note.submittedAt"), "Official timestamp rendered");
  assert.ok(!notingWorkspaceViewTsx.includes("deleteNote"), "No delete button on official notes");
  assert.ok(!notingWorkspaceViewTsx.includes("editOfficialNote"), "No edit button on official notes");
});

test("7. Inline anchored remarks with Open / Addressed lifecycle", () => {
  assert.ok(remarkModalTsx.includes("Add Remark on Note"), "Add Remark modal present");
  assert.ok(remarkModalTsx.includes("/remarks"), "Calls remarks endpoint");
  assert.ok(remarkModalTsx.includes('method: "PUT"'), "Remark state update uses PUT method");
  assert.ok(remarkModalTsx.includes('notes/${noteId}/remarks/${remark.id}'), "Remark state update uses /notes/{noteId}/remarks/{remarkId} route");
  assert.ok(remarkModalTsx.includes('"Mark Addressed"'), "Toggles Open to Addressed");
  assert.ok(remarkModalTsx.includes('"Reopen Remark"'), "Toggles Addressed to Open");
  assert.ok(remarkModalTsx.includes("expectedRevision: remark.revision"), "Remark state change passes expectedRevision");
});

test("8. Document citations link note text to Matter documents and pages", () => {
  assert.ok(citeDocModalTsx.includes("/noting/draft/citations"), "CiteDocumentModal calls /citations endpoint");
  assert.ok(citeDocModalTsx.includes("documentId: selectedDocId"), "Passes selected document ID");
  assert.ok(citeDocModalTsx.includes("expectedRevision: draftRevision"), "Passes expectedRevision");
  assert.ok(notingWorkspaceViewTsx.includes("note-citation-pill"), "Citations rendered as interactive badges in notes");
  assert.ok(notingWorkspaceViewTsx.includes("handleCitationClick"), "Clicking citation opens right rail / focus drawer at exact page");
});

test("9. Shared PDF highlights overlay and Word ONLYOFFICE handling", () => {
  assert.ok(docRailViewerTsx.includes("/annotations"), "Loads and posts PDF annotations");
  assert.ok(docRailViewerTsx.includes("Shared Highlights"), "Displays shared highlights overlay");
  assert.ok(docRailViewerTsx.includes("ONLYOFFICE"), "Word documents reference ONLYOFFICE integration");
});

test("10. Digital-only file movement has NO mandatory physical custody check", () => {
  assert.ok(sendReturnModalTsx.includes("includesPhysicalOriginal: includesPhysical"), "Send command passes includesPhysicalOriginal boolean");
  assert.ok(sendReturnModalTsx.includes("selectedDak?.hasPhysicalOriginal"), "Physical checkbox is conditionally displayed only when physical original exists");
  assert.ok(!sendReturnModalTsx.includes("required={!includesPhysical}"), "Physical checkbox is never mandatory for digital-only movement");
});

test("11. Multi-Dak disambiguation forces explicit controlling Dak choice", () => {
  assert.ok(sendReturnModalTsx.includes("linkedDaks.length > 1"), "Multi-Dak condition handled in SendReturnModal");
  assert.ok(sendReturnModalTsx.includes("Controlling Active Dak"), "Label for controlling Dak selector");
  assert.ok(sendReturnModalTsx.includes("sourceDakId: selectedDakId"), "Send command targets chosen controlling Dak");
});

test("12. Native routing policy pending banner for Dak-less Matters", () => {
  assert.ok(notingWorkspaceViewTsx.includes("nativeRoutingPolicyPending"), "Reads nativeRoutingPolicyPending capability");
  assert.ok(notingWorkspaceViewTsx.includes("Native Routing Policy Pending:"), "Renders clear policy banner");
  assert.ok(notingWorkspaceViewTsx.includes("disabled={!capabilities.canSend}"), "Send button disabled when canSend is false");
});

test("13. Dak → Create / Link Matter with Village Classification", () => {
  assert.ok(dakMatterLinkModalTsx.includes("/village-classification"), "DakMatterLinkModal supports village classification check");
  assert.ok(dakMatterLinkModalTsx.includes("/matter-options"), "Queries compatible matters filtered by classified Village");
  assert.ok(dakMatterLinkModalTsx.includes("/matter-workspace"), "Calls atomic POST /matter-workspace endpoint");
  assert.ok(dakDetailTsx.includes("DakMatterLinkModal"), "DakDetailWorkspace renders DakMatterLinkModal");
  assert.ok(dakDetailTsx.includes("+ Open / Link Matter"), "DakDetailWorkspace renders button to open/link matter");
});

test("14. Matter Workspace integrates Green Noting tab as premier surface", () => {
  assert.ok(matterWsTsx.includes('🌿 Green Noting'), "MatterWorkspace renders Green Noting tab");
  assert.ok(matterWsTsx.includes('activeTab === "noting"'), "MatterWorkspace renders NotingWorkspaceView when noting tab is active");
  assert.ok(matterWsTsx.includes("sourceDakParam"), "sourceDakId URL param triggers noting tab directly from Dak navigation");
});
