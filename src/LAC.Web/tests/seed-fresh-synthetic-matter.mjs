import { execFileSync } from "child_process";
import crypto from "crypto";

const PSQL = "C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe";
const DB = "lac_onlyoffice_real_9b0459_20261012";

export function createFreshSyntheticMatter() {
  const runId = Date.now().toString(36).toUpperCase();
  const dakId = crypto.randomUUID();
  const matterId = crypto.randomUUID();
  const transfer1Id = crypto.randomUUID();
  const assignment1Id = crypto.randomUUID();
  const move1Id = crypto.randomUUID();
  const move2Id = crypto.randomUUID();
  const move3Id = crypto.randomUUID();
  const receipt2Id = move2Id;
  const receipt3Id = move3Id;
  const villageLinkId = crypto.randomUUID();
  const dakMatterLinkId = crypto.randomUUID();
  const doc1LinkId = crypto.randomUUID();
  const doc2LinkId = crypto.randomUUID();
  const regReqId = crypto.randomUUID();

  const officerA_UserId = "40d1fe67-dc46-4e66-958e-eb2236e5577f";
  const officerA_DeskId = "34150a85-7b0f-402c-bdef-e657fea3763a";
  const villageId = "aac0b8d7-f508-4163-86b9-093ee46458a3";
  const workstreamId = "52712a78-ce8a-42ac-84c4-20ee8ed33cf6";
  const doc1Id = "da772330-a071-4b58-8c33-0ba91fcdc7aa";
  const doc2Id = "fa583b20-f871-41b3-a97e-f552909550e5";

  const diaryNumber = `SYNTHETIC/ACCEPTANCE/${runId}`;
  
  // Coordinated timestamps for trigger verification
  const tsReg = "2026-10-12 10:00:00+00";
  const tsMarked = "2026-10-12 10:00:01+00";
  const tsReceived = "2026-10-12 10:00:02+00";

  const sql = `
    SET search_path TO lac_office_acceptance_20261012;
    BEGIN;
    SET CONSTRAINTS ALL DEFERRED;

    -- 1. Dak
    INSERT INTO "Daks" (
      "Id", "DiaryNumber", "ReceivedDate", "Subject", "SenderName", "WorkstreamId",
      "Status", "MainDocumentId", "Revision", "CreatedAt", "UpdatedAt", "RecordStatus",
      "HasPhysicalOriginal", "RegisteredByUserId", "RegistrationRequestHash", "RegistrationRequestId",
      "PhysicalState", "ProcessingCycle", "RoutingState", "VillageClassification",
      "InwardMode", "Priority"
    ) VALUES (
      '${dakId}', '${diaryNumber}', CURRENT_DATE, 'Synthetic Acceptance Run ${runId} - Green Legal Matter', 'Synthetic Acceptance Office', '${workstreamId}',
      'InProcess', '${doc1Id}', 4, '${tsReg}', '${tsReceived}', 'Active',
      false, '${officerA_UserId}', 'D4A020A94406B3EBACD111D3251786AF45EC1C34270D4CE54642BBAF0280A698', '${regReqId}',
      'Unknown', 1, 'WithHolder', 'VillageSpecific',
      'Electronic', 'Routine'
    );

    -- 2. DakTransfers (Inserted before DakMovements to satisfy FK)
    INSERT INTO "DakTransfers" (
      "Id", "DakId", "Purpose", "FromStatus", "FromRoutingState", "SenderUserId",
      "ToDeskId", "ToUserId", "DestinationKind", "IncludesPhysicalOriginal", "SentAt",
      "Remarks", "State", "ReceivedAt"
    ) VALUES (
      '${transfer1Id}', '${dakId}', 'Marked', 'Registered', 'Unassigned', '${officerA_UserId}',
      '${officerA_DeskId}', '${officerA_UserId}', 'Officer', false, '${tsMarked}',
      'Synthetic fresh acceptance marking', 'Received', '${tsReceived}'
    );

    -- 3. DakMovements (References DakTransfers)
    INSERT INTO "DakMovements" (
      "Id", "DakId", "SequenceNumber", "Action", "ActionByUserId", "ActionByDisplayNameSnapshot", "ActionAt",
      "FromDeskId", "ToDeskId", "WorkstreamIdSnapshot", "WorkstreamNameSnapshot", "EventVersion", "TransferId"
    ) VALUES 
      ('${move1Id}', '${dakId}', 1, 'Registered', '${officerA_UserId}', 'Synthetic ONLYOFFICE Editor', '${tsReg}', NULL, NULL, '${workstreamId}', 'Pilot Correspondence', 0, NULL),
      ('${move2Id}', '${dakId}', 2, 'Marked', '${officerA_UserId}', 'Synthetic ONLYOFFICE Editor', '${tsMarked}', NULL, '${officerA_DeskId}', '${workstreamId}', 'Pilot Correspondence', 1, '${transfer1Id}'),
      ('${move3Id}', '${dakId}', 3, 'Received', '${officerA_UserId}', 'Synthetic ONLYOFFICE Editor', '${tsReceived}', NULL, '${officerA_DeskId}', '${workstreamId}', 'Pilot Correspondence', 2, '${transfer1Id}');

    -- 4. DakWorkflowCommandReceipts (Referenced by movement triggers)
    INSERT INTO "DakWorkflowCommandReceipts" (
      "Id", "DakId", "ActorUserId", "RequestId", "Action", "PayloadHash", "ResultJson", "CreatedAt"
    ) VALUES 
      ('${receipt2Id}', '${dakId}', '${officerA_UserId}', '${crypto.randomUUID()}', 'Marked', 'DAE29F60A93A86546245F2215E372B3C9509A292853C6183D08DE53E54461407', '{"action":"Marked"}', '${tsMarked}'),
      ('${receipt3Id}', '${dakId}', '${officerA_UserId}', '${crypto.randomUUID()}', 'Received', '93384E540D5C8239BA1AD2F3CF4D32F49BB93AF3691A3BA43C240B99BFCF2608', '{"action":"Received"}', '${tsReceived}');

    -- 5. DakAssignments
    INSERT INTO "DakAssignments" (
      "Id", "DakId", "OfficeDeskId", "AssignedUserId", "AssignedByUserId", "AssignedAt",
      "IsActive", "CreatedAt", "UpdatedAt", "RecordStatus", "ReceivedAt"
    ) VALUES (
      '${assignment1Id}', '${dakId}', '${officerA_DeskId}', '${officerA_UserId}', '${officerA_UserId}', '${tsReceived}',
      true, '${tsReceived}', '${tsReceived}', 'Active', '${tsReceived}'
    );

    -- 6. DakVillageLinks
    INSERT INTO "DakVillageLinks" ("Id", "DakId", "VillageId", "CreatedAt", "UpdatedAt", "RecordStatus")
    VALUES ('${villageLinkId}', '${dakId}', '${villageId}', NOW(), NOW(), 'Active');

    -- 7. Matters (Fresh Matter, 0 notes)
    INSERT INTO "Matters" (
      "Id", "VillageId", "Title", "MatterType", "Status", "CreatedAt", "UpdatedAt",
      "CreatedBy", "RecordStatus", "Revision", "WorkstreamId"
    ) VALUES (
      '${matterId}', '${villageId}', 'SYNTHETIC CLEAN RUN ${runId} - Green Noting Acceptance', 'General', 'Open', NOW(), NOW(),
      'Synthetic ONLYOFFICE Editor', 'Active', 1, '${workstreamId}'
    );

    -- 8. DakMatterLinks
    INSERT INTO "DakMatterLinks" ("Id", "DakId", "MatterId", "RecordStatus", "CreatedAt", "UpdatedAt", "CreatedBy")
    VALUES ('${dakMatterLinkId}', '${dakId}', '${matterId}', 'Active', NOW(), NOW(), 'Synthetic ONLYOFFICE Editor');

    -- 9. MatterDocuments
    INSERT INTO "MatterDocuments" ("Id", "MatterId", "DocumentId", "DocumentRole", "DisplayName", "CreatedAt", "UpdatedAt", "RecordStatus")
    VALUES 
      ('${doc1LinkId}', '${matterId}', '${doc1Id}', 'Receipt', 'Synthetic receipt PDF', NOW(), NOW(), 'Active'),
      ('${doc2LinkId}', '${matterId}', '${doc2Id}', 'Supporting summary', 'Synthetic Word summary', NOW(), NOW(), 'Active');

    COMMIT;
  `;

  execFileSync(PSQL, ["-h", "127.0.0.1", "-p", "56683", "-U", "postgres", "-d", DB, "-c", sql], {
    encoding: "utf8"
  });

  console.log(`✓ Successfully seeded clean synthetic Matter & Dak:`);
  console.log(`  Matter ID: ${matterId}`);
  console.log(`  Dak ID:    ${dakId}`);
  console.log(`  Diary:     ${diaryNumber}`);

  return { matterId, dakId, diaryNumber };
}

if (process.argv[1]?.endsWith("seed-fresh-synthetic-matter.mjs")) {
  createFreshSyntheticMatter();
}
