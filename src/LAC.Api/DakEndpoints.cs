namespace LAC.Api;

using System.IO;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public static class DakEndpoints
{
    public static RouteGroupBuilder MapDakEndpoints(this RouteGroupBuilder api)
    {
        var dak = api.MapGroup("/dak");

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
                DocumentContentType: contentType
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

        // 1c. Operational Lookup: Directory Active Office Desks
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

            return Results.Ok(new { desks });
        }).RequirePermission(PermissionCodes.DakView);

        // 2. Collection Query with Union-of-Scopes Filtering
        dak.MapGet("/", async (
            int? page,
            int? pageSize,
            string? q,
            string? status,
            string? priority,
            Guid? deskId,
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
                .AsQueryable();

            // Authorize collection:
            var authResult = await dakAuth.AuthorizeListQueryAsync(baseQuery, PermissionCodes.DakView, userId, ct);
            if (!authResult.HasPermission)
                return Results.Forbid();

            var query = authResult.Query;

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
                d.MainDocumentId != null,
                d.Revision,
                d.CreatedAt
            )).ToListAsync(ct);

            return Results.Ok(new { items, totalCount, page = p, pageSize = ps });
        }).RequirePermission(PermissionCodes.DakView);

        // 3. Get Dak Details
        dak.MapGet("/{id:guid}", async (
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
                    needsAttention
                ),
                dakRecord.Attachments.Select(a => new DakAttachmentDto(a.Id, a.DocumentId, a.Document.OriginalFileName, a.Title, a.AttachmentType, a.SequenceOrder, a.CreatedAt)).ToList(),
                dakRecord.VillageLinks.Select(v => new DakLinkItemDto(v.Id, v.VillageId, v.Village.Name, "Village")).ToList(),
                dakRecord.AwardLinks.Select(a => new DakLinkItemDto(a.Id, a.AwardId, a.Award.AwardNumber, "Award")).ToList(),
                dakRecord.MatterLinks.Select(m => new DakLinkItemDto(m.Id, m.MatterId, m.Matter.Title, "Matter")).ToList(),
                dakRecord.KhasraLinks.Select(k => new DakLinkItemDto(k.Id, k.KhasraId, k.Khasra.DisplayNumber, "Khasra")).ToList(),
                dakRecord.CreatedAt,
                dakRecord.CreatedBy,
                dakRecord.UpdatedAt,
                dakRecord.UpdatedBy
            );

            return Results.Ok(detailDto);
        }).RequirePermission(PermissionCodes.DakView);

        // 4. Update Dak Metadata / Classification
        dak.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDakMetadataRequest request,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct))
                return Results.Forbid();

            var dak = await db.Daks.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dak is null) return Results.NotFound();

            if (dak.Status == DakStatus.Disposed || dak.Status == DakStatus.Cancelled)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: $"Cannot modify metadata of a Dak in terminal status '{dak.Status}'.");

            if (dak.Revision != request.ExpectedRevision)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: "This Dak was modified elsewhere. Refresh before saving.");

            if (request.CategoryId.HasValue)
            {
                var catExists = await db.DakCategories.AsNoTracking().AnyAsync(c => c.Id == request.CategoryId.Value && c.IsActive && c.RecordStatus == RecordStatus.Active, ct);
                if (!catExists) return Results.BadRequest(new { message = "Category does not exist or is inactive." });
            }

            if (request.WorkstreamId.HasValue)
            {
                var wsExists = await db.Workstreams.AsNoTracking().AnyAsync(w => w.Id == request.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (!wsExists) return Results.BadRequest(new { message = "Workstream does not exist or is inactive." });
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
            dak.Revision++;

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = dak.Id, revision = dak.Revision, updatedAt = dak.UpdatedAt });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: "This Dak was modified elsewhere. Refresh before saving.");
            }
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
                    m.InstructionsSnapshot
                )).ToListAsync(ct);

            return Results.Ok(movements);
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

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakMove, userId, ct))
                return Results.Forbid();

            var desks = await db.OfficeDesks.AsNoTracking()
                .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
                .OrderBy(d => d.Name)
                .Select(d => new
                {
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
        }).RequirePermission(PermissionCodes.DakMove);

        // 6. Move Dak (Marked, Forwarded, Returned)
        dak.MapPost("/{id:guid}/move", async (
            Guid id,
            MoveDakRequest request,
            DakWorkflowService workflow,
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

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakMove, userId, ct))
                return Results.Forbid();

            try
            {
                var cmd = new MoveDakCommand(action, request.ToDeskId, request.ToUserId, request.Remarks, request.Instructions, request.ExpectedRevision);
                var updated = await workflow.MoveAsync(id, cmd, userId, ct);
                return Results.Ok(new { id = updated.Id, revision = updated.Revision, status = updated.Status.ToString() });
            }
            catch (DakWorkflowException ex)
            {
                return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, detail: ex.Message);
            }
        }).RequirePermission(PermissionCodes.DakMove);

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

        // 9. Attachments
        dak.MapPost("/{id:guid}/attachments", async (
            Guid id,
            HttpRequest request,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct))
                return Results.Forbid();

            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "Request must be multipart/form-data." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "A valid document file is required." });

            var title = form["title"].ToString();
            var attachmentType = form["attachmentType"].ToString();

            await using var stream = file.OpenReadStream();
            if (!TryValidateAndDeriveMime(stream, file.FileName, out var derivedMime, out var mimeError))
            {
                return Results.BadRequest(new { message = mimeError });
            }

            var actionUser = await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

            string? savedStoragePath = null;
            var fileResult = await storage.SaveAndHashAsync(stream, file.FileName, ct);
            savedStoragePath = fileResult.StoragePath;

            try
            {
                var doc = new Document
                {
                    OriginalFileName = file.FileName,
                    StoragePath = fileResult.StoragePath,
                    Sha256Hash = fileResult.Sha256Hash,
                    FileSize = fileResult.FileSize,
                    MimeType = derivedMime,
                    DocumentType = "DakAttachment",
                    UploadedBy = actionUser.DisplayName
                };
                db.Documents.Add(doc);

                var maxSeq = await db.DakAttachments.Where(a => a.DakId == id).MaxAsync(a => (int?)a.SequenceOrder, ct);
                var att = new DakAttachment
                {
                    DakId = id,
                    Document = doc,
                    Title = string.IsNullOrWhiteSpace(title) ? file.FileName : title.Trim(),
                    AttachmentType = string.IsNullOrWhiteSpace(attachmentType) ? "Annexure" : attachmentType.Trim(),
                    SequenceOrder = (maxSeq ?? 0) + 1,
                    RecordStatus = RecordStatus.Active
                };
                db.DakAttachments.Add(att);

                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/dak/{id}/attachments/{att.Id}", new { id = att.Id, documentId = doc.Id, title = att.Title });
            }
            catch
            {
                if (savedStoragePath is not null)
                {
                    try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { }
                }
                throw;
            }
        }).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            LacDbContext db,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct))
                return Results.Forbid();

            var att = await db.DakAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.DakId == id && a.RecordStatus == RecordStatus.Active, ct);
            if (att is null) return Results.NotFound();

            att.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequirePermission(PermissionCodes.DakEdit);

        // 10. Strongly-Typed Domain Links
        // Village Links
        async Task<IResult> LinkVillageInternal(Guid id, Guid villageId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var villageExists = await db.Villages.AsNoTracking().AnyAsync(v => v.Id == villageId && v.RecordStatus == RecordStatus.Active, ct);
            if (!villageExists) return Results.BadRequest(new { message = "Village does not exist or is inactive." });

            var existingActive = await db.DakVillageLinks.AnyAsync(l => l.DakId == id && l.VillageId == villageId && l.RecordStatus == RecordStatus.Active, ct);
            if (existingActive) return Results.Conflict(new { message = "Village is already actively linked." });

            var link = new DakVillageLink { DakId = id, VillageId = villageId, RecordStatus = RecordStatus.Active };
            db.DakVillageLinks.Add(link);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/dak/{id}/links/villages/{link.Id}", new { linkId = link.Id, id = link.Id, villageId });
        }

        async Task<IResult> UnlinkVillageInternal(Guid id, Guid targetId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var link = await db.DakVillageLinks.FirstOrDefaultAsync(l => l.DakId == id && (l.Id == targetId || l.VillageId == targetId) && l.RecordStatus == RecordStatus.Active, ct);
            if (link is null) return Results.NotFound();

            link.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        dak.MapPost("/{id:guid}/villages/{villageId:guid}", (Guid id, Guid villageId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkVillageInternal(id, villageId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapPost("/{id:guid}/links/villages", (Guid id, LinkEntityRequest request, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkVillageInternal(id, request.EntityId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/villages/{villageId:guid}", (Guid id, Guid villageId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkVillageInternal(id, villageId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/links/villages/{linkId:guid}", (Guid id, Guid linkId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkVillageInternal(id, linkId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        // Award Links
        async Task<IResult> LinkAwardInternal(Guid id, Guid awardId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var awardExists = await db.Awards.AsNoTracking().AnyAsync(a => a.Id == awardId && a.RecordStatus == RecordStatus.Active, ct);
            if (!awardExists) return Results.BadRequest(new { message = "Award does not exist or is inactive." });

            var existingActive = await db.DakAwardLinks.AnyAsync(l => l.DakId == id && l.AwardId == awardId && l.RecordStatus == RecordStatus.Active, ct);
            if (existingActive) return Results.Conflict(new { message = "Award is already actively linked." });

            var link = new DakAwardLink { DakId = id, AwardId = awardId, RecordStatus = RecordStatus.Active };
            db.DakAwardLinks.Add(link);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/dak/{id}/links/awards/{link.Id}", new { linkId = link.Id, id = link.Id, awardId });
        }

        async Task<IResult> UnlinkAwardInternal(Guid id, Guid targetId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var link = await db.DakAwardLinks.FirstOrDefaultAsync(l => l.DakId == id && (l.Id == targetId || l.AwardId == targetId) && l.RecordStatus == RecordStatus.Active, ct);
            if (link is null) return Results.NotFound();

            link.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        dak.MapPost("/{id:guid}/awards/{awardId:guid}", (Guid id, Guid awardId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkAwardInternal(id, awardId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapPost("/{id:guid}/links/awards", (Guid id, LinkEntityRequest request, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkAwardInternal(id, request.EntityId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/awards/{awardId:guid}", (Guid id, Guid awardId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkAwardInternal(id, awardId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/links/awards/{linkId:guid}", (Guid id, Guid linkId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkAwardInternal(id, linkId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        // Matter Links
        async Task<IResult> LinkMatterInternal(Guid id, Guid matterId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var matterExists = await db.Matters.AsNoTracking().AnyAsync(m => m.Id == matterId && m.RecordStatus == RecordStatus.Active, ct);
            if (!matterExists) return Results.BadRequest(new { message = "Matter does not exist or is inactive." });

            var existingActive = await db.DakMatterLinks.AnyAsync(l => l.DakId == id && l.MatterId == matterId && l.RecordStatus == RecordStatus.Active, ct);
            if (existingActive) return Results.Conflict(new { message = "Matter is already actively linked." });

            var link = new DakMatterLink { DakId = id, MatterId = matterId, RecordStatus = RecordStatus.Active };
            db.DakMatterLinks.Add(link);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/dak/{id}/links/matters/{link.Id}", new { linkId = link.Id, id = link.Id, matterId });
        }

        async Task<IResult> UnlinkMatterInternal(Guid id, Guid targetId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var link = await db.DakMatterLinks.FirstOrDefaultAsync(l => l.DakId == id && (l.Id == targetId || l.MatterId == targetId) && l.RecordStatus == RecordStatus.Active, ct);
            if (link is null) return Results.NotFound();

            link.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        dak.MapPost("/{id:guid}/matters/{matterId:guid}", (Guid id, Guid matterId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkMatterInternal(id, matterId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapPost("/{id:guid}/links/matters", (Guid id, LinkEntityRequest request, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkMatterInternal(id, request.EntityId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/matters/{matterId:guid}", (Guid id, Guid matterId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkMatterInternal(id, matterId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/links/matters/{linkId:guid}", (Guid id, Guid linkId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkMatterInternal(id, linkId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        // Khasra Links
        async Task<IResult> LinkKhasraInternal(Guid id, Guid khasraId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var khasraExists = await db.Khasras.AsNoTracking().AnyAsync(k => k.Id == khasraId && k.RecordStatus == RecordStatus.Active, ct);
            if (!khasraExists) return Results.BadRequest(new { message = "Khasra does not exist or is inactive." });

            var existingActive = await db.DakKhasraLinks.AnyAsync(l => l.DakId == id && l.KhasraId == khasraId && l.RecordStatus == RecordStatus.Active, ct);
            if (existingActive) return Results.Conflict(new { message = "Khasra is already actively linked." });

            var link = new DakKhasraLink { DakId = id, KhasraId = khasraId, RecordStatus = RecordStatus.Active };
            db.DakKhasraLinks.Add(link);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/dak/{id}/links/khasras/{link.Id}", new { linkId = link.Id, id = link.Id, khasraId });
        }

        async Task<IResult> UnlinkKhasraInternal(Guid id, Guid targetId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var link = await db.DakKhasraLinks.FirstOrDefaultAsync(l => l.DakId == id && (l.Id == targetId || l.KhasraId == targetId) && l.RecordStatus == RecordStatus.Active, ct);
            if (link is null) return Results.NotFound();

            link.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        dak.MapPost("/{id:guid}/khasras/{khasraId:guid}", (Guid id, Guid khasraId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkKhasraInternal(id, khasraId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapPost("/{id:guid}/links/khasras", (Guid id, LinkEntityRequest request, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            LinkKhasraInternal(id, request.EntityId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/khasras/{khasraId:guid}", (Guid id, Guid khasraId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkKhasraInternal(id, khasraId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        dak.MapDelete("/{id:guid}/links/khasras/{linkId:guid}", (Guid id, Guid linkId, LacDbContext db, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
            UnlinkKhasraInternal(id, linkId, db, dakAuth, currentUser, ct)).RequirePermission(PermissionCodes.DakEdit);

        // 11. Scoped Document Content Stream
        // Primary main document
        dak.MapGet("/{id:guid}/content", async (
            Guid id,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
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

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
        }).RequirePermission(PermissionCodes.DakView);

        // Attachment document
        dak.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (
            Guid id,
            Guid attachmentId,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
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

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
        }).RequirePermission(PermissionCodes.DakView);

        // Document by document ID
        dak.MapGet("/{id:guid}/documents/{docId:guid}/content", async (
            Guid id,
            Guid docId,
            HttpContext httpContext,
            LacDbContext db,
            IDocumentStorage storage,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
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

            httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
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
    DateTimeOffset CreatedAt
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
    string? UpdatedBy
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
    bool NeedsAttention
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

public sealed record DakLinkItemDto(Guid LinkId, Guid EntityId, string DisplayName, string EntityType);

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
    string? Instructions
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
