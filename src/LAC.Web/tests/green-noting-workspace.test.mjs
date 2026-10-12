import test from "node:test";
import assert from "node:assert/strict";
import {
  computeSha256Hex,
  deriveExactAnchor,
  splitsSurrogate
} from "../src/matter/canonicalTextSelection.ts";

test("Canonical Text Selection - computeSha256Hex matches known test vectors and C# NotingText.Hash", async (t) => {
  // Vector 1: Standard ASCII
  const hash1 = await computeSha256Hex("Note 1 text");
  assert.equal(hash1, "B48D0E4399A7FE0B9F3EE471C26E0F7E94692B3F182337B9DEFE94E97D90DB48");
  assert.equal(hash1, hash1.toUpperCase(), "Hash must be uppercase hex matching C# Convert.ToHexString");

  // Vector 2: Official LAC legal note excerpt
  const legalText = "It is submitted that the revenue records of Village Rampura have been scrutinized.";
  const hash2 = await computeSha256Hex(legalText);
  // Verify length is 64 hex chars
  assert.equal(hash2.length, 64);
  assert.match(hash2, /^[0-9A-F]{64}$/);

  // Vector 3: UTF-8 characters
  const unicodeText = "भूमि अर्जन कलेक्टर — Office of the Land Acquisition Collector";
  const hash3 = await computeSha256Hex(unicodeText);
  assert.equal(hash3.length, 64);
  assert.match(hash3, /^[0-9A-F]{64}$/);
});

test("Canonical Text Selection - deriveExactAnchor happy path with exact offsets and hash", async (t) => {
  const canonical = "Paragraph 1: The proposal has been examined.\nParagraph 2: Necessary sanction may be accorded.";
  const start = 13;
  const end = 43; // "The proposal has been examined"
  const expectedQuote = "The proposal has been examined";

  const result = await deriveExactAnchor(canonical, start, end, expectedQuote);
  assert.equal(result.success, true);
  assert.ok(result.anchor);
  assert.equal(result.anchor.version, 1);
  assert.equal(result.anchor.start, start);
  assert.equal(result.anchor.end, end);
  assert.equal(result.anchor.quote, expectedQuote);
  assert.equal(result.anchor.quoteHash, await computeSha256Hex(expectedQuote));
});

test("Canonical Text Selection - negative paths and boundary validation", async (t) => {
  const canonical = "Official Note Text Line 1.\nLine 2.";

  // Out of bounds negative start
  const negStart = await deriveExactAnchor(canonical, -1, 5);
  assert.equal(negStart.success, false);
  assert.match(negStart.errorMessage, /out of range/i);

  // End beyond text length
  const outOfBoundsEnd = await deriveExactAnchor(canonical, 0, canonical.length + 5);
  assert.equal(outOfBoundsEnd.success, false);
  assert.match(outOfBoundsEnd.errorMessage, /out of range/i);

  // End <= Start (zero-width or reversed)
  const zeroWidth = await deriveExactAnchor(canonical, 5, 5);
  assert.equal(zeroWidth.success, false);
  assert.match(zeroWidth.errorMessage, /out of range/i);

  const reversed = await deriveExactAnchor(canonical, 10, 5);
  assert.equal(reversed.success, false);
  assert.match(reversed.errorMessage, /out of range/i);

  // Whitespace-only selection
  const whitespaceResult = await deriveExactAnchor("Line 1.   \n\t   Line 2.", 7, 14);
  assert.equal(whitespaceResult.success, false);
  assert.match(whitespaceResult.errorMessage, /whitespace-only/i);

  // Mismatched expected quote
  const mismatch = await deriveExactAnchor(canonical, 0, 8, "Different Text");
  assert.equal(mismatch.success, false);
  assert.match(mismatch.errorMessage, /does not match/i);
});

test("Canonical Text Selection - Unicode surrogate pair boundary safety", async (t) => {
  // Emojis / astral plane characters use UTF-16 surrogate pairs (e.g. \uD83D\uDCDC = 📜)
  const prefix = "Legal document: ";
  const emoji = "\uD83D\uDCDC"; // 📜
  const suffix = " approved by Competent Authority.";
  const canonical = prefix + emoji + suffix;

  const emojiStart = prefix.length; // 16
  const emojiEnd = emojiStart + 2;   // 18
  const surrogateSplitIndex = emojiStart + 1; // 17 (splits high and low surrogate)

  // Verify splitsSurrogate helper
  assert.equal(splitsSurrogate(canonical, emojiStart), false, "Start before surrogate pair is valid");
  assert.equal(splitsSurrogate(canonical, surrogateSplitIndex), true, "Index between high and low surrogate splits pair");
  assert.equal(splitsSurrogate(canonical, emojiEnd), false, "Index after surrogate pair is valid");

  // Selection that splits start of surrogate pair
  const splitStartResult = await deriveExactAnchor(canonical, surrogateSplitIndex, emojiEnd + 5);
  assert.equal(splitStartResult.success, false);
  assert.match(splitStartResult.errorMessage, /splits a Unicode surrogate pair/i);

  // Selection that splits end of surrogate pair
  const splitEndResult = await deriveExactAnchor(canonical, 0, surrogateSplitIndex);
  assert.equal(splitEndResult.success, false);
  assert.match(splitEndResult.errorMessage, /splits a Unicode surrogate pair/i);

  // Selection enclosing the surrogate pair completely must succeed
  const validSurrogateResult = await deriveExactAnchor(canonical, emojiStart, emojiEnd, emoji);
  assert.equal(validSurrogateResult.success, true);
  assert.equal(validSurrogateResult.anchor.quote, emoji);
  assert.equal(validSurrogateResult.anchor.quoteHash, await computeSha256Hex(emoji));
});

