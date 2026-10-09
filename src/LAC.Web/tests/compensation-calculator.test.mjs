import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  formatInr,
  numberToIndianWords,
  parseNumericInput
} from "../src/calculator/compensationFormatters.ts";
import {
  INITIAL_COMPENSATION_FORM_STATE,
  buildCompensationRequest,
  convertFormulaReadableToInternal,
  validateFormulaSyntax,
  getAvailableFormulaVariables,
  computeFormSignature,
  isResultValidForState
} from "../src/calculator/compensationContracts.ts";
import {
  AREA_UNITS,
  areaToSqm,
  sqmToArea
} from "../src/calculator/landConversions.js";
import { evaluateExpression } from "../src/calculator/arithmetic.js";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "../src");
const read = (relative) => readFileSync(path.join(root, relative), "utf8");

const compSource = read("calculator/CompensationCalculator.tsx");
const modalSource = read("calculator/CalculatorModal.tsx");
const contextSource = read("calculator/CalculatorContext.tsx");
const cssSource = read("calculator/calculator.css");
const appShellSource = read("components/AppShell.tsx");

// 1. Fresh defaults
test("1. fresh defaults: factor blank, solatium blank, interest rate blank, duration blank, official equivalent blank, trees/assets 0, additional type Interest", () => {
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.landArea, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.officialEquivalentArea, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.useOfficialEquivalent, false);
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.marketRate, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.multiplicationFactor, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.treesAndStructures, "0");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.solatiumPercentage, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.additionalAmountType, "interest");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.annualRate, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.durationValue, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.durationType, "days");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.calculatedOn, "MarketValue");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.formulaReadable, "");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.otherDurationMode, "None");
  assert.equal(INITIAL_COMPENSATION_FORM_STATE.otherDurationValue, "");

  // Verify non-intrusive placeholders in UI source
  assert.ok(compSource.includes('placeholder="e.g. 18"'));
  assert.ok(compSource.includes('placeholder="e.g. 53,00,000"'));
  assert.ok(compSource.includes('placeholder="e.g. 2"'));
  assert.ok(compSource.includes('placeholder="e.g. 100"'));
  assert.ok(compSource.includes('placeholder="e.g. 12"'));
  assert.ok(compSource.includes('placeholder="e.g. 30"'));
  assert.ok(compSource.includes('placeholder="e.g. 3.744"'));

  // Ensure 2, 100, 12, 30 are not hard-coded defaults in the form state
  assert.doesNotMatch(compSource, /useState<string>\("2"\)/);
  assert.doesNotMatch(compSource, /useState<string>\("100"\)/);
  assert.doesNotMatch(compSource, /useState<string>\("12"\)/);
  assert.doesNotMatch(compSource, /useState<string>\("30"\)/);
});

// 2. Exact backend request payload for Interest
test("2. exact backend request payload for Interest", () => {
  const state = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const { valid, errors, payload } = buildCompensationRequest(state);
  assert.equal(valid, true);
  assert.deepEqual(errors, {});
  assert.deepEqual(payload, {
    conversionProfile: "lac-delhi-v1",
    land: {
      area: 18,
      unit: "bigha"
    },
    marketRate: {
      amount: 5300000,
      perUnit: "acre"
    },
    multiplicationFactor: 2,
    assets: {
      treesAndStructures: 0
    },
    solatium: {
      percent: 100
    },
    additionalAmount: {
      type: "Interest",
      annualRatePercent: 12,
      duration: {
        mode: "Days",
        value: 30
      },
      basis: "MarketValue"
    }
  });
});

// 3. Exact backend request payload for Other without duration
test("3. exact backend request payload for Other without duration", () => {
  const state = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "other",
    formulaReadable: "Base Compensation + 50000",
    otherDurationMode: "None"
  };

  const { valid, payload } = buildCompensationRequest(state);
  assert.equal(valid, true);
  assert.deepEqual(payload.additionalAmount, {
    type: "Other",
    basis: "MarketValue",
    formula: "BASE_COMPENSATION + 50000"
  });
  assert.equal("duration" in payload.additionalAmount, false);
  assert.equal("annualRatePercent" in payload.additionalAmount, false);
});

