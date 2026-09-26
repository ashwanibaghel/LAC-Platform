import assert from "node:assert/strict";
import test from "node:test";
import { performance } from "node:perf_hooks";
import { addRevenueAreas } from "../src/calculator/landConversions.js";
import { detectBulkRevenueFormat, normalizedRowsForExcel, parseBulkRevenue, parseRevenueColumnTable, parseRevenueReferenceTable, parseRevenueShorthandBulk, parseSeparateUnitLists, parseTransposedRevenueRows, sumBulkRevenueRows } from "../src/calculator/bulkRevenueParser.js";

const pair = [{ bigha: 22, biswa: 3, biswansi: 2 }, { bigha: 11, biswa: 2, biswansi: 15 }];
const assertPair = parsed => { assert.equal(parsed.errorCount, 0); assert.deepEqual(parsed.rows.map(row => row.value), pair); assert.deepEqual(sumBulkRevenueRows(parsed).revenue, addRevenueAreas(pair)); };

test("newline, comma and tab shorthand produce the same exact total", () => {
  for (const separator of ["\n", ", ", "\t"]) assertPair(parseBulkRevenue(`22-3-2${separator}11-2-15`));
  assert.equal(detectBulkRevenueFormat("22-3-2, 11-2-15"), "shorthand");
});

test("Excel columns, references and recognized headers retain record order", () => {
  assertPair(parseBulkRevenue("22\t3\t2\n11\t2\t15"));
  assertPair(parseRevenueColumnTable("Bigha\tBiswa\tBiswansi\n22\t3\t2\n11\t2\t15"));
  const references = parseRevenueReferenceTable("Khasra No\tBigha\tBiswa\tBiswansi\n259/2\t2\t9\t1\n260/1\t4\t16\t0");
  assert.deepEqual(references.rows.map(row => row.reference), ["259/2", "260/1"]);
  assert.match(normalizedRowsForExcel(references), /^Reference\tBigha\tBiswa\tBiswansi\tRevenue Area\n259\/2\t2\t9\t1\t2-9-1/);
  assert.match(normalizedRowsForExcel(parseRevenueShorthandBulk("22-3-2")), /^Bigha\tBiswa\tBiswansi\tRevenue Area\n22\t3\t2\t22-3-2$/);
});

test("transposed rows require explicit selection when layout could be a 3-column table", () => {
  const source = "22\t11\t4\t2\n3\t2\t16\t9\n2\t15\t0\t1";
  assert.equal(detectBulkRevenueFormat(source), null);
  assert.deepEqual(parseTransposedRevenueRows(source).rows.map(row => row.value), [
    { bigha: 22, biswa: 3, biswansi: 2 }, { bigha: 11, biswa: 2, biswansi: 15 },
    { bigha: 4, biswa: 16, biswansi: 0 }, { bigha: 2, biswa: 9, biswansi: 1 },
  ]);
  const square = "1\t2\t3\n4\t5\t6\n7\t8\t9";
  assert.equal(parseBulkRevenue(square).unresolved, true);
  assert.throws(() => sumBulkRevenueRows(parseBulkRevenue(square)), /Resolve all parsing errors/i);
});

test("separate lists fill entirely absent units but reject partial lists", () => {
  const all = parseSeparateUnitLists("22,11,4,2", "3,2,16,9", "2,15,0,1");
  assert.deepEqual(all.rows.map(row => row.value), [pair[0], pair[1], { bigha: 4, biswa: 16, biswansi: 0 }, { bigha: 2, biswa: 9, biswansi: 1 }]);
  assert.deepEqual(parseSeparateUnitLists("22,11", "3,2", "").rows.map(row => row.value), [
    { bigha: 22, biswa: 3, biswansi: 0 }, { bigha: 11, biswa: 2, biswansi: 0 },
  ]);
  assert.deepEqual(parseSeparateUnitLists("22,11,4", "", "").rows.map(row => row.value), [
    { bigha: 22, biswa: 0, biswansi: 0 }, { bigha: 11, biswa: 0, biswansi: 0 }, { bigha: 4, biswa: 0, biswansi: 0 },
  ]);
  assert.deepEqual(parseSeparateUnitLists("", "3,2", "").rows.map(row => row.value), [
    { bigha: 0, biswa: 3, biswansi: 0 }, { bigha: 0, biswa: 2, biswansi: 0 },
  ]);
  const mismatch = parseSeparateUnitLists("22,11,4,2", "3,2", "2,15,0,1");
  assert.equal(mismatch.recordCount, 4);
  assert.match(mismatch.rows[0].error, /Biswa contains 2 values but 4 records are required/);
  assert.throws(() => sumBulkRevenueRows(mismatch), /Resolve all parsing errors/i);
});

test("malformed, decimal, range and blank cell inputs block the entire total", () => {
  for (const text of ["1-20-0", "1-0-20", "22-3-x", "2.5-3-1", "22-3-2, random prose"]) {
    const parsed = parseRevenueShorthandBulk(text);
    assert.ok(parsed.errorCount > 0, text);
    assert.throws(() => sumBulkRevenueRows(parsed), /Resolve all parsing errors/i);
  }
  const malformedList = parseSeparateUnitLists("22, abc, 14", "", "");
  assert.match(malformedList.rows[1].error, /Invalid Bigha value "abc"/);
  const blank = parseRevenueColumnTable("22\t\t2\n11\t2\t15");
  assert.match(blank.rows[0].error, /Invalid Biswa value ""/);
  assert.equal(blank.validCount, 1);
  assert.throws(() => sumBulkRevenueRows(blank), /Resolve all parsing errors/i);
  const shortRow = parseRevenueColumnTable("22\t3\t2\n11\t2");
  assert.match(shortRow.rows[1].error, /Expected 3 columns, found 2/);
});

test("10,000 records parse and sum exactly without row loss", () => {
  const source = Array.from({ length: 10000 }, () => "1-19-19").join("\n");
  const started = performance.now();
  const parsed = parseBulkRevenue(source);
  const total = sumBulkRevenueRows(parsed);
  const elapsedMs = performance.now() - started;
  assert.equal(parsed.rows.length, 10000);
  assert.equal(total.totalBiswansi, 7990000);
  assert.deepEqual(total.revenue, { bigha: 19975, biswa: 0, biswansi: 0 });
  assert.equal(total.sqm, total.totalBiswansi * 2.1075);
  assert.ok(elapsedMs < 5000, `10,000 records took ${elapsedMs.toFixed(0)} ms`);
  console.log(`10,000-row parse + sum: ${elapsedMs.toFixed(1)} ms`);
});
