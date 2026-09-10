using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNmEntitlementWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceEvidence_TypedTarget",
                table: "SourceEvidence");

            migrationBuilder.AddColumn<Guid>(
                name: "NmEntitlementId",
                table: "SourceEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NmEntitlementKhasraId",
                table: "SourceEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NmRecordedPersonId",
                table: "SourceEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NmDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    RecordDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmDocuments_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmDocuments_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmRecordedPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayNameAsRecorded = table.Column<string>(type: "text", nullable: false),
                    FatherOrSpouseAsRecorded = table.Column<string>(type: "text", nullable: true),
                    AddressAsRecorded = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRow = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmRecordedPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmRecordedPeople_NmDocuments_NmDocumentId",
                        column: x => x.NmDocumentId,
                        principalTable: "NmDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmRecordedPeople_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmReviewRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePage = table.Column<int>(type: "integer", nullable: false),
                    SourceRow = table.Column<string>(type: "text", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    RecordedPersonText = table.Column<string>(type: "text", nullable: true),
                    FatherOrSpouseText = table.Column<string>(type: "text", nullable: true),
                    RawKhasrasText = table.Column<string>(type: "text", nullable: true),
                    RawShareText = table.Column<string>(type: "text", nullable: true),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    EntitlementAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    EntitlementBasisText = table.Column<string>(type: "text", nullable: true),
                    RawSuggestionText = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    VerifiedBy = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmReviewRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmReviewRows_NmDocuments_NmDocumentId",
                        column: x => x.NmDocumentId,
                        principalTable: "NmDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NmRecordedPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRow = table.Column<string>(type: "text", nullable: false),
                    RawShareText = table.Column<string>(type: "text", nullable: true),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    EntitlementAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    EntitlementBasisText = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmEntitlements_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmEntitlements_NmDocuments_NmDocumentId",
                        column: x => x.NmDocumentId,
                        principalTable: "NmDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmEntitlements_NmRecordedPeople_NmRecordedPersonId",
                        column: x => x.NmRecordedPersonId,
                        principalTable: "NmRecordedPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmReviewKhasras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmReviewRowId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawKhasraText = table.Column<string>(type: "text", nullable: false),
                    NormalizedNumber = table.Column<string>(type: "text", nullable: false),
                    Qualifier = table.Column<string>(type: "text", nullable: true),
                    SuggestedKhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    RawShareText = table.Column<string>(type: "text", nullable: true),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmReviewKhasras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmReviewKhasras_Khasras_SuggestedKhasraId",
                        column: x => x.SuggestedKhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmReviewKhasras_NmReviewRows_NmReviewRowId",
                        column: x => x.NmReviewRowId,
                        principalTable: "NmReviewRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NmEntitlementKhasras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NmEntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawKhasraText = table.Column<string>(type: "text", nullable: false),
                    RawQualifier = table.Column<string>(type: "text", nullable: true),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    RawShareText = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NmEntitlementKhasras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NmEntitlementKhasras_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NmEntitlementKhasras_NmEntitlements_NmEntitlementId",
                        column: x => x.NmEntitlementId,
                        principalTable: "NmEntitlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_NmEntitlementId",
                table: "SourceEvidence",
                column: "NmEntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_NmEntitlementKhasraId",
                table: "SourceEvidence",
                column: "NmEntitlementKhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidence_NmRecordedPersonId",
                table: "SourceEvidence",
                column: "NmRecordedPersonId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceEvidence_TypedTarget",
                table: "SourceEvidence",
                sql: "num_nonnulls(\"AwardId\", \"AwardKhasraId\", \"NotificationId\", \"PossessionEventId\", \"CourtCaseId\", \"ClaimId\", \"AwardAreaIssueId\", \"AwardValuationRuleId\", \"AwardCompensationRuleId\", \"AwardLandClassId\", \"AwardSupplementaryMatterId\", \"NmRecordedPersonId\", \"NmEntitlementId\", \"NmEntitlementKhasraId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_NmDocuments_AwardId",
                table: "NmDocuments",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_NmDocuments_DocumentId",
                table: "NmDocuments",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmDocuments_VillageId",
                table: "NmDocuments",
                column: "VillageId");

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlementKhasras_KhasraId",
                table: "NmEntitlementKhasras",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlementKhasras_NmEntitlementId_KhasraId",
                table: "NmEntitlementKhasras",
                columns: new[] { "NmEntitlementId", "KhasraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlements_AwardId",
                table: "NmEntitlements",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlements_NmDocumentId",
                table: "NmEntitlements",
                column: "NmDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_NmEntitlements_NmRecordedPersonId",
                table: "NmEntitlements",
                column: "NmRecordedPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_NmRecordedPeople_NmDocumentId",
                table: "NmRecordedPeople",
                column: "NmDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_NmRecordedPeople_PartyId",
                table: "NmRecordedPeople",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_NmReviewKhasras_NmReviewRowId_NormalizedNumber_Qualifier",
                table: "NmReviewKhasras",
                columns: new[] { "NmReviewRowId", "NormalizedNumber", "Qualifier" });

            migrationBuilder.CreateIndex(
                name: "IX_NmReviewKhasras_SuggestedKhasraId",
                table: "NmReviewKhasras",
                column: "SuggestedKhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_NmReviewRows_NmDocumentId_SourcePage_SourceRow",
                table: "NmReviewRows",
                columns: new[] { "NmDocumentId", "SourcePage", "SourceRow" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceEvidence_NmEntitlementKhasras_NmEntitlementKhasraId",
                table: "SourceEvidence",
                column: "NmEntitlementKhasraId",
                principalTable: "NmEntitlementKhasras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceEvidence_NmEntitlements_NmEntitlementId",
                table: "SourceEvidence",
                column: "NmEntitlementId",
                principalTable: "NmEntitlements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceEvidence_NmRecordedPeople_NmRecordedPersonId",
                table: "SourceEvidence",
                column: "NmRecordedPersonId",
                principalTable: "NmRecordedPeople",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceEvidence_NmEntitlementKhasras_NmEntitlementKhasraId",
                table: "SourceEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceEvidence_NmEntitlements_NmEntitlementId",
                table: "SourceEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceEvidence_NmRecordedPeople_NmRecordedPersonId",
                table: "SourceEvidence");

            migrationBuilder.DropTable(
                name: "NmEntitlementKhasras");

            migrationBuilder.DropTable(
                name: "NmReviewKhasras");

            migrationBuilder.DropTable(
                name: "NmEntitlements");

            migrationBuilder.DropTable(
                name: "NmReviewRows");

            migrationBuilder.DropTable(
                name: "NmRecordedPeople");

            migrationBuilder.DropTable(
                name: "NmDocuments");

            migrationBuilder.DropIndex(
                name: "IX_SourceEvidence_NmEntitlementId",
                table: "SourceEvidence");

            migrationBuilder.DropIndex(
                name: "IX_SourceEvidence_NmEntitlementKhasraId",
                table: "SourceEvidence");

            migrationBuilder.DropIndex(
                name: "IX_SourceEvidence_NmRecordedPersonId",
                table: "SourceEvidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceEvidence_TypedTarget",
                table: "SourceEvidence");

            migrationBuilder.DropColumn(
                name: "NmEntitlementId",
                table: "SourceEvidence");

            migrationBuilder.DropColumn(
                name: "NmEntitlementKhasraId",
                table: "SourceEvidence");

            migrationBuilder.DropColumn(
                name: "NmRecordedPersonId",
                table: "SourceEvidence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceEvidence_TypedTarget",
                table: "SourceEvidence",
                sql: "num_nonnulls(\"AwardId\", \"AwardKhasraId\", \"NotificationId\", \"PossessionEventId\", \"CourtCaseId\", \"ClaimId\", \"AwardAreaIssueId\", \"AwardValuationRuleId\", \"AwardCompensationRuleId\", \"AwardLandClassId\", \"AwardSupplementaryMatterId\") = 1");
        }
    }
}
