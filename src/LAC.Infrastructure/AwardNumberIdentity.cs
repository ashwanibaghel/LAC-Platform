using System.Text.RegularExpressions;

namespace LAC.Infrastructure;

internal static partial class AwardNumberIdentity
{
    [GeneratedRegex(@"^(?<number>[0-9]+)\s*/\s*(?<start>[0-9]{4})\s*-\s*(?<end>[0-9]{2}|[0-9]{4})$")]
    private static partial Regex YearRangePattern();

    public static bool Equivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        var first = left.Trim().TrimEnd('.').TrimEnd();
        var second = right.Trim().TrimEnd('.').TrimEnd();
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
        return TryNormalizeYearRange(first, out var firstKey)
            && TryNormalizeYearRange(second, out var secondKey)
            && string.Equals(firstKey, secondKey, StringComparison.Ordinal);
    }

    private static bool TryNormalizeYearRange(string value, out string key)
    {
        key = string.Empty;
        var match = YearRangePattern().Match(value);
        if (!match.Success) return false;
        var start = int.Parse(match.Groups["start"].Value);
        var endText = match.Groups["end"].Value;
        var end = int.Parse(endText);
        if (endText.Length == 2)
        {
            end += start / 100 * 100;
            if (end < start) end += 100;
        }
        key = $"{match.Groups["number"].Value}/{start:D4}-{end:D4}";
        return true;
    }
}
