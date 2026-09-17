import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const sourcePath = fileURLToPath(new URL("../src/editor/notingLayoutProfile.ts", import.meta.url));
const source = readFileSync(sourcePath, "utf8");
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2023 },
}).outputText;
const moduleUrl = `data:text/javascript;base64,${Buffer.from(output).toString("base64")}`;
const { DELHI_LAC_NOTING_LEGAL_MIRROR_V1, getNotingPageGeometry } = await import(moduleUrl);

const profile = DELHI_LAC_NOTING_LEGAL_MIRROR_V1;
assert.equal(profile.pageWidthMm, 215.9);
assert.equal(profile.pageHeightMm, 355.6);
assert.equal(profile.topMm, 25);
assert.equal(profile.bottomMm, 25);
assert.equal(profile.gutterMm, 45);
assert.equal(profile.writingWidthMm, 170.9);
assert.equal(profile.writingHeightMm, 305.6);

const page1 = getNotingPageGeometry(1);
assert.deepEqual(
  { reservedSide: page1.reservedSide, separatorXmm: page1.separatorXmm, contentLeftMm: page1.contentLeftMm, contentRightMm: page1.contentRightMm, contentWidthMm: page1.contentWidthMm },
  { reservedSide: "left", separatorXmm: 45, contentLeftMm: 45, contentRightMm: 0, contentWidthMm: 170.9 },
);

const page2 = getNotingPageGeometry(2);
assert.deepEqual(
  { reservedSide: page2.reservedSide, separatorXmm: page2.separatorXmm, contentLeftMm: page2.contentLeftMm, contentRightMm: page2.contentRightMm, contentWidthMm: page2.contentWidthMm },
  { reservedSide: "right", separatorXmm: 170.9, contentLeftMm: 0, contentRightMm: 45, contentWidthMm: 170.9 },
);

assert.deepEqual(getNotingPageGeometry(3), { ...page1, pageNumber: 3 });
assert.deepEqual(getNotingPageGeometry(4), { ...page2, pageNumber: 4 });

for (let pageNumber = 1; pageNumber <= 100; pageNumber++) {
  const geometry = getNotingPageGeometry(pageNumber);
  assert.equal(geometry.reservedSide, pageNumber % 2 ? "left" : "right");
  assert.equal(geometry.contentWidthMm, 170.9);
  assert.equal(geometry.separatorXmm, pageNumber % 2 ? 45 : 170.9);
  assert.equal(geometry.separatorBottomMm, 330.6);
}

assert.throws(() => getNotingPageGeometry(0), RangeError);
assert.throws(() => getNotingPageGeometry(1.5), RangeError);
console.log("Noting layout geometry: 100 pages passed without drift.");
