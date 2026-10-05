using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class CourtImportPriorityTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private sealed class Clock : IOfficeClock
    {
        public DateOnly GetCurrentDate() => Today;
        public DateTimeOffset GetUtcNow() => new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    }
    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static CourtImportBatch Batch() => new() { Status = CourtImportBatchStatus.Parsed, RecordStatus = RecordStatus.Active };
    private static CourtImportRow Row(CourtImportBatch batch, int? offset, int source = 3) => new()
    { Batch = batch, BatchId = batch.Id, ParsedNdoh = offset.HasValue ? Today.AddDays(offset.Value) : null, SourceRowNumber = source,
      SuggestedStatusClass = CourtImportStatusClass.Pending, RawCaseNumber = "Unconfirmed workbook reference", RowStatus = CourtImportRowStatus.NeedsReview };

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(7, true)] [InlineData(8, false)] [InlineData(-1, true)] [InlineData(null, false)]
    public async Task DeterministicDateQualification(int? offset, bool expected)
    {
        await using var db = Db(); var row = Row(Batch(), offset); row.RawNdoh = "unparseable"; db.Add(row); await db.SaveChangesAsync();
        Assert.Equal(expected, await CourtImportPriorityQuery.Urgent(db.CourtImportRows, Today).AnyAsync());
        Assert.Equal(CourtImportRowStatus.NeedsReview, row.RowStatus);
        Assert.Empty(db.CourtCases);
    }

    [Theory]
    [InlineData("committed")] [InlineData("skipped")] [InlineData("disposed-overdue")] [InlineData("inactive")] [InlineData("unparsed")]
    public async Task NonActionableRowsAreExcluded(string reason)
    {
        await using var db = Db(); var row = Row(Batch(), reason == "disposed-overdue" ? -1 : 0);
        if (reason == "committed") row.CommitStatus = CourtImportCommitStatus.Committed;
        if (reason == "skipped") row.ResolutionAction = CourtImportResolutionAction.Skip;
        if (reason == "disposed-overdue") row.SuggestedStatusClass = CourtImportStatusClass.Disposed;
        if (reason == "inactive") row.Batch.RecordStatus = RecordStatus.Archived;
        if (reason == "unparsed") row.Batch.Status = CourtImportBatchStatus.Failed;
        db.Add(row); await db.SaveChangesAsync(); Assert.Empty(await CourtImportPriorityQuery.Urgent(db.CourtImportRows, Today).ToListAsync());
    }

    [Fact]
    public async Task UpcomingFirstThenNearestOverdueWithStableTiesAndCountsAcrossBatches()
    {
        await using var db = Db(); var batch = Batch(); var other = Batch();
        var rows = new[] { Row(batch, -3, 8), Row(batch, 7, 7), Row(batch, 0, 5), Row(batch, -1, 6), Row(other, 0, 3), Row(other, 1, 4) };
        rows[1].ResolutionAction = CourtImportResolutionAction.ImportAsNewCase;
        rows[3].CommitStatus = CourtImportCommitStatus.Failed;
        db.AddRange(rows); await db.SaveChangesAsync(); var service = new CourtImportService(db, null!, new Clock());
        var summary = await service.UrgentSummaryAsync();
        Assert.Equal(6, summary.UrgentTotal); Assert.Equal(4, summary.UpcomingNext7Days); Assert.Equal(2, summary.OverduePending); Assert.Equal(other.Id, summary.TargetBatchId);
        var entries = await service.UrgentEntriesAsync(); Assert.Equal(new[] { 3, 5, 4, 7, 6, 8 }, entries.Select(x => x.SourceRowNumber));
        Assert.All(entries, x => { Assert.True(x.ReviewPending); Assert.Equal("Court Excel", x.Source); Assert.Equal("Unconfirmed workbook reference", x.RawCaseNumber); });
        var filtered = await service.RowsAsync(batch.Id, null, null, null, 1, 25, priority: "urgent");
        Assert.Equal(new[] { 5, 7, 6, 8 }, filtered.Item1.Select(x => x.SourceRowNumber));
        Assert.Equal(4, (await service.UrgentSummaryAsync(batch.Id)).UrgentTotal);
        Assert.Empty(db.CourtCases);
        Assert.DoesNotContain(typeof(CourtImportUrgentEntry).GetProperties(), x => x.Name.Contains("CourtCaseId"));
    }

    [Fact]
    public void QueryTranslatesToPostgresWithoutDatabaseAccess()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql = CourtImportPriorityQuery.Order(CourtImportPriorityQuery.Urgent(db.CourtImportRows, Today), Today).ToQueryString();
        Assert.Contains("ORDER BY", sql); Assert.Contains("ParsedNdoh", sql);
    }
}
