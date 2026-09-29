using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDhcAssistedSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DhcAssistedSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalCases = table.Column<int>(type: "integer", nullable: false),
                    CompletedCases = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCases = table.Column<int>(type: "integer", nullable: false),
                    NoChangeCases = table.Column<int>(type: "integer", nullable: false),
                    NeedsReviewCases = table.Column<int>(type: "integer", nullable: false),
                    FailedCases = table.Column<int>(type: "integer", nullable: false),
                    CaptchaChallenges = table.Column<int>(type: "integer", nullable: false),
                    FailureMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DhcAssistedSyncRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DhcAssistedSyncRuns_AppUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DhcAssistedSyncItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueOrder = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DhcAssistedSyncItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DhcAssistedSyncItems_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DhcAssistedSyncItems_DhcAssistedSyncRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "DhcAssistedSyncRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalCaseStatusObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NormalizedCaseIdentity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RawCaseNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawDiaryNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RawStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RawParties = table.Column<string>(type: "text", nullable: true),
                    RawListingDate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ListingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RawCourtNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RawEvidenceText = table.Column<string>(type: "text", nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParserVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalCaseStatusObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalCaseStatusObservations_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtExternalCaseStatusObservations_DhcAssistedSyncItems_Ru~",
                        column: x => x.RunItemId,
                        principalTable: "DhcAssistedSyncItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalOrderObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NormalizedCaseIdentity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RawCaseNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RawOrderDate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OfficialUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CorrigendumUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UploadDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RawUploadDate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RawRemark = table.Column<string>(type: "text", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawEvidenceText = table.Column<string>(type: "text", nullable: false),
                    ParserVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalOrderObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalOrderObservations_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtExternalOrderObservations_DhcAssistedSyncItems_RunItem~",
                        column: x => x.RunItemId,
                        principalTable: "DhcAssistedSyncItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalAssistedDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalAssistedDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalAssistedDecisions_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtExternalAssistedDecisions_CourtExternalCaseStatusObser~",
                        column: x => x.ObservationId,
                        principalTable: "CourtExternalCaseStatusObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalAssistedDecisions_ActorUserId",
                table: "CourtExternalAssistedDecisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalAssistedDecisions_ObservationId_DecidedAt",
                table: "CourtExternalAssistedDecisions",
                columns: new[] { "ObservationId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalCaseStatusObservations_CourtCaseId_ObservedAt",
                table: "CourtExternalCaseStatusObservations",
                columns: new[] { "CourtCaseId", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalCaseStatusObservations_RunItemId_EvidenceSha256",
                table: "CourtExternalCaseStatusObservations",
                columns: new[] { "RunItemId", "EvidenceSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalOrderObservations_CourtCaseId_ObservedAt",
                table: "CourtExternalOrderObservations",
                columns: new[] { "CourtCaseId", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalOrderObservations_RunItemId_EvidenceSha256",
                table: "CourtExternalOrderObservations",
                columns: new[] { "RunItemId", "EvidenceSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DhcAssistedSyncItems_CourtCaseId",
                table: "DhcAssistedSyncItems",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DhcAssistedSyncItems_RunId_QueueOrder",
                table: "DhcAssistedSyncItems",
                columns: new[] { "RunId", "QueueOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DhcAssistedSyncRuns_StartedByUserId",
                table: "DhcAssistedSyncRuns",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DhcAssistedSyncRuns_Status_StartedAt",
                table: "DhcAssistedSyncRuns",
                columns: new[] { "Status", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtExternalAssistedDecisions");

            migrationBuilder.DropTable(
                name: "CourtExternalOrderObservations");

            migrationBuilder.DropTable(
                name: "CourtExternalCaseStatusObservations");

            migrationBuilder.DropTable(
                name: "DhcAssistedSyncItems");

            migrationBuilder.DropTable(
                name: "DhcAssistedSyncRuns");
        }
    }
}
