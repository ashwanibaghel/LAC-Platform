import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import ts from "typescript";

// 1. Transpile and import notingLayoutProfile and pagination
const notingProfileSourcePath = fileURLToPath(new URL("../src/editor/notingLayoutProfile.ts", import.meta.url));
const notingProfileSource = readFileSync(notingProfileSourcePath, "utf8");
const notingProfileJs = ts.transpileModule(notingProfileSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2023 },
}).outputText;
const notingProfileUrl = "data:text/javascript;base64," + Buffer.from(notingProfileJs).toString("base64");
const { DELHI_LAC_NOTING_LEGAL_MIRROR_V1 } = await import(notingProfileUrl);

const paginationSourcePath = fileURLToPath(new URL("../src/editor/pagination.ts", import.meta.url));
let paginationSource = readFileSync(paginationSourcePath, "utf8");
// Mock tiptap/prosemirror dependencies for standalone evaluation
paginationSource = `class PluginKey { constructor(n) { this.n = n; } }
class Decoration { static inline() {} static widget() {} }
class DecorationSet { static create() { return {}; } static empty = {}; }
const Extension = { create: () => ({}) };
` + paginationSource
  .replace(/import\s+\{[^}]*\}\s+from\s+"@tiptap\/[^"]+";/g, "")
  .replace(/import\s+\{[^}]*\}\s+from\s+"\.\/pageProfiles";/g, "const mmToPx = mm => mm * 96 / 25.4;");

const paginationJs = ts.transpileModule(paginationSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2023 },
}).outputText;
const paginationUrl = "data:text/javascript;base64," + Buffer.from(paginationJs).toString("base64");
const { computePageBreaks, derivePageSegments } = await import(paginationUrl);

const profile = {
  ...DELHI_LAC_NOTING_LEGAL_MIRROR_V1,
  widthMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.pageWidthMm,
  heightMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.pageHeightMm,
  marginTopMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.topMm,
  marginRightMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.gutterMm,
  marginBottomMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.bottomMm,
  marginLeftMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.gutterMm,
};

const totalPageHeightPx = (profile.heightMm * 96) / 25.4;
const marginTopPx = (profile.marginTopMm * 96) / 25.4;
const marginBottomPx = (profile.marginBottomMm * 96) / 25.4;
const printableHeightPx = totalPageHeightPx - marginTopPx - marginBottomPx;
const SHEET_GAP_PX = 32;
const onePhysicalPageOffset = marginTopPx + marginBottomPx + SHEET_GAP_PX;

console.log("Profile geometry:", {
  totalPageHeightPx: Math.round(totalPageHeightPx),
  printableHeightPx: Math.round(printableHeightPx),
  onePhysicalPageOffset: Math.round(onePhysicalPageOffset),
});

// Setup mock document:
// Block 0: Paragraph on Page 1 (pos 0..100) -> height 700px
// Block 1: Paragraph on Page 2 (pos 100..200) -> height 700px (overflows P1, pushed to P2)
// Block 2: Giant paragraph starting on Page 3 (pos 200..3200) -> height 3500px (spans P3 -> P4 -> P5)
const blocks = [
  { start: 0, size: 100, top: marginTopPx, bottom: marginTopPx + 700, type: "paragraph" },
  { start: 100, size: 100, top: marginTopPx + 700, bottom: marginTopPx + 1400, type: "paragraph" },
  { start: 200, size: 3000, top: marginTopPx + 1400, bottom: marginTopPx + 4900, type: "paragraph" },
];

const totalDocSize = 3200;

function mockCoordsAtPos(pos) {
  for (const block of blocks) {
    if (pos >= block.start && pos <= block.start + block.size) {
      const fraction = (pos - block.start) / block.size;
      const y = block.top + fraction * (block.bottom - block.top);
      return { top: y, bottom: y + 20, left: 0, right: 100 };
    }
  }
  return { top: 0, bottom: 20, left: 0, right: 100 };
}

