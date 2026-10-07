using System.Globalization;
using System.Text.RegularExpressions;
using LAC.Domain.Calculators;
using Xunit;

namespace LAC.Tests;

public sealed class CompensationCalculatorTests
{
    private readonly CompensationCalculator calculator = new();
    public static CompensationRequest Sample() => new(new(18m, "bigha", 3.744m), new(5300000m, "acre"), 2m,
        new(0m), new(100m), new(AnnualRatePercent: 12m, Duration: new(DurationMode.Days, 30m)));
    private static CompensationRequest Simple() => new(new(2m, "acre"), new(100m, "acre"), 1m,
        new(0m), new(0m), new(AnnualRatePercent: 0m, Duration: new(DurationMode.Days, 0m)));
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void OfficeReference_ReproducesEveryStage_WithoutChangingProfile()
    {
        var result = calculator.Compute(Sample());
        Assert.Equal("19843200.00", result.MarketValue.Display);
        Assert.Equal("39686400.00", result.FactorAdjustedValue.Display);
        Assert.Equal(result.FactorAdjustedValue, result.BaseCompensation);
        Assert.Equal("39686400.00", result.SolatiumAmount.Display);
        Assert.Equal("79372800.00", result.AmountAfterSolatium.Display);
        Assert.Equal("195713.75", result.AdditionalAmount.Amount.Display);
        Assert.Equal("79568513.75", result.FinalCompensation.Display);
        Assert.Equal(19843200m * 12m / 100m * (30m / 365m), D(result.AdditionalAmount.Amount.Precise));
        Assert.True(result.Area.UsesExplicitEquivalentArea);
        Assert.Equal("3.744", result.Area.AppliedArea);
        Assert.Equal(18m * 843m / 4046.8564224m, D(result.Area.ProfileConvertedArea));
        Assert.Equal("Rupees Seven Crore Ninety Five Lakh Sixty Eight Thousand Five Hundred Thirteen and Seventy Five Paise Only", result.FinalAmountInWords);
        Assert.Equal("INR", result.Currency);
        Assert.Contains("user-supplied", result.Trace.Single(x => x.Name == "appliedArea").Formula);
        Assert.Contains("Days / 365", result.AdditionalAmount.Duration!.Convention);
        Assert.Equal("MARKET_VALUE * ANNUAL_RATE_PERCENT / 100 * (DAYS / 365)", result.AdditionalAmount.NormalizedFormula);
    }

    [Fact]
    public void DefaultProfile_UsesAcceptedFrontendConversionRatherThanRoundedExample()
    {
        var result = calculator.Compute(Sample() with { Land = new(18m, "bigha") });
        Assert.False(result.Area.UsesExplicitEquivalentArea);
        Assert.Equal(result.Area.ProfileConvertedArea, result.Area.AppliedArea);
        Assert.NotEqual("19843200.00", result.MarketValue.Display);
    }

