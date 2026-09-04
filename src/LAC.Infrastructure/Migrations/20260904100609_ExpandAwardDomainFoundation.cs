using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAwardDomainFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Awards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AwardedAreaBigha",
                table: "AwardKhasra",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwardedAreaBiswa",
                table: "AwardKhasra",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwardedAreaBiswansi",
                table: "AwardKhasra",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecordedTotalAreaBigha",
                table: "AwardKhasra",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecordedTotalAreaBiswa",
                table: "AwardKhasra",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecordedTotalAreaBiswansi",
                table: "AwardKhasra",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelationshipStatus",
                table: "AwardKhasra",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwardApportionmentEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShareNumerator = table.Column<int>(type: "integer", nullable: true),
                    ShareDenominator = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    DisputeStatus = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardApportionmentEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardApportionmentEntry_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardApportionmentEntry_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardApportionmentEntry_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardAreaIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueType = table.Column<string>(type: "text", nullable: false),
                    NotificationAreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    NotificationAreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    NotificationAreaBiswansi = table.Column<int>(type: "integer", nullable: true),
                    FieldBookAreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    FieldBookAreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    FieldBookAreaBiswansi = table.Column<int>(type: "integer", nullable: true),
                    DifferenceBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DifferenceBiswa = table.Column<int>(type: "integer", nullable: true),
                    DifferenceBiswansi = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CorrigendumReference = table.Column<string>(type: "text", nullable: true),
                    CorrigendumDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardAreaIssue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardAreaIssue_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardAreaIssue_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardAreaSummary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaType = table.Column<string>(type: "text", nullable: false),
                    AreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    AreaBiswansi = table.Column<int>(type: "integer", nullable: true),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardAreaSummary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardAreaSummary_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardCompensationRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "text", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    RateAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    LegalSection = table.Column<string>(type: "text", nullable: true),
                    BasisDescription = table.Column<string>(type: "text", nullable: true),
                    StartEvent = table.Column<string>(type: "text", nullable: true),
                    EndEvent = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardCompensationRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardCompensationRule_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardLandClass",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardLandClass", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardLandClass_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardNotifications_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardNotifications_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardVillages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardVillages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardVillages_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardVillages_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimantPartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimReference = table.Column<string>(type: "text", nullable: true),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClaimText = table.Column<string>(type: "text", nullable: true),
                    ClaimedRateAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ClaimedRateUnit = table.Column<string>(type: "text", nullable: true),
                    ClaimedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Claims_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Claims_Parties_ClaimantPartyId",
                        column: x => x.ClaimantPartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<string>(type: "text", nullable: false),
                    CourtName = table.Column<string>(type: "text", nullable: false),
                    CaseType = table.Column<string>(type: "text", nullable: true),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentStatus = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KhasraReviewFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: false),
                    RelatedAwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhasraReviewFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KhasraReviewFlags_Awards_RelatedAwardId",
                        column: x => x.RelatedAwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KhasraReviewFlags_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PossessionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    PossessionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EventType = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PossessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PossessionEvents_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardKhasraClassification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardLandClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    AreaBiswansi = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardKhasraClassification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardKhasraClassification_AwardLandClass_AwardLandClassId",
                        column: x => x.AwardLandClassId,
                        principalTable: "AwardLandClass",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardKhasraClassification_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardValuationRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardLandClassId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleType = table.Column<string>(type: "text", nullable: false),
                    RateAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RateUnit = table.Column<string>(type: "text", nullable: true),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LegalSection = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardValuationRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardValuationRule_AwardLandClass_AwardLandClassId",
                        column: x => x.AwardLandClassId,
                        principalTable: "AwardLandClass",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardValuationRule_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimKhasra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedAreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ClaimedAreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    ClaimedAreaBiswansi = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimKhasra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimKhasra_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimKhasra_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseAward",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseAward", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseAward_Awards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseAward_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtCaseKhasra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtCaseKhasra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtCaseKhasra_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtCaseKhasra_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PossessionKhasra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PossessionEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaBigha = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    AreaBiswa = table.Column<int>(type: "integer", nullable: true),
                    AreaBiswansi = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PossessionKhasra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PossessionKhasra_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PossessionKhasra_PossessionEvents_PossessionEventId",
                        column: x => x.PossessionEventId,
                        principalTable: "PossessionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards",
                column: "AwardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_AwardApportionmentEntry_AwardId",
                table: "AwardApportionmentEntry",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardApportionmentEntry_KhasraId",
                table: "AwardApportionmentEntry",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardApportionmentEntry_PartyId",
                table: "AwardApportionmentEntry",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardAreaIssue_AwardId",
                table: "AwardAreaIssue",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardAreaIssue_KhasraId",
                table: "AwardAreaIssue",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardAreaSummary_AwardId",
                table: "AwardAreaSummary",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardCompensationRule_AwardId",
                table: "AwardCompensationRule",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardKhasraClassification_AwardLandClassId_KhasraId",
                table: "AwardKhasraClassification",
                columns: new[] { "AwardLandClassId", "KhasraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwardKhasraClassification_KhasraId",
                table: "AwardKhasraClassification",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardLandClass_AwardId_Code",
                table: "AwardLandClass",
                columns: new[] { "AwardId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwardNotifications_AwardId_NotificationId",
                table: "AwardNotifications",
                columns: new[] { "AwardId", "NotificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwardNotifications_NotificationId",
                table: "AwardNotifications",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardValuationRule_AwardId",
                table: "AwardValuationRule",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardValuationRule_AwardLandClassId",
                table: "AwardValuationRule",
                column: "AwardLandClassId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardVillages_AwardId_VillageId",
                table: "AwardVillages",
                columns: new[] { "AwardId", "VillageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AwardVillages_VillageId",
                table: "AwardVillages",
                column: "VillageId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimKhasra_ClaimId_KhasraId",
                table: "ClaimKhasra",
                columns: new[] { "ClaimId", "KhasraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimKhasra_KhasraId",
                table: "ClaimKhasra",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_AwardId",
                table: "Claims",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimantPartyId",
                table: "Claims",
                column: "ClaimantPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseAward_AwardId",
                table: "CourtCaseAward",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseAward_CourtCaseId_AwardId",
                table: "CourtCaseAward",
                columns: new[] { "CourtCaseId", "AwardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseKhasra_CourtCaseId_KhasraId",
                table: "CourtCaseKhasra",
                columns: new[] { "CourtCaseId", "KhasraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtCaseKhasra_KhasraId",
                table: "CourtCaseKhasra",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_KhasraReviewFlags_KhasraId",
                table: "KhasraReviewFlags",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_KhasraReviewFlags_RelatedAwardId",
                table: "KhasraReviewFlags",
                column: "RelatedAwardId");

            migrationBuilder.CreateIndex(
                name: "IX_PossessionEvents_AwardId",
                table: "PossessionEvents",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_PossessionKhasra_KhasraId",
                table: "PossessionKhasra",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_PossessionKhasra_PossessionEventId_KhasraId",
                table: "PossessionKhasra",
                columns: new[] { "PossessionEventId", "KhasraId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwardApportionmentEntry");

            migrationBuilder.DropTable(
                name: "AwardAreaIssue");

            migrationBuilder.DropTable(
                name: "AwardAreaSummary");

            migrationBuilder.DropTable(
                name: "AwardCompensationRule");

            migrationBuilder.DropTable(
                name: "AwardKhasraClassification");

            migrationBuilder.DropTable(
                name: "AwardNotifications");

            migrationBuilder.DropTable(
                name: "AwardValuationRule");

            migrationBuilder.DropTable(
                name: "AwardVillages");

            migrationBuilder.DropTable(
                name: "ClaimKhasra");

            migrationBuilder.DropTable(
                name: "CourtCaseAward");

            migrationBuilder.DropTable(
                name: "CourtCaseKhasra");

            migrationBuilder.DropTable(
                name: "KhasraReviewFlags");

            migrationBuilder.DropTable(
                name: "PossessionKhasra");

            migrationBuilder.DropTable(
                name: "AwardLandClass");

            migrationBuilder.DropTable(
                name: "Claims");

            migrationBuilder.DropTable(
                name: "CourtCases");

            migrationBuilder.DropTable(
                name: "PossessionEvents");

            migrationBuilder.DropIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Awards");

            migrationBuilder.DropColumn(
                name: "AwardedAreaBigha",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "AwardedAreaBiswa",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "AwardedAreaBiswansi",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "RecordedTotalAreaBigha",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "RecordedTotalAreaBiswa",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "RecordedTotalAreaBiswansi",
                table: "AwardKhasra");

            migrationBuilder.DropColumn(
                name: "RelationshipStatus",
                table: "AwardKhasra");

            migrationBuilder.CreateIndex(
                name: "IX_Awards_AwardNumber",
                table: "Awards",
                column: "AwardNumber",
                unique: true);
        }
    }
}
