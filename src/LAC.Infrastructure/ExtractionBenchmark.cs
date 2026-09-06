using System.Text.Json;
using LAC.Domain;

namespace LAC.Infrastructure;

public sealed record ExtractionTruth(AwardIngestionCandidateType CandidateType, string Identity);
public sealed record ExtractionQualityMetric(int TruePositives, int FalsePositives, int FalseNegatives)
{
    public decimal Precision => TruePositives + FalsePositives == 0 ? 1m : (decimal)TruePositives / (TruePositives + FalsePositives);
    public decimal Recall => TruePositives + FalseNegatives == 0 ? 1m : (decimal)TruePositives / (TruePositives + FalseNegatives);
    public decimal F1 => Precision + Recall == 0 ? 0 : (2m * Precision * Recall) / (Precision + Recall);
}
public sealed record ExtractionBenchmarkResult(IReadOnlyDictionary<AwardIngestionCandidateType, ExtractionQualityMetric> ByCandidateType)
{
    public decimal AutoReadyPrecision => ByCandidateType.Values.Sum(x => x.TruePositives + x.FalsePositives) == 0 ? 1m : (decimal)ByCandidateType.Values.Sum(x => x.TruePositives) / ByCandidateType.Values.Sum(x => x.TruePositives + x.FalsePositives);
}

/// <summary>Small deterministic metric utility for fictional golden fixtures. It does not inspect or upload real documents.</summary>
public static class ExtractionBenchmark
{
    public static ExtractionBenchmarkResult Measure(IEnumerable<ExtractionCandidate> actual, IEnumerable<ExtractionTruth> expected)
    {
        var actualSet = actual.Select(candidate => new ExtractionTruth(candidate.Input.CandidateType, Identity(candidate.Input))).ToHashSet();
        var expectedSet = expected.ToHashSet();
        var types = actualSet.Select(x => x.CandidateType).Concat(expectedSet.Select(x => x.CandidateType)).Distinct();
        return new(types.ToDictionary(type => type, type =>
        {
            var found = actualSet.Where(x => x.CandidateType == type).ToHashSet(); var truth = expectedSet.Where(x => x.CandidateType == type).ToHashSet();
            return new ExtractionQualityMetric(found.Count(truth.Contains), found.Count(x => !truth.Contains(x)), truth.Count(x => !found.Contains(x)));
        }));
    }

    private static string Identity(IngestionCandidateInput input)
    {
        using var document = JsonDocument.Parse(input.PayloadJson); var root = document.RootElement;
        return input.CandidateType switch
        {
            AwardIngestionCandidateType.AwardCore => Property(root, "AwardNumber").GetString() ?? "",
            AwardIngestionCandidateType.AwardKhasra => (Property(root, "KhasraNumber").GetString() ?? "") + "|" + (TryProperty(root, "Qualifier", out var qualifier) ? qualifier.GetString() : ""),
            AwardIngestionCandidateType.Notification => (Property(root, "SectionType").GetString() ?? "") + "|" + (Property(root, "NotificationNumber").GetString() ?? ""),
            _ => input.CandidateType.ToString()
        };
    }
    private static JsonElement Property(JsonElement root, string name) => TryProperty(root, name, out var result) ? result : throw new InvalidOperationException($"Expected {name} in extraction candidate.");
    private static bool TryProperty(JsonElement root, string name, out JsonElement result) => root.TryGetProperty(name, out result) || root.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out result);
}
