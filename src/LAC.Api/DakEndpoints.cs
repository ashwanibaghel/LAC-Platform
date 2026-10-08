namespace LAC.Api;

using System.IO;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public static partial class DakEndpoints
{
    public static RouteGroupBuilder MapDakEndpoints(this RouteGroupBuilder api)
    {
        var dak = api.MapGroup("/dak");
        dak.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
        });

        MapCustodyEndpoints(dak);

        // 1. Register Inward Dak
        dak.MapPost("/", async (
            HttpRequest request,
            LacDbContext db,
            DakWorkflowService workflow,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanRegisterDakAsync(userId, ct))
                return Results.Forbid();

            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "Request must be multipart/form-data." });

            var form = await request.ReadFormAsync(ct);

            var diaryNumber = form["diaryNumber"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(diaryNumber))
                return Results.BadRequest(new { message = "Diary Number is mandatory." });
            if (diaryNumber.Length > 100)
                return Results.BadRequest(new { message = "Diary Number cannot exceed 100 characters." });

            var subject = form["subject"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(subject))
                return Results.BadRequest(new { message = "Subject is mandatory." });
            if (subject.Length > 500)
                return Results.BadRequest(new { message = "Subject cannot exceed 500 characters." });

            var senderName = form["senderName"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(senderName))
                return Results.BadRequest(new { message = "Sender Name is mandatory." });
            if (senderName.Length > 200)
                return Results.BadRequest(new { message = "Sender Name cannot exceed 200 characters." });

            var receivedDateStr = form["receivedDate"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(receivedDateStr) || !DateOnly.TryParse(receivedDateStr, out var receivedDate))
            {
                return Results.BadRequest(new { message = "Received Date is required and must be a valid date (YYYY-MM-DD)." });
            }

            DateOnly? senderLetterDate = null;
            var senderLetterDateStr = form["senderLetterDate"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(senderLetterDateStr))
            {
                if (!DateOnly.TryParse(senderLetterDateStr, out var sld))
                    return Results.BadRequest(new { message = "Sender Letter Date is invalid." });
                senderLetterDate = sld;
            }

            DateOnly? dueDate = null;
            var dueDateStr = form["dueDate"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(dueDateStr))
            {
                if (!DateOnly.TryParse(dueDateStr, out var dd))
                    return Results.BadRequest(new { message = "Due Date is invalid." });
                dueDate = dd;
            }

            Guid? categoryId = null;
            var categoryIdStr = form["categoryId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(categoryIdStr))
            {
                if (!Guid.TryParse(categoryIdStr, out var catId))
                    return Results.BadRequest(new { message = "Category ID is invalid." });
                categoryId = catId;
            }

            Guid? workstreamId = null;
            var workstreamIdStr = form["workstreamId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(workstreamIdStr))
            {
                if (!Guid.TryParse(workstreamIdStr, out var wsId))
                    return Results.BadRequest(new { message = "Workstream ID is invalid." });
                workstreamId = wsId;
            }

            DakPriority priority = DakPriority.Routine;
            var priorityStr = form["priority"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(priorityStr))
            {
                if (!Enum.TryParse<DakPriority>(priorityStr, true, out priority))
                    return Results.BadRequest(new { message = $"Priority '{priorityStr}' is invalid." });
            }

            var senderDesignation = form["senderDesignation"].ToString().Trim();
            var senderDepartment = form["senderDepartment"].ToString().Trim();
            var senderAddress = form["senderAddress"].ToString().Trim();
            var senderReferenceNumber = form["senderReferenceNumber"].ToString().Trim();
            var inwardMode = form["inwardMode"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(inwardMode)) inwardMode = "Physical";

            Guid? requestId = null;
            var requestKey = request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrEmpty(requestKey))
            {
                if (!Guid.TryParse(requestKey, out var parsedKey) || parsedKey == Guid.Empty)
                    return Results.BadRequest(new { message = "Idempotency-Key must be a non-empty UUID." });
                requestId = parsedKey;
            }
            var file = form.Files.GetFile("file");
            Stream? stream = null;
            string? fileName = null;
            string? contentType = null;

            if (file is not null && file.Length > 0)
            {
                stream = file.OpenReadStream();
                if (!TryValidateAndDeriveMime(stream, file.FileName, out var derivedMime, out var mimeError))
                {
                    await stream.DisposeAsync();
                    return Results.BadRequest(new { message = mimeError });
                }
                fileName = file.FileName;
                contentType = derivedMime;
            }

            var cmd = new RegisterDakCommand(
                DiaryNumber: diaryNumber,
                ReceivedDate: receivedDate,
                Subject: subject,
                SenderName: senderName,
                SenderDesignation: string.IsNullOrWhiteSpace(senderDesignation) ? null : senderDesignation,
                SenderDepartment: string.IsNullOrWhiteSpace(senderDepartment) ? null : senderDepartment,
                SenderAddress: string.IsNullOrWhiteSpace(senderAddress) ? null : senderAddress,
                SenderReferenceNumber: string.IsNullOrWhiteSpace(senderReferenceNumber) ? null : senderReferenceNumber,
                SenderLetterDate: senderLetterDate,
                InwardMode: inwardMode,
                Priority: priority,
                DueDate: dueDate,
                CategoryId: categoryId,
                WorkstreamId: workstreamId,
                DocumentStream: stream,
                DocumentFileName: fileName,
                DocumentContentType: contentType,
                RequestId: requestId
            );

            try
            {
                var created = await workflow.RegisterAsync(cmd, userId, ct);
                return Results.Created($"/api/dak/{created.Id}", new { id = created.Id, diaryNumber = created.DiaryNumber, revision = created.Revision, status = created.Status.ToString() });
            }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
            finally
            {
                if (stream is not null) await stream.DisposeAsync();
            }
        }).RequirePermission(PermissionCodes.DakRegister);

        // 1b. Operational Lookup: Registration Categories & Workstreams
        dak.MapGet("/lookups/registration", async (
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanRegisterDakAsync(userId, ct))
                return Results.Forbid();

            var categories = await db.DakCategories.AsNoTracking()
                .Where(c => c.IsActive && c.RecordStatus == RecordStatus.Active)
                .OrderBy(c => c.Name)
                .Select(c => new DakCategoryDto(c.Id, c.Code, c.Name, c.Description, c.DefaultPriority.ToString(), c.DefaultWorkstreamId, c.DefaultWorkstream == null ? null : c.DefaultWorkstream.Name, c.IsActive))
                .ToListAsync(ct);

            var workstreams = await db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                .OrderBy(w => w.Name)
                .Select(w => new { id = w.Id, code = w.Code, name = w.Name, isActive = w.IsActive })
                .ToListAsync(ct);

            return Results.Ok(new { categories, workstreams });
        }).RequirePermission(PermissionCodes.DakRegister);

        // 1c. Operational Lookup: Active Canonical Directory Filters
        dak.MapGet("/lookups/directory", async (
            LacDbContext db,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            var desks = await db.OfficeDesks.AsNoTracking()
                .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
                .OrderBy(d => d.Name)
                .Select(d => new { id = d.Id, code = d.Code, name = d.Name, isActive = d.IsActive })
                .ToListAsync(ct);

            var handlers = await db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive && u.RecordStatus == RecordStatus.Active
                    && db.UserDeskMemberships.Any(m => m.UserId == u.Id && m.IsActive && m.RemovedAt == null
                        && m.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                    && db.UserRoles.Any(r => r.UserId == u.Id && r.Role.IsActive && r.Role.RecordStatus == RecordStatus.Active
                        && r.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.DakReceive
                            && (p.ScopeMode == ScopeMode.All || p.ScopeMode == ScopeMode.Assigned || p.ScopeMode == ScopeMode.Workstream))))
                .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
                .Select(u => new { id = u.Id, displayName = u.DisplayName }).ToListAsync(ct);
            var categories = await db.DakCategories.AsNoTracking()
                .Where(c => c.IsActive && c.RecordStatus == RecordStatus.Active).OrderBy(c => c.Name)
                .Select(c => new { id = c.Id, code = c.Code, name = c.Name }).ToListAsync(ct);
            var workstreams = await db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active).OrderBy(w => w.Name)
                .Select(w => new { id = w.Id, code = w.Code, name = w.Name }).ToListAsync(ct);

            return Results.Ok(new { desks, handlers, categories, workstreams });
        }).RequirePermission(PermissionCodes.DakView);

        // 2. Collection Query with Union-of-Scopes Filtering
        dak.MapGet("/", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            string? q,
            string? status,
            string? priority,
            Guid? deskId,
            DateOnly? receivedFrom,
            DateOnly? receivedTo,
            string? inwardMode,
            Guid? categoryId,
            Guid? workstreamId,
            Guid? handlerId,
            string? sender,
            bool? hasDocument,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var baseQuery = db.Daks.AsNoTracking()
                .Include(d => d.Category)
                .Include(d => d.Workstream)
                .Include(d => d.CurrentAssignment)
                    .ThenInclude(a => a!.OfficeDesk)
                .Include(d => d.CurrentAssignment)
                    .ThenInclude(a => a!.AssignedUser)
                .OrderByDescending(d => d.ReceivedDate)
                .ThenByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id)
                .AsQueryable();

            // Authorize collection:
            var authResult = await dakAuth.AuthorizeListQueryAsync(baseQuery, PermissionCodes.DakView, userId, ct);
            if (!authResult.HasPermission)
                return Results.Forbid();

            var query = authResult.Query;
            if (includeArchived != true) query = query.Where(d => d.RecordStatus == RecordStatus.Active);
            if (receivedFrom.HasValue && receivedTo.HasValue && receivedFrom > receivedTo)
                return Results.BadRequest(new { message = "Received From must be on or before Received To." });
            query = new DakDirectoryFilters(receivedFrom, receivedTo, inwardMode, categoryId, workstreamId, handlerId, sender, hasDocument).Apply(query);

            // Apply search/filters
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(d => d.DiaryNumber.ToLower().Contains(term)
                                      || d.Subject.ToLower().Contains(term)
                                      || d.SenderName.ToLower().Contains(term)
                                      || (d.SenderReferenceNumber != null && d.SenderReferenceNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DakStatus>(status, true, out var st))
            {
                query = query.Where(d => d.Status == st);
            }

            if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<DakPriority>(priority, true, out var pr))
            {
                query = query.Where(d => d.Priority == pr);
            }

            if (deskId.HasValue)
            {
                query = query.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.OfficeDeskId == deskId.Value);
            }

            var p = Math.Max(0, page ?? 0);
            var ps = Math.Clamp(pageSize ?? 25, 1, 100);
            var totalCount = await query.CountAsync(ct);

            var items = await query.Skip(p * ps).Take(ps).Select(d => new DakListItemDto(
                d.Id,
                d.DiaryNumber,
                d.ReceivedDate,
                d.Subject,
                d.SenderName,
                d.SenderDepartment,
                d.InwardMode,
                d.Priority.ToString(),
                d.DueDate,
                d.Status.ToString(),
                d.Category == null ? null : d.Category.Name,
                d.Workstream == null ? null : d.Workstream.Name,
                d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.OfficeDesk != null ? d.CurrentAssignment.OfficeDesk.Name : null,
                d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.AssignedUser != null ? d.CurrentAssignment.AssignedUser.DisplayName : null,
                d.MainDocumentId != null || d.Attachments.Any(a => a.RecordStatus == RecordStatus.Active),
                d.Revision,
                d.CreatedAt,
                d.RecordStatus.ToString(), d.RoutingState.ToString(), d.PhysicalState.ToString()
            )).ToListAsync(ct);

            return Results.Ok(new { items, totalCount, page = p, pageSize = ps });
        }).RequirePermission(PermissionCodes.DakView);

        // 2b. My Desk Operational Queue
        dak.MapGet("/my-desk", async (
            int? page,
            int? pageSize,
            string? q,
            Guid? deskId,
            string? priority,
            string? due,
            string? handler,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            IOfficeClock officeClock,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            // Step A: Resolve live active desk memberships directly from DB
            var activeDesks = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.RemovedAt == null
                         && m.RecordStatus == RecordStatus.Active
                         && m.OfficeDesk.IsActive
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .OrderBy(m => m.OfficeDesk.Name)
                .Select(m => new MyDeskUserDeskDto(
                    m.OfficeDeskId,
                    m.OfficeDesk.Code,
                    m.OfficeDesk.Name,
                    m.IsPrimary
                ))
                .ToListAsync(ct);

            // Validate pagination parameters
            var p = Math.Max(0, page ?? 0);
            var ps = Math.Clamp(pageSize ?? 25, 1, 100);

            // Validate priority: strictly textual (all, routine, urgent, immediate)
            DakPriority? priorityFilter = null;
            var priorityStr = (priority ?? "all").Trim().ToLowerInvariant();
            if (priorityStr != "all")
            {
                if (priorityStr == "routine")
                    priorityFilter = DakPriority.Routine;
                else if (priorityStr == "urgent")
                    priorityFilter = DakPriority.Urgent;
                else if (priorityStr == "immediate")
                    priorityFilter = DakPriority.Immediate;
                else
                    return Results.BadRequest(new { message = "Invalid priority filter. Valid values are: all, routine, urgent, immediate." });
            }

            // Validate due
            var dueFilter = (due ?? "all").Trim().ToLowerInvariant();
            if (dueFilter != "all" && dueFilter != "overdue" && dueFilter != "today" && dueFilter != "upcoming" && dueFilter != "none")
            {
                return Results.BadRequest(new { message = "Invalid due filter. Valid values are: all, overdue, today, upcoming, none." });
            }

            // Validate handler
            var handlerFilter = (handler ?? "all").Trim().ToLowerInvariant();
            if (handlerFilter != "all" && handlerFilter != "me" && handlerFilter != "unallocated" && handlerFilter != "others")
            {
                return Results.BadRequest(new { message = "Invalid handler filter. Valid values are: all, me, unallocated, others." });
            }

            // Validate deskId: must belong to the caller's live active desk memberships
            if (deskId.HasValue)
            {
                if (!activeDesks.Any(d => d.Id == deskId.Value))
                {
                    return Results.BadRequest(new { message = "Invalid My Desk desk filter." });
                }
            }

            // If caller has no active desks, return empty response immediately
            if (activeDesks.Count == 0)
            {
                return Results.Ok(new MyDeskResponseDto(
                    new MyDeskSummaryDto(0, 0, 0, 0, 0, 0, 0),
                    activeDesks,
                    [],
                    0,
                    p,
                    ps
                ));
            }

            var activeDeskIds = activeDesks.Select(d => d.Id).ToList();

            // Step B: Build custody base query
            var custodyBaseQuery = db.Daks.AsNoTracking()
                .Where(d => d.RecordStatus == RecordStatus.Active
                         && d.CurrentAssignment != null
                         && d.CurrentAssignment.RecordStatus == RecordStatus.Active
                         && d.CurrentAssignment.IsActive
                         && d.Status != DakStatus.Resolved && d.RoutingState != DakRoutingState.InTransit
                         && activeDeskIds.Contains(d.CurrentAssignment.OfficeDeskId)
                         && d.CurrentAssignment.OfficeDesk.IsActive
                         && d.CurrentAssignment.OfficeDesk.RecordStatus == RecordStatus.Active);

            // Step C: Intersect with existing Dak.View authorization
            var authResult = await dakAuth.AuthorizeListQueryAsync(custodyBaseQuery, PermissionCodes.DakView, userId, ct);
            if (!authResult.HasPermission)
                return Results.Forbid();

            var authorizedQuery = authResult.Query;

            // Filter application order:
            // custody base -> Dak.View authorization -> requested desk filter -> search -> priority -> due -> handler
            var filteredQuery = authorizedQuery;

            if (deskId.HasValue)
            {
                filteredQuery = filteredQuery.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.OfficeDeskId == deskId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                filteredQuery = filteredQuery.Where(d => d.DiaryNumber.ToLower().Contains(term)
                                                      || d.Subject.ToLower().Contains(term)
                                                      || d.SenderName.ToLower().Contains(term)
                                                      || (d.SenderReferenceNumber != null && d.SenderReferenceNumber.ToLower().Contains(term)));
            }

            if (priorityFilter.HasValue)
            {
                filteredQuery = filteredQuery.Where(d => d.Priority == priorityFilter.Value);
            }

            var officeToday = officeClock.GetCurrentDate();
            if (dueFilter == "overdue")
            {
                filteredQuery = filteredQuery.Where(d => d.DueDate != null && d.DueDate.Value < officeToday);
            }
            else if (dueFilter == "today")
            {
                filteredQuery = filteredQuery.Where(d => d.DueDate != null && d.DueDate.Value == officeToday);
            }
            else if (dueFilter == "upcoming")
            {
                filteredQuery = filteredQuery.Where(d => d.DueDate != null && d.DueDate.Value > officeToday);
            }
            else if (dueFilter == "none")
            {
                filteredQuery = filteredQuery.Where(d => d.DueDate == null);
            }

            if (handlerFilter == "me")
            {
                filteredQuery = filteredQuery.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.AssignedUserId == userId);
            }
            else if (handlerFilter == "unallocated")
            {
                filteredQuery = filteredQuery.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.AssignedUserId == null);
            }
            else if (handlerFilter == "others")
            {
                filteredQuery = filteredQuery.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.AssignedUserId != null && d.CurrentAssignment.AssignedUserId != userId);
            }

            // Summary calculation and total count across filteredQuery BEFORE pagination
            var totalCount = await filteredQuery.CountAsync(ct);
            MyDeskSummaryDto summary;
            if (totalCount == 0)
            {
                summary = new MyDeskSummaryDto(0, 0, 0, 0, 0, 0, 0);
            }
            else
            {
                summary = await filteredQuery
                    .GroupBy(_ => 1)
                    .Select(g => new MyDeskSummaryDto(
                        g.Count(),
                        g.Count(d => d.Priority == DakPriority.Immediate),
                        g.Count(d => d.Priority == DakPriority.Urgent),
                        g.Count(d => d.DueDate != null && d.DueDate.Value < officeToday),
                        g.Count(d => d.DueDate != null && d.DueDate.Value == officeToday),
                        g.Count(d => d.CurrentAssignment != null && d.CurrentAssignment.AssignedUserId == userId),
                        g.Count(d => d.CurrentAssignment != null && d.CurrentAssignment.AssignedUserId == null)
                    ))
                    .FirstOrDefaultAsync(ct) ?? new MyDeskSummaryDto(totalCount, 0, 0, 0, 0, 0, 0);
            }

            // Sorting: CurrentAssignment.AssignedAt DESC, Dak.CreatedAt DESC, Dak.Id
            var items = await filteredQuery
                .OrderByDescending(d => d.CurrentAssignment!.AssignedAt)
                .ThenByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id)
                .Skip(p * ps)
                .Take(ps)
                .Select(d => new MyDeskItemDto(
                    d.Id,
                    d.DiaryNumber,
                    d.ReceivedDate,
                    d.Subject,
                    d.SenderName,
                    d.SenderDepartment,
                    d.Priority.ToString(),
                    d.DueDate,
                    d.Workstream != null ? d.Workstream.Name : null,
                    d.Status.ToString(),
                    d.Revision,
                    new MyDeskAssignmentDto(
                        d.CurrentAssignment!.OfficeDeskId,
                        d.CurrentAssignment.OfficeDesk!.Code,
                        d.CurrentAssignment.OfficeDesk.Name,
                        d.CurrentAssignment.AssignedUserId,
                        d.CurrentAssignment.AssignedUser != null ? d.CurrentAssignment.AssignedUser.DisplayName : null,
                        d.CurrentAssignment.AssignedAt,
                        d.CurrentAssignment.AssignedUserId == null
                            ? "Unallocated"
                            : d.CurrentAssignment.AssignedUserId == userId
                                ? "AssignedToMe"
                                : "AssignedToOther"
                    )
                ))
                .ToListAsync(ct);

            return Results.Ok(new MyDeskResponseDto(
                summary,
                activeDesks,
                items,
                totalCount,
                p,
                ps
            ));
        }).RequirePermission(PermissionCodes.DakView);

        // 3. Get Dak Details
        dak.MapGet("/{id:guid}", async (
            Guid id,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            IMatterAuthorizationService matterAuth,
            IAccessControlService accessControl,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct))
                return Results.Forbid();

            var dakRecord = await db.Daks.AsNoTracking()
                .Include(d => d.Category)
                .Include(d => d.Workstream)
                .Include(d => d.CurrentAssignment)
                    .ThenInclude(a => a!.OfficeDesk)
                .Include(d => d.CurrentAssignment)
                    .ThenInclude(a => a!.AssignedUser)
                .Include(d => d.CurrentAssignment)
                    .ThenInclude(a => a!.AssignedByUser)
                .Include(d => d.MainDocument)
                .Include(d => d.Attachments.Where(a => a.RecordStatus == RecordStatus.Active))
                    .ThenInclude(a => a.Document)
                .Include(d => d.VillageLinks.Where(l => l.RecordStatus == RecordStatus.Active))
                    .ThenInclude(l => l.Village)
                .Include(d => d.AwardLinks.Where(l => l.RecordStatus == RecordStatus.Active))
                    .ThenInclude(l => l.Award)
                .Include(d => d.MatterLinks.Where(l => l.RecordStatus == RecordStatus.Active))
                    .ThenInclude(l => l.Matter)
                .Include(d => d.KhasraLinks.Where(l => l.RecordStatus == RecordStatus.Active))
                    .ThenInclude(l => l.Khasra)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (dakRecord is null) return Results.NotFound();

            // Evaluate Attention / Health Flags
            var isDeskActive = dakRecord.CurrentAssignment?.OfficeDesk?.IsActive == true && dakRecord.CurrentAssignment?.OfficeDesk?.RecordStatus == RecordStatus.Active;
            bool isUserEligible = true;

            if (dakRecord.CurrentAssignment?.AssignedUserId.HasValue == true)
            {
                var assignedUid = dakRecord.CurrentAssignment.AssignedUserId.Value;
                var assignedDeskId = dakRecord.CurrentAssignment.OfficeDeskId;
                isUserEligible = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == assignedUid
                                && m.OfficeDeskId == assignedDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.User.IsActive
                                && m.User.RecordStatus == RecordStatus.Active, ct);
            }

            var needsAttention = dakRecord.CurrentAssignment != null
                              && dakRecord.CurrentAssignment.IsActive
                              && (!isDeskActive || !isUserEligible);

            var villageLinks = await ProjectLinksAsync(dakRecord.VillageLinks.Select(l => new DakLinkItemDto(l.Id, l.VillageId, l.Village.Name, "Village")),
                () => accessControl.CanAsync(PermissionCodes.VillageView, cancellationToken: ct));
            var awardLinks = await ProjectLinksAsync(dakRecord.AwardLinks.Select(l => new DakLinkItemDto(l.Id, l.AwardId, l.Award.AwardNumber, "Award")),
                () => accessControl.CanAsync(PermissionCodes.AwardView, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct));
            var khasraLinks = await ProjectLinksAsync(dakRecord.KhasraLinks.Select(l => new DakLinkItemDto(l.Id, l.KhasraId, l.Khasra.DisplayNumber, "Khasra")),
                () => accessControl.CanAsync(PermissionCodes.KhasraView, cancellationToken: ct));
            var matterLinks = new List<DakLinkItemDto>();
            foreach (var link in dakRecord.MatterLinks)
            {
                var canRead = await matterAuth.CanAccessMatterAsync(link.MatterId, PermissionCodes.MatterView, userId, ct);
                matterLinks.Add(canRead
                    ? new DakLinkItemDto(link.Id, link.MatterId, link.Matter.Title, "Matter")
                    : new DakLinkItemDto(link.Id, null, "Restricted record", "Matter", false));
            }

            var pendingDelivery = await db.DakTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.DakId == id && t.State == DakTransferState.Pending, ct);
            var pendingNeedsAttention = pendingDelivery != null && (!await db.UserDeskMemberships.AnyAsync(m => m.UserId == pendingDelivery.ToUserId && m.OfficeDeskId == pendingDelivery.ToDeskId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active && m.OfficeDesk.Purpose == (pendingDelivery.DestinationKind == DakDestinationKind.RecordRoom ? OfficeDeskPurpose.RecordRoom : OfficeDeskPurpose.General), ct) ||
                !await db.UserRoles.AnyAsync(u => u.UserId == pendingDelivery.ToUserId && u.Role.IsActive && u.Role.RecordStatus == RecordStatus.Active && u.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.DakReceive && (p.ScopeMode == ScopeMode.All || p.ScopeMode == ScopeMode.Workstream || p.ScopeMode == ScopeMode.Assigned)), ct));
            var detailDto = new DakDetailDto(
                dakRecord.Id,
                dakRecord.DiaryNumber,
                dakRecord.ReceivedDate,
                dakRecord.Subject,
                dakRecord.SenderName,
                dakRecord.SenderDesignation,
                dakRecord.SenderDepartment,
                dakRecord.SenderAddress,
                dakRecord.SenderReferenceNumber,
                dakRecord.SenderLetterDate,
                dakRecord.InwardMode,
                dakRecord.Priority.ToString(),
                dakRecord.DueDate,
                dakRecord.Status.ToString(),
                dakRecord.CategoryId,
                dakRecord.Category?.Name,
                dakRecord.WorkstreamId,
                dakRecord.Workstream?.Name,
                dakRecord.Revision,
                dakRecord.MainDocumentId,
                dakRecord.MainDocument?.OriginalFileName,
                dakRecord.CurrentAssignment == null ? null : new DakAssignmentDto(
                    dakRecord.CurrentAssignment.Id,
                    dakRecord.CurrentAssignment.OfficeDeskId,
                    dakRecord.CurrentAssignment.OfficeDesk.Code,
                    dakRecord.CurrentAssignment.OfficeDesk.Name,
                    dakRecord.CurrentAssignment.AssignedUserId,
                    dakRecord.CurrentAssignment.AssignedUser?.DisplayName,
                    dakRecord.CurrentAssignment.AssignedByUser.DisplayName,
                    dakRecord.CurrentAssignment.AssignedAt,
                    dakRecord.CurrentAssignment.Instructions,
                    dakRecord.CurrentAssignment.IsActive,
                    isDeskActive,
                    isUserEligible,
                    needsAttention, dakRecord.CurrentAssignment.ReceivedAt,
                    dakRecord.CurrentAssignment.IsActive && dakRecord.CurrentAssignment.RecordStatus == RecordStatus.Active && dakRecord.CurrentAssignment.ReceivedAt != null
                ),
                dakRecord.Attachments.Select(a => new DakAttachmentDto(a.Id, a.DocumentId, a.Document.OriginalFileName, a.Title, a.AttachmentType, a.SequenceOrder, a.CreatedAt)).ToList(),
                villageLinks,
                awardLinks,
                matterLinks,
                khasraLinks,
                dakRecord.CreatedAt,
                dakRecord.CreatedBy,
                dakRecord.UpdatedAt,
                dakRecord.UpdatedBy,
                dakRecord.RecordStatus.ToString(), dakRecord.RoutingState.ToString(), dakRecord.PhysicalState.ToString(), dakRecord.ProcessingCycle, pendingDelivery?.Id, pendingDelivery?.ToUserId,
                needsAttention || pendingNeedsAttention || dakRecord.RoutingState == DakRoutingState.LegacyUnconfirmed || dakRecord.PhysicalState == DakPhysicalState.ReturnPending,
                dakRecord.ResolvedAt, dakRecord.ResolvedByUserId, dakRecord.ResolutionRemarks
            );

            return Results.Ok(detailDto);
        }).RequirePermission(PermissionCodes.DakView);

        // 4. Update Dak Metadata / Classification
        dak.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDakMetadataRequest request,
            LacDbContext db,
            DakWorkflowService workflow,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct))
                return Results.Forbid();

            await workflow.MutateIntakeAsync(id, request.ExpectedRevision, userId, "MetadataUpdated", async (dak, mutationCt) =>
            {
                if (request.CategoryId.HasValue)
                {
                    var catExists = await db.DakCategories.AsNoTracking().AnyAsync(c => c.Id == request.CategoryId.Value && c.IsActive && c.RecordStatus == RecordStatus.Active, mutationCt);
                    if (!catExists) throw new DakWorkflowException("Category does not exist or is inactive.");
                }

                if (request.WorkstreamId.HasValue)
                {
                    var wsExists = await db.Workstreams.AsNoTracking().AnyAsync(w => w.Id == request.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, mutationCt);
                    if (!wsExists) throw new DakWorkflowException("Workstream does not exist or is inactive.");
                }

                dak.Subject = string.IsNullOrWhiteSpace(request.Subject) ? dak.Subject : request.Subject.Trim();
                dak.SenderName = string.IsNullOrWhiteSpace(request.SenderName) ? dak.SenderName : request.SenderName.Trim();
                dak.SenderDesignation = request.SenderDesignation?.Trim();
                dak.SenderDepartment = request.SenderDepartment?.Trim();
                dak.SenderAddress = request.SenderAddress?.Trim();
                dak.SenderReferenceNumber = request.SenderReferenceNumber?.Trim();
                dak.SenderLetterDate = request.SenderLetterDate;
                dak.InwardMode = string.IsNullOrWhiteSpace(request.InwardMode) ? dak.InwardMode : request.InwardMode.Trim();
                dak.Priority = request.Priority;
                dak.DueDate = request.DueDate;
                dak.CategoryId = request.CategoryId;
                dak.WorkstreamId = request.WorkstreamId;
                return true;
            }, ct);
            var updated = await db.Daks.AsNoTracking().SingleAsync(d => d.Id == id, ct);
            return Results.Ok(new { id, revision = updated.Revision, updatedAt = updated.UpdatedAt });
        }).RequirePermission(PermissionCodes.DakEdit);

        // 4b. Operational Lookup: Edit Categories & Workstreams
        dak.MapGet("/{id:guid}/lookups/edit", async (
            Guid id,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct))
                return Results.Forbid();

            var categories = await db.DakCategories.AsNoTracking()
                .Where(c => c.IsActive && c.RecordStatus == RecordStatus.Active)
                .OrderBy(c => c.Name)
                .Select(c => new DakCategoryDto(c.Id, c.Code, c.Name, c.Description, c.DefaultPriority.ToString(), c.DefaultWorkstreamId, c.DefaultWorkstream == null ? null : c.DefaultWorkstream.Name, c.IsActive))
                .ToListAsync(ct);

            var workstreams = await db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                .OrderBy(w => w.Name)
                .Select(w => new { id = w.Id, code = w.Code, name = w.Name, isActive = w.IsActive })
                .ToListAsync(ct);

            return Results.Ok(new { categories, workstreams });
        }).RequirePermission(PermissionCodes.DakEdit);

        // 5. Get Movement Timeline
        dak.MapGet("/{id:guid}/timeline", async (
            Guid id,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct))
                return Results.Forbid();

            var movements = await db.DakMovements.AsNoTracking()
                .Where(m => m.DakId == id)
                .OrderBy(m => m.SequenceNumber)
                .Select(m => new DakMovementDto(
                    m.Id,
                    m.SequenceNumber,
                    m.Action.ToString(),
                    m.FromDeskId,
                    m.FromDeskCodeSnapshot,
                    m.FromDeskNameSnapshot,
                    m.FromUserId,
                    m.FromUserDisplayNameSnapshot,
                    m.ToDeskId,
                    m.ToDeskCodeSnapshot,
                    m.ToDeskNameSnapshot,
                    m.ToUserId,
                    m.ToUserDisplayNameSnapshot,
                    m.ActionByUserId,
                    m.ActionByDisplayNameSnapshot,
                    m.ActionAt,
                    m.Remarks,
                    m.InstructionsSnapshot, m.TransferId, m.EventVersion, null
                )).ToListAsync(ct);
            var snapshots = await db.DakMovements.AsNoTracking().Where(m => m.DakId == id && m.StateSnapshotJson != null)
                .ToDictionaryAsync(m => m.Id, m => m.StateSnapshotJson!, ct);
            return Results.Ok(movements.Select(m => snapshots.TryGetValue(m.Id, out var snapshot)
                ? m with { StateChanges = PublicCustodyStateChanges(snapshot) } : m));
        }).RequirePermission(PermissionCodes.DakView);

        // 5b. Operational Lookup: Movement Target Desks & Eligible Members
        dak.MapGet("/{id:guid}/movement-targets", async (
            Guid id,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var initial = await db.Daks.AnyAsync(d => d.Id == id && (d.Status == DakStatus.Registered && d.RoutingState == DakRoutingState.Unassigned || d.RoutingState == DakRoutingState.LegacyUnconfirmed && d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.AssignedUserId == null), ct);
            if (!await dakAuth.CanAccessDakAsync(id, initial ? PermissionCodes.DakMark : PermissionCodes.DakMove, userId, ct))
                return Results.Forbid();

            var desks = await db.OfficeDesks.AsNoTracking()
                .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
                .OrderBy(d => d.Name)
                .Select(d => new
                {
                    purpose = d.Purpose.ToString(),
                    id = d.Id,
                    code = d.Code,
                    name = d.Name,
                    isActive = d.IsActive,
                    members = d.UserMemberships
                        .Where(m => m.IsActive
                                 && m.RemovedAt == null
                                 && m.RecordStatus == RecordStatus.Active
                                 && m.User.IsActive
                                 && m.User.RecordStatus == RecordStatus.Active)
                        .OrderByDescending(m => m.IsPrimary)
                        .ThenBy(m => m.User.DisplayName)
                        .Select(m => new
                        {
                            userId = m.UserId,
                            displayName = m.User.DisplayName,
                            designation = m.User.Designation == null ? null : m.User.Designation.Name,
                            isPrimary = m.IsPrimary
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            return Results.Ok(new { desks });
        });

        // 6. Move Dak (Marked, Forwarded, Returned)
        dak.MapPost("/{id:guid}/move", async (
            Guid id,
            MoveDakRequest request,
            HttpRequest http,
            DakWorkflowService workflow,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            // Strict Server-Side Whitelist
            if (!Enum.TryParse<DakMovementAction>(request.Action, true, out var action) ||
                (action != DakMovementAction.Marked && action != DakMovementAction.Forwarded && action != DakMovementAction.Returned))
            {
                return Results.BadRequest(new { message = $"Action '{request.Action}' is invalid for Move. Only Marked, Forwarded, and Returned are allowed." });
            }

            if (!await dakAuth.CanAccessDakAsync(id, action == DakMovementAction.Marked ? PermissionCodes.DakMark : PermissionCodes.DakMove, userId, ct))
            {
                var header = http.Headers["Idempotency-Key"];
                if (header.Count != 1 || !Guid.TryParse(header[0], out var replayKey) ||
                    !await db.DakWorkflowCommandReceipts.AnyAsync(r => r.ActorUserId == userId && r.RequestId == replayKey && r.DakId == id, ct))
                    return Results.Forbid();
            }

            try
            {
                var updated = await workflow.SendAsync(id, new SendDakCommand(action, request.ToDeskId, request.ToUserId ?? Guid.Empty,
                    DakDestinationKind.Officer, false, request.Remarks, request.Instructions, request.ExpectedRevision, CustodyRequestKey(http)), userId, ct);
                return Results.Ok(new { id = updated.DakId, updated.CommandId, updated.TransferId, updated.Revision, updated.Status,
                    updated.RoutingState, updated.PhysicalState, updated.ConfirmedHolderUserId });
            }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
        });

        // 7. Dispose Dak
        dak.MapPost("/{id:guid}/dispose", async (
            Guid id,
            DisposeDakRequest request,
            DakWorkflowService workflow,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakDispose, userId, ct))
                return Results.Forbid();

            try
            {
                var cmd = new DisposeDakCommand(request.Remarks, request.ExpectedRevision);
                var updated = await workflow.DisposeAsync(id, cmd, userId, ct);
                return Results.Ok(new { id = updated.Id, revision = updated.Revision, status = updated.Status.ToString() });
            }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
        }).RequirePermission(PermissionCodes.DakDispose);

        // 8. Cancel Dak
        dak.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelDakRequest request,
            DakWorkflowService workflow,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakCancel, userId, ct))
                return Results.Forbid();

            try
            {
                var cmd = new CancelDakCommand(request.Reason, request.ExpectedRevision);
                var updated = await workflow.CancelAsync(id, cmd, userId, ct);
                return Results.Ok(new { id = updated.Id, revision = updated.Revision, status = updated.Status.ToString() });
            }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
        }).RequirePermission(PermissionCodes.DakCancel);

        // Intake mutations serialize against marking/disposal on the same Dak row.
        dak.MapPost("/{id:guid}/attachments", async (Guid id, HttpRequest request, DakWorkflowService workflow,
            IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct)) return Results.Forbid();
            if (!request.HasFormContentType) return Results.BadRequest(new { message = "Request must be multipart/form-data." });
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { message = "A valid document file is required." });
            await using var stream = file.OpenReadStream();
            if (!TryValidateAndDeriveMime(stream, file.FileName, out var mime, out var error))
                return Results.BadRequest(new { message = error });
            var attachment = await workflow.AddIntakeAttachmentAsync(id, ReadRevision(request), userId, stream,
                file.FileName, mime, form["title"].ToString(), form["attachmentType"].ToString(), ct);
            return Results.Created($"/api/dak/{id}/attachments/{attachment.Id}",
                new { id = attachment.Id, documentId = attachment.DocumentId, title = attachment.Title });
        }).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId,
            HttpRequest request, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser, CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct)) return Results.Forbid();
            await workflow.MutateIntakeAsync(id, ReadRevision(request), userId, "AttachmentArchived", async (_, c) =>
            {
                var attachment = await db.DakAttachments.SingleOrDefaultAsync(a => a.Id == attachmentId && a.DakId == id && a.RecordStatus == RecordStatus.Active, c)
                    ?? throw new DakWorkflowException("Attachment not found.", 404);
                attachment.RecordStatus = RecordStatus.Archived;
                return true;
            }, ct);
            return Results.NoContent();
        }).RequirePermission(PermissionCodes.DakEdit);

        dak.MapPost("/{id:guid}/villages/{villageId:guid}", (Guid id, Guid villageId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, villageId, "Village", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/links/villages", (Guid id, LinkEntityRequest request, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, request.EntityId, "Village", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/villages/{villageId:guid}", (Guid id, Guid villageId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, villageId, "Village", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/links/villages/{villageId:guid}", (Guid id, Guid villageId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, villageId, "Village", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/awards/{awardId:guid}", (Guid id, Guid awardId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, awardId, "Award", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/links/awards", (Guid id, LinkEntityRequest request, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, request.EntityId, "Award", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/awards/{awardId:guid}", (Guid id, Guid awardId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, awardId, "Award", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/links/awards/{awardId:guid}", (Guid id, Guid awardId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, awardId, "Award", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/matters/{matterId:guid}", (Guid id, Guid matterId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, matterId, "Matter", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/links/matters", (Guid id, LinkEntityRequest request, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, request.EntityId, "Matter", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/matters/{matterId:guid}", (Guid id, Guid matterId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, matterId, "Matter", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/links/matters/{matterId:guid}", (Guid id, Guid matterId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, matterId, "Matter", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/khasras/{khasraId:guid}", (Guid id, Guid khasraId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, khasraId, "Khasra", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapPost("/{id:guid}/links/khasras", (Guid id, LinkEntityRequest request, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, request.EntityId, "Khasra", false, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/khasras/{khasraId:guid}", (Guid id, Guid khasraId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, khasraId, "Khasra", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);
        dak.MapDelete("/{id:guid}/links/khasras/{khasraId:guid}", (Guid id, Guid khasraId, HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth, IMatterAuthorizationService matterAuth, IAccessControlService accessControl, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkContextAsync(id, khasraId, "Khasra", true, http, db, workflow, dakAuth, matterAuth, accessControl, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapGet("/{id:guid}/physical-original", async (Guid id, LacDbContext db, IDakAuthorizationService auth,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (!user.UserId.HasValue) return Results.Unauthorized();
            if (!await auth.CanAccessDakAsync(id, PermissionCodes.DakView, user.UserId.Value, ct)) return Results.Forbid();
            return Results.Ok(await db.Daks.AsNoTracking().Where(d => d.Id == id).Select(d => new
            {
                physicalState = d.PhysicalState.ToString(),
                lastConfirmedCustodianUserId = d.PhysicalOriginalUserId,
                d.HasPhysicalOriginal, deskId = d.PhysicalOriginalDeskId, userId = d.PhysicalOriginalUserId,
                locationNote = d.PhysicalOriginalLocationNote, provenanceNote = d.PhysicalOriginalProvenanceNote,
                updatedAt = d.PhysicalOriginalUpdatedAt, updatedByUserId = d.PhysicalOriginalUpdatedByUserId, d.Revision
            }).SingleAsync(ct));
        }).RequirePermission(PermissionCodes.DakView);

        dak.MapPut("/{id:guid}/physical-original", async (Guid id, PhysicalOriginalRequest request,
            LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService auth,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (!user.UserId.HasValue) return Results.Unauthorized();
            if (!await auth.CanAccessDakAsync(id, PermissionCodes.DakEdit, user.UserId.Value, ct)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.ProvenanceNote) || request.ProvenanceNote.Length > 1000 || request.LocationNote?.Length > 1000)
                return Results.BadRequest(new { message = "A provenance note (up to 1000 characters) is required; location note must not exceed 1000 characters." });
            if (request.HasPhysicalOriginal != true && (request.DeskId.HasValue || request.UserId.HasValue || !string.IsNullOrWhiteSpace(request.LocationNote)))
                return Results.BadRequest(new { message = "A location can only be recorded when physical original existence is confirmed." });
            await workflow.MutateIntakeAsync(id, request.ExpectedRevision, user.UserId.Value, "PhysicalOriginalUpdated", async (dak, c) =>
            {
                if (dak.PhysicalState is DakPhysicalState.InTransit or DakPhysicalState.ReturnPending)
                    throw new DakWorkflowException("Use receive or confirm-return to settle physical custody.", 409);
                if (dak.RoutingState == DakRoutingState.WithHolder || await db.DakTransfers.AnyAsync(t => t.DakId == id, c))
                {
                    var sameCustody = dak.PhysicalOriginalUserId == request.UserId && dak.PhysicalOriginalDeskId == request.DeskId && dak.HasPhysicalOriginal == request.HasPhysicalOriginal;
                    var firstSelfObservation = dak.PhysicalOriginalUserId == null && request.HasPhysicalOriginal == true && request.UserId == user.UserId &&
                        (dak.Status == DakStatus.Registered && dak.RoutingState == DakRoutingState.Unassigned || await db.DakAssignments.AnyAsync(a => a.DakId == id && a.ReceivedAt != null && a.IsActive && a.AssignedUserId == user.UserId && a.OfficeDeskId == request.DeskId, c));
                    var unnamedObservation = dak.PhysicalOriginalUserId == null && dak.PhysicalOriginalDeskId == null && request.UserId == null && request.DeskId == null;
                    if (!sameCustody && !firstSelfObservation && !unnamedObservation) throw new DakWorkflowException("Physical custodian changes require acknowledged transfer.", 409);
                }
                if (request.DeskId.HasValue && !await db.OfficeDesks.AnyAsync(d => d.Id == request.DeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, c))
                    throw new DakWorkflowException("Physical location desk is inactive or missing.");
                if (request.UserId.HasValue && !await db.AppUsers.AnyAsync(u => u.Id == request.UserId && u.IsActive && u.RecordStatus == RecordStatus.Active, c))
                    throw new DakWorkflowException("Physical custodian user is inactive or missing.");
                if (request.DeskId.HasValue && request.UserId.HasValue && !await db.UserDeskMemberships.AnyAsync(m =>
                    m.OfficeDeskId == request.DeskId && m.UserId == request.UserId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active, c))
                    throw new DakWorkflowException("Physical custodian is not an active member of the specified desk.");
                var wasHeld = dak.PhysicalState == DakPhysicalState.Held;
                dak.PhysicalState = request.HasPhysicalOriginal == null ? DakPhysicalState.Unknown : request.HasPhysicalOriginal == false ? DakPhysicalState.NotPresent : wasHeld ? DakPhysicalState.Held : DakPhysicalState.AtRecordedLocation;
                dak.HasPhysicalOriginal = request.HasPhysicalOriginal;
                dak.PhysicalOriginalDeskId = request.DeskId;
                dak.PhysicalOriginalUserId = request.UserId;
                dak.PhysicalOriginalLocationNote = request.LocationNote?.Trim();
                dak.PhysicalOriginalProvenanceNote = request.ProvenanceNote.Trim();
                dak.PhysicalOriginalUpdatedAt = DateTimeOffset.UtcNow;
                dak.PhysicalOriginalUpdatedByUserId = user.UserId;
                return true;
            }, ct);
            return Results.Ok(new { id, revision = await db.Daks.Where(d => d.Id == id).Select(d => d.Revision).SingleAsync(ct) });
        }).RequirePermission(PermissionCodes.DakEdit);

        // 11. Scoped Document Content Stream
        // Primary main document
        dak.MapGet("/{id:guid}/content", async (
            Guid id,
            bool? download,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct))
                return Results.Forbid();

            var dakRecord = await db.Daks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dakRecord is null || !dakRecord.MainDocumentId.HasValue) return Results.NotFound();

            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dakRecord.MainDocumentId.Value && x.Status == "Active", ct);
            if (document is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(document.StoragePath, ct);
            if (stream is null) return Results.NotFound();

            var isDownload = download == true;
            var action = isDownload ? RecordAccessAction.Downloaded : RecordAccessAction.Opened;

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: action,
                DocumentId: document.Id,
                ContextEntityType: "Dak",
                ContextEntityId: id,
                DocumentTitleSnapshot: document.OriginalFileName
            ), ct);

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            if (isDownload)
            {
                return Results.File(stream, document.MimeType ?? "application/octet-stream", document.OriginalFileName, enableRangeProcessing: true);
            }
            return Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
        }).RequirePermission(PermissionCodes.DakView);

        // Attachment document
        dak.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (
            Guid id,
            Guid attachmentId,
            bool? download,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct))
                return Results.Forbid();

            var att = await db.DakAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.DakId == id && a.RecordStatus == RecordStatus.Active, ct);
            if (att is null) return Results.NotFound();

            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == att.DocumentId && x.Status == "Active", ct);
            if (document is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(document.StoragePath, ct);
            if (stream is null) return Results.NotFound();

            var ext = Path.GetExtension(document.OriginalFileName);
            var isDownload = download == true || (download == null && !MatterDocumentValidation.IsInlineDisposition(ext));
            var action = isDownload ? RecordAccessAction.Downloaded : RecordAccessAction.Opened;

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: action,
                DocumentId: document.Id,
                ContextEntityType: "Dak",
                ContextEntityId: id,
                DocumentTitleSnapshot: document.OriginalFileName
            ), ct);

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            if (isDownload)
            {
                return Results.File(stream, document.MimeType ?? "application/octet-stream", document.OriginalFileName, enableRangeProcessing: true);
            }
            return Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
        }).RequirePermission(PermissionCodes.DakView);

        // Document by document ID
        dak.MapGet("/{id:guid}/documents/{docId:guid}/content", async (
            Guid id,
            Guid docId,
            bool? download,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDocumentAsync(id, docId, userId, ct))
                return Results.Forbid();

            var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == docId && x.Status == "Active", ct);
            if (document is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(document.StoragePath, ct);
            if (stream is null) return Results.NotFound();

            var ext = Path.GetExtension(document.OriginalFileName);
            var isDownload = download == true || (download == null && !MatterDocumentValidation.IsInlineDisposition(ext));
            var action = isDownload ? RecordAccessAction.Downloaded : RecordAccessAction.Opened;

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: action,
                DocumentId: document.Id,
                ContextEntityType: "Dak",
                ContextEntityId: id,
                DocumentTitleSnapshot: document.OriginalFileName
            ), ct);

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            if (isDownload)
            {
                return Results.File(stream, document.MimeType ?? "application/octet-stream", document.OriginalFileName, enableRangeProcessing: true);
            }
            return Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
        }).RequirePermission(PermissionCodes.DakView);

        // 12. Dak Category Admin Endpoints
        var catAdmin = api.MapGroup("/admin/dak-categories");

        catAdmin.MapGet("/", async (LacDbContext db, CancellationToken ct) =>
        {
            var categories = await db.DakCategories.AsNoTracking()
                .Include(c => c.DefaultWorkstream)
                .OrderBy(c => c.Name)
                .Select(c => new DakCategoryDto(c.Id, c.Code, c.Name, c.Description, c.DefaultPriority.ToString(), c.DefaultWorkstreamId, c.DefaultWorkstream == null ? null : c.DefaultWorkstream.Name, c.IsActive))
                .ToListAsync(ct);

            return Results.Ok(categories);
        }).RequirePermission(PermissionCodes.AccessManage);

        catAdmin.MapPost("/", async (CreateDakCategoryRequest request, LacDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Category Code and Name are required." });

            var normalizedCode = request.Code.Trim().ToUpperInvariant();
            var exists = await db.DakCategories.AnyAsync(c => c.Code == normalizedCode, ct);
            if (exists) return Results.BadRequest(new { message = $"A category with code '{normalizedCode}' already exists." });

            if (request.DefaultWorkstreamId.HasValue)
            {
                var wsExists = await db.Workstreams.AnyAsync(w => w.Id == request.DefaultWorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (!wsExists) return Results.BadRequest(new { message = "Default Workstream does not exist or is inactive." });
            }

            Enum.TryParse<DakPriority>(request.DefaultPriority, true, out var pr);

            var cat = new DakCategory
            {
                Code = normalizedCode,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                DefaultPriority = pr,
                DefaultWorkstreamId = request.DefaultWorkstreamId,
                IsActive = true
            };
            db.DakCategories.Add(cat);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/admin/dak-categories/{cat.Id}", new { id = cat.Id, code = cat.Code, name = cat.Name });
        }).RequirePermission(PermissionCodes.AccessManage);

        catAdmin.MapPut("/{id:guid}", async (Guid id, UpdateDakCategoryRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var cat = await db.DakCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cat is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Category Name is required." });

            if (request.DefaultWorkstreamId.HasValue)
            {
                var wsExists = await db.Workstreams.AnyAsync(w => w.Id == request.DefaultWorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (!wsExists) return Results.BadRequest(new { message = "Default Workstream does not exist or is inactive." });
            }

            Enum.TryParse<DakPriority>(request.DefaultPriority, true, out var pr);

            cat.Name = request.Name.Trim();
            cat.Description = request.Description?.Trim();
            cat.DefaultPriority = pr;
            cat.DefaultWorkstreamId = request.DefaultWorkstreamId;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = cat.Id, code = cat.Code, name = cat.Name });
        }).RequirePermission(PermissionCodes.AccessManage);

        catAdmin.MapPost("/{id:guid}/toggle-active", async (Guid id, LacDbContext db, CancellationToken ct) =>
        {
            var cat = await db.DakCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cat is null) return Results.NotFound();

            cat.IsActive = !cat.IsActive;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = cat.Id, isActive = cat.IsActive });
        }).RequirePermission(PermissionCodes.AccessManage);

        return api;
    }

    private static bool TryValidateAndDeriveMime(Stream stream, string fileName, out string mimeType, out string? errorMessage)
    {
        mimeType = "";
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            errorMessage = "File name is required.";
            return false;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".png" && ext != ".jpg" && ext != ".jpeg")
        {
            errorMessage = "Only PDF (.pdf) and standard image files (.png, .jpg, .jpeg) are allowed.";
            return false;
        }

        if (!stream.CanSeek)
        {
            errorMessage = "Unable to inspect file stream.";
            return false;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[8];
        var bytesRead = stream.Read(header);
        stream.Position = 0;

        if (bytesRead < 4)
        {
            errorMessage = "File is empty or corrupted.";
            return false;
        }

        // PDF magic bytes: %PDF- (0x25, 0x50, 0x44, 0x46)
        if (ext == ".pdf")
        {
            if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            {
                mimeType = "application/pdf";
                return true;
            }
            errorMessage = "File has a .pdf extension but its content does not have a valid PDF header (%PDF-).";
            return false;
        }

        // PNG magic bytes: 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        if (ext == ".png")
        {
            if (bytesRead >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                mimeType = "image/png";
                return true;
            }
            errorMessage = "File has a .png extension but its content does not have a valid PNG header.";
            return false;
        }

        // JPEG magic bytes: 0xFF, 0xD8, 0xFF
        if (ext == ".jpg" || ext == ".jpeg")
        {
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                mimeType = "image/jpeg";
                return true;
            }
            errorMessage = "File has a JPEG extension but its content does not have a valid JPEG header.";
            return false;
        }

        errorMessage = "Unsupported file format.";
        return false;
    }
}

