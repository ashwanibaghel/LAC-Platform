using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed record EndpointPermission(string Code);

// One API-wide narrowing gate. Existing endpoint/service permission and custody checks still run.
public static class OperationalAuthorizationFilter
{
    public static async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var ct = http.RequestAborted;
        var db = http.RequestServices.GetRequiredService<LacDbContext>();
        var current = http.RequestServices.GetRequiredService<ICurrentUserContext>();
        if (!current.UserId.HasValue) return Results.Unauthorized();
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == current.UserId, ct);
        db.AuthorizationClock = http.RequestServices.GetRequiredService<TimeProvider>();
        if (user is null || !user.IsActive) return Results.Unauthorized();
        if (!await OfficeHierarchyFilter.AllowedAsync(ctx, db, user.Id, ct)) return Results.Forbid();
        if (!user.SupervisingOfficerId.HasValue) db.RequestOfficerUserId = user.Id;
        http.Items["rbac_actor_name"] = user.DisplayName;
        var path = http.Request.Path.Value!.ToLowerInvariant();
        if (path.StartsWith("/api/auth/") || path.StartsWith("/api/admin/") || path.StartsWith("/api/officers/") || path.StartsWith("/api/office/"))
            http.Response.Headers.CacheControl = "no-store";
        if (user.SupervisingOfficerId.HasValue)
        {
            var parent = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.SupervisingOfficerId
                && u.IsActive && u.RecordStatus == RecordStatus.Active && u.SupervisingOfficerId == null, ct);
            if (parent is null) return Results.Forbid();
            http.Items["rbac_supervisor_id"] = parent.Id;
            http.Items["rbac_supervisor_name"] = parent.DisplayName;
            var limits = await db.AssistantPermissionLimits.Where(x => x.UserId == user.Id && x.Permission.Category != "Administration")
                .Select(x => x.PermissionId).ToListAsync(ct);
            var parentPermissions = await db.UserRoles.Where(x => x.UserId == parent.Id && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
                .SelectMany(x => x.Role.RolePermissions).Select(x => x.PermissionId).Distinct().ToListAsync(ct);
            db.AssistantPermissionCeiling = limits.Intersect(parentPermissions).ToArray();
            if (path.StartsWith("/api/admin/") || path.StartsWith("/api/officers/")) return Results.Forbid();
        }
        if (path.StartsWith("/api/auth/")) return await next(ctx);
        if (http.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint
            && routeEndpoint.RoutePattern.Parameters.Any(p => p.IsCatchAll)) return await next(ctx);
        if (user.MustChangePassword) return Results.Json(new { message = "Replace the temporary credential before operational access." }, statusCode: 403);
        if (path.StartsWith("/api/admin/") || path.StartsWith("/api/officers/") || path.StartsWith("/api/office/")) return await next(ctx);
        var permissions = http.GetEndpoint()!.Metadata.GetOrderedMetadata<EndpointPermission>().Select(x => x.Code).Distinct().ToList();
        var hasExplicitPermission = permissions.Count > 0;
        var read = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
        if (permissions.Count == 0) permissions = InferWorkflowPermissions(path, read);
        var action = !read && !path.EndsWith("/export");
        if (action && hasExplicitPermission)
        {
            var held = await db.UserRoles.Where(x => x.UserId == user.Id && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
                .SelectMany(x => x.Role.RolePermissions).Select(x => x.Permission.Code).Distinct().ToListAsync(ct);
            if (permissions.Any(code => !held.Contains(code))) return Results.Forbid();
        }
        // Read-only role authority intentionally remains broader than action allocations.
        if (!action && !user.SupervisingOfficerId.HasValue) return await next(ctx);
        // Creation context contains only individually authorized workstreams, not Matter records.
        // The endpoint applies live permission and allocation ceilings for each returned option.
        if (read && path == "/api/matters/context") return await next(ctx);
        if (path == "/api/court-cases/imports" && action)
        {
            var court = http.RequestServices.GetRequiredService<ICourtAuthorizationService>();
            if (!await court.CanCreateCourtCaseAsync(user.Id, ct) || !await court.CanViewCourtReferencesAsync(user.Id, ct)) return Results.Forbid();
        }
        var resource = await OperationalResourceResolver.ResolveAsync(ctx, db, permissions, ct);
        if (!resource.Applicable)
        {
            // Ad-hoc task actions retain their existing workflow rules for officers. Human assistants
            // fail closed on unsupported context; AI conversation endpoints are not delegation APIs.
            return user.SupervisingOfficerId.HasValue ? Results.Forbid() : await next(ctx);
        }
        var allocations = http.RequestServices.GetRequiredService<WorkAllocationService>();
        if (!await allocations.CanWorkAsync(user.Id, resource.Kind, resource.WorkstreamId, resource.VillageIds, ct)) return Results.Forbid();
        foreach (var stream in resource.AdditionalWorkstreamIds ?? [])
            if (!await allocations.CanWorkAsync(user.Id, null, stream, resource.VillageIds, ct)) return Results.Forbid();
        if (user.SupervisingOfficerId.HasValue)
        {
            if (permissions.Count == 0) return Results.Forbid();
            var access = http.RequestServices.GetRequiredService<AccessControlService>();
            var context = new AccessResourceContext(WorkstreamId: resource.WorkstreamId, WorkstreamCode: resource.WorkstreamCode,
                AssignedDeskId: resource.DeskId);
            foreach (var permission in permissions)
                if (!await access.CanForUserAsync(user.Id, permission, context, ct)) return Results.Forbid();
            if (action && resource.DeskId.HasValue && !await db.UserDeskMemberships.AnyAsync(x => x.UserId == user.SupervisingOfficerId
                    && x.OfficeDeskId == resource.DeskId && x.IsActive && x.RemovedAt == null && x.RecordStatus == RecordStatus.Active
                    && x.OfficeDesk.IsActive && x.OfficeDesk.RecordStatus == RecordStatus.Active, ct)) return Results.Forbid();
        }
        return await next(ctx);
    }

    public static bool IsReadPermission(string code) => code.EndsWith(".View", StringComparison.Ordinal)
        || code is PermissionCodes.AuditView;

    private static List<string> InferWorkflowPermissions(string path, bool read)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var family = parts.ElementAtOrDefault(1);
        var create = parts.Length == 2;
        return family switch
        {
            "matters" or "matter-drafts" => path.Contains("draft")
                ? [PermissionCodes.MatterView, read ? PermissionCodes.DraftView : create || path.EndsWith("/drafts") ? PermissionCodes.DraftCreate : PermissionCodes.DraftEdit]
                : path.Contains("document") ? [PermissionCodes.MatterView, read && !path.EndsWith("/eligible-documents") ? PermissionCodes.MatterView : PermissionCodes.MatterDocumentManage]
                : [read ? PermissionCodes.MatterView : create ? PermissionCodes.MatterCreate : PermissionCodes.MatterEdit],
            "outward" => [read ? PermissionCodes.OutwardView : create ? PermissionCodes.OutwardCreate : path.EndsWith("/dispatch") ? PermissionCodes.OutwardDispatch : path.EndsWith("/cancel") ? PermissionCodes.OutwardCancel : PermissionCodes.OutwardEdit],
            "court-cases" => [read ? PermissionCodes.CourtView : path.Contains("document") ? PermissionCodes.CourtDocumentManage : path.Contains("proceeding") ? PermissionCodes.CourtProceedingManage : path.Contains("assign") ? PermissionCodes.CourtAssign : create || path.EndsWith("/imports") ? PermissionCodes.CourtCreate : PermissionCodes.CourtEdit],
            "work-items" => [read ? PermissionCodes.WorkItemView : create ? PermissionCodes.WorkItemCreate : path.Contains("reassign") || path.Contains("contributors") ? PermissionCodes.WorkItemAssign : path.Contains("review") || path.Contains("accept") || path.Contains("return") ? PermissionCodes.WorkItemReview : path.Contains("contribution") ? PermissionCodes.WorkItemContribute : path.Contains("complete") ? PermissionCodes.WorkItemComplete : path.Contains("cancel") ? PermissionCodes.WorkItemCancel : PermissionCodes.WorkItemUpdate],
            "scheduled-events" => [read ? PermissionCodes.ScheduleView : create ? PermissionCodes.ScheduleCreate : path.EndsWith("/reassign") ? PermissionCodes.ScheduleAssign : path.EndsWith("/complete") ? PermissionCodes.ScheduleComplete : path.EndsWith("/cancel") ? PermissionCodes.ScheduleCancel : PermissionCodes.ScheduleUpdate],
            "villages" when path.EndsWith("/matters") => [read ? PermissionCodes.MatterView : PermissionCodes.MatterCreate],
            _ => []
        };
    }
}

