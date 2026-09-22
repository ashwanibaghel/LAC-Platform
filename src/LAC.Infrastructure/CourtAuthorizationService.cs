namespace LAC.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public interface ICourtAuthorizationService
{
    Task<bool> CanViewCourtReferencesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanEditCourtReferencesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanViewCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessCourtCaseAsync(Guid courtCaseId, string permissionCode, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessCourtCaseAsync(CourtCase courtCase, string permissionCode, Guid userId, CancellationToken ct = default);
    Task<bool> CanCreateCourtCaseAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanCreateCourtCaseAsync(Guid userId, Guid? initialDeskId, CancellationToken ct = default);
    Task<bool> CanEditCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanAssignCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageProceedingsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageDocumentsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanViewAwardWorkspaceAsync(Guid userId, CancellationToken ct = default);
    Task<IQueryable<CourtCase>> AuthorizeListQueryAsync(IQueryable<CourtCase> query, Guid userId, CancellationToken ct = default);
    Task<bool> HasCourtViewPermissionAsync(Guid userId, CancellationToken ct = default);
}

public sealed class CourtAuthorizationService(LacDbContext db) : ICourtAuthorizationService
{
    public async Task<bool> HasCourtViewPermissionAsync(Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtView, ct);
        return scopes.Count > 0;
    }

    public async Task<bool> CanViewCourtReferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtView, ct);
        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (isMember) return true;
        }

        // Assigned and Own scopes do NOT grant global workspace access.
        // Assigned scope only grants case-specific access via CanViewCourtCaseAsync.
        // Own scope fails closed.
        return false;
    }

    public async Task<bool> CanEditCourtReferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtEdit, ct);
        if (scopes.Count == 0)
        {
            scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtProceedingManage, ct);
        }

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (isMember) return true;
        }

        return false;
    }

    public async Task<bool> CanCreateCourtCaseAsync(Guid userId, CancellationToken ct = default)
    {
        return await CanCreateCourtCaseAsync(userId, (Guid?)null, ct);
    }

    public async Task<bool> CanCreateCourtCaseAsync(Guid userId, Guid? initialDeskId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtCreate, ct);
        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (isMember) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned))
        {
            if (initialDeskId.HasValue)
            {
                var isDeskMemberInCourtWs = await (
                    from m in db.UserDeskMemberships.AsNoTracking()
                    join d in db.OfficeDesks.AsNoTracking() on m.OfficeDeskId equals d.Id
                    join w in db.Workstreams.AsNoTracking() on d.WorkstreamId equals w.Id
                    where m.UserId == userId
                       && m.OfficeDeskId == initialDeskId.Value
                       && m.IsActive
                       && m.RemovedAt == null
                       && m.RecordStatus == RecordStatus.Active
                       && d.IsActive
                       && d.RecordStatus == RecordStatus.Active
                       && w.Code == WorkstreamCodes.CourtReferences
                    select m
                ).AnyAsync(ct);

                if (isDeskMemberInCourtWs) return true;
            }
            else
            {
                // When called without desk specified, check if user belongs to any active desk in CourtReferences
                var hasAnyCourtDesk = await (
                    from m in db.UserDeskMemberships.AsNoTracking()
                    join d in db.OfficeDesks.AsNoTracking() on m.OfficeDeskId equals d.Id
                    join w in db.Workstreams.AsNoTracking() on d.WorkstreamId equals w.Id
                    where m.UserId == userId
                       && m.IsActive
                       && m.RemovedAt == null
                       && m.RecordStatus == RecordStatus.Active
                       && d.IsActive
                       && d.RecordStatus == RecordStatus.Active
                       && w.Code == WorkstreamCodes.CourtReferences
                    select m
                ).AnyAsync(ct);

                if (hasAnyCourtDesk) return true;
            }
        }

        // Own scope fails closed
        return false;
    }

    public async Task<bool> CanViewCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        return await CanAccessCourtCaseAsync(courtCaseId, PermissionCodes.CourtView, userId, ct);
    }

    public async Task<bool> CanEditCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        return await CanAccessCourtCaseAsync(courtCaseId, PermissionCodes.CourtEdit, userId, ct);
    }

    public async Task<bool> CanAssignCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        return await CanAccessCourtCaseAsync(courtCaseId, PermissionCodes.CourtAssign, userId, ct);
    }

    public async Task<bool> CanManageProceedingsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        return await CanAccessCourtCaseAsync(courtCaseId, PermissionCodes.CourtProceedingManage, userId, ct);
    }

    public async Task<bool> CanManageDocumentsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        return await CanAccessCourtCaseAsync(courtCaseId, PermissionCodes.CourtDocumentManage, userId, ct);
    }

    public async Task<bool> CanAccessCourtCaseAsync(Guid courtCaseId, string permissionCode, Guid userId, CancellationToken ct = default)
    {
        var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtCaseId, ct);
        if (courtCase is null || courtCase.RecordStatus != RecordStatus.Active) return false;

        return await CanAccessCourtCaseAsync(courtCase, permissionCode, userId, ct);
    }

    public async Task<bool> CanAccessCourtCaseAsync(CourtCase courtCase, string permissionCode, Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, permissionCode, ct);
        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (isMember) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned))
        {
            if (courtCase.ResponsibleOfficeDeskId.HasValue)
            {
                var isDeskMember = await (
                    from m in db.UserDeskMemberships.AsNoTracking()
                    join d in db.OfficeDesks.AsNoTracking() on m.OfficeDeskId equals d.Id
                    where m.UserId == userId
                       && m.OfficeDeskId == courtCase.ResponsibleOfficeDeskId.Value
                       && m.IsActive
                       && m.RemovedAt == null
                       && m.RecordStatus == RecordStatus.Active
                       && d.IsActive
                       && d.RecordStatus == RecordStatus.Active
                    select m
                ).AnyAsync(ct);

                if (isDeskMember) return true;
            }

            // CRITICAL INVARIANT: Named handler AssignedUserId is routing metadata only and NEVER grants ACL access!
        }

        // Own scope fails closed
        if (scopes.Contains(ScopeMode.Own))
        {
            return false;
        }

        return false;
    }

    public async Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        var canView = await CanViewCourtCaseAsync(courtCaseId, userId, ct);
        if (!canView) return null;

        // Directly return court case workspace URL without requiring Award.View or redirecting to Award
        return $"/court-cases/{courtCaseId}";
    }

    public async Task<bool> CanViewAwardWorkspaceAsync(Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.AwardView
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isAwardMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.Award, ct);

            if (isAwardMember) return true;
        }

        return false;
    }

    public async Task<IQueryable<CourtCase>> AuthorizeListQueryAsync(IQueryable<CourtCase> query, Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive)
            return query.Where(_ => false);

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtView, ct);
        if (scopes.Count == 0)
            return query.Where(_ => false);

        if (scopes.Contains(ScopeMode.All))
            return query;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isWorkstreamMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (isWorkstreamMember)
                return query;
        }

        if (scopes.Contains(ScopeMode.Assigned))
        {
            var deskIds = await (
                from m in db.UserDeskMemberships.AsNoTracking()
                join d in db.OfficeDesks.AsNoTracking() on m.OfficeDeskId equals d.Id
                where m.UserId == userId
                   && m.IsActive
                   && m.RemovedAt == null
                   && m.RecordStatus == RecordStatus.Active
                   && d.IsActive
                   && d.RecordStatus == RecordStatus.Active
                select m.OfficeDeskId
            ).ToListAsync(ct);

            // Access strictly from current responsible-desk membership; AssignedUserId is NOT ACL
            return query.Where(c => c.ResponsibleOfficeDeskId.HasValue && deskIds.Contains(c.ResponsibleOfficeDeskId.Value));
        }

        // Own scope fails closed
        return query.Where(_ => false);
    }

    private async Task<List<ScopeMode>> GetUserScopesAsync(Guid userId, string permissionCode, CancellationToken ct)
    {
        return await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == permissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);
    }
}
