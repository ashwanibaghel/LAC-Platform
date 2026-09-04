using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwardPdfExtractionStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AwardDocumentExtractionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedVillageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalPages = table.Column<int>(type: "integer", nullable: true),
                    ProcessedPages = table.Column<int>(type: "integer", nullable: false),
                    CurrentStage = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ExtractorVersion = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardDocumentExtractionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardDocumentExtractionJobs_AwardIngestionSessions_Ingestio~",
                        column: x => x.IngestionSessionId,
                        principalTable: "AwardIngestionSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardDocumentExtractionJobs_Awards_TargetAwardId",
                        column: x => x.TargetAwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardDocumentExtractionJobs_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardDocumentExtractionJobs_Villages_SelectedVillageId",
                        column: x => x.SelectedVillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardDocumentPageExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Height = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ExtractionMethod = table.Column<string>(type: "text", nullable: false),
                    NormalizedText = table.Column<string>(type: "text", nullable: false),
                    StructuredLayoutJson = table.Column<string>(type: "text", nullable: false),
                    OcrConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    WarningMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardDocumentPageExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardDocumentPageExtractions_AwardDocumentExtractionJobs_Jo~",
                        column: x => x.JobId,
                        principalTable: "AwardDocumentExtractionJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentExtractionJobs_DocumentId",
                table: "AwardDocumentExtractionJobs",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentExtractionJobs_IngestionSessionId",
                table: "AwardDocumentExtractionJobs",
                column: "IngestionSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentExtractionJobs_SelectedVillageId",
                table: "AwardDocumentExtractionJobs",
                column: "SelectedVillageId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentExtractionJobs_Status_CreatedAt",
                table: "AwardDocumentExtractionJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentExtractionJobs_TargetAwardId",
                table: "AwardDocumentExtractionJobs",
                column: "TargetAwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardDocumentPageExtractions_JobId_PageNumber",
                table: "AwardDocumentPageExtractions",
                columns: new[] { "JobId", "PageNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwardDocumentPageExtractions");

            migrationBuilder.DropTable(
                name: "AwardDocumentExtractionJobs");
        }
    }
}
