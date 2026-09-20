namespace LAC.Api;

using System.IO;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public static class OutwardEndpoints
{
    public static RouteGroupBuilder MapOutwardEndpoints(this RouteGroupBuilder api)
    {
        var outward = api.MapGroup("/outward");

        // ====================================================================
        // 1. REGISTER OUTWARD
        // ====================================================================
        outward.MapPost("/", async (
            HttpRequest request,
            LacDbContext db,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "Request must be multipart/form-data." });

            var form = await request.ReadFormAsync(ct);

            var outwardNumber = form["outwardNumber"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(outwardNumber))
                return Results.BadRequest(new { message = "Outward Number is mandatory." });
            if (outwardNumber.Length > 100)
                return Results.BadRequest(new { message = "Outward Number cannot exceed 100 characters." });

            var outwardDateStr = form["outwardDate"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(outwardDateStr) || !DateOnly.TryParse(outwardDateStr, out var outwardDate))
                return Results.BadRequest(new { message = "Outward Date is required and must be a valid date (YYYY-MM-DD)." });

            var subject = form["subject"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(subject))
                return Results.BadRequest(new { message = "Subject is mandatory." });
            if (subject.Length > 500)
                return Results.BadRequest(new { message = "Subject cannot exceed 500 characters." });

            var recipientName = form["recipientName"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(recipientName))
                return Results.BadRequest(new { message = "Recipient Name is mandatory." });
            if (recipientName.Length > 200)
                return Results.BadRequest(new { message = "Recipient Name cannot exceed 200 characters." });

            var issuingDeskStr = form["issuingDeskId"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(issuingDeskStr) || !Guid.TryParse(issuingDeskStr, out var issuingDeskId))
                return Results.BadRequest(new { message = "Issuing Desk is mandatory and must be a valid GUID." });

            Guid? workstreamId = null;
            var wsStr = form["workstreamId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(wsStr) && Guid.TryParse(wsStr, out var parsedWs))
                workstreamId = parsedWs;

            // Authorize Create
            if (!await outwardAuth.CanCreateAsync(issuingDeskId, workstreamId, userId, ct))
                return Results.Forbid();

            Guid? primaryDakId = null;
            var dakStr = form["primaryDakId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(dakStr) && Guid.TryParse(dakStr, out var parsedDak))
                primaryDakId = parsedDak;

            Guid? matterId = null;
            var matterStr = form["matterId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(matterStr) && Guid.TryParse(matterStr, out var parsedMatter))
                matterId = parsedMatter;

            Guid? existingDocId = null;
            var docStr = form["existingDocumentId"].ToString().Trim();
            if (!string.IsNullOrWhiteSpace(docStr) && Guid.TryParse(docStr, out var parsedDoc))
                existingDocId = parsedDoc;

            var recipientDesignation = form["recipientDesignation"].ToString().Trim();
            var recipientDepartment = form["recipientDepartment"].ToString().Trim();
            var recipientAddress = form["recipientAddress"].ToString().Trim();
            var recipientEmail = form["recipientEmail"].ToString().Trim();
            var recipientPhone = form["recipientPhone"].ToString().Trim();
            var officeRef = form["officeReferenceNumber"].ToString().Trim();
            var remarks = form["remarks"].ToString().Trim();

            var file = form.Files.GetFile("file");

            if (file is not null && existingDocId.HasValue)
                return Results.BadRequest(new { message = "Cannot specify both a new uploaded file and an existing document ID." });

            Stream? stream = null;
            string? fileName = null;
            string? contentType = null;

            if (file is not null)
            {
                if (file.Length > 50 * 1024 * 1024)
                    return Results.BadRequest(new { message = "Attached file exceeds maximum size limit of 50 MB." });

                stream = file.OpenReadStream();
                fileName = file.FileName;
                contentType = file.ContentType;
            }

            var cmd = new RegisterOutwardCommand(
                OutwardNumber: outwardNumber,
                OutwardDate: outwardDate,
                Subject: subject,
                RecipientName: recipientName,
                RecipientDesignation: string.IsNullOrEmpty(recipientDesignation) ? null : recipientDesignation,
                RecipientDepartment: string.IsNullOrEmpty(recipientDepartment) ? null : recipientDepartment,
                RecipientAddress: string.IsNullOrEmpty(recipientAddress) ? null : recipientAddress,
                RecipientEmail: string.IsNullOrEmpty(recipientEmail) ? null : recipientEmail,
                RecipientPhone: string.IsNullOrEmpty(recipientPhone) ? null : recipientPhone,
                IssuingDeskId: issuingDeskId,
                WorkstreamId: workstreamId,
                OfficeReferenceNumber: string.IsNullOrEmpty(officeRef) ? null : officeRef,
                Remarks: string.IsNullOrEmpty(remarks) ? null : remarks,
                PrimaryDakId: primaryDakId,
                MatterId: matterId,
                ExistingDocumentId: existingDocId,
                DocumentStream: stream,
                DocumentFileName: fileName,
                DocumentContentType: contentType
            );

            try
            {
                var created = await workflow.RegisterAsync(cmd, userId, ct);
                return Results.Created($"/api/outward/{created.Id}", new
                {
                    id = created.Id,
                    outwardNumber = created.OutwardNumber,
                    status = created.Status.ToString(),
                    revision = created.Revision
                });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 2. LIST / DIRECTORY
        // ====================================================================
        outward.MapGet("/", async (
            string? q,
            string? status,
            string? dispatchMode,
            Guid? issuingDeskId,
            Guid? workstreamId,
            Guid? dakId,
            Guid? matterId,
            string? fromDate,
            string? toDate,
            int page,
            int pageSize,
            LacDbContext db,
            IOutwardAuthorizationService outwardAuth,
            IDakAuthorizationService dakAuth,
            IMatterAuthorizationService matterAuth,
            IAccessControlService accessControl,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (page < 0) page = 0;
            if (pageSize <= 0 || pageSize > 100) pageSize = 20;

            var baseQuery = db.Outwards
                .AsNoTracking()
                .Where(o => o.RecordStatus == RecordStatus.Active);

            var authResult = await outwardAuth.AuthorizeListQueryAsync(baseQuery, userId, ct);
            if (!authResult.HasPermission) return Results.Forbid();

            var query = authResult.Query;

            // Context Filters with strict authorization check to prevent indirect information disclosure
            if (dakId.HasValue)
            {
                var canAccessDak = await dakAuth.CanAccessDakAsync(dakId.Value, PermissionCodes.DakView, userId, ct);
                if (!canAccessDak)
                {
                    // Fail closed without revealing whether matching outward records exist
                    return Results.Ok(new
                    {
                        items = Array.Empty<object>(),
                        totalCount = 0,
                        page,
                        pageSize
                    });
                }

                query = query.Where(o => o.DakLinks.Any(l => l.DakId == dakId.Value && l.RecordStatus == RecordStatus.Active));
            }

            if (matterId.HasValue)
            {
                var canViewMatter = await matterAuth.CanAccessMatterAsync(matterId.Value, PermissionCodes.MatterView, userId, ct);
                if (!canViewMatter)
                {
                    return Results.Ok(new
                    {
                        items = Array.Empty<object>(),
                        totalCount = 0,
                        page,
                        pageSize
                    });
                }

                query = query.Where(o => o.MatterId == matterId.Value);
            }

            // Text search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLowerInvariant();
                query = query.Where(o =>
                    o.OutwardNumber.ToLower().Contains(term)
                    || o.Subject.ToLower().Contains(term)
                    || o.RecipientName.ToLower().Contains(term)
                    || (o.OfficeReferenceNumber != null && o.OfficeReferenceNumber.ToLower().Contains(term))
                    || (o.DispatchReferenceNumber != null && o.DispatchReferenceNumber.ToLower().Contains(term)));
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(status) && status.Trim().ToLowerInvariant() != "all")
            {
                var s = status.Trim().ToLowerInvariant();
                if (s == "registered") query = query.Where(o => o.Status == OutwardStatus.Registered);
                else if (s == "dispatched") query = query.Where(o => o.Status == OutwardStatus.Dispatched);
                else if (s == "cancelled") query = query.Where(o => o.Status == OutwardStatus.Cancelled);
                else return Results.BadRequest(new { message = "Invalid status filter. Allowed values: all, registered, dispatched, cancelled." });
            }

            // Dispatch Mode filter
            if (!string.IsNullOrWhiteSpace(dispatchMode) && dispatchMode.Trim().ToLowerInvariant() != "all")
            {
                var mode = dispatchMode.Trim().ToLowerInvariant();
                query = query.Where(o => o.DispatchMode != null && o.DispatchMode.ToLower() == mode);
            }

            // Desk and Workstream filters
            if (issuingDeskId.HasValue)
                query = query.Where(o => o.IssuingDeskId == issuingDeskId.Value);

            if (workstreamId.HasValue)
                query = query.Where(o => o.WorkstreamId == workstreamId.Value);

            // Date Range filters
            if (!string.IsNullOrWhiteSpace(fromDate) && DateOnly.TryParse(fromDate, out var parsedFrom))
                query = query.Where(o => o.OutwardDate >= parsedFrom);

            if (!string.IsNullOrWhiteSpace(toDate) && DateOnly.TryParse(toDate, out var parsedTo))
                query = query.Where(o => o.OutwardDate <= parsedTo);

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(o => o.OutwardDate)
                .ThenByDescending(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(o => new
                {
                    o.Id,
                    o.OutwardNumber,
                    o.OutwardDate,
                    o.Subject,
                    o.RecipientName,
                    o.RecipientDesignation,
                    o.RecipientDepartment,
                    o.IssuingDeskId,
                    IssuingDeskName = o.IssuingDesk.Name,
                    IssuingDeskCode = o.IssuingDesk.Code,
                    o.WorkstreamId,
                    WorkstreamName = o.Workstream != null ? o.Workstream.Name : null,
                    Status = o.Status.ToString(),
                    o.DispatchDate,
                    o.DispatchMode,
                    o.DispatchReferenceNumber,
                    HasMainDocument = o.MainDocumentId != null,
                    AttachmentsCount = o.Attachments.Count(a => a.RecordStatus == RecordStatus.Active),
                    DakLinksCount = o.DakLinks.Count(l => l.RecordStatus == RecordStatus.Active),
                    o.Revision
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                totalCount,
                page,
                pageSize
            });
        });

        // ====================================================================
        // 3. GET DETAILS BY ID
        // ====================================================================
        outward.MapGet("/{id:guid}", async (
            Guid id,
            LacDbContext db,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardView, userId, ct))
                return Results.Forbid();

            var outwardRecord = await db.Outwards
                .AsNoTracking()
                .Include(o => o.IssuingDesk)
                .Include(o => o.Workstream)
                .Include(o => o.MainDocument)
                .Include(o => o.DispatchedByUser)
                .Include(o => o.CancelledByUser)
                .Include(o => o.Matter)
                .Include(o => o.Attachments.Where(a => a.RecordStatus == RecordStatus.Active))
                    .ThenInclude(a => a.Document)
                .Include(o => o.DakLinks.Where(l => l.RecordStatus == RecordStatus.Active))
                    .ThenInclude(l => l.Dak)
                .Include(o => o.Events)
                    .ThenInclude(e => e.ActionByUser)
                .FirstOrDefaultAsync(o => o.Id == id && o.RecordStatus == RecordStatus.Active, ct);

            if (outwardRecord is null)
                return Results.NotFound(new { message = "Outward record not found." });

            return Results.Ok(new
            {
                outwardRecord.Id,
                outwardRecord.OutwardNumber,
                outwardRecord.OutwardDate,
                outwardRecord.Subject,
                outwardRecord.RecipientName,
                outwardRecord.RecipientDesignation,
                outwardRecord.RecipientDepartment,
                outwardRecord.RecipientAddress,
                outwardRecord.RecipientEmail,
                outwardRecord.RecipientPhone,
                outwardRecord.IssuingDeskId,
                IssuingDeskName = outwardRecord.IssuingDesk.Name,
                IssuingDeskCode = outwardRecord.IssuingDesk.Code,
                outwardRecord.WorkstreamId,
                WorkstreamName = outwardRecord.Workstream?.Name,
                outwardRecord.OfficeReferenceNumber,
                outwardRecord.Remarks,
                Status = outwardRecord.Status.ToString(),
                outwardRecord.Revision,
                outwardRecord.MainDocumentId,
                MainDocument = outwardRecord.MainDocument != null ? new
                {
                    id = outwardRecord.MainDocument.Id,
                    fileName = outwardRecord.MainDocument.OriginalFileName,
                    fileSize = outwardRecord.MainDocument.FileSize,
                    mimeType = outwardRecord.MainDocument.MimeType,
                    uploadedAt = outwardRecord.MainDocument.UploadedAt
                } : null,
                outwardRecord.DispatchDate,
                outwardRecord.DispatchMode,
                outwardRecord.DispatchReferenceNumber,
                outwardRecord.DispatchedAt,
                DispatchedBy = outwardRecord.DispatchedByUser?.DisplayName,
                outwardRecord.CancellationReason,
                outwardRecord.CancelledAt,
                CancelledBy = outwardRecord.CancelledByUser?.DisplayName,
                outwardRecord.MatterId,
                MatterTitle = outwardRecord.Matter?.Title,
                outwardRecord.CreatedAt,
                outwardRecord.CreatedBy,
                outwardRecord.UpdatedAt,
                outwardRecord.UpdatedBy,
                Attachments = outwardRecord.Attachments
                    .OrderBy(a => a.SequenceOrder)
                    .Select(a => new
                    {
                        id = a.Id,
                        documentId = a.DocumentId,
                        title = a.Title,
                        attachmentType = a.AttachmentType,
                        sequenceOrder = a.SequenceOrder,
                        fileName = a.Document.OriginalFileName,
                        fileSize = a.Document.FileSize,
                        mimeType = a.Document.MimeType
                    }),
                DakLinks = outwardRecord.DakLinks
                    .OrderByDescending(l => l.IsPrimary)
                    .ThenBy(l => l.CreatedAt)
                    .Select(l => new
                    {
                        id = l.Id,
                        dakId = l.DakId,
                        diaryNumber = l.Dak.DiaryNumber,
                        subject = l.Dak.Subject,
                        senderName = l.Dak.SenderName,
                        isPrimary = l.IsPrimary,
                        relationshipType = l.RelationshipType
                    }),
                Events = outwardRecord.Events
                    .OrderBy(e => e.SequenceNumber)
                    .Select(e => new
                    {
                        id = e.Id,
                        sequenceNumber = e.SequenceNumber,
                        action = e.Action.ToString(),
                        actionByUserId = e.ActionByUserId,
                        actionBy = e.ActionByDisplayNameSnapshot,
                        actionAt = e.ActionAt,
                        documentId = e.DocumentId,
                        attachmentId = e.AttachmentId,
                        dakId = e.DakId,
                        dispatchDate = e.DispatchDate,
                        dispatchMode = e.DispatchMode,
                        dispatchReferenceNumber = e.DispatchReferenceNumber,
                        cancellationReason = e.CancellationReason
                    })
            });
        });

        // ====================================================================
        // 4. OPERATIONAL LOOKUPS
        // ====================================================================
        outward.MapGet("/lookups/directory", async (
            LacDbContext db,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var baseQuery = db.Outwards.AsNoTracking().Where(o => o.RecordStatus == RecordStatus.Active);
            var authResult = await outwardAuth.AuthorizeListQueryAsync(baseQuery, userId, ct);
            if (!authResult.HasPermission) return Results.Forbid();

            var accessibleDeskIds = await authResult.Query.Select(o => o.IssuingDeskId).Distinct().ToListAsync(ct);
            var accessibleWorkstreamIds = await authResult.Query.Where(o => o.WorkstreamId.HasValue).Select(o => o.WorkstreamId!.Value).Distinct().ToListAsync(ct);

            var desks = await db.OfficeDesks.AsNoTracking()
                .Where(d => accessibleDeskIds.Contains(d.Id))
                .OrderBy(d => d.Name)
                .Select(d => new { id = d.Id, name = d.Name, code = d.Code })
                .ToListAsync(ct);

            var workstreams = await db.Workstreams.AsNoTracking()
                .Where(w => accessibleWorkstreamIds.Contains(w.Id))
                .OrderBy(w => w.Name)
                .Select(w => new { id = w.Id, name = w.Name, code = w.Code })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                desks,
                workstreams,
                dispatchModes = new[] { "SpeedPost", "RegisteredPost", "ByHand", "SpecialMessenger", "Courier", "Email", "Other" }
            });
        });

        async Task<IResult> RegistrationLookupsHandler(
            LacDbContext db,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessRegistrationLookupsAsync(userId, ct))
                return Results.Forbid();

            // Only desks where user holds an active live membership can be used for creation
            var desks = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.RemovedAt == null
                         && m.RecordStatus == RecordStatus.Active
                         && m.OfficeDesk.IsActive
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.OfficeDesk.Name)
                .Select(m => new
                {
                    id = m.OfficeDeskId,
                    name = m.OfficeDesk.Name,
                    code = m.OfficeDesk.Code,
                    isPrimary = m.IsPrimary
                })
                .ToListAsync(ct);

            var createScopes = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                join rp in db.RolePermissions on r.Id equals rp.RoleId
                join p in db.Permissions on rp.PermissionId equals p.Id
                where ur.UserId == userId
                   && r.IsActive && r.RecordStatus == RecordStatus.Active
                   && p.Code == PermissionCodes.OutwardCreate
                select rp.ScopeMode
            ).Distinct().ToListAsync(ct);

            List<object> workstreams;
            if (createScopes.Contains(ScopeMode.All) || createScopes.Contains(ScopeMode.Assigned))
            {
                workstreams = await db.Workstreams.AsNoTracking()
                    .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                    .OrderBy(w => w.Name)
                    .Select(w => (object)new { id = w.Id, name = w.Name, code = w.Code })
                    .ToListAsync(ct);
            }
            else
            {
                workstreams = await db.UserWorkstreamMemberships.AsNoTracking()
                    .Where(m => m.UserId == userId
                             && m.IsActive
                             && m.Workstream.IsActive
                             && m.Workstream.RecordStatus == RecordStatus.Active)
                    .OrderBy(m => m.Workstream.Name)
                    .Select(m => (object)new { id = m.WorkstreamId, name = m.Workstream.Name, code = m.Workstream.Code })
                    .ToListAsync(ct);
            }

            return Results.Ok(new
            {
                desks,
                workstreams,
                dispatchModes = new[] { "SpeedPost", "RegisteredPost", "ByHand", "SpecialMessenger", "Courier", "Email", "Other" }
            });
        }

        outward.MapGet("/lookups/registration", RegistrationLookupsHandler);
        outward.MapGet("/context", RegistrationLookupsHandler);

        // ====================================================================
        // 5. UPDATE METADATA
        // ====================================================================
        outward.MapPut("/{id:guid}", async (
            Guid id,
            UpdateOutwardMetadataRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            if (!await outwardAuth.CanUseEditContextAsync(request.IssuingDeskId, request.WorkstreamId, userId, ct))
                return Results.Forbid();

            var cmd = new UpdateOutwardMetadataCommand(
                Subject: request.Subject,
                RecipientName: request.RecipientName,
                RecipientDesignation: request.RecipientDesignation,
                RecipientDepartment: request.RecipientDepartment,
                RecipientAddress: request.RecipientAddress,
                RecipientEmail: request.RecipientEmail,
                RecipientPhone: request.RecipientPhone,
                IssuingDeskId: request.IssuingDeskId,
                WorkstreamId: request.WorkstreamId,
                OfficeReferenceNumber: request.OfficeReferenceNumber,
                Remarks: request.Remarks,
                ExpectedRevision: request.ExpectedRevision,
                MatterId: request.MatterId
            );

            try
            {
                var updated = await workflow.UpdateMetadataAsync(id, cmd, userId, ct);
                return Results.Ok(new { id = updated.Id, revision = updated.Revision });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 6. MAIN DOCUMENT ATTACH / REPLACE
        // ====================================================================
        async Task<IResult> ChangeMainDocumentHandler(
            Guid id,
            HttpRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct)
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            Guid? existingDocId = null;
            int expectedRevision = 0;
            Stream? stream = null;
            string? fileName = null;
            string? contentType = null;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                if (int.TryParse(form["expectedRevision"], out var expRev))
                    expectedRevision = expRev;

                var docStr = form["existingDocumentId"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(docStr) && Guid.TryParse(docStr, out var parsedDoc))
                    existingDocId = parsedDoc;

                var file = form.Files.GetFile("file");
                if (file is not null)
                {
                    stream = file.OpenReadStream();
                    fileName = file.FileName;
                    contentType = file.ContentType;
                }
            }
            else
            {
                var body = await request.ReadFromJsonAsync<ChangeMainDocumentJsonRequest>(cancellationToken: ct);
                if (body is not null)
                {
                    existingDocId = body.ExistingDocumentId;
                    expectedRevision = body.ExpectedRevision;
                }
            }

            var cmd = new ChangeMainDocumentCommand(
                ExistingDocumentId: existingDocId,
                DocumentStream: stream,
                DocumentFileName: fileName,
                DocumentContentType: contentType,
                ExpectedRevision: expectedRevision
            );

            try
            {
                var updated = await workflow.ChangeMainDocumentAsync(id, cmd, userId, ct);
                return Results.Ok(new { id = updated.Id, mainDocumentId = updated.MainDocumentId, revision = updated.Revision });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        }

        outward.MapPut("/{id:guid}/document", ChangeMainDocumentHandler);
        outward.MapPost("/{id:guid}/document", ChangeMainDocumentHandler);
        outward.MapPost("/{id:guid}/main-document", ChangeMainDocumentHandler);

        // ====================================================================
        // 7. ATTACHMENTS (ADD & REMOVE)
        // ====================================================================
        outward.MapPost("/{id:guid}/attachments", async (
            Guid id,
            HttpRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            string title;
            string attachmentType;
            int sequenceOrder = 0;
            int expectedRevision = 0;
            Guid? existingDocId = null;
            Stream? stream = null;
            string? fileName = null;
            string? contentType = null;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                title = form["title"].ToString().Trim();
                attachmentType = form["attachmentType"].ToString().Trim();
                int.TryParse(form["sequenceOrder"], out sequenceOrder);
                int.TryParse(form["expectedRevision"], out expectedRevision);

                var docStr = form["existingDocumentId"].ToString().Trim();
                if (!string.IsNullOrWhiteSpace(docStr) && Guid.TryParse(docStr, out var parsedDoc))
                    existingDocId = parsedDoc;

                var file = form.Files.GetFile("file");
                if (file is not null)
                {
                    stream = file.OpenReadStream();
                    fileName = file.FileName;
                    contentType = file.ContentType;
                }
            }
            else
            {
                var body = await request.ReadFromJsonAsync<AddAttachmentJsonRequest>(cancellationToken: ct);
                if (body is null)
                    return Results.BadRequest(new { message = "Invalid request payload." });

                title = body.Title ?? "";
                attachmentType = body.AttachmentType ?? "Enclosure";
                sequenceOrder = body.SequenceOrder;
                expectedRevision = body.ExpectedRevision;
                existingDocId = body.ExistingDocumentId;
            }

            var cmd = new AddAttachmentCommand(
                Title: title,
                AttachmentType: string.IsNullOrEmpty(attachmentType) ? "Enclosure" : attachmentType,
                SequenceOrder: sequenceOrder,
                ExistingDocumentId: existingDocId,
                DocumentStream: stream,
                DocumentFileName: fileName,
                DocumentContentType: contentType,
                ExpectedRevision: expectedRevision
            );

            try
            {
                var att = await workflow.AddAttachmentAsync(id, cmd, userId, ct);
                return Results.Created($"/api/outward/{id}/attachments/{att.Id}", new
                {
                    id = att.Id,
                    documentId = att.DocumentId,
                    title = att.Title
                });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        outward.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            int? expectedRevision,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            try
            {
                await workflow.RemoveAttachmentAsync(id, attachmentId, new RemoveAttachmentCommand(expectedRevision ?? 0), userId, ct);
                return Results.Ok();
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 8. DAK LINKS (ADD & REMOVE)
        // ====================================================================
        outward.MapPost("/{id:guid}/dak-links", async (
            Guid id,
            AddDakLinkRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            var cmd = new AddDakCommandWrapper(
                DakId: request.DakId,
                IsPrimary: request.IsPrimary,
                RelationshipType: request.RelationshipType,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                var link = await workflow.AddDakLinkAsync(id, cmd, userId, ct);
                return Results.Created($"/api/outward/{id}/dak-links/{link.Id}", new
                {
                    id = link.Id,
                    dakId = link.DakId,
                    isPrimary = link.IsPrimary,
                    relationshipType = link.RelationshipType
                });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        outward.MapDelete("/{id:guid}/dak-links/{linkId:guid}", async (
            Guid id,
            Guid linkId,
            int? expectedRevision,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardEdit, userId, ct))
                return Results.Forbid();

            try
            {
                await workflow.RemoveDakLinkAsync(id, linkId, new RemoveDakLinkCommand(expectedRevision ?? 0), userId, ct);
                return Results.Ok();
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 9. DISPATCH
        // ====================================================================
        outward.MapPost("/{id:guid}/dispatch", async (
            Guid id,
            DispatchOutwardRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardDispatch, userId, ct))
                return Results.Forbid();

            var cmd = new DispatchOutwardCommand(
                DispatchDate: request.DispatchDate,
                DispatchMode: request.DispatchMode,
                DispatchReferenceNumber: request.DispatchReferenceNumber,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                var dispatched = await workflow.DispatchAsync(id, cmd, userId, ct);
                return Results.Ok(new
                {
                    id = dispatched.Id,
                    status = dispatched.Status.ToString(),
                    dispatchDate = dispatched.DispatchDate,
                    dispatchMode = dispatched.DispatchMode,
                    revision = dispatched.Revision
                });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 10. CANCEL
        // ====================================================================
        outward.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelOutwardRequest request,
            OutwardWorkflowService workflow,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardCancel, userId, ct))
                return Results.Forbid();

            var cmd = new CancelOutwardCommand(
                Reason: request.EffectiveReason,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                var cancelled = await workflow.CancelAsync(id, cmd, userId, ct);
                return Results.Ok(new
                {
                    id = cancelled.Id,
                    status = cancelled.Status.ToString(),
                    cancellationReason = cancelled.CancellationReason,
                    revision = cancelled.Revision
                });
            }
            catch (OutwardWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 11. STREAM DOCUMENT CONTENT
        // ====================================================================
        outward.MapGet("/{id:guid}/document/content", async (
            Guid id,
            LacDbContext db,
            IDocumentStorage storage,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardView, userId, ct))
                return Results.Forbid();

            var outwardRecord = await db.Outwards.AsNoTracking()
                .Include(o => o.MainDocument)
                .FirstOrDefaultAsync(o => o.Id == id && o.RecordStatus == RecordStatus.Active, ct);

            if (outwardRecord?.MainDocument is null)
                return Results.NotFound(new { message = "Main document not found." });

            var doc = outwardRecord.MainDocument;
            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound(new { message = "Document file not found on storage volume." });

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: RecordAccessAction.Opened,
                DocumentId: doc.Id,
                ContextEntityType: "Outward",
                ContextEntityId: id,
                DocumentTitleSnapshot: doc.OriginalFileName
            ), ct);

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.Stream(stream, doc.MimeType ?? "application/pdf", doc.OriginalFileName, enableRangeProcessing: true);
        });

        outward.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (
            Guid id,
            Guid attachmentId,
            LacDbContext db,
            IDocumentStorage storage,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessOutwardAsync(id, PermissionCodes.OutwardView, userId, ct))
                return Results.Forbid();

            var attachment = await db.OutwardAttachments.AsNoTracking()
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OutwardId == id && a.RecordStatus == RecordStatus.Active, ct);

            if (attachment?.Document is null)
                return Results.NotFound(new { message = "Attachment document not found." });

            var doc = attachment.Document;
            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound(new { message = "Document file not found on storage volume." });

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: RecordAccessAction.Opened,
                DocumentId: doc.Id,
                ContextEntityType: "Outward",
                ContextEntityId: id,
                DocumentTitleSnapshot: doc.OriginalFileName
            ), ct);

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.Stream(stream, doc.MimeType ?? "application/pdf", doc.OriginalFileName, enableRangeProcessing: true);
        });

        outward.MapGet("/{id:guid}/documents/{documentId:guid}", async (
            Guid id,
            Guid documentId,
            LacDbContext db,
            IDocumentStorage storage,
            IOutwardAuthorizationService outwardAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await outwardAuth.CanAccessDocumentAsync(id, documentId, userId, ct))
                return Results.Forbid();

            var doc = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId && d.RecordStatus == RecordStatus.Active, ct);

            if (doc is null) return Results.NotFound(new { message = "Document record not found." });

            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound(new { message = "Document file not found on storage volume." });

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: RecordAccessAction.Opened,
                DocumentId: doc.Id,
                ContextEntityType: "Outward",
                ContextEntityId: id,
                DocumentTitleSnapshot: doc.OriginalFileName
            ), ct);

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.Stream(stream, doc.MimeType ?? "application/octet-stream", doc.OriginalFileName, enableRangeProcessing: true);
        });

        return outward;
    }
}

