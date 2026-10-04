using System.Text.Json.Nodes;

namespace LAC.Api;

public sealed record CourtSourceDiagnostic(string? OrderDate, string? RawOrderDate, string? OfficialUrl,
    Guid SourceObservationId, string SourceState, string ReasonCode, string OfficerMessage,
    string AiState, int UsableFactCount, bool ReviewRequired, string SourceLabel, string AiLabel,
    object Technical);

public static class CourtSourceDiagnostics
{
    public static CourtSourceDiagnostic Evaluate(CourtIntelligenceCaseIndex index, CourtIntelligenceKnownOrder source, JsonNode? order)
    {
        var code = CourtIntelligenceCaseData.EligibilityReason(index, source);
        var status = order?["status"]?.GetValue<string>();
        var failure = order?["failureMessage"]?.GetValue<string>();
        var complete = order?["coverage"]?["allSelectedChunksProcessed"]?.GetValue<bool>() == true;
        var usable = (order?["summaryFacts"] as JsonArray)?.Count ??
            (status == "Validated" ? (order?["facts"] as JsonArray)?.Count ?? 0 : 0);
        var blocked = code != "Eligible";
        var ai = blocked ? "BlockedBeforeAI" : status is null or "Unprocessed" ? "Waiting" :
            status == "Processing" ? "Processing" : status == "Validated" ? "Processed" :
            complete && string.IsNullOrEmpty(failure) ? "ProcessedWithReview" : "Incomplete";
        if (!blocked && status == "NeedsSourceReview")
        {
            // Compatibility adapter for persisted pre-diagnostic artifacts.
            // Only known worker boundary failures receive these precise codes.
            code = order?["sourceReasonCode"]?.GetValue<string>() ?? (failure switch
            {
                var s when s?.StartsWith("NeedsSourceReview: connected-case PDF", StringComparison.Ordinal) == true => "ConnectedCasePdf",
                var s when s?.StartsWith("Known official source bytes changed", StringComparison.Ordinal) == true => "SourceBytesChanged",
                var s when s?.Contains("insufficient native text", StringComparison.Ordinal) == true => "InsufficientNativeText",
                var s when s?.Contains("caption/body boundary", StringComparison.Ordinal) == true => "PdfCaptionUnverified",
                _ => "SourceVerificationRequired"
            });
            blocked = true; ai = "BlockedBeforeAI"; usable = 0;
        }
        else if (!blocked && failure == "Exact requested case identity absent from Court caption")
        { code = "PdfAttributionMismatch"; blocked = true; ai = "BlockedBeforeAI"; usable = 0; }
        else if (!blocked && failure == "Source-confirmed order date required")
        { code = "PdfOrderDateMismatch"; blocked = true; ai = "BlockedBeforeAI"; usable = 0; }
        else if (!blocked && order?["deepProcessingComplete"]?.GetValue<bool>() == false && usable > 0) code = "FastBriefReady";
        else if (!blocked && order?["refreshFailure"] is not null) code = "RefreshFailed";
        else if (!blocked && ai == "Incomplete") code = "AiExtractionIncomplete";
        else if (!blocked && ai == "ProcessedWithReview") code = "AiProcessedWithReview";
        if (ai == "Incomplete") usable = 0;
        var sourceState = blocked ? "Blocked" : order?["sourceVerificationComplete"]?.GetValue<bool>() == true ? "Verified" :
            ai is "Waiting" or "Processing" or "Incomplete" ? "OfficialRowVerified" : "Verified";
        var sourceLabel = code switch
        {
            "ConnectedCasePdf" => "Connected-case PDF", "SourceBytesChanged" => "Source bytes changed",
            "MissingOrderDate" or "UnparseableOfficialDate" => "Order date needs verification",
            "CaseIdentityMismatch" => "Case identity mismatch", "PdfAttributionMismatch" => "PDF attribution mismatch",
            "PdfOrderDateMismatch" => "PDF order date mismatch", "UnsafeOfficialUrl" => "Unsafe official URL",
            "UnsupportedOfficialPdfRoute" => "Unsupported official PDF route", "InsufficientNativeText" => "Native PDF text unavailable",
            "PdfCaptionUnverified" => "PDF caption needs verification", "SourceVerificationRequired" => "Source verification required",
            "OfficialPdfDownloadTimeout" => "Official PDF download timed out", "OfficialPdfUnavailable" => "Official PDF unavailable",
            "OfficialPdfTooLarge" => "PDF exceeds source limit", "OfficialSourceNotPdf" => "Official link did not return a PDF",
            _ => sourceState == "Verified" ? "Verified source" : "Official row verified"
        };
        var message = code switch
        {
            "ConnectedCasePdf" => "The PDF names connected cases. Case-specific attribution is not verified; its facts are withheld.",
            "SourceBytesChanged" => "The official PDF bytes changed from the previously checked version. Its facts are withheld pending source-version review.",
            "MissingOrderDate" => "The official source has no order date. Verify its date before AI processing.",
            "UnparseableOfficialDate" => "The official date could not be parsed safely. Verify the displayed raw date before AI processing.",
            "CaseIdentityMismatch" => "The source identity does not exactly match this registered Delhi High Court case. Facts are withheld.",
            "PdfAttributionMismatch" => "The requested case is not confirmed in the PDF caption. Facts are withheld.",
            "PdfOrderDateMismatch" => "The PDF does not confirm the official row's order date. Facts are withheld.",
            "UnsafeOfficialUrl" => "The source URL does not meet the approved official DHC origin and URL safety checks.",
            "UnsupportedOfficialPdfRoute" => "This official URL uses a PDF route that has not been approved for retrieval.",
            "InsufficientNativeText" => "The PDF lacks sufficient native text. It remains withheld; OCR has not been used.",
            "PdfCaptionUnverified" => "The Court caption and body cannot be separated reliably. Facts are withheld.",
            "SourceVerificationRequired" => "The PDF has not passed source verification. Facts are withheld; inspect technical details.",
            "OfficialPdfDownloadTimeout" => "The official PDF download timed out before AI processing. Retry source retrieval; no facts from this source are used.",
            "OfficialPdfUnavailable" => "The official PDF could not be retrieved before AI processing. Retry source retrieval; no facts from this source are used.",
            "OfficialPdfTooLarge" => "The official PDF exceeds the bounded source size. Facts are withheld pending source review.",
            "OfficialSourceNotPdf" => "The official link did not return PDF content. Facts are withheld pending source review.",
            "AiExtractionIncomplete" => "AI extraction did not complete. Partial chunks contribute no usable facts.",
            "AiProcessedWithReview" => "AI extraction completed with coverage or attribution review. Only individually verified facts are usable.",
            "FastBriefReady" => "Verified latest-order evidence is ready. Background enrichment is still pending; this is not a complete order digest.",
            "RefreshFailed" => "The latest source check failed. Previously verified evidence is retained; review the source before relying on completeness.",
            _ => ai == "Waiting" ? "The official row is verified; AI processing has not started." : ai == "Processing" ? "AI processing is in progress." : "Source and evidence checks completed."
        };
        var aiLabel = ai switch { "BlockedBeforeAI" => "Facts withheld", "Waiting" => "Waiting for AI",
            "Processing" => "AI processing", "Incomplete" => "AI extraction incomplete", "ProcessedWithReview" => code == "FastBriefReady" ? "Fast brief ready" : "AI processed with review", _ => "AI processed" };
        return new(source.OrderDate?.ToString("yyyy-MM-dd"), source.RawOrderDate, source.OfficialUrl,
            source.SourceObservationId, sourceState, code, message, ai, usable,
            blocked || ai is "Incomplete" or "ProcessedWithReview" || code == "RefreshFailed", sourceLabel, aiLabel,
            new { source.NormalizedCaseIdentity, source.RawCaseNumber, source.SourceEvidenceSha256,
                source.SourceUrl, Evidence = source.RawEvidenceText is { } evidence ? evidence[..Math.Min(evidence.Length, 2000)] : null,
                WorkerFailure = failure });
    }
}
