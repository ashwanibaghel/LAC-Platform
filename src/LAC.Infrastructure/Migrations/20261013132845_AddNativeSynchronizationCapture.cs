using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeSynchronizationCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NativeCanonicalHash",
                table: "MatterOfficeSaveRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeCaptureSha256",
                table: "MatterOfficeSaveRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NativeCaptureSize",
                table: "MatterOfficeSaveRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeCaptureStoragePath",
                table: "MatterOfficeSaveRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NativeContentHash",
                table: "MatterOfficeSaveRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Office_NativeCapture",
                table: "MatterOfficeSaveRequests",
                sql: "(\"NativeCaptureStoragePath\" IS NULL AND \"NativeCaptureSha256\" IS NULL AND \"NativeCaptureSize\" IS NULL AND \"NativeCanonicalHash\" IS NULL AND \"NativeContentHash\" IS NULL) OR (\"NativeCaptureStoragePath\" IS NOT NULL AND \"NativeCaptureStoragePath\" <> '' AND \"NativeCaptureSha256\" IS NOT NULL AND \"NativeCaptureSha256\" ~ '^[0-9a-f]{64}$' AND \"NativeCaptureSize\" IS NOT NULL AND \"NativeCaptureSize\" BETWEEN 1 AND 5242880 AND \"NativeCanonicalHash\" IS NOT NULL AND \"NativeCanonicalHash\" ~ '^[0-9A-F]{64}$' AND \"NativeContentHash\" IS NOT NULL AND \"NativeContentHash\" ~ '^[0-9A-F]{64}$')");
            migrationBuilder.Sql("""
                CREATE FUNCTION lac_office_capture_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE composition text; BEGIN
                  SELECT "CompositionVersion" INTO composition FROM "MatterOfficeSessions" WHERE "Id"=NEW."SessionId";
                  IF (composition='ContinuousGreenEnvelopeV2') IS DISTINCT FROM (NEW."NativeCanonicalHash" IS NOT NULL)
                    THEN RAISE EXCEPTION 'Native capture must match the authorized lease composition'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER office_capture_guard BEFORE INSERT OR UPDATE ON "MatterOfficeSaveRequests" FOR EACH ROW EXECUTE FUNCTION lac_office_capture_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "MatterOfficeSaveRequests" WHERE "NativeCaptureStoragePath" IS NOT NULL)
                  THEN RAISE EXCEPTION 'Native synchronization evidence is durable: downgrade blocked'; END IF; END $$;
                DROP TRIGGER office_capture_guard ON "MatterOfficeSaveRequests";
                DROP FUNCTION lac_office_capture_guard();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Office_NativeCapture",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropColumn(
                name: "NativeCanonicalHash",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropColumn(
                name: "NativeCaptureSha256",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropColumn(
                name: "NativeCaptureSize",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropColumn(
                name: "NativeCaptureStoragePath",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropColumn(
                name: "NativeContentHash",
                table: "MatterOfficeSaveRequests");
        }
    }
}
