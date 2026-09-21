using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedTimeAndAttentionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourtProceedings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProceedingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OrderType = table.Column<string>(type: "text", nullable: true),
                    RestraintNature = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    NextDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtProceedings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtProceedings_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibleOfficeDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedByDesignationSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MatterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DakId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutwardId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourtProceedingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_AppUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_CourtCases_CourtCaseId",
                        column: x => x.CourtCaseId,
                        principalTable: "CourtCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_CourtProceedings_CourtProceedingId",
                        column: x => x.CourtProceedingId,
                        principalTable: "CourtProceedings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Daks_DakId",
                        column: x => x.DakId,
                        principalTable: "Daks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_OfficeDesks_ResponsibleOfficeDeskId",
                        column: x => x.ResponsibleOfficeDeskId,
                        principalTable: "OfficeDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Outwards_OutwardId",
                        column: x => x.OutwardId,
                        principalTable: "Outwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Workstreams_WorkstreamId",
                        column: x => x.WorkstreamId,
                        principalTable: "Workstreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledEventEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActorDesignationSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    WorkstreamIdSnapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstreamNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDeskNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetDeskId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDeskNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceUserDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetUserDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OldScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OldScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    NewScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ReminderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReminderDaysBefore = table.Column<int>(type: "integer", nullable: true),
                    LinkedWorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEventEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledEventEvents_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEventEvents_ScheduledEvents_ScheduledEventId",
                        column: x => x.ScheduledEventId,
                        principalTable: "ScheduledEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysBefore = table.Column<int>(type: "integer", nullable: false),
                    ReminderTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    RecordStatus = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledReminders_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledReminders_ScheduledEvents_ScheduledEventId",
                        column: x => x.ScheduledEventId,
                        principalTable: "ScheduledEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtProceedings_CourtCaseId",
                table: "CourtProceedings",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_ActionAt",
                table: "ScheduledEventEvents",
                column: "ActionAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_ActorUserId",
                table: "ScheduledEventEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_ScheduledEventId",
                table: "ScheduledEventEvents",
                column: "ScheduledEventId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_ScheduledEventId_SequenceNumber",
                table: "ScheduledEventEvents",
                columns: new[] { "ScheduledEventId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_SourceDeskId",
                table: "ScheduledEventEvents",
                column: "SourceDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_TargetDeskId",
                table: "ScheduledEventEvents",
                column: "TargetDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventEvents_WorkstreamIdSnapshot",
                table: "ScheduledEventEvents",
                column: "WorkstreamIdSnapshot");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_AssignedUserId",
                table: "ScheduledEvents",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourtCaseId",
                table: "ScheduledEvents",
                column: "CourtCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourtProceedingId",
                table: "ScheduledEvents",
                column: "CourtProceedingId",
                unique: true,
                filter: "\"CourtProceedingId\" IS NOT NULL AND \"Status\" != 'Cancelled' AND \"RecordStatus\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CreatedByUserId",
                table: "ScheduledEvents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_DakId",
                table: "ScheduledEvents",
                column: "DakId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_LastActivityAt",
                table: "ScheduledEvents",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_MatterId",
                table: "ScheduledEvents",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_OutwardId",
                table: "ScheduledEvents",
                column: "OutwardId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_ResponsibleOfficeDeskId",
                table: "ScheduledEvents",
                column: "ResponsibleOfficeDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_ScheduledDate",
                table: "ScheduledEvents",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_ScheduledDate_Status",
                table: "ScheduledEvents",
                columns: new[] { "ScheduledDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_Status",
                table: "ScheduledEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_WorkItemId",
                table: "ScheduledEvents",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_WorkstreamId",
                table: "ScheduledEvents",
                column: "WorkstreamId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReminders_CreatedByUserId",
                table: "ScheduledReminders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReminders_ScheduledEventId",
                table: "ScheduledReminders",
                column: "ScheduledEventId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReminders_ScheduledEventId_IsActive",
                table: "ScheduledReminders",
                columns: new[] { "ScheduledEventId", "IsActive" });

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_scheduled_event_events_immutable()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Scheduled event events are strictly immutable. Official schedule history cannot be modified or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_scheduled_event_events_immutable
                BEFORE UPDATE OR DELETE ON ""ScheduledEventEvents""
                FOR EACH ROW
                EXECUTE FUNCTION fn_scheduled_event_events_immutable();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_scheduled_event_events_immutable ON ""ScheduledEventEvents"";
                DROP FUNCTION IF EXISTS fn_scheduled_event_events_immutable();
            ");

            migrationBuilder.DropTable(
                name: "ScheduledEventEvents");

            migrationBuilder.DropTable(
                name: "ScheduledReminders");

            migrationBuilder.DropTable(
                name: "ScheduledEvents");

            migrationBuilder.DropTable(
                name: "CourtProceedings");
        }
    }
}