// DTOs
public sealed record DakListItemDto(
    Guid Id,
    string DiaryNumber,
    DateOnly ReceivedDate,
    string Subject,
    string SenderName,
    string? SenderDepartment,
    string InwardMode,
    string Priority,
    DateOnly? DueDate,
    string Status,
    string? CategoryName,
    string? WorkstreamName,
    string? AssignedDeskName,
    string? AssignedUserDisplayName,
    bool HasDocument,
    int Revision,
    DateTimeOffset CreatedAt,
    string RecordStatus,
    string RoutingState = "Unassigned",
    string PhysicalState = "Unknown"
);

public sealed record DakDetailDto(
    Guid Id,
    string DiaryNumber,
    DateOnly ReceivedDate,
    string Subject,
    string SenderName,
    string? SenderDesignation,
    string? SenderDepartment,
    string? SenderAddress,
    string? SenderReferenceNumber,
    DateOnly? SenderLetterDate,
    string InwardMode,
    string Priority,
    DateOnly? DueDate,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? WorkstreamId,
    string? WorkstreamName,
    int Revision,
    Guid? MainDocumentId,
    string? MainDocumentFileName,
    DakAssignmentDto? CurrentAssignment,
    IReadOnlyList<DakAttachmentDto> Attachments,
    IReadOnlyList<DakLinkItemDto> VillageLinks,
    IReadOnlyList<DakLinkItemDto> AwardLinks,
    IReadOnlyList<DakLinkItemDto> MatterLinks,
    IReadOnlyList<DakLinkItemDto> KhasraLinks,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    string RecordStatus,
    string RoutingState = "Unassigned",
    string PhysicalState = "Unknown",
    int ProcessingCycle = 1,
    Guid? PendingTransferId = null,
    Guid? PendingReceiverUserId = null,
    bool NeedsAttention = false,
    DateTimeOffset? ResolvedAt = null,
    Guid? ResolvedByUserId = null,
    string? ResolutionRemarks = null
);

