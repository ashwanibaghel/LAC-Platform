using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveCourtCaseScheduleProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_CourtProceedingId",
                table: "ScheduledEvents");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourtCaseId_ActiveCourtProjection",
                table: "ScheduledEvents",
                column: "CourtCaseId",
                unique: true,
                filter: "\"CourtCaseId\" IS NOT NULL AND \"Origin\" = 'CourtProceeding' AND \"Status\" = 'Scheduled' AND \"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourtProceedingId",
                table: "ScheduledEvents",
                column: "CourtProceedingId",
                unique: true,
                filter: "\"CourtProceedingId\" IS NOT NULL AND \"Status\" = 'Scheduled' AND \"RecordStatus\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_CourtCaseId_ActiveCourtProjection",
                table: "ScheduledEvents");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_CourtProceedingId",
                table: "ScheduledEvents");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourtProceedingId",
                table: "ScheduledEvents",
                column: "CourtProceedingId",
                unique: true,
                filter: "\"CourtProceedingId\" IS NOT NULL AND \"Status\" != 'Cancelled' AND \"RecordStatus\" = 'Active'");
        }
    }
}
