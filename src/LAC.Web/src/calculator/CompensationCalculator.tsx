import React, { useId, useMemo, useState } from "react";
import { AREA_UNITS, areaToSqm, sqmToArea } from "./landConversions";
import { evaluateExpression } from "./arithmetic";
import {
  formatInr,
  numberToIndianWords,
  parseNumericInput,
  type MoneyInput
} from "./compensationFormatters";

export interface MoneyValue {
  precise: string;
  display: string;
}

export interface AreaCalculation {
  enteredArea: string;
  unit: string;
  rateUnit: string;
  profile: string;
  profileConvertedArea: string;
  appliedArea: string;
  usesExplicitEquivalentArea: boolean;
}

export interface DurationCalculation {
  mode: string;
  value?: string;
  fraction?: string;
  convention?: string;
}

export interface AdditionalAmountCalculation {
  type: string;
  basis?: string;
  annualRatePercent?: string;
  duration?: DurationCalculation;
  normalizedFormula?: string;
  substitutedFormula?: string;
  variables?: Record<string, string>;
  amount: MoneyValue | MoneyInput;
}

export interface CalculationBreakdownItem {
  label: string;
  amount: MoneyInput;
  detail?: string;
}

export interface CompensationResponse {
  currency?: string;
  area?: AreaCalculation;
  rate?: string;
  multiplicationFactor?: string;
  solatiumPercent?: string;
  marketValue?: MoneyValue;
  factorAdjustedValue?: MoneyValue;
  treesAndStructures?: MoneyValue;
  baseCompensation?: MoneyValue;
  solatiumAmount?: MoneyValue;
  amountAfterSolatium?: MoneyValue;
  additionalAmount?: AdditionalAmountCalculation;
  finalCompensation?: MoneyValue;
  finalAmountInWords?: string;
  roundingPolicy?: string;
  breakdown?: CalculationBreakdownItem[];
  items?: CalculationBreakdownItem[];
  trace?: Array<{
    name: string;
    formula: string;
    substitutedFormula: string;
    result: string;
  }>;
}

export const FORMULA_VARIABLES = [
  { id: "MARKET_VALUE", label: "Market Value" },
  { id: "FACTOR_VALUE", label: "Factor Value" },
  { id: "ASSET_VALUE", label: "Assets" },
  { id: "BASE_COMPENSATION", label: "Base Compensation" },
  { id: "SOLATIUM_AMOUNT", label: "Solatium" },
  { id: "AFTER_SOLATIUM", label: "After Solatium" },
  { id: "DAYS", label: "Days" },
  { id: "MONTHS", label: "Months" }
] as const;

export const FORMULA_OPERATORS = ["+", "-", "×", "÷", "(", ")"] as const;

