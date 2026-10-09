using System.Globalization;
using System.Text.RegularExpressions;
using LAC.Domain.Calculators;
namespace LAC.Api;

public sealed record CompensationHistoryInputs(
    string LandArea, string LandAreaUnit, string MarketRate, string MarketRateUnit,
    bool UseOfficialEquivalent, string OfficialEquivalentArea, string MultiplicationFactor,
    string TreesAndStructures, string SolatiumPercentage, string AdditionalAmountType,
    string AnnualRate, string DurationType, string DurationValue, string StartDate, string EndDate,
    string CalculatedOn, string FormulaReadable, string OtherDurationMode, string OtherDurationValue,
    string OtherStartDate, string OtherEndDate)
{
    public CompensationRequest Normalize()
    {
        decimal Number(string? text, string field) => CalculatorNumbers.TryParseInput((text ?? "").Replace(",", "").Replace("₹", "").Trim(), out var value)
            ? value : throw new CalculatorValidationException(field, "Enter a bounded plain decimal with at most 12 fractional digits.");
        DateOnly Date(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new CalculatorValidationException("duration", "Use a valid ISO date.");
        DurationInput Duration(string mode, string value, string start, string end) => mode switch {
            "days" or "Days" => new(DurationMode.Days, Number(value, "duration")),
            "months" or "Months" => new(DurationMode.Months, Number(value, "duration")),
            "date_range" or "DateRange" => new(DurationMode.DateRange, StartDate: Date(start), EndDate: Date(end)),
            _ => throw new CalculatorValidationException("duration", "Invalid duration mode.") };
        var text = LandArea?.Trim() ?? "";
        decimal area;
        if (text.Contains('-')) {
            var match = Regex.Match(text, @"^(\d+)-(\d+)(?:-(\d+))?$", RegexOptions.CultureInvariant);
            if (LandAreaUnit != "bigha" || !match.Success) throw new CalculatorValidationException("land.area", "Use 4-16 or 2-9-1 only with Bigha.");
            var bigha=Number(match.Groups[1].Value, "land.area");
            var biswa=Number(match.Groups[2].Value, "land.area");
            var biswansi=match.Groups[3].Success ? Number(match.Groups[3].Value, "land.area") : 0;
            if (biswa > 19 || biswansi > 19) throw new CalculatorValidationException("land.area", "Biswa and Biswansi must be between 0 and 19.");
            area=bigha + biswa / 20m + biswansi / 400m;
            if (area > 1_000_000_000_000m) throw new CalculatorValidationException("land.area", "Area exceeds the decimal limit.");
        } else area=Number(text, "land.area");
        if (!Enum.TryParse<InterestBasis>(CalculatedOn, false, out var basis) || !Enum.IsDefined(basis)) throw new CalculatorValidationException("calculatedOn", "Invalid interest basis.");
        AdditionalAmountInput additional;
        if (AdditionalAmountType == "interest") additional = new(AnnualRatePercent: Number(AnnualRate, "annualRate"), Duration: Duration(DurationType, DurationValue, StartDate, EndDate), Basis: basis);
        else if (AdditionalAmountType == "other") {
            var formula=FormulaReadable ?? "";
            foreach (var (label, variable) in new[] {("Market Value","MARKET_VALUE"),("Factor Value","FACTOR_VALUE"),("Assets","ASSET_VALUE"),("Base Compensation","BASE_COMPENSATION"),("Solatium","SOLATIUM_AMOUNT"),("After Solatium","AFTER_SOLATIUM"),("Days","DAYS"),("Months","MONTHS")}) formula=formula.Replace(label, variable);
            additional=new(LAC.Domain.Calculators.AdditionalAmountType.Other, Duration: OtherDurationMode == "None" ? null : Duration(OtherDurationMode, OtherDurationValue, OtherStartDate, OtherEndDate), Basis: basis, Formula: formula.Replace("×","*").Replace("÷","/").Trim());
        } else throw new CalculatorValidationException("additionalAmountType", "Invalid additional amount type.");
        return new(new(area, LandAreaUnit, UseOfficialEquivalent ? Number(OfficialEquivalentArea, "officialEquivalentArea") : null), new(Number(MarketRate, "marketRate"), MarketRateUnit), Number(MultiplicationFactor, "multiplicationFactor"), new(string.IsNullOrWhiteSpace(TreesAndStructures) ? 0 : Number(TreesAndStructures, "treesAndStructures")), new(Number(SolatiumPercentage, "solatiumPercentage")), additional);
    }
}
