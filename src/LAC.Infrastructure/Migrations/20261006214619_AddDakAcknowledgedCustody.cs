using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDakAcknowledgedCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Daks", "DakAssignments" IN SHARE ROW EXCLUSIVE MODE;
                DO $$ DECLARE conflicts text; BEGIN
                    SELECT jsonb_agg(jsonb_build_object('dakId', a."DakId", 'diaryNumber', d."DiaryNumber", 'assignmentId', a."Id"))::text INTO conflicts
                    FROM "DakAssignments" a LEFT JOIN "Daks" d ON d."Id" = a."DakId"
                    LEFT JOIN "OfficeDesks" desk ON desk."Id" = a."OfficeDeskId"
                    LEFT JOIN "AppUsers" u ON u."Id" = a."AssignedUserId"
                    LEFT JOIN "AppUsers" author ON author."Id" = a."AssignedByUserId"
                    WHERE d."Id" IS NULL OR desk."Id" IS NULL OR author."Id" IS NULL OR (a."AssignedUserId" IS NOT NULL AND u."Id" IS NULL);
                    IF conflicts IS NOT NULL THEN RAISE EXCEPTION USING
                        MESSAGE = 'Dak custody migration blocked: structurally invalid legacy assignments.', DETAIL = conflicts,
                        HINT = 'Review original records; do not invent holder acceptance, renumber or delete history.';
                    END IF;
                END $$;
                """);


            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "OfficeDesks",
                type: "text",
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "PhysicalState",
                table: "Daks",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<int>(
                name: "ProcessingCycle",
                table: "Daks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionRemarks",
                table: "Daks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Daks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedByUserId",
                table: "Daks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoutingState",
                table: "Daks",
                type: "text",
                nullable: false,
                defaultValue: "Unassigned");

            migrationBuilder.AddColumn<int>(
                name: "EventVersion",
                table: "DakMovements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StateSnapshotJson",
                table: "DakMovements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferId",
                table: "DakMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "DakAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DakTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    FromStatus = table.Column<string>(type: "text", nullable: false),
                    FromRoutingState = table.Column<string>(type: "text", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromHolderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToDeskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationKind = table.Column<string>(type: "text", nullable: false),
                    IncludesPhysicalOriginal = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PhysicalReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PulledBackAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PullBackReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PhysicalReturnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PhysicalReturnProvenance = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakTransfers", x => x.Id);
                    table.UniqueConstraint("AK_DakTransfers_Id_DakId", x => new { x.Id, x.DakId });
                    table.CheckConstraint("CK_DakTransfers_Dispatch", "\"Purpose\" IN ('Marked','Forwarded','Returned') AND \"DestinationKind\" IN ('Officer','RecordRoom') AND ((\"Purpose\" = 'Marked' AND \"FromHolderUserId\" IS NULL) OR (\"Purpose\" <> 'Marked' AND \"FromHolderUserId\" IS NOT NULL AND \"SenderUserId\" = \"FromHolderUserId\"))");
                    table.CheckConstraint("CK_DakTransfers_Physical", "(\"PhysicalReceivedAt\" IS NULL OR (\"IncludesPhysicalOriginal\" AND \"State\" = 'Received' AND \"PhysicalReceivedAt\" = \"ReceivedAt\")) AND (\"PhysicalReturnedAt\" IS NULL OR (\"IncludesPhysicalOriginal\" AND \"State\" = 'PulledBack' AND \"PhysicalReturnedAt\" >= \"PulledBackAt\" AND \"PhysicalReturnProvenance\" IS NOT NULL AND length(btrim(\"PhysicalReturnProvenance\", E' \\t\\r\\n')) > 0)) AND (NOT \"IncludesPhysicalOriginal\" OR \"State\" <> 'Received' OR \"PhysicalReceivedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_DakTransfers_State", "(\"State\" = 'Pending' AND \"ReceivedAt\" IS NULL AND \"PulledBackAt\" IS NULL) OR (\"State\" = 'Received' AND \"ReceivedAt\" IS NOT NULL AND \"ReceivedAt\" >= \"SentAt\" AND \"PulledBackAt\" IS NULL) OR (\"State\" = 'PulledBack' AND \"PulledBackAt\" IS NOT NULL AND \"PulledBackAt\" >= \"SentAt\" AND \"ReceivedAt\" IS NULL AND \"PullBackReason\" IS NOT NULL AND length(btrim(\"PullBackReason\", E' \\t\\r\\n')) > 0)");
                    table.ForeignKey(
                        name: "FK_DakTransfers_AppUsers_FromHolderUserId",
                        column: x => x.FromHolderUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakTransfers_AppUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakTransfers_AppUsers_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakTransfers_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakTransfers_OfficeDesks_FromDeskId",
                        column: x => x.FromDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakTransfers_OfficeDesks_ToDeskId",
                        column: x => x.ToDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DakWorkflowCommandReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DakId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DakWorkflowCommandReceipts", x => x.Id);
                    table.CheckConstraint("CK_DakWorkflowCommands_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_DakWorkflowCommandReceipts_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DakWorkflowCommandReceipts_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Daks_ResolvedByUserId",
                table: "Daks",
                column: "ResolvedByUserId");

            migrationBuilder.Sql("""
                UPDATE "Daks" d SET "RoutingState" = CASE WHEN EXISTS
                    (SELECT 1 FROM "DakAssignments" a WHERE a."DakId" = d."Id" AND a."IsActive") THEN 'LegacyUnconfirmed' ELSE 'Unassigned' END,
                    "PhysicalState" = CASE WHEN "HasPhysicalOriginal" IS NULL THEN 'Unknown'
                        WHEN NOT "HasPhysicalOriginal" THEN 'NotPresent' ELSE 'AtRecordedLocation' END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Daks_CustodyStates",
                table: "Daks",
                sql: "\"RoutingState\" IN ('Unassigned','WithHolder','InTransit','LegacyUnconfirmed') AND \"PhysicalState\" IN ('Unknown','NotPresent','AtRecordedLocation','Held','InTransit','ReturnPending') AND \"ProcessingCycle\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Daks_Resolution",
                table: "Daks",
                sql: "\"Status\" <> 'Resolved' OR (\"ResolvedAt\" IS NOT NULL AND \"ResolvedByUserId\" IS NOT NULL AND \"ResolutionRemarks\" IS NOT NULL AND length(btrim(\"ResolutionRemarks\", E' \\t\\r\\n')) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_DakMovements_TransferId_DakId",
                table: "DakMovements",
                columns: new[] { "TransferId", "DakId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DakAssignments_ReceivedHolder",
                table: "DakAssignments",
                sql: "\"ReceivedAt\" IS NULL OR \"AssignedUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_FromDeskId",
                table: "DakTransfers",
                column: "FromDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_FromHolderUserId",
                table: "DakTransfers",
                column: "FromHolderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_OnePending",
                table: "DakTransfers",
                column: "DakId",
                unique: true,
                filter: "\"State\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_SenderUserId_State",
                table: "DakTransfers",
                columns: new[] { "SenderUserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_ToDeskId",
                table: "DakTransfers",
                column: "ToDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_DakTransfers_ToUserId_State",
                table: "DakTransfers",
                columns: new[] { "ToUserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_DakWorkflowCommandReceipts_ActorUserId_RequestId",
                table: "DakWorkflowCommandReceipts",
                columns: new[] { "ActorUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DakWorkflowCommandReceipts_DakId",
                table: "DakWorkflowCommandReceipts",
                column: "DakId");

            migrationBuilder.AddForeignKey(
                name: "FK_DakMovements_DakTransfers_TransferId_DakId",
                table: "DakMovements",
                columns: new[] { "TransferId", "DakId" },
                principalTable: "DakTransfers",
                principalColumns: new[] { "Id", "DakId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Daks_AppUsers_ResolvedByUserId",
                table: "Daks",
                column: "ResolvedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_dak_custody_history_mutation() RETURNS trigger AS $$ BEGIN
                    RAISE EXCEPTION 'Dak custody history is immutable.';
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_dak_commands_immutable BEFORE UPDATE OR DELETE ON "DakWorkflowCommandReceipts"
                    FOR EACH ROW EXECUTE FUNCTION prevent_dak_custody_history_mutation();
                CREATE FUNCTION prevent_dak_audit_mutation() RETURNS trigger AS $$ BEGIN
                    IF OLD."EntityType" LIKE 'Dak%' OR (TG_OP = 'UPDATE' AND NEW."EntityType" LIKE 'Dak%') THEN
                        RAISE EXCEPTION 'Dak audit history is immutable.';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_dak_audit_immutable BEFORE UPDATE OR DELETE ON "AuditLogs"
                    FOR EACH ROW EXECUTE FUNCTION prevent_dak_audit_mutation();
                CREATE TRIGGER trg_dak_movements_no_truncate BEFORE TRUNCATE ON "DakMovements"
                    FOR EACH STATEMENT EXECUTE FUNCTION prevent_dak_custody_history_mutation();
                CREATE TRIGGER trg_dak_commands_no_truncate BEFORE TRUNCATE ON "DakWorkflowCommandReceipts"
                    FOR EACH STATEMENT EXECUTE FUNCTION prevent_dak_custody_history_mutation();
                CREATE TRIGGER trg_dak_transfers_no_truncate BEFORE TRUNCATE ON "DakTransfers"
                    FOR EACH STATEMENT EXECUTE FUNCTION prevent_dak_custody_history_mutation();
                CREATE TRIGGER trg_dak_audit_no_truncate BEFORE TRUNCATE ON "AuditLogs"
                    FOR EACH STATEMENT EXECUTE FUNCTION prevent_dak_custody_history_mutation();
                CREATE FUNCTION protect_dak_transfer_facts() RETURNS trigger AS $$ BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Dak transfer history cannot be deleted.'; END IF;
                    IF (to_jsonb(NEW) - ARRAY['State','ReceivedAt','PhysicalReceivedAt','PulledBackAt','PullBackReason','PhysicalReturnedAt','PhysicalReturnProvenance']) IS DISTINCT FROM
                       (to_jsonb(OLD) - ARRAY['State','ReceivedAt','PhysicalReceivedAt','PulledBackAt','PullBackReason','PhysicalReturnedAt','PhysicalReturnProvenance']) THEN
                        RAISE EXCEPTION 'Dak dispatch facts are immutable.';
                    END IF;
                    IF OLD."State" <> 'Pending' AND (to_jsonb(NEW) - ARRAY['PhysicalReturnedAt','PhysicalReturnProvenance']) IS DISTINCT FROM
                        (to_jsonb(OLD) - ARRAY['PhysicalReturnedAt','PhysicalReturnProvenance']) THEN RAISE EXCEPTION 'Dak delivery outcome is immutable.'; END IF;
                    IF OLD."PhysicalReturnedAt" IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Physical recovery evidence is immutable.'; END IF;
                    RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_dak_transfer_facts BEFORE UPDATE OR DELETE ON "DakTransfers"
                    FOR EACH ROW EXECUTE FUNCTION protect_dak_transfer_facts();
                CREATE FUNCTION verify_dak_transfer_evidence() RETURNS trigger AS $$ DECLARE t "DakTransfers"%ROWTYPE; BEGIN
                    SELECT * INTO t FROM "DakTransfers" WHERE "Id" = NEW."Id";
                    IF NOT EXISTS (SELECT 1 FROM "DakMovements" m JOIN "DakWorkflowCommandReceipts" r ON r."Id" = m."Id"
                        WHERE m."TransferId" = t."Id" AND m."DakId" = t."DakId" AND r."DakId" = t."DakId"
                        AND m."Action" = t."Purpose" AND m."ActionByUserId" = t."SenderUserId" AND m."ActionAt" = t."SentAt") THEN
                        RAISE EXCEPTION 'Dak dispatch requires immutable command/event evidence.';
                    END IF;
                    IF t."State" = 'Received' AND NOT EXISTS (SELECT 1 FROM "DakMovements" m JOIN "DakWorkflowCommandReceipts" r ON r."Id" = m."Id"
                        WHERE m."TransferId" = t."Id" AND m."Action" = 'Received' AND m."ActionByUserId" = t."ToUserId" AND m."ActionAt" = t."ReceivedAt") THEN
                        RAISE EXCEPTION 'Dak receive requires immutable receiver evidence.';
                    END IF;
                    IF t."State" = 'PulledBack' AND NOT EXISTS (SELECT 1 FROM "DakMovements" m JOIN "DakWorkflowCommandReceipts" r ON r."Id" = m."Id"
                        WHERE m."TransferId" = t."Id" AND m."Action" = 'PulledBack' AND m."ActionByUserId" = t."SenderUserId" AND m."ActionAt" = t."PulledBackAt") THEN
                        RAISE EXCEPTION 'Dak pull-back requires immutable sender evidence.';
                    END IF;
                    IF t."PhysicalReturnedAt" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "DakMovements" m JOIN "DakWorkflowCommandReceipts" r ON r."Id" = m."Id"
                        WHERE m."TransferId" = t."Id" AND m."Action" = 'PhysicalReturnConfirmed' AND m."ActionByUserId" = t."SenderUserId" AND m."ActionAt" = t."PhysicalReturnedAt") THEN
                        RAISE EXCEPTION 'Physical recovery requires immutable sender evidence.';
                    END IF;
                    RETURN NULL;
                END $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER trg_dak_transfer_evidence AFTER INSERT OR UPDATE ON "DakTransfers"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_dak_transfer_evidence();
                CREATE FUNCTION verify_dak_custody_projection() RETURNS trigger AS $$
                DECLARE dak_id uuid; d "Daks"%ROWTYPE; pending_count integer; paper_pending integer; returns_pending integer;
                BEGIN
                    IF TG_TABLE_NAME = 'Daks' THEN dak_id := NEW."Id"; ELSE dak_id := NEW."DakId"; END IF;
                    SELECT * INTO d FROM "Daks" WHERE "Id" = dak_id;
                    SELECT count(*), count(*) FILTER (WHERE "IncludesPhysicalOriginal") INTO pending_count, paper_pending
                        FROM "DakTransfers" WHERE "DakId" = dak_id AND "State" = 'Pending';
                    SELECT count(*) INTO returns_pending FROM "DakTransfers" WHERE "DakId" = dak_id AND "State" = 'PulledBack'
                        AND "IncludesPhysicalOriginal" AND "PhysicalReturnedAt" IS NULL;
                    IF (pending_count > 0) IS DISTINCT FROM (d."RoutingState" = 'InTransit') OR (pending_count > 0 AND d."RecordStatus" <> 'Active') THEN
                        RAISE EXCEPTION 'Dak delivery projection is inconsistent.';
                    END IF;
                    IF (paper_pending > 0) IS DISTINCT FROM (d."PhysicalState" = 'InTransit') OR
                        (returns_pending > 0) IS DISTINCT FROM (d."PhysicalState" = 'ReturnPending') OR (returns_pending > 0 AND pending_count > 0) THEN
                        RAISE EXCEPTION 'Dak physical delivery projection is inconsistent.';
                    END IF;
                    IF pending_count > 0 AND EXISTS (SELECT 1 FROM "DakTransfers" t WHERE t."DakId" = dak_id AND t."State" = 'Pending' AND t."FromHolderUserId" IS NOT NULL AND NOT EXISTS
                        (SELECT 1 FROM "DakAssignments" a WHERE a."DakId" = dak_id AND a."IsActive" AND a."RecordStatus" = 'Active' AND a."AssignedUserId" = t."FromHolderUserId" AND a."OfficeDeskId" = t."FromDeskId" AND a."ReceivedAt" IS NOT NULL)) THEN
                        RAISE EXCEPTION 'Sender remains the confirmed booked holder until receipt.';
                    END IF;
                    IF d."Status" = 'Resolved' AND (pending_count > 0 OR returns_pending > 0 OR d."RoutingState" <> 'WithHolder') THEN
                        RAISE EXCEPTION 'Resolved Dak requires settled delivery and confirmed holding.';
                    END IF;
                    IF d."RoutingState" = 'WithHolder'  AND d."Status" NOT IN ('Disposed','Cancelled') AND NOT EXISTS
                        (SELECT 1 FROM "DakAssignments" a WHERE a."DakId" = dak_id AND a."IsActive" AND a."RecordStatus" = 'Active' AND a."ReceivedAt" IS NOT NULL AND a."AssignedUserId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'WithHolder requires confirmed received custody.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "DakAssignments" a WHERE a."DakId" = dak_id AND a."ReceivedAt" IS NOT NULL AND NOT EXISTS
                        (SELECT 1 FROM "DakMovements" m JOIN "DakWorkflowCommandReceipts" r ON r."Id" = m."Id"
                         WHERE m."DakId" = dak_id AND m."Action" IN ('Received','CustodyConfirmed')
                           AND m."SequenceNumber" = (SELECT max(latest."SequenceNumber") FROM "DakMovements" latest WHERE latest."DakId" = dak_id AND latest."Action" IN ('Received','CustodyConfirmed'))
                           AND m."ActionByUserId" = a."AssignedUserId" AND m."ActionAt" = a."ReceivedAt"
                           AND CASE WHEN m."Action" = 'Received' THEN m."ToDeskId" ELSE m."FromDeskId" END = a."OfficeDeskId")) THEN
                        RAISE EXCEPTION 'Confirmed assignment requires latest immutable personal receipt evidence.';
                    END IF;
                    RETURN NULL;
                END $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER trg_dak_assignment_projection AFTER INSERT OR UPDATE ON "DakAssignments"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_dak_custody_projection();
                CREATE CONSTRAINT TRIGGER trg_dak_delivery_projection AFTER INSERT OR UPDATE ON "Daks"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_dak_custody_projection();
                CREATE CONSTRAINT TRIGGER trg_dak_transfer_projection AFTER INSERT OR UPDATE ON "DakTransfers"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_dak_custody_projection();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "DakTransfers") OR EXISTS (SELECT 1 FROM "DakWorkflowCommandReceipts") OR
                        EXISTS (SELECT 1 FROM "DakMovements" WHERE "EventVersion" > 0) THEN
                        RAISE EXCEPTION 'Dak custody downgrade blocked: preserve acknowledged custody history; use a forward migration.';
                    END IF;
                END $$;
                DROP TRIGGER trg_dak_assignment_projection ON "DakAssignments";
                DROP TRIGGER trg_dak_delivery_projection ON "Daks";
                DROP TRIGGER trg_dak_transfer_projection ON "DakTransfers";
                DROP TRIGGER trg_dak_transfer_evidence ON "DakTransfers";
                DROP TRIGGER trg_dak_transfer_facts ON "DakTransfers";
                DROP TRIGGER trg_dak_commands_immutable ON "DakWorkflowCommandReceipts";
                DROP TRIGGER trg_dak_audit_immutable ON "AuditLogs";
                DROP TRIGGER trg_dak_movements_no_truncate ON "DakMovements";
                DROP TRIGGER trg_dak_commands_no_truncate ON "DakWorkflowCommandReceipts";
                DROP TRIGGER trg_dak_transfers_no_truncate ON "DakTransfers";
                DROP TRIGGER trg_dak_audit_no_truncate ON "AuditLogs";
                DROP FUNCTION verify_dak_custody_projection();
                DROP FUNCTION verify_dak_transfer_evidence();
                DROP FUNCTION protect_dak_transfer_facts();
                DROP FUNCTION prevent_dak_audit_mutation();
                DROP FUNCTION prevent_dak_custody_history_mutation();
                """);


            migrationBuilder.DropForeignKey(
                name: "FK_DakMovements_DakTransfers_TransferId_DakId",
                table: "DakMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Daks_AppUsers_ResolvedByUserId",
                table: "Daks");

            migrationBuilder.DropTable(
                name: "DakTransfers");

            migrationBuilder.DropTable(
                name: "DakWorkflowCommandReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Daks_ResolvedByUserId",
                table: "Daks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Daks_CustodyStates",
                table: "Daks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Daks_Resolution",
                table: "Daks");

            migrationBuilder.DropIndex(
                name: "IX_DakMovements_TransferId_DakId",
                table: "DakMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DakAssignments_ReceivedHolder",
                table: "DakAssignments");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "OfficeDesks");

            migrationBuilder.DropColumn(
                name: "PhysicalState",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "ProcessingCycle",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "ResolutionRemarks",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "RoutingState",
                table: "Daks");

            migrationBuilder.DropColumn(
                name: "EventVersion",
                table: "DakMovements");

            migrationBuilder.DropColumn(
                name: "StateSnapshotJson",
                table: "DakMovements");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "DakMovements");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "DakAssignments");
        }
    }
}
