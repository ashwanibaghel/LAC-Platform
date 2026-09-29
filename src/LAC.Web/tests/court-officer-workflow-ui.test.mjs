import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "../src");
const read = relative => readFileSync(path.join(root, relative), "utf8");

test("empty Court register leads to Excel import without daily DHC work", () => {
  const directory = read("court/CourtDirectory.tsx");
  assert.match(directory, /registerCount === 0 \? <section className="court-first-run"/);
  assert.match(directory, /Import your existing LAC court Excel file to begin/);
  assert.match(directory, /Delhi High Court automatic updates will start after DHC matters are imported/);
  assert.match(directory, /: <DhcSyncPanel \/>/);
});

test("safe import is one explicit action and risky rows remain separate", () => {
  const preview = read("court/CourtImportPreview.tsx");
  assert.match(preview, /Add \{safeActionCount\} Court Matters/);
  assert.match(preview, /\/add-ready/);
  assert.doesNotMatch(preview, /Approve safe rows/);
  assert.match(preview, /useState<"pending" \| "committed" \| "all">\("pending"\)/);
});

test("login distinguishes wrong credentials from an unavailable server", () => {
  const auth = read("auth/AuthProvider.tsx");
  assert.match(auth, /response\.status === 401/);
  assert.match(auth, /Username or password is incorrect/);
  assert.match(auth, /LAC server is not available right now/);
});

test("assisted check explains repeat code and optional orders", () => {
  const page = read("court/DhcAssistedPage.tsx");
  assert.match(page, /Delhi High Court needs another verification code/);
  assert.match(page, /Check latest official order links too/);
  assert.match(page, /setAnswer\(""\); \/\/ Never retain a submitted answer/);
});
