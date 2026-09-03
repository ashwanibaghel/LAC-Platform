using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text;

namespace LAC.Infrastructure;

public sealed class KhasraWorkspaceException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record KhasraWorkspaceRow(string KhasraNumber, decimal? Bigha, int? Biswa, int? Biswansi, string? AwardNumber, DateOnly? AwardDate);
public sealed record KhasraImportProblem(int RowNumber, string? KhasraNumber, string Message);
public sealed record KhasraImportPreview(int TotalRows, int NewKhasras, int ExistingKhasras, int NewAwards, int ExistingAwards, IReadOnlyList<KhasraImportProblem> Problems, IReadOnlyList<KhasraWorkspaceRow> ValidRows);
public sealed record KhasraImportResult(int CreatedKhasras, int ReusedKhasras, int CreatedAwards, int ReusedAwards, int CreatedAwardLinks);
public sealed record KhasraExportRow(string KhasraNumber, decimal? Bigha, int? Biswa, int? Biswansi, string OwnerSummary, string AcquisitionStatus, string Awards);

public sealed class KhasraWorkspaceService(LacDbContext db)
{
    public async Task<KhasraImportPreview> PreviewAsync(Guid villageId, Stream workbook, CancellationToken ct)
    {
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new KhasraWorkspaceException("Village was not found.", 404);
        using var xlsx = new XLWorkbook(workbook);
        var sheet = xlsx.Worksheets.FirstOrDefault() ?? throw new KhasraWorkspaceException("The workbook has no worksheet.");
        var header = sheet.FirstRowUsed() ?? throw new KhasraWorkspaceException("The worksheet is empty.");
        var map = header.CellsUsed().ToDictionary(c => Header(c.GetString()), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        if (!map.ContainsKey("KHASRANUMBER")) throw new KhasraWorkspaceException("Excel must contain a 'Khasra Number' column.");
        var rows = new List<(int RowNumber, KhasraWorkspaceRow? Row, string? Problem)>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > header.RowNumber()))
        {
            var values = map.Values.Select(column => row.Cell(column).GetString()).ToList();
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                var item = new KhasraWorkspaceRow(Text(row, map, "KHASRANUMBER") ?? "", Decimal(row, map, "BIGHA"), Integer(row, map, "BISWA"), Integer(row, map, "BISWANSI"), Text(row, map, "AWARDNUMBER"), Date(row, map, "AWARDDATE"));
                Validate(item); rows.Add((row.RowNumber(), item, null));
            }
            catch (KhasraWorkspaceException ex) { rows.Add((row.RowNumber(), null, ex.Message)); }
        }
        return await PreviewRowsAsync(villageId, rows, ct);
    }

    public async Task<KhasraImportPreview> PreviewRowsAsync(Guid villageId, IReadOnlyList<KhasraWorkspaceRow> input, CancellationToken ct)
        => await PreviewRowsAsync(villageId, input.Select((x, i) => (i + 1, (KhasraWorkspaceRow?)x, (string?)null)).ToList(), ct);

    public async Task<KhasraImportResult> ImportAsync(Guid villageId, IReadOnlyList<KhasraWorkspaceRow> rows, CancellationToken ct)
    {
        var preview = await PreviewRowsAsync(villageId, rows, ct);
        if (preview.Problems.Count > 0) throw new KhasraWorkspaceException("Resolve problem rows before importing. Valid rows have not been changed.");
        IDbContextTransaction? transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var result = new int[5];
            foreach (var row in preview.ValidRows)
            {
                var normalized = KhasraNumber.Normalize(row.KhasraNumber);
                var khasra = await db.Khasras.SingleOrDefaultAsync(x => x.VillageId == villageId && x.NormalizedNumber == normalized, ct);
                if (khasra is null) { khasra = NewKhasra(villageId, row, normalized); db.Khasras.Add(khasra); result[0]++; }
                else { Apply(khasra, row); result[1]++; }
                if (!string.IsNullOrWhiteSpace(row.AwardNumber))
                {
                    var award = await db.Awards.SingleOrDefaultAsync(x => x.AwardNumber == row.AwardNumber.Trim(), ct);
                    if (award is null) { award = new Award { AwardNumber = row.AwardNumber.Trim(), AwardDate = row.AwardDate, Status = "Draft" }; db.Awards.Add(award); result[2]++; }
                    else result[3]++;
                    await db.SaveChangesAsync(ct);
                    if (!await db.Set<AwardKhasra>().AnyAsync(x => x.AwardId == award.Id && x.KhasraId == khasra.Id, ct)) { db.Add(new AwardKhasra { AwardId = award.Id, KhasraId = khasra.Id }); result[4]++; }
                }
            }
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return new(result[0], result[1], result[2], result[3], result[4]);
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(ct); throw; }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public async Task UpdateAsync(Guid khasraId, KhasraWorkspaceRow row, CancellationToken ct)
    {
        Validate(row); var khasra = await db.Khasras.SingleOrDefaultAsync(x => x.Id == khasraId, ct) ?? throw new KhasraWorkspaceException("Khasra was not found.", 404);
        var normalized = KhasraNumber.Normalize(row.KhasraNumber);
        if (await db.Khasras.AnyAsync(x => x.VillageId == khasra.VillageId && x.NormalizedNumber == normalized && x.Id != khasra.Id, ct)) throw new KhasraWorkspaceException("This Khasra number already exists in the village.", 409);
        khasra.DisplayNumber = row.KhasraNumber.Trim(); khasra.NormalizedNumber = normalized; Apply(khasra, row);
        if (!string.IsNullOrWhiteSpace(row.AwardNumber))
        {
            var award = await db.Awards.SingleOrDefaultAsync(x => x.AwardNumber == row.AwardNumber.Trim(), ct) ?? new Award { AwardNumber = row.AwardNumber.Trim(), AwardDate = row.AwardDate, Status = "Draft" };
            if (award.Id == Guid.Empty) db.Awards.Add(award); await db.SaveChangesAsync(ct);
            if (!await db.Set<AwardKhasra>().AnyAsync(x => x.AwardId == award.Id && x.KhasraId == khasra.Id, ct)) db.Add(new AwardKhasra { AwardId = award.Id, KhasraId = khasra.Id });
        }
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(Khasra), EntityId = khasra.Id, Action = "KhasraWorkspaceEdited", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<KhasraExportRow>> ExportRowsAsync(Guid villageId, string? query, CancellationToken ct)
    {
        var queryable = db.Khasras.AsNoTracking().Where(x => x.VillageId == villageId && x.RecordStatus == RecordStatus.Active);
        if (!string.IsNullOrWhiteSpace(query)) { var term = KhasraNumber.Normalize(query); queryable = queryable.Where(x => x.NormalizedNumber.Contains(term)); }
        var items = await queryable.Include(x => x.AwardLinks).ThenInclude(x => x.Award).Include(x => x.KhataLinks).ThenInclude(x => x.Khata).ThenInclude(x => x.PartyShares).ThenInclude(x => x.Party).OrderBy(x => x.DisplayNumber).ToListAsync(ct);
        return items.Select(x =>
        {
            var status = string.Join(", ", x.AwardLinks.Select(a => a.AcquisitionStatus).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            return new KhasraExportRow(x.DisplayNumber, x.AreaBigha, x.AreaBiswa, x.AreaBiswansi,
                string.Join(", ", x.KhataLinks.SelectMany(k => k.Khata.PartyShares).Select(s => s.Party.DisplayName).Distinct()),
                string.IsNullOrWhiteSpace(status) ? "Not recorded" : status,
                string.Join(", ", x.AwardLinks.Select(a => a.Award.AwardNumber)));
        }).ToList();
    }

    public static byte[] Excel(IReadOnlyList<KhasraExportRow> rows) { using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Village Khasras"); WriteSheet(ws, rows); using var stream = new MemoryStream(); wb.SaveAs(stream); return stream.ToArray(); }
    public static byte[] Csv(IReadOnlyList<KhasraExportRow> rows) { var b = new StringBuilder(); b.AppendLine("Khasra No.,Bigha,Biswa,Biswansi,Recorded Owner,Acquisition Status,Linked Awards"); foreach (var r in rows) b.AppendLine(string.Join(',', new[] { r.KhasraNumber, r.Bigha?.ToString(CultureInfo.InvariantCulture) ?? "", r.Biswa?.ToString() ?? "", r.Biswansi?.ToString() ?? "", r.OwnerSummary, r.AcquisitionStatus, r.Awards }.Select(CsvCell))); return Encoding.UTF8.GetBytes(b.ToString()); }
    public static byte[] Pdf(IReadOnlyList<KhasraExportRow> rows) => QuestPDF.Fluent.Document.Create(c => c.Page(p => { p.Margin(25); p.Header().Text("Village Khasras").FontSize(18).SemiBold(); p.Content().Table(t => { t.ColumnsDefinition(d => { for (var i = 0; i < 7; i++) d.RelativeColumn(); }); t.Header(h => { foreach (var text in new[] { "Khasra", "Bigha", "Biswa", "Biswansi", "Recorded owner", "Acquisition", "Awards" }) h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text(text).SemiBold(); }); foreach (var r in rows) foreach (var value in Values(r)) t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(value); }); p.Footer().AlignCenter().Text("LAC Platform · development export"); })).GeneratePdf();
    public static byte[] Docx(IReadOnlyList<KhasraExportRow> rows) { using var stream = new MemoryStream(); using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true)) { var main = doc.AddMainDocumentPart(); var body = new Body(new Paragraph(new Run(new Text("Village Khasras")))); var table = new Table(); foreach (var row in rows.Prepend(new KhasraExportRow("Khasra No.", null, null, null, "Recorded Owner", "Acquisition Status", "Linked Awards"))) { var cells = Values(row).Select(value => new TableCell(new Paragraph(new Run(new Text(value))))).ToArray(); table.Append(new TableRow(cells)); } body.Append(table); main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body); main.Document.Save(); } return stream.ToArray(); }

    private async Task<KhasraImportPreview> PreviewRowsAsync(Guid villageId, IReadOnlyList<(int RowNumber, KhasraWorkspaceRow? Row, string? Problem)> rows, CancellationToken ct)
    {
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new KhasraWorkspaceException("Village was not found.", 404);
        var problems = new List<KhasraImportProblem>(); var valid = new List<KhasraWorkspaceRow>(); var seen = new HashSet<string>();
        foreach (var (rowNumber, row, error) in rows) { if (error is not null) { problems.Add(new(rowNumber, null, error)); continue; } try { Validate(row!); var key = KhasraNumber.Normalize(row!.KhasraNumber); if (!seen.Add(key)) throw new KhasraWorkspaceException("Duplicate Khasra number within this import."); valid.Add(row); } catch (KhasraWorkspaceException ex) { problems.Add(new(rowNumber, row?.KhasraNumber, ex.Message)); } }
        var keys = valid.Select(x => KhasraNumber.Normalize(x.KhasraNumber)).ToList(); var existing = await db.Khasras.Where(x => x.VillageId == villageId && keys.Contains(x.NormalizedNumber)).Select(x => x.NormalizedNumber).ToListAsync(ct); var awardNumbers = valid.Where(x => !string.IsNullOrWhiteSpace(x.AwardNumber)).Select(x => x.AwardNumber!.Trim()).Distinct().ToList(); var existingAwards = await db.Awards.Where(x => awardNumbers.Contains(x.AwardNumber)).Select(x => x.AwardNumber).ToListAsync(ct);
        return new(rows.Count, valid.Count(x => !existing.Contains(KhasraNumber.Normalize(x.KhasraNumber))), valid.Count(x => existing.Contains(KhasraNumber.Normalize(x.KhasraNumber))), awardNumbers.Count(x => !existingAwards.Contains(x)), awardNumbers.Count(x => existingAwards.Contains(x)), problems, valid);
    }
    private static void Validate(KhasraWorkspaceRow row) { if (string.IsNullOrWhiteSpace(row.KhasraNumber)) throw new KhasraWorkspaceException("Khasra Number is required."); if (row.Bigha is < 0 || row.Biswa is < 0 || row.Biswansi is < 0) throw new KhasraWorkspaceException("Area values cannot be negative."); if (row.AwardDate is not null && string.IsNullOrWhiteSpace(row.AwardNumber)) throw new KhasraWorkspaceException("Award Date requires Award Number."); }
    private static Khasra NewKhasra(Guid villageId, KhasraWorkspaceRow row, string normalized) { var k = new Khasra { VillageId = villageId, DisplayNumber = row.KhasraNumber.Trim(), NormalizedNumber = normalized }; Apply(k, row); return k; }
    private static void Apply(Khasra khasra, KhasraWorkspaceRow row) { khasra.AreaBigha = row.Bigha; khasra.AreaBiswa = row.Biswa; khasra.AreaBiswansi = row.Biswansi; }
    private static string Header(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); private static string? Text(IXLRow row, IReadOnlyDictionary<string, int> map, string key) => map.TryGetValue(key, out var i) && !row.Cell(i).IsEmpty() ? row.Cell(i).GetString().Trim() : null;
    private static decimal? Decimal(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { var v = Text(row, map, key); return string.IsNullOrWhiteSpace(v) ? null : decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : throw new KhasraWorkspaceException($"{key} must be numeric."); }
    private static int? Integer(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { var v = Text(row, map, key); return string.IsNullOrWhiteSpace(v) ? null : int.TryParse(v, out var n) ? n : throw new KhasraWorkspaceException($"{key} must be a whole number."); }
    private static DateOnly? Date(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { var v = Text(row, map, key); if (string.IsNullOrWhiteSpace(v)) return null; if (DateOnly.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d; throw new KhasraWorkspaceException("Award Date must be a valid date."); }
    private static void WriteSheet(IXLWorksheet ws, IReadOnlyList<KhasraExportRow> rows) { var headers = new[] { "Khasra No.", "Bigha", "Biswa", "Biswansi", "Recorded Owner", "Acquisition Status", "Linked Awards" }; for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i]; for (var r = 0; r < rows.Count; r++) { var x = rows[r]; ws.Cell(r + 2, 1).Value = x.KhasraNumber; ws.Cell(r + 2, 2).Value = x.Bigha; ws.Cell(r + 2, 3).Value = x.Biswa; ws.Cell(r + 2, 4).Value = x.Biswansi; ws.Cell(r + 2, 5).Value = x.OwnerSummary; ws.Cell(r + 2, 6).Value = x.AcquisitionStatus; ws.Cell(r + 2, 7).Value = x.Awards; } ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true; ws.Columns().AdjustToContents(); }
    private static string CsvCell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string[] Values(KhasraExportRow row) => [row.KhasraNumber, row.Bigha?.ToString() ?? "", row.Biswa?.ToString() ?? "", row.Biswansi?.ToString() ?? "", row.OwnerSummary, row.AcquisitionStatus, row.Awards];
}
