using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>One server-rendered, encrypted registration PDF per child. The
/// existing public upload API never accepts this reserved slot, and only the
/// existing authorized placement trigger promotes the document into Player 360.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128192000_RegistrationFormPdfEscrow")]
public sealed class RegistrationFormPdfEscrow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE academy_intake.registration_document_escrow
        DROP CONSTRAINT registration_document_escrow_document_slot_check,
        DROP CONSTRAINT registration_document_escrow_document_type_check;
      ALTER TABLE academy_intake.registration_document_escrow
        ADD CONSTRAINT registration_document_escrow_document_slot_check CHECK(document_slot IN ('id_front','id_back','birth_certificate','passport_photo','registration_form')),
        ADD CONSTRAINT registration_document_escrow_document_type_check CHECK((document_slot IN ('id_front','id_back') AND document_type='id_passport')
          OR (document_slot='birth_certificate' AND document_type='birth_certificate')
          OR (document_slot='passport_photo' AND document_type='photo')
          OR (document_slot='registration_form' AND document_type='registration_form' AND mime_type='application/pdf' AND expiry_date IS NULL));
      CREATE OR REPLACE FUNCTION academy_intake.store_public_registration_document(
        _registration uuid,_token_digest text,_object uuid,_slot text,
        _expires date,_mime text,_size bigint,_key text,_sha text,
        _cipher bytea,_nonce bytea,_tag bytea
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $store_private$
      DECLARE v_existing academy_intake.registration_document_escrow%ROWTYPE;
      BEGIN
        IF _registration IS NULL OR _object IS NULL
          OR _token_digest IS NULL OR _token_digest !~ '^[0-9a-f]{64}$'
          OR _slot IS NULL OR _slot NOT IN ('id_front','id_back','birth_certificate','passport_photo','registration_form')
          OR (_slot='registration_form' AND (_mime IS DISTINCT FROM 'application/pdf' OR _expires IS NOT NULL OR _size>2000000))
          OR _mime NOT IN ('application/pdf','image/jpeg','image/png','image/webp')
          OR _size NOT BETWEEN 1 AND 10485760
          OR _key IS NULL OR length(_key) NOT BETWEEN 1 AND 120
          OR _sha IS NULL OR _sha !~ '^[0-9a-f]{64}$'
          OR _cipher IS NULL OR octet_length(_cipher)<>_size
          OR _nonce IS NULL OR octet_length(_nonce)<>12
          OR _tag IS NULL OR octet_length(_tag)<>16
          OR (_slot IN ('id_front','id_back') AND _expires IS NULL)
        THEN RAISE EXCEPTION 'invalid_private_attachment'
          USING ERRCODE='22023'; END IF;

        -- The bearer must match an unexpired token for this exact pending
        -- child. One token NEVER authorizes another sibling.
        PERFORM 1 FROM academy_intake.registration_upload_capabilities c
          JOIN academy_intake.registrations r ON r.id=c.registration_id
          WHERE c.registration_id=_registration
            AND c.token_digest=_token_digest
            AND c.expires_at>clock_timestamp()
            AND r.organization_id='lefteria_fc_academy'
            AND r.status='pending'
            AND NOT EXISTS(SELECT 1 FROM academy_intake.registration_placements p
              WHERE p.registration_id=r.id)
          FOR UPDATE OF c;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'private_attachment_unauthorized'
            USING ERRCODE='42501'; END IF;

        SELECT * INTO v_existing
          FROM academy_intake.registration_document_escrow e
          WHERE e.registration_id=_registration AND e.document_slot=_slot
          FOR UPDATE;
        IF FOUND THEN
          IF v_existing.sha256=_sha AND v_existing.mime_type=_mime
             AND v_existing.size_bytes=_size
             AND v_existing.expiry_date IS NOT DISTINCT FROM _expires
             AND v_existing.promoted_at IS NULL
          THEN RETURN 'replay'; END IF;
          RAISE EXCEPTION 'registration_document_slot_conflict'
            USING ERRCODE='23505';
        END IF;
        IF (SELECT count(*) FROM academy_intake.registration_document_escrow
            WHERE registration_id=_registration AND document_slot<>'registration_form')>=4
            AND _slot<>'registration_form' THEN
          RAISE EXCEPTION 'registration_attachment_limit'
            USING ERRCODE='22023'; END IF;
        INSERT INTO academy_intake.registration_document_escrow(
          id,registration_id,document_slot,document_type,expiry_date,
          mime_type,size_bytes,encryption_key_id,sha256,ciphertext,nonce,auth_tag)
        VALUES(_object,_registration,_slot,
          CASE WHEN _slot IN ('id_front','id_back') THEN 'id_passport'
            WHEN _slot='birth_certificate' THEN 'birth_certificate'
            WHEN _slot='registration_form' THEN 'registration_form'
            ELSE 'photo' END,
          _expires,_mime,_size,_key,_sha,_cipher,_nonce,_tag);
        RETURN 'stored';
      END $store_private$;
      CREATE OR REPLACE FUNCTION academy_intake.promote_documents_on_enrollment()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $promote$
      DECLARE v_doc record; v_id uuid; v_title text;
      BEGIN
        IF TG_TABLE_SCHEMA<>'academy_intake'
          OR TG_TABLE_NAME<>'registration_placements'
          OR NEW.organization_id<>'lefteria_fc_academy'
          OR NOT EXISTS (
            SELECT 1 FROM academy_intake.registrations r
            WHERE r.id=NEW.registration_id
              AND r.organization_id=NEW.organization_id)
        THEN RAISE EXCEPTION 'registration_document_placement_invalid'
          USING ERRCODE='42501'; END IF;

        FOR v_doc IN
          SELECT * FROM academy_intake.registration_document_escrow e
          WHERE e.registration_id=NEW.registration_id
            AND e.organization_id=NEW.organization_id
            AND e.promoted_at IS NULL
          ORDER BY e.document_slot FOR UPDATE
        LOOP
          v_id:=gen_random_uuid();
          v_title:=CASE v_doc.document_slot
            WHEN 'id_front' THEN 'Ταυτότητα - εμπρόσθια όψη'
            WHEN 'id_back' THEN 'Ταυτότητα - οπίσθια όψη'
            WHEN 'birth_certificate' THEN 'Πιστοποιητικό γέννησης'
            WHEN 'registration_form' THEN 'Υπογεγραμμένη αίτηση εγγραφής (PDF)'
            ELSE 'Φωτογραφία τύπου διαβατηρίου' END;
          -- The same ciphertext is moved into the existing player vault;
          -- quarantine remains in effect, no scanner bypass/download grant.
          INSERT INTO academy_private.encrypted_document_objects(
            id,organization_id,player_id,key_id,ciphertext,
            nonce,auth_tag,sha256,scan_status)
          VALUES(v_doc.id,NEW.organization_id,NEW.player_id,
            v_doc.encryption_key_id,v_doc.ciphertext,
            v_doc.nonce,v_doc.auth_tag,v_doc.sha256,'quarantined');
          INSERT INTO academy_private.player_documents(
            id,organization_id,player_id,document_type,title,issue_date,
            expiry_date,storage_object_id,encryption_key_id,sha256,
            mime_type,size_bytes,uploaded_by)
          VALUES(v_id,NEW.organization_id,NEW.player_id,v_doc.document_type,
            v_title,NULL,v_doc.expiry_date,v_doc.id,v_doc.encryption_key_id,
            v_doc.sha256,v_doc.mime_type,v_doc.size_bytes,NEW.enrolled_by);
          INSERT INTO academy_private.document_audit_events(
            id,document_id,actor_id,action,request_id)
          VALUES(gen_random_uuid(),v_id,NEW.enrolled_by,'upload',gen_random_uuid());
          INSERT INTO academy_intake.registration_document_promotions(
            escrow_id,registration_id,player_document_id,player_id,approved_by)
          VALUES(v_doc.id,NEW.registration_id,v_id,NEW.player_id,NEW.enrolled_by);
          -- Delete the pre-approval encrypted copy; its single durable
          -- encrypted object now belongs to the exact approved player.
          UPDATE academy_intake.registration_document_escrow SET
            ciphertext=NULL,nonce=NULL,auth_tag=NULL,promoted_at=clock_timestamp(),
            promoted_player_id=NEW.player_id,promoted_document_id=v_id
          WHERE id=v_doc.id AND registration_id=NEW.registration_id
            AND promoted_at IS NULL;
        END LOOP;
        RETURN NEW;
      END $promote$;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registration_document_escrow
          WHERE document_slot='registration_form') THEN
          RAISE EXCEPTION 'registration_pdf_requires_forward_recovery' USING ERRCODE='23505';
        END IF;
      END $guard$;
      ALTER TABLE academy_intake.registration_document_escrow
        DROP CONSTRAINT registration_document_escrow_document_slot_check,
        DROP CONSTRAINT registration_document_escrow_document_type_check;
      ALTER TABLE academy_intake.registration_document_escrow
        ADD CONSTRAINT registration_document_escrow_document_slot_check CHECK(document_slot IN ('id_front','id_back','birth_certificate','passport_photo')),
        ADD CONSTRAINT registration_document_escrow_document_type_check CHECK((document_slot IN ('id_front','id_back') AND document_type='id_passport')
          OR (document_slot='birth_certificate' AND document_type='birth_certificate')
          OR (document_slot='passport_photo' AND document_type='photo'));
      CREATE OR REPLACE FUNCTION academy_intake.store_public_registration_document(
        _registration uuid,_token_digest text,_object uuid,_slot text,
        _expires date,_mime text,_size bigint,_key text,_sha text,
        _cipher bytea,_nonce bytea,_tag bytea
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $store_private$
      DECLARE v_existing academy_intake.registration_document_escrow%ROWTYPE;
      BEGIN
        IF _registration IS NULL OR _object IS NULL
          OR _token_digest IS NULL OR _token_digest !~ '^[0-9a-f]{64}$'
          OR _slot NOT IN ('id_front','id_back','birth_certificate','passport_photo')
          OR _mime NOT IN ('application/pdf','image/jpeg','image/png','image/webp')
          OR _size NOT BETWEEN 1 AND 10485760
          OR _key IS NULL OR length(_key) NOT BETWEEN 1 AND 120
          OR _sha IS NULL OR _sha !~ '^[0-9a-f]{64}$'
          OR _cipher IS NULL OR octet_length(_cipher)<>_size
          OR _nonce IS NULL OR octet_length(_nonce)<>12
          OR _tag IS NULL OR octet_length(_tag)<>16
          OR (_slot IN ('id_front','id_back') AND _expires IS NULL)
        THEN RAISE EXCEPTION 'invalid_private_attachment'
          USING ERRCODE='22023'; END IF;

        -- The bearer must match an unexpired token for this exact pending
        -- child. One token NEVER authorizes another sibling.
        PERFORM 1 FROM academy_intake.registration_upload_capabilities c
          JOIN academy_intake.registrations r ON r.id=c.registration_id
          WHERE c.registration_id=_registration
            AND c.token_digest=_token_digest
            AND c.expires_at>clock_timestamp()
            AND r.organization_id='lefteria_fc_academy'
            AND r.status='pending'
            AND NOT EXISTS(SELECT 1 FROM academy_intake.registration_placements p
              WHERE p.registration_id=r.id)
          FOR UPDATE OF c;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'private_attachment_unauthorized'
            USING ERRCODE='42501'; END IF;

        SELECT * INTO v_existing
          FROM academy_intake.registration_document_escrow e
          WHERE e.registration_id=_registration AND e.document_slot=_slot
          FOR UPDATE;
        IF FOUND THEN
          IF v_existing.sha256=_sha AND v_existing.mime_type=_mime
             AND v_existing.size_bytes=_size
             AND v_existing.expiry_date IS NOT DISTINCT FROM _expires
             AND v_existing.promoted_at IS NULL
          THEN RETURN 'replay'; END IF;
          RAISE EXCEPTION 'registration_document_slot_conflict'
            USING ERRCODE='23505';
        END IF;
        IF (SELECT count(*) FROM academy_intake.registration_document_escrow
            WHERE registration_id=_registration)>=4 THEN
          RAISE EXCEPTION 'registration_attachment_limit'
            USING ERRCODE='22023'; END IF;
        INSERT INTO academy_intake.registration_document_escrow(
          id,registration_id,document_slot,document_type,expiry_date,
          mime_type,size_bytes,encryption_key_id,sha256,ciphertext,nonce,auth_tag)
        VALUES(_object,_registration,_slot,
          CASE WHEN _slot IN ('id_front','id_back') THEN 'id_passport'
            WHEN _slot='birth_certificate' THEN 'birth_certificate'
            ELSE 'photo' END,
          _expires,_mime,_size,_key,_sha,_cipher,_nonce,_tag);
        RETURN 'stored';
      END $store_private$;
      CREATE OR REPLACE FUNCTION academy_intake.promote_documents_on_enrollment()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $promote$
      DECLARE v_doc record; v_id uuid; v_title text;
      BEGIN
        IF TG_TABLE_SCHEMA<>'academy_intake'
          OR TG_TABLE_NAME<>'registration_placements'
          OR NEW.organization_id<>'lefteria_fc_academy'
          OR NOT EXISTS (
            SELECT 1 FROM academy_intake.registrations r
            WHERE r.id=NEW.registration_id
              AND r.organization_id=NEW.organization_id)
        THEN RAISE EXCEPTION 'registration_document_placement_invalid'
          USING ERRCODE='42501'; END IF;

        FOR v_doc IN
          SELECT * FROM academy_intake.registration_document_escrow e
          WHERE e.registration_id=NEW.registration_id
            AND e.organization_id=NEW.organization_id
            AND e.promoted_at IS NULL
          ORDER BY e.document_slot FOR UPDATE
        LOOP
          v_id:=gen_random_uuid();
          v_title:=CASE v_doc.document_slot
            WHEN 'id_front' THEN 'Ταυτότητα - εμπρόσθια όψη'
            WHEN 'id_back' THEN 'Ταυτότητα - οπίσθια όψη'
            WHEN 'birth_certificate' THEN 'Πιστοποιητικό γέννησης'
            ELSE 'Φωτογραφία τύπου διαβατηρίου' END;
          -- The same ciphertext is moved into the existing player vault;
          -- quarantine remains in effect, no scanner bypass/download grant.
          INSERT INTO academy_private.encrypted_document_objects(
            id,organization_id,player_id,key_id,ciphertext,
            nonce,auth_tag,sha256,scan_status)
          VALUES(v_doc.id,NEW.organization_id,NEW.player_id,
            v_doc.encryption_key_id,v_doc.ciphertext,
            v_doc.nonce,v_doc.auth_tag,v_doc.sha256,'quarantined');
          INSERT INTO academy_private.player_documents(
            id,organization_id,player_id,document_type,title,issue_date,
            expiry_date,storage_object_id,encryption_key_id,sha256,
            mime_type,size_bytes,uploaded_by)
          VALUES(v_id,NEW.organization_id,NEW.player_id,v_doc.document_type,
            v_title,NULL,v_doc.expiry_date,v_doc.id,v_doc.encryption_key_id,
            v_doc.sha256,v_doc.mime_type,v_doc.size_bytes,NEW.enrolled_by);
          INSERT INTO academy_private.document_audit_events(
            id,document_id,actor_id,action,request_id)
          VALUES(gen_random_uuid(),v_id,NEW.enrolled_by,'upload',gen_random_uuid());
          INSERT INTO academy_intake.registration_document_promotions(
            escrow_id,registration_id,player_document_id,player_id,approved_by)
          VALUES(v_doc.id,NEW.registration_id,v_id,NEW.player_id,NEW.enrolled_by);
          -- Delete the pre-approval encrypted copy; its single durable
          -- encrypted object now belongs to the exact approved player.
          UPDATE academy_intake.registration_document_escrow SET
            ciphertext=NULL,nonce=NULL,auth_tag=NULL,promoted_at=clock_timestamp(),
            promoted_player_id=NEW.player_id,promoted_document_id=v_id
          WHERE id=v_doc.id AND registration_id=NEW.registration_id
            AND promoted_at IS NULL;
        END LOOP;
        RETURN NEW;
      END $promote$;
      """);
}
