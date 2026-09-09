using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record AwardKhasraFieldDecision(string FieldRole, string Decision, string? HumanValue, DateTimeOffset DecidedAt, string DecidedBy);

public sealed partial class AwardIngestionService
{
    private static readonly string[] AwardKhasraRoles = ["Khasra", "RecordedArea", "AwardedArea", "Qualifier"];

    public async Task VerifyAwardKhasraFieldAsync(Guid candidateId, VerifyAwardKhasraFieldRequest request, CancellationToken ct)
    {
        RequireReviewer(request.VerifiedBy);
        var row = await db.AwardIngestionCandidates.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == candidateId, ct)
            ?? throw new AwardIngestionException("Review item not found.", 404);
        if (row.CandidateType != AwardIngestionCandidateType.AwardKhasra || row.Session.SourceDocumentId is null)
            throw new AwardIngestionException("This field-review action is available only for document-backed Award Khasras.");
        if (row.Status is AwardIngestionCandidateStatus.Committed or AwardIngestionCandidateStatus.Rejected)
            throw new AwardIngestionException("This item is finalised.");
        var role = AwardKhasraRoles.SingleOrDefault(x => string.Equals(x, request.FieldRole, StringComparison.OrdinalIgnoreCase))
            ?? throw new AwardIngestionException("Unknown Award Khasra field.");
        var decision = request.Decision.Trim();
        if (decision is not "Confirm" and not "Correct" and not "Skip" and not "Uncertain")
            throw new AwardIngestionException("Choose Confirm, Correct, Skip, or Uncertain.");

        var payload = Deserialize<AwardKhasraCandidate>(row);
        var source = SourceForRole(row.SourceLocatorJson, role);
        if (source.Page < 1 || source.Region is null) throw new AwardIngestionException("This field has no valid source-cell evidence.");
        var required = RequiredRoles(payload, row.SourceLocatorJson);
        if (!required.Contains(role)) throw new AwardIngestionException("This field is not present in the source candidate.");
        string? human = request.HumanValue?.Trim();
        if (decision is "Confirm" or "Correct")
        {
            if (string.IsNullOrWhiteSpace(human)) throw new AwardIngestionException("Enter the value you verified from the source cell.");
            payload = ApplyField(payload, role, human);
        }

        var decisions = ReadDecisions(row.FieldReviewJson);
        var prior = decisions.FirstOrDefault(x => x.FieldRole == role);
        decisions.RemoveAll(x => x.FieldRole == role);
        decisions.Add(new(role, decision, human, DateTimeOffset.UtcNow, request.VerifiedBy.Trim()));
        row.FieldReviewJson = JsonSerializer.Serialize(decisions, Json);
        row.StructuredPayloadJson = JsonSerializer.Serialize(payload, Json);
        row.VerifiedAt = null; row.VerifiedBy = null; row.VerifiedPayloadJson = null; row.SafeToConfirm = false;

        if (decision is "Confirm" or "Correct")
            AddFieldTrainingGold(row, payload, role, source, human!, decision, request.VerifiedBy.Trim(), prior);

