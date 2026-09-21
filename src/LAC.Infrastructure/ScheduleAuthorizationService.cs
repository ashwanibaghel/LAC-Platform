namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record ScheduleDeskOptionDto(
    Guid Id,
    string Code,
    string Name,
    Guid? WorkstreamId,
    IReadOnlyList<ScheduleDeskMemberOptionDto> Members
);

public sealed record ScheduleDeskMemberOptionDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Designation
);

public sealed record ScheduleOptionsDto(
    IReadOnlyList<WorkstreamDto> Workstreams,
    IReadOnlyList<ScheduleDeskOptionDto> Desks
);

public sealed record WorkstreamDto(
    Guid Id,
    string Code,
    string Name
);

public interface IScheduleAuthorizationService
{
    Task<bool> CanAccessScheduledEventAsync(
        Guid eventId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanAccessScheduledEventAsync(
        ScheduledEvent evt,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanCreateScheduledEventAsync(
        Guid targetWorkstreamId,
        Guid? targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<bool> CanAssignScheduledEventAsync(
        ScheduledEvent evt,
        Guid? targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<bool> CanUpdateScheduledEventAsync(
        ScheduledEvent evt,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanCompleteScheduledEventAsync(
        ScheduledEvent evt,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanCancelScheduledEventAsync(
        ScheduledEvent evt,
        Guid userId,
        CancellationToken ct = default);

    Task<(bool HasAll, HashSet<Guid> WorkstreamIds, HashSet<Guid> DeskIds)> GetAuthorizedScopesAsync(
        Guid userId,
        string permissionCode,
        CancellationToken ct = default);

    Task<ScheduleOptionsDto> GetCreateOptionsAsync(
        Guid userId,
        CancellationToken ct = default);
}

public sealed class ScheduleAuthorizationService(
    LacDbContext db,
    ICourtAuthorizationService? courtAuth = null) : IScheduleAuthorizationService
{
    public async Task<bool> CanAccessScheduledEventAsync(
        Guid eventId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var evt = await db.ScheduledEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.RecordStatus == RecordStatus.Active, ct);

        if (evt is null) return false;

        return await CanAccessScheduledEventAsync(evt, permissionCode, userId, ct);
    }

    public async Task<bool> CanAccessScheduledEventAsync(
        ScheduledEvent evt,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        if (permissionCode == PermissionCodes.ScheduleView)
        {
            var isCourtLinked = evt.Origin == ScheduledEventOrigin.CourtProceeding
                             || evt.CourtCaseId.HasValue
                             || evt.CourtProceedingId.HasValue;
            if (isCourtLinked && courtAuth != null && !await courtAuth.CanViewCourtReferencesAsync(userId, ct))
            {
                return false;
            }
        }

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == permissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var hasWorkstreamMembership = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == evt.WorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (hasWorkstreamMembership) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned) && evt.ResponsibleOfficeDeskId.HasValue)
        {
            var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.OfficeDeskId == evt.ResponsibleOfficeDeskId.Value
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

            if (isDeskMember) return true;
        }

        // Own scope fails closed for institutional ScheduledEvent authority
        return false;
    }

    public async Task<bool> CanCreateScheduledEventAsync(
        Guid targetWorkstreamId,
        Guid? targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive) return false;

        var isWsActive = await db.Workstreams.AsNoTracking()
            .AnyAsync(w => w.Id == targetWorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);

        if (!isWsActive) return false;

        if (targetDeskId.HasValue)
        {
            var isDeskActive = await db.OfficeDesks.AsNoTracking()
                .AnyAsync(d => d.Id == targetDeskId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct);

            if (!isDeskActive) return false;

            if (targetUserId.HasValue)
            {
                var isTargetUserDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == targetUserId.Value
                                && m.OfficeDeskId == targetDeskId.Value
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.User.IsActive
                                && m.User.RecordStatus == RecordStatus.Active, ct);

                if (!isTargetUserDeskMember) return false;
            }
        }
        else if (targetUserId.HasValue)
        {
            // Named handler requires a responsible desk
            return false;
        }

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == callerUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.ScheduleCreate
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isWsMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.WorkstreamId == targetWorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (isWsMember) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned) && targetDeskId.HasValue)
        {
            var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.OfficeDeskId == targetDeskId.Value
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

            if (isDeskMember) return true;
        }

