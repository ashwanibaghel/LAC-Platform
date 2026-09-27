using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwardClaimSourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimantText",
                table: "Claims",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedAreaText",
                table: "Claims",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KhasraReferences",
                table: "Claims",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSerialNumber",
                table: "Claims",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimantText",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ClaimedAreaText",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "KhasraReferences",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "SourceSerialNumber",
                table: "Claims");
        }
    }
}
