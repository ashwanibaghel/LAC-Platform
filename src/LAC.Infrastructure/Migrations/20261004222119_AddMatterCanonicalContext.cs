using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterCanonicalContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContextEntityId",
                table: "MatterEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextEntityType",
                table: "MatterEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatterKhasras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterKhasras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterKhasras_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterKhasras_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatterAwards_MatterId",
                table: "MatterAwards",
                column: "MatterId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_MatterKhasras_KhasraId",
                table: "MatterKhasras",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterKhasras_MatterId_KhasraId",
                table: "MatterKhasras",
                columns: new[] { "MatterId", "KhasraId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatterKhasras");

            migrationBuilder.DropIndex(
                name: "IX_MatterAwards_MatterId",
                table: "MatterAwards");

            migrationBuilder.DropColumn(
                name: "ContextEntityId",
                table: "MatterEvents");

            migrationBuilder.DropColumn(
                name: "ContextEntityType",
                table: "MatterEvents");
        }
    }
}
