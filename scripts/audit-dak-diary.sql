-- Read-only preflight; safe before and after the Phase A migration.
-- Run against the approved local office database with a read-only account.
BEGIN TRANSACTION READ ONLY;

WITH receipts AS (
    SELECT "Id", "DiaryNumber", "ReceivedDate", "Status", "RecordStatus",
        translate(btrim("DiaryNumber", E' \t\r\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') AS diary_key
    FROM "Daks"
), groups AS (
    SELECT diary_key, count(*) AS global_count FROM receipts
    GROUP BY diary_key HAVING count(*) > 1 OR diary_key IS NULL OR length(diary_key) = 0
)
-- Every row below blocks migration. Lifecycle/RecordStatus are diagnostics only;
-- archival, disposal and cancellation never release a stamped diary identity.
SELECT CASE WHEN g.diary_key IS NULL OR length(g.diary_key) = 0 THEN 'BLOCKING_GLOBAL_BLANK'
            ELSE 'BLOCKING_GLOBAL_DUPLICATE' END AS blocking_reason,
    g.diary_key, g.global_count, r."Id", r."DiaryNumber", r."ReceivedDate", r."Status", r."RecordStatus"
FROM groups g JOIN receipts r ON r.diary_key IS NOT DISTINCT FROM g.diary_key
ORDER BY g.diary_key, r."ReceivedDate", r."Id";

-- Informational status counts only; these never exempt a row from the blockers above.
SELECT 'DIAGNOSTIC_STATUS_COUNTS' AS diagnostic, "RecordStatus", "Status", count(*) AS receipt_count
FROM "Daks" GROUP BY "RecordStatus", "Status" ORDER BY "RecordStatus", "Status";

ROLLBACK;
