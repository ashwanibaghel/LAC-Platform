namespace LAC.Infrastructure;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record CourtImportBatchDto(Guid Id, string Status, string SourceSheetName, int TotalRows, int ValidRows, int NeedsReviewRows, int ConflictRows, int InvalidRows, string? FailureMessage, DateTimeOffset CreatedAt);
public sealed record CourtImportRowDto(Guid Id, int SourceRowNumber, string? SourceSerialNumberRaw, string? RawCaseNumber, string? RawCaseTitle, string? RawCourt, string? RawStatus, string? SuggestedStatusClass, string? RawNdoh, DateOnly? ParsedNdoh, string? RawAdvocate, string? RawVillage, string? RawAwardNumber, string? LastOrderLinkState, string RowStatus, string ValidationIssuesJson, string? RawDirections, string? RawBriefFacts, string? RawLastOrderLink, string? ResolutionAction, Guid? ResolvedCourtCaseId, string? ApprovedCaseNumber, string? ApprovedCaseTitle, string? ApprovedCourtName, string? ApprovedStatus, bool ApplyStatusToExisting, string? NdohAction, string? ReviewerNotes, string CommitStatus, Guid? CommittedCourtCaseId, Guid? CommittedProceedingId, string? CommitError);
public interface ICourtImportService { Task<CourtImportBatchDto> StageAsync(Stream source, string fileName, string? contentType, Guid userId, CancellationToken ct = default); Task<CourtImportBatchDto?> GetAsync(Guid id, CancellationToken ct = default); Task<IReadOnlyList<CourtImportBatchDto>> ListAsync(CancellationToken ct = default); Task<(IReadOnlyList<CourtImportRowDto> Items,int Total)> RowsAsync(Guid id,string? status,string? search,int? sourceRow,int page,int pageSize,CancellationToken ct=default); }

