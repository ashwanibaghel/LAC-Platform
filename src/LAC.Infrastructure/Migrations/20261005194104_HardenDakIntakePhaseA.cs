using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenDakIntakePhaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Audit under a write lock before installing constraints. Never rewrite historical facts.
            migrationBuilder.Sql("""
                LOCK TABLE "Daks" IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Daks" WHERE "RecordStatus" = 'Active'
                        AND length(translate(btrim("DiaryNumber", E' \t\r\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')) = 0) THEN
                        RAISE EXCEPTION USING MESSAGE = 'Dak intake migration blocked: active receipts contain blank diary numbers.',
                            HINT = 'Run scripts/audit-dak-diary.sql read-only. Review against original stamps; do not invent numbers.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "Daks" WHERE "RecordStatus" = 'Active'
                        GROUP BY translate(btrim("DiaryNumber", E' \t\r\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') HAVING count(*) > 1) THEN
                        RAISE EXCEPTION USING MESSAGE = 'Dak intake migration blocked: duplicate canonical active diary numbers exist.',
                            HINT = 'Run scripts/audit-dak-diary.sql read-only. Explicitly review duplicates before retrying; no automatic merge or renumbering is performed.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "HasPhysicalOriginal",
                table: "Daks",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalOriginalDeskId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicalOriginalLocationNote",
                table: "Daks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicalOriginalProvenanceNote",
                table: "Daks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhysicalOriginalUpdatedAt",
                table: "Daks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalOriginalUpdatedByUserId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalOriginalUserId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegisteredByUserId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationRequestHash",
                table: "Daks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegistrationRequestId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiaryNumberKey",
                table: "Daks",
                type: "text",
                nullable: false,
                computedColumnSql: "translate(btrim(\"DiaryNumber\", E' \\t\\r\\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Daks_DiaryNumberKey",
                table: "Daks",
                column: "DiaryNumberKey",
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_PhysicalOriginalDeskId",
                table: "Daks",
                column: "PhysicalOriginalDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_PhysicalOriginalUpdatedByUserId",
                table: "Daks",
                column: "PhysicalOriginalUpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_PhysicalOriginalUserId",
                table: "Daks",
                column: "PhysicalOriginalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_RegisteredByUserId_RegistrationRequestId",
                table: "Daks",
                columns: new[] { "RegisteredByUserId", "RegistrationRequestId" },
                unique: true,
                filter: "\"RegistrationRequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Daks_ActiveDiaryNumber",
                table: "Daks",
                sql: "\"RecordStatus\" <> 'Active' OR length(\"DiaryNumberKey\") > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Daks_AppUsers_PhysicalOriginalUpdatedByUserId",
                table: "Daks",
                column: "PhysicalOriginalUpdatedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Daks_AppUsers_PhysicalOriginalUserId",
                table: "Daks",
                column: "PhysicalOriginalUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Daks_AppUsers_RegisteredByUserId",
                table: "Daks",
                column: "RegisteredByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Daks_OfficeDesks_PhysicalOriginalDeskId",
                table: "Daks",
                column: "PhysicalOriginalDeskId",
                principalTable: "OfficeDesks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Daks_AppUsers_PhysicalOriginalUpdatedByUserId",
                table: "Daks");

            migrationBuilder.DropForeignKey(
                name: "FK_Daks_AppUsers_PhysicalOriginalUserId",
                table: "Daks");

            migrationBuilder.DropForeignKey(
                name: "FK_Daks_AppUsers_RegisteredByUserId",
                table: "Daks");

            migrationBuilder.DropForeignKey(
                name: "FK_Daks_OfficeDesks_PhysicalOriginalDeskId",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_Daks_DiaryNumberKey",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_Daks_PhysicalOriginalDeskId",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_Daks_PhysicalOriginalUpdatedByUserId",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_Daks_PhysicalOriginalUserId",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_Daks_RegisteredByUserId_RegistrationRequestId",
                table: "Daks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Daks_ActiveDiaryNumber",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "DiaryNumberKey",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "HasPhysicalOriginal",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalDeskId",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalLocationNote",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalProvenanceNote",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalUpdatedAt",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalUpdatedByUserId",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "PhysicalOriginalUserId",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "RegisteredByUserId",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "RegistrationRequestHash",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "RegistrationRequestId",
                table: "Daks");
        }
    }
}
