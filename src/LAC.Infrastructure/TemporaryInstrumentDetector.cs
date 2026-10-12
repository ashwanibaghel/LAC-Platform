using System.Text.RegularExpressions;

namespace LAC.Infrastructure;

internal static class TemporaryInstrumentDetector
{
    public static bool IsOfferOrOccupationProceeding(string text) =>
        Regex.IsMatch(text, @"(?:^|\n)\s*(?:temporary\s+)?offer\s*(?:no\.?|number|#)\s*[:.-]?", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"\bsection\s*35\s*(?:\(\s*1\s*\))?", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(text, @"\b(?:temporary\s+(?:occupation|acquisition)|occupation\s+for|offer\s+no\.?|compensation\s+proceedings)\b", RegexOptions.IgnoreCase);
}
