namespace LAC.Domain.Calculators;

public enum AdditionalAmountType { Interest, Other }
public enum DurationMode { Days, Months, DateRange }
public enum InterestBasis { MarketValue, FactorAdjustedValue, BaseCompensation, AmountAfterSolatium }

public sealed record LandInput(decimal? Area, string? Unit, decimal? EquivalentAreaInRateUnit = null);
public sealed record MarketRateInput(decimal? Amount, string? PerUnit);
public sealed record AssetsInput(decimal? TreesAndStructures);
public sealed record SolatiumInput(decimal? Percent);
public sealed record DurationInput(DurationMode? Mode, decimal? Value = null, DateOnly? StartDate = null, DateOnly? EndDate = null);
public sealed record AdditionalAmountInput(
    AdditionalAmountType Type = AdditionalAmountType.Interest,
    decimal? AnnualRatePercent = null, DurationInput? Duration = null,
    InterestBasis Basis = InterestBasis.MarketValue, string? Formula = null);
public sealed record CompensationRequest(
    LandInput? Land, MarketRateInput? MarketRate, decimal? MultiplicationFactor,
    AssetsInput? Assets, SolatiumInput? Solatium, AdditionalAmountInput? AdditionalAmount,
    string ConversionProfile = CalculatorAreaConversion.Profile);

// Exact decimal strings prevent JavaScript clients from losing precision. Display is separate.
public sealed record MoneyValue(string Precise, string Display);
public sealed record CalculationStep(string Name, string Formula, string SubstitutedFormula, string Result);
public sealed record AreaCalculation(
    string EnteredArea, string Unit, string RateUnit, string Profile,
    string ProfileConvertedArea, string AppliedArea, bool UsesExplicitEquivalentArea);
public sealed record DurationCalculation(string Mode, string Value, string Fraction, string Convention);
public sealed record AdditionalAmountCalculation(
    string Type, string? Basis, string? AnnualRatePercent, DurationCalculation? Duration,
    string NormalizedFormula, string SubstitutedFormula,
    IReadOnlyDictionary<string, string> Variables, MoneyValue Amount);
public sealed record CompensationResponse(
    string Currency, AreaCalculation Area, string Rate, string MultiplicationFactor,
    string SolatiumPercent, MoneyValue MarketValue, MoneyValue FactorAdjustedValue,
    MoneyValue TreesAndStructures, MoneyValue BaseCompensation, MoneyValue SolatiumAmount,
    MoneyValue AmountAfterSolatium, AdditionalAmountCalculation AdditionalAmount,
    MoneyValue FinalCompensation, string FinalAmountInWords, string RoundingPolicy,
    IReadOnlyList<CalculationStep> Trace);

public sealed class CalculatorValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