public sealed record DakAssignmentDto(
    Guid Id,
    Guid OfficeDeskId,
    string DeskCode,
    string DeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    string AssignedByDisplayName,
    DateTimeOffset AssignedAt,
    string? Instructions,
    bool IsActive,
    bool IsDeskActive,
    bool IsUserEligible,
    bool NeedsAttention,
    DateTimeOffset? ReceivedAt = null,
    bool IsConfirmed = false
);

public sealed record DakAttachmentDto(
    Guid Id,
    Guid DocumentId,
    string OriginalFileName,
    string Title,
    string AttachmentType,
    int SequenceOrder,
    DateTimeOffset CreatedAt
);

public sealed record DakLinkItemDto(Guid LinkId, Guid? EntityId, string DisplayName, string EntityType, bool CanOpen = true);

public sealed record DakMovementDto(
    Guid Id,
    int SequenceNumber,
    string Action,
    Guid? FromDeskId,
    string? FromDeskCode,
    string? FromDeskName,
    Guid? FromUserId,
    string? FromUserDisplayName,
    Guid? ToDeskId,
    string? ToDeskCode,
    string? ToDeskName,
    Guid? ToUserId,
    string? ToUserDisplayName,
    Guid ActionByUserId,
    string ActionByDisplayName,
    DateTimeOffset ActionAt,
    string? Remarks,
    string? Instructions,
    Guid? TransferId = null,
    int EventVersion = 0,
    JsonElement? StateChanges = null
);

