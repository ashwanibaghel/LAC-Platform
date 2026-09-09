using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplementaryAwardParentReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentAwardId",
                table: "Awards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentAwardReference",
                table: "Awards",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Awards_ParentAwardId",
                table: "Awards",
                column: "ParentAwardId");

            migrationBuilder.AddForeignKey(
                name: "FK_Awards_Awards_ParentAwardId",
                table: "Awards",
                column: "ParentAwardId",
                principalTable: "Awards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Awards_Awards_ParentAwardId",
                table: "Awards");

            migrationBuilder.DropIndex(
                name: "IX_Awards_ParentAwardId",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "ParentAwardId",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "ParentAwardReference",
                table: "Awards");
        }
    }
}
