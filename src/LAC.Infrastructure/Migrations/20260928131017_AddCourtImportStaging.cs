using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtImportStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourtImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSheetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ValidRows = table.Column<int>(type: "integer", nullable: false),
                    NeedsReviewRows = table.Column<int>(type: "integer", nullable: false),
                    ConflictRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ParsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureMessage = table.Column<string>(type: "text", nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtImportBatches_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceSerialNumberRaw = table.Column<string>(type: "text", nullable: true),
                    RawRowJson = table.Column<string>(type: "text", nullable: false),
                    RawNdoh = table.Column<string>(type: "text", nullable: true),
                    ParsedNdoh = table.Column<DateOnly>(type: "date", nullable: true),
                    RawStatus = table.Column<string>(type: "text", nullable: true),
                    SuggestedStatusClass = table.Column<string>(type: "text", nullable: true),
                    RawAdvocate = table.Column<string>(type: "text", nullable: true),
                    RawCaseTitle = table.Column<string>(type: "text", nullable: true),
                    RawCaseNumber = table.Column<string>(type: "text", nullable: true),
                    SuggestedCaseType = table.Column<string>(type: "text", nullable: true),
                    SuggestedCaseNumber = table.Column<string>(type: "text", nullable: true),
                    SuggestedCaseYear = table.Column<int>(type: "integer", nullable: true),
                    RawVillage = table.Column<string>(type: "text", nullable: true),
                    RawAwardNumber = table.Column<string>(type: "text", nullable: true),
                    RawDirections = table.Column<string>(type: "text", nullable: true),
                    RawCourt = table.Column<string>(type: "text", nullable: true),
                    SuggestedCourtName = table.Column<string>(type: "text", nullable: true),
                    RawLastOrderLink = table.Column<string>(type: "text", nullable: true),
                    LastOrderLinkState = table.Column<string>(type: "text", nullable: true),
                    RawBriefFacts = table.Column<string>(type: "text", nullable: true),
                    ExtraCellsJson = table.Column<string>(type: "text", nullable: false),
                    RowStatus = table.Column<string>(type: "text", nullable: false),
                    ValidationIssuesJson = table.Column<string>(type: "text", nullable: false),
                    CandidateCourtCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentityKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SourceRowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtImportRows_CourtImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "CourtImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportBatches_CreatedAt",
                table: "CourtImportBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportBatches_SourceDocumentId",
                table: "CourtImportBatches",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_BatchId_IdentityKey",
                table: "CourtImportRows",
                columns: new[] { "BatchId", "IdentityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_BatchId_RowStatus",
                table: "CourtImportRows",
                columns: new[] { "BatchId", "RowStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_BatchId_SourceRowNumber",
                table: "CourtImportRows",
                columns: new[] { "BatchId", "SourceRowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_CandidateCourtCaseId",
                table: "CourtImportRows",
                column: "CandidateCourtCaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtImportRows");

            migrationBuilder.DropTable(
                name: "CourtImportBatches");
        }
    }
}
