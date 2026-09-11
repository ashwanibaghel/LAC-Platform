using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmSemanticStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NmEntitlementComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmEntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentType = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RateOrBasisRaw = table.Column<string>(type: "text", nullable: true),
                    SourceSequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmEntitlementComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmEntitlementComponents_NmEntitlements_NmEntitlementId",
                        column: x => x.NmEntitlementId,
                        principalTable: "NmEntitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticAnalysisSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParserVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SourcePagesJson = table.Column<string>(type: "text", nullable: false),
                    AutoStructuredCount = table.Column<int>(type: "integer", nullable: false),
                    ExceptionCount = table.Column<int>(type: "integer", nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticAnalysisSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticAnalysisSessions_NmDocuments_NmDocumentId",
                        column: x => x.NmDocumentId,
                        principalTable: "NmDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticOwnerBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalysisSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSequence = table.Column<int>(type: "integer", nullable: false),
                    PageStart = table.Column<int>(type: "integer", nullable: false),
                    PageEnd = table.Column<int>(type: "integer", nullable: false),
                    RecordedNameRaw = table.Column<string>(type: "text", nullable: true),
                    FatherOrSpouseRaw = table.Column<string>(type: "text", nullable: true),
                    ResidenceRaw = table.Column<string>(type: "text", nullable: true),
                    ShareRaw = table.Column<string>(type: "text", nullable: true),
                    ShareNumerator = table.Column<int>(type: "integer", nullable: true),
                    ShareDenominator = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    FieldSourcesJson = table.Column<string>(type: "text", nullable: false),
                    ValidationSummaryJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticOwnerBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticOwnerBlocks_NmSemanticAnalysisSessions_AnalysisSe~",
                        column: x => x.AnalysisSessionId,
                        principalTable: "NmSemanticAnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticCompensationComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentType = table.Column<string>(type: "text", nullable: false),
                    RawAmountText = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RateOrBasisRaw = table.Column<string>(type: "text", nullable: true),
                    SourceSequence = table.Column<int>(type: "integer", nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    SemanticState = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticCompensationComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticCompensationComponents_NmSemanticOwnerBlocks_Owne~",
                        column: x => x.OwnerBlockId,
                        principalTable: "NmSemanticOwnerBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticExceptions_NmSemanticOwnerBlocks_OwnerBlockId",
                        column: x => x.OwnerBlockId,
                        principalTable: "NmSemanticOwnerBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticParcelGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSequence = table.Column<int>(type: "integer", nullable: false),
                    ParcelCountAsRecorded = table.Column<string>(type: "text", nullable: true),
                    TotalAreaAsRecorded = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticParcelGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticParcelGroups_NmSemanticOwnerBlocks_OwnerBlockId",
                        column: x => x.OwnerBlockId,
                        principalTable: "NmSemanticOwnerBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticParcelEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParcelGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSequence = table.Column<int>(type: "integer", nullable: false),
                    RawKhasraText = table.Column<string>(type: "text", nullable: true),
                    NormalizedKhasraText = table.Column<string>(type: "text", nullable: true),
                    Qualifier = table.Column<string>(type: "text", nullable: true),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    NormalizedAreaText = table.Column<string>(type: "text", nullable: true),
                    LandClassRaw = table.Column<string>(type: "text", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    KhasraSourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    AreaSourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    LandClassSourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    IsInherited = table.Column<bool>(type: "boolean", nullable: false),
                    ExactKhasraCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidationState = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticParcelEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticParcelEntries_Khasras_ExactKhasraCandidateId",
                        column: x => x.ExactKhasraCandidateId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmSemanticParcelEntries_NmSemanticParcelGroups_ParcelGroupId",
                        column: x => x.ParcelGroupId,
                        principalTable: "NmSemanticParcelGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmSemanticSourceRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "text", nullable: false),
                    RelatedParcelGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    RawSourceText = table.Column<string>(type: "text", nullable: false),
                    OriginalSourcePage = table.Column<int>(type: "integer", nullable: true),
                    OriginalSourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    OriginalSourceSequence = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmSemanticSourceRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmSemanticSourceRelations_NmSemanticOwnerBlocks_OwnerBlockId",
                        column: x => x.OwnerBlockId,
                        principalTable: "NmSemanticOwnerBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmSemanticSourceRelations_NmSemanticParcelGroups_RelatedPar~",
                        column: x => x.RelatedParcelGroupId,
                        principalTable: "NmSemanticParcelGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlementComponents_NmEntitlementId_SourceSequence",
                table: "NmEntitlementComponents",
                columns: new[] { "NmEntitlementId", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticAnalysisSessions_NmDocumentId_StartedAt",
                table: "NmSemanticAnalysisSessions",
                columns: new[] { "NmDocumentId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticCompensationComponents_OwnerBlockId_ComponentType~",
                table: "NmSemanticCompensationComponents",
                columns: new[] { "OwnerBlockId", "ComponentType", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticExceptions_OwnerBlockId_Reason",
                table: "NmSemanticExceptions",
                columns: new[] { "OwnerBlockId", "Reason" });

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticOwnerBlocks_AnalysisSessionId_SourceSequence",
                table: "NmSemanticOwnerBlocks",
                columns: new[] { "AnalysisSessionId", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticParcelEntries_ExactKhasraCandidateId",
                table: "NmSemanticParcelEntries",
                column: "ExactKhasraCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticParcelEntries_ParcelGroupId_SourceSequence",
                table: "NmSemanticParcelEntries",
                columns: new[] { "ParcelGroupId", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticParcelGroups_OwnerBlockId_SourceSequence",
                table: "NmSemanticParcelGroups",
                columns: new[] { "OwnerBlockId", "SourceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticSourceRelations_OwnerBlockId_RelationType_SourceP~",
                table: "NmSemanticSourceRelations",
                columns: new[] { "OwnerBlockId", "RelationType", "SourcePage" });

            migrationBuilder.CreateIndex(
                name: "IX_NmSemanticSourceRelations_RelatedParcelGroupId",
                table: "NmSemanticSourceRelations",
                column: "RelatedParcelGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NmEntitlementComponents");

            migrationBuilder.DropTable(
                name: "NmSemanticCompensationComponents");

            migrationBuilder.DropTable(
                name: "NmSemanticExceptions");

            migrationBuilder.DropTable(
                name: "NmSemanticParcelEntries");

            migrationBuilder.DropTable(
                name: "NmSemanticSourceRelations");

            migrationBuilder.DropTable(
                name: "NmSemanticParcelGroups");

            migrationBuilder.DropTable(
                name: "NmSemanticOwnerBlocks");

            migrationBuilder.DropTable(
                name: "NmSemanticAnalysisSessions");
        }
    }
}
