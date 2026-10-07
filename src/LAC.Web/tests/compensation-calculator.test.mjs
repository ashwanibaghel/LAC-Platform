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

// 1. Compensation Calculator accessible from global Calculator
test("1. Compensation Calculator accessible from global Calculator", () => {
  assert.ok(
    modalSource.includes('["compensation", "Compensation Calculator"]'),
    "CalculatorModal navigation must include Compensation Calculator tab"
  );
  assert.ok(
    modalSource.includes("<CompensationCalculator />"),
    "CalculatorModal must render CompensationCalculator component"
  );
  assert.ok(
    appShellSource.includes("openCalculator"),
    "AppShell header must have openCalculator trigger"
  );
  assert.ok(
    contextSource.includes("openCalculator"),
    "CalculatorContext must export openCalculator function"
  );
});

// 2. Existing Area Calculator still works
test("2. existing Area Calculator still works", () => {
  assert.ok(
    modalSource.includes("<AreaConverter />"),
    "CalculatorModal must preserve AreaConverter tab and component"
  );
  assert.ok(
    modalSource.includes("<NormalCalculator />"),
    "CalculatorModal must preserve NormalCalculator tab and component"
  );
  assert.ok(
    modalSource.includes("<RevenueArithmetic />"),
    "CalculatorModal must preserve RevenueArithmetic tab and component"
  );
  assert.equal(areaToSqm(1, "bigha"), 843);
  assert.equal(areaToSqm(1, "hectare"), 10000);
});

// 3. Area + unit entry
test("3. area + unit entry", () => {
  assert.ok(compSource.includes("Land Area"), "Form must have Land Area label");
  assert.ok(
    compSource.includes('type="number"'),
    "Area input must accept numeric input"
  );
  assert.ok(
    compSource.includes("setLandAreaUnit"),
    "Area unit dropdown must update landAreaUnit state"
  );
  assert.ok(
    compSource.includes("AREA_UNITS"),
    "Area unit dropdown must reuse canonical AREA_UNITS"
  );
});

// 4. Rate + per-unit dropdown
test("4. rate + per-unit dropdown", () => {
  assert.ok(compSource.includes("Market Rate"), "Form must have Market Rate label");
  assert.ok(
    compSource.includes('Per {u.label}'),
    "Rate dropdown must display Per unit options"
  );
  assert.ok(
    compSource.includes('className="comp-currency-prefix">₹'),
    "Market Rate must have INR ₹ visual adornment"
  );
});

// 5. Converted-area helper & official equivalent area override
test("5. converted-area helper", () => {
  assert.ok(
    compSource.includes("autoConvertedStr"),
    "Component must compute subtle automatic conversion helper"
  );
  assert.ok(
    compSource.includes("Auto conversion:"),
    "Component must render Auto conversion label"
  );
  assert.ok(
    compSource.includes("Use official/document equivalent area"),
    "Component must provide option to use official/document equivalent area"
  );
  assert.ok(
    compSource.includes("Official equivalent area"),
    "Component must provide input for official equivalent area"
  );
  assert.ok(
    compSource.includes("Use this only when the Award/official document specifies a different equivalent area"),
    "Component must provide official equivalent helper guidance"
  );
  assert.ok(
    compSource.includes("equivalentAreaInRateUnit"),
    "Component must send land.equivalentAreaInRateUnit when enabled"
  );

  // Verify existing Area Calculator conversion semantics:
  const bighaSqm = areaToSqm(18, "bigha");
  const convertedAcre = sqmToArea(bighaSqm, "acre");
  assert.ok(convertedAcre > 3.7495 && convertedAcre < 3.7496);
  assert.equal(convertedAcre.toFixed(6), "3.749577");
  assert.ok(
    compSource.includes("areaToSqm") && compSource.includes("sqmToArea"),
    "Conversion helper must reuse existing landConversions semantics"
  );
});

// 6. Factor input
test("6. factor input", () => {
  assert.ok(
    compSource.includes("Multiplication Factor"),
    "Form must have Multiplication Factor label"
  );
  assert.ok(
    compSource.includes('placeholder="e.g. 2"'),
    "Factor input must show neutral placeholder without legal mandates"
  );
  assert.doesNotMatch(
    compSource,
    /mandatory factor|statutory factor 2/i,
    "Factor wording must remain neutral"
  );
});

// 7. Trees/structure zero/Nil UX
test("7. trees/structure zero/Nil UX", () => {
  assert.ok(
    compSource.includes("Trees &amp; Structures") || compSource.includes("Trees & Structures"),
    "Form must have Trees & Structures label"
  );
  assert.ok(
    compSource.includes("Nil / ₹0"),
    "Trees & Structures must have a one-click Nil / ₹0 action button"
  );
  assert.ok(
    compSource.includes('setTreesAndStructures("0")'),
    "Nil action must store and send numeric 0"
  );
});

