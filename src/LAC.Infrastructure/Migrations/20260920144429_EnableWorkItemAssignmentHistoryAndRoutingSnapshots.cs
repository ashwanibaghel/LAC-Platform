using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnableWorkItemAssignmentHistoryAndRoutingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItemAssignments_WorkItemId",
                table: "WorkItemAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAssignmentId",
                table: "WorkItemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDeskId",
                table: "WorkItemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceDeskNameSnapshot",
                table: "WorkItemEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUserDisplayNameSnapshot",
                table: "WorkItemEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceUserId",
                table: "WorkItemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetAssignmentId",
                table: "WorkItemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetDeskNameSnapshot",
                table: "WorkItemEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetUserDisplayNameSnapshot",
                table: "WorkItemEvents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_WorkItemId",
                table: "WorkItemAssignments",
                column: "WorkItemId",
                unique: true,
                filter: "\"IsActive\" = true AND \"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_WorkItemId_AssignedAt",
                table: "WorkItemAssignments",
                columns: new[] { "WorkItemId", "AssignedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItemAssignments_WorkItemId",
                table: "WorkItemAssignments");

            migrationBuilder.DropIndex(
                name: "IX_WorkItemAssignments_WorkItemId_AssignedAt",
                table: "WorkItemAssignments");

            migrationBuilder.DropColumn(
                name: "SourceAssignmentId",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "SourceDeskId",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "SourceDeskNameSnapshot",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "SourceUserDisplayNameSnapshot",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "SourceUserId",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "TargetAssignmentId",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "TargetDeskNameSnapshot",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "TargetUserDisplayNameSnapshot",
                table: "WorkItemEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_WorkItemId",
                table: "WorkItemAssignments",
                column: "WorkItemId",
                unique: true);
        }
    }
}
