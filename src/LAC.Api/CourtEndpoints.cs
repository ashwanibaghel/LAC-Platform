namespace LAC.Api;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public sealed record CreateCourtProceedingApiRequest(
    DateOnly? ProceedingDate,
    string? OrderType,
    string? RestraintNature,
    string? Summary,
    DateOnly? NextDate,
    int? ExpectedRevision = null
);

public sealed record LinkAwardApiRequest(Guid AwardId, int? ExpectedRevision = null);
public sealed record LinkKhasraApiRequest(Guid KhasraId, int? ExpectedRevision = null);
public sealed record LinkMatterApiRequest(Guid MatterId, int? ExpectedRevision = null);
public sealed record LinkDocumentApiRequest(Guid DocumentId, string? DocumentRole, string? DisplayName, Guid? CourtProceedingId, int? ExpectedRevision = null);

public static class CourtEndpoints
{
    public static IResult ToProblem(CourtWorkflowException ex) =>
        ex.StatusCode switch
        {
            400 => Results.BadRequest(new { error = ex.Message }),
            401 => Results.Unauthorized(),
            403 => Results.Forbid(),
            404 => Results.NotFound(new { error = ex.Message }),
            409 => Results.Conflict(new { error = ex.Message }),
            _ => Results.Problem(ex.Message, statusCode: ex.StatusCode)
        };

    public static RouteGroupBuilder MapCourtEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/court-cases");