// 8. Solatium input
test("8. solatium input", () => {
  assert.ok(
    compSource.includes("Solatium Rate (%)"),
    "Form must have neutral Solatium Rate label"
  );
  assert.doesNotMatch(
    compSource,
    /statutory solatium|legally required 100/i,
    "Solatium wording must not assume a statutory requirement"
  );
});

// 9. Interest default
test("9. Interest default", () => {
  assert.ok(
    compSource.includes('useState<"interest" | "other">("interest")'),
    "Additional amount type must default to interest"
  );
  assert.ok(
    compSource.includes('useState<string>("12")'),
    "Annual interest rate must default to 12%"
  );
});

// 10. Days duration
test("10. days duration", () => {
  assert.ok(
    compSource.includes('<option value="days">Days</option>'),
    "Duration unit dropdown must include Days"
  );
  assert.ok(
    compSource.includes('durationType === "days"'),
    "Component must handle Days duration"
  );
});

// 11. Months duration
test("11. months duration", () => {
  assert.ok(
    compSource.includes('<option value="months">Months</option>'),
    "Duration unit dropdown must include Months"
  );
  assert.ok(
    compSource.includes('durationType === "months"'),
    "Component must handle Months duration"
  );
});

// 12. Date range duration
test("12. date range duration", () => {
  assert.ok(
    compSource.includes('<option value="date_range">Date range</option>'),
    "Duration unit dropdown must include Date range"
  );
  assert.ok(
    compSource.includes('type="date"'),
    "Component must provide Start and End date pickers for date range"
  );
  assert.ok(
    compSource.includes("dateRangeDays"),
    "Component must compute elapsed days for date range"
  );
});

// 13. Calculated-on dropdown
test("13. calculated-on dropdown", () => {
  assert.ok(
    compSource.includes("Calculated On"),
    "Interest section must have Calculated On dropdown"
  );
  assert.ok(
    compSource.includes(">Market Value</option>"),
    "Calculated On options must include Market Value"
  );
  assert.ok(
    compSource.includes(">Factor Value</option>"),
    "Calculated On options must include Factor Value"
  );
  assert.ok(
    compSource.includes(">Base Compensation</option>"),
    "Calculated On options must include Base Compensation"
  );
});

// 14. Other formula mode hidden until selected
test("14. Other formula mode hidden until selected", () => {
  assert.ok(
    compSource.includes('additionalAmountType === "interest" ?'),
    "Formula builder must be hidden while Interest is selected"
  );
  assert.ok(
    compSource.includes('comp-formula-builder'),
    "Formula builder section must be conditioned on Other selection"
  );
});

// 15. Formula variable chips / operators
test("15. formula variable chips/operators", () => {
  assert.ok(
    compSource.includes("FORMULA_VARIABLES"),
    "Formula builder must expose whitelisted FORMULA_VARIABLES"
  );
  assert.ok(
    compSource.includes("FORMULA_OPERATORS"),
    "Formula builder must expose whitelisted FORMULA_OPERATORS"
  );
  for (const label of [
    "Market Value",
    "Factor Value",
    "Assets",
    "Base Compensation",
    "Solatium",
    "After Solatium",
    "Days",
    "Months"
  ]) {
    assert.ok(
      compSource.includes(`label: "${label}"`),
      `Formula builder must have chip for ${label}`
    );
  }
});

// 16. Formula preview
test("16. formula preview", () => {
  assert.ok(
    compSource.includes("formulaPreviewAmount"),
    "Component must calculate live formula preview amount"
  );
  // Test reference formula math:
  // Market Value: 19843200, Days: 30
  // Formula: Market Value * 12 / 100 * Days / 365
  const sampleExpr = "19843200 * 12 / 100 * 30 / 365";
  const result = evaluateExpression(sampleExpr);
  assert.equal(result.toFixed(2), "195713.75");
  assert.equal(formatInr(result), "₹1,95,713.75");
});

