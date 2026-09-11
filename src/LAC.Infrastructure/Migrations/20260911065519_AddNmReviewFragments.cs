using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmReviewFragments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NmReviewFragments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    RawOcrText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmReviewFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmReviewFragments_NmDocuments_NmDocumentId",
                        column: x => x.NmDocumentId,
                        principalTable: "NmDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NmReviewFragments_NmDocumentId_SourcePage",
                table: "NmReviewFragments",
                columns: new[] { "NmDocumentId", "SourcePage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NmReviewFragments");
        }
    }
}
