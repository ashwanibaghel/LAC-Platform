using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmOwnerReviewOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewerKhasra",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerKhasraNormalized",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerLandClass",
                table: "NmSemanticParcelEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "NmSemanticOwnerBlocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerFatherOrSpouse",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerName",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerResidence",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerShare",
                table: "NmSemanticOwnerBlocks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewerKhasra",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "ReviewerKhasraNormalized",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "ReviewerLandClass",
                table: "NmSemanticParcelEntries");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewerFatherOrSpouse",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewerName",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewerResidence",
                table: "NmSemanticOwnerBlocks");

            migrationBuilder.DropColumn(
                name: "ReviewerShare",
                table: "NmSemanticOwnerBlocks");
        }
    }
}
