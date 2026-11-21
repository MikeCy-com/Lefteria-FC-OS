using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Restricted human-reviewed identity proof for the original guardian of a
/// newly enrolled Academy child. A review is NOT an app login, an OTP, a
/// canonical contact binding or a guardian permission. In particular this
/// migration grants NO mobile access or account creation capability.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115960000_AcademyGuardianProofReview")]
public sealed class AcademyGuardianProofReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.guardian_proof_reviews (
        registration_id uuid PRIMARY KEY
          REFERENCES academy_intake.registration_placements(registration_id)
          ON DELETE RESTRICT,
        player_id varchar(128) COLLATE "C" NOT NULL
          REFERENCES club.players(id) ON DELETE RESTRICT,
        normalized_phone varchar(15) NOT NULL
          CHECK (normalized_phone ~ '^[1-9][0-9]{7,14}$'),
        proof_method varchar(28) NOT NULL
          CHECK (proof_method IN ('in_person_document','court_guardianship_order')),
        case_reference varchar(64) NOT NULL
          CHECK (case_reference ~ '^[A-Za-z0-9][A-Za-z0-9_-]{7,63}$'),
        request_id uuid NOT NULL UNIQUE
          CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
        reviewed_by uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        status varchar(16) NOT NULL DEFAULT 'verified_proof'
          CHECK(status IN ('verified_proof','revoked')),
        version integer NOT NULL DEFAULT 1 CHECK(version BETWEEN 1 AND 100000),
        revoked_by uuid REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        revoked_at timestamptz,
        CHECK ((status='verified_proof' AND revoked_by IS NULL AND revoked_at IS NULL)
          OR (status='revoked' AND revoked_by IS NOT NULL AND revoked_at IS NOT NULL))
      );
      CREATE TABLE academy_intake.guardian_proof_review_events (
        id uuid PRIMARY KEY,
        registration_id uuid NOT NULL REFERENCES academy_intake.guardian_proof_reviews(registration_id)
          ON DELETE RESTRICT,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        event_type varchar(16) NOT NULL CHECK(event_type IN ('verified_proof','revoked')),
        version integer NOT NULL,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        UNIQUE(registration_id,version)
      );
      CREATE INDEX ix_guardian_proof_verified_phone
        ON academy_intake.guardian_proof_reviews(normalized_phone)
        WHERE status='verified_proof';
      ALTER TABLE academy_intake.guardian_proof_reviews ENABLE ROW LEVEL SECURITY;
      ALTER TABLE academy_intake.guardian_proof_review_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON academy_intake.guardian_proof_reviews,
        academy_intake.guardian_proof_review_events FROM PUBLIC;

      CREATE FUNCTION academy_intake.guardian_proof_immutable()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $immutable$
      BEGIN
        IF TG_OP='DELETE' THEN
          RAISE EXCEPTION 'guardian_proof_history_immutable' USING ERRCODE='23514';
        END IF;
        IF NEW.registration_id<>OLD.registration_id
          OR NEW.player_id<>OLD.player_id
          OR NEW.normalized_phone<>OLD.normalized_phone
          OR NEW.proof_method<>OLD.proof_method
          OR NEW.case_reference<>OLD.case_reference
          OR NEW.request_id<>OLD.request_id
          OR NEW.reviewed_by<>OLD.reviewed_by
          OR NEW.reviewed_at<>OLD.reviewed_at
          OR OLD.status<>'verified_proof' OR NEW.status<>'revoked'
          OR NEW.version<>OLD.version+1
          OR NEW.revoked_by IS NULL OR NEW.revoked_at IS NULL
        THEN RAISE EXCEPTION 'guardian_proof_update_denied' USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $immutable$;
      CREATE TRIGGER guardian_proof_review_immutable
        BEFORE UPDATE OR DELETE ON academy_intake.guardian_proof_reviews
        FOR EACH ROW EXECUTE FUNCTION academy_intake.guardian_proof_immutable();
      CREATE TRIGGER guardian_proof_event_immutable
        BEFORE UPDATE OR DELETE ON academy_intake.guardian_proof_review_events
        FOR EACH ROW EXECUTE FUNCTION academy_intake.reject_evidence_mutation();
      REVOKE ALL ON FUNCTION academy_intake.guardian_proof_immutable() FROM PUBLIC;

      CREATE FUNCTION identity.admin_academy_guardian_proof(
        _digest text,_operation text,_body jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $guardian$
      DECLARE
        v_actor uuid; v_role text;
        v_registration uuid;
        v_player varchar(128);
        v_phone text;
        v_method text;
        v_case text;
        v_request uuid;
        v_version int;
        v_required integer;
        v_review academy_intake.guardian_proof_reviews%ROWTYPE;
        v_approval academy_intake.registrations%ROWTYPE;
      BEGIN
        IF _operation NOT IN ('read','verify','revoke')
          OR _body IS NULL OR jsonb_typeof(_body)<>'object'
          OR _body->>'registration_id' IS NULL
          OR _body->>'registration_id' !~*
            '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR (SELECT count(*) FROM jsonb_object_keys(_body))>6
        THEN RAISE EXCEPTION 'guardian_review_request_invalid' USING ERRCODE='22023'; END IF;

        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy');
        -- Only a verified active Academy Super Admin is allowed to attest
        -- or see this restricted case review; managers cannot reach it.
        IF v_role<>'super_admin' THEN
          RAISE EXCEPTION 'guardian_proof_super_admin_only' USING ERRCODE='42501';
        END IF;
        v_registration:=(_body->>'registration_id')::uuid;
        IF v_registration='00000000-0000-0000-0000-000000000000'
        THEN RAISE EXCEPTION 'guardian_registration_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_approval FROM academy_intake.registrations a
          WHERE a.id=v_registration AND a.organization_id='lefteria_fc_academy';
        IF NOT FOUND OR v_approval.status<>'approved'
          OR v_approval.recorded_by IS NOT NULL
          OR v_approval.onboarded_player_id IS NULL
        THEN RAISE EXCEPTION 'guardian_registration_not_onboarded'
          USING ERRCODE='42501'; END IF;
        SELECT rp.player_id INTO v_player
          FROM academy_intake.registration_placements rp
          JOIN club.players p ON p.id=rp.player_id
            AND p.organization_id=rp.organization_id
          WHERE rp.registration_id=v_registration
            AND rp.organization_id='lefteria_fc_academy'
            AND rp.player_id=v_approval.onboarded_player_id
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_roster_scope_denied'
          USING ERRCODE='42501'; END IF;

        IF _operation='read' THEN
          IF (SELECT count(*) FROM jsonb_object_keys(_body))<>1
          THEN RAISE EXCEPTION 'guardian_read_invalid' USING ERRCODE='22023'; END IF;
          SELECT * INTO v_review FROM academy_intake.guardian_proof_reviews
            WHERE registration_id=v_registration;
          IF NOT FOUND THEN RETURN jsonb_build_object('registration_id',v_registration,
            'player_id',v_player,'status','not_verified',
            'app_access_enabled',false); END IF;
          RETURN jsonb_build_object('registration_id',v_registration,
            'player_id',v_review.player_id,'status',v_review.status,
            'phone_hint','••••'||right(v_review.normalized_phone,4),
            'proof_method',v_review.proof_method,
            'reviewed_at',v_review.reviewed_at,
            'revoked_at',v_review.revoked_at,'version',v_review.version,
            'app_access_enabled',false);
        END IF;

        IF _operation='verify' THEN
          IF (SELECT count(*) FROM jsonb_object_keys(_body))<>6
            OR NOT (_body ?& ARRAY['registration_id','phone','proof_method',
              'case_reference','request_id','attested'])
            OR jsonb_typeof(_body->'attested')<>'boolean'
            OR _body->>'attested'<>'true'
            OR _body->>'request_id' IS NULL
            OR _body->>'request_id' !~*
              '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          THEN RAISE EXCEPTION 'guardian_review_attestation_required'
            USING ERRCODE='22023'; END IF;
          v_phone:=_body->>'phone';
          v_method:=_body->>'proof_method';
          v_case:=_body->>'case_reference';
          v_request:=(_body->>'request_id')::uuid;
          IF v_phone IS NULL OR v_phone !~ '^[1-9][0-9]{7,14}$'
            OR v_phone<>regexp_replace(v_approval.guardian_phone,'[^0-9]','','g')
            OR v_method NOT IN ('in_person_document','court_guardianship_order')
            OR v_case IS NULL
            OR v_case !~ '^[A-Za-z0-9][A-Za-z0-9_-]{7,63}$'
            OR v_request='00000000-0000-0000-0000-000000000000'
          THEN RAISE EXCEPTION 'guardian_review_identity_mismatch'
            USING ERRCODE='22023'; END IF;

          -- Never let a phone currently assigned to a child, coach or
          -- management identity receive a future guardian access claim.
          IF EXISTS(SELECT 1 FROM identity.mobile_identities i
              WHERE i.normalized_phone=v_phone AND i.is_active
                AND i.role IN ('player','coach','management'))
            OR EXISTS(SELECT 1 FROM identity.canonical_contact_bindings c
              WHERE c.normalized_phone=v_phone AND c.kind='player_own')
          THEN RAISE EXCEPTION 'guardian_phone_role_conflict' USING ERRCODE='23505'; END IF;

          IF NOT EXISTS(SELECT 1 FROM academy_intake.registration_sealed_evidence e
              WHERE e.registration_id=v_registration)
          THEN RAISE EXCEPTION 'guardian_application_evidence_missing'
            USING ERRCODE='42501'; END IF;
          SELECT count(DISTINCT c.consent_kind) INTO v_required
            FROM academy_intake.registration_consents c
            WHERE c.registration_id=v_registration AND c.granted
              AND c.consent_kind IN ('participation','medical_authorization',
                'privacy_notice','liability','financial')
              AND c.revision=(SELECT max(latest.revision)
                FROM academy_intake.registration_consents latest
                WHERE latest.registration_id=v_registration
                  AND latest.consent_kind=c.consent_kind);
          IF v_required<>5 THEN RAISE EXCEPTION 'guardian_current_consent_missing'
            USING ERRCODE='42501'; END IF;

          SELECT * INTO v_review FROM academy_intake.guardian_proof_reviews
            WHERE registration_id=v_registration FOR UPDATE;
          IF FOUND THEN
            IF v_review.request_id=v_request AND v_review.normalized_phone=v_phone
              AND v_review.proof_method=v_method AND v_review.case_reference=v_case
              AND v_review.status='verified_proof'
            THEN RETURN jsonb_build_object('registration_id',v_registration,
              'player_id',v_player,'status','verified_proof',
              'version',v_review.version,'app_access_enabled',false,
              'replay',true); END IF;
            RAISE EXCEPTION 'guardian_review_already_recorded'
              USING ERRCODE='23505';
          END IF;

          INSERT INTO academy_intake.guardian_proof_reviews(
            registration_id,player_id,normalized_phone,proof_method,case_reference,
            request_id,reviewed_by,status)
          VALUES(v_registration,v_player,v_phone,v_method,v_case,v_request,
            v_actor,'verified_proof');
          INSERT INTO academy_intake.guardian_proof_review_events(
            id,registration_id,actor_id,event_type,version)
          VALUES(gen_random_uuid(),v_registration,v_actor,'verified_proof',1);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.academy.guardian.proof.verify','succeeded',
            'lefteria_fc_academy',v_method);
          RETURN jsonb_build_object('registration_id',v_registration,
            'player_id',v_player,'status','verified_proof','version',1,
            'app_access_enabled',false,'replay',false);
        END IF;

        IF _operation='revoke' THEN
          IF (SELECT count(*) FROM jsonb_object_keys(_body))<>2
            OR _body->>'version' IS NULL
            OR _body->>'version' !~ '^[0-9]{1,6}$'
          THEN RAISE EXCEPTION 'guardian_review_revocation_invalid'
            USING ERRCODE='22023'; END IF;
          v_version:=(_body->>'version')::int;
          SELECT * INTO v_review FROM academy_intake.guardian_proof_reviews
            WHERE registration_id=v_registration FOR UPDATE;
          IF NOT FOUND THEN RAISE EXCEPTION 'guardian_proof_missing'
            USING ERRCODE='42501'; END IF;
          IF v_review.status<>'verified_proof' OR v_review.version<>v_version
          THEN RAISE EXCEPTION 'guardian_review_changed'
            USING ERRCODE='23505'; END IF;
          UPDATE academy_intake.guardian_proof_reviews
            SET status='revoked',version=v_version+1,
              revoked_at=clock_timestamp(),revoked_by=v_actor
            WHERE registration_id=v_registration;
          INSERT INTO academy_intake.guardian_proof_review_events(
            id,registration_id,actor_id,event_type,version)
          VALUES(gen_random_uuid(),v_registration,v_actor,'revoked',v_version+1);
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,action,outcome,
            organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
            'admin.academy.guardian.proof.revoke','succeeded',
            'lefteria_fc_academy','case_review');
          RETURN jsonb_build_object('registration_id',v_registration,
            'player_id',v_player,'status','revoked','version',v_version+1,
            'app_access_enabled',false);
        END IF;
        RAISE EXCEPTION 'guardian_operation_invalid' USING ERRCODE='22023';
      END $guardian$;
      REVOKE ALL ON FUNCTION identity.admin_academy_guardian_proof(
        text,text,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.guardian_proof_reviews)
        THEN RAISE EXCEPTION 'guardian_proof_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION identity.admin_academy_guardian_proof(text,text,jsonb);
      DROP TRIGGER guardian_proof_event_immutable
        ON academy_intake.guardian_proof_review_events;
      DROP TRIGGER guardian_proof_review_immutable
        ON academy_intake.guardian_proof_reviews;
      DROP FUNCTION academy_intake.guardian_proof_immutable();
      DROP TABLE academy_intake.guardian_proof_review_events;
      DROP TABLE academy_intake.guardian_proof_reviews;
    """);
}
