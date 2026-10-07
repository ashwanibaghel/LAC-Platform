using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtStructuredLacIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourtOrderIntelligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OfficialUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrectsOrderId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtOrderIntelligence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtOrderIntelligence_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderIntelligence_CourtOrderIntelligence_CorrectsOrder~",
                        column: x => x.CorrectsOrderId,
                        principalTable: "CourtOrderIntelligence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtOrderIntelligenceRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtOrderIntelligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Contract = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StructuredFactsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExtractionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LacRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    LacRelevanceState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LacAuthorityScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LacActionable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtOrderIntelligenceRevisions", x => x.Id);
                    table.CheckConstraint("CK_CourtScope_Actionable", "NOT \"LacActionable\" OR (\"LacRelevant\" AND \"LacAuthorityScope\" = 'ThisOffice' AND \"LacRelevanceState\" = 'Relevant')");
                    table.ForeignKey(
                        name: "FK_CourtOrderIntelligenceRevisions_CourtExternalOrderObservati~",
                        column: x => x.SourceObservationId,
                        principalTable: "CourtExternalOrderObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderIntelligenceRevisions_CourtOrderIntelligence_Cour~",
                        column: x => x.CourtOrderIntelligenceId,
                        principalTable: "CourtOrderIntelligence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtOrderRecordLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractedEntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchReason = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtOrderRecordLinks", x => x.Id);
                    table.CheckConstraint("CK_CourtLink_Review", "\"MatchState\" <> 'Confirmed' OR (\"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAt\" IS NOT NULL AND \"ReviewReason\" IS NOT NULL AND length(trim(\"ReviewReason\")) > 0)");
                    table.CheckConstraint("CK_CourtLink_Target", "(\"EntityType\" IN ('Village','Award','Khasra') AND \"MatchState\" IN ('NotMatched','NeedsReview') AND \"VillageId\" IS NULL AND \"AwardId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Village' AND \"VillageId\" IS NOT NULL AND \"AwardId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Award' AND \"AwardId\" IS NOT NULL AND \"VillageId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Khasra' AND \"KhasraId\" IS NOT NULL AND \"VillageId\" IS NULL AND \"AwardId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CourtOrderRecordLinks_AppUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderRecordLinks_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderRecordLinks_CourtOrderIntelligenceRevisions_Revis~",
                        column: x => x.RevisionId,
                        principalTable: "CourtOrderIntelligenceRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderRecordLinks_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtOrderRecordLinks_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderIntelligence_CorrectsOrderId",
                table: "CourtOrderIntelligence",
                column: "CorrectsOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderIntelligence_CourtCaseId_OrderDate_OfficialUrl",
                table: "CourtOrderIntelligence",
                columns: new[] { "CourtCaseId", "OrderDate", "OfficialUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderIntelligenceRevisions_CourtOrderIntelligenceId_Cr~",
                table: "CourtOrderIntelligenceRevisions",
                columns: new[] { "CourtOrderIntelligenceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderIntelligenceRevisions_CourtOrderIntelligenceId_So~",
                table: "CourtOrderIntelligenceRevisions",
                columns: new[] { "CourtOrderIntelligenceId", "SourceObservationId", "PayloadSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderIntelligenceRevisions_SourceObservationId",
                table: "CourtOrderIntelligenceRevisions",
                column: "SourceObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderRecordLinks_AwardId_MatchState",
                table: "CourtOrderRecordLinks",
                columns: new[] { "AwardId", "MatchState" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderRecordLinks_KhasraId_MatchState",
                table: "CourtOrderRecordLinks",
                columns: new[] { "KhasraId", "MatchState" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderRecordLinks_ReviewedByUserId",
                table: "CourtOrderRecordLinks",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderRecordLinks_RevisionId_ExtractedEntityId",
                table: "CourtOrderRecordLinks",
                columns: new[] { "RevisionId", "ExtractedEntityId" },
                unique: true,
                filter: "\"MatchState\" = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "IX_CourtOrderRecordLinks_VillageId_MatchState",
                table: "CourtOrderRecordLinks",
                columns: new[] { "VillageId", "MatchState" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtOrderRecordLinks");

            migrationBuilder.DropTable(
                name: "CourtOrderIntelligenceRevisions");

            migrationBuilder.DropTable(
                name: "CourtOrderIntelligence");
        }
    }
}
