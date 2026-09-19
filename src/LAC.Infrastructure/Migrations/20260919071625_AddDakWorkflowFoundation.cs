using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDakWorkflowFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DakCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DefaultPriority = table.Column<string>(type: "text", nullable: false),
                    DefaultWorkstreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakCategories_Workstreams_DefaultWorkstreamId",
                        column: x => x.DefaultWorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Daks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaryNumber = table.Column<string>(type: "text", nullable: false),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    SenderName = table.Column<string>(type: "text", nullable: false),
                    SenderDesignation = table.Column<string>(type: "text", nullable: true),
                    SenderDepartment = table.Column<string>(type: "text", nullable: true),
                    SenderAddress = table.Column<string>(type: "text", nullable: true),
                    SenderReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    SenderLetterDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InwardMode = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    MainDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Daks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Daks_DakCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "DakCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Daks_Documents_MainDocumentId",
                        column: x => x.MainDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Daks_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficeDeskId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakAssignments_AppUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakAssignments_AppUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakAssignments_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakAssignments_OfficeDesks_OfficeDeskId",
                        column: x => x.OfficeDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    AttachmentType = table.Column<string>(type: "text", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakAttachments_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakAttachments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakAwardLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakAwardLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakAwardLinks_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakAwardLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakKhasraLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakKhasraLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakKhasraLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakKhasraLinks_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakMatterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakMatterLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakMatterLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMatterLinks_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    FromDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromDeskCodeSnapshot = table.Column<string>(type: "text", nullable: true),
                    FromDeskNameSnapshot = table.Column<string>(type: "text", nullable: true),
                    FromUserDisplayNameSnapshot = table.Column<string>(type: "text", nullable: true),
                    ToDeskCodeSnapshot = table.Column<string>(type: "text", nullable: true),
                    ToDeskNameSnapshot = table.Column<string>(type: "text", nullable: true),
                    ToUserDisplayNameSnapshot = table.Column<string>(type: "text", nullable: true),
                    ActionByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    InstructionsSnapshot = table.Column<string>(type: "text", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakMovements_AppUsers_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_AppUsers_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_AppUsers_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_OfficeDesks_FromDeskId",
                        column: x => x.FromDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakMovements_OfficeDesks_ToDeskId",
                        column: x => x.ToDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakVillageLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakVillageLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DakVillageLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakVillageLinks_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DakAssignments_AssignedByUserId",
                table: "DakAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DakAssignments_AssignedUserId",
                table: "DakAssignments",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DakAssignments_DakId",
                table: "DakAssignments",
                column: "DakId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DakAssignments_OfficeDeskId",
                table: "DakAssignments",
                column: "OfficeDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_DakAttachments_DakId_DocumentId",
                table: "DakAttachments",
                columns: new[] { "DakId", "DocumentId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_DakAttachments_DocumentId",
                table: "DakAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DakAwardLinks_AwardId",
                table: "DakAwardLinks",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_DakAwardLinks_DakId_AwardId",
                table: "DakAwardLinks",
                columns: new[] { "DakId", "AwardId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_DakCategories_Code",
                table: "DakCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DakCategories_DefaultWorkstreamId",
                table: "DakCategories",
                column: "DefaultWorkstreamId");

            migrationBuilder.CreateIndex(
                name: "IX_DakKhasraLinks_DakId_KhasraId",
                table: "DakKhasraLinks",
                columns: new[] { "DakId", "KhasraId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_DakKhasraLinks_KhasraId",
                table: "DakKhasraLinks",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMatterLinks_DakId_MatterId",
                table: "DakMatterLinks",
                columns: new[] { "DakId", "MatterId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_DakMatterLinks_MatterId",
                table: "DakMatterLinks",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_ActionByUserId",
                table: "DakMovements",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_DakId_ActionAt",
                table: "DakMovements",
                columns: new[] { "DakId", "ActionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_DakId_SequenceNumber",
                table: "DakMovements",
                columns: new[] { "DakId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_DocumentId",
                table: "DakMovements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_FromDeskId",
                table: "DakMovements",
                column: "FromDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_FromUserId",
                table: "DakMovements",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_ToDeskId",
                table: "DakMovements",
                column: "ToDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_ToUserId",
                table: "DakMovements",
                column: "ToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_CategoryId",
                table: "Daks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_DiaryNumber",
                table: "Daks",
                column: "DiaryNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_MainDocumentId",
                table: "Daks",
                column: "MainDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_Priority",
                table: "Daks",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Daks_Status_ReceivedDate",
                table: "Daks",
                columns: new[] { "Status", "ReceivedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Daks_WorkstreamId",
                table: "Daks",
                column: "WorkstreamId");

            migrationBuilder.CreateIndex(
                name: "IX_DakVillageLinks_DakId_VillageId",
                table: "DakVillageLinks",
                columns: new[] { "DakId", "VillageId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_DakVillageLinks_VillageId",
                table: "DakVillageLinks",
                column: "VillageId");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION prevent_dak_movement_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Dak movements are strictly immutable. Official movement history cannot be modified or deleted.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_dak_movement_immutable
BEFORE UPDATE OR DELETE ON ""DakMovements""
FOR EACH ROW
EXECUTE FUNCTION prevent_dak_movement_mutation();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_dak_movement_immutable ON ""DakMovements"";
DROP FUNCTION IF EXISTS prevent_dak_movement_mutation();
");

            migrationBuilder.DropTable(
                name: "DakAssignments");

            migrationBuilder.DropTable(
                name: "DakAttachments");

            migrationBuilder.DropTable(
                name: "DakAwardLinks");

            migrationBuilder.DropTable(
                name: "DakKhasraLinks");

            migrationBuilder.DropTable(
                name: "DakMatterLinks");

            migrationBuilder.DropTable(
                name: "DakMovements");

            migrationBuilder.DropTable(
                name: "DakVillageLinks");

            migrationBuilder.DropTable(
                name: "Daks");

            migrationBuilder.DropTable(
                name: "DakCategories");
        }
    }
}
