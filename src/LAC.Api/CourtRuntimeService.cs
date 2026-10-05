using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LAC.Infrastructure;

namespace LAC.Api;

/// <summary>Loopback health and explicit, configured recovery; case work remains in refresh.json.</summary>
public sealed class CourtRuntimeService(IHttpClientFactory clients, IConfiguration config, LocalStoragePaths paths)
{
    private readonly object gate = new();
    private Process? recovery;
    public static readonly string[] States = ["Ready", "Starting", "Processing", "BusyWithOtherCase", "ModelOffline",
        "QuestionServiceOffline", "CaseNotReady", "SourceBlocked", "Failed"];
    private string RootFingerprint => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        Path.GetFullPath(paths.ExtractionRoot).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant())));

    public async Task<JsonObject> StatusAsync(CourtIntelligenceCaseIndex index, JsonElement view, CancellationToken ct)
    {
        var progress = view.GetProperty("progressSummary");
        var ready = progress.GetProperty("usableBriefs").GetInt32() > 0;
        var blocked = progress.GetProperty("blockedSources").GetInt32() > 0;
        var caseState = ready ? "Ready" : blocked ? "SourceBlocked" : "CaseNotReady";
        var local = await ProbeAsync("CourtCaseQuestions", "status", new {
            caseId = index.CaseId, caseNumber = index.CaseNumber, orderIndex = index.Orders.Select(o => new {
                o.CourtCaseId, o.NormalizedCaseIdentity, o.OrderDate, o.OfficialUrl, o.CorrigendumUrl,
                o.UploadDate, o.SourceObservationId, o.SourceEvidenceSha256, o.SourceKind }) }, ct);
        var result = local ?? new JsonObject { ["caseId"] = index.CaseId.ToString(),
            ["questionServiceState"] = "QuestionServiceOffline", ["modelState"] = await ModelStateAsync(ct),
            ["runtimeState"] = "QuestionServiceOffline", ["reasonCode"] = "QuestionServiceOffline" };
        if (local is not null && (local["caseId"]?.GetValue<string>() != index.CaseId.ToString()
            || local["extractionRootFingerprint"]?.GetValue<string>() != RootFingerprint
            || local["modelVersion"]?.GetValue<string>() != config["CourtRuntime:ModelVersion"]
            || local["questionPackageSha256"]?.GetValue<string>() != config["CourtRuntime:PackageSha256"]))
            result = new JsonObject { ["caseId"] = index.CaseId.ToString(), ["runtimeState"] = "Failed",
                ["reasonCode"] = "QuestionRuntimeMismatch", ["questionServiceState"] = "Failed" };
        if (!States.Contains(result["runtimeState"]?.GetValue<string>()))
            result["runtimeState"] = "Failed";
        result["caseState"] = caseState;
        if (result["runtimeState"]?.GetValue<string>() is "Ready" or "CaseNotReady" or "SourceBlocked")
        {
            result["runtimeState"] = caseState;
            if (caseState == "SourceBlocked") result["reasonCode"] = view.GetProperty("sourceDiagnostics").EnumerateArray()
                .First(d => d.GetProperty("aiState").GetString() == "BlockedBeforeAI").GetProperty("reasonCode").GetString();
        }
        result["checked"] ??= view.TryGetProperty("refreshState", out var refresh) && refresh.TryGetProperty("checked", out var checkedCount)
            ? checkedCount.GetInt32() : progress.GetProperty("processingChecked").GetInt32();
        result["total"] = progress.GetProperty("officialSources").GetInt32();
        result["usableBriefs"] = progress.GetProperty("usableBriefs").GetInt32();
        result["actionStatus"] = ready ? "VerifiedEvidenceAvailable" : "UnavailableUntilVerifiedIntelligenceReady";
        result["actionStatusMessage"] = ready ? "See verified current-action evidence."
            : "Action status unavailable until verified intelligence is ready.";
        lock (gate)
        {
            if (recovery is { HasExited: false })
            {
                result["runtimeState"] = "Starting"; result["reasonCode"] = "VerifyingAndStartingPinnedServices";
            }
        }
        var record = ReadRecovery();
        if (record is not null)
        {
            result["recovery"] = record;
            if (record["runtimeState"]?.GetValue<string>() == "Starting")
            { result["runtimeState"] = "Starting"; result["reasonCode"] = record["reasonCode"]!.DeepClone(); }
            else if (record["runtimeState"]?.GetValue<string>() == "Failed" && result["runtimeState"]?.GetValue<string>() is "QuestionServiceOffline" or "ModelOffline")
            { result["runtimeState"] = "Failed"; result["reasonCode"] = record["reasonCode"]!.DeepClone(); result["message"] = record["message"]?.DeepClone(); }
        }
        return result;
    }

    public static JsonElement Attach(JsonElement view, JsonObject runtime)
    {
        var node = JsonNode.Parse(view.GetRawText())!.AsObject();
        node["runtime"] = runtime;
        foreach (var key in new[] { "runtimeState", "reasonCode", "startedAt", "elapsedSeconds", "actionStatus", "actionStatusMessage" })
            if (runtime[key] is not null) node["progressSummary"]![key] = runtime[key]!.DeepClone();
        return JsonSerializer.SerializeToElement(node);
    }

    public IResult Recover()
    {
        lock (gate)
        {
            if (recovery is { HasExited: false }) return Results.Json(new { runtimeState = "Starting", reasonCode = "RecoveryAlreadyRunning" }, statusCode: 202);
            if (!config.GetValue<bool>("CourtRuntime:RecoveryEnabled"))
                return Results.Json(new { runtimeState = "Failed", reasonCode = "RecoveryNotConfigured",
                    error = "Verified local runtime recovery has not been configured." }, statusCode: 503);
            try
            {
                var python = RequiredPath("PythonExecutable");
                var script = RequiredPath("RecoveryScript");
                var manifest = RequiredPath("ManifestPath");
                var directory = RequiredPath("RuntimeDirectory", directory: true);
                var scriptPin = Pin("RecoveryScriptSha256");
                using var input = File.OpenRead(script);
                if (Convert.ToHexStringLower(SHA256.HashData(input)) != scriptPin) throw new InvalidDataException();
                var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(script)! };
                foreach (var value in new[] { script, "--manifest", manifest, "--manifest-sha256", Pin("ManifestSha256"),
                    "--package-sha256", Pin("PackageSha256"), "--extraction-root", paths.ExtractionRoot,
                    "--runtime-directory", directory }) start.ArgumentList.Add(value);
                recovery = Process.Start(start) ?? throw new InvalidOperationException();
                return Results.Json(new { runtimeState = "Starting", reasonCode = "VerifyingAndStartingPinnedServices" }, statusCode: 202);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { return Results.Json(new { runtimeState = "Failed", reasonCode = "RecoveryConfigurationInvalid" }, statusCode: 503); }
        }
    }

    private string RequiredPath(string key, bool directory = false)
    {
        var value = config["CourtRuntime:" + key];
        if (value is null || !Path.IsPathFullyQualified(value) || !(directory ? Directory.Exists(value) : File.Exists(value)))
            throw new InvalidDataException();
        return value;
    }
    private string Pin(string key)
    {
        var value = config["CourtRuntime:" + key];
        if (value is null || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-f0-9]{64}$")) throw new InvalidDataException();
        return value;
    }
    private JsonNode? ReadRecovery()
    {
        var directory = config["CourtRuntime:RuntimeDirectory"];
        if (directory is null) return null;
        try
        {
            var path = Path.Combine(directory, "recovery.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 8192) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var record = JsonNode.Parse(stream)!;
            if (record["runtimeState"]?.GetValue<string>() == "Starting")
            {
                var alive = false;
                try
                {
                    using var process = Process.GetProcessById(record["recoveryPid"]!.GetValue<int>());
                    alive = !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime()
                        - DateTimeOffset.Parse(record["startedAt"]!.GetValue<string>()).UtcDateTime).TotalSeconds) < 10;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NullReferenceException) { }
                if (!alive) { record["runtimeState"] = "Failed"; record["reasonCode"] = "RecoveryInterrupted"; }
            }
            // Only officer-safe lifecycle fields. PID, paths and logs remain local diagnostics.
            return new JsonObject(new[] { "runtimeState", "reasonCode", "message", "startedAt", "completedAt", "modelVersion", "manifestSha256" }
                .Where(k => record[k] is not null).Select(k => KeyValuePair.Create<string, JsonNode?>(k, record[k]!.DeepClone())));
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }
    private async Task<string> ModelStateAsync(CancellationToken ct)
    {
        var model = await ProbeAsync("CourtLocalModel", "health", null, ct);
        return model?["status"]?.GetValue<string>() == "ok" ? "Ready" : model?["error"] is not null ? "Starting" : "ModelOffline";
    }
    private async Task<JsonObject?> ProbeAsync(string clientName, string route, object? payload, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = clients.CreateClient(clientName);
            var bytes = payload is null ? null : JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
            if (bytes?.Length > 512 * 1024) return null;
            using var content = bytes is null ? null : new ByteArrayContent(bytes);
            if (content is not null) content.Headers.ContentType = new("application/json");
            using var response = payload is null ? await client.GetAsync(route, timeout.Token)
                : await client.PostAsync(route, content, timeout.Token);
            if (payload is not null && (int)response.StatusCode == 503) return null;
            if (response.Content.Headers.ContentLength > 8192) return null;
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var data = new byte[8193]; var count = 0; int n;
            while (count < data.Length && (n = await body.ReadAsync(data.AsMemory(count), timeout.Token)) > 0) count += n;
            if (count > 8192) return null;
            return JsonNode.Parse(data.AsSpan(0, count)) as JsonObject;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { return null; }
    }
}
