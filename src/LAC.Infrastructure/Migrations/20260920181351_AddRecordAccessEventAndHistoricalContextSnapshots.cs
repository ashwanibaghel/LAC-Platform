using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordAccessEventAndHistoricalContextSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IssuingDeskIdSnapshot",
                table: "OutwardEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstreamIdSnapshot",
                table: "OutwardEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceWorkstreamId",
                table: "MatterEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstreamIdSnapshot",
                table: "MatterEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecordAccessEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextEntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContextEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficeDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentTitleSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordAccessEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordAccessEvents_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordAccessEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordAccessEvents_OfficeDesks_OfficeDeskId",
                        column: x => x.OfficeDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordAccessEvents_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_ActorUserId",
                table: "RecordAccessEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_DeduplicationKey",
                table: "RecordAccessEvents",
                column: "DeduplicationKey",
                unique: true,
                filter: "\"DeduplicationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_DocumentId",
                table: "RecordAccessEvents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_OccurredAt",
                table: "RecordAccessEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_OfficeDeskId",
                table: "RecordAccessEvents",
                column: "OfficeDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessEvents_WorkstreamId",
                table: "RecordAccessEvents",
                column: "WorkstreamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordAccessEvents");

            migrationBuilder.DropColumn(
                name: "IssuingDeskIdSnapshot",
                table: "OutwardEvents");

            migrationBuilder.DropColumn(
                name: "WorkstreamIdSnapshot",
                table: "OutwardEvents");

            migrationBuilder.DropColumn(
                name: "SourceWorkstreamId",
                table: "MatterEvents");

            migrationBuilder.DropColumn(
                name: "WorkstreamIdSnapshot",
                table: "MatterEvents");
        }
    }
}
