using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterDraftOfficeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastOfficeSaveTokenHash",
                table: "MatterDrafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OfficeDocumentId",
                table: "MatterDrafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OfficeKeyGeneration",
                table: "MatterDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MatterDrafts_OfficeDocumentId",
                table: "MatterDrafts",
                column: "OfficeDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatterDrafts_Documents_OfficeDocumentId",
                table: "MatterDrafts",
                column: "OfficeDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatterDrafts_Documents_OfficeDocumentId",
                table: "MatterDrafts");

            migrationBuilder.DropIndex(
                name: "IX_MatterDrafts_OfficeDocumentId",
                table: "MatterDrafts");

            migrationBuilder.DropColumn(
                name: "LastOfficeSaveTokenHash",
                table: "MatterDrafts");

            migrationBuilder.DropColumn(
                name: "OfficeDocumentId",
                table: "MatterDrafts");

            migrationBuilder.DropColumn(
                name: "OfficeKeyGeneration",
                table: "MatterDrafts");
        }
    }
}
