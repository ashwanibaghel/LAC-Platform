using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepairAwardVillageLegacyLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "AwardVillages" ("Id", "AwardId", "VillageId")
                SELECT (
                    substr(md5(ak."AwardId"::text || ':' || k."VillageId"::text), 1, 8) || '-' ||
                    substr(md5(ak."AwardId"::text || ':' || k."VillageId"::text), 9, 4) || '-' ||
                    substr(md5(ak."AwardId"::text || ':' || k."VillageId"::text), 13, 4) || '-' ||
                    substr(md5(ak."AwardId"::text || ':' || k."VillageId"::text), 17, 4) || '-' ||
                    substr(md5(ak."AwardId"::text || ':' || k."VillageId"::text), 21, 12)
                )::uuid, ak."AwardId", k."VillageId"
                FROM "AwardKhasra" ak
                INNER JOIN "Khasras" k ON k."Id" = ak."KhasraId"
                LEFT JOIN "AwardVillages" av ON av."AwardId" = ak."AwardId" AND av."VillageId" = k."VillageId"
                WHERE av."Id" IS NULL
                GROUP BY ak."AwardId", k."VillageId";
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