// 4. Other + 30 Days + DAYS formula sends DurationMode Days
test("4. Other + 30 Days + DAYS formula sends DurationMode Days", () => {
  const state = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "other",
    formulaReadable: "MARKET_VALUE * 12 / 100 * (DAYS / 365)",
    otherDurationMode: "Days",
    otherDurationValue: "30"
  };

  const { valid, payload } = buildCompensationRequest(state);
  assert.equal(valid, true);
  assert.deepEqual(payload.additionalAmount, {
    type: "Other",
    basis: "MarketValue",
    duration: {
      mode: "Days",
      value: 30
    },
    formula: "MARKET_VALUE * 12 / 100 * (DAYS / 365)"
  });
});

// 5. Other + Months makes MONTHS available, not DAYS
test("5. Other + Months makes MONTHS available, not DAYS", () => {
  const monthChips = getAvailableFormulaVariables("Months");
  assert.ok(monthChips.some((c) => c.id === "MONTHS"));
  assert.ok(!monthChips.some((c) => c.id === "DAYS"));

  // Formula validation rejects DAYS if mode is Months
  const syntaxWithDays = validateFormulaSyntax("MARKET_VALUE * (DAYS / 365)", "Months");
  assert.equal(syntaxWithDays, "DAYS variable requires Days or Date Range duration.");

  // Formula validation accepts MONTHS when mode is Months
  const syntaxWithMonths = validateFormulaSyntax("MARKET_VALUE * 12 / 100 * (MONTHS / 12)", "Months");
  assert.equal(syntaxWithMonths, null);

  const state = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "other",
    formulaReadable: "MARKET_VALUE * 12 / 100 * (MONTHS / 12)",
    otherDurationMode: "Months",
    otherDurationValue: "6"
  };

  const { valid, payload } = buildCompensationRequest(state);
  assert.equal(valid, true);
  assert.deepEqual(payload.additionalAmount, {
    type: "Other",
    basis: "MarketValue",
    duration: {
      mode: "Months",
      value: 6
    },
    formula: "MARKET_VALUE * 12 / 100 * (MONTHS / 12)"
  });
});

// 6. Other + DateRange sends exact dates
test("6. Other + DateRange sends exact dates", () => {
  const state = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "other",
    formulaReadable: "MARKET_VALUE * 12 / 100 * (DAYS / 365)",
    otherDurationMode: "DateRange",
    otherStartDate: "2026-01-01",
    otherEndDate: "2026-01-31"
  };

  const { valid, payload } = buildCompensationRequest(state);
  assert.equal(valid, true);
  assert.deepEqual(payload.additionalAmount, {
    type: "Other",
    basis: "MarketValue",
    duration: {
      mode: "DateRange",
      startDate: "2026-01-01",
      endDate: "2026-01-31"
    },
    formula: "MARKET_VALUE * 12 / 100 * (DAYS / 365)"
  });
});

// 7. DAYS unavailable when Other duration=None
test("7. DAYS unavailable when Other duration=None", () => {
  const noneChips = getAvailableFormulaVariables("None");
  assert.ok(!noneChips.some((c) => c.id === "DAYS"));

  const err = validateFormulaSyntax("MARKET_VALUE * (DAYS / 365)", "None");
  assert.equal(err, "DAYS variable requires Days or Date Range duration.");
});

// 8. MONTHS unavailable when Other duration=None
test("8. MONTHS unavailable when Other duration=None", () => {
  const noneChips = getAvailableFormulaVariables("None");
  assert.ok(!noneChips.some((c) => c.id === "MONTHS"));

  const err = validateFormulaSyntax("MARKET_VALUE * (MONTHS / 12)", "None");
  assert.equal(err, "MONTHS variable requires Months duration.");
});

// 9. No locally calculated authoritative monetary preview
test("9. no locally calculated authoritative monetary preview", () => {
  assert.doesNotMatch(
    compSource,
    /evaluateExpression/,
    "CompensationCalculator must not import or run local evaluateExpression math engine"
  );
  assert.doesNotMatch(
    compSource,
    /estMarketValue|estFactorValue|estBaseComp|estSolatium/i,
    "CompensationCalculator must not duplicate domain calculation variables locally"
  );
  assert.ok(
    compSource.includes("Amount will be calculated by the server."),
    "Formula preview must state that amount will be calculated by the server before computation"
  );
});

