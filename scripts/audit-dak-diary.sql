-- Read-only preflight; safe before and after the Phase A migration.
-- Run against the approved local office database with a read-only account.
BEGIN TRANSACTION READ ONLY;

WITH receipts AS (
    SELECT "Id", "DiaryNumber", "ReceivedDate", "Status", "RecordStatus",
        translate(btrim("DiaryNumber", E' \t\r\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') AS diary_key
    FROM "Daks"
), groups AS (
    SELECT diary_key, count(*) AS active_count FROM receipts
    WHERE "RecordStatus" = 'Active' GROUP BY diary_key HAVING count(*) > 1 OR length(diary_key) = 0
)
SELECT g.diary_key, g.active_count, r."Id", r."DiaryNumber", r."ReceivedDate", r."Status", r."RecordStatus"
FROM groups g JOIN receipts r ON r.diary_key = g.diary_key
WHERE r."RecordStatus" = 'Active'
ORDER BY g.diary_key, r."ReceivedDate", r."Id";

-- Historical duplicates are reported separately; archived records do not reserve an active key.
SELECT translate(btrim("DiaryNumber", E' \t\r\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') AS diary_key,
    count(*) AS historical_count, array_agg("Id" ORDER BY "Id") AS receipt_ids
FROM "Daks" WHERE "RecordStatus" <> 'Active'
GROUP BY diary_key HAVING count(*) > 1;

ROLLBACK;
