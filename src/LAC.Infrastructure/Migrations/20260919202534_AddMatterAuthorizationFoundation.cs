using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterAuthorizationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Matters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstreamId",
                table: "Matters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionByDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatterDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetWorkstreamId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterEvents_AppUsers_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterEvents_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matters_WorkstreamId",
                table: "Matters",
                column: "WorkstreamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterEvents_ActionByUserId",
                table: "MatterEvents",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterEvents_MatterId_SequenceNumber",
                table: "MatterEvents",
                columns: new[] { "MatterId", "SequenceNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Matters_Workstreams_WorkstreamId",
                table: "Matters",
                column: "WorkstreamId",
                principalTable: "Workstreams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION trg_prevent_matter_events_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'MatterEvent records are strictly append-only and immutable.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_matter_events_immutable
BEFORE UPDATE OR DELETE ON ""MatterEvents""
FOR EACH ROW
EXECUTE FUNCTION trg_prevent_matter_events_mutation();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_matter_events_immutable ON ""MatterEvents"";
DROP FUNCTION IF EXISTS trg_prevent_matter_events_mutation();
");

            migrationBuilder.DropForeignKey(
                name: "FK_Matters_Workstreams_WorkstreamId",
                table: "Matters");

            migrationBuilder.DropTable(
                name: "MatterEvents");

            migrationBuilder.DropIndex(
                name: "IX_Matters_WorkstreamId",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "WorkstreamId",
                table: "Matters");
        }
    }
}