// 10. Final Additional Amount renders backend response
test("10. final Additional Amount renders backend response", () => {
  assert.ok(
    compSource.includes("activeResult.additionalAmount.amount") || compSource.includes("result.additionalAmount.amount"),
    "Component must render backend additionalAmount.amount"
  );
  assert.ok(
    compSource.includes("formatInr(activeResult.additionalAmount.amount)") || compSource.includes("formatInr(result.additionalAmount.amount)"),
    "Component must format backend additional amount via formatInr"
  );
});

// 11. Official override omitted unless explicitly enabled
test("11. official override omitted unless explicitly enabled", () => {
  const stateDisabled = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    annualRate: "12",
    durationValue: "30",
    useOfficialEquivalent: false,
    officialEquivalentArea: "3.744"
  };

  const resDisabled = buildCompensationRequest(stateDisabled);
  assert.equal(resDisabled.valid, true);
  assert.equal("equivalentAreaInRateUnit" in resDisabled.payload.land, false);

  const stateEnabled = {
    ...stateDisabled,
    useOfficialEquivalent: true,
    officialEquivalentArea: "3.744"
  };

  const resEnabled = buildCompensationRequest(stateEnabled);
  assert.equal(resEnabled.valid, true);
  assert.equal(resEnabled.payload.land.equivalentAreaInRateUnit, 3.744);
});

// 12. Enabled official override blank blocks submit
test("12. enabled official override blank blocks submit", () => {
  const stateBlank = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    annualRate: "12",
    durationValue: "30",
    useOfficialEquivalent: true,
    officialEquivalentArea: ""
  };

  const res = buildCompensationRequest(stateBlank);
  assert.equal(res.valid, false);
  assert.ok(res.errors["land.equivalentAreaInRateUnit"]);
  assert.ok(res.errors["land.equivalentAreaInRateUnit"].includes("official equivalent area"));
});

// 13. Sample acceptance
test("13. sample acceptance: 18 Bigha, 3.744 Acre override, 53L, Factor 2, Assets 0, Solatium 100, Interest 12, 30 Days => ₹7,95,68,513.75", () => {
  const acceptanceState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    useOfficialEquivalent: true,
    officialEquivalentArea: "3.744",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const { valid, payload } = buildCompensationRequest(acceptanceState);
  assert.equal(valid, true);
  assert.deepEqual(payload, {
    conversionProfile: "lac-delhi-v1",
    land: {
      area: 18,
      unit: "bigha",
      equivalentAreaInRateUnit: 3.744
    },
    marketRate: {
      amount: 5300000,
      perUnit: "acre"
    },
    multiplicationFactor: 2,
    assets: {
      treesAndStructures: 0
    },
    solatium: {
      percent: 100
    },
    additionalAmount: {
      type: "Interest",
      annualRatePercent: 12,
      duration: {
        mode: "Days",
        value: 30
      },
      basis: "MarketValue"
    }
  });

  // Authoritative display formatting assertions:
  assert.equal(formatInr("79568513.75"), "₹7,95,68,513.75");
  assert.equal(
    formatInr({ precise: "79568513.75342465753424657534", display: "79568513.75" }),
    "₹7,95,68,513.75"
  );
  assert.equal(
    formatInr({ precise: "195713.75342465753424657534249", display: "195713.75" }),
    "₹1,95,713.75"
  );
  assert.equal(
    formatInr({ precise: "19843200", display: "19843200.00" }),
    "₹1,98,43,200"
  );
  assert.equal(
    formatInr({ precise: "39686400", display: "39686400.00" }),
    "₹3,96,86,400"
  );
  assert.equal(
    formatInr({ precise: "79372800", display: "79372800.00" }),
    "₹7,93,72,800"
  );
});

// 14. Existing calculator regression
test("14. existing calculator regression", () => {
  assert.equal(AREA_UNITS.bigha.sqm, 843);
  assert.equal(AREA_UNITS.biswa.sqm, 42.15);
  assert.equal(AREA_UNITS.biswansi.sqm, 2.1075);
  assert.equal(AREA_UNITS.acre.sqm, 4046.8564224);
  assert.equal(evaluateExpression("2 + 3 * 4"), 14);

  assert.ok(modalSource.includes("<AreaConverter />"));
  assert.ok(modalSource.includes("<NormalCalculator />"));
  assert.ok(modalSource.includes("<RevenueArithmetic />"));
  assert.ok(modalSource.includes("<LengthConverter />"));
});

