using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeNoChangeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NativeCaptureJson",
                table: "MatterOfficePreviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Office_PreviewNativeCapture",
                table: "MatterOfficePreviews",
                sql: "\"NativeCaptureJson\" IS NULL OR (length(\"NativeCaptureJson\") <= 8192 AND jsonb_typeof(\"NativeCaptureJson\"::jsonb)='object' AND \"NativeCaptureJson\"::jsonb ?& ARRAY['Version','StoragePath','Sha256','Size','CanonicalHash','ContentHash'] AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Version')='number' AND (\"NativeCaptureJson\"::jsonb->>'Version')='1' AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'StoragePath')='string' AND (\"NativeCaptureJson\"::jsonb->>'StoragePath')<>'' AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Sha256')='string' AND (\"NativeCaptureJson\"::jsonb->>'Sha256')~'^[0-9a-f]{64}$' AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Size')='number' AND (\"NativeCaptureJson\"::jsonb->>'Size')::bigint BETWEEN 1 AND 5242880 AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'CanonicalHash')='string' AND (\"NativeCaptureJson\"::jsonb->>'CanonicalHash')=\"CanonicalHash\" AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'ContentHash')='string' AND (\"NativeCaptureJson\"::jsonb->>'ContentHash')~'^[0-9A-F]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DO $$ BEGIN IF EXISTS (SELECT 1 FROM "MatterOfficePreviews" WHERE "NativeCaptureJson" IS NOT NULL) THEN RAISE EXCEPTION 'Native review evidence is durable: downgrade blocked'; END IF; END $$;""");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Office_PreviewNativeCapture",
                table: "MatterOfficePreviews");

            migrationBuilder.DropColumn(
                name: "NativeCaptureJson",
                table: "MatterOfficePreviews");
        }
    }
}