        // 1. Directory Listing
        group.MapGet("", async (
            string? search,
            string? courtName,
            string? currentStatus,
            DateOnly? filedFrom,
            DateOnly? filedTo,
            Guid? deskId,
            Guid? assignedUserId,
            Guid? awardId,
            Guid? villageId,
            int? page,
            int? pageSize,
            ICourtAuthorizationService courtAuth,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await courtAuth.HasCourtViewPermissionAsync(currentUser.UserId.Value, ct))
                return Results.Forbid();

            var p = page.GetValueOrDefault(1);
            var ps = pageSize.GetValueOrDefault(25);

            var query = new CourtCaseFilterQuery(
                Search: search,
                CourtName: courtName,
                CurrentStatus: currentStatus,
                FiledFrom: filedFrom,
                FiledTo: filedTo,
                DeskId: deskId,
                AssignedUserId: assignedUserId,
                AwardId: awardId,
                VillageId: villageId,
                Page: p > 0 ? p : 1,
                PageSize: ps > 0 ? ps : 25
            );

            var result = await projection.GetCourtCasesAsync(query, currentUser.UserId.Value, ct);
            return Results.Ok(result);
        });

        // 2. Filter Options
        group.MapGet("/filter-options", async (
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var options = await projection.GetFilterOptionsAsync(currentUser.UserId.Value, ct);
            return Results.Ok(options);
        });

        // 3. Create Case
        group.MapPost("", async (
            CreateCourtCaseCommand cmd,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var id = await workflow.CreateCourtCaseAsync(cmd, currentUser.UserId.Value, ct);
                return Results.Created($"/api/court-cases/{id}", new { id });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 4. Case Workspace Detail
        group.MapGet("/{id:guid}", async (
            Guid id,
            ICourtAuthorizationService courtAuth,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await courtAuth.CanViewCourtCaseAsync(id, currentUser.UserId.Value, ct))
                return Results.Forbid();

            var detail = await projection.GetCourtCaseDetailAsync(id, currentUser.UserId.Value, ct);
            if (detail is null) return Results.NotFound(new { error = "Court case not found." });
            return Results.Ok(detail);
        });

        // 5. Update Case Metadata
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCourtCaseMetadataCommand cmd,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.UpdateMetadataAsync(id, cmd, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 6. Reassign Case
        var reassignHandler = async (
            Guid id,
            AssignCourtCaseCommand cmd,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.AssignAsync(id, cmd, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        };
        group.MapPost("/{id:guid}/reassign", reassignHandler);
        group.MapPost("/{id:guid}/assign", reassignHandler);

        // 7. Get Proceedings
        group.MapGet("/{id:guid}/proceedings", async (
            Guid id,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var proceedings = await projection.GetProceedingsAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(proceedings);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        // 8. Record Proceeding
        group.MapPost("/{id:guid}/proceedings", async (
            Guid id,
            CreateCourtProceedingApiRequest req,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var cmd = new RecordCourtProceedingCommand(
                    ProceedingDate: req.ProceedingDate,
                    OrderType: req.OrderType,
                    RestraintNature: req.RestraintNature,
                    Summary: req.Summary,
                    NextDate: req.NextDate,
                    ExpectedRevision: req.ExpectedRevision
                );
                var result = await workflow.RecordProceedingAsync(id, cmd, currentUser.UserId.Value, ct);
                return Results.Created($"/api/court-cases/{id}/proceedings/{result.Id}", result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 9. Get Documents
        group.MapGet("/{id:guid}/documents", async (
            Guid id,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var docs = await projection.GetDocumentsAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(docs);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        // 10. Upload Document
        var uploadHandler = async (
            Guid id,
            HttpRequest request,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "File must be provided." });

            var documentRole = form["documentRole"].ToString();
            var displayName = form["displayName"].ToString();
            Guid? proceedingId = Guid.TryParse(form["courtProceedingId"].ToString(), out var pid) ? pid : null;
            int? expectedRevision = int.TryParse(form["expectedRevision"].ToString(), out var rev) ? rev : null;

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await workflow.UploadDocumentAsync(
                    id,
                    stream,
                    file.FileName,
                    file.ContentType,
                    string.IsNullOrWhiteSpace(documentRole) ? null : documentRole,
                    string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    proceedingId,
                    expectedRevision,
                    currentUser.UserId.Value,
                    ct
                );
                return Results.Created($"/api/court-cases/{id}/documents/{result.Id}", result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        };
        group.MapPost("/{id:guid}/documents", uploadHandler);
        group.MapPost("/{id:guid}/documents/upload", uploadHandler);

        // 11. Link Existing Document
        group.MapPost("/{id:guid}/documents/link", async (
            Guid id,
            LinkDocumentApiRequest req,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var result = await workflow.LinkDocumentAsync(id, req.DocumentId, req.DocumentRole, req.DisplayName, req.CourtProceedingId, req.ExpectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 12. Unlink Document
        group.MapDelete("/{id:guid}/documents/{documentLinkId:guid}", async (
            Guid id,
            Guid documentLinkId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.UnlinkDocumentAsync(id, documentLinkId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 13. Dedicated Streaming Route for Court Case Documents
        group.MapGet("/{id:guid}/documents/{documentId:guid}/content", async (
            Guid id,
            Guid documentId,
            bool? download,
            LacDbContext db,
            IDocumentStorage storage,
            ICourtAuthorizationService courtAuth,
            ICurrentUserContext currentUser,
            IRecordAccessLogger accessLogger,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!await courtAuth.CanViewCourtCaseAsync(id, userId, ct))
                return Results.Forbid();

            var linkExists = await db.CourtCaseDocuments.AsNoTracking()
                .AnyAsync(cd => cd.CourtCaseId == id && cd.DocumentId == documentId && cd.RecordStatus == RecordStatus.Active, ct);
            if (!linkExists) return Results.NotFound(new { error = "Document is not linked to this court case." });

            var doc = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId && d.RecordStatus == RecordStatus.Active && d.Status == "Active", ct);
            if (doc is null) return Results.NotFound(new { error = "Document not found or inactive." });

            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound(new { error = "Document file not found on storage volume." });

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            var ext = Path.GetExtension(doc.OriginalFileName);
            var mime = doc.MimeType ?? "application/octet-stream";

            var isDownload = download == true;
            var action = isDownload ? RecordAccessAction.Downloaded : RecordAccessAction.Previewed;

            await accessLogger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: action,
                DocumentId: documentId,
                ContextEntityType: "CourtCase",
                ContextEntityId: id,
                DocumentTitleSnapshot: doc.OriginalFileName
            ), ct);

            if (!isDownload)
            {
                return Results.Stream(stream, mime, enableRangeProcessing: true);
            }

            return Results.Stream(stream, mime, doc.OriginalFileName, enableRangeProcessing: true);
        });

        // 14. Link Award
        group.MapPost("/{id:guid}/awards", async (
            Guid id,
            LinkAwardApiRequest req,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.LinkAwardAsync(id, req.AwardId, req.ExpectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 15. Unlink Award
        group.MapDelete("/{id:guid}/awards/{awardId:guid}", async (
            Guid id,
            Guid awardId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.UnlinkAwardAsync(id, awardId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 16. Link Khasra
        group.MapPost("/{id:guid}/khasras", async (
            Guid id,
            LinkKhasraApiRequest req,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.LinkKhasraAsync(id, req.KhasraId, req.ExpectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 17. Unlink Khasra
        group.MapDelete("/{id:guid}/khasras/{khasraId:guid}", async (
            Guid id,
            Guid khasraId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.UnlinkKhasraAsync(id, khasraId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 18. Link Matter
        group.MapPost("/{id:guid}/matters", async (
            Guid id,
            LinkMatterApiRequest req,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.LinkMatterAsync(id, req.MatterId, req.ExpectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 19. Unlink Matter
        group.MapDelete("/{id:guid}/matters/{matterId:guid}", async (
            Guid id,
            Guid matterId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.UnlinkMatterAsync(id, matterId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 20. Add Party
        group.MapPost("/{id:guid}/parties", async (
            Guid id,
            CreateCourtCasePartyDto dto,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var result = await workflow.AddPartyAsync(id, dto, currentUser.UserId.Value, ct);
                return Results.Created($"/api/court-cases/{id}/parties/{result.Id}", result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 21. Update Party
        group.MapPut("/{id:guid}/parties/{partyId:guid}", async (
            Guid id,
            Guid partyId,
            CreateCourtCasePartyDto dto,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var result = await workflow.UpdatePartyAsync(id, partyId, dto, currentUser.UserId.Value, ct);
                return Results.Ok(result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 22. Remove Party
        group.MapDelete("/{id:guid}/parties/{partyId:guid}", async (
            Guid id,
            Guid partyId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.RemovePartyAsync(id, partyId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 23. Add Representative
        group.MapPost("/{id:guid}/representatives", async (
            Guid id,
            CreateCourtCaseRepresentativeDto dto,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var result = await workflow.AddRepresentativeAsync(id, dto, currentUser.UserId.Value, ct);
                return Results.Created($"/api/court-cases/{id}/representatives/{result.Id}", result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 24. Update Representative
        group.MapPut("/{id:guid}/representatives/{repId:guid}", async (
            Guid id,
            Guid repId,
            CreateCourtCaseRepresentativeDto dto,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var result = await workflow.UpdateRepresentativeAsync(id, repId, dto, currentUser.UserId.Value, ct);
                return Results.Ok(result);
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 25. Remove Representative
        group.MapDelete("/{id:guid}/representatives/{repId:guid}", async (
            Guid id,
            Guid repId,
            int? expectedRevision,
            ICourtWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                await workflow.RemoveRepresentativeAsync(id, repId, expectedRevision, currentUser.UserId.Value, ct);
                return Results.Ok(new { success = true });
            }
            catch (CourtWorkflowException ex) { return ToProblem(ex); }
        });

        // 26. Timeline
        group.MapGet("/{id:guid}/timeline", async (
            Guid id,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var timeline = await projection.GetTimelineAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(timeline);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        // 27. Linked Work
        group.MapGet("/{id:guid}/work", async (
            Guid id,
            ICourtProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try
            {
                var work = await projection.GetLinkedWorkAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(work);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        return group;
    }
}