public sealed record OperationalResource(bool Applicable, OperationalWorkKind? Kind, Guid? WorkstreamId,
    string? WorkstreamCode, Guid? DeskId, IReadOnlyList<Guid> VillageIds, IReadOnlyList<Guid>? AdditionalWorkstreamIds = null);

public static class OperationalResourceResolver
{
    public static async Task<OperationalResource> ResolveAsync(EndpointFilterInvocationContext ctx, LacDbContext db,
        IReadOnlyList<string> permissions, CancellationToken ct)
    {
        var request = ctx.HttpContext.Request;
        var path = request.Path.Value!.ToLowerInvariant();
        var family = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
        OperationalWorkKind? kind = permissions.Any(x => x.StartsWith("LR.") || x.StartsWith("Khasra.")) ? OperationalWorkKind.LandRecords
            : permissions.Any(x => x.StartsWith("Award.")) ? OperationalWorkKind.Award : null;
        if (path.Contains("/nm-") || path.Contains("/nm/")) kind = OperationalWorkKind.Nm;
        if (path.Contains("possession")) kind = OperationalWorkKind.Possession;
        if (path.Contains("claims") || path.Contains("apportionment")) kind = OperationalWorkKind.Compensation;
        if (family.StartsWith("court") || family.StartsWith("dhc")) kind = OperationalWorkKind.Court;
        if (family is "notifications" or "projects") kind = OperationalWorkKind.LandAcquisition;
        var applicable = kind.HasValue || permissions.Any(x => x.StartsWith("Matter.") || x == PermissionCodes.VillageView)
            || family is "matters" or "matter-drafts" or "dak" or "outward" or "schedule" or "schedules" or "scheduled-events" or "work-items";
        var villages = new HashSet<Guid>();
        var awards = new HashSet<Guid>();
        var khasras = new HashSet<Guid>();
        var matters = new HashSet<Guid>();
        var notifications = new HashSet<Guid>();
        var workstreams = new HashSet<Guid>();
        Guid? workstreamId = null, deskId = null;
        void Collect(string name, string? value)
        {
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty) return;
            switch (name.ToLowerInvariant())
            {
                case "villageid": case "selectedvillageid": villages.Add(id); break;
                case "awardid": case "targetawardid": case "parentawardid": awards.Add(id); break;
                case "khasraid": case "khasraids": khasras.Add(id); break;
                case "matterid": matters.Add(id); break;
                case "notificationid": notifications.Add(id); break;
                case "workstreamid": case "targetworkstreamid": workstreamId = id; workstreams.Add(id); break;
                case "officedeskid": case "responsibleofficedeskid": deskId = id; break;
            }
        }
        void Walk(JsonElement element, string? name = null)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject()) Walk(property.Value, property.Name);
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var value in element.EnumerateArray()) Walk(value, name);
            else if (element.ValueKind == JsonValueKind.String && name is not null) Collect(name, element.GetString());
        }
        foreach (var route in request.RouteValues) Collect(route.Key, route.Value?.ToString());
        foreach (var query in request.Query) foreach (var value in query.Value) Collect(query.Key, value);
        // Minimal API binding has already produced typed DTOs; never re-read a JSON body or stream.
        foreach (var arg in ctx.Arguments.Where(a => a is not null && a is not HttpRequest && a is not HttpContext && a is not IFormFile
            && (a.GetType().Namespace is null || a.GetType().GetMethod("<Clone>$") is not null
                || a.GetType().Namespace?.StartsWith("LAC.") == true && a.GetType().Name.EndsWith("Request"))))
            Walk(JsonSerializer.SerializeToElement(arg));
        if (request.HasFormContentType)
        {
            try { foreach (var field in await request.ReadFormAsync(ct)) foreach (var value in field.Value) Collect(field.Key, value); }
            catch (InvalidDataException) { throw new AllocationException(400, "Invalid multipart request."); }
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Guid.TryParse(parts.ElementAtOrDefault(2), out var routeId);
        if (routeId != Guid.Empty)
        {
            switch (family)
            {
                case "villages": villages.Add(routeId); break;
                case "awards": awards.Add(routeId); break;
                case "khasras": khasras.Add(routeId); break;
                case "notifications": notifications.Add(routeId); break;
                case "village-lrs": villages.UnionWith(await db.VillageLRs.Where(x => x.Id == routeId).Select(x => x.VillageId).ToListAsync(ct)); break;
                case "lr-entries": villages.UnionWith(await db.LREntries.Where(x => x.Id == routeId).Select(x => x.VillageLR.VillageId).ToListAsync(ct)); break;
                case "khatauni": villages.UnionWith(await db.KhatauniRecords.Where(x => x.Id == routeId).Select(x => x.VillageId).ToListAsync(ct)); break;
                case "khatas": villages.UnionWith(await db.Khatas.Where(x => x.Id == routeId).Select(x => x.KhatauniRecord.VillageId).ToListAsync(ct)); break;
                case "shares": villages.UnionWith(await db.KhataPartyShares.Where(x => x.Id == routeId).Select(x => x.Khata.KhatauniRecord.VillageId).ToListAsync(ct)); break;
                case "matters": matters.Add(routeId); break;
                case "matter-drafts": matters.UnionWith(await db.MatterDrafts.Where(x => x.Id == routeId).Select(x => x.MatterId).ToListAsync(ct)); break;
                case "core-document-intakes": villages.UnionWith(await db.CoreDocumentIntakes.Where(x => x.Id == routeId).Select(x => x.VillageId).ToListAsync(ct)); break;
                case "nm-documents":
                    villages.UnionWith(await db.NmDocuments.Where(x => x.Id == routeId).Select(x => x.VillageId).ToListAsync(ct));
                    awards.UnionWith(await db.NmDocuments.Where(x => x.Id == routeId && x.AwardId.HasValue).Select(x => x.AwardId!.Value).ToListAsync(ct)); break;
                case "nm-review-rows":
                    villages.UnionWith(await db.NmReviewRows.Where(x => x.Id == routeId).Select(x => x.NmDocument.VillageId).ToListAsync(ct));
                    awards.UnionWith(await db.NmReviewRows.Where(x => x.Id == routeId && x.NmDocument.AwardId.HasValue).Select(x => x.NmDocument.AwardId!.Value).ToListAsync(ct)); break;
                case "nm-review-khasras": villages.UnionWith(await db.NmReviewKhasras.Where(x => x.Id == routeId).Select(x => x.NmReviewRow.NmDocument.VillageId).ToListAsync(ct)); break;
                case "nm-semantic-sessions": villages.UnionWith(await db.NmSemanticAnalysisSessions.Where(x => x.Id == routeId).Select(x => x.NmDocument.VillageId).ToListAsync(ct)); break;
                case "nm-semantic-owners": villages.UnionWith(await db.NmSemanticOwnerBlocks.Where(x => x.Id == routeId).Select(x => x.AnalysisSession.NmDocument.VillageId).ToListAsync(ct)); break;
                case "award-ingestion-candidates":
                    var candidate = await db.AwardIngestionCandidates.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    if (candidate?.Session.TargetAwardId is Guid candidateAward) awards.Add(candidateAward);
                    if (candidate?.Session.SelectedVillageId is Guid candidateVillage) villages.Add(candidateVillage);
                    if (candidate is not null) { try { Walk(JsonDocument.Parse(candidate.StructuredPayloadJson).RootElement); } catch (JsonException) { } }
                    break;
                case "award-ingestion-sessions":
                    var session = await db.AwardIngestionSessions.SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    if (session?.TargetAwardId is Guid sessionAward) awards.Add(sessionAward);
                    if (session?.SelectedVillageId is Guid sessionVillage) villages.Add(sessionVillage);
                    break;
                case "award-pdf-extractions":
                    var extraction = await db.AwardDocumentExtractionJobs.SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    if (extraction?.TargetAwardId is Guid extractionAward) awards.Add(extractionAward);
                    if (extraction?.SelectedVillageId is Guid extractionVillage) villages.Add(extractionVillage);
                    break;
                case "dak":
                    var dak = await db.Daks.Include(x => x.CurrentAssignment).SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    workstreamId = dak?.WorkstreamId; deskId = dak?.CurrentAssignment?.OfficeDeskId;
                    villages.UnionWith(await db.DakVillageLinks.Where(x => x.DakId == routeId && x.RecordStatus == RecordStatus.Active).Select(x => x.VillageId).ToListAsync(ct));
                    break;
                case "outward":
                    var outward = await db.Outwards.SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    workstreamId = outward?.WorkstreamId; deskId = outward?.IssuingDeskId;
                    if (outward?.MatterId is Guid outwardMatter) matters.Add(outwardMatter);
                    break;
                case "scheduled-events":
                    var scheduled = await db.ScheduledEvents.SingleOrDefaultAsync(x => x.Id == routeId, ct);
                    workstreamId = scheduled?.WorkstreamId; deskId = scheduled?.ResponsibleOfficeDeskId;
                    if (scheduled?.MatterId is Guid scheduledMatter) matters.Add(scheduledMatter);
                    break;
                case "court-cases":
                    deskId = await db.CourtCases.Where(x => x.Id == routeId).Select(x => x.ResponsibleOfficeDeskId).SingleOrDefaultAsync(ct);
                    awards.UnionWith(await db.Set<CourtCaseAward>().Where(x => x.CourtCaseId == routeId).Select(x => x.AwardId).ToListAsync(ct));
                    khasras.UnionWith(await db.Set<CourtCaseKhasra>().Where(x => x.CourtCaseId == routeId).Select(x => x.KhasraId).ToListAsync(ct));
                    break;
                case "work-items":
                    workstreamId = await db.WorkItems.Where(x => x.Id == routeId).Select(x => (Guid?)x.WorkstreamId).SingleOrDefaultAsync(ct);
                    matters.UnionWith(await db.WorkItemMatterLinks.Where(x => x.WorkItemId == routeId && x.RecordStatus == RecordStatus.Active).Select(x => x.MatterId).ToListAsync(ct));
                    deskId = await db.WorkItemAssignments.Where(x => x.WorkItemId == routeId && x.IsActive).Select(x => (Guid?)x.OfficeDeskId).FirstOrDefaultAsync(ct);
                    break;
            }
        }
        villages.UnionWith(await db.Khasras.Where(x => khasras.Contains(x.Id)).Select(x => x.VillageId).ToListAsync(ct));
        villages.UnionWith(await db.AwardVillages.Where(x => awards.Contains(x.AwardId)).Select(x => x.VillageId).ToListAsync(ct));
        villages.UnionWith(await db.Set<AwardKhasra>().Where(x => awards.Contains(x.AwardId)).Select(x => x.Khasra.VillageId).ToListAsync(ct));
        villages.UnionWith(await db.Set<NotificationKhasra>().Where(x => notifications.Contains(x.NotificationId)).Select(x => x.Khasra.VillageId).ToListAsync(ct));
        villages.UnionWith(await db.Matters.Where(x => matters.Contains(x.Id)).Select(x => x.VillageId).ToListAsync(ct));
        if (family is "matters" or "matter-drafts" || family == "villages" && path.EndsWith("/matters"))
        {
            workstreams.UnionWith(await db.Matters.Where(x => matters.Contains(x.Id) && x.WorkstreamId.HasValue).Select(x => x.WorkstreamId!.Value).ToListAsync(ct));
            if (matters.Count > 0 && !workstreamId.HasValue) workstreamId = await db.Matters.Where(x => matters.Contains(x.Id)).Select(x => x.WorkstreamId).FirstOrDefaultAsync(ct);
        }
        if (!kind.HasValue && !workstreamId.HasValue && family is "dak" or "outward") kind = OperationalWorkKind.Correspondence;
        var workstreamCode = workstreamId.HasValue ? await db.Workstreams.Where(x => x.Id == workstreamId).Select(x => x.Code).SingleOrDefaultAsync(ct)
            : kind switch { OperationalWorkKind.LandRecords => WorkstreamCodes.LandRecords, OperationalWorkKind.Award or OperationalWorkKind.Nm or OperationalWorkKind.Enm => WorkstreamCodes.Award,
                OperationalWorkKind.Court => WorkstreamCodes.CourtReferences, OperationalWorkKind.Possession => WorkstreamCodes.Possession,
                OperationalWorkKind.Accounts or OperationalWorkKind.Compensation or OperationalWorkKind.StatementA => WorkstreamCodes.AccountsCompensation,
                OperationalWorkKind.LandAcquisition => WorkstreamCodes.LandAcquisition, OperationalWorkKind.Correspondence => WorkstreamCodes.DakCorrespondence, _ => null };
        // One-off WorkItems are tasks, not new responsibility/permission categories.
        if (family == "work-items" && !ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>().OnBehalfOfUserId.HasValue) applicable = false;
        return new(applicable, kind, workstreamId, workstreamCode, deskId, villages.ToList(), workstreams.Where(x => x != workstreamId).ToList());
    }
}
