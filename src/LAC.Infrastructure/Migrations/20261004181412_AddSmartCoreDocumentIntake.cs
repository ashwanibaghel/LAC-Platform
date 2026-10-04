using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartCoreDocumentIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoreDocumentIntakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProposalJson = table.Column<string>(type: "text", nullable: false),
                    SourcePagesJson = table.Column<string>(type: "text", nullable: false),
                    ClassifierVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ConfirmedAwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedRole = table.Column<string>(type: "text", nullable: true),
                    ConfirmedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationJson = table.Column<string>(type: "text", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreDocumentIntakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoreDocumentIntakes_Awards_ConfirmedAwardId",
                        column: x => x.ConfirmedAwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoreDocumentIntakes_Documents_ConfirmedDocumentId",
                        column: x => x.ConfirmedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoreDocumentIntakes_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoreDocumentIntakes_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_ConfirmedAwardId",
                table: "CoreDocumentIntakes",
                column: "ConfirmedAwardId");

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_ConfirmedDocumentId",
                table: "CoreDocumentIntakes",
                column: "ConfirmedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_DocumentId",
                table: "CoreDocumentIntakes",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_VillageId_CreatedAt",
                table: "CoreDocumentIntakes",
                columns: new[] { "VillageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_VillageId_Sha256Hash",
                table: "CoreDocumentIntakes",
                columns: new[] { "VillageId", "Sha256Hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoreDocumentIntakes");
        }
    }
}
