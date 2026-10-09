using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompensationHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InputsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    CalculatorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConversionVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalAreaNotation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MarketRate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinalAmount = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompensationHistory_AppUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationHistory_OwnerUserId_CreatedAt_Id",
                table: "CompensationHistory",
                columns: new[] { "OwnerUserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationHistory_OwnerUserId_IdempotencyKey",
                table: "CompensationHistory",
                columns: new[] { "OwnerUserId", "IdempotencyKey" },
                unique: true);
            // Protect the immutable evidence even from direct SQL or future EF code.
            migrationBuilder.Sql("""
                CREATE FUNCTION lac_compensation_history_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Calculation history cannot be deleted'; END IF;
                    IF (to_jsonb(NEW) - 'Title') IS DISTINCT FROM (to_jsonb(OLD) - 'Title') THEN
                        RAISE EXCEPTION 'Calculation evidence is immutable; create a new entry';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER compensation_history_immutable BEFORE UPDATE OR DELETE ON "CompensationHistory"
                    FOR EACH ROW EXECUTE FUNCTION lac_compensation_history_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompensationHistory");
            migrationBuilder.Sql("DROP FUNCTION lac_compensation_history_immutable();");
        }
    }
}
