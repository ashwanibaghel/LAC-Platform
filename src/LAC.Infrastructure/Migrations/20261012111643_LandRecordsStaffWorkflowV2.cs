using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LandRecordsStaffWorkflowV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFinalized",
                table: "Notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NumberAbsentReason",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDetached",
                table: "DocumentAwards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSharedSource",
                table: "DocumentAwards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LegalDate",
                table: "DocumentAwards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartNumber",
                table: "DocumentAwards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PossessionEventId",
                table: "DocumentAwards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "DocumentAwards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "DocumentAwards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "DocumentAwards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "DocumentAwards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VillageId",
                table: "DocumentAwards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveKind",
                table: "Awards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Awards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LandRecordMutationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MutationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandRecordMutationReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryAcquisitionProceedings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nature = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    ActRegime = table.Column<string>(type: "text", nullable: false),
                    LegalSection = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    DurationMonths = table.Column<int>(type: "integer", nullable: true),
                    LegalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CommencementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompensationReference = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryAcquisitionProceedings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryAcquisitionProceedings_AcquisitionProjects_Acquisi~",
                        column: x => x.AcquisitionProjectId,
                        principalTable: "AcquisitionProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryAcquisitionProceedings_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryAcquisitionProceedings_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryAcquisitionProceedings_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAwards_PossessionEventId",
                table: "DocumentAwards",
                column: "PossessionEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAwards_VillageId",
                table: "DocumentAwards",
                column: "VillageId");

            migrationBuilder.CreateIndex(
                name: "IX_LandRecordMutationReceipts_Scope_MutationId",
                table: "LandRecordMutationReceipts",
                columns: new[] { "Scope", "MutationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAcquisitionProceedings_AcquisitionProjectId",
                table: "TemporaryAcquisitionProceedings",
                column: "AcquisitionProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAcquisitionProceedings_AwardId",
                table: "TemporaryAcquisitionProceedings",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAcquisitionProceedings_DocumentId_VillageId",
                table: "TemporaryAcquisitionProceedings",
                columns: new[] { "DocumentId", "VillageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAcquisitionProceedings_VillageId",
                table: "TemporaryAcquisitionProceedings",
                column: "VillageId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAwards_PossessionEvents_PossessionEventId",
                table: "DocumentAwards",
                column: "PossessionEventId",
                principalTable: "PossessionEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAwards_Villages_VillageId",
                table: "DocumentAwards",
                column: "VillageId",
                principalTable: "Villages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Only unambiguous historical context is backfilled. No PP date or
            // event identity is inferred from upload timestamps or matching dates.
            migrationBuilder.Sql("""
                WITH single_village AS (
                    SELECT "AwardId", (array_agg("VillageId" ORDER BY "VillageId"))[1] AS village
                    FROM "AwardVillages" GROUP BY "AwardId" HAVING count(*) = 1
                ), changed AS (
                    UPDATE "DocumentAwards" d SET "VillageId" = s.village
                    FROM single_village s WHERE d."AwardId" = s."AwardId" AND d."VillageId" IS NULL
                    RETURNING d."Id", d."VillageId"
                )
                INSERT INTO "AuditLogs" ("Id", "EntityType", "EntityId", "Action", "ChangedAt", "ChangedBy", "OldValues", "NewValues")
                SELECT gen_random_uuid(), 'DocumentAward', "Id", 'StaffV2UnambiguousVillageBackfill', CURRENT_TIMESTAMP,
                    'Migration:LandRecordsStaffWorkflowV2', '{"VillageId":null}', json_build_object('VillageId', "VillageId")::text FROM changed;

                WITH changed AS (
                    UPDATE "DocumentAwards" a SET "CoreDocumentRole" = CASE WHEN d."DocumentType" = 'Award PDF' THEN 'Award' ELSE d."DocumentType" END
                    FROM "Documents" d WHERE a."DocumentId" = d."Id" AND a."CoreDocumentRole" IS NULL
                      AND d."DocumentType" IN ('Award PDF', 'Award', 'NM', 'StatementA', 'PossessionProceeding')
                    RETURNING a."Id", a."CoreDocumentRole"
                )
                INSERT INTO "AuditLogs" ("Id", "EntityType", "EntityId", "Action", "ChangedAt", "ChangedBy", "OldValues", "NewValues")
                SELECT gen_random_uuid(), 'DocumentAward', "Id", 'StaffV2KnownRoleBackfill', CURRENT_TIMESTAMP,
                    'Migration:LandRecordsStaffWorkflowV2', '{"CoreDocumentRole":null}', json_build_object('CoreDocumentRole', "CoreDocumentRole")::text FROM changed;

                INSERT INTO "DocumentVillages" ("Id", "DocumentId", "VillageId")
                SELECT gen_random_uuid(), a."DocumentId", a."VillageId" FROM "DocumentAwards" a
                WHERE a."VillageId" IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM "DocumentVillages" v WHERE v."DocumentId" = a."DocumentId" AND v."VillageId" = a."VillageId")
                GROUP BY a."DocumentId", a."VillageId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "TemporaryAcquisitionProceedings")
                       OR EXISTS (SELECT 1 FROM "LandRecordMutationReceipts")
                       OR EXISTS (SELECT 1 FROM "DocumentAwards" WHERE "Revision" > 0 OR "PossessionEventId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM "AuditLogs" WHERE "Action" IN ('StaffCoreDocumentAttached', 'StaffStoredAwardPdfLinked', 'StaffPdfOwnershipResolved', 'StaffNotificationSourceLinked'))
                    THEN RAISE EXCEPTION 'Staff workflow contains official evidence. Generic rollback is blocked; use a reviewed backup restore or preserving migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentAwards_PossessionEvents_PossessionEventId",
                table: "DocumentAwards");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentAwards_Villages_VillageId",
                table: "DocumentAwards");

            migrationBuilder.DropTable(
                name: "LandRecordMutationReceipts");

            migrationBuilder.DropTable(
                name: "TemporaryAcquisitionProceedings");

            migrationBuilder.DropIndex(
                name: "IX_DocumentAwards_PossessionEventId",
                table: "DocumentAwards");

            migrationBuilder.DropIndex(
                name: "IX_DocumentAwards_VillageId",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "IsFinalized",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "NumberAbsentReason",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsDetached",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "IsSharedSource",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "LegalDate",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "PartNumber",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "PossessionEventId",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "VillageId",
                table: "DocumentAwards");

            migrationBuilder.DropColumn(
                name: "ArchiveKind",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Awards");
        }
    }
}
