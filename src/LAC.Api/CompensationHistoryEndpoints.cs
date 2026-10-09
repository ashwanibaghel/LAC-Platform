using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using LAC.Domain.Calculators;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace LAC.Api;

public static class CompensationHistoryEndpoints
{
    public sealed record Submission(Guid IdempotencyKey, CompensationHistoryInputs Inputs);
    public sealed record Rename(string? Title);
    private static readonly JsonSerializerOptions Json = CompensationCalculatorEndpoints.InputOptions;
    public static void MapCompensationHistoryEndpoints(this RouteGroupBuilder calculators)
    {
        var routes=calculators.MapGroup("/compensation/history").RequireAuthorization();
        routes.AddEndpointFilter(async (context, next) => { context.HttpContext.Response.Headers.CacheControl="no-store"; return await next(context); });
        routes.MapPost("", Save);
        routes.MapGet("", async (HttpContext http, LacDbContext db, int? page, int? pageSize, string? search, CancellationToken ct) => {
            var owner=Owner(http); if (owner is null) return Results.Unauthorized();
            var p=page ?? 1; var size=pageSize ?? 20;
            if(p < 1 || p > 100000 || size < 1 || size > 100 || search?.Length > 200) return Invalid("page", "Use page >= 1, pageSize 1–100 and a search of at most 200 characters.");
            var query=db.CompensationHistory.AsNoTracking().Where(x=>x.OwnerUserId==owner);
            if(!string.IsNullOrWhiteSpace(search)) { var term=search.Trim().ToLower(); query=query.Where(x=>x.Title != null && x.Title.ToLower().Contains(term)); }
            var total=await query.CountAsync(ct);
            var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Skip((p-1)*size).Take(size).Select(x=>new {x.Id,x.Title,x.CreatedAt,x.OriginalAreaNotation,x.MarketRate,x.FinalAmount,x.CalculatorVersion,x.ConversionVersion,x.RequestJson}).ToListAsync(ct);
            var items=rows.Select(x=> { var request=JsonSerializer.Deserialize<CompensationRequest>(x.RequestJson,Json)!; return new {x.Id,x.Title,x.CreatedAt,x.OriginalAreaNotation,x.MarketRate,x.FinalAmount,x.CalculatorVersion,x.ConversionVersion,summary=$"Factor {request.MultiplicationFactor} · Solatium {request.Solatium?.Percent}% · {request.AdditionalAmount?.Type}"}; });
            return Results.Ok(new { items, total, page=p, pageSize=size });
        });
        routes.MapGet("/{id:guid}", async (Guid id, HttpContext http, LacDbContext db, CancellationToken ct) => {
            var row=await db.CompensationHistory.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.OwnerUserId==Owner(http),ct);
            return row is null ? Results.NotFound() : Results.Ok(Detail(row));
        });
        routes.MapPatch("/{id:guid}", async (Guid id, HttpContext http, LacDbContext db, CancellationToken ct) => {
            try {
                var edit=await Read<Rename>(http,ct);
                if(edit.Title?.Length > 200) return Invalid("title","Use at most 200 characters.");
                var row=await db.CompensationHistory.SingleOrDefaultAsync(x=>x.Id==id && x.OwnerUserId==Owner(http),ct);
                if(row is null) return Results.NotFound();
                row.Title=string.IsNullOrWhiteSpace(edit.Title) ? null : edit.Title.Trim();
                await db.SaveChangesAsync(ct); return Results.Ok(new { row.Id,row.Title,saved=true });
            } catch(JsonException) { return Invalid("request","Invalid JSON or unknown field."); }
            catch(BadHttpRequestException ex) { return Results.StatusCode(ex.StatusCode); }
            catch(DbUpdateException) { return NotSaved(); }
        });
    }
    private static Guid? Owner(HttpContext http) => Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static object Detail(CompensationHistory row) => new { row.Id,row.Title,row.CreatedAt,row.OriginalAreaNotation,row.CalculatorVersion,row.ConversionVersion, inputs=JsonSerializer.Deserialize<JsonElement>(row.InputsJson), request=JsonSerializer.Deserialize<JsonElement>(row.RequestJson), response=JsonSerializer.Deserialize<JsonElement>(row.ResponseJson),saved=true };
    private static async Task<T> Read<T>(HttpContext http,CancellationToken ct) {
        if(!http.Request.HasJsonContentType()) throw new BadHttpRequestException("Use application/json",415);
        using var stream=new MemoryStream(); var buffer=new byte[1024]; int count;
        while((count=await http.Request.Body.ReadAsync(buffer,ct)) != 0) { if(stream.Length+count > CompensationCalculatorEndpoints.MaxRequestBytes) throw new BadHttpRequestException("Request too large",413); stream.Write(buffer,0,count); }
        return JsonSerializer.Deserialize<T>(stream.ToArray(),Json) ?? throw new JsonException();
    }
    private static async Task<IResult> Save(HttpContext http,LacDbContext db,CompensationCalculator calculator,CancellationToken ct) {
        var owner=Owner(http); if(owner is null) return Results.Unauthorized();
        try {
            var input=await Read<Submission>(http,ct);
            if(input.IdempotencyKey==Guid.Empty || input.Inputs is null) return Invalid("request","A non-empty idempotencyKey and original inputs are required.");
            var inputsJson=JsonSerializer.Serialize(input.Inputs,Json);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputsJson))).ToLowerInvariant();
            var prior=await db.CompensationHistory.AsNoTracking().SingleOrDefaultAsync(x=>x.OwnerUserId==owner && x.IdempotencyKey==input.IdempotencyKey,ct);
            if(prior is not null) return prior.SubmissionHash==hash ? Results.Ok(Detail(prior)) : Results.Conflict(new {saved=false,message="Idempotency key was already used for different inputs."});
            var request=input.Inputs.Normalize(); var response=calculator.Compute(request);
            var row=new CompensationHistory {OwnerUserId=owner.Value,CreatedAt=DateTimeOffset.UtcNow,IdempotencyKey=input.IdempotencyKey,SubmissionHash=hash,InputsJson=inputsJson,RequestJson=JsonSerializer.Serialize(request,Json),ResponseJson=JsonSerializer.Serialize(response,Json),CalculatorVersion="compensation-v1",ConversionVersion=request.ConversionProfile,OriginalAreaNotation=$"{input.Inputs.LandArea.Trim()} {input.Inputs.LandAreaUnit}",MarketRate=$"{response.Rate} / {response.Area.RateUnit}",FinalAmount=response.FinalCompensation.Precise};
            db.CompensationHistory.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch(DbUpdateException ex) when(ex.InnerException is PostgresException {SqlState: PostgresErrorCodes.UniqueViolation}) {
                db.ChangeTracker.Clear();
                prior=await db.CompensationHistory.AsNoTracking().SingleAsync(x=>x.OwnerUserId==owner && x.IdempotencyKey==input.IdempotencyKey,ct);
                return prior.SubmissionHash==hash ? Results.Ok(Detail(prior)) : Results.Conflict(new {saved=false,message="Idempotency key was already used for different inputs."});
            }
            await db.Entry(row).ReloadAsync(ct); // Read the committed PostgreSQL JSON/timestamp representation.
            return Results.Ok(Detail(row)); // SaveChanges' transaction has committed before Saved is returned.
        } catch(CalculatorValidationException ex) { return Invalid(ex.Field,ex.Message); }
        catch(JsonException) { return Invalid("request","Invalid JSON, duplicate or unknown field."); }
        catch(BadHttpRequestException ex) { return Results.StatusCode(ex.StatusCode); }
        catch(DbUpdateException) { return NotSaved(); }
        catch(NpgsqlException) { return NotSaved(); }
    }
    private static IResult NotSaved() => Results.Json(new {saved=false,message="Calculation was not confirmed saved. Retry the same submission; its idempotency key prevents duplicates."},statusCode:503);
    private static IResult Invalid(string field,string message) => Results.ValidationProblem(new Dictionary<string,string[]> {[field]=[message]});
}
