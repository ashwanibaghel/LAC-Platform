using System.Globalization;

namespace LAC.Domain.Calculators;

public static class CalculatorNumbers
{
    public const decimal MaxInput = 1_000_000_000_000m;
    public const decimal MaxResult = 1_000_000_000_000_000_000_000_000m;
    public static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    public static MoneyValue Money(decimal value) => new(Text(value),
        decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture));

    public static decimal Input(decimal? value, string field, bool positive = false)
    {
        if (value is null) throw new CalculatorValidationException(field, "A decimal value is required.");
        var scale = (decimal.GetBits(value.Value)[3] >> 16) & 0xff;
        if (scale > 12 || value < 0 || value > MaxInput || positive && value == 0)
            throw new CalculatorValidationException(field, $"Use a {(positive ? "positive" : "non-negative")} decimal up to {MaxInput} with at most 12 fractional digits.");
        return value.Value;
    }

    public static decimal Result(decimal value, string field)
    {
        if (value < -MaxResult || value > MaxResult)
            throw new CalculatorValidationException(field, "Calculation exceeds the supported magnitude (10^24).");
        return value;
    }

    public static decimal Multiply(decimal a, decimal b, string field) => ArithmeticResult(a * b, a != 0 && b != 0, field);
    public static decimal Divide(decimal a, decimal b, string field) => ArithmeticResult(a / b, a != 0, field);
    private static decimal ArithmeticResult(decimal result, bool mustBeNonzero, string field)
    {
        if (mustBeNonzero && result == 0)
            throw new CalculatorValidationException(field, "Calculation underflows decimal precision; increase input magnitudes.");
        return Result(result, field);
    }

    // Used before decimal parsing, which can otherwise silently round excessive JSON precision.
    public static bool TryParseInput(string text, out decimal value)
    {
        value = 0;
        if (text.Length is 0 or > 32) return false;
        var start = text[0] == '-' ? 1 : 0;
        var point = false;
        var fractional = 0;
        var digits = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '.' && !point && digits > 0) { point = true; continue; }
            if (text[i] < '0' || text[i] > '9') return false;
            digits++;
            if (point) fractional++;
        }
        return digits > 0 && (!point || fractional > 0) && fractional <= 12
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value >= -MaxInput && value <= MaxInput;
    }
}