// 17. Backend validation errors
test("17. backend validation errors", () => {
  assert.ok(
    compSource.includes("fieldErrors"),
    "Component must maintain field-specific errors state"
  );
  assert.ok(
    compSource.includes("generalError"),
    "Component must maintain general error state"
  );
  assert.ok(
    compSource.includes('role="alert"'),
    "Error banner and field errors must be accessibly marked with role=alert"
  );
  assert.doesNotMatch(
    compSource,
    /\balert\(/,
    "Component must not use browser alert() window popups"
  );
});

// 18. Final compensation rendering & distinct area breakdown
test("18. final compensation rendering", () => {
  assert.ok(
    compSource.includes("FINAL COMPENSATION"),
    "Result panel must prominently show FINAL COMPENSATION banner"
  );
  assert.ok(
    compSource.includes("comp-final-amount"),
    "Result panel must have dedicated large final amount display"
  );
  assert.ok(
    compSource.includes("Calculation Breakdown"),
    "Result panel must render clean calculation breakdown"
  );
  assert.ok(
    compSource.includes("Entered land:"),
    "Result breakdown must show Entered land label"
  );
  assert.ok(
    compSource.includes("Automatic conversion:"),
    "Result breakdown must show Automatic conversion label"
  );
  assert.ok(
    compSource.includes("Applied for calculation:"),
    "Result breakdown must show Applied for calculation label"
  );
  assert.ok(
    compSource.includes("Official equivalent entered"),
    "Result breakdown must distinguish official equivalent override"
  );
});

// 19. Indian currency formatting
test("19. Indian currency formatting", () => {
  // Direct numbers
  assert.equal(formatInr(79568513.75), "₹7,95,68,513.75");
  assert.equal(formatInr(19843200), "₹1,98,43,200");
  assert.equal(formatInr(39686400), "₹3,96,86,400");
  assert.equal(formatInr(0), "₹0");
  assert.equal(formatInr(195713.75), "₹1,95,713.75");
  assert.equal(formatInr(null), "—");

  // Backend MoneyValue { precise, display } objects
  assert.equal(formatInr({ precise: "79568513.75342465753424657534", display: "79568513.75" }), "₹7,95,68,513.75");
  assert.equal(formatInr({ precise: "19843200", display: "19843200.00" }), "₹1,98,43,200");
  assert.equal(formatInr({ precise: "0", display: "0.00" }), "₹0");
  assert.equal(formatInr({ precise: "195713.75342465753424657534249", display: "195713.75" }), "₹1,95,713.75");
});

// 20. Amount in words rendering when returned
test("20. amount in words rendering when returned", () => {
  assert.ok(
    compSource.includes("result.finalAmountInWords || numberToIndianWords(result.finalCompensation)"),
    "Result panel must display amount in words from backend or format helper"
  );
  const words = numberToIndianWords({ precise: "79568513.7534", display: "79568513.75" });
  assert.ok(words.includes("Seven Crore"));
  assert.ok(words.includes("Ninety Five Lakh"));
  assert.ok(words.includes("Sixty Eight Thousand"));
  assert.ok(words.includes("Five Hundred Thirteen"));
  assert.ok(words.includes("Seventy Five Paise"));
});

// 21. Reset
test("21. Reset", () => {
  assert.ok(
    compSource.includes("handleReset"),
    "Component must have Reset handler"
  );
  assert.ok(
    compSource.includes('setResult(null)'),
    "Reset handler must clear calculation results and return to empty panel"
  );
});

// 22. No Save/Award link/history
test("22. no Save/Award link/history", () => {
  assert.doesNotMatch(
    compSource,
    /Save Calculation|Link to Award|Save to Award|Calculation History|localStorage/i,
    "Compensation Calculator must not have Save, Link Award, or history actions"
  );
});

// 23. 1366x768 no broken layout
test("23. 1366x768 no broken layout", () => {
  assert.ok(
    cssSource.includes(".comp-modal-wide"),
    "CSS must define wide modal container for desktop viewports"
  );
  assert.ok(
    cssSource.includes(".comp-grid-layout"),
    "CSS must define two-column grid layout"
  );
  assert.ok(
    cssSource.includes("@media (max-width: 860px)"),
    "CSS must handle stacked responsiveness for smaller widths"
  );
});

// 24. Keyboard/focus basics
test("24. keyboard/focus basics", () => {
  assert.ok(
    compSource.includes("htmlFor="),
    "Inputs must be properly associated with accessible labels"
  );
  assert.ok(
    compSource.includes("onWheel={(e) => e.currentTarget.blur()}"),
    "Number inputs must prevent accidental mouse-wheel number changes"
  );
  assert.ok(
    cssSource.includes(":focus"),
    "CSS must provide visible focus indicators"
  );
});

// 25. Existing Calculator regression
test("25. existing Calculator regression", () => {
  // Canonical Delhi revenue constants
  assert.equal(AREA_UNITS.bigha.sqm, 843);
  assert.equal(AREA_UNITS.biswa.sqm, 42.15);
  assert.equal(AREA_UNITS.biswansi.sqm, 2.1075);
  assert.equal(AREA_UNITS.acre.sqm, 4046.8564224);
  assert.equal(evaluateExpression("2 + 3 * 4"), 14);
});

// 26. Home/Court/Matter/Land preservation tests
test("26. Home/Court/Matter/Land preservation tests", () => {
  const homeSource = read("home/Home.tsx");
  const courtSource = read("court/CourtDirectory.tsx");
  const matterSource = read("matter/MatterDirectory.tsx");
  const landSource = read("land/LandRecordsHierarchy.tsx");

  assert.ok(homeSource.includes("Land &amp; Area Calculator") || homeSource.includes("Land & Area Calculator"));
  assert.ok(courtSource.includes("Court matters"));
  assert.ok(matterSource.includes("MattersDirectory") || matterSource.includes("Matter Directory") || matterSource.includes("Matter"));
  assert.ok(landSource.includes("Land Records Hierarchy") || landSource.includes("villages"));
});
