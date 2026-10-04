using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LAC.Api;

public sealed record CourtQuestionSource(string OrderDate, string OfficialUrl);
public sealed record CourtQuestionTurn(string Question, IReadOnlyList<CourtQuestionSource> Sources);
public sealed record CourtQuestionContext(Guid CaseId, IReadOnlyList<CourtQuestionTurn> Turns);

/// <summary>Ephemeral referents, never evidence. Isolated by authenticated user,
/// login cookie and active-case generation; a case switch drops the old segment.</summary>
public sealed class CourtQuestionConversation(TimeProvider clock)
{
    private const int Capacity = 256;
    private readonly object gate = new();
    private readonly Dictionary<string, Segment> sessions = new();
    private sealed class Segment(Guid caseId, DateTimeOffset touched)
    {
        public Guid CaseId = caseId;
        public Guid Generation = Guid.NewGuid();
        public DateTimeOffset Touched = touched;
        public List<CourtQuestionTurn> Turns = [];
    }
    public sealed record Lease(string Key, Guid Generation, CourtQuestionContext Context);

    public Lease Enter(Guid userId, string? loginCookie, Guid caseId)
    {
        // Hash only for an in-memory partition key. Never persist/log a cookie.
        var key = userId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loginCookie ?? "")));
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in sessions.Where(x => now - x.Value.Touched > TimeSpan.FromMinutes(20)).Select(x => x.Key).ToArray())
                sessions.Remove(expired);
            if (!sessions.TryGetValue(key, out var segment) || segment.CaseId != caseId)
            {
                if (sessions.Count >= Capacity) sessions.Remove(sessions.MinBy(x => x.Value.Touched).Key);
                sessions[key] = segment = new(caseId, now);
            }
            segment.Touched = now;
            return new(key, segment.Generation, new(caseId, segment.Turns.ToArray()));
        }
    }

    public void Remember(Lease lease, string question, JsonElement validatedAnswer)
    {
        // Called ONLY after AskAsync's independent current artifact validation.
        // Assistant prose is deliberately absent from the stored/forwarded turn.
        if (!validatedAnswer.TryGetProperty("mode", out var mode) || mode.GetString() != "CourtGrounded"
            || !validatedAnswer.TryGetProperty("caseId", out var id) || id.GetGuid() != lease.Context.CaseId
            || !validatedAnswer.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Array)
            return;
        var sources = claims.EnumerateArray().Select(x => x.GetProperty("source"))
            .Select(x => new CourtQuestionSource(x.GetProperty("orderDate").GetString()!, x.GetProperty("officialUrl").GetString()!))
            .Distinct().Take(8).ToArray();
        // An unsupported last turn clears the referent; do not silently fall
        // back to an older answer's source as if it belonged to the last turn.
        lock (gate)
        {
            if (!sessions.TryGetValue(lease.Key, out var segment) || segment.Generation != lease.Generation) return;
            segment.Turns.Add(new(question, sources));
            if (segment.Turns.Count > 4) segment.Turns.RemoveAt(0);
            segment.Touched = clock.GetUtcNow();
        }
    }
}
