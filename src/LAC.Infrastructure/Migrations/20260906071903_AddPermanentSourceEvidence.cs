using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermanentSourceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SafeToConfirm",
                table: "AwardIngestionCandidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SourcePage",
                table: "AwardIngestionCandidates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "AwardIngestionCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedBy",
                table: "AwardIngestionCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedPayloadJson",
                table: "AwardIngestionCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwardSupplementaryMatter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SupplementaryAwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardSupplementaryMatter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardSupplementaryMatter_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardSupplementaryMatter_Awards_SupplementaryAwardId",
                        column: x => x.SupplementaryAwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    PageEnd = table.Column<int>(type: "integer", nullable: true),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: true),
                    ExtractedSnippet = table.Column<string>(type: "text", nullable: true),
                    FactName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmedValueJson = table.Column<string>(type: "text", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardKhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PossessionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardAreaIssueId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardValuationRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardCompensationRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardLandClassId = table.Column<Guid>(type: "uuid", nullable: true),
                    AwardSupplementaryMatterId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceEvidence", x => x.Id);
                    table.CheckConstraint("CK_SourceEvidence_Page", "\"PageNumber\" > 0 AND (\"PageEnd\" IS NULL OR \"PageEnd\" >= \"PageNumber\")");
                    table.CheckConstraint("CK_SourceEvidence_TypedTarget", "num_nonnulls(\"AwardId\", \"AwardKhasraId\", \"NotificationId\", \"PossessionEventId\", \"CourtCaseId\", \"ClaimId\", \"AwardAreaIssueId\", \"AwardValuationRuleId\", \"AwardCompensationRuleId\", \"AwardLandClassId\", \"AwardSupplementaryMatterId\") = 1");
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardAreaIssue_AwardAreaIssueId",
                        column: x => x.AwardAreaIssueId,
                        principalTable: "AwardAreaIssue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardCompensationRule_AwardCompensationRuleId",
                        column: x => x.AwardCompensationRuleId,
                        principalTable: "AwardCompensationRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardKhasra_AwardKhasraId",
                        column: x => x.AwardKhasraId,
                        principalTable: "AwardKhasra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardLandClass_AwardLandClassId",
                        column: x => x.AwardLandClassId,
                        principalTable: "AwardLandClass",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardSupplementaryMatter_AwardSupplementaryM~",
                        column: x => x.AwardSupplementaryMatterId,
                        principalTable: "AwardSupplementaryMatter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_AwardValuationRule_AwardValuationRuleId",
                        column: x => x.AwardValuationRuleId,
                        principalTable: "AwardValuationRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidence_PossessionEvents_PossessionEventId",
                        column: x => x.PossessionEventId,
                        principalTable: "PossessionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionCandidates_SessionId_SafeToConfirm_SourcePage",
                table: "AwardIngestionCandidates",
                columns: new[] { "SessionId", "SafeToConfirm", "SourcePage" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardSupplementaryMatter_AwardId",
                table: "AwardSupplementaryMatter",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardSupplementaryMatter_SupplementaryAwardId",
                table: "AwardSupplementaryMatter",
                column: "SupplementaryAwardId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardAreaIssueId",
                table: "SourceEvidence",
                column: "AwardAreaIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardCompensationRuleId",
                table: "SourceEvidence",
                column: "AwardCompensationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardId",
                table: "SourceEvidence",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardKhasraId",
                table: "SourceEvidence",
                column: "AwardKhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardLandClassId",
                table: "SourceEvidence",
                column: "AwardLandClassId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardSupplementaryMatterId",
                table: "SourceEvidence",
                column: "AwardSupplementaryMatterId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_AwardValuationRuleId",
                table: "SourceEvidence",
                column: "AwardValuationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_ClaimId",
                table: "SourceEvidence",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_CourtCaseId",
                table: "SourceEvidence",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_DocumentId_PageNumber",
                table: "SourceEvidence",
                columns: new[] { "DocumentId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_NotificationId",
                table: "SourceEvidence",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_PossessionEventId",
                table: "SourceEvidence",
                column: "PossessionEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceEvidence");

            migrationBuilder.DropTable(
                name: "AwardSupplementaryMatter");

            migrationBuilder.DropIndex(
                name: "IX_AwardIngestionCandidates_SessionId_SafeToConfirm_SourcePage",
                table: "AwardIngestionCandidates");

            migrationBuilder.DropColumn(
                name: "SafeToConfirm",
                table: "AwardIngestionCandidates");

            migrationBuilder.DropColumn(
                name: "SourcePage",
                table: "AwardIngestionCandidates");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "AwardIngestionCandidates");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                table: "AwardIngestionCandidates");

            migrationBuilder.DropColumn(
                name: "VerifiedPayloadJson",
                table: "AwardIngestionCandidates");
        }
    }
}
