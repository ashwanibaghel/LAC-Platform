using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewedCourtImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "CourtProceedings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ApplyStatusToExisting",
                table: "CourtImportRows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCaseNumber",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCaseTitle",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCourtName",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedStatus",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitError",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitStatus",
                table: "CourtImportRows",
                type: "text",
                nullable: false,
                defaultValue: "NotCommitted");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommittedAt",
                table: "CourtImportRows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommittedCourtCaseId",
                table: "CourtImportRows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommittedProceedingId",
                table: "CourtImportRows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NdohAction",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionAction",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedCourtCaseId",
                table: "CourtImportRows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "CourtImportRows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByDisplayNameSnapshot",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "CourtImportRows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerNotes",
                table: "CourtImportRows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CourtImportBatchId",
                table: "CourtCaseEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CourtImportRowId",
                table: "CourtCaseEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_BatchId_CommitStatus",
                table: "CourtImportRows",
                columns: new[] { "BatchId", "CommitStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_CommittedCourtCaseId",
                table: "CourtImportRows",
                column: "CommittedCourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtImportRows_CommittedProceedingId",
                table: "CourtImportRows",
                column: "CommittedProceedingId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseEvents_CourtImportRowId",
                table: "CourtCaseEvents",
                column: "CourtImportRowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourtImportRows_BatchId_CommitStatus",
                table: "CourtImportRows");

            migrationBuilder.DropIndex(
                name: "IX_CourtImportRows_CommittedCourtCaseId",
                table: "CourtImportRows");

            migrationBuilder.DropIndex(
                name: "IX_CourtImportRows_CommittedProceedingId",
                table: "CourtImportRows");

            migrationBuilder.DropIndex(
                name: "IX_CourtCaseEvents_CourtImportRowId",
                table: "CourtCaseEvents");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "CourtProceedings");

            migrationBuilder.DropColumn(
                name: "ApplyStatusToExisting",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ApprovedCaseNumber",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ApprovedCaseTitle",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ApprovedCourtName",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ApprovedStatus",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CommitError",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CommitStatus",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CommittedAt",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CommittedCourtCaseId",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CommittedProceedingId",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "NdohAction",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ResolutionAction",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ResolvedCourtCaseId",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ReviewedByDisplayNameSnapshot",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "ReviewerNotes",
                table: "CourtImportRows");

            migrationBuilder.DropColumn(
                name: "CourtImportBatchId",
                table: "CourtCaseEvents");

            migrationBuilder.DropColumn(
                name: "CourtImportRowId",
                table: "CourtCaseEvents");
        }
    }
}
