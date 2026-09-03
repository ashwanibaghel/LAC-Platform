using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceKhasraQualifierIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber_Qualifier",
                table: "Khasras");

            migrationBuilder.Sql("CREATE UNIQUE INDEX \"UX_Khasras_VillageId_NormalizedNumber_QualifierIdentity\" ON \"Khasras\" (\"VillageId\", \"NormalizedNumber\", COALESCE(\"Qualifier\", ''));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"UX_Khasras_VillageId_NormalizedNumber_QualifierIdentity\";");

            migrationBuilder.CreateIndex(
                name: "IX_Khasras_VillageId_NormalizedNumber_Qualifier",
                table: "Khasras",
                columns: new[] { "VillageId", "NormalizedNumber", "Qualifier" },
                unique: true);
        }
    }
}
