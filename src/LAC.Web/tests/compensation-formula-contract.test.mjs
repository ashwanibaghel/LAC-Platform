import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  INITIAL_COMPENSATION_FORM_STATE,
  ALL_RECOGNIZED_VARIABLES,
  buildCompensationRequest,
  convertFormulaReadableToInternal,
  validateFormulaSyntax
} from "../src/calculator/compensationContracts.ts";

const cases = JSON.parse(readFileSync(new URL("./fixtures/compensation-formula-contracts.json", import.meta.url), "utf8"));
const referenceForm = {
  ...INITIAL_COMPENSATION_FORM_STATE,
  landArea: "18", marketRate: "5300000", useOfficialEquivalent: true,
  officialEquivalentArea: "3.744", multiplicationFactor: "2", solatiumPercentage: "100",
  annualRate: "12", durationValue: "30"
};

for (const formula of cases) {
  test(`shared frontend/history formula contract: ${formula.readable} (${formula.durationMode})`, () => {
    assert.equal(convertFormulaReadableToInternal(formula.readable), formula.normalized);
    assert.equal(validateFormulaSyntax(formula.readable, formula.durationMode) === null, formula.valid);
    const form = { ...referenceForm, additionalAmountType: "other", formulaReadable: formula.readable,
      otherDurationMode: formula.durationMode, otherDurationValue: formula.durationValue };
    const original = structuredClone(form);
    const built = buildCompensationRequest(form);
    assert.equal(built.valid, formula.valid);
    if (formula.valid) {
      assert.deepEqual(built.payload.additionalAmount, {
        type: "Other", basis: "MarketValue", formula: formula.normalized,
        ...(formula.durationMode === "None" ? {} : { duration: { mode: formula.durationMode, value: Number(formula.durationValue) } })
      });
    }
    assert.deepEqual(form, original);
  });
}

for (const basis of ["MarketValue", "FactorAdjustedValue", "BaseCompensation", "AmountAfterSolatium"]) {
  test(`switch Interest (${basis}) to Other ignores interest-only fields and preserves input`, () => {
    const interest = { ...referenceForm, calculatedOn: basis };
    assert.equal(buildCompensationRequest(interest).payload.additionalAmount.basis, basis);
    const other = { ...interest, additionalAmountType: "other", formulaReadable: cases[0].readable };
    const original = structuredClone(other);
    assert.deepEqual(buildCompensationRequest(other).payload.additionalAmount, {
      type: "Other", basis: "MarketValue", formula: cases[0].normalized
    });
    assert.deepEqual(other, original);
    assert.equal(buildCompensationRequest({ ...other, annualRate: "invalid", durationType: "date_range",
      startDate: "invalid", endDate: "invalid", calculatedOn: "obsolete" }).valid, true);
  });
}

test("formula replacement preserves the available-variable display order", () => {
  const original = structuredClone(ALL_RECOGNIZED_VARIABLES);
  convertFormulaReadableToInternal("After Solatium + Solatium");
  assert.deepEqual(ALL_RECOGNIZED_VARIABLES, original);
});
