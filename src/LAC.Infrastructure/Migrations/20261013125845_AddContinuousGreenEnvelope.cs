using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuousGreenEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompositionVersion",
                table: "MatterOfficeSessions",
                type: "text",
                nullable: false,
                defaultValue: "OfficeNoteV1");

            migrationBuilder.AddColumn<string>(
                name: "HistoryManifestHash",
                table: "MatterOfficeSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryManifestJson",
                table: "MatterOfficeSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoryManifestSeal",
                table: "MatterOfficeSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Office_Composition",
                table: "MatterOfficeSessions",
                sql: "(\"CompositionVersion\" = 'OfficeNoteV1' AND \"HistoryManifestJson\" IS NULL AND \"HistoryManifestHash\" IS NULL AND \"HistoryManifestSeal\" IS NULL) OR (\"CompositionVersion\" = 'ContinuousGreenEnvelopeV2' AND \"HistoryManifestJson\" IS NOT NULL AND \"HistoryManifestHash\" IS NOT NULL AND \"HistoryManifestHash\" ~ '^[0-9A-F]{64}$' AND \"HistoryManifestSeal\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS (SELECT 1 FROM "MatterOfficeSessions" WHERE "CompositionVersion" = 'ContinuousGreenEnvelopeV2') THEN RAISE EXCEPTION 'Preserve continuous noting lease evidence: downgrade blocked'; END IF; END $$;""");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Office_Composition",
                table: "MatterOfficeSessions");

            migrationBuilder.DropColumn(
                name: "CompositionVersion",
                table: "MatterOfficeSessions");

            migrationBuilder.DropColumn(
                name: "HistoryManifestHash",
                table: "MatterOfficeSessions");

            migrationBuilder.DropColumn(
                name: "HistoryManifestJson",
                table: "MatterOfficeSessions");

            migrationBuilder.DropColumn(
                name: "HistoryManifestSeal",
                table: "MatterOfficeSessions");
        }
    }
}
