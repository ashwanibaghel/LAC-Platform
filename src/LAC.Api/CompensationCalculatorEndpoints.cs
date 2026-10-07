using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LAC.Domain.Calculators;

namespace LAC.Api;

public static class CompensationCalculatorEndpoints
{
    public const int MaxRequestBytes = 16384;
    private static readonly JsonSerializerOptions InputOptions = CreateInputOptions();
    private static JsonSerializerOptions CreateInputOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false
        };
        options.Converters.Add(new StrictCalculatorDecimalConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static void MapCompensationCalculatorEndpoints(this RouteGroupBuilder calculators)
    {
        calculators.MapPost("/compensation/compute", Compute)
            .RequireAuthorization()
            // The wildcard keeps unsupported media on this authenticated route instead
            // of letting the application's catch-all route turn the error into a 404.
            // Compute enforces JSON and returns 415 before reading the body.
            .Accepts<CompensationRequest>("application/json", "*/*")
            .Produces<CompensationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> Compute(HttpContext http, CompensationCalculator calculator, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!http.Request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (http.Request.ContentLength > MaxRequestBytes) return TooLarge();
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            int count;
            while ((count = await http.Request.Body.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + count > MaxRequestBytes) return TooLarge();
                buffer.Write(chunk, 0, count);
            }
            var request = JsonSerializer.Deserialize<CompensationRequest>(buffer.ToArray(), InputOptions)
                ?? throw new CalculatorValidationException("request", "A JSON calculation request is required.");
            return Results.Ok(calculator.Compute(request));
        }
        catch (CalculatorValidationException ex) { return Invalid(ex.Field, ex.Message); }
        catch (JsonException ex) { return Invalid(ex.Path?.TrimStart('$', '.') is { Length: > 0 } path ? path : "request", "Invalid JSON value, unknown/duplicate field, enum, date, or decimal. Use plain numbers with at most 12 fractional digits and magnitude at most 10^12."); }
        catch (BadHttpRequestException ex) when (ex.StatusCode is 400 or 413)
        { return ex.StatusCode == 413 ? TooLarge() : Invalid("request", "Invalid request body."); }
    }
    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult TooLarge() => Results.Problem(statusCode: 413, title: $"Calculator request must not exceed {MaxRequestBytes} bytes.");

    private sealed class StrictCalculatorDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray()) : Encoding.UTF8.GetString(reader.ValueSpan),
                _ => null
            };
            if (text is null || !CalculatorNumbers.TryParseInput(text, out var value)) throw new JsonException("Invalid bounded decimal.");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}