    // Read the real frontend graph, so a later frontend constant/unit addition fails parity.
    public static IEnumerable<object[]> AreaPairs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LAC-Platform.sln"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/LAC.Web/src/calculator/landConversions.js"));
        var graph = source[..source.IndexOf("export const LENGTH_UNITS", StringComparison.Ordinal)];
        var units = Regex.Matches(graph, "(\\w+): \\{ label: [^\\r\\n]+?sqm: ([0-9.]+) \\}")
            .Select(m => (Unit: m.Groups[1].Value, Sqm: D(m.Groups[2].Value))).ToArray();
        Assert.Equal(9, units.Length);
        Assert.Equal(units.Length, CalculatorAreaConversion.SquareMetresPerUnit.Count);
        foreach (var from in units)
            foreach (var to in units) yield return [from.Unit, from.Sqm, to.Unit, to.Sqm];
    }
    [Theory]
    [MemberData(nameof(AreaPairs))]
    public void AllAcceptedUnits_ConversionParity(string from, decimal source, string to, decimal target)
    {
        Assert.Equal(source, CalculatorAreaConversion.SquareMetresPerUnit[from]);
        Assert.Equal(target, CalculatorAreaConversion.SquareMetresPerUnit[to]);
        Assert.Equal(from == to ? 18m : 18m * source / target, CalculatorAreaConversion.Convert(18m, from, to));
    }

    [Theory]
    [InlineData(1, 0, 0, "200")]
    [InlineData(2, 0, 100, "800")]
    [InlineData(1, 50, 0, "250")]
    [InlineData(2, 50, 25, "562.5")]
    public void FactorAssetsSolatium_AreUserInputs(int factor, int assets, int solatium, string expected)
    {
        var result = calculator.Compute(Simple() with { MultiplicationFactor = factor, Assets = new(assets), Solatium = new(solatium) });
        Assert.Equal(D(expected), D(result.FinalCompensation.Precise));
        Assert.Equal("2", result.Area.AppliedArea);
    }

    [Fact]
    public void CrossUnitRate_ComputesInRateUnit()
    {
        var result = calculator.Compute(Simple() with { Land = new(1m, "hectare"), MarketRate = new(2m, "sqm") });
        Assert.Equal("10000", result.Area.AppliedArea);
        Assert.Equal("20000", result.FinalCompensation.Precise);
    }
    [Theory]
    [InlineData(InterestBasis.MarketValue, "200")]
    [InlineData(InterestBasis.FactorAdjustedValue, "400")]
    [InlineData(InterestBasis.BaseCompensation, "450")]
    [InlineData(InterestBasis.AmountAfterSolatium, "900")]
    public void EachInterestBasis(InterestBasis basis, string expected)
    {
        var result = calculator.Compute(Simple() with { MultiplicationFactor = 2m, Assets = new(50m), Solatium = new(100m),
            AdditionalAmount = new(AnnualRatePercent: 100m, Duration: new(DurationMode.Days, 365m), Basis: basis) });
        Assert.Equal(expected, result.AdditionalAmount.Amount.Precise);
        Assert.Equal(basis.ToString(), result.AdditionalAmount.Basis);
    }
    [Fact]
    public void Months_UseMonthsOverTwelve()
    {
        var result = calculator.Compute(Simple() with { AdditionalAmount = new(AnnualRatePercent: 12m, Duration: new(DurationMode.Months, 1.5m)) });
        Assert.Equal("3", result.AdditionalAmount.Amount.Precise);
        Assert.Equal("0.125", result.AdditionalAmount.Duration!.Fraction);
        Assert.Contains("Months / 12", result.AdditionalAmount.Duration.Convention);
        Assert.False(result.AdditionalAmount.Variables.ContainsKey("DAYS"));
    }
    [Theory]
    [InlineData("2024-02-28", "2024-03-01", "2")]
    [InlineData("2026-01-01", "2026-01-31", "30")]
    [InlineData("2026-01-01", "2026-01-01", "0")]
    public void DateRange_ExactDaysWithExclusiveEnd(string start, string end, string days)
    {
        var result = calculator.Compute(Simple() with { AdditionalAmount = new(AnnualRatePercent: 100m, Duration: new(DurationMode.DateRange, StartDate: DateOnly.Parse(start), EndDate: DateOnly.Parse(end))) });
        Assert.Equal(days, result.AdditionalAmount.Duration!.Value);
        Assert.Equal(200m * (D(days) / 365m), D(result.AdditionalAmount.Amount.Precise));
        Assert.Contains("start inclusive, end exclusive", result.AdditionalAmount.Duration.Convention);
    }

    [Theory]
    [InlineData("AREA * RATE + (BASE_COMPENSATION / 2)", "300")]
    [InlineData("10 - 2 * 3", "4")]
    [InlineData("(10 - 2) * 3", "24")]
    [InlineData("10 / 2 / 5", "1")]
    [InlineData("-(-1) + +2", "3")]
    [InlineData("0.1 + 0.2", "0.3")]
    [InlineData("ASSET_VALUE + SOLATIUM_AMOUNT + FACTOR + FACTOR_VALUE + AFTER_SOLATIUM + SOLATIUM_PERCENT + MARKET_VALUE", "601")]
    public void Other_HappyPath(string formula, string expected)
    {
        var result = calculator.Compute(Simple() with { AdditionalAmount = new(AdditionalAmountType.Other, Formula: formula) });
        Assert.Equal(expected, result.AdditionalAmount.Amount.Precise);
        Assert.DoesNotContain("AREA", result.AdditionalAmount.SubstitutedFormula);
        Assert.Equal(result.AdditionalAmount.NormalizedFormula, calculator.Compute(Simple() with { AdditionalAmount = new(AdditionalAmountType.Other, Formula: result.AdditionalAmount.NormalizedFormula) }).AdditionalAmount.NormalizedFormula);
    }
    [Fact]
    public void Other_OptionalDurationEnablesOnlyItsRelevantVariable()
    {
        var result = calculator.Compute(Simple() with { AdditionalAmount = new(AdditionalAmountType.Other, Duration: new(DurationMode.Months, 2m), Formula: "BASE_COMPENSATION * MONTHS / 12") });
        Assert.Equal(200m * 2m / 12m, D(result.AdditionalAmount.Amount.Precise));
        Assert.False(result.AdditionalAmount.Variables.ContainsKey("DAYS"));
    }
    [Theory]
    [InlineData("UNKNOWN + 1")]
    [InlineData("DAYS + 1")]
    [InlineData("MONTHS + 1")]
    [InlineData("1 / (2 - 2)")]
    [InlineData("Math.Abs(1)")]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("1; DROP TABLE Awards")]
    [InlineData("eval(1)")]
    [InlineData("AREA.ToString()")]
    [InlineData("1e2")]
    [InlineData("2**3")]
    [InlineData("2^3")]
    [InlineData("'1'")]
    [InlineData("1..2")]
    [InlineData(".1")]
    [InlineData("1+")]
    [InlineData("(1+2")]
    [InlineData("1 2")]
    [InlineData("2(3)")]
    [InlineData("0.0000000000001")]
    [InlineData("1000000000001")]
    [InlineData("1000000000000 * 1000000000000 * 1000000000000")]
    [InlineData("-1")]
    [InlineData("0.000000000001 * 0.000000000001 * 0.000000000001")]
    public void UnsafeInvalidOrNegativeFormulas_AreFieldErrors(string formula)
    {
        var ex = Assert.Throws<CalculatorValidationException>(() => calculator.Compute(Simple() with { AdditionalAmount = new(AdditionalAmountType.Other, Formula: formula) }));
        Assert.Equal("additionalAmount.formula", ex.Field);
    }
    [Theory]
    [InlineData("length")]
    [InlineData("tokens")]
    [InlineData("parentheses")]
    [InlineData("unary")]
    public void Complexity_IsBounded(string kind)
    {
        var formula = kind switch { "length" => new string('1', 2049), "tokens" => string.Join("+", Enumerable.Repeat("1", 150)), "parentheses" => new string('(', 34) + "1" + new string(')', 34), _ => new string('-', 34) + "1" };
        Assert.Throws<CalculatorValidationException>(() => calculator.Compute(Simple() with { AdditionalAmount = new(AdditionalAmountType.Other, Formula: formula) }));
    }
    public static IEnumerable<object[]> InvalidInputs()
    {
        var s = Simple();
        yield return [s with { Land = new(-1m, "acre") }, "land.area"];
        yield return [s with { MarketRate = new(0m, "acre") }, "marketRate.amount"];
        yield return [s with { MarketRate = new(-1m, "acre") }, "marketRate.amount"];
        yield return [s with { MultiplicationFactor = 0m }, "multiplicationFactor"];
        yield return [s with { MultiplicationFactor = -1m }, "multiplicationFactor"];
        yield return [s with { Assets = new(-1m) }, "assets.treesAndStructures"];
        yield return [s with { Solatium = new(-1m) }, "solatium.percent"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: -1m, Duration: new(DurationMode.Days, 1m)) }, "additionalAmount.annualRatePercent"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.Days, -1m)) }, "additionalAmount.duration.value"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.Months, -1m)) }, "additionalAmount.duration.value"];
        yield return [s with { Land = new(1m, "unknown") }, "land.unit"];
        yield return [s with { MarketRate = new(1m, "unknown") }, "marketRate.perUnit"];
        yield return [s with { ConversionProfile = "universal-bigha" }, "conversionProfile"];
        yield return [s with { Land = new(1m, "acre", 2m) }, "land.equivalentAreaInRateUnit"];
        yield return [s with { Land = new(0m, "bigha", 1m) }, "land.equivalentAreaInRateUnit"];
        yield return [s with { Land = new(1m, "bigha", -1m) }, "land.equivalentAreaInRateUnit"];
        yield return [s with { Land = new(0.0000000000001m, "acre") }, "land.area"];
        yield return [s with { MultiplicationFactor = 1000000000001m }, "multiplicationFactor"];
        yield return [s with { Land = null }, "land.area"];
        yield return [s with { Assets = null }, "assets.treesAndStructures"];
        yield return [s with { Solatium = null }, "solatium.percent"];
        yield return [s with { AdditionalAmount = null }, "additionalAmount"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m) }, "additionalAmount.duration"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.DateRange, StartDate: new(2026, 1, 2), EndDate: new(2026, 1, 1))) }, "additionalAmount.duration.endDate"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.DateRange, 1m, new(2026, 1, 1), new(2026, 1, 2))) }, "additionalAmount.duration"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.Days, 1m, new(2026, 1, 1))) }, "additionalAmount.duration"];
        yield return [s with { AdditionalAmount = new((AdditionalAmountType)999) }, "additionalAmount.type"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new((DurationMode)999)) }, "additionalAmount.duration.mode"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.Days, 1m), Basis: (InterestBasis)999) }, "additionalAmount.basis"];
        yield return [s with { AdditionalAmount = new(AnnualRatePercent: 1m, Duration: new(DurationMode.Days, 1m), Formula: "1") }, "additionalAmount.formula"];
        yield return [s with { AdditionalAmount = new(AdditionalAmountType.Other, AnnualRatePercent: 1m, Formula: "1") }, "additionalAmount"];
    }
    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void InvalidInput_ReturnsFieldValidation(CompensationRequest request, string field)
    {
        Assert.Equal(field, Assert.Throws<CalculatorValidationException>(() => calculator.Compute(request)).Field);
    }
    [Fact]
    public void ExcessiveIntermediateMagnitude_IsValidationError()
    {
        Assert.Throws<CalculatorValidationException>(() => calculator.Compute(Simple() with { Land = new(1000000000000m, "sqkm"), MarketRate = new(1000000000000m, "sqft") }));
    }
    [Fact]
    public void Precision_DisplayRoundsOnceFromUnroundedTotal()
    {
        var result = calculator.Compute(Simple() with { Land = new(1m, "acre"), MarketRate = new(0.005m, "acre"), MultiplicationFactor = 1.5m, Solatium = new(50m) });
        Assert.Equal("0.005", result.MarketValue.Precise);
        Assert.Equal("0.01", result.MarketValue.Display);
        Assert.Equal("0.0075", result.BaseCompensation.Precise);
        Assert.Equal("0.00375", result.SolatiumAmount.Precise);
        Assert.Equal("0.01125", result.FinalCompensation.Precise);
        Assert.Equal("0.01", result.FinalCompensation.Display);
    }
    [Fact]
    public void Precision_NonzeroValuesCannotSilentlyUnderflowToZero()
    {
        var request = Simple() with { Land = new(0.000000000001m, "acre"), MarketRate = new(0.000000000001m, "acre"), MultiplicationFactor = 0.000000000001m };
        Assert.Equal("factorAdjustedValue", Assert.Throws<CalculatorValidationException>(() => calculator.Compute(request)).Field);
    }
    [Theory]
    [InlineData("0", "Rupees Zero Only")]
    [InlineData("0.01", "Rupees Zero and One Paise Only")]
    [InlineData("0.995", "Rupees One Only")]
    [InlineData("999.99", "Rupees Nine Hundred Ninety Nine and Ninety Nine Paise Only")]
    [InlineData("1000", "Rupees One Thousand Only")]
    [InlineData("100000", "Rupees One Lakh Only")]
    [InlineData("10000000", "Rupees One Crore Only")]
    [InlineData("1000000000", "Rupees One Hundred Crore Only")]
    [InlineData("10000001.10", "Rupees One Crore One and Ten Paise Only")]
    [InlineData("999.995", "Rupees One Thousand Only")]
    [InlineData("99999.995", "Rupees One Lakh Only")]
    [InlineData("9999999.995", "Rupees One Crore Only")]
    public void IndianWords_Boundaries(string value, string expected) => Assert.Equal(expected, IndianCurrencyWords.Format(D(value)));
    [Fact]
    public async Task StatelessService_IsDeterministicAcrossConcurrentCalls()
    {
        var expected = calculator.Compute(Sample());
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => calculator.Compute(Sample()))));
        Assert.All(results, actual => Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual)));
        Assert.Empty(typeof(CompensationCalculator).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
    }
}
