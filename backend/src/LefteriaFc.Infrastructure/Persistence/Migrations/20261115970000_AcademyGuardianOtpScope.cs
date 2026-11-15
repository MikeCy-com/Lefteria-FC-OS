using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Original-guardian Academy claim: independent document review AND proof of
/// possession of that precise phone from an OTP challenge issued afterwards.
/// No imported account digest or public-form phone alone can grant access.
/// Every child read re-evaluates current proof, consents and roster status.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115970000_AcademyGuardianOtpScope")]
public sealed class AcademyGuardianOtpScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE identity.academy_guardian_otp_claims (
        registration_id uuid PRIMARY KEY
          REFERENCES academy_intake.guardian_proof_reviews(registration_id)
          ON DELETE RESTRICT,
        normalized_phone varchar(15) NOT NULL
          CHECK(normalized_phone ~ '^[1-9][0-9]{7,14}$'),
        challenge_id uuid NOT NULL,
        confirmed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        -- A phone+proof assertion must be renewed by re-review after 90 days.
        -- No indefinite entitlement follows a recycled phone number.
        expires_at timestamptz NOT NULL,
        CHECK(expires_at > confirmed_at)
      );
      ALTER TABLE identity.academy_guardian_otp_claims ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON identity.academy_guardian_otp_claims FROM PUBLIC;
      CREATE INDEX ix_academy_guardian_otp_claims_phone
        ON identity.academy_guardian_otp_claims(normalized_phone,expires_at);
      CREATE TRIGGER academy_guardian_otp_claims_immutable
        BEFORE UPDATE OR DELETE ON identity.academy_guardian_otp_claims
        FOR EACH ROW EXECUTE FUNCTION academy_intake.reject_evidence_mutation();

      -- Proof alone is only a human-reviewed eligibility fact. A normal
      -- mobile OTP must be issued AFTER that fact and match this phone.
      -- A disabled Parent identity is never revived by a reviewed case.
      CREATE FUNCTION identity.has_pending_academy_guardian_proof(
        _phone text,_challenge uuid
      ) RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $eligible$
        SELECT _phone ~ '^[1-9][0-9]{7,14}$'
          AND _challenge IS NOT NULL
          AND NOT EXISTS (
            SELECT 1 FROM identity.mobile_identities blocked
            WHERE blocked.normalized_phone=_phone AND (
              (blocked.role='parent' AND NOT blocked.is_active)
              OR (blocked.is_active AND blocked.role IN ('player','coach','management'))))
          AND NOT EXISTS(
            SELECT 1 FROM identity.canonical_contact_bindings blocked
            WHERE blocked.normalized_phone=_phone AND blocked.kind='player_own')
          AND EXISTS(
            SELECT 1 FROM identity.otp_challenges otp
            JOIN academy_intake.guardian_proof_reviews proof
              ON proof.normalized_phone=otp.normalized_phone
              AND proof.status='verified_proof'
              AND otp.created_at>=proof.reviewed_at
              AND proof.reviewed_at+interval '90 days'>statement_timestamp()
            JOIN academy_intake.registration_placements rp
              ON rp.registration_id=proof.registration_id
              AND rp.player_id=proof.player_id
              AND rp.organization_id='lefteria_fc_academy'
            JOIN academy_intake.registrations application
              ON application.id=proof.registration_id
              AND application.organization_id=rp.organization_id
              AND application.status='approved'
              AND application.onboarded_player_id=proof.player_id
            JOIN club.players player
              ON player.id=proof.player_id
              AND player.organization_id=rp.organization_id
              AND player.is_active AND NOT player.is_archived
              AND NOT player.is_transferred
            WHERE otp.id=_challenge AND otp.normalized_phone=_phone
              AND NOT otp.is_consumed
              AND otp.expires_at>statement_timestamp()
              AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
              AND (SELECT count(DISTINCT c.consent_kind)
                FROM academy_intake.registration_consents c
                WHERE c.registration_id=proof.registration_id AND c.granted
                AND c.consent_kind IN ('participation','medical_authorization',
                   'privacy_notice','liability','financial')
                AND c.revision=(SELECT max(latest.revision)
                  FROM academy_intake.registration_consents latest
                  WHERE latest.registration_id=c.registration_id
                    AND latest.consent_kind=c.consent_kind))=5);
      $eligible$;
      REVOKE ALL ON FUNCTION identity.has_pending_academy_guardian_proof(
        text,uuid) FROM PUBLIC;

      -- The server calls this only after its OTP digest comparison succeeds.
      -- Database additionally checks the HMAC digest itself, the locked
      -- unconsumed challenge, its issue time, the reviewed proof and consent.
      -- A direct app-DB call without a valid server-computed digest cannot
      -- turn a publicly supplied phone number into a child's entitlement.
      CREATE FUNCTION identity.confirm_academy_guardian_otp(
        _phone text,_challenge uuid,_code_digest bytea
      ) RETURNS integer LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $confirm$
      DECLARE
        v_otp identity.otp_challenges%ROWTYPE;
        v_review record;
        v_added int:=0;
        v_exists boolean;
      BEGIN
        IF _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
          OR _challenge IS NULL OR _challenge='00000000-0000-0000-0000-000000000000'
          OR _code_digest IS NULL OR octet_length(_code_digest)<>32
        THEN RAISE EXCEPTION 'guardian_otp_request_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_otp FROM identity.otp_challenges
          WHERE id=_challenge AND normalized_phone=_phone
          FOR UPDATE;
        IF NOT FOUND OR v_otp.is_consumed OR
          v_otp.expires_at<=clock_timestamp()
          OR v_otp.created_at>clock_timestamp()
          OR v_otp.code_digest IS DISTINCT FROM _code_digest
        THEN RAISE EXCEPTION 'guardian_otp_invalid' USING ERRCODE='42501'; END IF;
        IF NOT identity.has_pending_academy_guardian_proof(_phone,_challenge)
        THEN RETURN 0; END IF;
        FOR v_review IN
          SELECT proof.registration_id,proof.reviewed_at
          FROM academy_intake.guardian_proof_reviews proof
          JOIN academy_intake.registration_placements rp
            ON rp.registration_id=proof.registration_id
            AND rp.player_id=proof.player_id
          WHERE proof.normalized_phone=_phone
            AND proof.status='verified_proof'
            AND proof.reviewed_at<=v_otp.created_at
            AND proof.reviewed_at+interval '90 days'>statement_timestamp()
            AND rp.organization_id='lefteria_fc_academy'
          ORDER BY proof.registration_id
          FOR SHARE OF proof,rp
        LOOP
          IF v_added>=20 THEN
            RAISE EXCEPTION 'guardian_otp_claim_limit' USING ERRCODE='54000'; END IF;
          -- Re-evaluate the exact child and latest consent under current
          -- transaction snapshot. Never broaden the guardian phone match.
          IF NOT EXISTS (
            SELECT 1 FROM academy_intake.registrations application
            JOIN club.players p ON p.id=application.onboarded_player_id
              AND p.organization_id=application.organization_id
            WHERE application.id=v_review.registration_id
              AND application.recorded_by IS NULL
              AND application.organization_id='lefteria_fc_academy'
              AND application.status='approved'
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
              AND (SELECT count(DISTINCT c.consent_kind)
                FROM academy_intake.registration_consents c
                WHERE c.registration_id=application.id AND c.granted
                  AND c.consent_kind IN ('participation','medical_authorization',
                    'privacy_notice','liability','financial')
                  AND c.revision=(SELECT max(last.revision)
                    FROM academy_intake.registration_consents last
                    WHERE last.registration_id=c.registration_id
                      AND last.consent_kind=c.consent_kind))=5)
          THEN CONTINUE; END IF;
          INSERT INTO identity.academy_guardian_otp_claims(
            registration_id,normalized_phone,challenge_id,confirmed_at,expires_at)
          VALUES(v_review.registration_id,_phone,_challenge,clock_timestamp(),
            LEAST(v_review.reviewed_at+interval '90 days',
              clock_timestamp()+interval '90 days'))
          ON CONFLICT (registration_id) DO NOTHING;
          IF FOUND THEN
            v_added:=v_added+1;
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'mobile',clock_timestamp(),
              'mobile.academy.guardian.otp_confirmed','succeeded',
              'lefteria_fc_academy','reviewed_phone');
          END IF;
        END LOOP;
        RETURN v_added;
      END $confirm$;
      REVOKE ALL ON FUNCTION identity.confirm_academy_guardian_otp(
        text,uuid,bytea) FROM PUBLIC;

      -- An OTP claim is not an unrestricted parent grant: scope comes only
      -- from one active, approved roster placement and CURRENT proof/consent.
      CREATE FUNCTION identity.current_academy_guardian_claims(_phone text)
      RETURNS TABLE(registration_id uuid,player_id varchar(128),
        organization_id varchar(32),team_id varchar(128))
      LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $current$
        SELECT rp.registration_id,p.id,p.organization_id,p.team_id
        FROM identity.academy_guardian_otp_claims claim
        JOIN academy_intake.guardian_proof_reviews proof
          ON proof.registration_id=claim.registration_id
          AND proof.normalized_phone=claim.normalized_phone
          AND proof.status='verified_proof'
        JOIN academy_intake.registration_placements rp
          ON rp.registration_id=claim.registration_id
          AND rp.player_id=proof.player_id
          AND rp.organization_id='lefteria_fc_academy'
        JOIN academy_intake.registrations application
          ON application.id=rp.registration_id
          AND application.organization_id=rp.organization_id
          AND application.onboarded_player_id=rp.player_id
          AND application.status='approved'
        JOIN club.players p ON p.id=rp.player_id
          AND p.organization_id=rp.organization_id
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        WHERE _phone ~ '^[1-9][0-9]{7,14}$'
          AND claim.normalized_phone=_phone
          AND claim.expires_at>statement_timestamp()
          AND proof.reviewed_at+interval '90 days'>statement_timestamp()
          AND application.recorded_by IS NULL
          AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
          AND NOT EXISTS(
            SELECT 1 FROM identity.mobile_identities blocked
            WHERE blocked.normalized_phone=_phone
              AND blocked.is_active AND blocked.role IN ('player','coach','management'))
          AND NOT EXISTS(
            SELECT 1 FROM identity.canonical_contact_bindings blocked
            WHERE blocked.normalized_phone=_phone AND blocked.kind='player_own')
          AND (SELECT count(DISTINCT c.consent_kind)
            FROM academy_intake.registration_consents c
            WHERE c.registration_id=rp.registration_id AND c.granted
              AND c.consent_kind IN ('participation','medical_authorization',
                'privacy_notice','liability','financial')
              AND c.revision=(SELECT max(last.revision)
                FROM academy_intake.registration_consents last
                WHERE last.registration_id=c.registration_id
                  AND last.consent_kind=c.consent_kind))=5;
      $current$;
      REVOKE ALL ON FUNCTION identity.current_academy_guardian_claims(
        text) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION identity.has_current_guardian_phone_grant(_phone text)
      RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT _phone ~ '^[1-9][0-9]{7,14}$' AND (
          EXISTS(SELECT 1 FROM identity.valid_guardian_phone_grants() valid
            WHERE valid.target_phone=_phone)
          OR EXISTS(SELECT 1 FROM identity.current_academy_guardian_claims(_phone))
        );
      $scope$;

      CREATE OR REPLACE FUNCTION identity.current_phone_granted_children(
        _identity uuid,_phone text,_role text,_expires timestamptz
      ) RETURNS TABLE(
        player_id varchar(128),organization_id varchar(32),
        team_id varchar(128),is_primary boolean,permission_id uuid
      ) LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT p.id,p.organization_id,p.team_id,false,g.permission_id
        FROM identity.mobile_identities recipient
        JOIN identity.valid_guardian_phone_grants() g
          ON g.target_phone=recipient.normalized_phone
        JOIN club.players p ON p.id=g.player_id
          AND p.organization_id=g.organization_id
        WHERE recipient.id=_identity AND recipient.normalized_phone=_phone
          AND recipient.role='parent' AND _role='parent'
          AND recipient.is_active AND _expires>statement_timestamp()
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        UNION
        SELECT p.id,p.organization_id,p.team_id,true,NULL::uuid
        FROM identity.mobile_identities recipient
        JOIN identity.current_academy_guardian_claims(_phone) claim
          ON claim.organization_id='lefteria_fc_academy'
        JOIN club.players p ON p.id=claim.player_id
          AND p.organization_id=claim.organization_id
        WHERE recipient.id=_identity AND recipient.normalized_phone=_phone
          AND recipient.role='parent' AND _role='parent'
          AND recipient.is_active AND _expires>statement_timestamp()
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred;
      $scope$;

      -- A new Academy guardian currently cannot delegate phone permissions
      -- without a separately audited lineage root. Old canonical parent
      -- delegates continue to work; this prevents silent unusable 'success'.
      CREATE FUNCTION identity.require_canonical_guardian_delegation_root()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $root$
      BEGIN
        IF NEW.source_permission_id IS NULL AND NOT EXISTS (
          SELECT 1 FROM identity.mobile_identities i
          JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role='parent'
            AND l.normalized_phone=i.normalized_phone
            AND l.collection='players' AND l.kind='guardian'
            AND l.record_id=NEW.player_id
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.kind=l.kind
            AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
          WHERE i.id=NEW.issuer_identity_id AND i.is_active
            AND i.role='parent')
        THEN RAISE EXCEPTION 'guardian_claim_delegation_not_enabled'
          USING ERRCODE='42501'; END IF;
        RETURN NEW;
      END $root$;
      REVOKE ALL ON FUNCTION identity.require_canonical_guardian_delegation_root()
        FROM PUBLIC;
      CREATE TRIGGER guard_unanchored_family_guardian_root
        BEFORE INSERT ON identity.family_guardian_permissions
        FOR EACH ROW EXECUTE FUNCTION identity.require_canonical_guardian_delegation_root();
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $safe$ BEGIN
        IF EXISTS(SELECT 1 FROM identity.academy_guardian_otp_claims)
        THEN RAISE EXCEPTION 'guardian_otp_claim_history_requires_forward_recovery'; END IF;
      END $safe$;
      DROP TRIGGER guard_unanchored_family_guardian_root
        ON identity.family_guardian_permissions;
      DROP FUNCTION identity.require_canonical_guardian_delegation_root();
      CREATE OR REPLACE FUNCTION identity.current_phone_granted_children(
        _identity uuid,_phone text,_role text,_expires timestamptz
      ) RETURNS TABLE(player_id varchar(128),organization_id varchar(32),
        team_id varchar(128),is_primary boolean,permission_id uuid)
      LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT player.id,player.organization_id,player.team_id,false,
          grant_row.permission_id
        FROM identity.mobile_identities recipient
        JOIN identity.valid_guardian_phone_grants() grant_row
          ON grant_row.target_phone=recipient.normalized_phone
        JOIN club.players player ON player.id=grant_row.player_id
          AND player.organization_id=grant_row.organization_id
        WHERE recipient.id=_identity AND recipient.normalized_phone=_phone
          AND recipient.role='parent' AND _role='parent'
          AND recipient.is_active AND _expires>statement_timestamp()
          AND player.is_active AND NOT player.is_archived
          AND NOT player.is_transferred;
      $scope$;
      CREATE OR REPLACE FUNCTION identity.has_current_guardian_phone_grant(_phone text)
      RETURNS boolean LANGUAGE sql SECURITY DEFINER STABLE
      SET search_path=pg_catalog,pg_temp AS $scope$
        SELECT _phone ~ '^[1-9][0-9]{7,14}$' AND EXISTS(
          SELECT 1 FROM identity.valid_guardian_phone_grants() valid
          WHERE valid.target_phone=_phone);
      $scope$;
      DROP FUNCTION identity.current_academy_guardian_claims(text);
      DROP FUNCTION identity.confirm_academy_guardian_otp(text,uuid,bytea);
      DROP FUNCTION identity.has_pending_academy_guardian_proof(text,uuid);
      DROP TRIGGER academy_guardian_otp_claims_immutable
        ON identity.academy_guardian_otp_claims;
      DROP TABLE identity.academy_guardian_otp_claims;
    """);
}
