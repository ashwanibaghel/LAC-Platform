import { parseNumericInput } from "./compensationFormatters.ts";

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
  value: string;
  fraction: string;
  convention: string;
}

export interface AdditionalAmountCalculation {
  type: string;
  basis: string | null;
  annualRatePercent: string | null;
  duration: DurationCalculation | null;
  normalizedFormula: string;
  substitutedFormula: string;
  variables: Record<string, string>;
  amount: MoneyValue;
}

export interface CalculationStep {
  name: string;
  formula: string;
  substitutedFormula: string;
  result: string;
}

export interface CompensationResponse {
  currency: string;
  area: AreaCalculation;
  rate: string;
  multiplicationFactor: string;
  solatiumPercent: string;
  marketValue: MoneyValue;
  factorAdjustedValue: MoneyValue;
  treesAndStructures: MoneyValue;
  baseCompensation: MoneyValue;
  solatiumAmount: MoneyValue;
  amountAfterSolatium: MoneyValue;
  additionalAmount: AdditionalAmountCalculation;
  finalCompensation: MoneyValue;
  finalAmountInWords: string;
  roundingPolicy: string;
  trace: CalculationStep[];
}

export interface DurationPayload {
  mode: "Days" | "Months" | "DateRange";
  value?: number | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface AdditionalAmountPayload {
  type: "Interest" | "Other";
  annualRatePercent?: number | null;
  duration?: DurationPayload | null;
  basis: "MarketValue" | "FactorAdjustedValue" | "BaseCompensation" | "AmountAfterSolatium";
  formula?: string | null;
}

export interface CompensationRequest {
  conversionProfile: string;
  land: {
    area: number;
    unit: string;
    equivalentAreaInRateUnit?: number;
  };
  marketRate: {
    amount: number;
    perUnit: string;
  };
  multiplicationFactor: number;
  assets: {
    treesAndStructures: number;
  };
  solatium: {
    percent: number;
  };
  additionalAmount: AdditionalAmountPayload;
}

export interface CompensationFormState {
  landArea: string;
  landAreaUnit: string;
  marketRate: string;
  marketRateUnit: string;
  useOfficialEquivalent: boolean;
  officialEquivalentArea: string;
  multiplicationFactor: string;
  treesAndStructures: string;
  solatiumPercentage: string;
  additionalAmountType: "interest" | "other";
  annualRate: string;
  durationType: "days" | "months" | "date_range";
  durationValue: string;
  startDate: string;
  endDate: string;
  calculatedOn: "MarketValue" | "FactorAdjustedValue" | "BaseCompensation" | "AmountAfterSolatium";
  formulaReadable: string;
  otherDurationMode: "None" | "Days" | "Months" | "DateRange";
  otherDurationValue: string;
  otherStartDate: string;
  otherEndDate: string;
}

export const INITIAL_COMPENSATION_FORM_STATE: CompensationFormState = {
  landArea: "",
  landAreaUnit: "bigha",
  marketRate: "",
  marketRateUnit: "acre",
  useOfficialEquivalent: false,
  officialEquivalentArea: "",
  multiplicationFactor: "",
  treesAndStructures: "0",
  solatiumPercentage: "",
  additionalAmountType: "interest",
  annualRate: "",
  durationType: "days",
  durationValue: "",
  startDate: "",
  endDate: "",
  calculatedOn: "MarketValue",
  formulaReadable: "",
  otherDurationMode: "None",
  otherDurationValue: "",
  otherStartDate: "",
  otherEndDate: ""
};

export const BASE_FORMULA_VARIABLES = [
  { id: "MARKET_VALUE", label: "Market Value" },
  { id: "FACTOR_VALUE", label: "Factor Value" },
  { id: "ASSET_VALUE", label: "Assets" },
  { id: "BASE_COMPENSATION", label: "Base Compensation" },
  { id: "SOLATIUM_AMOUNT", label: "Solatium" },
  { id: "AFTER_SOLATIUM", label: "After Solatium" }
] as const;

export const ALL_RECOGNIZED_VARIABLES = [
  ...BASE_FORMULA_VARIABLES,
  { id: "DAYS", label: "Days" },
  { id: "MONTHS", label: "Months" }
] as const;

export const FORMULA_OPERATORS = ["+", "-", "×", "÷", "(", ")"] as const;

export function getAvailableFormulaVariables(durationMode: "None" | "Days" | "Months" | "DateRange") {
  const vars: Array<{ id: string; label: string }> = [...BASE_FORMULA_VARIABLES];
  if (durationMode === "Days" || durationMode === "DateRange") {
    vars.push({ id: "DAYS", label: "Days" });
  } else if (durationMode === "Months") {
    vars.push({ id: "MONTHS", label: "Months" });
  }
  return vars;
}

export function convertFormulaReadableToInternal(readable: string): string {
  let expr = readable;
  for (const v of ALL_RECOGNIZED_VARIABLES) {
    expr = expr.replaceAll(v.label, v.id);
  }
  expr = expr.replaceAll("×", "*").replaceAll("÷", "/");
  return expr.trim();
}

export function validateFormulaSyntax(
  readableFormula: string,
  durationMode: "None" | "Days" | "Months" | "DateRange"
): string | null {
  const trimmed = readableFormula.trim();
  if (!trimmed) return null;

  let openCount = 0;
  for (const ch of trimmed) {
    if (ch === "(") openCount++;
    if (ch === ")") openCount--;
    if (openCount < 0) return "Unbalanced parentheses (unexpected closing parenthesis).";
  }
  if (openCount !== 0) return "Unbalanced parentheses (unclosed parenthesis).";

  const internal = convertFormulaReadableToInternal(trimmed);

  if (/\bDAYS\b/.test(internal)) {
    if (durationMode !== "Days" && durationMode !== "DateRange") {
      return "DAYS variable requires Days or Date Range duration.";
    }
  }
  if (/\bMONTHS\b/.test(internal)) {
    if (durationMode !== "Months") {
      return "MONTHS variable requires Months duration.";
    }
  }

  if (!/^[A-Z0-9_.\s+\-*\/()]+$/.test(internal)) {
    return "Formula contains invalid characters.";
  }

  return null;
}

export function buildCompensationRequest(state: CompensationFormState): {
  valid: boolean;
  errors: Record<string, string>;
  payload?: CompensationRequest;
} {
  const errors: Record<string, string> = {};

  const parsedArea = parseNumericInput(state.landArea);
  if (!state.landArea.trim() || parsedArea <= 0) {
    errors["land.area"] = "Enter a valid land area greater than zero.";
    errors.landArea = "Enter a valid land area greater than zero.";
  }

  const parsedRate = parseNumericInput(state.marketRate);
  if (!state.marketRate.trim() || parsedRate <= 0) {
    errors["marketRate.amount"] = "Enter a valid market rate greater than zero.";
    errors.marketRate = "Enter a valid market rate greater than zero.";
  }

  const parsedFactor = parseNumericInput(state.multiplicationFactor);
  if (!state.multiplicationFactor.trim() || parsedFactor <= 0) {
    errors.multiplicationFactor = "Enter a valid multiplication factor greater than zero.";
  }

  if (state.useOfficialEquivalent) {
    const parsedOfficial = parseNumericInput(state.officialEquivalentArea);
    if (!state.officialEquivalentArea.trim() || parsedOfficial <= 0) {
      errors["land.equivalentAreaInRateUnit"] = "Enter a valid official equivalent area.";
      errors.officialEquivalentArea = "Enter a valid official equivalent area.";
    }
  }

  const parsedSolatium = parseNumericInput(state.solatiumPercentage);
  if (!state.solatiumPercentage.trim() || parsedSolatium < 0) {
    errors["solatium.percent"] = "Enter a valid solatium percentage (0 or more).";
    errors.solatiumPercentage = "Enter a valid solatium percentage (0 or more).";
  }

  if (state.additionalAmountType === "interest") {
    const parsedAnnualRate = parseNumericInput(state.annualRate);
    if (!state.annualRate.trim() || parsedAnnualRate < 0) {
      errors["additionalAmount.annualRatePercent"] = "Enter a valid annual interest rate (0 or more).";
      errors.annualRate = "Enter a valid annual interest rate (0 or more).";
    }

    if (state.durationType === "date_range") {
      if (!state.startDate || !state.endDate) {
        errors["additionalAmount.duration"] = "Select both start and end dates.";
        errors.duration = "Select both start and end dates.";
      } else if (state.endDate < state.startDate) {
        errors["additionalAmount.duration.endDate"] = "End date must be on or after start date.";
        errors.duration = "End date must be on or after start date.";
      }
    } else {
      const dur = parseNumericInput(state.durationValue);
      if (!state.durationValue.trim() || dur <= 0) {
        errors["additionalAmount.duration.value"] = "Enter a valid duration greater than zero.";
        errors.duration = "Enter a valid duration greater than zero.";
      }
    }
  } else {
    // Other mode
    if (!state.formulaReadable.trim()) {
      errors["additionalAmount.formula"] = "Enter a formula expression for Other additional amount.";
    } else {
      const syntaxError = validateFormulaSyntax(state.formulaReadable, state.otherDurationMode);
      if (syntaxError) {
        errors["additionalAmount.formula"] = syntaxError;
      }
    }

    if (state.otherDurationMode === "Days" || state.otherDurationMode === "Months") {
      const dur = parseNumericInput(state.otherDurationValue);
      if (!state.otherDurationValue.trim() || dur <= 0) {
        errors["additionalAmount.duration.value"] = `Enter a valid duration for ${state.otherDurationMode}.`;
        errors.otherDuration = `Enter a valid duration for ${state.otherDurationMode}.`;
      }
    } else if (state.otherDurationMode === "DateRange") {
      if (!state.otherStartDate || !state.otherEndDate) {
        errors["additionalAmount.duration"] = "Select both start and end dates for formula duration.";
        errors.otherDuration = "Select both start and end dates for formula duration.";
      } else if (state.otherEndDate < state.otherStartDate) {
        errors["additionalAmount.duration.endDate"] = "End date must be on or after start date.";
        errors.otherDuration = "End date must be on or after start date.";
      }
    }
  }

  if (Object.keys(errors).length > 0) {
    return { valid: false, errors };
  }

  const landPayload: {
    area: number;
    unit: string;
    equivalentAreaInRateUnit?: number;
  } = {
    area: parsedArea,
    unit: state.landAreaUnit
  };

  if (state.useOfficialEquivalent && parseNumericInput(state.officialEquivalentArea) > 0) {
    landPayload.equivalentAreaInRateUnit = parseNumericInput(state.officialEquivalentArea);
  }

  const treesAndStructuresNum = state.treesAndStructures.trim()
    ? parseNumericInput(state.treesAndStructures)
    : 0;

  let additionalAmountPayload: AdditionalAmountPayload;

  if (state.additionalAmountType === "interest") {
    let durationPayload: DurationPayload;
    if (state.durationType === "date_range") {
      durationPayload = {
        mode: "DateRange",
        startDate: state.startDate,
        endDate: state.endDate
      };
    } else {
      durationPayload = {
        mode: state.durationType === "months" ? "Months" : "Days",
        value: parseNumericInput(state.durationValue)
      };
    }

    additionalAmountPayload = {
      type: "Interest",
      annualRatePercent: parseNumericInput(state.annualRate),
      duration: durationPayload,
      basis: state.calculatedOn
    };
  } else {
    let durationPayload: DurationPayload | null = null;
    if (state.otherDurationMode === "Days" || state.otherDurationMode === "Months") {
      durationPayload = {
        mode: state.otherDurationMode,
        value: parseNumericInput(state.otherDurationValue)
      };
    } else if (state.otherDurationMode === "DateRange") {
      durationPayload = {
        mode: "DateRange",
        startDate: state.otherStartDate,
        endDate: state.otherEndDate
      };
    }

    additionalAmountPayload = {
      type: "Other",
      basis: "MarketValue",
      formula: convertFormulaReadableToInternal(state.formulaReadable),
      ...(durationPayload ? { duration: durationPayload } : {})
    };
  }

  const payload: CompensationRequest = {
    conversionProfile: "lac-delhi-v1",
    land: landPayload,
    marketRate: {
      amount: parsedRate,
      perUnit: state.marketRateUnit
    },
    multiplicationFactor: parsedFactor,
    assets: {
      treesAndStructures: treesAndStructuresNum
    },
    solatium: {
      percent: parsedSolatium
    },
    additionalAmount: additionalAmountPayload
  };

  return { valid: true, errors: {}, payload };
}
