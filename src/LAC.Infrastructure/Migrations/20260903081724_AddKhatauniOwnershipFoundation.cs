using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKhatauniOwnershipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KhatauniRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VillageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    RecordYearText = table.Column<string>(type: "text", nullable: true),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    VerificationStatus = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhatauniRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KhatauniRecords_Documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KhatauniRecords_Villages_VillageId",
                        column: x => x.VillageId,
                        principalTable: "Villages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyType = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    FatherOrSpouseName = table.Column<string>(type: "text", nullable: true),
                    AddressText = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentKhatauniRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhatauniRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentKhatauniRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentKhatauniRecords_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentKhatauniRecords_KhatauniRecords_KhatauniRecordId",
                        column: x => x.KhatauniRecordId,
                        principalTable: "KhatauniRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Khatas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KhatauniRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhataNumber = table.Column<string>(type: "text", nullable: false),
                    RawKhataNumber = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Khatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Khatas_KhatauniRecords_KhatauniRecordId",
                        column: x => x.KhatauniRecordId,
                        principalTable: "KhatauniRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KhataKhasras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KhataId = table.Column<Guid>(type: "uuid", nullable: false),
                    KhasraId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawKhasraText = table.Column<string>(type: "text", nullable: true),
                    RecordedArea = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RawAreaText = table.Column<string>(type: "text", nullable: true),
                    AreaUnit = table.Column<string>(type: "text", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhataKhasras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KhataKhasras_Khasras_KhasraId",
                        column: x => x.KhasraId,
                        principalTable: "Khasras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KhataKhasras_Khatas_KhataId",
                        column: x => x.KhataId,
                        principalTable: "Khatas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KhataPartyShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KhataId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawShareText = table.Column<string>(type: "text", nullable: true),
                    ShareNumerator = table.Column<int>(type: "integer", nullable: true),
                    ShareDenominator = table.Column<int>(type: "integer", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    VerificationStatus = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhataPartyShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KhataPartyShares_Khatas_KhataId",
                        column: x => x.KhataId,
                        principalTable: "Khatas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KhataPartyShares_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentKhatauniRecords_DocumentId_KhatauniRecordId",
                table: "DocumentKhatauniRecords",
                columns: new[] { "DocumentId", "KhatauniRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentKhatauniRecords_KhatauniRecordId",
                table: "DocumentKhatauniRecords",
                column: "KhatauniRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_KhataKhasras_KhasraId",
                table: "KhataKhasras",
                column: "KhasraId");

            migrationBuilder.CreateIndex(
                name: "IX_KhataKhasras_KhataId_KhasraId",
                table: "KhataKhasras",
                columns: new[] { "KhataId", "KhasraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KhataPartyShares_KhataId_PartyId",
                table: "KhataPartyShares",
                columns: new[] { "KhataId", "PartyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KhataPartyShares_PartyId",
                table: "KhataPartyShares",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Khatas_KhatauniRecordId_KhataNumber",
                table: "Khatas",
                columns: new[] { "KhatauniRecordId", "KhataNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KhatauniRecords_SourceDocumentId",
                table: "KhatauniRecords",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KhatauniRecords_VillageId",
                table: "KhatauniRecords",
                column: "VillageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentKhatauniRecords");

            migrationBuilder.DropTable(
                name: "KhataKhasras");

            migrationBuilder.DropTable(
                name: "KhataPartyShares");

            migrationBuilder.DropTable(
                name: "Khatas");

            migrationBuilder.DropTable(
                name: "Parties");

            migrationBuilder.DropTable(
                name: "KhatauniRecords");
        }
    }
}