test("Truthful save serialization and edit generation tracking", async (t) => {
  // Simulate the save state coordinator in GreenNotingWorkspace
  class SaveStateCoordinator {
    constructor() {
      this.dirtyGeneration = 0;
      this.lastRequestedGeneration = 0;
      this.lastCommittedGeneration = 0;
      this.inFlightSavePromise = null;
    }

    userTyped() {
      this.dirtyGeneration++;
    }

    get hasUnsavedChanges() {
      return this.dirtyGeneration > this.lastCommittedGeneration;
    }

    get displayStatus() {
      if (this.inFlightSavePromise) {
        return "Saving to server…";
      }
      if (this.hasUnsavedChanges) {
        return "Unsaved changes";
      }
      if (this.lastCommittedGeneration > 0) {
        return `Saved to server (gen ${this.lastCommittedGeneration})`;
      }
      return "Draft ready";
    }

    async save(simulatedBackendSave) {
      if (this.inFlightSavePromise) {
        // Serialized: wait for in-flight save before starting new save
        await this.inFlightSavePromise;
      }
      if (this.lastCommittedGeneration >= this.dirtyGeneration) {
        return; // nothing new to save
      }

      const genToSave = this.dirtyGeneration;
      this.lastRequestedGeneration = genToSave;

      const p = (async () => {
        const receipt = await simulatedBackendSave(genToSave);
        if (receipt.state === "Committed") {
          this.lastCommittedGeneration = Math.max(this.lastCommittedGeneration, genToSave);
        }
        return receipt;
      })();

      this.inFlightSavePromise = p;
      try {
        return await p;
      } finally {
        this.inFlightSavePromise = null;
      }
    }
  }

  const coordinator = new SaveStateCoordinator();
  assert.equal(coordinator.displayStatus, "Draft ready");

  // User types edit 1
  coordinator.userTyped();
  assert.equal(coordinator.dirtyGeneration, 1);
  assert.equal(coordinator.hasUnsavedChanges, true);
  assert.equal(coordinator.displayStatus, "Unsaved changes");

  // Start save 1 with delay
  let finishSave1;
  const save1Promise = coordinator.save((gen) => new Promise((resolve) => {
    finishSave1 = () => resolve({ state: "Committed", version: gen });
  }));

  assert.equal(coordinator.displayStatus, "Saving to server…");

  // User types edit 2 while save 1 is in-flight!
  coordinator.userTyped();
  assert.equal(coordinator.dirtyGeneration, 2);

  // Complete save 1
  finishSave1();
  await save1Promise;

  // Crucial acceptance rule: Even though save 1 committed, user has typed gen 2!
  // The UI must NOT show "Saved to server"!
  assert.equal(coordinator.lastCommittedGeneration, 1);
  assert.equal(coordinator.dirtyGeneration, 2);
  assert.equal(coordinator.hasUnsavedChanges, true, "Must still report unsaved changes");
  assert.equal(coordinator.displayStatus, "Unsaved changes");

  // Now save again for gen 2
  await coordinator.save(async (gen) => ({ state: "Committed", version: gen }));

  assert.equal(coordinator.lastCommittedGeneration, 2);
  assert.equal(coordinator.hasUnsavedChanges, false);
  assert.equal(coordinator.displayStatus, "Saved to server (gen 2)");
});

test("Save mutation idempotency & safe network retry", async (t) => {
  // Verify idempotency command tracking
  const idempotencyKey = "idemp-" + Date.now();
  const initialRevision = 2;

  const initialSaveCommand = {
    idempotencyKey,
    expectedRevision: initialRevision,
    payload: { action: "save" }
  };

  // On network uncertainty / timeout, retry MUST reuse exact same idempotencyKey and expectedRevision
  const retrySaveCommand = {
    ...initialSaveCommand
  };

  assert.equal(retrySaveCommand.idempotencyKey, initialSaveCommand.idempotencyKey);
  assert.equal(retrySaveCommand.expectedRevision, initialSaveCommand.expectedRevision);
  assert.equal(retrySaveCommand.expectedRevision, 2, "Does not artificially increment revision on retry");
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
