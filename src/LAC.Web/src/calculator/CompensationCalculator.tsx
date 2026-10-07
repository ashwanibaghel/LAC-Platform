import React, { useId, useMemo, useState } from "react";
import { AREA_UNITS, areaToSqm, sqmToArea } from "./landConversions";
import { formatInr, parseNumericInput } from "./compensationFormatters";
import {
  type MoneyValue,
  type AreaCalculation,
  type DurationCalculation,
  type AdditionalAmountCalculation,
  type CalculationStep,
  type CompensationResponse,
  type DurationPayload,
  type AdditionalAmountPayload,
  type CompensationRequest,
  type CompensationFormState,
  INITIAL_COMPENSATION_FORM_STATE,
  BASE_FORMULA_VARIABLES,
  ALL_RECOGNIZED_VARIABLES,
  FORMULA_OPERATORS,
  getAvailableFormulaVariables,
  convertFormulaReadableToInternal,
  validateFormulaSyntax,
  buildCompensationRequest
} from "./compensationContracts";

export {
  type MoneyValue,
  type AreaCalculation,
  type DurationCalculation,
  type AdditionalAmountCalculation,
  type CalculationStep,
  type CompensationResponse,
  type DurationPayload,
  type AdditionalAmountPayload,
  type CompensationRequest,
  type CompensationFormState,
  INITIAL_COMPENSATION_FORM_STATE,
  BASE_FORMULA_VARIABLES,
  ALL_RECOGNIZED_VARIABLES,
  FORMULA_OPERATORS,
  getAvailableFormulaVariables,
  convertFormulaReadableToInternal,
  validateFormulaSyntax,
  buildCompensationRequest
};

