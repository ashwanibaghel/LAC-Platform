namespace LAC.Api;

using System.IO;
using System.IO.Compression;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public static class MatterEndpoints
{
    public static RouteGroupBuilder MapMatterEndpoints(this RouteGroupBuilder api)
    {
        var matters = api.MapGroup("/matters");

        // ====================================================================
        // 1. DIRECTORY / LIST MATTERS
        // ====================================================================
        matters.MapGet("/", async (
            int? page,
            int? pageSize,
            string? q,
            Guid? workstreamId,
            string? workstream,
            string? status,
            Guid? villageId,
            string? sortBy,
            bool? sortDesc,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var baseQuery = db.Matters.AsNoTracking().Where(m => m.RecordStatus == RecordStatus.Active);
            var auth = await matterAuth.AuthorizeListQueryAsync(baseQuery, PermissionCodes.MatterView, userId, false, ct);
            if (!auth.HasPermission)
            {
                return Results.Forbid();
            }

            var query = auth.Query;

            // Explicit workstream filter
            if (workstreamId.HasValue)
            {
                query = query.Where(m => m.WorkstreamId == workstreamId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(workstream))
            {
                var wsFilter = workstream.Trim().ToUpperInvariant();
                if (wsFilter == "UNCLASSIFIED")
                {
                    query = query.Where(m => m.WorkstreamId == null);
                }
                else
                {
                    query = query.Where(m => m.Workstream != null && (m.Workstream.Code.ToUpper() == wsFilter || m.Workstream.Name.ToUpper() == wsFilter));
                }
            }

            // Village filter
            if (villageId.HasValue)
            {
                query = query.Where(m => m.VillageId == villageId.Value);
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                var s = status.Trim();
                query = query.Where(m => m.Status == s);
            }

            // Search query
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(m =>
                    m.Title.ToLower().Contains(term)
                    || (m.ReferenceNumber != null && m.ReferenceNumber.ToLower().Contains(term))
                    || (m.KhasraReferenceText != null && m.KhasraReferenceText.ToLower().Contains(term))
                    || m.Village.Name.ToLower().Contains(term));
            }

            // Sorting
            var desc = sortDesc ?? false;
            query = sortBy?.ToLowerInvariant() switch
            {
                "title" => desc ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title),
                "referencenumber" => desc ? query.OrderByDescending(m => m.ReferenceNumber) : query.OrderBy(m => m.ReferenceNumber),
                "status" => desc ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
                "updatedat" => desc ? query.OrderByDescending(m => m.UpdatedAt) : query.OrderBy(m => m.UpdatedAt),
                "village" => desc ? query.OrderByDescending(m => m.Village.Name) : query.OrderBy(m => m.Village.Name),
                _ => desc ? query.OrderBy(m => m.CreatedAt) : query.OrderByDescending(m => m.CreatedAt)
            };

            var currentPage = (page.HasValue && page.Value > 0) ? page.Value : 1;
            var currentPageSize = (pageSize.HasValue && pageSize.Value > 0 && pageSize.Value <= 200) ? pageSize.Value : 20;

            var totalCount = await query.CountAsync(ct);
            var items = await query
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .Select(m => new
                {
                    m.Id,
                    m.VillageId,
                    villageName = m.Village.Name,
                    m.WorkstreamId,
                    workstreamName = m.Workstream != null ? m.Workstream.Name : null,
                    workstreamCode = m.Workstream != null ? m.Workstream.Code : null,
                    isUnclassified = m.WorkstreamId == null,
                    m.Title,
                    m.MatterType,
                    m.Status,
                    m.ReferenceNumber,
                    m.Remarks,
                    m.KhasraReferenceText,
                    m.Revision,
                    m.CreatedAt,
                    m.UpdatedAt,
                    award = m.AwardLinks.Where(a => a.IsPrimary).Select(a => new { a.AwardId, a.Award.AwardNumber }).FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                totalCount,
                page = currentPage,
                pageSize = currentPageSize
            });
        });

        // ====================================================================
        // 2. CREATE MATTER
        // ====================================================================
        matters.MapPost("/", async (
            CreateMatterApiRequest request,
            LacDbContext db,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (request.VillageId == Guid.Empty)
                return Results.BadRequest(new { message = "Village is mandatory." });
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Matter title is mandatory." });
            if (!request.WorkstreamId.HasValue || request.WorkstreamId.Value == Guid.Empty)
                return Results.BadRequest(new { message = "Workstream is mandatory for new matters." });

            var cmd = new CreateMatterCommand(
                VillageId: request.VillageId,
                Title: request.Title,
                MatterType: !string.IsNullOrWhiteSpace(request.MatterType) ? request.MatterType.Trim() : "Other",
                WorkstreamId: request.WorkstreamId.Value,
                ReferenceNumber: request.ReferenceNumber,
                Remarks: request.Remarks,
                KhasraReferenceText: request.KhasraReferenceText,
                AwardId: request.AwardId
            );

            try
            {
                var matter = await workflow.CreateMatterAsync(cmd, userId, ct);
                return Results.Created($"/api/matters/{matter.Id}", new { id = matter.Id });
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 3. GET MATTER DETAILS
        // ====================================================================
        matters.MapGet("/{id:guid}", async (
            Guid id,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterAsync(id, PermissionCodes.MatterView, userId, ct))
                return Results.Forbid();

            var matter = await db.Matters.AsNoTracking()
                .Where(x => x.Id == id && x.RecordStatus == RecordStatus.Active)
                .Select(x => new
                {
                    x.Id,
                    x.VillageId,
                    villageName = x.Village.Name,
                    x.WorkstreamId,
                    workstreamName = x.Workstream != null ? x.Workstream.Name : null,
                    workstreamCode = x.Workstream != null ? x.Workstream.Code : null,
                    isUnclassified = x.WorkstreamId == null,
                    x.Title,
                    x.MatterType,
                    x.Status,
                    x.ReferenceNumber,
                    x.Remarks,
                    x.KhasraReferenceText,
                    x.Revision,
                    x.CreatedAt,
                    x.UpdatedAt,
                    award = x.AwardLinks.Where(a => a.IsPrimary).Select(a => new
                    {
                        a.AwardId,
                        a.Award.AwardNumber
                    }).FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            return matter is null ? Results.NotFound(new { message = "Matter not found." }) : Results.Ok(matter);
        });

        // ====================================================================
        // 4. UPDATE METADATA
        // ====================================================================
        matters.MapPut("/{id:guid}", async (
            Guid id,
            UpdateMatterMetadataApiRequest request,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Title is mandatory." });

            var cmd = new UpdateMatterMetadataCommand(
                Title: request.Title,
                MatterType: !string.IsNullOrWhiteSpace(request.MatterType) ? request.MatterType.Trim() : "Other",
                ReferenceNumber: request.ReferenceNumber,
                Remarks: request.Remarks,
                KhasraReferenceText: request.KhasraReferenceText,
                ExpectedRevision: request.ExpectedRevision,
                Status: request.Status
            );

            try
            {
                var updated = await workflow.UpdateMetadataAsync(id, cmd, userId, ct);
                return Results.Ok(new
                {
                    id = updated.Id,
                    title = updated.Title,
                    matterType = updated.MatterType,
                    status = updated.Status,
                    referenceNumber = updated.ReferenceNumber,
                    remarks = updated.Remarks,
                    khasraReferenceText = updated.KhasraReferenceText,
                    revision = updated.Revision,
                    updatedAt = updated.UpdatedAt
                });
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 5. RECLASSIFY WORKSTREAM
        // ====================================================================
        matters.MapPost("/{id:guid}/reclassify", async (
            Guid id,
            ReclassifyMatterApiRequest request,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (request.TargetWorkstreamId == Guid.Empty)
                return Results.BadRequest(new { message = "Target Workstream ID is mandatory." });

            var cmd = new ReclassifyWorkstreamCommand(
                TargetWorkstreamId: request.TargetWorkstreamId,
                Reason: request.Reason,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                var reclassified = await workflow.ReclassifyWorkstreamAsync(id, cmd, userId, ct);
                return Results.Ok(new
                {
                    id = reclassified.Id,
                    workstreamId = reclassified.WorkstreamId,
                    revision = reclassified.Revision
                });
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 6. ARCHIVE MATTER
        // ====================================================================
        matters.MapPost("/{id:guid}/archive", async (
            Guid id,
            ArchiveMatterApiRequest request,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var cmd = new ArchiveMatterCommand(
                Reason: request.Reason,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                var archived = await workflow.ArchiveAsync(id, cmd, userId, ct);
                return Results.Ok(new
                {
                    id = archived.Id,
                    status = archived.Status,
                    revision = archived.Revision
                });
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 7. MATTER DRAFTS
        // ====================================================================
        matters.MapGet("/{id:guid}/drafts", async (
            Guid id,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterDraftCapabilityAsync(id, PermissionCodes.DraftView, userId, ct))
                return Results.Forbid();

            var exists = await db.Matters.AsNoTracking().AnyAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (!exists) return Results.NotFound(new { message = "Matter not found." });

            var drafts = await db.MatterDrafts.AsNoTracking()
                .Where(x => x.MatterId == id && x.RecordStatus == RecordStatus.Active)
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    draftType = x.DraftType.ToString(),
                    status = x.Status.ToString(),
                    x.Revision,
                    x.UpdatedAt
                })
                .ToListAsync(ct);

            return Results.Ok(drafts);
        });

        matters.MapPost("/{id:guid}/drafts", async (
            Guid id,
            CreateMatterDraftRequest request,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterDraftCapabilityAsync(id, PermissionCodes.DraftCreate, userId, ct))
                return Results.Forbid();

            var matter = await db.Matters.FirstOrDefaultAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (matter is null) return Results.NotFound(new { message = "Matter not found." });

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Draft title is required." });

            if (!Enum.TryParse<MatterDraftType>(request.DraftType, true, out var draftType))
                return Results.BadRequest(new { message = "Choose Letter or Noting." });

            var draft = new MatterDraft
            {
                MatterId = id,
                Title = request.Title.Trim(),
                DraftType = draftType,
                Status = MatterDraftStatus.Draft,
                Revision = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (draftType == MatterDraftType.Noting)
            {
                MatterDraftLayoutProfiles.ApplyDraftLayout(draft, MatterDraftLayoutProfiles.NotingSheetV1Provisional);
            }

            db.MatterDrafts.Add(draft);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/matter-drafts/{draft.Id}", new { id = draft.Id });
        });

        // ====================================================================
        // 8. MATTER DOCUMENTS (LIST, UPLOAD, ELIGIBLE, LINK, EXPORT)
        // ====================================================================
        matters.MapGet("/{id:guid}/documents", async (
            Guid id,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterAsync(id, PermissionCodes.MatterView, userId, ct))
                return Results.Forbid();

            var exists = await db.Matters.AsNoTracking().AnyAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (!exists) return Results.NotFound(new { message = "Matter not found." });

            var docs = await db.MatterDocuments.AsNoTracking()
                .Where(x => x.MatterId == id && x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active")
                .OrderByDescending(x => x.Document.UploadedAt)
                .Select(x => new
                {
                    x.Id,
                    x.DocumentId,
                    x.DocumentRole,
                    x.DisplayName,
                    x.Document.OriginalFileName,
                    x.Document.MimeType,
                    x.Document.FileSize,
                    x.Document.UploadedAt
                })
                .ToListAsync(ct);

            return Results.Ok(docs);
        });

        matters.MapPost("/{id:guid}/documents", async (
            Guid id,
            HttpRequest request,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "Request must be multipart/form-data." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "Choose a non-empty document file." });

            const long maxFileSize = 50 * 1024 * 1024;
            if (file.Length > maxFileSize)
                return Results.BadRequest(new { message = "Document exceeds the 50 MB upload limit." });

            var role = form["role"].ToString().Trim();
            var displayName = form["displayName"].ToString().Trim();
            int.TryParse(form["expectedRevision"], out var expectedRevision);

            await using var stream = file.OpenReadStream();
            var cmd = new UploadMatterDocumentCommand(
                DocumentStream: stream,
                DocumentFileName: file.FileName,
                DocumentContentType: file.ContentType,
                DocumentRole: !string.IsNullOrWhiteSpace(role) ? role : null,
                DisplayName: !string.IsNullOrWhiteSpace(displayName) ? displayName : null,
                ExpectedRevision: expectedRevision
            );

            try
            {
                var matterDoc = await workflow.UploadDocumentAsync(id, cmd, userId, ct);
                return Results.Created($"/api/matters/{id}/documents/{matterDoc.DocumentId}", new
                {
                    id = matterDoc.Id,
                    documentId = matterDoc.DocumentId,
                    role = matterDoc.DocumentRole,
                    displayName = matterDoc.DisplayName
                });
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        matters.MapGet("/{id:guid}/eligible-documents", async (
            Guid id,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            IAccessControlService accessControl,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterAsync(id, PermissionCodes.MatterDocumentManage, userId, ct))
                return Results.Forbid();

            var matter = await db.Matters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (matter is null) return Results.NotFound(new { message = "Matter not found." });

            var alreadyLinkedIds = await db.MatterDocuments.AsNoTracking()
                .Where(md => md.MatterId == id)
                .Select(md => md.DocumentId)
                .ToListAsync(ct);

            var docSourceMap = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(db, matter, accessControl, ct);

            var unlinkedCandidateIds = docSourceMap.Keys.Except(alreadyLinkedIds).ToList();

            var docs = await db.Documents.AsNoTracking()
                .Where(d => unlinkedCandidateIds.Contains(d.Id) && d.RecordStatus == RecordStatus.Active && d.Status == "Active")
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new
                {
                    d.Id,
                    d.OriginalFileName,
                    d.DocumentType,
                    d.UploadedAt
                })
                .ToListAsync(ct);

            var result = docs.Select(d => new
            {
                d.Id,
                d.OriginalFileName,
                d.DocumentType,
                d.UploadedAt,
                source = docSourceMap.GetValueOrDefault(d.Id, "Candidate")
            });

            return Results.Ok(result);
        });

        matters.MapPost("/{id:guid}/documents/link", async (
            Guid id,
            LinkMatterDocumentApiRequest request,
            MatterWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (request.DocumentId == Guid.Empty)
                return Results.BadRequest(new { message = "Document ID is mandatory." });

            var cmd = new LinkExistingDocumentCommand(
                DocumentId: request.DocumentId,
                DocumentRole: request.Role,
                DisplayName: request.DisplayName,
                ExpectedRevision: request.ExpectedRevision
            );

            try
            {
                await workflow.LinkExistingDocumentAsync(id, cmd, userId, ct);
                return Results.NoContent();
            }
            catch (MatterWorkflowException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        matters.MapPost("/{id:guid}/export", async (
            Guid id,
            ExportMatterDocumentsApiRequest request,
            LacDbContext db,
            IDocumentStorage storage,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterAsync(id, PermissionCodes.MatterDocumentManage, userId, ct))
                return Results.Forbid();

            var exists = await db.Matters.AsNoTracking().AnyAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (!exists) return Results.NotFound(new { message = "Matter not found." });

            // FROZEN RULE: Only explicit MatterDocument joins are exported
            var allowedIds = await db.MatterDocuments.AsNoTracking()
                .Where(x => x.MatterId == id)
                .Select(x => x.DocumentId)
                .Distinct()
                .ToListAsync(ct);

            var requested = request.DocumentIds.Distinct().ToList();
            if (requested.Count == 0 || requested.Any(x => !allowedIds.Contains(x)))
                return Results.BadRequest(new { message = "Select only documents explicitly linked to this Matter." });

            var docs = await db.Documents.AsNoTracking()
                .Where(x => requested.Contains(x.Id) && x.RecordStatus == RecordStatus.Active && x.Status == "Active")
                .ToListAsync(ct);

            var tempPath = Path.Combine(Path.GetTempPath(), $"lac-matter-{Guid.NewGuid():N}.zip");
            var zipStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            try
            {
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var document in docs)
                    {
                        var name = Path.GetFileName(document.OriginalFileName);
                        var candidate = name;
                        var n = 2;
                        while (!names.Add(candidate))
                            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n++}){Path.GetExtension(name)}";

                        var entry = zip.CreateEntry(candidate, CompressionLevel.Fastest);
                        await using var input = await storage.OpenReadAsync(document.StoragePath, ct)
                            ?? throw new InvalidOperationException("A selected document is unavailable.");
                        await using var output = entry.Open();
                        await input.CopyToAsync(output, ct);

                        await accessLogger.LogAccessAsync(new RecordAccessCommand(
                            ActorUserId: userId,
                            Action: RecordAccessAction.Downloaded,
                            DocumentId: document.Id,
                            ContextEntityType: "Matter",
                            ContextEntityId: id,
                            DocumentTitleSnapshot: document.OriginalFileName
                        ), ct);
                    }
                }
                zipStream.Position = 0;
                return Results.File(zipStream, "application/zip", $"matter-{id:N}-documents.zip");
            }
            catch
            {
                await zipStream.DisposeAsync();
                throw;
            }
        });

        // Dedicated streaming route for matter documents
        matters.MapGet("/{matterId:guid}/documents/{documentId:guid}/content", async (
            Guid matterId,
            Guid documentId,
            bool? download,
            LacDbContext db,
            IDocumentStorage storage,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessMatterDocumentAsync(matterId, documentId, userId, ct))
                return Results.Forbid();

            var doc = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId && d.RecordStatus == RecordStatus.Active && d.Status == "Active", ct);
            if (doc is null) return Results.NotFound(new { message = "Document record not found." });

            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound(new { message = "Document file not found on storage volume." });

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            var ext = Path.GetExtension(doc.OriginalFileName);
            var mime = doc.MimeType ?? MatterDocumentValidation.GetServerDerivedMimeType(ext);

            var isDownload = download == true || (download == null && !MatterDocumentValidation.IsInlineDisposition(ext));
            var action = isDownload ? RecordAccessAction.Downloaded : RecordAccessAction.Previewed;
            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: action,
                DocumentId: documentId,
                ContextEntityType: "Matter",
                ContextEntityId: matterId,
                DocumentTitleSnapshot: doc.OriginalFileName
            ), ct);

            if (!isDownload)
            {
                return Results.Stream(stream, mime, enableRangeProcessing: true);
            }

            return Results.Stream(stream, mime, doc.OriginalFileName, enableRangeProcessing: true);
        });

        // ====================================================================
        // 9. MATTER LOOKUPS / CONTEXT
        // ====================================================================
        matters.MapGet("/context", async (
            LacDbContext db,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var createScopes = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                join rp in db.RolePermissions on r.Id equals rp.RoleId
                join p in db.Permissions on rp.PermissionId equals p.Id
                where ur.UserId == userId
                   && r.IsActive && r.RecordStatus == RecordStatus.Active
                   && p.Code == PermissionCodes.MatterCreate
                select rp.ScopeMode
            ).Distinct().ToListAsync(ct);

            List<object> workstreams;
            if (createScopes.Contains(ScopeMode.All))
            {
                workstreams = await db.Workstreams.AsNoTracking()
                    .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                    .OrderBy(w => w.Name)
                    .Select(w => (object)new { id = w.Id, name = w.Name, code = w.Code })
                    .ToListAsync(ct);
            }
            else if (createScopes.Contains(ScopeMode.Workstream))
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
            else
            {
                return Results.Forbid();
            }

            return Results.Ok(new
            {
                workstreams,
                matterTypes = new[] { "Court Case", "Compensation", "Land Acquisition", "General", "Other" }
            });
        });

        return matters;
    }

    public static RouteGroupBuilder MapMatterDraftEndpoints(this RouteGroupBuilder api)
    {
        var drafts = api.MapGroup("/matter-drafts");

        drafts.MapGet("/{id:guid}", async (
            Guid id,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessDraftAsync(id, PermissionCodes.DraftView, userId, ct))
                return Results.Forbid();

            var draft = await db.MatterDrafts.AsNoTracking()
                .Include(x => x.Matter)
                .SingleOrDefaultAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (draft is null) return Results.NotFound(new { message = "Matter draft not found." });

            var layout = MatterDraftLayoutProfiles.For(draft);
            return Results.Ok(new
            {
                draft.Id,
                draft.MatterId,
                matterTitle = draft.Matter.Title,
                draft.Title,
                draftType = draft.DraftType.ToString(),
                status = draft.Status.ToString(),
                draft.ContentJson,
                draft.Revision,
                layout.PageSize,
                layout.Orientation,
                layout.MarginTopMm,
                layout.MarginRightMm,
                layout.MarginBottomMm,
                layout.MarginLeftMm,
                draft.UpdatedAt
            });
        });

        drafts.MapPut("/{id:guid}", async (
            Guid id,
            UpdateMatterDraftRequest request,
            LacDbContext db,
            IMatterAuthorizationService matterAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await matterAuth.CanAccessDraftAsync(id, PermissionCodes.DraftEdit, userId, ct))
                return Results.Forbid();

            var draft = await db.MatterDrafts.SingleOrDefaultAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (draft is null) return Results.NotFound(new { message = "Matter draft not found." });

            if (draft.Revision != request.ExpectedRevision)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Draft conflict", detail: "This draft was changed elsewhere. Reload before saving.");

            if (!MatterDraftLayoutProfiles.TryDraftTitle(request.Title, out var title, out var titleProblem))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = [titleProblem] });

            if (!MatterDraftLayoutProfiles.TryValidateDraftContent(request.ContentJson, out var contentProblem))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["contentJson"] = [contentProblem] });

            if (!MatterDraftLayoutProfiles.TryValidateDraftLayout(request, draft.DraftType, out var layout, out var layoutProblem))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pageLayout"] = [layoutProblem] });

            draft.Title = title;
            draft.ContentJson = request.ContentJson;
            MatterDraftLayoutProfiles.ApplyDraftLayout(draft, layout);
            draft.Revision++;
            draft.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Draft conflict", detail: "This draft was changed elsewhere. Reload before saving.");
            }

            return Results.Ok(new { draft.Id, draft.Revision, draft.UpdatedAt });
        });

        return drafts;
    }
}

public sealed record CreateMatterApiRequest(
    Guid VillageId,
    string Title,
    string? MatterType,
    Guid? WorkstreamId,
    string? ReferenceNumber,
    string? Remarks,
    string? KhasraReferenceText,
    Guid? AwardId
);

public sealed record UpdateMatterMetadataApiRequest(
    string Title,
    string? MatterType,
    string? ReferenceNumber,
    string? Remarks,
    string? KhasraReferenceText,
    int ExpectedRevision,
    string? Status = null
);

public sealed record ReclassifyMatterApiRequest(
    Guid TargetWorkstreamId,
    string? Reason,
    int ExpectedRevision
);

public sealed record ArchiveMatterApiRequest(
    string? Reason,
    int ExpectedRevision
);

public sealed record LinkMatterDocumentApiRequest(
    Guid DocumentId,
    string? Role,
    string? DisplayName,
    int ExpectedRevision
);

public sealed record ExportMatterDocumentsApiRequest(
    List<Guid> DocumentIds
);
