using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtLitigationWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "CourtProceedings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RestraintNature",
                table: "CourtProceedings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrderType",
                table: "CourtProceedings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Remarks",
                table: "CourtCases",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CurrentStatus",
                table: "CourtCases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CourtName",
                table: "CourtCases",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CaseType",
                table: "CourtCases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CaseNumber",
                table: "CourtCases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedUserId",
                table: "CourtCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaseTitle",
                table: "CourtCases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DisposedDate",
                table: "CourtCases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponsibleOfficeDeskId",
                table: "CourtCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CourtCases",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "CourtCaseDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtProceedingId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseDocuments_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseDocuments_CourtProceedings_CourtProceedingId",
                        column: x => x.CourtProceedingId,
                        principalTable: "CourtProceedings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActorDesignationSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CaseNumberSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CaseTitleSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WorkstreamIdSnapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstreamNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDeskNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceUserDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDeskNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetUserDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OldStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtCasePartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtCaseRepresentativeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtProceedingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseEvents_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseEvents_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseMatters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseMatters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseMatters_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseMatters_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FatherOrSpouseName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AddressText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseParties_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseParties_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseRepresentatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCasePartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RepresentativeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RepresentsRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseRepresentatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseRepresentatives_CourtCaseParties_CourtCasePartyId",
                        column: x => x.CourtCasePartyId,
                        principalTable: "CourtCaseParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseRepresentatives_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtProceedings_CourtCaseId_ProceedingDate_CreatedAt",
                table: "CourtProceedings",
                columns: new[] { "CourtCaseId", "ProceedingDate", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_AssignedUserId",
                table: "CourtCases",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_CaseNumber",
                table: "CourtCases",
                column: "CaseNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_CourtName",
                table: "CourtCases",
                column: "CourtName");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_CurrentStatus",
                table: "CourtCases",
                column: "CurrentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_DisposedDate",
                table: "CourtCases",
                column: "DisposedDate");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_FiledDate",
                table: "CourtCases",
                column: "FiledDate");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCases_ResponsibleOfficeDeskId",
                table: "CourtCases",
                column: "ResponsibleOfficeDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseDocuments_CourtCaseId",
                table: "CourtCaseDocuments",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseDocuments_CourtCaseId_DocumentId",
                table: "CourtCaseDocuments",
                columns: new[] { "CourtCaseId", "DocumentId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseDocuments_CourtProceedingId",
                table: "CourtCaseDocuments",
                column: "CourtProceedingId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseDocuments_DocumentId",
                table: "CourtCaseDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseEvents_ActionAt",
                table: "CourtCaseEvents",
                column: "ActionAt");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseEvents_ActorUserId",
                table: "CourtCaseEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseEvents_CourtCaseId",
                table: "CourtCaseEvents",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseEvents_CourtCaseId_SequenceNumber",
                table: "CourtCaseEvents",
                columns: new[] { "CourtCaseId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseMatters_CourtCaseId",
                table: "CourtCaseMatters",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseMatters_CourtCaseId_MatterId",
                table: "CourtCaseMatters",
                columns: new[] { "CourtCaseId", "MatterId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseMatters_MatterId",
                table: "CourtCaseMatters",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseParties_CourtCaseId",
                table: "CourtCaseParties",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseParties_PartyId",
                table: "CourtCaseParties",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseRepresentatives_CourtCaseId",
                table: "CourtCaseRepresentatives",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseRepresentatives_CourtCasePartyId",
                table: "CourtCaseRepresentatives",
                column: "CourtCasePartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CourtCases_AppUsers_AssignedUserId",
                table: "CourtCases",
                column: "AssignedUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CourtCases_OfficeDesks_ResponsibleOfficeDeskId",
                table: "CourtCases",
                column: "ResponsibleOfficeDeskId",
                principalTable: "OfficeDesks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_court_case_events_immutable()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Court case events are strictly immutable. Official litigation history cannot be modified or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_court_case_events_immutable
                BEFORE UPDATE OR DELETE ON ""CourtCaseEvents""
                FOR EACH ROW
                EXECUTE FUNCTION fn_court_case_events_immutable();

                INSERT INTO ""Permissions"" (""Id"", ""Code"", ""Name"", ""Description"", ""Category"")
                VALUES
                    (gen_random_uuid(), 'Court.View', 'View Court Cases', 'View court cases, litigation workspace, proceedings, and documents', 'Court / Litigation'),
                    (gen_random_uuid(), 'Court.Create', 'Create Court Cases', 'Register new court cases and litigation records', 'Court / Litigation'),
                    (gen_random_uuid(), 'Court.Edit', 'Edit Court Cases', 'Edit court case metadata, parties, counsel, and linked records', 'Court / Litigation'),
                    (gen_random_uuid(), 'Court.Assign', 'Assign Court Cases', 'Assign and reassign court cases to responsible desks and officers', 'Court / Litigation'),
                    (gen_random_uuid(), 'Court.Proceeding.Manage', 'Manage Court Proceedings', 'Record court proceedings, hearing summaries, and next dates', 'Court / Litigation'),
                    (gen_random_uuid(), 'Court.Document.Manage', 'Manage Court Documents', 'Upload, link, and manage official court case documents', 'Court / Litigation')
                ON CONFLICT (""Code"") DO NOTHING;

                INSERT INTO ""RolePermissions"" (""Id"", ""RoleId"", ""PermissionId"", ""ScopeMode"")
                SELECT gen_random_uuid(), r.""Id"", p.""Id"", 'All'
                FROM ""Roles"" r
                CROSS JOIN ""Permissions"" p
                WHERE r.""Code"" = 'SYSTEM_ADMIN'
                  AND p.""Code"" IN ('Court.View', 'Court.Create', 'Court.Edit', 'Court.Assign', 'Court.Proceeding.Manage', 'Court.Document.Manage')
                  AND NOT EXISTS (
                      SELECT 1 FROM ""RolePermissions"" rp
                      WHERE rp.""RoleId"" = r.""Id"" AND rp.""PermissionId"" = p.""Id""
                  );

                INSERT INTO ""RolePermissions"" (""Id"", ""RoleId"", ""PermissionId"", ""ScopeMode"")
                SELECT gen_random_uuid(), rp.""RoleId"", target_perm.""Id"", rp.""ScopeMode""
                FROM ""RolePermissions"" rp
                JOIN ""Permissions"" src_perm ON rp.""PermissionId"" = src_perm.""Id"" AND src_perm.""Code"" = 'Award.View'
                JOIN ""Permissions"" target_perm ON target_perm.""Code"" = 'Court.View'
                WHERE rp.""ScopeMode"" IN ('All', 'Workstream')
                  AND NOT EXISTS (
                      SELECT 1 FROM ""RolePermissions"" existing
                      WHERE existing.""RoleId"" = rp.""RoleId"" AND existing.""PermissionId"" = target_perm.""Id""
                  );

                INSERT INTO ""RolePermissions"" (""Id"", ""RoleId"", ""PermissionId"", ""ScopeMode"")
                SELECT gen_random_uuid(), rp.""RoleId"", target_perm.""Id"", rp.""ScopeMode""
                FROM ""RolePermissions"" rp
                JOIN ""Permissions"" src_perm ON rp.""PermissionId"" = src_perm.""Id"" AND src_perm.""Code"" = 'Award.Edit'
                CROSS JOIN ""Permissions"" target_perm
                WHERE src_perm.""Code"" = 'Award.Edit'
                  AND target_perm.""Code"" IN ('Court.Create', 'Court.Edit', 'Court.Proceeding.Manage', 'Court.Document.Manage')
                  AND rp.""ScopeMode"" IN ('All', 'Workstream')
                  AND NOT EXISTS (
                      SELECT 1 FROM ""RolePermissions"" existing
                      WHERE existing.""RoleId"" = rp.""RoleId"" AND existing.""PermissionId"" = target_perm.""Id""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourtCases_AppUsers_AssignedUserId",
                table: "CourtCases");

            migrationBuilder.DropForeignKey(
                name: "FK_CourtCases_OfficeDesks_ResponsibleOfficeDeskId",
                table: "CourtCases");

            migrationBuilder.DropTable(
                name: "CourtCaseDocuments");

            migrationBuilder.DropTable(
                name: "CourtCaseEvents");

            migrationBuilder.DropTable(
                name: "CourtCaseMatters");

            migrationBuilder.DropTable(
                name: "CourtCaseRepresentatives");

            migrationBuilder.DropTable(
                name: "CourtCaseParties");

            migrationBuilder.DropIndex(
                name: "IX_CourtProceedings_CourtCaseId_ProceedingDate_CreatedAt",
                table: "CourtProceedings");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_AssignedUserId",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_CaseNumber",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_CourtName",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_CurrentStatus",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_DisposedDate",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_FiledDate",
                table: "CourtCases");

            migrationBuilder.DropIndex(
                name: "IX_CourtCases_ResponsibleOfficeDeskId",
                table: "CourtCases");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "CourtCases");

            migrationBuilder.DropColumn(
                name: "CaseTitle",
                table: "CourtCases");

            migrationBuilder.DropColumn(
                name: "DisposedDate",
                table: "CourtCases");

            migrationBuilder.DropColumn(
                name: "ResponsibleOfficeDeskId",
                table: "CourtCases");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CourtCases");

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "CourtProceedings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RestraintNature",
                table: "CourtProceedings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrderType",
                table: "CourtProceedings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Remarks",
                table: "CourtCases",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CurrentStatus",
                table: "CourtCases",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CourtName",
                table: "CourtCases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "CaseType",
                table: "CourtCases",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CaseNumber",
                table: "CourtCases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_court_case_events_immutable ON ""CourtCaseEvents"";
                DROP FUNCTION IF EXISTS fn_court_case_events_immutable();
            ");
        }
    }
}
