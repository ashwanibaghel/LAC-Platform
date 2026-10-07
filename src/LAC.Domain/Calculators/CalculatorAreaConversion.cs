using System.Collections.ObjectModel;

namespace LAC.Domain.Calculators;

public static class CalculatorAreaConversion
{
    public const string Profile = "lac-delhi-v1";
    // Decimal port of LAC.Web/src/calculator/landConversions.js AREA_UNITS.
    // Published rounded revenue references are deliberately not mixed into this graph.
    public static IReadOnlyDictionary<string, decimal> SquareMetresPerUnit { get; } =
        new ReadOnlyDictionary<string, decimal>(new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["bigha"] = 843m, ["biswa"] = 42.15m, ["biswansi"] = 2.1075m,
            ["sqm"] = 1m, ["sqyd"] = 0.83612736m, ["sqft"] = 0.09290304m,
            ["hectare"] = 10000m, ["acre"] = 4046.8564224m, ["sqkm"] = 1000000m
        });

    public static decimal Convert(decimal area, string? from, string? to, string? profile = Profile)
    {
        if (profile != Profile) throw new CalculatorValidationException("conversionProfile", $"Only {Profile} is supported.");
        CalculatorNumbers.Input(area, "land.area");
        if (from is null || !SquareMetresPerUnit.TryGetValue(from, out var source))
            throw new CalculatorValidationException("land.unit", "Unsupported area unit; use an AREA_UNITS identifier.");
        if (to is null || !SquareMetresPerUnit.TryGetValue(to, out var target))
            throw new CalculatorValidationException("marketRate.perUnit", "Unsupported rate unit; use an AREA_UNITS identifier.");
        return from == to ? area : CalculatorNumbers.Divide(CalculatorNumbers.Multiply(area, source, "land.area"), target, "land.area");
    }
}
