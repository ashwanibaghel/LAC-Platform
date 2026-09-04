using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwardIngestionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AwardIngestionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedVillageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardIngestionSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardIngestionSessions_Awards_TargetAwardId",
                        column: x => x.TargetAwardId,
                        principalTable: "Awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardIngestionSessions_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AwardIngestionSessions_Villages_SelectedVillageId",
                        column: x => x.SelectedVillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AwardIngestionCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateType = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StructuredPayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CanonicalEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalEntityType = table.Column<string>(type: "text", nullable: true),
                    ResolutionAction = table.Column<string>(type: "text", nullable: true),
                    ValidationIssuesJson = table.Column<string>(type: "text", nullable: true),
                    ConflictDetailsJson = table.Column<string>(type: "text", nullable: true),
                    SourceLocatorJson = table.Column<string>(type: "text", nullable: true),
                    RawSourceText = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardIngestionCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardIngestionCandidates_AwardIngestionSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AwardIngestionSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionCandidates_CanonicalEntityId",
                table: "AwardIngestionCandidates",
                column: "CanonicalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionCandidates_SessionId_CandidateType",
                table: "AwardIngestionCandidates",
                columns: new[] { "SessionId", "CandidateType" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionCandidates_SessionId_Status",
                table: "AwardIngestionCandidates",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionSessions_CreatedAt",
                table: "AwardIngestionSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionSessions_SelectedVillageId",
                table: "AwardIngestionSessions",
                column: "SelectedVillageId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionSessions_SourceDocumentId",
                table: "AwardIngestionSessions",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AwardIngestionSessions_TargetAwardId",
                table: "AwardIngestionSessions",
                column: "TargetAwardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwardIngestionCandidates");

            migrationBuilder.DropTable(
                name: "AwardIngestionSessions");
        }
    }
}
