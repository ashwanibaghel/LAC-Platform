using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDhcHistoricalBackfillMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourtExternalListingObservations_SourceDocumentId_Normalize~",
                table: "CourtExternalListingObservations");

            migrationBuilder.AddColumn<int>(
                name: "ArchivePagesDiscovered",
                table: "CourtExternalSyncRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CasesAdvanced",
                table: "CourtExternalSyncRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EligibleCaseCount",
                table: "CourtExternalSyncRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "CourtExternalSyncRuns",
                type: "text",
                nullable: false,
                defaultValue: "LiveWindow");

            migrationBuilder.AddColumn<int>(
                name: "TargetCaseMatches",
                table: "CourtExternalSyncRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WindowEnd",
                table: "CourtExternalSyncRuns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WindowStart",
                table: "CourtExternalSyncRuns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "CourtExternalListingObservations",
                type: "text",
                nullable: false,
                defaultValue: "LiveWindow");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingObservations_SourceDocumentId_Normalize~",
                table: "CourtExternalListingObservations",
                columns: new[] { "SourceDocumentId", "NormalizedCaseIdentity", "ListingDate", "Mode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourtExternalListingObservations_SourceDocumentId_Normalize~",
                table: "CourtExternalListingObservations");

            migrationBuilder.DropColumn(
                name: "ArchivePagesDiscovered",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "CasesAdvanced",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "EligibleCaseCount",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "TargetCaseMatches",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "WindowEnd",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "WindowStart",
                table: "CourtExternalSyncRuns");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "CourtExternalListingObservations");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingObservations_SourceDocumentId_Normalize~",
                table: "CourtExternalListingObservations",
                columns: new[] { "SourceDocumentId", "NormalizedCaseIdentity", "ListingDate" },
                unique: true);
        }
    }
}
