using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKhasraStructuredAreaWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AreaBigha",
                table: "Khasras",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AreaBiswa",
                table: "Khasras",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AreaBiswansi",
                table: "Khasras",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaBigha",
                table: "Khasras");

            migrationBuilder.DropColumn(
                name: "AreaBiswa",
                table: "Khasras");

            migrationBuilder.DropColumn(
                name: "AreaBiswansi",
                table: "Khasras");
        }
    }
}