export function CompensationCalculator() {
  const formId = useId();

  // Step 1: Land & Rate
  const [landArea, setLandArea] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.landArea);
  const [landAreaUnit, setLandAreaUnit] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.landAreaUnit);
  const [marketRate, setMarketRate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.marketRate);
  const [marketRateUnit, setMarketRateUnit] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.marketRateUnit);
  const [useOfficialEquivalent, setUseOfficialEquivalent] = useState<boolean>(INITIAL_COMPENSATION_FORM_STATE.useOfficialEquivalent);
  const [officialEquivalentArea, setOfficialEquivalentArea] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.officialEquivalentArea);

  // Step 2: Adjustments
  const [multiplicationFactor, setMultiplicationFactor] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.multiplicationFactor);
  const [treesAndStructures, setTreesAndStructures] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.treesAndStructures);

  // Step 3: Solatium
  const [solatiumPercentage, setSolatiumPercentage] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.solatiumPercentage);

  // Step 4: Additional Amount
  const [additionalAmountType, setAdditionalAmountType] = useState<"interest" | "other">(INITIAL_COMPENSATION_FORM_STATE.additionalAmountType);
  const [annualRate, setAnnualRate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.annualRate);
  const [durationType, setDurationType] = useState<"days" | "months" | "date_range">(INITIAL_COMPENSATION_FORM_STATE.durationType);
  const [durationValue, setDurationValue] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.durationValue);
  const [startDate, setStartDate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.startDate);
  const [endDate, setEndDate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.endDate);
  const [calculatedOn, setCalculatedOn] = useState<"MarketValue" | "FactorAdjustedValue" | "BaseCompensation" | "AmountAfterSolatium">(INITIAL_COMPENSATION_FORM_STATE.calculatedOn);

  // Formula Mode (Other)
  const [formulaReadable, setFormulaReadable] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.formulaReadable);
  const [otherDurationMode, setOtherDurationMode] = useState<"None" | "Days" | "Months" | "DateRange">(INITIAL_COMPENSATION_FORM_STATE.otherDurationMode);
  const [otherDurationValue, setOtherDurationValue] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.otherDurationValue);
  const [otherStartDate, setOtherStartDate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.otherStartDate);
  const [otherEndDate, setOtherEndDate] = useState<string>(INITIAL_COMPENSATION_FORM_STATE.otherEndDate);

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

  // Date range duration calculation helper for Interest display hint
  const dateRangeDays = useMemo(() => {
    if (!startDate || !endDate) return null;
    const start = new Date(startDate);
    const end = new Date(endDate);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) return null;
    const diffTime = end.getTime() - start.getTime();
    const diffDays = Math.round(diffTime / (1000 * 60 * 60 * 24));
    return diffDays >= 0 ? diffDays : null;
  }, [startDate, endDate]);

  // Formula syntax validation
  const formulaValidationError = useMemo(() => {
    if (additionalAmountType !== "other") return null;
    if (!formulaReadable.trim()) return null;
    return validateFormulaSyntax(formulaReadable, otherDurationMode);
  }, [additionalAmountType, formulaReadable, otherDurationMode]);

  // Formula available variable chips based on selected duration mode
  const availableChips = useMemo(() => {
    return getAvailableFormulaVariables(otherDurationMode);
  }, [otherDurationMode]);

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

  const handleReset = () => {
    setLandArea(INITIAL_COMPENSATION_FORM_STATE.landArea);
    setLandAreaUnit(INITIAL_COMPENSATION_FORM_STATE.landAreaUnit);
    setMarketRate(INITIAL_COMPENSATION_FORM_STATE.marketRate);
    setMarketRateUnit(INITIAL_COMPENSATION_FORM_STATE.marketRateUnit);
    setUseOfficialEquivalent(INITIAL_COMPENSATION_FORM_STATE.useOfficialEquivalent);
    setOfficialEquivalentArea(INITIAL_COMPENSATION_FORM_STATE.officialEquivalentArea);
    setMultiplicationFactor(INITIAL_COMPENSATION_FORM_STATE.multiplicationFactor);
    setTreesAndStructures(INITIAL_COMPENSATION_FORM_STATE.treesAndStructures);
    setSolatiumPercentage(INITIAL_COMPENSATION_FORM_STATE.solatiumPercentage);
    setAdditionalAmountType(INITIAL_COMPENSATION_FORM_STATE.additionalAmountType);
    setAnnualRate(INITIAL_COMPENSATION_FORM_STATE.annualRate);
    setDurationType(INITIAL_COMPENSATION_FORM_STATE.durationType);
    setDurationValue(INITIAL_COMPENSATION_FORM_STATE.durationValue);
    setStartDate(INITIAL_COMPENSATION_FORM_STATE.startDate);
    setEndDate(INITIAL_COMPENSATION_FORM_STATE.endDate);
    setCalculatedOn(INITIAL_COMPENSATION_FORM_STATE.calculatedOn);
    setFormulaReadable(INITIAL_COMPENSATION_FORM_STATE.formulaReadable);
    setOtherDurationMode(INITIAL_COMPENSATION_FORM_STATE.otherDurationMode);
    setOtherDurationValue(INITIAL_COMPENSATION_FORM_STATE.otherDurationValue);
    setOtherStartDate(INITIAL_COMPENSATION_FORM_STATE.otherStartDate);
    setOtherEndDate(INITIAL_COMPENSATION_FORM_STATE.otherEndDate);
    setFieldErrors({});
    setGeneralError("");
    setResult(null);
  };

  const getFieldError = (fieldKey: string) => {
    return fieldErrors[fieldKey] || null;
  };

  const handleCalculate = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();

    const formState: CompensationFormState = {
      landArea,
      landAreaUnit,
      marketRate,
      marketRateUnit,
      useOfficialEquivalent,
      officialEquivalentArea,
      multiplicationFactor,
      treesAndStructures,
      solatiumPercentage,
      additionalAmountType,
      annualRate,
      durationType,
      durationValue,
      startDate,
      endDate,
      calculatedOn,
      formulaReadable,
      otherDurationMode,
      otherDurationValue,
      otherStartDate,
      otherEndDate
    };

    const validation = buildCompensationRequest(formState);
    if (!validation.valid || !validation.payload) {
      setFieldErrors(validation.errors);
      setGeneralError("Please resolve the errors highlighted below.");
      return;
    }

    setFieldErrors({});
    setGeneralError("");
    setLoading(true);

    try {
      const response = await fetch("/api/calculators/compensation/compute", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(validation.payload)
      });

      if (!response.ok) {
        if (response.status === 401) {
          setGeneralError("Your session has expired or authentication is required. Please sign in.");
          return;
        }
        if (response.status === 413) {
          setGeneralError("The calculation request or formula exceeds the allowable size limit.");
          return;
        }
        if (response.status === 415) {
          setGeneralError("Invalid calculation request format. Content-Type must be application/json.");
          return;
        }

        let errJson: any = null;
        try {
          errJson = await response.json();
        } catch {
          // not JSON
        }

        if (errJson?.errors && typeof errJson.errors === "object") {
          const flatErrors: Record<string, string> = {};
          for (const [k, v] of Object.entries(errJson.errors)) {
            const msg = Array.isArray(v) ? v[0] : String(v);
            flatErrors[k] = msg;
            if (k.startsWith("land.area")) flatErrors.landArea = msg;
            if (k.startsWith("land.equivalentAreaInRateUnit")) flatErrors.officialEquivalentArea = msg;
            if (k.startsWith("marketRate.amount")) flatErrors.marketRate = msg;
            if (k.startsWith("multiplicationFactor")) flatErrors.multiplicationFactor = msg;
            if (k.startsWith("solatium.percent")) flatErrors.solatiumPercentage = msg;
            if (k.startsWith("additionalAmount.annualRatePercent")) flatErrors.annualRate = msg;
            if (k.startsWith("additionalAmount.duration")) flatErrors.duration = msg;
            if (k.startsWith("additionalAmount.formula")) flatErrors["additionalAmount.formula"] = msg;
          }
          setFieldErrors(flatErrors);
          setGeneralError(errJson.title || errJson.message || "Please resolve the errors highlighted below.");
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
                          placeholder="e.g. 3.744"
                          aria-invalid={Boolean(getFieldError("land.equivalentAreaInRateUnit") || getFieldError("officialEquivalentArea"))}
                        />
                        <span className="comp-unit-addon">{AREA_UNITS[marketRateUnit]?.label}</span>
                      </div>
                      <p className="comp-official-help">
                        Use this only when the Award/official document specifies a different equivalent area.
                      </p>
                      {(getFieldError("land.equivalentAreaInRateUnit") || getFieldError("officialEquivalentArea")) && (
                        <span className="comp-field-error" role="alert">
                          {getFieldError("land.equivalentAreaInRateUnit") || getFieldError("officialEquivalentArea")}
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
                  step="any"
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
                  placeholder="e.g. 100"
                  aria-invalid={Boolean(getFieldError("solatium.percent") || getFieldError("solatiumPercentage"))}
                  aria-describedby={getFieldError("solatium.percent") || getFieldError("solatiumPercentage") ? `${formId}-solatium-err` : undefined}
                />
                <span className="comp-percent-suffix">%</span>
              </div>
              {(getFieldError("solatium.percent") || getFieldError("solatiumPercentage")) && (
                <span className="comp-field-error" id={`${formId}-solatium-err`} role="alert">
                  {getFieldError("solatium.percent") || getFieldError("solatiumPercentage")}
                </span>
              )}
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
                        placeholder="e.g. 12"
                        aria-invalid={Boolean(getFieldError("additionalAmount.annualRatePercent") || getFieldError("annualRate"))}
                        aria-describedby={getFieldError("additionalAmount.annualRatePercent") || getFieldError("annualRate") ? `${formId}-annual-rate-err` : undefined}
                      />
                      <span className="comp-percent-suffix">%</span>
                    </div>
                    {(getFieldError("additionalAmount.annualRatePercent") || getFieldError("annualRate")) && (
                      <span className="comp-field-error" id={`${formId}-annual-rate-err`} role="alert">
                        {getFieldError("additionalAmount.annualRatePercent") || getFieldError("annualRate")}
                      </span>
                    )}
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
                          placeholder="e.g. 30"
                          aria-label={`Duration in ${durationType}`}
                          aria-invalid={Boolean(getFieldError("additionalAmount.duration.value") || getFieldError("duration"))}
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

                    {(getFieldError("additionalAmount.duration.value") ||
                      getFieldError("additionalAmount.duration") ||
                      getFieldError("duration")) && (
                      <span className="comp-field-error" role="alert">
                        {getFieldError("additionalAmount.duration.value") ||
                          getFieldError("additionalAmount.duration") ||
                          getFieldError("duration")}
                      </span>
                    )}

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
                {/* Optional Formula Duration Selector */}
                <div className="comp-formula-duration-row">
                  <label htmlFor={`${formId}-other-dur-mode`}>Formula Duration (Optional)</label>
                  <div className="comp-formula-duration-controls">
                    <select
                      id={`${formId}-other-dur-mode`}
                      value={otherDurationMode}
                      onChange={(e) => setOtherDurationMode(e.target.value as typeof otherDurationMode)}
                    >
                      <option value="None">None</option>
                      <option value="Days">Days</option>
                      <option value="Months">Months</option>
                      <option value="DateRange">Date Range</option>
                    </select>

                    {otherDurationMode === "Days" && (
                      <div className="comp-inline-duration-wrap">
                        <input
                          id={`${formId}-other-dur-days`}
                          type="number"
                          min="0"
                          step="any"
                          value={otherDurationValue}
                          onChange={(e) => setOtherDurationValue(e.target.value)}
                          onWheel={(e) => e.currentTarget.blur()}
                          placeholder="e.g. 30"
                          aria-label="Duration in Days"
                        />
                        <span className="comp-subtle-hint">Days</span>
                      </div>
                    )}

                    {otherDurationMode === "Months" && (
                      <div className="comp-inline-duration-wrap">
                        <input
                          id={`${formId}-other-dur-months`}
                          type="number"
                          min="0"
                          step="any"
                          value={otherDurationValue}
                          onChange={(e) => setOtherDurationValue(e.target.value)}
                          onWheel={(e) => e.currentTarget.blur()}
                          placeholder="e.g. 6"
                          aria-label="Duration in Months"
                        />
                        <span className="comp-subtle-hint">Months</span>
                      </div>
                    )}

                    {otherDurationMode === "DateRange" && (
                      <div className="comp-date-range-wrap">
                        <input
                          type="date"
                          aria-label="Start Date"
                          value={otherStartDate}
                          onChange={(e) => setOtherStartDate(e.target.value)}
                        />
                        <span className="comp-date-to">to</span>
                        <input
                          type="date"
                          aria-label="End Date"
                          value={otherEndDate}
                          onChange={(e) => setOtherEndDate(e.target.value)}
                        />
                      </div>
                    )}
                  </div>
                  {(getFieldError("additionalAmount.duration.value") ||
                    getFieldError("additionalAmount.duration") ||
                    getFieldError("otherDuration")) && (
                    <span className="comp-field-error" role="alert">
                      {getFieldError("additionalAmount.duration.value") ||
                        getFieldError("additionalAmount.duration") ||
                        getFieldError("otherDuration")}
                    </span>
                  )}
                </div>

                <label htmlFor={`${formId}-formula-expr`}>Formula Expression</label>
                <p className="comp-formula-help">
                  Select value chips and operators, or enter numerical constants.
                </p>

                {/* Available Value Chips */}
                <div className="comp-formula-chips" aria-label="Available values">
                  {availableChips.map((v) => (
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
                  placeholder="e.g. MARKET_VALUE * 12 / 100 * (DAYS / 365)"
                  aria-label="Guided formula expression"
                  aria-invalid={Boolean(getFieldError("additionalAmount.formula") || formulaValidationError)}
                />

                {/* Formula Validation & Preview Footer */}
                <div className="comp-formula-footer">
                  {formulaValidationError || getFieldError("additionalAmount.formula") ? (
                    <span className="comp-formula-msg comp-formula-err">
                      {formulaValidationError || getFieldError("additionalAmount.formula")}
                    </span>
                  ) : (
                    <span className="comp-formula-msg comp-formula-ok">
                      {formulaReadable.trim() ? "Valid formula syntax" : "Enter formula or select chips"}
                    </span>
                  )}

                  <div className="comp-formula-preview">
                    <span>Additional Amount:</span>
                    {result?.additionalAmount?.amount ? (
                      <strong>{formatInr(result.additionalAmount.amount)}</strong>
                    ) : (
                      <span className="comp-formula-server-hint">Amount will be calculated by the server.</span>
                    )}
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
                  {result.finalAmountInWords}
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
                        <span>× Factor {result.multiplicationFactor}</span>
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
                        <span>+ Solatium {result.solatiumPercent}%</span>
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
                            {result.additionalAmount.annualRatePercent
                              ? `${result.additionalAmount.annualRatePercent}% for ${result.additionalAmount.duration.value} ${result.additionalAmount.duration.mode?.toLowerCase()}`
                              : `${result.additionalAmount.duration.value} ${result.additionalAmount.duration.mode?.toLowerCase()}`}{" "}
                            {result.additionalAmount.basis ? `on ${result.additionalAmount.basis}` : ""}
                          </small>
                        )}
                        {result.additionalAmount.type === "Other" && (
                          <small title={result.additionalAmount.normalizedFormula}>
                            Formula: {result.additionalAmount.normalizedFormula}
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
