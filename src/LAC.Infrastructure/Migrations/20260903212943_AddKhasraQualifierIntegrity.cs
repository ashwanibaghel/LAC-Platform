using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKhasraQualifierIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber",
                table: "Khasras");

            migrationBuilder.AddColumn<string>(
                name: "Qualifier",
                table: "Khasras",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber_Qualifier",
                table: "Khasras",
                columns: new[] { "VillageId", "NormalizedNumber", "Qualifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber_Qualifier",
                table: "Khasras");

            migrationBuilder.DropColumn(
                name: "Qualifier",
                table: "Khasras");

            migrationBuilder.CreateIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber",
                table: "Khasras",
                columns: new[] { "VillageId", "NormalizedNumber" },
                unique: true);
        }
    }
}