const mockView = {
  state: {
    doc: {
      childCount: blocks.length,
      child(i) {
        return {
          nodeSize: blocks[i].size,
          type: { name: blocks[i].type },
        };
      },
      content: { size: totalDocSize },
    },
  },
  dom: {
    offsetHeight: 10000,
    getBoundingClientRect() {
      return { top: 0, bottom: 10000, left: 0, right: 800, width: 800, height: 10000 };
    },
    querySelectorAll() {
      return [];
    },
  },
  nodeDOM() { return null; },
  domAtPos() { return { node: null }; },
  coordsAtPos(pos) { return mockCoordsAtPos(pos); },
};

const result = computePageBreaks(mockView, profile, 1);
console.log("computePageBreaks result:", {
  pageCount: result.pageCount,
  breakCount: result.breaks.length,
  breaks: result.breaks.map(b => ({
    pos: b.pos,
    pageIndex: b.pageIndex,
    heightPx: Math.round(b.heightPx),
    isInline: !!b.isInlineBreak,
  })),
});

// Assertions from Step 3:
// 1. Break 0: pushes Block 1 from Page 1 to Page 2
assert.equal(result.breaks[0].pageIndex, 1, "Break 0 creates Page 2 (index 1)");
assert.equal(result.breaks[0].pos, 100, "Break 0 is before Block 1");

// 2. Break 1: pushes Block 2 (giant paragraph) from Page 2 to Page 3
assert.equal(result.breaks[1].pageIndex, 2, "Break 1 creates Page 3 (index 2)");
assert.equal(result.breaks[1].pos, 200, "Break 1 is before Block 2 (giant paragraph)");

// 3. Giant paragraph starts on Page 3 and continues across multiple pages:
// First continuation goes to Page 4:
assert.equal(result.breaks[2].pageIndex, 3, "First continuation goes to page 4 (index 3)");
assert.equal(result.breaks[2].isInlineBreak, true, "First continuation is an inline break");

// Next continuation goes to Page 5:
assert.equal(result.breaks[3].pageIndex, 4, "Next continuation goes to page 5 (index 4)");
assert.equal(result.breaks[3].isInlineBreak, true, "Next continuation is an inline break");

// Assert no page-number jump
for (let i = 0; i < result.breaks.length; i++) {
  assert.equal(result.breaks[i].pageIndex, i + 1, `Break ${i} must have sequential pageIndex ${i + 1} with zero skips`);
}

// Assert spacer stays within one physical-page-scale offset (NOT ~2966px)
console.log(`First inline spacer height: ${Math.round(result.breaks[2].heightPx)}px (expected ~${Math.round(onePhysicalPageOffset)}px)`);
console.log(`Second inline spacer height: ${Math.round(result.breaks[3].heightPx)}px (expected ~${Math.round(onePhysicalPageOffset)}px)`);

assert.ok(
  result.breaks[2].heightPx <= onePhysicalPageOffset + 50 && result.breaks[2].heightPx >= onePhysicalPageOffset - 50,
  `First inline spacer (${result.breaks[2].heightPx}px) must stay within one physical-page-scale offset (~${Math.round(onePhysicalPageOffset)}px), not ~2966px`
);

assert.ok(
  result.breaks[3].heightPx <= onePhysicalPageOffset + 50 && result.breaks[3].heightPx >= onePhysicalPageOffset - 50,
  `Second inline spacer (${result.breaks[3].heightPx}px) must stay within one physical-page-scale offset (~${Math.round(onePhysicalPageOffset)}px)`
);

// Assert global page numbering remains intact
assert.equal(result.pageCount, 6, "Total pages must be 6 (P1..P6)");

// Test derivePageSegments
const segments = derivePageSegments(totalDocSize, result.pageCount, result.breaks);
console.log("derivePageSegments result:", segments);
assert.equal(segments.length, 6, "Must have 6 sequential segments");
assert.deepEqual(segments.map(s => s.pageNumber), [1, 2, 3, 4, 5, 6], "Segments must be numbered 1, 2, 3, 4, 5, 6");
assert.equal(segments[0].from, 0);
assert.equal(segments[0].to, result.breaks[0].pos);
assert.equal(segments[5].to, totalDocSize);

console.log("PASS: All regression assertions for giant paragraph on Page 3 passed successfully!");
