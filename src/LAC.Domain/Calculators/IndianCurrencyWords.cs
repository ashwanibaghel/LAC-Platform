namespace LAC.Domain.Calculators;

public static class IndianCurrencyWords
{
    private static readonly string[] Small = ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];
    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];
    public static string Format(decimal amount)
    {
        CalculatorNumbers.Result(amount, "finalCompensation");
        if (amount < 0) throw new CalculatorValidationException("finalCompensation", "Amount in words requires a non-negative amount.");
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var whole = decimal.Truncate(rounded);
        var paise = (int)((rounded - whole) * 100);
        return $"Rupees {Whole(whole)}{(paise == 0 ? "" : $" and {Whole(paise)} Paise")} Only";
    }
    private static string Whole(decimal value)
    {
        foreach (var (divisor, label) in new[] { (10000000m, "Crore"), (100000m, "Lakh"), (1000m, "Thousand"), (100m, "Hundred") })
            if (value >= divisor) return Whole(decimal.Truncate(value / divisor)) + " " + label + (value % divisor == 0 ? "" : " " + Whole(value % divisor));
        var number = (int)value;
        return number < 20 ? Small[number] : Tens[number / 10] + (number % 10 == 0 ? "" : " " + Small[number % 10]);
    }
}