export function CompensationCalculator() {
  const formId = useId();

  // Step 1: Land & Rate
  const [landArea, setLandArea] = useState<string>("");
  const [landAreaUnit, setLandAreaUnit] = useState<string>("bigha");
  const [marketRate, setMarketRate] = useState<string>("");
  const [marketRateUnit, setMarketRateUnit] = useState<string>("acre");
  const [useOfficialEquivalent, setUseOfficialEquivalent] = useState<boolean>(false);
  const [officialEquivalentArea, setOfficialEquivalentArea] = useState<string>("3.744");

  // Step 2: Adjustments
  const [multiplicationFactor, setMultiplicationFactor] = useState<string>("2");
  const [treesAndStructures, setTreesAndStructures] = useState<string>("0");

  // Step 3: Solatium
  const [solatiumPercentage, setSolatiumPercentage] = useState<string>("100");

  // Step 4: Additional Amount
  const [additionalAmountType, setAdditionalAmountType] = useState<"interest" | "other">("interest");
  const [annualRate, setAnnualRate] = useState<string>("12");
  const [durationType, setDurationType] = useState<"days" | "months" | "date_range">("days");
  const [durationValue, setDurationValue] = useState<string>("30");
  const [startDate, setStartDate] = useState<string>("");
  const [endDate, setEndDate] = useState<string>("");
  const [calculatedOn, setCalculatedOn] = useState<"MarketValue" | "FactorAdjustedValue" | "BaseCompensation" | "AmountAfterSolatium">("MarketValue");

  // Formula Mode (Other)
  const [formulaReadable, setFormulaReadable] = useState<string>("Market Value × 12 ÷ 100 × Days ÷ 365");

  // Calculation state & errors
  const [loading, setLoading] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string>("");
  const [result, setResult] = useState<CompensationResponse | null>(null);

  // Automatic conversion calculation using canonical Area Calculator constants
  const areaNumber = parseNumericInput(landArea);
  const isDifferentUnit = landAreaUnit !== marketRateUnit;

  const autoConvertedArea = useMemo(() => {
    if (!Number.isFinite(areaNumber) || areaNumber <= 0 || !isDifferentUnit) return null;
    const sqm = areaToSqm(areaNumber, landAreaUnit);
    if (sqm === null) return null;
    return sqmToArea(sqm, marketRateUnit);
  }, [areaNumber, landAreaUnit, marketRateUnit, isDifferentUnit]);

  const autoConvertedStr = useMemo(() => {
    if (autoConvertedArea === null) return "";
    return autoConvertedArea.toFixed(6);
  }, [autoConvertedArea]);

  // Date range duration calculation helper
  const dateRangeDays = useMemo(() => {
    if (!startDate || !endDate) return null;
    const start = new Date(startDate);
    const end = new Date(endDate);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) return null;
    const diffTime = end.getTime() - start.getTime();
    const diffDays = Math.round(diffTime / (1000 * 60 * 60 * 24));
    return diffDays >= 0 ? diffDays : null;
  }, [startDate, endDate]);

  // Formula mapping and internal identifier conversion
  const formulaInternal = useMemo(() => {
    let expr = formulaReadable;
    for (const v of FORMULA_VARIABLES) {
      expr = expr.replaceAll(v.label, v.id);
    }
    expr = expr.replaceAll("×", "*").replaceAll("÷", "/");
    return expr;
  }, [formulaReadable]);

  // Local formula preview evaluation
  const { formulaPreviewAmount, formulaValidationError } = useMemo(() => {
    if (additionalAmountType !== "other") {
      return { formulaPreviewAmount: null, formulaValidationError: null };
    }
    const trimmed = formulaReadable.trim();
    if (!trimmed) {
      return { formulaPreviewAmount: null, formulaValidationError: "Formula expression is required." };
    }

    const appliedArea =
      useOfficialEquivalent && parseNumericInput(officialEquivalentArea) > 0
        ? parseNumericInput(officialEquivalentArea)
        : autoConvertedArea ?? areaNumber;

    const rateNum = parseNumericInput(marketRate);
    const estMarketValue = rateNum * (appliedArea ?? 0);
    const factorNum = parseNumericInput(multiplicationFactor) || 1;
    const estFactorValue = estMarketValue * factorNum;
    const assetsNum = parseNumericInput(treesAndStructures) || 0;
    const estBaseComp = estFactorValue + assetsNum;
    const solatiumNum = parseNumericInput(solatiumPercentage) || 0;
    const estSolatium = estBaseComp * (solatiumNum / 100);
    const estAfterSolatium = estBaseComp + estSolatium;

    let daysNum = 0;
    let monthsNum = 0;
    if (durationType === "days") {
      daysNum = parseNumericInput(durationValue) || 0;
      monthsNum = daysNum / 30;
    } else if (durationType === "months") {
      monthsNum = parseNumericInput(durationValue) || 0;
      daysNum = monthsNum * 30;
    } else if (durationType === "date_range" && dateRangeDays !== null) {
      daysNum = dateRangeDays;
      monthsNum = daysNum / 30;
    }

    const varValues: Record<string, number> = {
      MARKET_VALUE: estMarketValue,
      FACTOR_VALUE: estFactorValue,
      ASSET_VALUE: assetsNum,
      BASE_COMPENSATION: estBaseComp,
      SOLATIUM_AMOUNT: estSolatium,
      AFTER_SOLATIUM: estAfterSolatium,
      DAYS: daysNum,
      MONTHS: monthsNum,
      AREA: appliedArea,
      RATE: rateNum,
      FACTOR: factorNum,
      SOLATIUM_PERCENT: solatiumNum
    };

    let evalSource = formulaInternal;
    for (const [key, val] of Object.entries(varValues)) {
      const regex = new RegExp(`\\b${key}\\b`, "g");
      evalSource = evalSource.replace(regex, String(val));
    }

    try {
      const evaluated = evaluateExpression(evalSource);
      return { formulaPreviewAmount: evaluated, formulaValidationError: null };
    } catch (err: unknown) {
      return {
        formulaPreviewAmount: null,
        formulaValidationError: err instanceof Error ? err.message : "Invalid formula."
      };
    }
  }, [
    additionalAmountType,
    formulaReadable,
    formulaInternal,
    useOfficialEquivalent,
    officialEquivalentArea,
    autoConvertedArea,
    areaNumber,
    marketRate,
    multiplicationFactor,
    treesAndStructures,
    solatiumPercentage,
    durationType,
    durationValue,
    dateRangeDays
  ]);

  // Formula chip click
  const appendFormulaToken = (token: string) => {
    setFormulaReadable((prev) => {
      const trimmed = prev.trim();
      if (!trimmed) return token;
      if (FORMULA_OPERATORS.includes(token as any)) {
        if (token === "(" || token === ")") {
          return `${trimmed} ${token}`;
        }
        return `${trimmed} ${token} `;
      }
      return `${trimmed} ${token}`;
    });
  };

  const handleFormulaClear = () => {
    setFormulaReadable("");
  };

  const handleFormulaUndo = () => {
    setFormulaReadable((prev) => {
      const trimmed = prev.trim();
      if (!trimmed) return "";
      const parts = trimmed.split(/\s+/);
      parts.pop();
      return parts.join(" ");
    });
  };

  // Reset calculator
  const handleReset = () => {
    setLandArea("");
    setLandAreaUnit("bigha");
    setMarketRate("");
    setMarketRateUnit("acre");
    setUseOfficialEquivalent(false);
    setOfficialEquivalentArea("3.744");
    setMultiplicationFactor("2");
    setTreesAndStructures("0");
    setSolatiumPercentage("100");
    setAdditionalAmountType("interest");
    setAnnualRate("12");
    setDurationType("days");
    setDurationValue("30");
    setStartDate("");
    setEndDate("");
    setCalculatedOn("MarketValue");
    setFormulaReadable("Market Value × 12 ÷ 100 × Days ÷ 365");
    setFieldErrors({});
    setGeneralError("");
    setResult(null);
  };

  // Helper for field-specific error display
  const getFieldError = (fieldKey: string) => {
    return fieldErrors[fieldKey] || null;
  };

  // Perform calculation via POST /api/calculators/compensation/compute
  const handleCalculate = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();

    const errors: Record<string, string> = {};
    const parsedArea = parseNumericInput(landArea);
    if (!landArea || parsedArea <= 0) {
      errors["land.area"] = "Enter a valid land area greater than zero.";
      errors.landArea = "Enter a valid land area greater than zero.";
    }

    const parsedRate = parseNumericInput(marketRate);
    if (!marketRate || parsedRate <= 0) {
      errors["marketRate.amount"] = "Enter a valid market rate greater than zero.";
      errors.marketRate = "Enter a valid market rate greater than zero.";
    }

    const parsedFactor = parseNumericInput(multiplicationFactor);
    if (!multiplicationFactor || parsedFactor <= 0) {
      errors.multiplicationFactor = "Enter a valid multiplication factor.";
    }

    if (useOfficialEquivalent) {
      const parsedOfficial = parseNumericInput(officialEquivalentArea);
      if (parsedOfficial <= 0) {
        errors["land.equivalentAreaInRateUnit"] = "Enter a valid official equivalent area.";
      }
    }

    if (additionalAmountType === "interest") {
      const parsedAnnualRate = parseNumericInput(annualRate);
      if (parsedAnnualRate < 0) {
        errors.annualRate = "Annual interest rate cannot be negative.";
      }
      if (durationType === "date_range") {
        if (!startDate || !endDate) {
          errors.duration = "Select both start and end dates.";
        } else if (dateRangeDays === null) {
          errors.duration = "End date must be after or equal to start date.";
        }
      } else {
        const dur = parseNumericInput(durationValue);
        if (dur < 0) {
          errors.duration = "Duration cannot be negative.";
        }
      }
    } else {
      if (!formulaReadable.trim()) {
        errors["additionalAmount.formula"] = "Enter a valid expression for Other additional amount.";
      } else if (formulaValidationError) {
        errors["additionalAmount.formula"] = formulaValidationError;
      }
    }

    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      setGeneralError("Please resolve the errors highlighted below.");
      return;
    }

    setFieldErrors({});
    setGeneralError("");
    setLoading(true);

    const landPayload: {
      area: number;
      unit: string;
      equivalentAreaInRateUnit?: number;
    } = {
      area: parsedArea,
      unit: landAreaUnit
    };

    if (useOfficialEquivalent && parseNumericInput(officialEquivalentArea) > 0) {
      landPayload.equivalentAreaInRateUnit = parseNumericInput(officialEquivalentArea);
    }

    const payload = {
      land: landPayload,
      marketRate: {
        amount: parsedRate,
        perUnit: marketRateUnit
      },
      multiplicationFactor: parsedFactor,
      assets: {
        treesAndStructures: parseNumericInput(treesAndStructures) || 0
      },
      solatium: {
        percent: parseNumericInput(solatiumPercentage) || 0
      },
      additionalAmount: {
        type: additionalAmountType === "interest" ? "Interest" : "Other",
        annualRatePercent: additionalAmountType === "interest" ? parseNumericInput(annualRate) : null,
        duration: additionalAmountType === "interest" ? {
          mode: durationType === "days" ? "Days" : durationType === "months" ? "Months" : "DateRange",
          value: durationType === "date_range" ? null : parseNumericInput(durationValue),
          startDate: durationType === "date_range" ? startDate : null,
          endDate: durationType === "date_range" ? endDate : null
        } : null,
        basis: additionalAmountType === "interest" ? calculatedOn : "MarketValue",
        formula: additionalAmountType === "other" ? formulaInternal : null
      },
      conversionProfile: "lac-delhi-v1"
    };

    try {
      const response = await fetch("/api/calculators/compensation/compute", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        let errJson: any = null;
        try {
          errJson = await response.json();
        } catch {
          // not json
        }
        if (errJson?.errors && typeof errJson.errors === "object") {
          const flatErrors: Record<string, string> = {};
          for (const [k, v] of Object.entries(errJson.errors)) {
            flatErrors[k] = Array.isArray(v) ? v[0] : String(v);
          }
          setFieldErrors(flatErrors);
          setGeneralError(errJson.title || errJson.message || errJson.error || "Validation error from calculation server.");
        } else {
          setGeneralError(errJson?.message || errJson?.error || `Calculation request failed (Status ${response.status}).`);
        }
        return;
      }

      const data: CompensationResponse = await response.json();
      setResult(data);
    } catch (err: unknown) {
      setGeneralError(err instanceof Error ? err.message : "Unable to reach calculation service.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="comp-calculator-root">
      <div className="comp-grid-layout">
        {/* LEFT COLUMN: Input Flow */}
        <form className="comp-input-column" onSubmit={handleCalculate} noValidate>
          {generalError && (
            <div className="comp-error-banner" role="alert" aria-live="assertive">
              <span>{generalError}</span>
            </div>
          )}

          {/* STEP 1: LAND & RATE */}
          <section className="comp-card" aria-labelledby={`${formId}-step1-title`}>
            <header className="comp-card-header">
              <span className="comp-step-badge">1</span>
              <h3 id={`${formId}-step1-title`}>Land &amp; Rate</h3>
            </header>

            <div className="comp-form-row">
              <div className="comp-field-group">
                <label htmlFor={`${formId}-land-area`}>Land Area</label>
                <div className="comp-input-with-select">
                  <input
                    id={`${formId}-land-area`}
                    type="number"
                    min="0"
                    step="any"
                    value={landArea}
                    onChange={(e) => setLandArea(e.target.value)}
                    onWheel={(e) => e.currentTarget.blur()}
                    placeholder="e.g. 18"
                    aria-invalid={Boolean(getFieldError("land.area") || getFieldError("landArea"))}
                    aria-describedby={getFieldError("land.area") || getFieldError("landArea") ? `${formId}-land-area-err` : undefined}
                  />
                  <select
                    id={`${formId}-land-area-unit`}
                    aria-label="Land Area Unit"
                    value={landAreaUnit}
                    onChange={(e) => setLandAreaUnit(e.target.value)}
                  >
                    {Object.entries(AREA_UNITS).map(([id, u]) => (
                      <option key={id} value={id}>
                        {u.label}
                      </option>
                    ))}
                  </select>
                </div>
                {(getFieldError("land.area") || getFieldError("landArea")) && (
                  <span className="comp-field-error" id={`${formId}-land-area-err`} role="alert">
                    {getFieldError("land.area") || getFieldError("landArea")}
                  </span>
                )}
              </div>

              <div className="comp-field-group">
                <label htmlFor={`${formId}-market-rate`}>Market Rate</label>
                <div className="comp-input-with-select">
                  <div className="comp-currency-input-wrap">
                    <span className="comp-currency-prefix">₹</span>
                    <input
                      id={`${formId}-market-rate`}
                      type="text"
                      inputMode="decimal"
                      value={marketRate}
                      onChange={(e) => setMarketRate(e.target.value)}
                      placeholder="e.g. 53,00,000"
                      aria-invalid={Boolean(getFieldError("marketRate.amount") || getFieldError("marketRate"))}
                      aria-describedby={getFieldError("marketRate.amount") || getFieldError("marketRate") ? `${formId}-market-rate-err` : undefined}
                    />
                  </div>
                  <select
                    id={`${formId}-market-rate-unit`}
                    aria-label="Market Rate Unit"
                    value={marketRateUnit}
                    onChange={(e) => setMarketRateUnit(e.target.value)}
                  >
                    {Object.entries(AREA_UNITS).map(([id, u]) => (
                      <option key={id} value={id}>
                        Per {u.label}
                      </option>
                    ))}
                  </select>
                </div>
                {(getFieldError("marketRate.amount") || getFieldError("marketRate")) && (
                  <span className="comp-field-error" id={`${formId}-market-rate-err`} role="alert">
                    {getFieldError("marketRate.amount") || getFieldError("marketRate")}
                  </span>
                )}
              </div>
            </div>

            {/* Area Conversion & Official Equivalent Option */}
            {isDifferentUnit && areaNumber > 0 && (
              <div className="comp-conversion-block" aria-live="polite">
                <div className="comp-auto-conversion-row">
                  <span className="comp-helper-icon">⇄</span>
                  <span>
                    Auto conversion: <strong>{autoConvertedStr} {AREA_UNITS[marketRateUnit]?.label}</strong>
                  </span>
                </div>

                <div className="comp-official-equivalent-toggle">
                  <label className="comp-checkbox-label">
                    <input
                      type="checkbox"
                      checked={useOfficialEquivalent}
                      onChange={(e) => setUseOfficialEquivalent(e.target.checked)}
                    />
                    <span>Use official/document equivalent area</span>
                  </label>
                </div>

                {useOfficialEquivalent && (
                  <div className="comp-official-equivalent-field">
                    <div className="comp-field-group">
                      <label htmlFor={`${formId}-official-area`}>
                        Official equivalent area ({AREA_UNITS[marketRateUnit]?.label})
                      </label>
                      <div className="comp-input-with-unit">
                        <input
                          id={`${formId}-official-area`}
                          type="number"
                          step="any"
                          min="0"
                          value={officialEquivalentArea}
                          onChange={(e) => setOfficialEquivalentArea(e.target.value)}
                          onWheel={(e) => e.currentTarget.blur()}
                          placeholder="3.744"
                          aria-invalid={Boolean(getFieldError("land.equivalentAreaInRateUnit"))}
                        />
                        <span className="comp-unit-addon">{AREA_UNITS[marketRateUnit]?.label}</span>
                      </div>
                      <p className="comp-official-help">
                        Use this only when the Award/official document specifies a different equivalent area.
                      </p>
                      {getFieldError("land.equivalentAreaInRateUnit") && (
                        <span className="comp-field-error" role="alert">
                          {getFieldError("land.equivalentAreaInRateUnit")}
                        </span>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}
          </section>

          {/* STEP 2: ADJUSTMENTS */}
          <section className="comp-card" aria-labelledby={`${formId}-step2-title`}>
            <header className="comp-card-header">
              <span className="comp-step-badge">2</span>
              <h3 id={`${formId}-step2-title`}>Adjustments</h3>
            </header>

            <div className="comp-form-row">
              <div className="comp-field-group">
                <label htmlFor={`${formId}-factor`}>Multiplication Factor</label>
                <input
                  id={`${formId}-factor`}
                  type="number"
                  min="0.1"
                  step="0.1"
                  value={multiplicationFactor}
                  onChange={(e) => setMultiplicationFactor(e.target.value)}
                  onWheel={(e) => e.currentTarget.blur()}
                  placeholder="e.g. 2"
                  aria-invalid={Boolean(getFieldError("multiplicationFactor"))}
                  aria-describedby={getFieldError("multiplicationFactor") ? `${formId}-factor-err` : undefined}
                />
                {getFieldError("multiplicationFactor") && (
                  <span className="comp-field-error" id={`${formId}-factor-err`} role="alert">
                    {getFieldError("multiplicationFactor")}
                  </span>
                )}
              </div>

              <div className="comp-field-group">
                <div className="comp-label-with-action">
                  <label htmlFor={`${formId}-trees-structures`}>Trees &amp; Structures</label>
                  <button
                    type="button"
                    className="comp-btn-nil"
                    onClick={() => setTreesAndStructures("0")}
                    title="Set trees and structures to ₹0"
                  >
                    Nil / ₹0
                  </button>
                </div>
                <div className="comp-currency-input-wrap">
                  <span className="comp-currency-prefix">₹</span>
                  <input
                    id={`${formId}-trees-structures`}
                    type="text"
                    inputMode="decimal"
                    value={treesAndStructures}
                    onChange={(e) => setTreesAndStructures(e.target.value)}
                    placeholder="0"
                  />
                </div>
              </div>
            </div>
          </section>

          {/* STEP 3: SOLATIUM */}
          <section className="comp-card" aria-labelledby={`${formId}-step3-title`}>
            <header className="comp-card-header">
              <span className="comp-step-badge">3</span>
              <h3 id={`${formId}-step3-title`}>Solatium</h3>
            </header>

            <div className="comp-field-group comp-field-compact">
              <label htmlFor={`${formId}-solatium`}>Solatium Rate (%)</label>
              <div className="comp-percent-input-wrap">
                <input
                  id={`${formId}-solatium`}
                  type="number"
                  min="0"
                  step="any"
                  value={solatiumPercentage}
                  onChange={(e) => setSolatiumPercentage(e.target.value)}
                  onWheel={(e) => e.currentTarget.blur()}
                  placeholder="100"
                />
                <span className="comp-percent-suffix">%</span>
              </div>
            </div>
          </section>

          {/* STEP 4: ADDITIONAL AMOUNT */}
          <section className="comp-card" aria-labelledby={`${formId}-step4-title`}>
            <header className="comp-card-header">
              <span className="comp-step-badge">4</span>
              <h3 id={`${formId}-step4-title`}>Additional Amount</h3>
            </header>

            <div className="comp-field-group comp-field-compact">
              <label htmlFor={`${formId}-add-type`}>Type</label>
              <select
                id={`${formId}-add-type`}
                value={additionalAmountType}
                onChange={(e) => setAdditionalAmountType(e.target.value as "interest" | "other")}
              >
                <option value="interest">Interest</option>
                <option value="other">Other</option>
              </select>
            </div>

            {additionalAmountType === "interest" ? (
              <div className="comp-interest-subfields">
                <div className="comp-form-row">
                  <div className="comp-field-group">
                    <label htmlFor={`${formId}-annual-rate`}>Annual Rate (%)</label>
                    <div className="comp-percent-input-wrap">
                      <input
                        id={`${formId}-annual-rate`}
                        type="number"
                        min="0"
                        step="any"
                        value={annualRate}
                        onChange={(e) => setAnnualRate(e.target.value)}
                        onWheel={(e) => e.currentTarget.blur()}
                        placeholder="12"
                      />
                      <span className="comp-percent-suffix">%</span>
                    </div>
                  </div>

                  <div className="comp-field-group">
                    <label htmlFor={`${formId}-duration-type`}>Duration</label>
                    <div className="comp-duration-inputs">
                      {durationType === "date_range" ? (
                        <div className="comp-date-range-wrap">
                          <input
                            type="date"
                            aria-label="Start Date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                          />
                          <span className="comp-date-to">to</span>
                          <input
                            type="date"
                            aria-label="End Date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                          />
                        </div>
                      ) : (
                        <input
                          id={`${formId}-duration-val`}
                          type="number"
                          min="0"
                          step="1"
                          value={durationValue}
                          onChange={(e) => setDurationValue(e.target.value)}
                          onWheel={(e) => e.currentTarget.blur()}
                          placeholder="30"
                          aria-label={`Duration in ${durationType}`}
                        />
                      )}

                      <select
                        id={`${formId}-duration-type`}
                        aria-label="Duration Unit"
                        value={durationType}
                        onChange={(e) => setDurationType(e.target.value as typeof durationType)}
                      >
                        <option value="days">Days</option>
                        <option value="months">Months</option>
                        <option value="date_range">Date range</option>
                      </select>
                    </div>

                    {durationType === "date_range" && dateRangeDays !== null && (
                      <span className="comp-subtle-hint">Duration: {dateRangeDays} days</span>
                    )}
                  </div>
                </div>

                <div className="comp-field-group comp-field-secondary">
                  <label htmlFor={`${formId}-calc-on`}>Calculated On</label>
                  <select
                    id={`${formId}-calc-on`}
                    value={calculatedOn}
                    onChange={(e) => setCalculatedOn(e.target.value as typeof calculatedOn)}
                  >
                    <option value="MarketValue">Market Value</option>
                    <option value="FactorAdjustedValue">Factor Value</option>
                    <option value="BaseCompensation">Base Compensation</option>
                    <option value="AmountAfterSolatium">Amount After Solatium</option>
                  </select>
                </div>
              </div>
            ) : (
              /* GUIDED FORMULA BUILDER */
              <div className="comp-formula-builder">
                <label htmlFor={`${formId}-formula-expr`}>Formula Expression</label>
                <p className="comp-formula-help">
                  Select value chips and operators, or enter numerical constants.
                </p>

                {/* Value Chips */}
                <div className="comp-formula-chips" aria-label="Available values">
                  {FORMULA_VARIABLES.map((v) => (
                    <button
                      key={v.id}
                      type="button"
                      className="comp-chip-btn"
                      onClick={() => appendFormulaToken(v.label)}
                    >
                      {v.label}
                    </button>
                  ))}
                </div>

                {/* Operator Bar */}
                <div className="comp-formula-operators" aria-label="Operators">
                  {FORMULA_OPERATORS.map((op) => (
                    <button
                      key={op}
                      type="button"
                      className="comp-op-btn"
                      onClick={() => appendFormulaToken(op)}
                    >
                      {op}
                    </button>
                  ))}
                  <button
                    type="button"
                    className="comp-formula-action-btn"
                    onClick={handleFormulaUndo}
                    title="Undo last token"
                  >
                    Undo
                  </button>
                  <button
                    type="button"
                    className="comp-formula-action-btn"
                    onClick={handleFormulaClear}
                    title="Clear formula"
                  >
                    Clear
                  </button>
                </div>

                {/* Expression Input Area */}
                <input
                  id={`${formId}-formula-expr`}
                  className="comp-formula-input"
                  value={formulaReadable}
                  onChange={(e) => setFormulaReadable(e.target.value)}
                  placeholder="e.g. Market Value × 12 ÷ 100 × Days ÷ 365"
                  aria-label="Guided formula expression"
                  aria-invalid={Boolean(getFieldError("additionalAmount.formula") || formulaValidationError)}
                />

                {/* Formula Validation & Preview */}
                <div className="comp-formula-footer">
                  {formulaValidationError ? (
                    <span className="comp-formula-msg comp-formula-err">{formulaValidationError}</span>
                  ) : (
                    <span className="comp-formula-msg comp-formula-ok">Valid formula expression</span>
                  )}

                  <div className="comp-formula-preview">
                    <span>Preview:</span>
                    <strong>{formatInr(formulaPreviewAmount)}</strong>
                  </div>
                </div>
              </div>
            )}
          </section>

          {/* ACTIONS: Calculate & Reset */}
          <div className="comp-actions-bar">
            <button
              type="submit"
              className="comp-btn-primary"
              disabled={loading}
            >
              {loading ? "Calculating…" : "Calculate Compensation"}
            </button>
            <button
              type="button"
              className="comp-btn-secondary"
              onClick={handleReset}
              disabled={loading}
            >
              Reset
            </button>
          </div>
        </form>

        {/* RIGHT COLUMN: Result Panel */}
        <aside className="comp-result-column" aria-label="Calculated Compensation Summary">
          {!result ? (
            <div className="comp-empty-panel">
              <div className="comp-empty-icon" aria-hidden="true">
                ₹
              </div>
              <h4>Compensation Summary</h4>
              <p>Enter land area and rate to see the compensation breakdown.</p>
            </div>
          ) : (
            <div className="comp-active-panel">
              {/* TOP: Final Compensation */}
              <div className="comp-final-card">
                <span className="comp-final-tag">FINAL COMPENSATION</span>
                <strong className="comp-final-amount">
                  {formatInr(result.finalCompensation)}
                </strong>
                <p className="comp-final-words">
                  {result.finalAmountInWords || numberToIndianWords(result.finalCompensation)}
                </p>
              </div>

              {/* CALCULATION BREAKDOWN */}
              <div className="comp-breakdown-card">
                <h4 className="comp-breakdown-heading">Calculation Breakdown</h4>
                <div className="comp-breakdown-list">
                  {/* Distinct Area Section */}
                  {result.area && (
                    <div className="comp-breakdown-area-summary">
                      <div className="comp-breakdown-row">
                        <div className="comp-row-label">
                          <span>Entered land:</span>
                        </div>
                        <strong className="comp-row-val">
                          {result.area.enteredArea} {AREA_UNITS[result.area.unit as keyof typeof AREA_UNITS]?.label || result.area.unit}
                        </strong>
                      </div>

                      {result.area.unit !== result.area.rateUnit && (
                        <>
                          <div className="comp-breakdown-row">
                            <div className="comp-row-label">
                              <span>Automatic conversion:</span>
                            </div>
                            <strong className="comp-row-val">
                              {Number(result.area.profileConvertedArea).toFixed(6)} {AREA_UNITS[result.area.rateUnit as keyof typeof AREA_UNITS]?.label || result.area.rateUnit}
                            </strong>
                          </div>

                          <div className="comp-breakdown-row comp-row-applied-area">
                            <div className="comp-row-label">
                              <span>Applied for calculation:</span>
                            </div>
                            <strong className="comp-row-val">
                              {result.area.appliedArea} {AREA_UNITS[result.area.rateUnit as keyof typeof AREA_UNITS]?.label || result.area.rateUnit}{" "}
                              <small>
                                {result.area.usesExplicitEquivalentArea
                                  ? "(Official equivalent entered)"
                                  : "(Automatic conversion applied)"}
                              </small>
                            </strong>
                          </div>
                        </>
                      )}
                    </div>
                  )}

                  {/* Financial Stages */}
                  {result.marketValue !== undefined && (
                    <div className="comp-breakdown-row">
                      <div className="comp-row-label">
                        <span>Market Value</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.marketValue)}</strong>
                    </div>
                  )}

                  {result.factorAdjustedValue !== undefined && (
                    <div className="comp-breakdown-row">
                      <div className="comp-row-label">
                        <span>× Factor {result.multiplicationFactor ?? 2}</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.factorAdjustedValue)}</strong>
                    </div>
                  )}

                  {result.treesAndStructures !== undefined && (
                    <div className="comp-breakdown-row">
                      <div className="comp-row-label">
                        <span>+ Trees &amp; Structures</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.treesAndStructures)}</strong>
                    </div>
                  )}

                  {result.baseCompensation !== undefined && (
                    <div className="comp-breakdown-row comp-row-subtotal">
                      <div className="comp-row-label">
                        <span>Base Compensation</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.baseCompensation)}</strong>
                    </div>
                  )}

                  {result.solatiumAmount !== undefined && (
                    <div className="comp-breakdown-row">
                      <div className="comp-row-label">
                        <span>+ Solatium {result.solatiumPercent ?? 100}%</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.solatiumAmount)}</strong>
                    </div>
                  )}

                  {result.amountAfterSolatium !== undefined && (
                    <div className="comp-breakdown-row comp-row-subtotal">
                      <div className="comp-row-label">
                        <span>Amount after Solatium</span>
                      </div>
                      <strong className="comp-row-amount">{formatInr(result.amountAfterSolatium)}</strong>
                    </div>
                  )}

                  {result.additionalAmount !== undefined && (
                    <div className="comp-breakdown-row">
                      <div className="comp-row-label">
                        <span>+ Additional Amount</span>
                        {result.additionalAmount.duration?.value && (
                          <small>
                            {result.additionalAmount.annualRatePercent}% for {result.additionalAmount.duration.value} {result.additionalAmount.duration.mode?.toLowerCase()} on {result.additionalAmount.basis}
                          </small>
                        )}
                      </div>
                      <strong className="comp-row-amount">
                        {formatInr(result.additionalAmount.amount)}
                      </strong>
                    </div>
                  )}

                  <div className="comp-breakdown-row comp-row-final">
                    <div className="comp-row-label">
                      <span>FINAL</span>
                    </div>
                    <strong className="comp-row-amount">{formatInr(result.finalCompensation)}</strong>
                  </div>
                </div>

                <p className="comp-neutral-note">Calculated from the values entered above.</p>
              </div>
            </div>
          )}
        </aside>
      </div>
    </div>
  );
}
