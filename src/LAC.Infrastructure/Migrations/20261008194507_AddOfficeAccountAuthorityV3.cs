using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeAccountAuthorityV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanRegisterInwardDak",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomDesignation",
                table: "AppUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LandAccess",
                table: "AppUsers",
                type: "text",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "OfficeAccessManaged",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OfficeRevision",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OfficeModuleMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Module = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeModuleMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficeModuleMemberships_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppUser_DesignationChoice",
                table: "AppUsers",
                sql: "\"CustomDesignation\" IS NULL OR (\"DesignationId\" IS NULL AND length(btrim(\"CustomDesignation\")) BETWEEN 1 AND 200)");

            migrationBuilder.CreateIndex(
                name: "IX_OfficeModuleMemberships_UserId_Module",
                table: "OfficeModuleMemberships",
                columns: new[] { "UserId", "Module" },
                unique: true,
                filter: "\"RecordStatus\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficeModuleMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppUser_DesignationChoice",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CanRegisterInwardDak",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CustomDesignation",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "LandAccess",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "OfficeAccessManaged",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "OfficeRevision",
                table: "AppUsers");
        }
    }
}
