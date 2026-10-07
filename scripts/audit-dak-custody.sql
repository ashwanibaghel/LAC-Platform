-- Read-only pre-migration custody review. Uses only the pre-acknowledgment schema.
-- An assignment is historical allocation evidence, not proof of RECEIVE.
-- After migration, use routingState/receivedAt and the attention delivery queue.
BEGIN TRANSACTION READ ONLY;
SELECT d."Id" AS dak_id, d."DiaryNumber", d."Status", d."RecordStatus",
    a."Id" AS assignment_id, a."OfficeDeskId", desk."Code" AS desk_code,
    a."AssignedUserId", u."DisplayName" AS recorded_assignee, a."AssignedAt",
    CASE WHEN desk."Id" IS NULL OR (a."AssignedUserId" IS NOT NULL AND u."Id" IS NULL) THEN 'BLOCKING_MISSING_REFERENCE'
         WHEN a."AssignedUserId" IS NULL THEN 'REVIEW_DESK_ONLY_NO_CONFIRMED_OFFICER'
         WHEN NOT desk."IsActive" OR desk."RecordStatus" <> 'Active' THEN 'REVIEW_INACTIVE_DESK'
         WHEN NOT u."IsActive" OR u."RecordStatus" <> 'Active' THEN 'REVIEW_INACTIVE_RECORDED_OFFICER'
         ELSE 'REVIEW_NO_HISTORICAL_RECEIVE_EVIDENCE' END AS review_reason,
    d."HasPhysicalOriginal", d."PhysicalOriginalDeskId", d."PhysicalOriginalUserId", d."PhysicalOriginalLocationNote"
FROM "Daks" d JOIN "DakAssignments" a ON a."DakId" = d."Id"
LEFT JOIN "OfficeDesks" desk ON desk."Id" = a."OfficeDeskId"
LEFT JOIN "AppUsers" u ON u."Id" = a."AssignedUserId"
WHERE a."IsActive"
ORDER BY d."DiaryNumber", d."Id";
ROLLBACK;
