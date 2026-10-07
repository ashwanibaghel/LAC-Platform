namespace LAC.Domain.Calculators;

// Pure, stateless service: no database, network, clock, or workflow dependencies.
public sealed class CompensationCalculator
{
    public const string RoundingPolicy = "System.Decimal (28-29 significant digits); no intermediate money rounding; display money rounded to 2 decimals, midpoint away from zero. Final display rounds the unrounded total. Words use final display.";
    public CompensationResponse Compute(CompensationRequest request)
    {
        try { return Calculate(request); }
        catch (OverflowException) { throw new CalculatorValidationException("calculation", "Decimal arithmetic overflow; reduce input magnitudes."); }
    }

    private static CompensationResponse Calculate(CompensationRequest r)
    {
        var area = CalculatorNumbers.Input(r.Land?.Area, "land.area");
        var converted = CalculatorAreaConversion.Convert(area, r.Land?.Unit, r.MarketRate?.PerUnit, r.ConversionProfile);
        var applied = r.Land!.EquivalentAreaInRateUnit is { } equivalent
            ? CalculatorNumbers.Input(equivalent, "land.equivalentAreaInRateUnit") : converted;
        if (r.Land.EquivalentAreaInRateUnit.HasValue && (area == 0 != (applied == 0)
            || r.Land.Unit == r.MarketRate!.PerUnit && applied != area))
            throw new CalculatorValidationException("land.equivalentAreaInRateUnit", "Equivalent area must preserve zero area and agree when the units are identical.");
        var rate = CalculatorNumbers.Input(r.MarketRate?.Amount, "marketRate.amount", positive: true);
        var factor = CalculatorNumbers.Input(r.MultiplicationFactor, "multiplicationFactor", positive: true);
        var assets = CalculatorNumbers.Input(r.Assets?.TreesAndStructures, "assets.treesAndStructures");
        var solatium = CalculatorNumbers.Input(r.Solatium?.Percent, "solatium.percent");
        var extra = r.AdditionalAmount ?? throw new CalculatorValidationException("additionalAmount", "Additional amount inputs are required.");
        if (!Enum.IsDefined(extra.Type)) throw new CalculatorValidationException("additionalAmount.type", "Use Interest or Other.");
        var trace = new List<CalculationStep>();
        decimal Step(string name, string formula, string substituted, decimal value)
        {
            CalculatorNumbers.Result(value, name);
            trace.Add(new(name, formula, substituted, CalculatorNumbers.Text(value)));
            return value;
        }
        var sourceFactor = CalculatorAreaConversion.SquareMetresPerUnit[r.Land.Unit!];
        var targetFactor = CalculatorAreaConversion.SquareMetresPerUnit[r.MarketRate!.PerUnit!];
        Step("profileConvertedArea", "land.area * sourceSquareMetresPerUnit / rateSquareMetresPerUnit",
            $"{T(area)} * {T(sourceFactor)} / {T(targetFactor)}", converted);
        Step("appliedArea", r.Land.EquivalentAreaInRateUnit.HasValue ? "land.equivalentAreaInRateUnit (user-supplied assumption)" : "profileConvertedArea", T(applied), applied);
        var market = Step("marketValue", "AREA * RATE", $"{T(applied)} * {T(rate)}", CalculatorNumbers.Multiply(applied, rate, "marketValue"));
        var factorValue = Step("factorAdjustedValue", "MARKET_VALUE * FACTOR", $"{T(market)} * {T(factor)}", CalculatorNumbers.Multiply(market, factor, "factorAdjustedValue"));
        var basis = Step("baseCompensation", "FACTOR_VALUE + ASSET_VALUE", $"{T(factorValue)} + {T(assets)}", factorValue + assets);
        var solatiumValue = Step("solatiumAmount", "BASE_COMPENSATION * SOLATIUM_PERCENT / 100", $"{T(basis)} * {T(solatium)} / 100",
            CalculatorNumbers.Divide(CalculatorNumbers.Multiply(basis, solatium, "solatiumAmount"), 100m, "solatiumAmount"));
        var after = Step("amountAfterSolatium", "BASE_COMPENSATION + SOLATIUM_AMOUNT", $"{T(basis)} + {T(solatiumValue)}", basis + solatiumValue);
        var variables = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["AREA"] = applied, ["RATE"] = rate, ["MARKET_VALUE"] = market, ["FACTOR"] = factor,
            ["FACTOR_VALUE"] = factorValue, ["ASSET_VALUE"] = assets, ["BASE_COMPENSATION"] = basis,
            ["SOLATIUM_PERCENT"] = solatium, ["SOLATIUM_AMOUNT"] = solatiumValue, ["AFTER_SOLATIUM"] = after
        };
        DurationCalculation? duration = null;
        decimal fraction = 0;
        if (extra.Duration is not null)
        {
            var (durationTrace, durationFraction, count) = CalculateDuration(extra.Duration);
            duration = durationTrace;
            fraction = durationFraction;
            variables[extra.Duration.Mode == DurationMode.Months ? "MONTHS" : "DAYS"] = count;
        }
        FormulaCalculation formulaResult;
        string? interestBasis = null;
        string? annualRate = null;
        if (extra.Type == AdditionalAmountType.Interest)
        {
            if (extra.Formula is not null) throw new CalculatorValidationException("additionalAmount.formula", "Formula is available only for Other.");
            if (duration is null) throw new CalculatorValidationException("additionalAmount.duration", "Duration is required for Interest.");
            var percent = CalculatorNumbers.Input(extra.AnnualRatePercent, "additionalAmount.annualRatePercent");
            var (key, interestValue) = extra.Basis switch
            {
                InterestBasis.MarketValue => ("MARKET_VALUE", market),
                InterestBasis.FactorAdjustedValue => ("FACTOR_VALUE", factorValue),
                InterestBasis.BaseCompensation => ("BASE_COMPENSATION", basis),
                InterestBasis.AmountAfterSolatium => ("AFTER_SOLATIUM", after),
                _ => throw new CalculatorValidationException("additionalAmount.basis", "Unsupported interest basis.")
            };
            var durationVariable = extra.Duration!.Mode == DurationMode.Months ? "MONTHS" : "DAYS";
            var divisor = extra.Duration.Mode == DurationMode.Months ? 12 : 365;
            // Apply the documented durationFraction as one decimal operation, without display rounding.
            var interest = CalculatorNumbers.Multiply(CalculatorNumbers.Divide(CalculatorNumbers.Multiply(interestValue, percent, "additionalAmount.annualRatePercent"), 100m, "additionalAmount.annualRatePercent"), fraction, "additionalAmount.duration");
            formulaResult = new(interest,
                $"{key} * ANNUAL_RATE_PERCENT / 100 * ({durationVariable} / {divisor})",
                $"{T(interestValue)} * {T(percent)} / 100 * ({duration.Value} / {divisor})");
            variables["ANNUAL_RATE_PERCENT"] = percent;
            interestBasis = extra.Basis.ToString();
            annualRate = T(percent);
        }
        else
        {
            if (extra.AnnualRatePercent is not null || extra.Basis != InterestBasis.MarketValue)
                throw new CalculatorValidationException("additionalAmount", "Other accepts a formula and optional duration, not interest inputs.");
            formulaResult = SafeArithmeticFormula.Evaluate(extra.Formula, variables);
        }
        if (formulaResult.Value < 0) throw new CalculatorValidationException("additionalAmount.formula", "Additional amount cannot be negative.");
        var additional = Step("additionalAmount", formulaResult.Normalized, formulaResult.Substituted, formulaResult.Value);
        var final = Step("finalCompensation", "AFTER_SOLATIUM + ADDITIONAL_AMOUNT", $"{T(after)} + {T(additional)}", after + additional);
        return new("INR", new(T(area), r.Land.Unit!, r.MarketRate.PerUnit!, r.ConversionProfile, T(converted), T(applied), r.Land.EquivalentAreaInRateUnit.HasValue),
            T(rate), T(factor), T(solatium), M(market), M(factorValue), M(assets), M(basis), M(solatiumValue), M(after),
            new(extra.Type.ToString(), interestBasis, annualRate, duration, formulaResult.Normalized, formulaResult.Substituted,
                variables.ToDictionary(x => x.Key, x => T(x.Value), StringComparer.Ordinal), M(additional)),
            M(final), IndianCurrencyWords.Format(final), RoundingPolicy, trace);
    }

    private static (DurationCalculation Trace, decimal Fraction, decimal Count) CalculateDuration(DurationInput d)
    {
        const string field = "additionalAmount.duration";
        decimal count;
        int divisor;
        string convention;
        switch (d.Mode)
        {
            case DurationMode.Days:
            case DurationMode.Months:
                if (d.StartDate.HasValue || d.EndDate.HasValue) throw new CalculatorValidationException(field, "Dates are accepted only for DateRange.");
                count = CalculatorNumbers.Input(d.Value, field + ".value");
                divisor = d.Mode == DurationMode.Months ? 12 : 365;
                convention = d.Mode == DurationMode.Months ? "Months / 12; fractional months permitted; no days conversion." : "Days / 365; fractional days permitted; fixed 365-day year.";
                break;
            case DurationMode.DateRange:
                if (d.Value.HasValue || !d.StartDate.HasValue || !d.EndDate.HasValue)
                    throw new CalculatorValidationException(field, "Supply startDate and endDate only for DateRange.");
                count = d.EndDate.Value.DayNumber - d.StartDate.Value.DayNumber;
                if (count < 0) throw new CalculatorValidationException(field + ".endDate", "End date must be on or after start date.");
                divisor = 365;
                convention = $"Exact calendar days: [{d.StartDate:yyyy-MM-dd}, {d.EndDate:yyyy-MM-dd}); start inclusive, end exclusive; Days / 365 even in leap years.";
                break;
            default: throw new CalculatorValidationException(field + ".mode", "Use Days, Months or DateRange.");
        }
        var fraction = count / divisor;
        return (new(d.Mode!.ToString()!, T(count), T(fraction), convention), fraction, count);
    }
    private static string T(decimal value) => CalculatorNumbers.Text(value);
    private static MoneyValue M(decimal value) => CalculatorNumbers.Money(value);
}
