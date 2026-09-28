using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDelhiHighCourtCauseListSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourtExternalSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceTitle = table.Column<string>(type: "text", nullable: false),
                    ListingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalSourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalSourceDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentsDiscovered = table.Column<int>(type: "integer", nullable: false),
                    SourceDocumentsProcessed = table.Column<int>(type: "integer", nullable: false),
                    ObservationsCreated = table.Column<int>(type: "integer", nullable: false),
                    ObservationsAccepted = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    FailureMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalListingObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ListingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourcePageNumber = table.Column<int>(type: "integer", nullable: false),
                    RawMatchedText = table.Column<string>(type: "text", nullable: false),
                    NormalizedCaseIdentity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ConflictReason = table.Column<string>(type: "text", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalListingObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalListingObservations_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtExternalListingObservations_CourtExternalSourceDocumen~",
                        column: x => x.SourceDocumentId,
                        principalTable: "CourtExternalSourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourtExternalListingDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "text", nullable: false),
                    ToStatus = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtExternalListingDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtExternalListingDecisions_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourtExternalListingDecisions_CourtExternalListingObservati~",
                        column: x => x.ObservationId,
                        principalTable: "CourtExternalListingObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingDecisions_ActorUserId",
                table: "CourtExternalListingDecisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingDecisions_ObservationId_DecidedAt",
                table: "CourtExternalListingDecisions",
                columns: new[] { "ObservationId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingObservations_CourtCaseId_Status_Observe~",
                table: "CourtExternalListingObservations",
                columns: new[] { "CourtCaseId", "Status", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalListingObservations_SourceDocumentId_Normalize~",
                table: "CourtExternalListingObservations",
                columns: new[] { "SourceDocumentId", "NormalizedCaseIdentity", "ListingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalSourceDocuments_DocumentId",
                table: "CourtExternalSourceDocuments",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalSourceDocuments_ProviderCode_SourceUrl",
                table: "CourtExternalSourceDocuments",
                columns: new[] { "ProviderCode", "SourceUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourtExternalSyncRuns_ProviderCode_StartedAt",
                table: "CourtExternalSyncRuns",
                columns: new[] { "ProviderCode", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtExternalListingDecisions");

            migrationBuilder.DropTable(
                name: "CourtExternalSyncRuns");

            migrationBuilder.DropTable(
                name: "CourtExternalListingObservations");

            migrationBuilder.DropTable(
                name: "CourtExternalSourceDocuments");
        }
    }
}
