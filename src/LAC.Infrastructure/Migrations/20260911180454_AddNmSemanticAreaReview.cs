using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmSemanticAreaReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreaFieldState",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AreaReviewedAt",
                table: "NmSemanticParcelEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AreaReviewedBy",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AreaReviewerValueNormalized",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AreaReviewerValueRaw",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaFieldState",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "AreaReviewedAt",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "AreaReviewedBy",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "AreaReviewerValueNormalized",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "AreaReviewerValueRaw",
                table: "NmSemanticParcelEntries");
        }
    }
}