// 15. Home/Court/Matter/Land preservation
test("15. Home/Court/Matter/Land preservation", () => {
  const homeSource = read("home/Home.tsx");
  const courtSource = read("court/CourtDirectory.tsx");
  const matterSource = read("matter/MatterDirectory.tsx");
  const landSource = read("land/LandRecordsHierarchy.tsx");

  assert.ok(homeSource.includes("Land &amp; Area Calculator") || homeSource.includes("Land & Area Calculator"));
  assert.ok(courtSource.includes("Court matters"));
  assert.ok(matterSource.includes("MattersDirectory") || matterSource.includes("Matter Directory") || matterSource.includes("Matter"));
  assert.ok(landSource.includes("Land Records Hierarchy") || landSource.includes("villages"));
});

// 16. HTTP status code handling (401, 413, 415, 400 field mapping)
test("16. HTTP status code handling (401, 413, 415, 400 field mapping)", () => {
  assert.ok(
    compSource.includes("response.status === 401"),
    "Component must handle 401 session expiration"
  );
  assert.ok(
    compSource.includes("response.status === 413"),
    "Component must handle 413 payload too large"
  );
  assert.ok(
    compSource.includes("response.status === 415"),
    "Component must handle 415 unsupported media type"
  );
  assert.ok(
    compSource.includes("flatErrors"),
    "Component must map backend validation errors to form fields"
  );
  assert.doesNotMatch(
    compSource,
    /\balert\(/,
    "Component must not use window alert() calls"
  );
});

// 17. Currency and Indian numbering formatters
test("17. currency and Indian numbering formatters", () => {
  assert.equal(formatInr(79568513.75), "₹7,95,68,513.75");
  assert.equal(formatInr(19843200), "₹1,98,43,200");
  assert.equal(formatInr(0), "₹0");
  assert.equal(formatInr(null), "—");

  const words = numberToIndianWords({ precise: "79568513.7534", display: "79568513.75" });
  assert.ok(words.includes("Seven Crore"));
  assert.ok(words.includes("Ninety Five Lakh"));
  assert.ok(words.includes("Sixty Eight Thousand"));
  assert.ok(words.includes("Five Hundred Thirteen"));
  assert.ok(words.includes("Seventy Five Paise"));
});

// 18. Reset handler clears state and returns to initial
test("18. reset handler clears state and returns to initial", () => {
  assert.ok(compSource.includes("handleReset"));
  assert.ok(compSource.includes("INITIAL_COMPENSATION_FORM_STATE"));
  assert.ok(compSource.includes("setResult(null)"));
  assert.ok(compSource.includes("setFieldErrors({})"));
});

// 19. 1366x768 layout styles and modal classes
test("19. 1366x768 layout styles and modal classes", () => {
  assert.ok(cssSource.includes(".comp-modal-wide"));
  assert.ok(cssSource.includes(".comp-grid-layout"));
  assert.ok(cssSource.includes(".comp-formula-duration-row"));
  assert.ok(cssSource.includes("@media (max-width: 860px)"));
});

// 20. No Save, Award linking, or calculation history
test("20. private server history remains separate from Award linking and localStorage", () => {
  assert.doesNotMatch(
    compSource,
    /Link to Award|Save to Award|localStorage/i,
    "Private history must not mutate Awards or use browser storage as its database"
  );
  assert.match(compSource, /\/api\/calculators\/compensation\/history/);
  assert.match(compSource, /saved\.saved !== true/);
  assert.match(compSource, /createCalculationSubmissionKey\(\)/);
});

// 21. successful result + Area edit => old result invalidated
test("21. successful result + Area edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const calculatedSig = computeFormSignature(baseState);
  assert.equal(isResultValidForState(calculatedSig, baseState), true);

  // Editing land area invalidates the result signature immediately
  const editedAreaState = { ...baseState, landArea: "20" };
  assert.equal(isResultValidForState(calculatedSig, editedAreaState), false);
  assert.notEqual(computeFormSignature(editedAreaState), calculatedSig);

  // Editing land area unit also invalidates the result signature
  const editedUnitState = { ...baseState, landAreaUnit: "acre" };
  assert.equal(isResultValidForState(calculatedSig, editedUnitState), false);
  assert.notEqual(computeFormSignature(editedUnitState), calculatedSig);

  // Verify component UI handles stale result with activeResult and pending message
  assert.ok(compSource.includes("calculatedSignature !== currentSignature"));
  assert.ok(compSource.includes("Inputs changed. Calculate again to see the updated compensation."));
});