        var allConfirmed = required.All(requiredRole => decisions.Any(x => x.FieldRole == requiredRole && x.Decision is "Confirm" or "Correct"));
        if (allConfirmed)
        {
            ValidateReviewPayload(payload);
            var checkedRow = await AnalyzeAsync(row.Session, payload, row.Sequence, ct);
            if (checkedRow.Status is AwardIngestionCandidateStatus.Invalid or AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Conflict)
            {
                row.Status = checkedRow.Status;
                row.ValidationIssuesJson = checkedRow.ValidationIssuesJson;
                row.ConflictDetailsJson = checkedRow.ConflictDetailsJson;
            }
            else
            {
                await VerifiedSourcePageAsync(row, ct);
                row.CanonicalEntityId = checkedRow.CanonicalEntityId;
                row.CanonicalEntityType = checkedRow.CanonicalEntityType;
                MarkVerified(row, request.VerifiedBy, row.StructuredPayloadJson, "AllAwardKhasraFieldsVerified");
            }
        }
        else
        {
            // A partial decision never clears a pre-existing conflict.  All
            // other partial states remain visibly in the shared attention set.
            row.Status = row.ConflictDetailsJson is not null ? AwardIngestionCandidateStatus.Conflict : AwardIngestionCandidateStatus.NeedsReview;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Session.Status = SessionStatus(await db.AwardIngestionCandidates.Where(x => x.SessionId == row.SessionId).ToListAsync(ct));
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(AwardIngestionCandidate), EntityId = row.Id, Action = $"HumanField{decision}:{role}", ChangedBy = request.VerifiedBy.Trim(), OldValues = prior is null ? null : JsonSerializer.Serialize(prior, Json), NewValues = JsonSerializer.Serialize(decisions.Last(x => x.FieldRole == role), Json) });
        await db.SaveChangesAsync(ct);
    }

    private static HashSet<string> RequiredRoles(AwardKhasraCandidate payload, string? locator)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Khasra" };
        // Geometry-backed cells exist even when OCR could not read them. Such
        // a cell remains reviewable; only genuinely absent source cells are N/A.
        foreach (var role in new[] { "RecordedArea", "AwardedArea" }) if (SourceForRole(locator, role).Region is not null) roles.Add(role);
        if (!string.IsNullOrWhiteSpace(payload.Qualifier)) roles.Add("Qualifier");
        return roles;
    }

    private static AwardKhasraCandidate ApplyField(AwardKhasraCandidate payload, string role, string value) => role switch
    {
        "Khasra" => ParseKhasra(payload, value),
        "Qualifier" => ParseQualifier(payload, value),
        "RecordedArea" => SetArea(payload, value, true),
        "AwardedArea" => SetArea(payload, value, false),
        _ => throw new AwardIngestionException("Unknown Award Khasra field.")
    };

    private static AwardKhasraCandidate ParseKhasra(AwardKhasraCandidate payload, string value)
    {
        if (!new StrictKhasraParser().TryParse(value + (string.IsNullOrWhiteSpace(payload.Qualifier) ? "" : " " + payload.Qualifier), out var parsed, out _))
            throw new AwardIngestionException("Enter a valid Khasra identifier; digits are never guessed.");
        return payload with { KhasraNumber = parsed.NormalizedNumber, Qualifier = parsed.Qualifier };
    }
    private static AwardKhasraCandidate ParseQualifier(AwardKhasraCandidate payload, string value)
    {
        if (!new StrictKhasraParser().TryParse(payload.KhasraNumber + " " + value, out var parsed, out _)) throw new AwardIngestionException("Qualifier must be supported by the Khasra grammar.");
        return payload with { Qualifier = parsed.Qualifier };
    }
    private static AwardKhasraCandidate SetArea(AwardKhasraCandidate payload, string value, bool recorded)
    {
        if (!new StrictAreaParser().TryParse(value, out var area)) throw new AwardIngestionException("Enter area as Bigha-Biswa or Bigha-Biswa-Biswansi.");
        return recorded ? payload with { RecordedAreaBigha = area.Bigha, RecordedAreaBiswa = area.Biswa, RecordedAreaBiswansi = area.Biswansi } : payload with { AwardedAreaBigha = area.Bigha, AwardedAreaBiswa = area.Biswa, AwardedAreaBiswansi = area.Biswansi };
    }

    private void AddFieldTrainingGold(AwardIngestionCandidate row, AwardKhasraCandidate payload, string role, SourceCell source, string human, string decision, string reviewer, AwardKhasraFieldDecision? prior)
    {
        var normalized = role switch { "Khasra" => payload.KhasraNumber, "Qualifier" => payload.Qualifier, "RecordedArea" => FormatArea(payload.RecordedAreaBigha, payload.RecordedAreaBiswa, payload.RecordedAreaBiswansi), _ => FormatArea(payload.AwardedAreaBigha, payload.AwardedAreaBiswa, payload.AwardedAreaBiswansi) };
        // An accepted formatting normalization is not a correction. For a
        // user edit, append an auditable revision, never overwrite old gold.
        var corrected = !string.Equals(human, source.Normalized ?? normalized, StringComparison.Ordinal);
        if (prior?.Decision is "Confirm" or "Correct" && string.Equals(prior.HumanValue, human, StringComparison.Ordinal)) return;
        var revision = db.DocumentTrainingExamples.Where(x => x.SourceCandidateId == row.Id && x.CellRole == role).Select(x => (int?)x.VerificationRevision).Max() ?? 0;
        db.DocumentTrainingExamples.Add(new DocumentTrainingExample { DocumentId = row.Session.SourceDocumentId!.Value, PageNumber = source.Page, SourceRegionJson = source.Region!, CellRole = role, RawOcr = source.Raw, NormalizedSuggestion = source.Normalized ?? normalized, HumanFinalValue = human, WasCorrected = corrected, ReviewDecision = decision, VerifiedAt = DateTimeOffset.UtcNow, VerifiedBy = reviewer, SourceCandidateId = row.Id, VerificationRevision = revision + 1 });
    }

    private sealed record SourceCell(int Page, string? Region, string? Raw, string? Normalized);
    private static SourceCell SourceForRole(string? locator, string role)
    {
        var key = role switch { "Khasra" or "Qualifier" => "khasra", "RecordedArea" => "recordedArea", _ => "awardedArea" };
        try
        {
            using var root = JsonDocument.Parse(locator ?? "{}"); var page = root.RootElement.TryGetProperty("page", out var p) ? p.GetInt32() : root.RootElement.TryGetProperty("Page", out p) ? p.GetInt32() : 0;
            if (!(root.RootElement.TryGetProperty("structuredPayload", out var payload) || root.RootElement.TryGetProperty("StructuredPayload", out payload)) || !payload.TryGetProperty("sourceCells", out var cells) || !cells.TryGetProperty(key, out var cell)) return new(page, null, null, null);
            return new(page, cell.TryGetProperty("sourceRegion", out var region) ? region.GetRawText() : null, Value(cell, "rawOcr"), Value(cell, "normalizedSuggestion"));
        }
        catch (JsonException) { return new(0, null, null, null); }
    }
    private static List<AwardKhasraFieldDecision> ReadDecisions(string? value) { try { return JsonSerializer.Deserialize<List<AwardKhasraFieldDecision>>(value ?? "[]", Json) ?? []; } catch (JsonException) { return []; } }
}
