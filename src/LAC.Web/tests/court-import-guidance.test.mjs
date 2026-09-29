import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { courtImportReviewGuidance } from "../src/court/courtImportReviewGuidance.ts";

const here = path.dirname(fileURLToPath(import.meta.url));
const preview = readFileSync(path.join(here, "../src/court/CourtImportPreview.tsx"), "utf8");

test("multiple case references explain NeedsReview without choosing an identity", () => {
  const guidance = courtImportReviewGuidance({
    rowStatus: "NeedsReview",
    rawCaseNumber: "Diary No. 36664/2025, SLP(C) No. 022670 - 022671 / 2025",
    validationIssuesJson: "[]",
  });
  assert.match(guidance.reasons[0], /more than one case reference/);
  assert.match(guidance.nextStep, /Check the official case or order document/);
});

test("source validation issue is shown in officer language", () => {
  const guidance = courtImportReviewGuidance({
    rowStatus: "NeedsReview", rawCaseNumber: "EX CIVIL/288/2021",
    validationIssuesJson: '["Last-order link is not an http/https URL."]',
  });
  assert.deepEqual(guidance.reasons, ["The last-order link is not a usable web address."]);
});

test("ambiguous and conflicting matches remain distinct", () => {
  assert.match(courtImportReviewGuidance({ rowStatus: "PotentialDuplicate", validationIssuesJson: "[]" }).reasons[0], /another imported row/);
  assert.match(courtImportReviewGuidance({ rowStatus: "IdentityConflict", validationIssuesJson: "[]" }).reasons[0], /party title/);
});

test("bulk approval and commit use in-app confirmation, not browser confirm", () => {
  assert.match(preview, /court-import-confirm-dialog/);
  assert.match(preview, /commitAcknowledged/);
  assert.doesNotMatch(preview, /\b(?:window\.)?confirm\(/);
});
