using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTrainingExamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldReviewJson",
                table: "AwardIngestionCandidates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentTrainingExamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceRegionJson = table.Column<string>(type: "text", nullable: false),
                    CellRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawOcr = table.Column<string>(type: "text", nullable: true),
                    NormalizedSuggestion = table.Column<string>(type: "text", nullable: true),
                    HumanFinalValue = table.Column<string>(type: "text", nullable: false),
                    WasCorrected = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewDecision = table.Column<string>(type: "text", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTrainingExamples", x => x.Id);
                    table.CheckConstraint("CK_DocumentTrainingExamples_Page", "\"PageNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_DocumentTrainingExamples_AwardIngestionCandidates_SourceCan~",
                        column: x => x.SourceCandidateId,
                        principalTable: "AwardIngestionCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTrainingExamples_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTrainingExamples_DocumentId_PageNumber",
                table: "DocumentTrainingExamples",
                columns: new[] { "DocumentId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTrainingExamples_SourceCandidateId_CellRole_Verific~",
                table: "DocumentTrainingExamples",
                columns: new[] { "SourceCandidateId", "CellRole", "VerificationRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTrainingExamples");

            migrationBuilder.DropColumn(
                name: "FieldReviewJson",
                table: "AwardIngestionCandidates");
        }
    }
}
