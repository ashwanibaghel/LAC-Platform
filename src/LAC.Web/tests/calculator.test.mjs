import assert from "node:assert/strict";
import test from "node:test";
import { AREA_UNITS, addRevenueAreas, areaToSqm, formatRevenueFromSqm, fromTotalBiswansi, isRevenueAreaUnit, lengthToMetres, normalizeRevenueTotal, parseRevenueShorthand, revenueToSqm, revenueTotals, sqmToRevenue, subtractRevenueAreas, toTotalBiswansi, validateRevenue } from "../src/calculator/landConversions.js";
import { evaluateExpression } from "../src/calculator/arithmetic.js";

test("Delhi revenue hierarchy and canonical basis", () => {
  assert.equal(revenueToSqm({ bigha: 1, biswa: 0, biswansi: 0 }), 843);
  assert.equal(revenueToSqm({ bigha: 0, biswa: 1, biswansi: 0 }), 42.15);
  assert.equal(revenueToSqm({ bigha: 0, biswa: 0, biswansi: 1 }), 2.1075);
  assert.notEqual(AREA_UNITS.biswa.sqm, 50 * AREA_UNITS.sqyd.sqm, "Biswa must not derive from the rounded 50 sq yd reference");
});
test("mixed revenue input validates and round-trips", () => {
  assert.deepEqual(parseRevenueShorthand("2-9-1").value, { bigha: 2, biswa: 9, biswansi: 1 });
  assert.equal(validateRevenue(2, 20, 0).valid, false); assert.equal(validateRevenue(2, 9, 20).valid, false);
  const result = sqmToRevenue(revenueToSqm({ bigha: 2, biswa: 9, biswansi: 1 }));
  assert.equal(result.bigha, 2); assert.equal(result.biswa, 9); assert.equal(result.biswansi, 1);

  // A hectare must convert through the exact canonical hierarchy, not a rounded
  // square-yard approximation. A square metre also yields a usable triplet.
  assert.equal(sqmToRevenue(10000).totalBiswansi, 10000 / 2.1075);
  assert.deepEqual(sqmToRevenue(843), { bigha: 1, biswa: 0, biswansi: 0, totalBiswansi: 400 });
});
test("area revenue display is rounded, normalized, and only available for revenue sources", () => {
  assert.equal(formatRevenueFromSqm(843), "1-0-0");
  assert.equal(formatRevenueFromSqm(revenueToSqm({ bigha: 2, biswa: 9, biswansi: 1 })), "2-9-1");
  const hectareRevenue = formatRevenueFromSqm(10000);
  assert.match(hectareRevenue, /^11-17-4\.9585$/);
  assert.ok(hectareRevenue.split("-")[2].split(".")[1].length <= 4);
  assert.equal(isRevenueAreaUnit("bigha"), true);
  assert.equal(isRevenueAreaUnit("biswa"), true);
  assert.equal(isRevenueAreaUnit("biswansi"), true);
  assert.equal(isRevenueAreaUnit("hectare"), false);
  assert.equal(isRevenueAreaUnit("sqm"), false);
});
test("standard area and Ghatta conversions", () => {
  assert.equal(areaToSqm(1, "hectare"), 10000); assert.equal(areaToSqm(1, "sqkm"), 1000000);
  assert.equal(areaToSqm(1, "acre"), 4046.8564224); assert.equal(areaToSqm(1, "sqyd"), 0.83612736); assert.equal(areaToSqm(1, "sqft"), 0.09290304);
  assert.equal(lengthToMetres(1, "ghatta"), 8.25 * 0.3048);
});
test("normal calculator supports precedence, brackets, decimals, percentage and divide by zero", () => {
  assert.equal(evaluateExpression("2 + 3 * 4"), 14); assert.equal(evaluateExpression("(2 + 3) * 4"), 20); assert.equal(evaluateExpression("1.5 + 2.25"), 3.75); assert.equal(evaluateExpression("100 * 15%"), 15); assert.throws(() => evaluateExpression("1 / 0"), /divide by zero/i);
});

test("revenue arithmetic carries Biswansi and Biswa through the canonical integer hierarchy", () => {
  assert.deepEqual(addRevenueAreas([{ bigha: 20, biswa: 19, biswansi: 9 }, { bigha: 12, biswa: 8, biswansi: 11 }]), { bigha: 33, biswa: 8, biswansi: 0 });
  assert.deepEqual(addRevenueAreas([{ bigha: 0, biswa: 0, biswansi: 19 }, { bigha: 0, biswa: 0, biswansi: 1 }]), { bigha: 0, biswa: 1, biswansi: 0 });
  assert.deepEqual(addRevenueAreas([{ bigha: 0, biswa: 19, biswansi: 0 }, { bigha: 0, biswa: 1, biswansi: 0 }]), { bigha: 1, biswa: 0, biswansi: 0 });
  assert.deepEqual(addRevenueAreas([{ bigha: 1, biswa: 1, biswansi: 1 }, { bigha: 2, biswa: 2, biswansi: 2 }, { bigha: 3, biswa: 3, biswansi: 3 }]), { bigha: 6, biswa: 6, biswansi: 6 });
});

test("revenue subtraction borrows safely and rejects negative results", () => {
  assert.deepEqual(subtractRevenueAreas({ bigha: 5, biswa: 13, biswansi: 0 }, { bigha: 0, biswa: 4, biswansi: 7 }), { bigha: 5, biswa: 8, biswansi: 13 });
  assert.deepEqual(subtractRevenueAreas({ bigha: 1, biswa: 0, biswansi: 0 }, { bigha: 0, biswa: 0, biswansi: 1 }), { bigha: 0, biswa: 19, biswansi: 19 });
  assert.throws(() => subtractRevenueAreas({ bigha: 0, biswa: 0, biswansi: 1 }, { bigha: 0, biswa: 0, biswansi: 2 }), /cannot be negative/i);
});

test("revenue normalizer and reverse totals remain exact", () => {
  assert.deepEqual(normalizeRevenueTotal(152, "biswa"), { bigha: 7, biswa: 12, biswansi: 0 });
  assert.deepEqual(normalizeRevenueTotal(487, "biswansi"), { bigha: 1, biswa: 4, biswansi: 7 });
  assert.equal(toTotalBiswansi({ bigha: 2, biswa: 9, biswansi: 1 }), 981);
  assert.deepEqual(revenueTotals({ bigha: 2, biswa: 9, biswansi: 1 }), { totalBiswansi: 981, totalBiswa: 49.05, totalBigha: 2.4525 });
  assert.throws(() => fromTotalBiswansi(-1), /non-negative/i);
  assert.equal(validateRevenue(1, 20, 0).valid, false);
  assert.equal(validateRevenue(1, 0, 20).valid, false);
});