// 22. successful result + Factor edit => old result invalidated
test("22. successful result + Factor edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const calculatedSig = computeFormSignature(baseState);
  assert.equal(isResultValidForState(calculatedSig, baseState), true);

  // Editing factor immediately invalidates the signature
  const editedFactorState = { ...baseState, multiplicationFactor: "1.5" };
  assert.equal(isResultValidForState(calculatedSig, editedFactorState), false);
  assert.notEqual(computeFormSignature(editedFactorState), calculatedSig);

  // Editing trees & structures immediately invalidates the signature
  const editedTreesState = { ...baseState, treesAndStructures: "50000" };
  assert.equal(isResultValidForState(calculatedSig, editedTreesState), false);
  assert.notEqual(computeFormSignature(editedTreesState), calculatedSig);
});

// 23. successful result + Solatium edit => old result invalidated
test("23. successful result + Solatium edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const calculatedSig = computeFormSignature(baseState);
  assert.equal(isResultValidForState(calculatedSig, baseState), true);

  // Editing solatium percentage immediately invalidates the signature
  const editedSolatiumState = { ...baseState, solatiumPercentage: "30" };
  assert.equal(isResultValidForState(calculatedSig, editedSolatiumState), false);
  assert.notEqual(computeFormSignature(editedSolatiumState), calculatedSig);
});

// 24. successful result + Interest / duration edit => old result invalidated
test("24. successful result + Interest / duration edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const calculatedSig = computeFormSignature(baseState);

  // Editing annual interest rate invalidates
  const editedRateState = { ...baseState, annualRate: "9" };
  assert.equal(isResultValidForState(calculatedSig, editedRateState), false);

  // Editing duration value invalidates
  const editedDurationValState = { ...baseState, durationValue: "45" };
  assert.equal(isResultValidForState(calculatedSig, editedDurationValState), false);

  // Editing duration mode/type invalidates
  const editedDurationTypeState = { ...baseState, durationType: "months", durationValue: "1" };
  assert.equal(isResultValidForState(calculatedSig, editedDurationTypeState), false);

  // Editing calculatedOn basis invalidates
  const editedBasisState = { ...baseState, calculatedOn: "BaseCompensation" };
  assert.equal(isResultValidForState(calculatedSig, editedBasisState), false);
});

// 25. successful result + Other formula edit => old result invalidated
test("25. successful result + Other formula edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "other",
    formulaReadable: "MARKET_VALUE * 12 / 100 * (DAYS / 365)",
    otherDurationMode: "Days",
    otherDurationValue: "30"
  };

  const calculatedSig = computeFormSignature(baseState);
  assert.equal(isResultValidForState(calculatedSig, baseState), true);

  // Changing formula expression text invalidates
  const editedFormulaState = { ...baseState, formulaReadable: "BASE_COMPENSATION + 50000" };
  assert.equal(isResultValidForState(calculatedSig, editedFormulaState), false);

  // Changing Other duration value invalidates
  const editedDurationState = { ...baseState, otherDurationValue: "60" };
  assert.equal(isResultValidForState(calculatedSig, editedDurationState), false);

  // Changing Other duration mode invalidates
  const editedModeState = { ...baseState, otherDurationMode: "None", otherDurationValue: "" };
  assert.equal(isResultValidForState(calculatedSig, editedModeState), false);

  // Switching additional amount type from other to interest invalidates
  const switchedTypeState = { ...baseState, additionalAmountType: "interest", annualRate: "12", durationValue: "30" };
  assert.equal(isResultValidForState(calculatedSig, switchedTypeState), false);
});

// 26. successful result + official equivalent toggle / area edit => old result invalidated
test("26. successful result + official equivalent toggle / area edit => old result invalidated", () => {
  const baseState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    useOfficialEquivalent: true,
    officialEquivalentArea: "3.744",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const calculatedSig = computeFormSignature(baseState);
  assert.equal(isResultValidForState(calculatedSig, baseState), true);

  // Toggling off the official equivalent override invalidates
  const toggledOffState = { ...baseState, useOfficialEquivalent: false };
  assert.equal(isResultValidForState(calculatedSig, toggledOffState), false);

  // Changing the official equivalent area value invalidates
  const editedEquivAreaState = { ...baseState, officialEquivalentArea: "3.750" };
  assert.equal(isResultValidForState(calculatedSig, editedEquivAreaState), false);
});

