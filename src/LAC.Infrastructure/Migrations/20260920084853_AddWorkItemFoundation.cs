using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    RequestedByDesignationSnapshot = table.Column<string>(type: "text", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItems_AppUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItems_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeDeskId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstSeenByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstActionByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_AppUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_AppUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_AppUsers_FirstActionByUserId",
                        column: x => x.FirstActionByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_AppUsers_FirstSeenByUserId",
                        column: x => x.FirstSeenByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_OfficeDesks_OfficeDeskId",
                        column: x => x.OfficeDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAssignments_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemContributors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemContributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemContributors_AppUsers_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemContributors_AppUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemContributors_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemContributors_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemDakLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemDakLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemDakLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemDakLinks_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ActionByDesignationSnapshot = table.Column<string>(type: "text", nullable: true),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromStatus = table.Column<string>(type: "text", nullable: true),
                    ToStatus = table.Column<string>(type: "text", nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkItemUpdateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContributorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextType = table.Column<string>(type: "text", nullable: true),
                    ContextEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemarksSnapshot = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemEvents_AppUsers_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemEvents_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemMatterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemMatterLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemMatterLinks_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemMatterLinks_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    AddedByDesignationSnapshot = table.Column<string>(type: "text", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemUpdates_AppUsers_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemUpdates_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    AttachmentType = table.Column<string>(type: "text", nullable: true),
                    WorkItemUpdateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemAttachments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAttachments_WorkItemUpdates_WorkItemUpdateId",
                        column: x => x.WorkItemUpdateId,
                        principalTable: "WorkItemUpdates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemAttachments_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_AssignedByUserId",
                table: "WorkItemAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_AssignedUserId",
                table: "WorkItemAssignments",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_FirstActionByUserId",
                table: "WorkItemAssignments",
                column: "FirstActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_FirstSeenByUserId",
                table: "WorkItemAssignments",
                column: "FirstSeenByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_OfficeDeskId",
                table: "WorkItemAssignments",
                column: "OfficeDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_OfficeDeskId_AssignedUserId_IsActive",
                table: "WorkItemAssignments",
                columns: new[] { "OfficeDeskId", "AssignedUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAssignments_WorkItemId",
                table: "WorkItemAssignments",
                column: "WorkItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAttachments_DocumentId",
                table: "WorkItemAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAttachments_WorkItemId_DocumentId",
                table: "WorkItemAttachments",
                columns: new[] { "WorkItemId", "DocumentId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemAttachments_WorkItemUpdateId",
                table: "WorkItemAttachments",
                column: "WorkItemUpdateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemContributors_AddedByUserId",
                table: "WorkItemContributors",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemContributors_ReviewedByUserId",
                table: "WorkItemContributors",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemContributors_UserId",
                table: "WorkItemContributors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemContributors_WorkItemId_UserId",
                table: "WorkItemContributors",
                columns: new[] { "WorkItemId", "UserId" },
                unique: true,
                filter: "\"IsActive\" = true AND \"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemDakLinks_DakId",
                table: "WorkItemDakLinks",
                column: "DakId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemDakLinks_WorkItemId_DakId",
                table: "WorkItemDakLinks",
                columns: new[] { "WorkItemId", "DakId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemEvents_ActionByUserId",
                table: "WorkItemEvents",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemEvents_WorkItemId_ActionAt",
                table: "WorkItemEvents",
                columns: new[] { "WorkItemId", "ActionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemEvents_WorkItemId_SequenceNumber",
                table: "WorkItemEvents",
                columns: new[] { "WorkItemId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemMatterLinks_MatterId",
                table: "WorkItemMatterLinks",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemMatterLinks_WorkItemId_MatterId",
                table: "WorkItemMatterLinks",
                columns: new[] { "WorkItemId", "MatterId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CreatedAt",
                table: "WorkItems",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_LastActivityAt",
                table: "WorkItems",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_RequestedByUserId",
                table: "WorkItems",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_Status_DueAt",
                table: "WorkItems",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_WorkstreamId",
                table: "WorkItems",
                column: "WorkstreamId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemUpdates_AddedByUserId",
                table: "WorkItemUpdates",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemUpdates_WorkItemId_AddedAt",
                table: "WorkItemUpdates",
                columns: new[] { "WorkItemId", "AddedAt" });

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_work_item_events_immutable()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Work item events are strictly immutable. Official event history cannot be modified or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_work_item_events_immutable
                BEFORE UPDATE OR DELETE ON ""WorkItemEvents""
                FOR EACH ROW
                EXECUTE FUNCTION fn_work_item_events_immutable();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_work_item_events_immutable ON ""WorkItemEvents"";
                DROP FUNCTION IF EXISTS fn_work_item_events_immutable();
            ");

            migrationBuilder.DropTable(
                name: "WorkItemAssignments");

            migrationBuilder.DropTable(
                name: "WorkItemAttachments");

            migrationBuilder.DropTable(
                name: "WorkItemContributors");

            migrationBuilder.DropTable(
                name: "WorkItemDakLinks");

            migrationBuilder.DropTable(
                name: "WorkItemEvents");

            migrationBuilder.DropTable(
                name: "WorkItemMatterLinks");

            migrationBuilder.DropTable(
                name: "WorkItemUpdates");

            migrationBuilder.DropTable(
                name: "WorkItems");
        }
    }
}