public sealed class CourtImportService(LacDbContext db, IDocumentStorage storage) : ICourtImportService
{
    public const string PrimarySheet = "Court case status pertains to L";
    private const long MaxBytes = 20 * 1024 * 1024;
    public async Task<CourtImportBatchDto> StageAsync(Stream source, string fileName, string? contentType, Guid userId, CancellationToken ct = default)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new CourtWorkflowException("Only .xlsx workbooks are supported.", 400);
        if (source.CanSeek && source.Length > MaxBytes) throw new CourtWorkflowException("Workbook exceeds the 20 MB upload limit.", 400);
        var saved = await storage.SaveAndHashAsync(source, fileName, ct);
        if (saved.FileSize > MaxBytes) { await storage.DeleteAsync(saved.StoragePath, ct); throw new CourtWorkflowException("Workbook exceeds the 20 MB upload limit.", 400); }
        var actor = await db.AppUsers.AsNoTracking().Where(x=>x.Id==userId).Select(x=>x.DisplayName).FirstOrDefaultAsync(ct) ?? userId.ToString();
        var doc = new Document { DocumentType="Court Import Workbook", OriginalFileName=Path.GetFileName(fileName), StoragePath=saved.StoragePath, Sha256Hash=saved.Sha256Hash, MimeType=string.IsNullOrWhiteSpace(contentType)?"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":contentType, FileSize=saved.FileSize, UploadedBy=actor };
        var batch = new CourtImportBatch { SourceDocument=doc, SourceSha256=saved.Sha256Hash, CreatedByUserId=userId, CreatedByDisplayNameSnapshot=actor };
        db.CourtImportBatches.Add(batch); await db.SaveChangesAsync(ct);
        try { await using var input=await storage.OpenReadAsync(saved.StoragePath,ct) ?? throw new InvalidDataException("Stored workbook could not be reopened."); Stage(batch,input); await db.SaveChangesAsync(ct); }
        catch(Exception ex) when(ex is not CourtWorkflowException)
        {
            // Parsing may have added rows before failing. Discard those tracked additions so a
            // failed import remains inspectable without accidentally persisting partial rows.
            db.ChangeTracker.Clear();
            batch = await db.CourtImportBatches.SingleAsync(x => x.Id == batch.Id, ct);
            batch.Status = CourtImportBatchStatus.Failed;
            batch.FailureMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(batch);
    }
    private void Stage(CourtImportBatch batch, Stream input)
    {
        using var book=new XLWorkbook(input); var sheet=book.Worksheets.FirstOrDefault(x=>x.Name.Equals(PrimarySheet,StringComparison.Ordinal)); if(sheet is null) throw new InvalidDataException($"Required sheet '{PrimarySheet}' was not found.");
        var headerRow=sheet.Row(2); var map=HeaderMap(headerRow); foreach(var needed in new[]{"srno","ndoh","status","title","caseno","court"}) if(!map.ContainsKey(needed)) throw new InvalidDataException($"Required source column '{needed}' was not found.");
        var used=sheet.RangeUsed() ?? throw new InvalidDataException("Primary sheet is empty.");
        for(var rn=3;rn<=used.LastRow().RowNumber();rn++) { var row=sheet.Row(rn); if(row.Cells(1,used.LastColumn().ColumnNumber()).All(c=>c.IsEmpty())) continue; var cells=Enumerable.Range(1,used.LastColumn().ColumnNumber()).ToDictionary(i=>XLHelper.GetColumnLetterFromNumber(i),i=>CellText(row.Cell(i))); var ndohCell=row.Cell(map["ndoh"]); var typedNdoh=!ndohCell.HasFormula && ndohCell.DataType==XLDataType.DateTime && ndohCell.TryGetValue<DateTime>(out var excelDate) ? DateOnly.FromDateTime(excelDate) : (DateOnly?)null; var staged=MakeRow(batch.Id,rn,cells,map,typedNdoh); batch.Rows.Add(staged); db.CourtImportRows.Add(staged); }
        Classify(batch); batch.TotalRows=batch.Rows.Count; batch.ValidRows=batch.Rows.Count(x=>x.RowStatus is CourtImportRowStatus.NewCandidate or CourtImportRowStatus.ExistingExact); batch.NeedsReviewRows=batch.Rows.Count(x=>x.RowStatus is CourtImportRowStatus.NeedsReview or CourtImportRowStatus.PotentialDuplicate); batch.ConflictRows=batch.Rows.Count(x=>x.RowStatus==CourtImportRowStatus.IdentityConflict); batch.InvalidRows=batch.Rows.Count(x=>x.RowStatus==CourtImportRowStatus.Invalid); batch.Status=CourtImportBatchStatus.Parsed; batch.ParsedAt=DateTimeOffset.UtcNow;
    }
    private static Dictionary<string,int> HeaderMap(IXLRow header) { var result=new Dictionary<string,int>(); foreach(var c in header.CellsUsed()){var h=NormalizeHeader(CellText(c)); var k=h switch {"srno"=>"srno", "ndoh"=>"ndoh", "casesatus"=>"status", "casestatus"=>"status", "recivedfromwhichadvocate"=>"advocate", "receivedfromwhichadvocate"=>"advocate", "casetitle"=>"title", "caseno"=>"caseno", "village"=>"village", "awardno"=>"award", "directions"=>"directions", "whichcourtpertainsto"=>"court", "lastorderlink"=>"link", "brieffactsofthecase"=>"facts", _=>""}; if(k!="")result[k]=c.Address.ColumnNumber;} return result; }
    private static CourtImportRow MakeRow(Guid batchId,int rn,Dictionary<string,string?> cells,Dictionary<string,int> m,DateOnly? typedNdoh) { string? V(string k)=>m.TryGetValue(k,out var col)?cells[XLHelper.GetColumnLetterFromNumber(col)]:null; var rawNo=V("caseno"); var (type,num,year)=ParseCase(rawNo); var rawStatus=V("status"); var statusClass=Status(rawStatus); var issues=new List<string>(); DateOnly? ndoh=typedNdoh??ParseDate(V("ndoh")); if(!string.IsNullOrWhiteSpace(V("ndoh"))&&!ndoh.HasValue)issues.Add("NDOH is not a deterministic date."); if(string.IsNullOrWhiteSpace(rawStatus))issues.Add("Case status is blank."); if(statusClass==CourtImportStatusClass.Attention)issues.Add("Case status needs classification review."); var link=V("link"); var linkState=string.IsNullOrWhiteSpace(link)?"Missing":Uri.TryCreate(link,UriKind.Absolute,out var u)&&(u.Scheme==Uri.UriSchemeHttp||u.Scheme==Uri.UriSchemeHttps)?"ValidHttpUrl":"NeedsReview"; if(linkState=="NeedsReview")issues.Add("Last-order link is not an http/https URL."); var extras=cells.Where(x=>m.Values.All(v=>XLHelper.GetColumnLetterFromNumber(v)!=x.Key)&&!string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x=>x.Key,x=>x.Value); var identity=type is null||num is null||year is null||string.IsNullOrWhiteSpace(V("court"))?null:$"{Key(V("court"))}|{type}|{num}|{year}"; return new CourtImportRow {BatchId=batchId,SourceRowNumber=rn,SourceSerialNumberRaw=V("srno"),RawRowJson=JsonSerializer.Serialize(cells),RawNdoh=V("ndoh"),ParsedNdoh=ndoh,RawStatus=rawStatus,SuggestedStatusClass=statusClass,RawAdvocate=V("advocate"),RawCaseTitle=V("title"),RawCaseNumber=rawNo,SuggestedCaseType=type,SuggestedCaseNumber=num,SuggestedCaseYear=year,RawVillage=V("village"),RawAwardNumber=V("award"),RawDirections=V("directions"),RawCourt=V("court"),SuggestedCourtName=Clean(V("court")),RawLastOrderLink=link,LastOrderLinkState=linkState,RawBriefFacts=V("facts"),ExtraCellsJson=JsonSerializer.Serialize(extras),IdentityKey=identity,RowStatus=identity is null||issues.Count>0?CourtImportRowStatus.NeedsReview:CourtImportRowStatus.NewCandidate,ValidationIssuesJson=JsonSerializer.Serialize(issues),SourceRowHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cells)))).ToLowerInvariant()}; }
    private void Classify(CourtImportBatch batch)
    {
        // Exact raw reference is a review signal only; it never becomes a canonical identity key.
        foreach (var group in batch.Rows.Where(x => !string.IsNullOrWhiteSpace(x.RawCourt) && !string.IsNullOrWhiteSpace(x.RawCaseNumber))
                     .GroupBy(x => $"{RawReferenceKey(x.RawCourt)}|{RawReferenceKey(x.RawCaseNumber)}"))
            FlagDuplicateGroup(group);
        foreach (var group in batch.Rows.Where(x => x.IdentityKey != null).GroupBy(x => x.IdentityKey!))
            FlagDuplicateGroup(group);

        var existing = db.CourtCases.AsNoTracking().Where(c => c.RecordStatus == RecordStatus.Active)
            .Select(c => new { c.Id, c.CourtName, c.CaseNumber, c.CaseTitle }).ToList();
        foreach (var row in batch.Rows.Where(x => x.IdentityKey != null))
        {
            var matches = existing.Where(c => Identity(c.CourtName, c.CaseNumber) == row.IdentityKey).ToList();
            if (matches.Count != 1)
            {
                if (matches.Count > 1 && row.RowStatus != CourtImportRowStatus.IdentityConflict)
                    row.RowStatus = CourtImportRowStatus.PotentialDuplicate;
                continue;
            }
            row.CandidateCourtCaseId = matches[0].Id;
            if (Key(matches[0].CaseTitle) != Key(row.RawCaseTitle)) row.RowStatus = CourtImportRowStatus.IdentityConflict;
            else if (row.RowStatus == CourtImportRowStatus.NewCandidate) row.RowStatus = CourtImportRowStatus.ExistingExact;
        }
    }
    private static void FlagDuplicateGroup(IEnumerable<CourtImportRow> group)
    {
        var rows = group.ToList(); if (rows.Count < 2) return;
        var conflict = rows.Select(x => Key(x.RawCaseTitle)).Distinct().Count() > 1;
        foreach (var row in rows)
            row.RowStatus = conflict ? CourtImportRowStatus.IdentityConflict : CourtImportRowStatus.PotentialDuplicate;
    }
    internal static string? Identity(string? court, string? caseNumber)
    {
        var (type, number, year) = ParseCase(caseNumber);
        return type is null || number is null || year is null || string.IsNullOrWhiteSpace(court)
            ? null : $"{Key(court)}|{type}|{number}|{year}";
    }
    private static (string?,string?,int?) ParseCase(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null,null,null);
        var match = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^(?<t>[A-Za-z][A-Za-z.() ]*?)\s*(?:No\.?\s*)?[-/]?\s*(?<n>\d+)\s*/\s*(?<y>(?:19|20)\d{2})\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? (Key(match.Groups["t"].Value), match.Groups["n"].Value.TrimStart('0') is { Length: > 0 } number ? number : "0", int.Parse(match.Groups["y"].Value,CultureInfo.InvariantCulture)) : (null,null,null);
    }
    private static DateOnly? ParseDate(string? s)=>DateOnly.TryParseExact(s?.Trim(),new[]{"dd.MM.yyyy","d.M.yyyy","dd/MM/yyyy","d/M/yyyy","yyyy-MM-dd","M/d/yyyy"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    private static CourtImportStatusClass? Status(string? s)=>string.IsNullOrWhiteSpace(s)?null:Key(s) == "pending"?CourtImportStatusClass.Pending:Key(s).StartsWith("disposed")||Key(s)=="disposedoff"?CourtImportStatusClass.Disposed:CourtImportStatusClass.Attention;
    private static string? CellText(IXLCell c)=>c.IsEmpty()?null:c.HasFormula?"="+c.FormulaA1:c.GetFormattedString(); private static string NormalizeHeader(string? s)=>new string((s??"").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); private static string Key(string? s)=>new string((s??"").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant(); private static string RawReferenceKey(string? s)=>(Clean(s)??"").ToUpperInvariant(); private static string? Clean(string? s)=>string.IsNullOrWhiteSpace(s)?null:string.Join(' ',s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    public async Task<CourtImportBatchDto?> GetAsync(Guid id,CancellationToken ct=default)=>await db.CourtImportBatches.AsNoTracking().Where(x=>x.Id==id).Select(x=>ToDto(x)).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<CourtImportBatchDto>> ListAsync(CancellationToken ct=default)=>await db.CourtImportBatches.AsNoTracking().OrderByDescending(x=>x.CreatedAt).Select(x=>ToDto(x)).ToListAsync(ct);
    public async Task<(IReadOnlyList<CourtImportRowDto>, int)> RowsAsync(Guid id, string? status, string? search, int? sourceRow, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.CourtImportRows.AsNoTracking().Where(x => x.BatchId == id);
        if (Enum.TryParse<CourtImportRowStatus>(status, true, out var rs)) q = q.Where(x => x.RowStatus == rs);
        if (sourceRow.HasValue) q = q.Where(x => x.SourceRowNumber == sourceRow);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            q = q.Where(x => (x.RawCaseNumber ?? "").ToLower().Contains(s) ||
                             (x.RawCaseTitle ?? "").ToLower().Contains(s) ||
                             (x.RawCourt ?? "").ToLower().Contains(s));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.SourceRowNumber)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100))
            .Select(x => new CourtImportRowDto(
                x.Id, x.SourceRowNumber, x.SourceSerialNumberRaw, x.RawCaseNumber, x.RawCaseTitle,
                x.RawCourt, x.RawStatus, x.SuggestedStatusClass == null ? null : x.SuggestedStatusClass.ToString(),
                x.RawNdoh, x.ParsedNdoh, x.RawAdvocate, x.RawVillage, x.RawAwardNumber,
                x.LastOrderLinkState, x.RowStatus.ToString(), x.ValidationIssuesJson,
                x.RawDirections, x.RawBriefFacts, x.RawLastOrderLink,
                x.ResolutionAction == null ? null : x.ResolutionAction.ToString(), x.ResolvedCourtCaseId,
                x.ApprovedCaseNumber, x.ApprovedCaseTitle, x.ApprovedCourtName, x.ApprovedStatus,
                x.ApplyStatusToExisting, x.NdohAction == null ? null : x.NdohAction.ToString(),
                x.ReviewerNotes, x.CommitStatus.ToString(), x.CommittedCourtCaseId,
                x.CommittedProceedingId, x.CommitError))
            .ToListAsync(ct);
        return (items, total);
    }
    private static CourtImportBatchDto ToDto(CourtImportBatch x)=>new(x.Id,x.Status.ToString(),x.SourceSheetName,x.TotalRows,x.ValidRows,x.NeedsReviewRows,x.ConflictRows,x.InvalidRows,x.FailureMessage,x.CreatedAt);
}
