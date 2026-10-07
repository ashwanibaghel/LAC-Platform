using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicWorkAllocationAndSessionSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "WorkItemEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "WorkItemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "ScheduledEventEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "ScheduledEventEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "RecordAccessEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "RecordAccessEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "OutwardEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "OutwardEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "MatterEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "MatterEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "DakMovements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "DakMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "CourtCaseEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "CourtCaseEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorDisplayNameSnapshot",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorLabel",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OnBehalfOfUserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssistantRevision",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionVersion",
                table: "AppUsers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SupervisingOfficerId",
                table: "AppUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TemporaryCredentialExpiresAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantPermissionLimits",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantPermissionLimits", x => new { x.UserId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_AssistantPermissionLimits_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistantPermissionLimits_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkDefinitions_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WorkOrderReference = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DelegatedFromAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAllocations", x => x.Id);
                    table.CheckConstraint("CK_WorkAllocation_Interval", "\"ValidTo\" IS NULL OR \"ValidTo\" > \"ValidFrom\"");
                    table.ForeignKey(
                        name: "FK_WorkAllocations_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAllocations_WorkAllocations_DelegatedFromAllocationId",
                        column: x => x.DelegatedFromAllocationId,
                        principalTable: "WorkAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAllocations_WorkDefinitions_WorkDefinitionId",
                        column: x => x.WorkDefinitionId,
                        principalTable: "WorkDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkAllocationScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubDivisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAllocationScopes", x => x.Id);
                    table.CheckConstraint("CK_WorkAllocationScope_Target", "(\"Kind\" = 'Global' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NULL) OR (\"Kind\" = 'District' AND \"DistrictId\" IS NOT NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NULL) OR (\"Kind\" = 'Subdivision' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NOT NULL AND \"VillageId\" IS NULL) OR (\"Kind\" = 'Village' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkAllocationScopes_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAllocationScopes_SubDivisions_SubDivisionId",
                        column: x => x.SubDivisionId,
                        principalTable: "SubDivisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAllocationScopes_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkAllocationScopes_WorkAllocations_WorkAllocationId",
                        column: x => x.WorkAllocationId,
                        principalTable: "WorkAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_SupervisingOfficerId",
                table: "AppUsers",
                column: "SupervisingOfficerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppUser_Supervisor",
                table: "AppUsers",
                sql: "\"SupervisingOfficerId\" IS NULL OR \"SupervisingOfficerId\" <> \"Id\"");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantPermissionLimits_PermissionId",
                table: "AssistantPermissionLimits",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocations_DelegatedFromAllocationId",
                table: "WorkAllocations",
                column: "DelegatedFromAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocations_UserId_WorkDefinitionId_ValidFrom",
                table: "WorkAllocations",
                columns: new[] { "UserId", "WorkDefinitionId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocations_WorkDefinitionId",
                table: "WorkAllocations",
                column: "WorkDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocationScopes_DistrictId",
                table: "WorkAllocationScopes",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocationScopes_SubDivisionId",
                table: "WorkAllocationScopes",
                column: "SubDivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocationScopes_VillageId",
                table: "WorkAllocationScopes",
                column: "VillageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkAllocationScopes_WorkAllocationId",
                table: "WorkAllocationScopes",
                column: "WorkAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkDefinitions_Code",
                table: "WorkDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkDefinitions_WorkstreamId",
                table: "WorkDefinitions",
                column: "WorkstreamId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_AppUsers_SupervisingOfficerId",
                table: "AppUsers",
                column: "SupervisingOfficerId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Invalidate pre-upgrade cookies and remove historical credential copies.
            migrationBuilder.Sql("""
                UPDATE "AppUsers" SET "SessionVersion" = gen_random_uuid();
                UPDATE "AuditLogs" SET
                    "OldValues" = CASE WHEN "OldValues" IS NULL THEN NULL ELSE
                        ("OldValues"::jsonb - ARRAY['PasswordHash','PasswordChangedAt','SessionVersion','SecurityStamp','MustChangePassword','TemporaryCredentialExpiresAt','Credential','Token','Secret'])::text END,
                    "NewValues" = CASE WHEN "NewValues" IS NULL THEN NULL ELSE
                        ("NewValues"::jsonb - ARRAY['PasswordHash','PasswordChangedAt','SessionVersion','SecurityStamp','MustChangePassword','TemporaryCredentialExpiresAt','Credential','Token','Secret'])::text END
                WHERE "EntityType" = 'AppUser';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_AppUsers_SupervisingOfficerId",
                table: "AppUsers");

            migrationBuilder.DropTable(
                name: "AssistantPermissionLimits");

            migrationBuilder.DropTable(
                name: "WorkAllocationScopes");

            migrationBuilder.DropTable(
                name: "WorkAllocations");

            migrationBuilder.DropTable(
                name: "WorkDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_SupervisingOfficerId",
                table: "AppUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppUser_Supervisor",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "WorkItemEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "ScheduledEventEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "ScheduledEventEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "RecordAccessEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "RecordAccessEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "OutwardEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "OutwardEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "MatterEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "MatterEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "DakMovements");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "DakMovements");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "CourtCaseEvents");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "CourtCaseEvents");

            migrationBuilder.DropColumn(
                name: "ActorDisplayNameSnapshot",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorLabel",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfDisplayNameSnapshot",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OnBehalfOfUserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "AssistantRevision",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "SupervisingOfficerId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TemporaryCredentialExpiresAt",
                table: "AppUsers");
        }
    }
}
