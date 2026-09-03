using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLrMigrationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_NotificationNumber",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards");

            migrationBuilder.AlterColumn<Guid>(
                name: "AcquisitionProjectId",
                table: "Notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "LREntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SectionType_NotificationNumber",
                table: "Notifications",
                columns: new[] { "SectionType", "NotificationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LREntries_AwardId",
                table: "LREntries",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_LREntries_Section4NotificationId",
                table: "LREntries",
                column: "Section4NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_LREntries_Section6NotificationId",
                table: "LREntries",
                column: "Section6NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards",
                column: "AwardNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LREntries_Awards_AwardId",
                table: "LREntries",
                column: "AwardId",
                principalTable: "Awards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LREntries_Notifications_Section4NotificationId",
                table: "LREntries",
                column: "Section4NotificationId",
                principalTable: "Notifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LREntries_Notifications_Section6NotificationId",
                table: "LREntries",
                column: "Section6NotificationId",
                principalTable: "Notifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LREntries_Awards_AwardId",
                table: "LREntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LREntries_Notifications_Section4NotificationId",
                table: "LREntries");

            migrationBuilder.DropForeignKey(
                name: "FK_LREntries_Notifications_Section6NotificationId",
                table: "LREntries");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_SectionType_NotificationNumber",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_LREntries_AwardId",
                table: "LREntries");

            migrationBuilder.DropIndex(
                name: "IX_LREntries_Section4NotificationId",
                table: "LREntries");

            migrationBuilder.DropIndex(
                name: "IX_LREntries_Section6NotificationId",
                table: "LREntries");

            migrationBuilder.DropIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "LREntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "AcquisitionProjectId",
                table: "Notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_NotificationNumber",
                table: "Notifications",
                column: "NotificationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards",
                column: "AwardNumber");
        }
    }
}
