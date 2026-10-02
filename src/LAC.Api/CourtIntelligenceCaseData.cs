using System.Text.Json;
using System.Text.Json.Nodes;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

// Only the existing registered matter and durable order observations. No writes.
public sealed record CourtIntelligenceCaseIndex(Guid CaseId, string CaseNumber,
    IReadOnlyList<CourtIntelligenceKnownOrder> Orders);

public static class CourtIntelligenceCaseData
{
    public static async Task<CourtIntelligenceCaseIndex?> LoadAsync(LacDbContext db, Guid id, CancellationToken ct)
    {
        var number = await db.CourtCases.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.CaseNumber).SingleOrDefaultAsync(ct);
        if (number is null) return null;
        var orders = await db.CourtExternalOrderObservations.AsNoTracking()
            .Where(x => x.CourtCaseId == id).OrderBy(x => x.OrderDate).ThenBy(x => x.Id)
            .Select(x => new CourtIntelligenceKnownOrder(x.CourtCaseId, x.NormalizedCaseIdentity,
                x.OrderDate, x.OfficialUrl, x.CorrigendumUrl, x.UploadDate, x.Id)).Take(1001).ToListAsync(ct);
        if (orders.Count > 1000) throw new InvalidDataException("Known-order index exceeds safety limit.");
        return new(id, number, orders);
    }

    public static bool OfficialPdf(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "delhihighcourt.nic.in" && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && new[] { "/app/showlogo/", "/app/showFileJudgment/", "/app/case_number_pdf/", "/app/downloadOrderbByDate/" }
            .Any(prefix => uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal));

    public static string Identity(string value) => new(value.Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant).ToArray());

    public static bool Eligible(CourtIntelligenceCaseIndex index, CourtIntelligenceKnownOrder order) =>
        order.CourtCaseId == index.CaseId && order.OrderDate.HasValue && OfficialPdf(order.OfficialUrl)
        && order.NormalizedCaseIdentity.StartsWith("delhihighcourt|", StringComparison.Ordinal)
        && order.NormalizedCaseIdentity.Split('|').Length == 4
        && string.Concat(order.NormalizedCaseIdentity.Split('|').Skip(1)) == Identity(index.CaseNumber);

    public static async Task<JsonElement> ViewAsync(string extractionRoot, CourtIntelligenceCaseIndex index, CancellationToken ct)
    {
        var stored = await CourtIntelligenceArtifactReader.ReadAsync(extractionRoot, index.CaseId, ct);
        if (stored.HasValue) ValidateArtifact(stored.Value, index);
        var view = stored.HasValue ? JsonNode.Parse(stored.Value.GetRawText())!.AsObject() : new JsonObject
        {
            ["version"] = 1, ["caseId"] = index.CaseId.ToString(), ["caseNumber"] = index.CaseNumber,
            ["status"] = "Unprocessed", ["processingComplete"] = false,
            ["currentPosition"] = new JsonArray(), ["beforeNextHearing"] = new JsonArray(),
            ["latestOrder"] = null, ["orders"] = new JsonArray()
        };
        var orders = view["orders"]!.AsArray();
        foreach (var source in index.Orders.Where(x => Eligible(index, x)))
        {
            if (orders.Any(o => o!["officialUrl"]!.GetValue<string>() == source.OfficialUrl
                && o["orderDate"]!.GetValue<string>() == source.OrderDate!.Value.ToString("yyyy-MM-dd"))) continue;
            orders.Add(new JsonObject { ["officialUrl"] = source.OfficialUrl,
                ["orderDate"] = source.OrderDate!.Value.ToString("yyyy-MM-dd"), ["status"] = "Unprocessed",
                ["facts"] = new JsonArray(), ["sourceObservationId"] = source.SourceObservationId.ToString(),
                ["corrigendumUrl"] = source.CorrigendumUrl, ["uploadDate"] = source.UploadDate?.ToString("yyyy-MM-dd") });
        }
        var sorted = new JsonArray(orders.OrderBy(o => o!["orderDate"]!.GetValue<string>())
            .Select(o => o!.DeepClone()).ToArray());
        view["orders"] = sorted;
        var newest = sorted.LastOrDefault();
        if (newest?["status"]?.GetValue<string>() == "Unprocessed")
        {
            view["latestOrder"] = newest.DeepClone(); view["currentPosition"] = new JsonArray();
            view["finalOrder"] = null; view["caption"] = null;
        }
        var unprocessed = sorted.Count(o => o!["status"]!.GetValue<string>() == "Unprocessed");
        view["knownOrderCount"] = index.Orders.Select(o => (o.OrderDate, o.OfficialUrl)).Distinct().Count();
        view["unprocessedOrderCount"] = unprocessed;
        view["unusableKnownOrderCount"] = index.Orders.Where(x => !Eligible(index, x))
            .Select(o => (o.OrderDate, o.OfficialUrl)).Distinct().Count();
        view["processingComplete"] = sorted.Count > 0 && unprocessed == 0;
        view["sourceCoverage"] = new JsonObject { ["basis"] = "Registered case official order index",
            ["knownSources"] = sorted.Count, ["checkedSources"] = sorted.Count(o => o!["status"]!.GetValue<string>() == "Validated"),
            ["gaps"] = new JsonArray(sorted.Where(o => o!["status"]!.GetValue<string>() != "Validated" || o["refreshFailure"] is not null)
                .Select(o => (JsonNode)new JsonObject { ["orderDate"] = o!["orderDate"]!.DeepClone(),
                    ["officialUrl"] = o["officialUrl"]!.DeepClone(), ["reason"] = o["refreshFailure"]?.GetValue<string>() ?? o["status"]!.GetValue<string>() }).ToArray()) };
        var statePath = Path.Combine(extractionRoot, "court-intelligence", "v1", index.CaseId.ToString(), "refresh.json");
        if (File.Exists(statePath))
        {
            var info = new FileInfo(statePath);
            if (info.Length > 8192) throw new InvalidDataException("Refresh state exceeds safety limit.");
            var state = JsonNode.Parse(await File.ReadAllTextAsync(statePath, ct)) as JsonObject
                ?? throw new InvalidDataException("Invalid refresh state.");
            if (state["caseId"]?.GetValue<string>() != index.CaseId.ToString()) throw new InvalidDataException("Refresh case mismatch.");
            if (!new[] { "Running", "Completed", "CompletedWithReview", "Failed", "Interrupted" }.Contains(state["status"]?.GetValue<string>()))
                throw new InvalidDataException("Invalid refresh status.");
            foreach (var key in new[] { "checked", "total", "needsReview" })
                if (state[key] is not null && state[key]!.GetValue<int>() is < 0 or > 1000) throw new InvalidDataException("Invalid refresh counter.");
            view["refreshState"] = state;
        }
        return JsonSerializer.SerializeToElement(view);
    }

    public static void ValidateArtifact(JsonElement artifact, CourtIntelligenceCaseIndex index)
    {
        if (artifact.GetProperty("caseNumber").ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(artifact.GetProperty("caseNumber").GetString())
            || artifact.GetProperty("caseId").GetGuid() != index.CaseId
            || Identity(artifact.GetProperty("caseNumber").GetString()!) != Identity(index.CaseNumber))
            throw new InvalidDataException("Court intelligence case identity mismatch.");
        foreach (var key in new[] { "orders", "currentPosition", "beforeNextHearing" })
            if (artifact.GetProperty(key).ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid intelligence shape.");
        foreach (var key in new[] { "currentPosition", "beforeNextHearing", "factualChronology", "lacCaptionAppearances" })
            if (artifact.TryGetProperty(key, out var items)) foreach (var item in items.EnumerateArray())
            {
                if (item.GetProperty("text").ValueKind != JsonValueKind.String
                    || item.GetProperty("source").ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid display item.");
            }
        foreach (var order in artifact.GetProperty("orders").EnumerateArray())
        {
            ValidateOrderSource(order, index);
            if (order.GetProperty("facts").ValueKind != JsonValueKind.Array
                || order.GetProperty("status").ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid order shape.");
            foreach (var key in new[] { "facts", "summaryFacts" })
                if (order.TryGetProperty(key, out var facts)) foreach (var fact in facts.EnumerateArray())
                {
                    ValidatePassage(fact);
                    foreach (var field in new[] { "value", "category", "scope", "field" })
                        if (fact.GetProperty(field).ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid fact shape.");
                }
            foreach (var key in new[] { "digest", "propositions" })
                if (order.TryGetProperty(key, out var entries)) foreach (var item in entries.EnumerateArray())
                    if (item.GetProperty("text").ValueKind != JsonValueKind.String
                        || item.GetProperty("source").ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid order digest.");
        }
        foreach (var key in new[] { "latestOrder", "finalOrder", "latestMeaningfulOrder" })
            if (artifact.TryGetProperty(key, out var displayed) && displayed.ValueKind != JsonValueKind.Null
                && !artifact.GetProperty("orders").EnumerateArray().Any(order =>
                    JsonNode.DeepEquals(JsonNode.Parse(order.GetRawText()), JsonNode.Parse(displayed.GetRawText()))))
                throw new InvalidDataException("Displayed order is not a registered artifact order.");
        WalkSources(artifact, index);
    }

    private static void ValidateOrderSource(JsonElement source, CourtIntelligenceCaseIndex index)
    {
        var url = source.GetProperty("officialUrl").GetString();
        var day = source.GetProperty("orderDate").GetString();
        if (!index.Orders.Any(o => Eligible(index, o) && o.OfficialUrl == url && o.OrderDate!.Value.ToString("yyyy-MM-dd") == day))
            throw new InvalidDataException("Evidence is not in this registered case's known-order index.");
    }
    private static void ValidatePassage(JsonElement source)
    {
        if (!source.GetProperty("page").TryGetInt32(out var page) || page is < 1 or > 200
            || source.GetProperty("evidence").ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(source.GetProperty("evidence").GetString()))
            throw new InvalidDataException("Invalid source/page evidence.");
        if (source.TryGetProperty("evidenceParts", out var parts))
        {
            if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
                throw new InvalidDataException("Invalid evidence fragments.");
            foreach (var part in parts.EnumerateArray()) ValidatePassage(part);
        }
    }
    private static void WalkSources(JsonElement node, CourtIntelligenceCaseIndex index)
    {
        if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) WalkSources(child, index);
        else if (node.ValueKind == JsonValueKind.Object) foreach (var property in node.EnumerateObject())
        {
            if (property.Name == "text" && property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid displayed proposition.");
            if (property.Name == "source" && property.Value.ValueKind != JsonValueKind.Null)
            {
                if (node.GetProperty("text").ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid displayed text.");
                ValidateOrderSource(property.Value, index); ValidatePassage(property.Value);
            }
            if (property.Name == "resolutionSource" && property.Value.ValueKind != JsonValueKind.Null)
            {
                ValidateOrderSource(property.Value, index); ValidatePassage(property.Value);
            }
            WalkSources(property.Value, index);
        }
    }

    public static void ValidateAnswer(JsonElement result, Guid caseId, JsonElement artifact)
    {
        if (result.GetProperty("caseId").GetGuid() != caseId) throw new InvalidDataException("Question case mismatch.");
        foreach (var claim in result.GetProperty("claims").EnumerateArray())
        {
            var source = claim.GetProperty("source"); ValidatePassage(source);
            var matched = artifact.GetProperty("orders").EnumerateArray().Any(order =>
                order.GetProperty("officialUrl").GetString() == source.GetProperty("officialUrl").GetString()
                && order.GetProperty("orderDate").GetString() == source.GetProperty("orderDate").GetString()
                && (order.TryGetProperty("summaryFacts", out var summary) ? summary :
                    order.GetProperty("status").GetString() == "Validated" ? order.GetProperty("facts") : JsonSerializer.SerializeToElement(Array.Empty<object>()))
                .EnumerateArray().Any(fact => fact.GetProperty("page").GetInt32() == source.GetProperty("page").GetInt32()
                    && fact.GetProperty("evidence").GetString() == source.GetProperty("evidence").GetString()
                    && fact.GetProperty("value").GetString() == claim.GetProperty("text").GetString()
                    && ((!source.TryGetProperty("evidenceParts", out var parts) && !fact.TryGetProperty("evidenceParts", out _))
                        || source.TryGetProperty("evidenceParts", out parts) && fact.TryGetProperty("evidenceParts", out var factParts)
                        && JsonNode.DeepEquals(JsonNode.Parse(parts.GetRawText()), JsonNode.Parse(factParts.GetRawText())))
                    && ValidSubmissionLabel(fact, claim)));
            if (!matched) throw new InvalidDataException("Question citation is not verified evidence of this case/order/page.");
        }
    }

    private static bool ValidSubmissionLabel(JsonElement fact, JsonElement claim)
    {
        var expected = fact.GetProperty("category").GetString() switch
        {
            "PETITIONER_SUBMISSION" => "Petitioner submission (not an established Court fact)",
            "LAC_OR_RESPONDENT_SUBMISSION" => "LAC/respondent submission (not an established Court fact)",
            "OTHER_PARTY_SUBMISSION" => "Other party submission (not an established Court fact)",
            _ => null
        };
        if (expected is null) return true;
        if (fact.GetProperty("scope").GetString() is "Historical" or "Quoted") expected = "Historical/quoted · " + expected;
        return claim.GetProperty("attribution").GetString() == expected;
    }
}
