using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutwardWorkflowFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Outwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutwardNumber = table.Column<string>(type: "text", nullable: false),
                    NormalizedOutwardNumber = table.Column<string>(type: "text", nullable: false),
                    OutwardDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    RecipientName = table.Column<string>(type: "text", nullable: false),
                    RecipientDesignation = table.Column<string>(type: "text", nullable: true),
                    RecipientDepartment = table.Column<string>(type: "text", nullable: true),
                    RecipientAddress = table.Column<string>(type: "text", nullable: true),
                    RecipientEmail = table.Column<string>(type: "text", nullable: true),
                    RecipientPhone = table.Column<string>(type: "text", nullable: true),
                    IssuingDeskId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficeReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MainDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DispatchMode = table.Column<string>(type: "text", nullable: true),
                    DispatchReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    DispatchedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outwards_AppUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outwards_AppUsers_DispatchedByUserId",
                        column: x => x.DispatchedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outwards_Documents_MainDocumentId",
                        column: x => x.MainDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outwards_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outwards_OfficeDesks_IssuingDeskId",
                        column: x => x.IssuingDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outwards_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutwardAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutwardId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_OutwardAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardAttachments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardAttachments_Outwards_OutwardId",
                        column: x => x.OutwardId,
                        principalTable: "Outwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutwardDakLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    RelationshipType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardDakLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardDakLinks_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardDakLinks_Outwards_OutwardId",
                        column: x => x.OutwardId,
                        principalTable: "Outwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutwardEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionByDisplayNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DakId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DispatchMode = table.Column<string>(type: "text", nullable: true),
                    DispatchReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardEvents_AppUsers_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardEvents_Outwards_OutwardId",
                        column: x => x.OutwardId,
                        principalTable: "Outwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutwardAttachments_DocumentId",
                table: "OutwardAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardAttachments_OutwardId_DocumentId",
                table: "OutwardAttachments",
                columns: new[] { "OutwardId", "DocumentId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardDakLinks_DakId",
                table: "OutwardDakLinks",
                column: "DakId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardDakLinks_OutwardId",
                table: "OutwardDakLinks",
                column: "OutwardId",
                unique: true,
                filter: "\"IsPrimary\" = true AND \"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardDakLinks_OutwardId_DakId",
                table: "OutwardDakLinks",
                columns: new[] { "OutwardId", "DakId" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardEvents_ActionByUserId",
                table: "OutwardEvents",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardEvents_OutwardId_SequenceNumber",
                table: "OutwardEvents",
                columns: new[] { "OutwardId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_CancelledByUserId",
                table: "Outwards",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_DispatchedByUserId",
                table: "Outwards",
                column: "DispatchedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_IssuingDeskId",
                table: "Outwards",
                column: "IssuingDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_MainDocumentId",
                table: "Outwards",
                column: "MainDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_MatterId",
                table: "Outwards",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_NormalizedOutwardNumber",
                table: "Outwards",
                column: "NormalizedOutwardNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_OutwardNumber",
                table: "Outwards",
                column: "OutwardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_Status_OutwardDate",
                table: "Outwards",
                columns: new[] { "Status", "OutwardDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Outwards_WorkstreamId",
                table: "Outwards",
                column: "WorkstreamId");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION prevent_outward_events_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'OutwardEvents table is strictly append-only. UPDATE and DELETE are prohibited.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_outward_events_immutable
BEFORE UPDATE OR DELETE ON ""OutwardEvents""
FOR EACH ROW
EXECUTE FUNCTION prevent_outward_events_mutation();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_outward_events_immutable ON ""OutwardEvents"";
DROP FUNCTION IF EXISTS prevent_outward_events_mutation();
");

            migrationBuilder.DropTable(
                name: "OutwardAttachments");

            migrationBuilder.DropTable(
                name: "OutwardDakLinks");

            migrationBuilder.DropTable(
                name: "OutwardEvents");

            migrationBuilder.DropTable(
                name: "Outwards");
        }
    }
}