// 27. client-side validation failure => no stale result
test("27. client-side validation failure => no stale result", () => {
  // Verifies that in CompensationCalculator, if client-side validation fails,
  // old result is cleared (setResult(null), setCalculatedSignature(null), setHasCalculated(false))
  assert.ok(
    compSource.includes("if (!validation.valid || !validation.payload)"),
    "Component checks client validation before request"
  );
  assert.ok(
    compSource.includes("setFieldErrors(validation.errors)"),
    "Component surfaces validation errors"
  );
  // Source proves setResult(null) and setCalculatedSignature(null) are called in the validation failure branch
  const validationBranch = compSource.slice(
    compSource.indexOf("if (!validation.valid || !validation.payload)"),
    compSource.indexOf("setLoading(true)")
  );
  assert.ok(validationBranch.includes("setResult(null)"), "Validation failure must clear result");
  assert.ok(validationBranch.includes("setCalculatedSignature(null)"), "Validation failure must clear signature");
  assert.ok(validationBranch.includes("setHasCalculated(false)"), "Validation failure must reset hasCalculated");
});

// 28. backend error response => no stale result
test("28. backend error response => no stale result", () => {
  // Source proves setResult(null) and setCalculatedSignature(null) are called when response is not ok
  const errorBranch = compSource.slice(
    compSource.indexOf("if (!response.ok)"),
    compSource.indexOf("const data: CompensationResponse = await response.json()")
  );
  assert.ok(errorBranch.includes("setResult(null)"), "HTTP error response must clear result");
  assert.ok(errorBranch.includes("setCalculatedSignature(null)"), "HTTP error response must clear signature");
  assert.ok(errorBranch.includes("setHasCalculated(false)"), "HTTP error response must reset hasCalculated");

  // Also catch block must clear result
  const catchBranch = compSource.slice(
    compSource.indexOf("catch (err: unknown)"),
    compSource.indexOf("finally")
  );
  assert.ok(catchBranch.includes("setResult(null)"), "Network error catch must clear result");
  assert.ok(catchBranch.includes("setCalculatedSignature(null)"), "Network error catch must clear signature");
  assert.ok(catchBranch.includes("setHasCalculated(false)"), "Network error catch must reset hasCalculated");
});

// 29. field edit does not trigger calculation request
test("29. field edit does not trigger calculation request", () => {
  // Verifies that no onChange handler triggers handleCalculate, compute, or fetch
  const onChangeMatches = compSource.match(/onChange=\{[^}]+\}/g) || [];
  for (const match of onChangeMatches) {
    assert.doesNotMatch(
      match,
      /handleCalculate|fetch\(/,
      `onChange handler (${match}) must not trigger calculation request`
    );
  }
  // The only place handleCalculate is triggered is onSubmit
  assert.ok(compSource.includes('onSubmit={handleCalculate}'));
});

// 30. fresh Calculate displays server response
test("30. fresh Calculate displays server response", () => {
  const freshState = {
    ...INITIAL_COMPENSATION_FORM_STATE,
    landArea: "18",
    landAreaUnit: "bigha",
    marketRate: "5300000",
    marketRateUnit: "acre",
    useOfficialEquivalent: true,
    officialEquivalentArea: "3.744",
    multiplicationFactor: "2",
    treesAndStructures: "0",
    solatiumPercentage: "100",
    additionalAmountType: "interest",
    annualRate: "12",
    durationType: "days",
    durationValue: "30",
    calculatedOn: "MarketValue"
  };

  const freshSig = computeFormSignature(freshState);
  assert.equal(isResultValidForState(freshSig, freshState), true);

  // When response arrives, setCalculatedSignature(requestSignature) pairs with setResult(data)
  assert.ok(
    compSource.includes("setCalculatedSignature(requestSignature)"),
    "Component records request signature on successful calculate"
  );
  assert.ok(
    compSource.includes("setResult(data)"),
    "Component records server response on successful calculate"
  );
  assert.ok(
    compSource.includes("setHasCalculated(true)"),
    "Component marks calculation as completed"
  );
  assert.ok(
    compSource.includes("const isStale = Boolean(result && calculatedSignature && calculatedSignature !== currentSignature)"),
    "Component derives freshness synchronously"
  );
});

