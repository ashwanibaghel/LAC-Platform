import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const here = path.dirname(fileURLToPath(import.meta.url));
const preview = readFileSync(path.join(here, "../src/court/CourtImportPreview.tsx"), "utf8");
const review = readFileSync(path.join(here, "../src/court/CourtImportRowReview.tsx"), "utf8");

test("upload screen explains Excel check and never uploads without a chosen file", () => {
  assert.match(preview, /We will check it before anything is added/);
  assert.match(preview, /accept="\.xlsx"/);
  assert.match(preview, /disabled=\{!file \|\| uploading\}/);
  assert.match(preview, /court-import-guide/);
  assert.match(preview, /form\.append\("file", file\)/);
});

test("review screen leads with one safe add action while separate reviewed decisions remain", () => {
  assert.match(preview, /court-import-next-step/);
  assert.match(preview, /Add \{safeActionCount\} Court Matters/);
  assert.match(preview, /summary\.safeBulkCandidates/);
  assert.match(preview, /summary\.ready/);
  assert.match(preview, /\/add-ready/);
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

test("review queue excludes committed and skipped rows while preserving source history", () => {
  assert.match(preview, /useState<"pending" \| "committed" \| "all">\("pending"\)/);
  assert.match(preview, /query\.set\("workState", workState\)/);
  assert.match(preview, /Imported history/);
  assert.match(preview, /Added and skipped rows are hidden from this working queue/);
  assert.equal((preview.match(/batch\.totalRows - summary\.committed - summary\.skipped/g) || []).length, 2);
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
