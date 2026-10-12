using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenOfficeNotingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OfficeNotingGuards.Up);
            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSnapshots_ActorUserId",
                table: "MatterOfficeSnapshots",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficePreviews_ActorUserId",
                table: "MatterOfficePreviews",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficePreviews_MatterId",
                table: "MatterOfficePreviews",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficePreviews_SourceDakId",
                table: "MatterOfficePreviews",
                column: "SourceDakId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterGreenSheetSources_ActorUserId",
                table: "MatterGreenSheetSources",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatterGreenSheetSources_AppUsers_ActorUserId",
                table: "MatterGreenSheetSources",
                column: "ActorUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficePreviews_AppUsers_ActorUserId",
                table: "MatterOfficePreviews",
                column: "ActorUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficePreviews_Daks_SourceDakId",
                table: "MatterOfficePreviews",
                column: "SourceDakId",
                principalTable: "Daks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficePreviews_MatterOfficeSessions_SessionId",
                table: "MatterOfficePreviews",
                column: "SessionId",
                principalTable: "MatterOfficeSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficePreviews_Matters_MatterId",
                table: "MatterOfficePreviews",
                column: "MatterId",
                principalTable: "Matters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficeSaveRequests_AppUsers_ActorUserId",
                table: "MatterOfficeSaveRequests",
                column: "ActorUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficeSnapshots_AppUsers_ActorUserId",
                table: "MatterOfficeSnapshots",
                column: "ActorUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OfficeNotingGuards.Down);
            migrationBuilder.DropForeignKey(
                name: "FK_MatterGreenSheetSources_AppUsers_ActorUserId",
                table: "MatterGreenSheetSources");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficePreviews_AppUsers_ActorUserId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficePreviews_Daks_SourceDakId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficePreviews_MatterOfficeSessions_SessionId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficePreviews_Matters_MatterId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficeSaveRequests_AppUsers_ActorUserId",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficeSnapshots_AppUsers_ActorUserId",
                table: "MatterOfficeSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_MatterOfficeSnapshots_ActorUserId",
                table: "MatterOfficeSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_MatterOfficePreviews_ActorUserId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropIndex(
                name: "IX_MatterOfficePreviews_MatterId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropIndex(
                name: "IX_MatterOfficePreviews_SourceDakId",
                table: "MatterOfficePreviews");

            migrationBuilder.DropIndex(
                name: "IX_MatterGreenSheetSources_ActorUserId",
                table: "MatterGreenSheetSources");
        }
    }
}
