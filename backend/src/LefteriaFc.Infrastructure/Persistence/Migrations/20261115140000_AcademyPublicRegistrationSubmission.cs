using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Academy anonymous application transaction and durable throttle. There is
/// intentionally NO grant to the normal API role in this migration: a staging
/// operator must approve the privacy/consent policy and grant only EXECUTE.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115140000_AcademyPublicRegistrationSubmission")]
public sealed class AcademyPublicRegistrationSubmission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.registration_rate_buckets (
        fingerprint char(64) COLLATE "C" PRIMARY KEY
          CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
        window_started_at timestamptz NOT NULL,
        attempts smallint NOT NULL CHECK (attempts BETWEEN 1 AND 5),
        last_attempt_at timestamptz NOT NULL
      );
      REVOKE ALL ON academy_intake.registration_rate_buckets FROM PUBLIC;
      ALTER TABLE academy_intake.registration_rate_buckets ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION academy_intake.submit_public_registration(
        _id uuid,_projection jsonb,_sealed bytea,_sha text,
        _key_id text,_fingerprint text,_policy text
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $academy_registration_submit$
      DECLARE
        v_time timestamptz := clock_timestamp();
        v_attempts smallint;
        v_first text;
        v_last text;
        v_guardian text;
        v_phone text;
        v_email text;
        v_birth date;
      BEGIN
        -- A caller cannot choose club, status, invoice, player, group or
        -- receipt. Only this server-generated UUID and approved projection
        -- are permitted. Critical authorization is checked twice.
        IF _id IS NULL OR _id='00000000-0000-0000-0000-000000000000'::uuid
           OR _projection IS NULL OR jsonb_typeof(_projection)<>'object'
           OR _sealed IS NULL OR length(_sealed) NOT BETWEEN 32 AND 65536
           OR _sha IS NULL OR _sha !~ '^[0-9a-f]{64}$'
           OR _key_id IS NULL OR length(_key_id) NOT BETWEEN 1 AND 120
           OR _key_id !~ '^[A-Za-z0-9_.-]+$'
           OR _fingerprint IS NULL OR _fingerprint !~ '^[0-9a-f]{64}$'
           OR _policy IS NULL OR length(_policy) NOT BETWEEN 1 AND 80
           OR _policy !~ '^[A-Za-z0-9_.-]+$'
           OR encode(digest(_sealed,'sha256'),'hex')<>_sha
           OR _projection->>'consent_participation' IS DISTINCT FROM 'true'
           OR _projection->>'consent_medical_auth' IS DISTINCT FROM 'true'
           OR _projection->>'consent_gdpr' IS DISTINCT FROM 'true'
           OR _projection->>'consent_liability' IS DISTINCT FROM 'true'
           OR _projection->>'consent_financial' IS DISTINCT FROM 'true'
           OR _projection->>'consent_media' NOT IN ('true','false')
           OR _projection->>'consent_communications' NOT IN ('true','false')
        THEN
          RAISE EXCEPTION 'invalid_academy_application' USING ERRCODE='22023';
        END IF;
        v_first:=btrim(_projection->>'applicant_first_name');
        v_last:=btrim(_projection->>'applicant_last_name');
        v_guardian:=btrim(_projection->>'guardian_name');
        v_phone:=btrim(_projection->>'guardian_phone');
        v_email:=btrim(_projection->>'guardian_email');
        IF v_first IS NULL OR length(v_first) NOT BETWEEN 1 AND 100
          OR v_last IS NULL OR length(v_last) NOT BETWEEN 1 AND 100
          OR v_guardian IS NULL OR length(v_guardian) NOT BETWEEN 1 AND 200
          OR v_phone IS NULL OR length(v_phone) NOT BETWEEN 7 AND 32
          OR v_email IS NULL OR length(v_email) NOT BETWEEN 3 AND 254
          OR v_email !~ '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'
          OR _projection->>'guardian_relationship' NOT IN
             ('Πατέρας','Μητέρα','Κηδεμόνας')
          OR _projection->>'applicant_birth_date' IS NULL
          OR _projection->>'applicant_birth_date' !~ '^\d{4}-\d{2}-\d{2}$'
        THEN
          RAISE EXCEPTION 'invalid_academy_identity' USING ERRCODE='22023';
        END IF;
        v_birth := (_projection->>'applicant_birth_date')::date;
        IF v_birth>=current_date OR v_birth<date '2000-01-01'
        THEN RAISE EXCEPTION 'invalid_academy_birth_date' USING ERRCODE='22023';
        END IF;

        -- Lock the fingerprint in PostgreSQL, not process memory. No
        -- registration or sensitive evidence is persisted after a 429.
        INSERT INTO academy_intake.registration_rate_buckets
          (fingerprint,window_started_at,attempts,last_attempt_at)
        VALUES (_fingerprint,v_time,1,v_time)
        ON CONFLICT(fingerprint) DO UPDATE SET
          attempts=CASE WHEN
            academy_intake.registration_rate_buckets.window_started_at<=v_time-interval '1 hour'
            THEN 1 ELSE academy_intake.registration_rate_buckets.attempts+1 END,
          window_started_at=CASE WHEN
            academy_intake.registration_rate_buckets.window_started_at<=v_time-interval '1 hour'
            THEN v_time ELSE academy_intake.registration_rate_buckets.window_started_at END,
          last_attempt_at=v_time
        WHERE academy_intake.registration_rate_buckets.window_started_at<=v_time-interval '1 hour'
           OR academy_intake.registration_rate_buckets.attempts<5
        RETURNING attempts INTO v_attempts;
        IF v_attempts IS NULL THEN RETURN 'rate_limited'; END IF;

        INSERT INTO academy_intake.registrations(
          id,organization_id,applicant_first_name,applicant_last_name,
          applicant_birth_date,guardian_name,guardian_relationship,
          guardian_phone,guardian_email,status,submitted_at)
        VALUES(_id,'lefteria_fc_academy',v_first,v_last,v_birth,v_guardian,
          _projection->>'guardian_relationship',v_phone,v_email,'pending',v_time);

        INSERT INTO academy_intake.registration_sealed_evidence(
          registration_id,encryption_key_id,evidence_ciphertext,evidence_sha256,stored_at)
        VALUES(_id,_key_id,_sealed,_sha,v_time);

        INSERT INTO academy_intake.registration_consents(
          registration_id,consent_kind,revision,granted,policy_version,
          recorded_at,evidence_digest)
        SELECT _id,decisions.kind,1,decisions.granted,_policy,v_time,
          encode(digest(convert_to(
            _sha||':'||decisions.kind||':'||decisions.granted::text||':'||_policy,'UTF8'),'sha256'),'hex')
        FROM (VALUES
          ('participation',true),
          ('medical_authorization',true),
          ('privacy_notice',true),
          ('media',(_projection->>'consent_media')::boolean),
          ('communications',(_projection->>'consent_communications')::boolean),
          ('liability',true),
          ('financial',true)
        ) AS decisions(kind,granted);
        RETURN 'stored';
      END $academy_registration_submit$;

      -- PostgreSQL would otherwise grant EXECUTE to PUBLIC by default.
      REVOKE ALL ON FUNCTION academy_intake.submit_public_registration(
        uuid,jsonb,bytea,text,text,text,text) FROM PUBLIC;
      -- The intake queue is only visible to the Academy super administrator.
      -- Location and city managers cannot see unassigned children's details.
      CREATE FUNCTION academy_intake.read_admin_registration_queue(
        _digest text,_limit integer,_offset integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_intake_queue$
      DECLARE v_role text; v_items jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}
      -- give the isolated API runtime role only USAGE on schema + EXECUTE on
      -- this single function after verifying policy/encryption/reverse proxy.
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registrations LIMIT 1) THEN
          RAISE EXCEPTION 'cannot_drop_collected_registration_data'
            USING ERRCODE='23505';
        END IF;
      END $guard$;
      DROP FUNCTION academy_intake.read_admin_registration_queue(text,integer,integer);
      DROP FUNCTION academy_intake.submit_public_registration(
        uuid,jsonb,bytea,text,text,text,text);
      DROP TABLE academy_intake.registration_rate_buckets;
    """);
}

           OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
           OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
        THEN RAISE EXCEPTION 'invalid_intake_filter' USING ERRCODE='22023'; END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,'lefteria_fc_academy');
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'intake_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id='lefteria_fc_academy'
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending
            AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin' THEN
          RAISE EXCEPTION 'intake_access_denied' USING ERRCODE='42501';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',r.id,'applicant_first_name',r.applicant_first_name,
          'applicant_last_name',r.applicant_last_name,
          'applicant_birth_date',r.applicant_birth_date,
          'guardian_name',r.guardian_name,
          'guardian_phone',r.guardian_phone,'guardian_email',r.guardian_email,
          'status',r.status,'submitted_at',r.submitted_at)
          ORDER BY r.submitted_at DESC,r.id),'[]'::jsonb) INTO v_items
          FROM (SELECT id,applicant_first_name,applicant_last_name,
            applicant_birth_date,guardian_name,guardian_phone,guardian_email,
            status,submitted_at FROM academy_intake.registrations
            WHERE organization_id='lefteria_fc_academy'
            ORDER BY submitted_at DESC,id DESC LIMIT _limit OFFSET _offset) r;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy',
          'offset',_offset,'applications',v_items);
      END $academy_intake_queue$;
      REVOKE ALL ON FUNCTION academy_intake.read_admin_registration_queue(
        text,integer,integer) FROM PUBLIC;

      -- DO NOT grant table access or EXECUTE automatically. An operator must
      -- give the isolated API runtime role only USAGE on schema + EXECUTE on
      -- this single function after verifying policy/encryption/reverse proxy.
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registrations LIMIT 1) THEN
          RAISE EXCEPTION 'cannot_drop_collected_registration_data'
            USING ERRCODE='23505';
        END IF;
      END $guard$;
      DROP FUNCTION academy_intake.submit_public_registration(
        uuid,jsonb,bytea,text,text,text,text);
      DROP TABLE academy_intake.registration_rate_buckets;
    """);
}
