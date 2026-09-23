using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterDocumentExtractProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "MatterDocuments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "MatterDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordStatus",
                table: "MatterDocuments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "MatterDocuments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "MatterDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatterDocumentExtracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSha256Hash = table.Column<string>(type: "text", nullable: true),
                    NormalizedSourcePagesText = table.Column<string>(type: "text", nullable: false),
                    NormalizedPageNumbersJson = table.Column<string>(type: "text", nullable: false),
                    ItemNumber = table.Column<string>(type: "text", nullable: true),
                    KhasraReferenceText = table.Column<string>(type: "text", nullable: true),
                    ContextLabel = table.Column<string>(type: "text", nullable: true),
                    ExtractedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractedByUserNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterDocumentExtracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterDocumentExtracts_AppUsers_ExtractedByUserId",
                        column: x => x.ExtractedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterDocumentExtracts_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterDocumentExtracts_MatterDocuments_MatterDocumentId",
                        column: x => x.MatterDocumentId,
                        principalTable: "MatterDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatterDocumentExtracts_ExtractedByUserId",
                table: "MatterDocumentExtracts",
                column: "ExtractedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterDocumentExtracts_MatterDocumentId",
                table: "MatterDocumentExtracts",
                column: "MatterDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterDocumentExtracts_SourceDocumentId",
                table: "MatterDocumentExtracts",
                column: "SourceDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatterDocumentExtracts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "MatterDocuments");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "MatterDocuments");

            migrationBuilder.DropColumn(
                name: "RecordStatus",
                table: "MatterDocuments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MatterDocuments");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "MatterDocuments");
        }
    }
}
