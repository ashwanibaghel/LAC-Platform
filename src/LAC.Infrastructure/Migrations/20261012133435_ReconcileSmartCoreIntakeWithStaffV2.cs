using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileSmartCoreIntakeWithStaffV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CoreDocumentIntakes_DocumentId",
                table: "CoreDocumentIntakes");

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_DocumentId",
                table: "CoreDocumentIntakes",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "CoreDocumentIntakes" GROUP BY "DocumentId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Cannot restore single-intake document uniqueness while shared source proposals exist. Preserve evidence and review rollback.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_CoreDocumentIntakes_DocumentId",
                table: "CoreDocumentIntakes");

            migrationBuilder.CreateIndex(
                name: "IX_CoreDocumentIntakes_DocumentId",
                table: "CoreDocumentIntakes",
                column: "DocumentId",
                unique: true);
        }
    }
}
