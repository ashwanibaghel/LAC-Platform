namespace LAC.Infrastructure;

using System.Security.Claims;
using LAC.Domain;
using Microsoft.AspNetCore.Http;

public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var val = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public string? Username => User?.Identity?.Name ?? User?.FindFirstValue("username") ?? User?.FindFirstValue(ClaimTypes.Name);

    public string? DisplayName => User?.FindFirstValue("display_name") ?? Username;

    public Guid? DesignationId
    {
        get
        {
            var val = User?.FindFirstValue("designation_id");
            return Guid.TryParse(val, out var id) ? id : null;
        }
    }

    public string? DesignationCode => User?.FindFirstValue("designation_code");

    public string? DesignationName => User?.FindFirstValue("designation_name");

    public IReadOnlyList<string> Roles => User?.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList() ?? [];

    public IReadOnlyList<string> Permissions => User?.FindAll("permission").Select(c => c.Value).Distinct().ToList() ?? [];

    public IReadOnlyList<Guid> WorkstreamIds => User?.FindAll("workstream_id")
        .Select(c => Guid.TryParse(c.Value, out var id) ? (Guid?)id : null)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .ToList() ?? [];

    public IReadOnlyList<string> WorkstreamCodes => User?.FindAll("workstream_code").Select(c => c.Value).Distinct().ToList() ?? [];
}