        return false;
    }

    public async Task<bool> CanAssignScheduledEventAsync(
        ScheduledEvent evt,
        Guid? targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive) return false;

        if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
            return false;

        // Verify target desk & handler validity if provided
        if (targetDeskId.HasValue)
        {
            var isTargetDeskActive = await db.OfficeDesks.AsNoTracking()
                .AnyAsync(d => d.Id == targetDeskId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct);

            if (!isTargetDeskActive) return false;

            if (targetUserId.HasValue)
            {
                var isTargetUserMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == targetUserId.Value
                                && m.OfficeDeskId == targetDeskId.Value
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.User.IsActive
                                && m.User.RecordStatus == RecordStatus.Active, ct);

                if (!isTargetUserMember) return false;
            }
        }
        else if (targetUserId.HasValue)
        {
            return false;
        }

        // Reassignment authorization is based on current source responsibility
        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == callerUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.ScheduleAssign
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isWsMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.WorkstreamId == evt.WorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (isWsMember) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned) && evt.ResponsibleOfficeDeskId.HasValue)
        {
            var isSourceDeskMember = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.OfficeDeskId == evt.ResponsibleOfficeDeskId.Value
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

            if (isSourceDeskMember) return true;
        }

        return false;
    }

    public async Task<bool> CanUpdateScheduledEventAsync(ScheduledEvent evt, Guid userId, CancellationToken ct = default)
    {
        if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
            return false;

        return await CanAccessScheduledEventAsync(evt, PermissionCodes.ScheduleUpdate, userId, ct);
    }

    public async Task<bool> CanCompleteScheduledEventAsync(ScheduledEvent evt, Guid userId, CancellationToken ct = default)
    {
        if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
            return false;

        return await CanAccessScheduledEventAsync(evt, PermissionCodes.ScheduleComplete, userId, ct);
    }

    public async Task<bool> CanCancelScheduledEventAsync(ScheduledEvent evt, Guid userId, CancellationToken ct = default)
    {
        if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
            return false;

        return await CanAccessScheduledEventAsync(evt, PermissionCodes.ScheduleCancel, userId, ct);
    }

    public async Task<(bool HasAll, HashSet<Guid> WorkstreamIds, HashSet<Guid> DeskIds)> GetAuthorizedScopesAsync(
        Guid userId,
        string permissionCode,
        CancellationToken ct = default)
    {
        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == permissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Contains(ScopeMode.All))
        {
            return (true, [], []);
        }

        HashSet<Guid> workstreamIds = [];
        if (scopes.Contains(ScopeMode.Workstream))
        {
            var ws = await db.UserWorkstreamMemberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => m.WorkstreamId)
                .ToListAsync(ct);
            workstreamIds = new HashSet<Guid>(ws);
        }

        HashSet<Guid> deskIds = [];
        if (scopes.Contains(ScopeMode.Assigned))
        {
            var desks = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct);
            deskIds = new HashSet<Guid>(desks);
        }

        return (false, workstreamIds, deskIds);
    }

    public async Task<ScheduleOptionsDto> GetCreateOptionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var (hasAll, wsIds, deskIds) = await GetAuthorizedScopesAsync(userId, PermissionCodes.ScheduleCreate, ct);

        IQueryable<Workstream> wsQuery = db.Workstreams.AsNoTracking()
            .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active);

        if (!hasAll)
        {
            wsQuery = wsQuery.Where(w => wsIds.Contains(w.Id) || db.OfficeDesks.Any(d => deskIds.Contains(d.Id) && d.WorkstreamId == w.Id));
        }

        var workstreams = await wsQuery.OrderBy(w => w.Name)
            .Select(w => new WorkstreamDto(w.Id, w.Code, w.Name))
            .ToListAsync(ct);

        IQueryable<OfficeDesk> deskQuery = db.OfficeDesks.AsNoTracking()
            .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active);

        if (!hasAll)
        {
            deskQuery = deskQuery.Where(d => deskIds.Contains(d.Id) || (d.WorkstreamId.HasValue && wsIds.Contains(d.WorkstreamId.Value)));
        }

        var deskEntities = await deskQuery.OrderBy(d => d.Name).ToListAsync(ct);
        var activeDeskIds = deskEntities.Select(d => d.Id).ToList();

        var deskMemberships = await db.UserDeskMemberships.AsNoTracking()
            .Include(m => m.User).ThenInclude(u => u.Designation)
            .Where(m => activeDeskIds.Contains(m.OfficeDeskId)
                     && m.IsActive
                     && m.RemovedAt == null
                     && m.RecordStatus == RecordStatus.Active
                     && m.User.IsActive
                     && m.User.RecordStatus == RecordStatus.Active)
            .ToListAsync(ct);

        var membersByDesk = deskMemberships
            .GroupBy(m => m.OfficeDeskId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => new ScheduleDeskMemberOptionDto(
                        m.UserId,
                        m.User.Username,
                        m.User.DisplayName,
                        m.User.Designation?.Name
                    ))
                    .OrderBy(m => m.DisplayName)
                    .ToList()
            );

        var desks = deskEntities.Select(d => new ScheduleDeskOptionDto(
            d.Id,
            d.Code,
            d.Name,
            d.WorkstreamId,
            membersByDesk.TryGetValue(d.Id, out var members) ? members : new List<ScheduleDeskMemberOptionDto>()
        )).ToList();

        return new ScheduleOptionsDto(workstreams, desks);
    }
}
