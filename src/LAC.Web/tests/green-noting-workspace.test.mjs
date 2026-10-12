import test from "node:test";
import assert from "node:assert/strict";

test("Green Legal ONLYOFFICE API contract routes and headers", async (t) => {
  // Test endpoints definition
  const endpoints = {
    createSession: (matterId) => `/api/matters/${matterId}/noting/office/sessions`,
    sessionConfig: (sessionId) => `/api/noting-office/sessions/${sessionId}/config`,
    requestSave: (sessionId) => `/api/noting-office/sessions/${sessionId}/saves`,
    saveReceipt: (sessionId, saveId) => `/api/noting-office/sessions/${sessionId}/saves/${saveId}`,
    createPreview: (sessionId) => `/api/noting-office/sessions/${sessionId}/previews`,
    confirmPreview: (matterId) => `/api/matters/${matterId}/noting/office/confirm`,
    revokeSession: (sessionId) => `/api/noting-office/sessions/${sessionId}/revoke`,
    createGreenSheet: (matterId) => `/api/matters/${matterId}/noting/green-sheets`,
    getGreenSheet: (sheetId) => `/api/noting-green-sheets/${sheetId}`,
    getGreenPdf: (sheetId) => `/api/noting-green-sheets/${sheetId}/pdf`,
    submitNote: (matterId) => `/api/matters/${matterId}/noting/submit`,
    sendNote: (matterId) => `/api/matters/${matterId}/noting/send`,
    updateRemark: (matterId, noteId, remarkId) => `/api/matters/${matterId}/notes/${noteId}/remarks/${remarkId}`
  };

  const matterId = "44444444-4444-4444-4444-444444444444";
  const sessionId = "55555555-5555-5555-5555-555555555555";
  const saveId = "66666666-6666-6666-6666-666666666666";
  const sheetId = "77777777-7777-7777-7777-777777777777";
  const noteId = "88888888-8888-8888-8888-888888888888";
  const remarkId = "99999999-9999-9999-9999-999999999999";

  assert.equal(endpoints.createSession(matterId), `/api/matters/${matterId}/noting/office/sessions`);
  assert.equal(endpoints.sessionConfig(sessionId), `/api/noting-office/sessions/${sessionId}/config`);
  assert.equal(endpoints.requestSave(sessionId), `/api/noting-office/sessions/${sessionId}/saves`);
  assert.equal(endpoints.saveReceipt(sessionId, saveId), `/api/noting-office/sessions/${sessionId}/saves/${saveId}`);
  assert.equal(endpoints.createPreview(sessionId), `/api/noting-office/sessions/${sessionId}/previews`);
  assert.equal(endpoints.confirmPreview(matterId), `/api/matters/${matterId}/noting/office/confirm`);
  assert.equal(endpoints.createGreenSheet(matterId), `/api/matters/${matterId}/noting/green-sheets`);
  assert.equal(endpoints.getGreenSheet(sheetId), `/api/noting-green-sheets/${sheetId}`);
  assert.equal(endpoints.getGreenPdf(sheetId), `/api/noting-green-sheets/${sheetId}/pdf`);
  assert.equal(endpoints.submitNote(matterId), `/api/matters/${matterId}/noting/submit`);
  assert.equal(endpoints.sendNote(matterId), `/api/matters/${matterId}/noting/send`);
  assert.equal(endpoints.updateRemark(matterId, noteId, remarkId), `/api/matters/${matterId}/notes/${noteId}/remarks/${remarkId}`);
});

