using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedMatterNoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NativeOwnerUserId",
                table: "Matters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VillageClassification",
                table: "Daks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unclassified");

            migrationBuilder.CreateTable(
                name: "MatterOfficialNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    CanonicalText = table.Column<string>(type: "text", nullable: false),
                    TextHash = table.Column<string>(type: "text", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    Designation = table.Column<string>(type: "text", nullable: false),
                    DeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeskName = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceDakId = table.Column<Guid>(type: "uuid", nullable: true),
                    CitationsJson = table.Column<string>(type: "text", nullable: false),
                    DocumentManifestJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterOfficialNotes", x => x.Id);
                    table.CheckConstraint("CK_OfficialNote_Number", "\"Number\" > 0 AND \"Version\" = 1");
                    table.ForeignKey(
                        name: "FK_MatterOfficialNotes_AppUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterOfficialNotes_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterPdfAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    DocumentHash = table.Column<string>(type: "text", nullable: true),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    RegionJson = table.Column<string>(type: "text", nullable: false),
                    Quote = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterPdfAnnotations", x => x.Id);
                    table.CheckConstraint("CK_Annotation_Page", "\"Page\" > 0");
                    table.ForeignKey(
                        name: "FK_MatterPdfAnnotations_AppUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterPdfAnnotations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterPdfAnnotations_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterWorkflowReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterWorkflowReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterWorkflowReceipts_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterWorkingNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    CanonicalText = table.Column<string>(type: "text", nullable: false),
                    SourceDakId = table.Column<Guid>(type: "uuid", nullable: true),
                    CitationsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterWorkingNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatterWorkingNotes_AppUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterWorkingNotes_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatterNoteRemarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorJson = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    Designation = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AddressedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AddressedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterNoteRemarks", x => x.Id);
                    table.CheckConstraint("CK_Remark_State", "\"State\" IN ('Open','Addressed')");
                    table.ForeignKey(
                        name: "FK_MatterNoteRemarks_AppUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterNoteRemarks_MatterOfficialNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "MatterOfficialNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatterNoteRemarks_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matters_NativeOwnerUserId",
                table: "Matters",
                column: "NativeOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterNoteRemarks_AuthorUserId",
                table: "MatterNoteRemarks",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterNoteRemarks_MatterId",
                table: "MatterNoteRemarks",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterNoteRemarks_NoteId_CreatedAt",
                table: "MatterNoteRemarks",
                columns: new[] { "NoteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficialNotes_AuthorUserId",
                table: "MatterOfficialNotes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterOfficialNotes_MatterId_Number",
                table: "MatterOfficialNotes",
                columns: new[] { "MatterId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterPdfAnnotations_AuthorUserId",
                table: "MatterPdfAnnotations",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterPdfAnnotations_DocumentId",
                table: "MatterPdfAnnotations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterPdfAnnotations_MatterId_DocumentId",
                table: "MatterPdfAnnotations",
                columns: new[] { "MatterId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatterWorkflowReceipts_ActorUserId_RequestId",
                table: "MatterWorkflowReceipts",
                columns: new[] { "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatterWorkingNotes_AuthorUserId",
                table: "MatterWorkingNotes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterWorkingNotes_MatterId_AuthorUserId",
                table: "MatterWorkingNotes",
                columns: new[] { "MatterId", "AuthorUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Matters_AppUsers_NativeOwnerUserId",
                table: "Matters",
                column: "NativeOwnerUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION lac_noting_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Official Matter noting history is immutable'; END $$;
                CREATE TRIGGER official_note_immutable BEFORE UPDATE OR DELETE ON "MatterOfficialNotes"
                  FOR EACH ROW EXECUTE FUNCTION lac_noting_immutable();
                CREATE TRIGGER noting_receipt_immutable BEFORE UPDATE OR DELETE ON "MatterWorkflowReceipts"
                  FOR EACH ROW EXECUTE FUNCTION lac_noting_immutable();
                CREATE TRIGGER pdf_annotation_immutable BEFORE UPDATE OR DELETE ON "MatterPdfAnnotations"
                  FOR EACH ROW EXECUTE FUNCTION lac_noting_immutable();
                CREATE FUNCTION lac_noting_number() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE expected integer;
                BEGIN
                  PERFORM 1 FROM "Matters" WHERE "Id" = NEW."MatterId" FOR UPDATE;
                  SELECT COALESCE(MAX("Number"), 0) + 1 INTO expected FROM "MatterOfficialNotes" WHERE "MatterId" = NEW."MatterId";
                  IF NEW."Number" <> expected THEN RAISE EXCEPTION 'Official note number must be canonical sequential order'; END IF;
                  IF NEW."TextHash" <> upper(encode(sha256(convert_to(NEW."CanonicalText", 'UTF8')), 'hex'))
                    THEN RAISE EXCEPTION 'Official canonical text hash mismatch'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER official_note_number BEFORE INSERT ON "MatterOfficialNotes" FOR EACH ROW EXECUTE FUNCTION lac_noting_number();
                CREATE FUNCTION lac_remark_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Remark history cannot be deleted'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM "MatterOfficialNotes" WHERE "Id" = NEW."NoteId" AND "MatterId" = NEW."MatterId")
                    THEN RAISE EXCEPTION 'Remark parent Matter mismatch'; END IF;
                  IF TG_OP = 'UPDATE' AND ((to_jsonb(NEW) - ARRAY['State','Revision','AddressedByUserId','AddressedAt'])
                      IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['State','Revision','AddressedByUserId','AddressedAt']) OR NEW."Revision" <> OLD."Revision" + 1)
                    THEN RAISE EXCEPTION 'Remark anchor, authorship and text are immutable'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER remark_history_guard BEFORE INSERT OR UPDATE OR DELETE ON "MatterNoteRemarks" FOR EACH ROW EXECUTE FUNCTION lac_remark_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "MatterOfficialNotes") OR EXISTS (SELECT 1 FROM "MatterWorkflowReceipts")
                     OR EXISTS (SELECT 1 FROM "MatterPdfAnnotations") OR EXISTS (SELECT 1 FROM "MatterWorkingNotes")
                  THEN RAISE EXCEPTION 'Shared Matter noting downgrade blocked: preserve durable drafts and official history'; END IF;
                END $$;
                DROP FUNCTION IF EXISTS lac_noting_immutable() CASCADE;
                DROP FUNCTION IF EXISTS lac_noting_number() CASCADE;
                DROP FUNCTION IF EXISTS lac_remark_guard() CASCADE;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Matters_AppUsers_NativeOwnerUserId",
                table: "Matters");

            migrationBuilder.DropTable(
                name: "MatterNoteRemarks");

            migrationBuilder.DropTable(
                name: "MatterPdfAnnotations");

            migrationBuilder.DropTable(
                name: "MatterWorkflowReceipts");

            migrationBuilder.DropTable(
                name: "MatterWorkingNotes");

            migrationBuilder.DropTable(
                name: "MatterOfficialNotes");

            migrationBuilder.DropIndex(
                name: "IX_Matters_NativeOwnerUserId",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "NativeOwnerUserId",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "VillageClassification",
                table: "Daks");
        }
    }
}
