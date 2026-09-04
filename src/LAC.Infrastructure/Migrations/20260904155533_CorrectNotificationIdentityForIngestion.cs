using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectNotificationIdentityForIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_SectionType_NotificationNumber",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SectionType_NotificationNumber_NotificationDa~",
                table: "Notifications",
                columns: new[] { "SectionType", "NotificationNumber", "NotificationDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_SectionType_NotificationNumber_NotificationDa~",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SectionType_NotificationNumber",
                table: "Notifications",
                columns: new[] { "SectionType", "NotificationNumber" },
                unique: true);
        }
    }
}
