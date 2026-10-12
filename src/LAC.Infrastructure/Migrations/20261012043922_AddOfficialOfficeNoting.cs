using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialOfficeNoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OfficePreviewId",
                table: "MatterWorkingNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OfficeSessionId",
                table: "MatterWorkingNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatterOfficeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDakId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorSessionVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkingRevision = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DocumentKey = table.Column<string>(type: "text", nullable: false),
                    LatestSnapshotId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficeSessions", x => x.Id);
                    table.CheckConstraint("CK_MatterOfficeSession_State", "\"State\" IN ('Active','Frozen','Reviewed','Revoked','Submitted')");
                    table.ForeignKey(
                        name: "FK_MatterOfficeSessions_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterOfficeSessions_Daks_SourceDakId",
                        column: x => x.SourceDakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterOfficeSessions_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterGreenSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PdfStoragePath = table.Column<string>(type: "text", nullable: false),
                    PdfSha256 = table.Column<string>(type: "text", nullable: false),
                    RendererVersion = table.Column<string>(type: "text", nullable: false),
                    FontManifestSha256 = table.Column<string>(type: "text", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterGreenSheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatterGreenSheetSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkingPreviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterGreenSheetSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterGreenSheetSources_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterOfficeNoteEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    ConverterVersion = table.Column<string>(type: "text", nullable: false),
                    LayoutPolicy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficeNoteEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterOfficeNoteEvidence_MatterOfficialNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "MatterOfficialNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterOfficePreviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDakId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkingRevision = table.Column<int>(type: "integer", nullable: false),
                    ConverterVersion = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    CanonicalText = table.Column<string>(type: "text", nullable: false),
                    CanonicalHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficePreviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatterOfficeSaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ClientGeneration = table.Column<int>(type: "integer", nullable: false),
                    SessionRevision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficeSaveRequests", x => x.Id);
                    table.CheckConstraint("CK_MatterOfficeSaveRequest_State", "\"State\" IN ('Pending','Failed','Committed')");
                    table.ForeignKey(
                        name: "FK_MatterOfficeSaveRequests_MatterOfficeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MatterOfficeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterOfficeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaveRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficeSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterOfficeSnapshots_MatterOfficeSaveRequests_SaveRequestId",
                        column: x => x.SaveRequestId,
                        principalTable: "MatterOfficeSaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterOfficeSnapshots_MatterOfficeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MatterOfficeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatterWorkingNotes_OfficePreviewId",
                table: "MatterWorkingNotes",
                column: "OfficePreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterWorkingNotes_OfficeSessionId",
                table: "MatterWorkingNotes",
                column: "OfficeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterGreenSheets_SourceId",
                table: "MatterGreenSheets",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterGreenSheetSources_MatterId",
                table: "MatterGreenSheetSources",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterGreenSheetSources_WorkingPreviewId",
                table: "MatterGreenSheetSources",
                column: "WorkingPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeNoteEvidence_NoteId",
                table: "MatterOfficeNoteEvidence",
                column: "NoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeNoteEvidence_PreviewId",
                table: "MatterOfficeNoteEvidence",
                column: "PreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeNoteEvidence_SnapshotId",
                table: "MatterOfficeNoteEvidence",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficePreviews_SessionId_SnapshotId",
                table: "MatterOfficePreviews",
                columns: new[] { "SessionId", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficePreviews_SnapshotId",
                table: "MatterOfficePreviews",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSaveRequests_ActorUserId_RequestId",
                table: "MatterOfficeSaveRequests",
                columns: new[] { "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSaveRequests_SessionId",
                table: "MatterOfficeSaveRequests",
                column: "SessionId",
                unique: true,
                filter: "\"State\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSaveRequests_SnapshotId",
                table: "MatterOfficeSaveRequests",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSessions_ActorUserId",
                table: "MatterOfficeSessions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSessions_DocumentKey",
                table: "MatterOfficeSessions",
                column: "DocumentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSessions_MatterId_ActorUserId",
                table: "MatterOfficeSessions",
                columns: new[] { "MatterId", "ActorUserId" },
                unique: true,
                filter: "\"State\" IN ('Active','Frozen','Reviewed')");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSessions_SourceDakId",
                table: "MatterOfficeSessions",
                column: "SourceDakId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSnapshots_SaveRequestId",
                table: "MatterOfficeSnapshots",
                column: "SaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficeSnapshots_SessionId_Version",
                table: "MatterOfficeSnapshots",
                columns: new[] { "SessionId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterWorkingNotes_MatterOfficePreviews_OfficePreviewId",
                table: "MatterWorkingNotes",
                column: "OfficePreviewId",
                principalTable: "MatterOfficePreviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterWorkingNotes_MatterOfficeSessions_OfficeSessionId",
                table: "MatterWorkingNotes",
                column: "OfficeSessionId",
                principalTable: "MatterOfficeSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterGreenSheets_MatterGreenSheetSources_SourceId",
                table: "MatterGreenSheets",
                column: "SourceId",
                principalTable: "MatterGreenSheetSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterGreenSheetSources_MatterOfficePreviews_WorkingPreview~",
                table: "MatterGreenSheetSources",
                column: "WorkingPreviewId",
                principalTable: "MatterOfficePreviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficeNoteEvidence_MatterOfficePreviews_PreviewId",
                table: "MatterOfficeNoteEvidence",
                column: "PreviewId",
                principalTable: "MatterOfficePreviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficeNoteEvidence_MatterOfficeSnapshots_SnapshotId",
                table: "MatterOfficeNoteEvidence",
                column: "SnapshotId",
                principalTable: "MatterOfficeSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficePreviews_MatterOfficeSnapshots_SnapshotId",
                table: "MatterOfficePreviews",
                column: "SnapshotId",
                principalTable: "MatterOfficeSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatterOfficeSaveRequests_MatterOfficeSnapshots_SnapshotId",
                table: "MatterOfficeSaveRequests",
                column: "SnapshotId",
                principalTable: "MatterOfficeSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatterWorkingNotes_MatterOfficePreviews_OfficePreviewId",
                table: "MatterWorkingNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterWorkingNotes_MatterOfficeSessions_OfficeSessionId",
                table: "MatterWorkingNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_MatterOfficeSaveRequests_MatterOfficeSnapshots_SnapshotId",
                table: "MatterOfficeSaveRequests");

            migrationBuilder.DropTable(
                name: "MatterGreenSheets");

            migrationBuilder.DropTable(
                name: "MatterOfficeNoteEvidence");

            migrationBuilder.DropTable(
                name: "MatterGreenSheetSources");

            migrationBuilder.DropTable(
                name: "MatterOfficePreviews");

            migrationBuilder.DropTable(
                name: "MatterOfficeSnapshots");

            migrationBuilder.DropTable(
                name: "MatterOfficeSaveRequests");

            migrationBuilder.DropTable(
                name: "MatterOfficeSessions");

            migrationBuilder.DropIndex(
                name: "IX_MatterWorkingNotes_OfficePreviewId",
                table: "MatterWorkingNotes");

            migrationBuilder.DropIndex(
                name: "IX_MatterWorkingNotes_OfficeSessionId",
                table: "MatterWorkingNotes");

            migrationBuilder.DropColumn(
                name: "OfficePreviewId",
                table: "MatterWorkingNotes");

            migrationBuilder.DropColumn(
                name: "OfficeSessionId",
                table: "MatterWorkingNotes");
        }
    }
}
