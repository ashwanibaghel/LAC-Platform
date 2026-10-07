using System.Globalization;
using System.Text.RegularExpressions;

namespace LAC.Infrastructure;

public static class CourtDirectionDeadline
{
    public static IReadOnlyList<DateOnly> SourceDates(string text) => Regex.Matches(text,@"\b(?:\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4})\b",RegexOptions.IgnoreCase)
        .Select(m=>DateOnly.TryParseExact(m.Value,["d.M.yyyy","d/M/yyyy","d-M-yyyy","d MMMM yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) ? (DateOnly?)date : null)
        .Where(d=>d.HasValue).Select(d=>d!.Value).Distinct().ToList();
    public static DateOnly? Resolve(DateOnly orderDate,string? deadline,DateOnly? nextHearing=null)
    {
        if(string.IsNullOrWhiteSpace(deadline))return null;
        if(Regex.IsMatch(deadline,@"receipt|service|thereafter|determination",RegexOptions.IgnoreCase))return null;
        if(Regex.IsMatch(deadline,@"before.{0,20}next (?:date|hearing)",RegexOptions.IgnoreCase))return nextHearing;
        var dates=SourceDates(deadline);if(dates.Count==1)return dates[0];if(dates.Count>1)return null;
        if(!Regex.IsMatch(deadline,@"from (?:today|the date of (?:this|the) (?:order|judgment)|this (?:order|judgment))",RegexOptions.IgnoreCase))return null;
        var match=Regex.Match(deadline,@"within (?:a period of )?(\d+|one|two|three|four|five|six|seven|eight|nine|ten|twelve)\s*(?:\(\d+\)\s*)?(days?|weeks?|months?)",RegexOptions.IgnoreCase);
        if(!match.Success)return null;
        var words=new Dictionary<string,int> { ["one"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,["six"]=6,["seven"]=7,["eight"]=8,["nine"]=9,["ten"]=10,["twelve"]=12 };
        if(!int.TryParse(match.Groups[1].Value,out var amount) && !words.TryGetValue(match.Groups[1].Value.ToLowerInvariant(),out amount))return null;
        if(amount<=0 || amount>366)return null;
        try{return match.Groups[2].Value.StartsWith("month",StringComparison.OrdinalIgnoreCase) ? orderDate.AddMonths(amount) : orderDate.AddDays(amount*(match.Groups[2].Value.StartsWith("week",StringComparison.OrdinalIgnoreCase) ? 7 : 1));}
        catch(ArgumentOutOfRangeException){return null;}
    }
}
