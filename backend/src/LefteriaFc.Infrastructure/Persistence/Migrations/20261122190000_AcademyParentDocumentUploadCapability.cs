using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Short-lived, one-family-child-specific anonymous document capability.
/// Only the enabled, origin-checked API may learn the bearer secret.
/// The database stores a digest, never the bearer secret or an ID scan URL.
/// Both SQL functions are zero-grant by default and require operator opt-in.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122190000_AcademyParentDocumentUploadCapability")]
public sealed class AcademyParentDocumentUploadCapability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.registration_upload_capabilities (
        registration_id uuid PRIMARY KEY
          REFERENCES academy_intake.registrations(id) ON DELETE RESTRICT,
        token_digest char(64) COLLATE "C" NOT NULL UNIQUE
          CHECK(token_digest ~ '^[0-9a-f]{64}$'),
        expires_at timestamptz NOT NULL,
        registered_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK(expires_at>registered_at AND
          expires_at<=registered_at+interval '1 hour')
      );
      REVOKE ALL ON academy_intake.registration_upload_capabilities FROM PUBLIC;
      ALTER TABLE academy_intake.registration_upload_capabilities
        ENABLE ROW LEVEL SECURITY;

      -- Wrapper intentionally requires the token reservation in the SAME
      -- transaction as the submitted child; no successful child receipt may
      -- be returned when its safe upload capability cannot be reserved.
      CREATE FUNCTION academy_intake.submit_public_registration_with_upload(
        _id uuid,_projection jsonb,_sealed bytea,_sha text,_key_id text,
        _fingerprint text,_policy text,_token_digest text
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $submit_with_upload$
      DECLARE v_result text;
      BEGIN
        IF _token_digest IS NULL OR _token_digest !~ '^[0-9a-f]{64}$'
          OR _id IS NULL THEN
          RAISE EXCEPTION 'invalid_upload_capability'
            USING ERRCODE='22023';
        END IF;
        v_result:=academy_intake.submit_public_registration(
          _id,_projection,_sealed,_sha,_key_id,_fingerprint,_policy);
        IF v_result<>'stored' THEN RETURN v_result; END IF;
        INSERT INTO academy_intake.registration_upload_capabilities(
          registration_id,token_digest,expires_at)
        VALUES(_id,_token_digest,clock_timestamp()+interval '1 hour');
        RETURN 'stored';
      END $submit_with_upload$;
      REVOKE ALL ON FUNCTION
        academy_intake.submit_public_registration_with_upload(
          uuid,jsonb,bytea,text,text,text,text,text) FROM PUBLIC;

      CREATE FUNCTION academy_intake.store_public_registration_document(
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
      REVOKE ALL ON FUNCTION
        academy_intake.store_public_registration_document(
          uuid,text,uuid,text,date,text,bigint,text,text,bytea,bytea,bytea)
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $safety$
      BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registration_upload_capabilities)
        THEN RAISE EXCEPTION 'registration_upload_capabilities_need_forward_recovery'
          USING ERRCODE='23505'; END IF;
      END $safety$;
      DROP FUNCTION academy_intake.store_public_registration_document(
        uuid,text,uuid,text,date,text,bigint,text,text,bytea,bytea,bytea);
      DROP FUNCTION academy_intake.submit_public_registration_with_upload(
        uuid,jsonb,bytea,text,text,text,text,text);
      DROP TABLE academy_intake.registration_upload_capabilities;
    """);
}
