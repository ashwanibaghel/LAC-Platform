namespace LAC.Infrastructure.Migrations;

internal static class OfficeNotingGuards
{
    internal const string Up = """
        CREATE FUNCTION lac_office_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Committed Office evidence is immutable'; END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['MatterOfficeSnapshots','MatterOfficePreviews','MatterOfficeNoteEvidence','MatterGreenSheetSources','MatterGreenSheets'] LOOP
            EXECUTE format('CREATE TRIGGER office_immutable BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION lac_office_immutable()',t);
            EXECUTE format('CREATE TRIGGER office_no_truncate BEFORE TRUNCATE ON %I FOR EACH STATEMENT EXECUTE FUNCTION lac_office_immutable()',t);
          END LOOP;
        END $$;
        CREATE TRIGGER office_receipts_no_truncate BEFORE TRUNCATE ON "MatterOfficeSaveRequests" FOR EACH STATEMENT EXECUTE FUNCTION lac_office_immutable();
        CREATE FUNCTION lac_office_lease_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Office leases are durable audit records'; END IF;
          IF OLD."State" IN ('Revoked','Submitted') OR NEW."Revision" <> OLD."Revision" + 1 OR
            (to_jsonb(NEW) - ARRAY['State','Revision','WorkingRevision','LatestSnapshotId']) IS DISTINCT FROM
            (to_jsonb(OLD) - ARRAY['State','Revision','WorkingRevision','LatestSnapshotId'])
            THEN RAISE EXCEPTION 'Office lease identity, expiry and retired state are immutable'; END IF;
          IF OLD."State"='Reviewed' AND NEW."State" NOT IN ('Reviewed','Submitted','Revoked') OR
             OLD."State"='Frozen' AND NEW."State" NOT IN ('Reviewed','Revoked')
            THEN RAISE EXCEPTION 'Office lease cannot become writable after review/freeze'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER office_lease_guard BEFORE UPDATE OR DELETE ON "MatterOfficeSessions" FOR EACH ROW EXECUTE FUNCTION lac_office_lease_guard();
        CREATE TRIGGER office_leases_no_truncate BEFORE TRUNCATE ON "MatterOfficeSessions" FOR EACH STATEMENT EXECUTE FUNCTION lac_office_immutable();
        CREATE FUNCTION lac_office_receipt_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Office receipts cannot be deleted'; END IF;
          IF TG_OP = 'UPDATE' AND (OLD."State" = 'Committed' OR
              (to_jsonb(NEW) - ARRAY['State','Deadline','SnapshotId','FailureCode']) IS DISTINCT FROM
              (to_jsonb(OLD) - ARRAY['State','Deadline','SnapshotId','FailureCode']))
            THEN RAISE EXCEPTION 'Office receipt identity and committed result are immutable'; END IF;
          IF NOT EXISTS (SELECT 1 FROM "MatterOfficeSessions" s WHERE s."Id" = NEW."SessionId" AND s."ActorUserId" = NEW."ActorUserId")
            THEN RAISE EXCEPTION 'Office receipt actor/session mismatch'; END IF;
          IF NEW."ClientGeneration" < 0 OR NEW."SessionRevision" < 1 THEN RAISE EXCEPTION 'Invalid Office receipt generation'; END IF;
          IF (NEW."State" = 'Committed') <> (NEW."SnapshotId" IS NOT NULL)
            THEN RAISE EXCEPTION 'Only committed receipts reference a snapshot'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER office_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON "MatterOfficeSaveRequests" FOR EACH ROW EXECUTE FUNCTION lac_office_receipt_guard();
        CREATE FUNCTION lac_office_evidence_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME = 'MatterOfficeSnapshots' THEN
            IF NOT EXISTS (SELECT 1 FROM "MatterOfficeSessions" s WHERE s."Id" = NEW."SessionId" AND s."ActorUserId" = NEW."ActorUserId") OR
               NEW."Version" < 0 OR NEW."FileSize" < 1 OR NEW."Sha256" !~ '^[0-9a-f]{64}$'
              THEN RAISE EXCEPTION 'Invalid Office snapshot provenance'; END IF;
            IF NEW."SaveRequestId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "MatterOfficeSaveRequests" r WHERE r."Id"=NEW."SaveRequestId" AND r."SessionId"=NEW."SessionId" AND r."ActorUserId"=NEW."ActorUserId")
              THEN RAISE EXCEPTION 'Office snapshot receipt mismatch'; END IF;
          ELSIF TG_TABLE_NAME = 'MatterOfficePreviews' THEN
            IF NOT EXISTS (SELECT 1 FROM "MatterOfficeSessions" s JOIN "MatterOfficeSnapshots" p ON p."SessionId"=s."Id"
              WHERE s."Id"=NEW."SessionId" AND p."Id"=NEW."SnapshotId" AND s."MatterId"=NEW."MatterId" AND s."ActorUserId"=NEW."ActorUserId"
                AND s."SourceDakId" IS NOT DISTINCT FROM NEW."SourceDakId" AND p."ActorUserId"=NEW."ActorUserId") OR
              NEW."CanonicalHash" <> upper(encode(sha256(convert_to(NEW."CanonicalText",'UTF8')),'hex'))
              THEN RAISE EXCEPTION 'Office preview provenance or hash mismatch'; END IF;
          ELSIF TG_TABLE_NAME = 'MatterOfficeNoteEvidence' THEN
            IF NOT EXISTS (SELECT 1 FROM "MatterOfficialNotes" n JOIN "MatterOfficePreviews" p ON p."Id"=NEW."PreviewId"
              JOIN "MatterOfficeSnapshots" s ON s."Id"=NEW."SnapshotId" WHERE n."Id"=NEW."NoteId" AND p."SnapshotId"=s."Id"
              AND n."MatterId"=p."MatterId" AND n."AuthorUserId"=p."ActorUserId" AND n."ContentJson"=p."ContentJson"
              AND n."CanonicalText"=p."CanonicalText" AND n."TextHash"=p."CanonicalHash" AND s."Sha256"=NEW."Sha256"
              AND NEW."ConverterVersion"=p."ConverterVersion" AND NEW."LayoutPolicy"='GreenLegalV2')
              THEN RAISE EXCEPTION 'Official Office evidence differs from its submitted note'; END IF;
          ELSIF TG_TABLE_NAME = 'MatterGreenSheetSources' THEN
            IF NEW."WorkingPreviewId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "MatterOfficePreviews" p WHERE p."Id"=NEW."WorkingPreviewId" AND p."MatterId"=NEW."MatterId" AND p."ActorUserId"=NEW."ActorUserId")
              THEN RAISE EXCEPTION 'Private Green Sheet preview mismatch'; END IF;
          ELSIF TG_TABLE_NAME = 'MatterGreenSheets' THEN
            IF NEW."PageCount" < 1 OR NEW."PdfSha256" !~ '^[0-9a-f]{64}$' OR NEW."FontManifestSha256" !~ '^[0-9a-f]{64}$'
              THEN RAISE EXCEPTION 'Invalid Green Sheet physical evidence'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['MatterOfficeSnapshots','MatterOfficePreviews','MatterOfficeNoteEvidence','MatterGreenSheetSources','MatterGreenSheets'] LOOP
            EXECUTE format('CREATE TRIGGER office_evidence_guard BEFORE INSERT ON %I FOR EACH ROW EXECUTE FUNCTION lac_office_evidence_guard()',t);
          END LOOP;
        END $$;
        CREATE FUNCTION lac_office_pointers() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME = 'MatterOfficeSessions' THEN
          IF NEW."LatestSnapshotId" IS NOT NULL AND NOT EXISTS
             (SELECT 1 FROM "MatterOfficeSnapshots" p WHERE p."Id"=NEW."LatestSnapshotId" AND p."SessionId"=NEW."Id" AND p."ActorUserId"=NEW."ActorUserId")
            THEN RAISE EXCEPTION 'Latest snapshot belongs to another Office session'; END IF;
          END IF;
          IF TG_TABLE_NAME = 'MatterOfficeSaveRequests' THEN
          IF NEW."SnapshotId" IS NOT NULL AND NOT EXISTS
             (SELECT 1 FROM "MatterOfficeSnapshots" p WHERE p."Id"=NEW."SnapshotId" AND p."SaveRequestId"=NEW."Id" AND p."SessionId"=NEW."SessionId")
            THEN RAISE EXCEPTION 'Committed receipt does not match its correlated snapshot'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER office_latest_snapshot AFTER INSERT OR UPDATE ON "MatterOfficeSessions" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lac_office_pointers();
        CREATE CONSTRAINT TRIGGER office_committed_snapshot AFTER INSERT OR UPDATE ON "MatterOfficeSaveRequests" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lac_office_pointers();
        """;
    internal const string Down = """
        DO $$ BEGIN IF EXISTS (SELECT 1 FROM "MatterOfficeSessions") OR EXISTS (SELECT 1 FROM "MatterGreenSheetSources")
          THEN RAISE EXCEPTION 'Office noting downgrade blocked: preserve committed evidence'; END IF; END $$;
        DROP FUNCTION lac_office_pointers() CASCADE;
        DROP FUNCTION lac_office_lease_guard() CASCADE;
        DROP FUNCTION lac_office_evidence_guard() CASCADE;
        DROP FUNCTION lac_office_receipt_guard() CASCADE;
        DROP FUNCTION lac_office_immutable() CASCADE;
        """;
}
