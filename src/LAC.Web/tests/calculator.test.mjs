import assert from "node:assert/strict";
import test from "node:test";
import { AREA_UNITS, areaToSqm, lengthToMetres, parseRevenueShorthand, revenueToSqm, sqmToRevenue, validateRevenue } from "../src/calculator/landConversions.js";
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
test("standard area and Ghatta conversions", () => {
  assert.equal(areaToSqm(1, "hectare"), 10000); assert.equal(areaToSqm(1, "sqkm"), 1000000);
  assert.equal(areaToSqm(1, "acre"), 4046.8564224); assert.equal(areaToSqm(1, "sqyd"), 0.83612736); assert.equal(areaToSqm(1, "sqft"), 0.09290304);
  assert.equal(lengthToMetres(1, "ghatta"), 8.25 * 0.3048);
});
test("normal calculator supports precedence, brackets, decimals, percentage and divide by zero", () => {
  assert.equal(evaluateExpression("2 + 3 * 4"), 14); assert.equal(evaluateExpression("(2 + 3) * 4"), 20); assert.equal(evaluateExpression("1.5 + 2.25"), 3.75); assert.equal(evaluateExpression("100 * 15%"), 15); assert.throws(() => evaluateExpression("1 / 0"), /divide by zero/i);
});
