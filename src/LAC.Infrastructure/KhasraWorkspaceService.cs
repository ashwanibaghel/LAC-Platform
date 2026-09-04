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
public sealed record KhasraWorkspaceRow(string KhasraNumber, decimal? Bigha, int? Biswa, int? Biswansi, string? AwardNumber, DateOnly? AwardDate, string? RectangleNumber = null, string? Qualifier = null);
public sealed record KhasraImportProblem(int RowNumber, string? KhasraNumber, string Message);
public sealed record KhasraImportRowPreview(int RowNumber, string? KhasraNumber, string KhasraStatus, string AreaStatus, string AwardStatus, string AwardLinkStatus, string Result, string? Message, bool CanImport, KhasraWorkspaceRow? Row);
public sealed record KhasraImportPreview(int TotalRows, int ValidRows, int InvalidRows, int NewKhasras, int ExistingKhasras, int NewAwards, int ExistingAwards, int AmbiguousAwards, int NewAwardLinks, int ExistingAwardLinks, int SkippedRows, IReadOnlyList<KhasraImportProblem> Problems, IReadOnlyList<KhasraWorkspaceRow> ImportableRows, IReadOnlyList<KhasraImportRowPreview> Rows);
public sealed record KhasraImportResult(int CreatedKhasras, int ReusedKhasras, int CreatedAwards, int ReusedAwards, int CreatedAwardLinks, int SkippedExistingLinks, int FailedRows);
public sealed record KhasraExportRow(string KhasraNumber, decimal? Bigha, int? Biswa, int? Biswansi, string OwnerSummary, string AcquisitionStatus, string Awards);

public sealed class KhasraWorkspaceService(LacDbContext db)
{
    public async Task<KhasraImportPreview> PreviewAsync(Guid villageId, Stream workbook, CancellationToken ct)
    {
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new KhasraWorkspaceException("Village was not found.", 404);
        using var xlsx = new XLWorkbook(workbook);
        var sheet = xlsx.Worksheets.FirstOrDefault(x => x.Name.Equals("All Occurrences", StringComparison.OrdinalIgnoreCase)) ?? xlsx.Worksheets.FirstOrDefault() ?? throw new KhasraWorkspaceException("The workbook has no worksheet.");
        var header = sheet.FirstRowUsed() ?? throw new KhasraWorkspaceException("The worksheet is empty.");
        var map = header.CellsUsed().ToDictionary(c => Header(c.GetString()), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        var sourceFormat = map.ContainsKey("NORMALIZEDKHASRA") || map.ContainsKey("NORMALIZEDKHASRANO") || map.ContainsKey("NORMALIZEDKHASRANUMBER") || map.ContainsKey("NORMALIZE");
        var required = sourceFormat ? new[] { "BIGHA", "BISWA", "BISWANSI", "AWARDNO", "AWARDDATE" } : new[] { "KHASRANO", "BIGHA", "BISWA", "BISWANSI", "AWARDNO", "AWARDDATE" };
        var missing = required.Where(x => !map.ContainsKey(x)).ToList();
        if (missing.Count > 0) throw new KhasraWorkspaceException($"Excel must contain exactly the canonical headers: Khasra No., Bigha, Biswa, Biswansi, Award No., Award Date. Missing: {string.Join(", ", missing)}.");
        var rows = new List<(int RowNumber, KhasraWorkspaceRow? Row, string? Problem)>();
        foreach (var row in sheet.RowsUsed().Where(x => x.RowNumber() > header.RowNumber()))
        {
            var values = map.Values.Select(column => row.Cell(column).GetString()).ToList();
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                var number = sourceFormat ? TextAny(row, map, "NORMALIZEDKHASRANO", "NORMALIZEDKHASRANUMBER", "NORMALIZEDKHASRA", "NORMALIZE") : Text(row, map, "KHASRANO");
                number ??= string.Empty;
                var qualifier = sourceFormat ? Text(row, map, "QUALIFIER") : null;
                var display = !string.IsNullOrWhiteSpace(qualifier) && !number.Contains(qualifier, StringComparison.OrdinalIgnoreCase) ? $"{number} {qualifier}" : number;
                var rectangle = Text(row, map, "RECTANGLE") ?? Rectangle(number);
                var item = new KhasraWorkspaceRow(display, Decimal(row, map, "BIGHA"), Integer(row, map, "BISWA"), Integer(row, map, "BISWANSI"), Text(row, map, "AWARDNO"), Date(row, map, "AWARDDATE"), rectangle, qualifier);
                Validate(item); rows.Add((row.RowNumber() - header.RowNumber(), item, null));
            }
            catch (KhasraWorkspaceException ex) { rows.Add((row.RowNumber() - header.RowNumber(), null, ex.Message)); }
        }
        return await PreviewRowsAsync(villageId, rows, ct);
    }

