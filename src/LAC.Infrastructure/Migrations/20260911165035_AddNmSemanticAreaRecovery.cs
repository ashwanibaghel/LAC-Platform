using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmSemanticAreaRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreaExtractionMethod",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AreaOcrConfidence",
                table: "NmSemanticParcelEntries",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaExtractionMethod",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "AreaOcrConfidence",
                table: "NmSemanticParcelEntries");
        }
    }
}