public sealed record UpdateDakMetadataRequest(
    string Subject,
    string SenderName,
    string? SenderDesignation,
    string? SenderDepartment,
    string? SenderAddress,
    string? SenderReferenceNumber,
    DateOnly? SenderLetterDate,
    string InwardMode,
    DakPriority Priority,
    DateOnly? DueDate,
    Guid? CategoryId,
    Guid? WorkstreamId,
    int ExpectedRevision
);

public sealed record MoveDakRequest(
    string Action,
    Guid ToDeskId,
    Guid? ToUserId,
    string? Remarks,
    string? Instructions,
    int ExpectedRevision
);

public sealed record DisposeDakRequest(
    string Remarks,
    int ExpectedRevision
);

public sealed record CancelDakRequest(
    string Reason,
    int ExpectedRevision
);

public sealed record LinkEntityRequest(Guid EntityId);

public sealed record CreateDakCategoryRequest(
    string Code,
    string Name,
    string? Description,
    string DefaultPriority,
    Guid? DefaultWorkstreamId
);

public sealed record UpdateDakCategoryRequest(
    string Name,
    string? Description,
    string DefaultPriority,
    Guid? DefaultWorkstreamId
);

public sealed record DakCategoryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string DefaultPriority,
    Guid? DefaultWorkstreamId,
    string? DefaultWorkstreamName,
    bool IsActive
);

public sealed record MyDeskSummaryDto(
    int Total,
    int Immediate,
    int Urgent,
    int Overdue,
    int DueToday,
    int AssignedToMe,
    int Unallocated
);

public sealed record MyDeskUserDeskDto(
    Guid Id,
    string Code,
    string Name,
    bool IsPrimary
);

public sealed record MyDeskAssignmentDto(
    Guid DeskId,
    string DeskCode,
    string DeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    DateTimeOffset AssignedAt,
    string HandlerState
);

public sealed record MyDeskItemDto(
    Guid Id,
    string DiaryNumber,
    DateOnly ReceivedDate,
    string Subject,
    string SenderName,
    string? SenderDepartment,
    string Priority,
    DateOnly? DueDate,
    string? WorkstreamName,
    string Status,
    int Revision,
    MyDeskAssignmentDto Assignment
);

public sealed record MyDeskResponseDto(
    MyDeskSummaryDto Summary,
    IReadOnlyList<MyDeskUserDeskDto> Desks,
    IReadOnlyList<MyDeskItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);


public sealed record PhysicalOriginalRequest(bool? HasPhysicalOriginal, Guid? DeskId, Guid? UserId,
    string? LocationNote, string ProvenanceNote, int ExpectedRevision);
