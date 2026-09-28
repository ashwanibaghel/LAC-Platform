using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowSharedDhcCauseListDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourtExternalSourceDocuments_DocumentId",
                table: "CourtExternalSourceDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalSourceDocuments_DocumentId",
                table: "CourtExternalSourceDocuments",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourtExternalSourceDocuments_DocumentId",
                table: "CourtExternalSourceDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalSourceDocuments_DocumentId",
                table: "CourtExternalSourceDocuments",
                column: "DocumentId",
                unique: true);
        }
    }
}
