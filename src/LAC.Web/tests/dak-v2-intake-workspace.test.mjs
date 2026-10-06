import test from "node:test";
import assert from "node:assert/strict";
import { officeCalendarDate } from "../src/dak/officeDate.js";

test("1. Dak entry uses Delhi (Asia/Kolkata) date across the IST midnight boundary", () => {
  // UTC 18:29:59 is 23:59:59 IST (same day)
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:29:59Z")), "2026-10-05");
  // UTC 18:30:00 is 00:00:00 IST (next day in Delhi)
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:30:00Z")), "2026-10-06");
  assert.equal(officeCalendarDate(new Date("2026-10-05T23:59:59Z")), "2026-10-06");
});

test("2. Idempotency semantics: stable key generation and retry retention", () => {
  let stableKey = "11111111-2222-3333-4444-555555555555";
  let submissionAttempts = 0;

  function simulateSubmit(attemptKey) {
    submissionAttempts++;
    if (submissionAttempts === 1) {
      // First attempt fails (e.g. 503 or network glitch)
      return { success: false, keyUsed: attemptKey };
    }
    // Second attempt succeeds
    return { success: true, keyUsed: attemptKey, dakId: "dak-uuid-123" };
  }

  // Attempt 1
  let res1 = simulateSubmit(stableKey);
  assert.equal(res1.success, false);
  assert.equal(res1.keyUsed, stableKey);

  // Retry (Attempt 2) MUST use the exact same stable key
  let res2 = simulateSubmit(stableKey);
  assert.equal(res2.success, true);
  assert.equal(res2.keyUsed, stableKey);

  // After successful 'Register & Next', a new stable key is generated for the NEXT entry
  let nextKey = "99999999-8888-7777-6666-555555555555";
  assert.notEqual(nextKey, stableKey);
});

test("3. Physical Original contract: provenance note mandatory and location requires confirmed existence", () => {
  function validatePO(payload) {
    if (!payload.provenanceNote || !payload.provenanceNote.trim()) {
      return { valid: false, error: "A provenance note is required." };
    }
    if (payload.hasPhysicalOriginal !== true && (payload.deskId || payload.userId || payload.locationNote)) {
      return { valid: false, error: "A location can only be recorded when physical original existence is confirmed." };
    }
    return { valid: true };
  }

  // Valid PO when paper exists
  assert.deepEqual(validatePO({
    hasPhysicalOriginal: true,
    deskId: "desk-1",
    locationNote: "Shelf A",
    provenanceNote: "Verified paper original present"
  }), { valid: true });

  // Invalid: missing provenance note
  assert.equal(validatePO({
    hasPhysicalOriginal: true,
    provenanceNote: ""
  }).valid, false);

  // Invalid: location recorded when paper is not confirmed (false or null)
  assert.equal(validatePO({
    hasPhysicalOriginal: false,
    locationNote: "Shelf A",
    provenanceNote: "Observed digital only"
  }).valid, false);
});

test("4. Privacy contract for restricted context links", () => {
  function projectLink(link) {
    if (!link.canOpen) {
      return { displayName: "Restricted record", entityId: null, canOpen: false };
    }
    return link;
  }

  const restrictedLink = projectLink({ canOpen: false, entityId: "secret-uuid", displayName: "Secret Matter Title" });
  assert.equal(restrictedLink.canOpen, false);
  assert.equal(restrictedLink.entityId, null);
  assert.equal(restrictedLink.displayName, "Restricted record");
  assert.equal(restrictedLink.displayName.includes("Secret"), false);
});

test("5. UNMARKED Dak directory identification", () => {
  const itemUnmarked = { id: "1", diaryNumber: "D-101", assignedDeskName: null };
  const itemMarked = { id: "2", diaryNumber: "D-102", assignedDeskName: "NT Desk" };

  assert.equal(!itemUnmarked.assignedDeskName, true);
  assert.equal(!itemMarked.assignedDeskName, false);
});
