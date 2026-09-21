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
    Task<bool> CanEditCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanAssignCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageProceedingsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanManageDocumentsAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<bool> CanViewAwardWorkspaceAsync(Guid userId, CancellationToken ct = default);
    Task<IQueryable<CourtCase>> AuthorizeListQueryAsync(IQueryable<CourtCase> query, Guid userId, CancellationToken ct = default);
}

public sealed class CourtAuthorizationService(LacDbContext db) : ICourtAuthorizationService
{
    public async Task<bool> CanViewCourtReferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtView, ct);
        if (scopes.Count == 0)
        {
            scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardView, ct);
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

        if (scopes.Contains(ScopeMode.Assigned) || scopes.Contains(ScopeMode.Own))
        {
            var userDeskIds = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct);

            var hasAssignedCase = await db.CourtCases.AsNoTracking()
                .AnyAsync(c => c.RecordStatus == RecordStatus.Active && (c.AssignedUserId == userId || (c.ResponsibleOfficeDeskId.HasValue && userDeskIds.Contains(c.ResponsibleOfficeDeskId.Value))), ct);

            if (hasAssignedCase) return true;
        }

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
        if (scopes.Count == 0)
        {
            scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardEdit, ct);
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
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await GetUserScopesAsync(userId, PermissionCodes.CourtCreate, ct);
        if (scopes.Count == 0)
        {
            scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardEdit, ct);
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

        // Fallback for legacy permission codes if new Court.* not explicitly mapped
        if (scopes.Count == 0)
        {
            if (permissionCode == PermissionCodes.CourtView)
                scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardView, ct);
            else if (permissionCode == PermissionCodes.CourtEdit || permissionCode == PermissionCodes.CourtProceedingManage || permissionCode == PermissionCodes.CourtDocumentManage)
                scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardEdit, ct);
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

        if (scopes.Contains(ScopeMode.Assigned))
        {
            if (courtCase.AssignedUserId == userId) return true;

            if (courtCase.ResponsibleOfficeDeskId.HasValue)
            {
                var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId
                                && m.OfficeDeskId == courtCase.ResponsibleOfficeDeskId.Value
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

                if (isDeskMember) return true;
            }
        }

        if (scopes.Contains(ScopeMode.Own))
        {
            if (courtCase.AssignedUserId == userId) return true;
        }

        return false;
    }

    public async Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        var canView = await CanViewCourtCaseAsync(courtCaseId, userId, ct);
        if (!canView) return null;

        // Caller must also be authorized for the actual Award workspace
        var canViewAward = await CanViewAwardWorkspaceAsync(userId, ct);
        if (!canViewAward) return null;

        // Check if court case is linked to an Award
        var linkedAward = await db.Set<CourtCaseAward>().AsNoTracking()
            .Where(x => x.CourtCaseId == courtCaseId)
            .Select(x => (Guid?)x.AwardId)
            .FirstOrDefaultAsync(ct);

        if (linkedAward.HasValue)
        {
            return $"/awards/{linkedAward.Value}";
        }

        return null;
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
        {
            scopes = await GetUserScopesAsync(userId, PermissionCodes.AwardView, ct);
        }

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

        var deskIds = new List<Guid>();
        if (scopes.Contains(ScopeMode.Assigned))
        {
            deskIds = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.RemovedAt == null
                         && m.RecordStatus == RecordStatus.Active
                         && m.OfficeDesk.IsActive
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct);
        }

        var hasAssigned = scopes.Contains(ScopeMode.Assigned);
        var hasOwn = scopes.Contains(ScopeMode.Own);

        if (hasAssigned && hasOwn)
        {
            return query.Where(c => (c.ResponsibleOfficeDeskId.HasValue && deskIds.Contains(c.ResponsibleOfficeDeskId.Value))
                                 || c.AssignedUserId == userId);
        }
        else if (hasAssigned)
        {
            return query.Where(c => (c.ResponsibleOfficeDeskId.HasValue && deskIds.Contains(c.ResponsibleOfficeDeskId.Value))
                                 || c.AssignedUserId == userId);
        }
        else if (hasOwn)
        {
            return query.Where(c => c.AssignedUserId == userId);
        }

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