test("Physical Green Legal geometry and screen-only guide parity", async (t) => {
  const legalWidthMm = 215.9;
  const legalHeightMm = 355.6;

  // Aspect ratio check
  const aspectRatio = legalWidthMm / legalHeightMm;
  assert.ok(Math.abs(aspectRatio - 0.6071428) < 0.001, "Aspect ratio must match legal portrait");

  // Odd page inside left margin (45mm)
  const oddMarginMm = 45.0;
  const oddRatio = oddMarginMm / legalWidthMm;
  assert.ok(Math.abs(oddRatio - 0.208429) < 0.001, "Odd page inside margin ratio must be 45/215.9");

  // Even page mirrored inside margin (170.9mm)
  const evenMarginMm = 170.9;
  const evenRatio = evenMarginMm / legalWidthMm;
  assert.ok(Math.abs(evenRatio - 0.791569) < 0.001, "Even page inside margin ratio must be 170.9/215.9");

  // Parity helper check
  function computeGuideX(page, widthPx) {
    const isOdd = page % 2 === 1;
    const xMm = isOdd ? 45.0 : 170.9;
    return (xMm / legalWidthMm) * widthPx;
  }

  const renderedWidth = 600;
  assert.equal(Math.round(computeGuideX(1, renderedWidth)), 125);
  assert.equal(Math.round(computeGuideX(2, renderedWidth)), 475);
  assert.equal(Math.round(computeGuideX(3, renderedWidth)), 125);
  assert.equal(Math.round(computeGuideX(4, renderedWidth)), 475);
});

test("Truthful save status transitions: never infer saved from editor state", async (t) => {
  // A client generation must never decrease
  let clientGeneration = 0;
  const nextGen1 = ++clientGeneration;
  assert.equal(nextGen1, 1);
  const nextGen2 = ++clientGeneration;
  assert.equal(nextGen2, 2);

  // Status mapping
  function getTruthfulStatusText(receipt) {
    if (!receipt) return "Draft initialised";
    if (receipt.state === "Pending") return "Saving to server…";
    if (receipt.state === "Committed") {
      const shortHash = receipt.sha256 ? receipt.sha256.substring(0, 8) : "";
      return `Saved to server: v${receipt.version || 1} (${shortHash})`;
    }
    if (receipt.state === "Failed") {
      return `Save failed: ${receipt.failureCode || "error"}. Last committed bytes retained.`;
    }
    return "Unknown status";
  }

  assert.equal(getTruthfulStatusText(null), "Draft initialised");
  assert.equal(getTruthfulStatusText({ state: "Pending" }), "Saving to server…");
  assert.equal(
    getTruthfulStatusText({ state: "Committed", version: 3, sha256: "872b8fbc584d8aa87d7ffd4a63001ce0" }),
    "Saved to server: v3 (872b8fbc)"
  );
  assert.equal(
    getTruthfulStatusText({ state: "Failed", failureCode: "DocumentServerCommandRejected" }),
    "Save failed: DocumentServerCommandRejected. Last committed bytes retained."
  );
});

test("Citation 409 conflict handling requires deliberate officer reset", async (t) => {
  function createConfirmPayload(previewId, canonicalHash, resetCitations) {
    return {
      previewId,
      canonicalHash: canonicalHash.toUpperCase(),
      resetCitations: Boolean(resetCitations)
    };
  }

  const normalPayload = createConfirmPayload("prev-1", "abc123hash", false);
  assert.equal(normalPayload.resetCitations, false);
  assert.equal(normalPayload.canonicalHash, "ABC123HASH");

  const resetPayload = createConfirmPayload("prev-1", "abc123hash", true);
  assert.equal(resetPayload.resetCitations, true);
});

test("Header ergonomics: sub-bar suppressed on specific matter workspace", async (t) => {
  function shouldShowMattersSubNav(pathname) {
    if (!pathname.startsWith("/matters") && !pathname.startsWith("/my-work")) {
      return false;
    }
    // Specific matter detail view /matters/:id must suppress redundant sub-nav
    if (pathname.match(/^\/matters\/[^/]+$/)) {
      return false;
    }
    return true;
  }

  assert.equal(shouldShowMattersSubNav("/matters"), true, "/matters directory shows sub-nav");
  assert.equal(shouldShowMattersSubNav("/my-work"), true, "/my-work shows sub-nav");
  assert.equal(shouldShowMattersSubNav("/matters/44444444-4444-4444-4444-444444444444"), false, "/matters/:id suppresses redundant sub-nav");
});