    public async Task<KhasraImportPreview> PreviewRowsAsync(Guid villageId, IReadOnlyList<KhasraWorkspaceRow> input, CancellationToken ct)
        => await PreviewRowsAsync(villageId, input.Select((x, i) => (i + 1, (KhasraWorkspaceRow?)x, (string?)null)).ToList(), ct);

    public async Task<KhasraImportResult> ImportAsync(Guid villageId, IReadOnlyList<KhasraWorkspaceRow> rows, CancellationToken ct)
    {
        var preview = await PreviewRowsAsync(villageId, rows, ct);
        var importable = preview.ImportableRows;
        if (importable.Count == 0) return new(0, 0, 0, 0, 0, 0, preview.TotalRows);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<KhasraImportResult>(async () =>
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            var result = new int[7];
            try
            {
                var baseNumbers = importable.Select(BaseNormalized).Distinct().ToList();
                var existingKhasras = (await db.Khasras
                    .Where(x => x.VillageId == villageId && baseNumbers.Contains(x.NormalizedNumber))
                    .ToListAsync(ct))
                    .ToDictionary(x => IdentityKey(x.NormalizedNumber, x.Qualifier));
                var awardNumbers = importable.Where(x => !string.IsNullOrWhiteSpace(x.AwardNumber))
                    .Select(x => x.AwardNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var awards = (await db.Awards.Where(x => awardNumbers.Contains(x.AwardNumber)).ToListAsync(ct))
                    .ToDictionary(x => x.AwardNumber, StringComparer.OrdinalIgnoreCase);
                var linksToCreate = new List<(Award Award, Khasra Khasra)>();

                foreach (var row in importable)
                {
                    var normalized = BaseNormalized(row);
                    var key = IdentityKey(normalized, row.Qualifier);
                    if (!existingKhasras.TryGetValue(key, out var khasra))
                    {
                        khasra = NewKhasra(villageId, row, normalized);
                        db.Khasras.Add(khasra);
                        existingKhasras.Add(key, khasra);
                        result[0]++;
                    }
                    else result[1]++;

                    if (string.IsNullOrWhiteSpace(row.AwardNumber)) continue;
                    var awardNumber = row.AwardNumber.Trim();
                    if (!awards.TryGetValue(awardNumber, out var award))
                    {
                        award = new Award { AwardNumber = awardNumber, AwardDate = row.AwardDate, Status = "Draft" };
                        db.Awards.Add(award);
                        awards.Add(awardNumber, award);
                        result[2]++;
                    }
                    else result[3]++;
                    if (!db.AwardVillages.Local.Any(x => x.AwardId == award.Id && x.VillageId == villageId) && !await db.AwardVillages.AnyAsync(x => x.AwardId == award.Id && x.VillageId == villageId, ct)) db.Add(new AwardVillage { Award = award, VillageId = villageId });
                    linksToCreate.Add((award, khasra));
                }

                await db.SaveChangesAsync(ct);
                var khasraIds = linksToCreate.Select(x => x.Khasra.Id).Distinct().ToList();
                var awardIds = linksToCreate.Select(x => x.Award.Id).Distinct().ToList();
                var existingLinks = (await db.Set<AwardKhasra>()
                    .Where(x => khasraIds.Contains(x.KhasraId) && awardIds.Contains(x.AwardId))
                    .Select(x => new { x.AwardId, x.KhasraId }).ToListAsync(ct))
                    .Select(x => (x.AwardId, x.KhasraId)).ToHashSet();
                foreach (var (award, khasra) in linksToCreate)
                {
                    if (existingLinks.Add((award.Id, khasra.Id)))
                    {
                        db.Add(new AwardKhasra { AwardId = award.Id, KhasraId = khasra.Id });
                        result[4]++;
                    }
                    else result[5]++;
                }
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return new(result[0], result[1], result[2], result[3], result[4], result[5], preview.InvalidRows);
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task UpdateAsync(Guid khasraId, KhasraWorkspaceRow row, CancellationToken ct)
    {
        Validate(row); var khasra = await db.Khasras.SingleOrDefaultAsync(x => x.Id == khasraId, ct) ?? throw new KhasraWorkspaceException("Khasra was not found.", 404);
        var normalized = BaseNormalized(row);
        if (await db.Khasras.AnyAsync(x => x.VillageId == khasra.VillageId && x.NormalizedNumber == normalized && x.Qualifier == row.Qualifier && x.Id != khasra.Id, ct)) throw new KhasraWorkspaceException("This Khasra number and qualifier already exist in the village.", 409);
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
    public static byte[] ImportTemplate() { using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Khasra Import"); var headers = new[] { "Khasra No.", "Bigha", "Biswa", "Biswansi", "Award No.", "Award Date" }; for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i]; ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true; ws.Columns().AdjustToContents(); var instructions = wb.AddWorksheet("Instructions"); var lines = new[] { "One row = one Khasra", "Do not combine Khasras in one cell", "Award is optional", "Award Date format DD/MM/YYYY", "Do not add merged cells", "Do not change column names" }; for (var i = 0; i < lines.Length; i++) instructions.Cell(i + 1, 1).Value = lines[i]; instructions.Column(1).AdjustToContents(); using var stream = new MemoryStream(); wb.SaveAs(stream); return stream.ToArray(); }
    public static byte[] Csv(IReadOnlyList<KhasraExportRow> rows) { var b = new StringBuilder(); b.AppendLine("Khasra No.,Bigha,Biswa,Biswansi,Recorded Owner,Acquisition Status,Linked Awards"); foreach (var r in rows) b.AppendLine(string.Join(',', new[] { r.KhasraNumber, r.Bigha?.ToString(CultureInfo.InvariantCulture) ?? "", r.Biswa?.ToString() ?? "", r.Biswansi?.ToString() ?? "", r.OwnerSummary, r.AcquisitionStatus, r.Awards }.Select(CsvCell))); return Encoding.UTF8.GetBytes(b.ToString()); }
    public static byte[] Pdf(IReadOnlyList<KhasraExportRow> rows) => QuestPDF.Fluent.Document.Create(c => c.Page(p => { p.Margin(25); p.Header().Text("Village Khasras").FontSize(18).SemiBold(); p.Content().Table(t => { t.ColumnsDefinition(d => { for (var i = 0; i < 7; i++) d.RelativeColumn(); }); t.Header(h => { foreach (var text in new[] { "Khasra", "Bigha", "Biswa", "Biswansi", "Recorded owner", "Acquisition", "Awards" }) h.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text(text).SemiBold(); }); foreach (var r in rows) foreach (var value in Values(r)) t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(value); }); p.Footer().AlignCenter().Text("LAC Platform · development export"); })).GeneratePdf();
    public static byte[] Docx(IReadOnlyList<KhasraExportRow> rows) { using var stream = new MemoryStream(); using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true)) { var main = doc.AddMainDocumentPart(); var body = new Body(new Paragraph(new Run(new Text("Village Khasras")))); var table = new Table(); foreach (var row in rows.Prepend(new KhasraExportRow("Khasra No.", null, null, null, "Recorded Owner", "Acquisition Status", "Linked Awards"))) { var cells = Values(row).Select(value => new TableCell(new Paragraph(new Run(new Text(value))))).ToArray(); table.Append(new TableRow(cells)); } body.Append(table); main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body); main.Document.Save(); } return stream.ToArray(); }

    private async Task<KhasraImportPreview> PreviewRowsAsync(Guid villageId, IReadOnlyList<(int RowNumber, KhasraWorkspaceRow? Row, string? Problem)> rows, CancellationToken ct)
    {
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new KhasraWorkspaceException("Village was not found.", 404);
        var checkedRows = new List<(int RowNumber, KhasraWorkspaceRow? Row, string? Error, string? Key)>(); var seen = new HashSet<string>();
        foreach (var (rowNumber, row, parseError) in rows)
        {
            try
            {
                if (parseError is not null) throw new KhasraWorkspaceException(parseError);
                Validate(row!); var key = IdentityKey(row!);
                if (!seen.Add(key)) throw new KhasraWorkspaceException("Duplicate Khasra number and qualifier within this import; one row must represent one canonical Khasra.");
                checkedRows.Add((rowNumber, row, null, key));
            }
            catch (KhasraWorkspaceException ex) { checkedRows.Add((rowNumber, row, ex.Message, null)); }
        }
        var keys = checkedRows.Where(x => x.Key is not null).Select(x => x.Key!).ToList();
        var baseNumbers = checkedRows.Where(x => x.Key is not null).Select(x => BaseNormalized(x.Row!)).Distinct().ToList();
        var existing = (await db.Khasras.Where(x => x.VillageId == villageId && baseNumbers.Contains(x.NormalizedNumber)).ToListAsync(ct)).ToDictionary(x => IdentityKey(x.NormalizedNumber, x.Qualifier));
        var awardNumbers = checkedRows.Where(x => x.Row?.AwardNumber is { Length: > 0 }).Select(x => x.Row!.AwardNumber!.Trim()).Distinct().ToList();
        var awards = await db.Awards.Where(x => awardNumbers.Contains(x.AwardNumber)).ToDictionaryAsync(x => x.AwardNumber, ct);
        var existingLinks = await db.Set<AwardKhasra>().Where(x => baseNumbers.Contains(x.Khasra.NormalizedNumber) && x.Khasra.VillageId == villageId).Select(x => new { x.Khasra.NormalizedNumber, x.Khasra.Qualifier, x.AwardId }).ToListAsync(ct);
        var previews = new List<KhasraImportRowPreview>(); var problems = new List<KhasraImportProblem>();
        foreach (var item in checkedRows)
        {
            if (item.Error is not null) { previews.Add(new(item.RowNumber, item.Row?.KhasraNumber, "Invalid Khasra", "—", "—", "—", "BLOCKED", item.Error, false, null)); problems.Add(new(item.RowNumber, item.Row?.KhasraNumber, item.Error)); continue; }
            var row = item.Row!; var khasraExists = existing.TryGetValue(item.Key!, out var existingKhasra); var areaConflict = khasraExists && !SameArea(existingKhasra!, row);
            var awardStatus = "No Award"; var linkStatus = "No Award Link"; var blocked = areaConflict; var message = areaConflict ? $"AREA CONFLICT: Existing {Area(existingKhasra!)}; incoming {Area(row)}. Keep existing, update explicitly with Edit, or skip this row." : null;
            if (!string.IsNullOrWhiteSpace(row.AwardNumber))
            {
                if (awards.TryGetValue(row.AwardNumber.Trim(), out var award))
                {
                    if (row.AwardDate is not null && award.AwardDate is not null && award.AwardDate != row.AwardDate) { awardStatus = "AWARD MATCH AMBIGUOUS"; blocked = true; message = "AWARD MATCH AMBIGUOUS: existing Award Number has a different date."; }
                    else { awardStatus = "Existing Award"; linkStatus = khasraExists && existingLinks.Any(x => IdentityKey(x.NormalizedNumber, x.Qualifier) == item.Key && x.AwardId == award.Id) ? "Already Linked" : "New Award Link"; }
                }
                else { awardStatus = "New Award"; linkStatus = "New Award Link"; }
            }
            var result = blocked ? "BLOCKED" : "READY";
            previews.Add(new(item.RowNumber, row.KhasraNumber, khasraExists ? (existingKhasra!.RecordStatus == RecordStatus.Archived ? "Archived Khasra — explicit restore required" : "Existing Khasra") : "New Khasra", areaConflict ? "AREA CONFLICT" : (khasraExists ? "Matches existing" : "New area"), awardStatus, linkStatus, result, message, !blocked && existingKhasra?.RecordStatus != RecordStatus.Archived, row));
            if (blocked) problems.Add(new(item.RowNumber, row.KhasraNumber, message!));
        }
        var importable = previews.Where(x => x.CanImport).Select(x => x.Row!).ToList();
        var newAwards = previews.Where(x => x.AwardStatus == "New Award").Select(x => x.Row!.AwardNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var existingAwards = previews.Where(x => x.AwardStatus == "Existing Award").Select(x => x.Row!.AwardNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new(rows.Count, importable.Count, previews.Count(x => !x.CanImport), previews.Count(x => x.KhasraStatus == "New Khasra"), previews.Count(x => x.KhasraStatus == "Existing Khasra"), newAwards, existingAwards, previews.Count(x => x.AwardStatus == "AWARD MATCH AMBIGUOUS"), previews.Count(x => x.AwardLinkStatus == "New Award Link"), previews.Count(x => x.AwardLinkStatus == "Already Linked"), previews.Count(x => !x.CanImport), problems, importable, previews);
    }
    private static void Validate(KhasraWorkspaceRow row) { if (string.IsNullOrWhiteSpace(row.KhasraNumber)) throw new KhasraWorkspaceException("Khasra Number is required."); if (row.KhasraNumber.IndexOfAny([',', ';']) >= 0) throw new KhasraWorkspaceException("One Excel row must contain one Khasra only; do not combine Khasra numbers."); if (row.Bigha is < 0 || row.Biswa is < 0 || row.Biswansi is < 0) throw new KhasraWorkspaceException("Area values cannot be negative."); if (row.AwardDate is not null && string.IsNullOrWhiteSpace(row.AwardNumber)) throw new KhasraWorkspaceException("Award Date requires Award Number."); }
    private static string BaseNormalized(KhasraWorkspaceRow row) => KhasraNumber.Normalize(string.IsNullOrWhiteSpace(row.Qualifier) ? row.KhasraNumber : row.KhasraNumber.Replace($" {row.Qualifier}", "", StringComparison.OrdinalIgnoreCase));
    private static string IdentityKey(KhasraWorkspaceRow row) => IdentityKey(BaseNormalized(row), row.Qualifier);
    private static string IdentityKey(string normalizedNumber, string? qualifier) => $"{normalizedNumber}|{qualifier?.Trim().ToUpperInvariant() ?? string.Empty}";
    private static Khasra NewKhasra(Guid villageId, KhasraWorkspaceRow row, string normalized) { var k = new Khasra { VillageId = villageId, DisplayNumber = row.KhasraNumber.Trim(), NormalizedNumber = normalized, Qualifier = row.Qualifier }; Apply(k, row); return k; }
    private static void Apply(Khasra khasra, KhasraWorkspaceRow row) { khasra.AreaBigha = row.Bigha; khasra.AreaBiswa = row.Biswa; khasra.AreaBiswansi = row.Biswansi; if (!string.IsNullOrWhiteSpace(row.Qualifier)) khasra.Qualifier = row.Qualifier.Trim(); if (!string.IsNullOrWhiteSpace(row.RectangleNumber)) khasra.RectangleNumber = row.RectangleNumber.Trim(); }
    private static string Header(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); private static string? Text(IXLRow row, IReadOnlyDictionary<string, int> map, string key) => map.TryGetValue(key, out var i) && !row.Cell(i).IsEmpty() ? row.Cell(i).GetString().Trim() : null; private static string? TextAny(IXLRow row, IReadOnlyDictionary<string, int> map, params string[] keys) => keys.Select(key => Text(row, map, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)); private static string? Rectangle(string? khasra) { var separator = khasra?.IndexOf("//", StringComparison.Ordinal) ?? -1; return separator > 0 ? khasra![..separator].Trim() : null; }
    private static decimal? Decimal(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { var v = Text(row, map, key); return string.IsNullOrWhiteSpace(v) ? null : decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : throw new KhasraWorkspaceException($"{key} must be numeric."); }
    private static int? Integer(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { var v = Text(row, map, key); return string.IsNullOrWhiteSpace(v) ? null : int.TryParse(v, out var n) ? n : throw new KhasraWorkspaceException($"{key} must be a whole number."); }
    private static DateOnly? Date(IXLRow row, IReadOnlyDictionary<string, int> map, string key) { if (!map.TryGetValue(key, out var column) || row.Cell(column).IsEmpty()) return null; var cell = row.Cell(column); if (cell.TryGetValue<DateTime>(out var excelDate)) return DateOnly.FromDateTime(excelDate); var value = cell.GetString().Trim(); if (DateOnly.TryParseExact(value, ["dd/MM/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date; throw new KhasraWorkspaceException("Award Date must be DD/MM/YYYY (or an actual Excel date cell)."); }
    private static void WriteSheet(IXLWorksheet ws, IReadOnlyList<KhasraExportRow> rows) { var headers = new[] { "Khasra No.", "Bigha", "Biswa", "Biswansi", "Recorded Owner", "Acquisition Status", "Linked Awards" }; for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i]; for (var r = 0; r < rows.Count; r++) { var x = rows[r]; ws.Cell(r + 2, 1).Value = x.KhasraNumber; ws.Cell(r + 2, 2).Value = x.Bigha; ws.Cell(r + 2, 3).Value = x.Biswa; ws.Cell(r + 2, 4).Value = x.Biswansi; ws.Cell(r + 2, 5).Value = x.OwnerSummary; ws.Cell(r + 2, 6).Value = x.AcquisitionStatus; ws.Cell(r + 2, 7).Value = x.Awards; } ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true; ws.Columns().AdjustToContents(); }
    private static string CsvCell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string[] Values(KhasraExportRow row) => [row.KhasraNumber, row.Bigha?.ToString() ?? "", row.Biswa?.ToString() ?? "", row.Biswansi?.ToString() ?? "", row.OwnerSummary, row.AcquisitionStatus, row.Awards];
    private static bool SameArea(Khasra existing, KhasraWorkspaceRow incoming) => existing.AreaBigha == incoming.Bigha && existing.AreaBiswa == incoming.Biswa && existing.AreaBiswansi == incoming.Biswansi;
    private static string Area(Khasra item) => $"{item.AreaBigha?.ToString(CultureInfo.InvariantCulture) ?? "—"}-{item.AreaBiswa?.ToString() ?? "—"}-{item.AreaBiswansi?.ToString() ?? "—"}";
    private static string Area(KhasraWorkspaceRow item) => $"{item.Bigha?.ToString(CultureInfo.InvariantCulture) ?? "—"}-{item.Biswa?.ToString() ?? "—"}-{item.Biswansi?.ToString() ?? "—"}";
}
