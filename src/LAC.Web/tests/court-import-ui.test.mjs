import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const here = path.dirname(fileURLToPath(import.meta.url));
const preview = readFileSync(path.join(here, "../src/court/CourtImportPreview.tsx"), "utf8");
const review = readFileSync(path.join(here, "../src/court/CourtImportRowReview.tsx"), "utf8");

test("upload screen explains staging and never uploads without a chosen file", () => {
  assert.match(preview, /Nothing is added to Court Matters until you approve and commit/);
  assert.match(preview, /accept="\.xlsx"/);
  assert.match(preview, /disabled=\{!file \|\| uploading\}/);
  assert.match(preview, /court-import-guide/);
  assert.match(preview, /form\.append\("file", file\)/);
});

test("review screen leads with the safe next action and keeps commit separate", () => {
  assert.match(preview, /court-import-next-step/);
  assert.match(preview, /Approve safe rows/);
  assert.match(preview, /Commit approved/);
  assert.match(preview, /summary\.safeBulkCandidates/);
  assert.match(preview, /summary\.ready/);
  assert.match(preview, /\/approve-safe/);
  assert.match(preview, /\/commit/);
});

test("classification tabs and condensed rows preserve every review action", () => {
  for (const value of ["NewCandidate", "NeedsReview", "PotentialDuplicate", "IdentityConflict", "ExistingExact", "Invalid"])
    assert.ok(preview.includes(`"${value}"`), `${value} filter missing`);
  assert.match(preview, /court-import-review-table/);
  assert.match(preview, /Review row/);
  assert.match(preview, /Edit decision/);
  assert.match(preview, /Clear decision/);
  assert.match(preview, /court-import-review-dialog/);
});

test("visible item numbering starts at one while retaining the Excel source row", () => {
  assert.match(preview, /rows\.map\(\(row, index\) =>/);
  assert.match(preview, /#\{\(page - 1\) \* pageSize \+ index \+ 1\}/);
  assert.match(preview, /Excel row \{row\.sourceRowNumber\}/);
  assert.match(preview, /Sr\. \{row\.sourceSerialNumberRaw\}/);
});

test("decision panel requires explicit choice for risky rows and retains safeguards", () => {
  assert.match(review, /row\.rowStatus === "NewCandidate" \? "ImportAsNewCase" : ""/);
  assert.match(review, /if \(!action\) \{ setError/);
  assert.match(review, /Why this row needs your check/);
  assert.match(review, /courtImportReviewGuidance\(row\)/);
  assert.match(review, /LinkToExistingCase/);
  assert.match(review, /KeepExisting/);
  assert.match(review, /UseImported/);
  assert.match(review, /reviewerNotes: notes/);
});