public sealed record UpdateOutwardMetadataRequest(
    string Subject,
    string RecipientName,
    string? RecipientDesignation,
    string? RecipientDepartment,
    string? RecipientAddress,
    string? RecipientEmail,
    string? RecipientPhone,
    Guid IssuingDeskId,
    Guid? WorkstreamId,
    string? OfficeReferenceNumber,
    string? Remarks,
    int ExpectedRevision,
    Guid? MatterId = null
);

public sealed record ChangeMainDocumentJsonRequest(
    Guid? ExistingDocumentId,
    int ExpectedRevision
);

public sealed record AddAttachmentJsonRequest(
    string? Title,
    string? AttachmentType,
    int SequenceOrder,
    Guid? ExistingDocumentId,
    int ExpectedRevision
);

public sealed record RemoveAttachmentRequest(
    int ExpectedRevision
);

public sealed record AddDakLinkRequest(
    Guid DakId,
    bool IsPrimary,
    string RelationshipType,
    int ExpectedRevision
);

public sealed record RemoveDakLinkRequest(
    int ExpectedRevision
);

public sealed record DispatchOutwardRequest(
    DateOnly DispatchDate,
    string DispatchMode,
    string? DispatchReferenceNumber,
    int ExpectedRevision
);

public sealed record CancelOutwardRequest(
    string? CancellationReason,
    string? Reason,
    int ExpectedRevision
)
{
    public string EffectiveReason => !string.IsNullOrWhiteSpace(CancellationReason) ? CancellationReason : (Reason ?? "");
};
