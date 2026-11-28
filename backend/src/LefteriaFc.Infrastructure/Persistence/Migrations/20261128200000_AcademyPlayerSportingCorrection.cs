using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Allow current Academy sporting edits without invalidating the
/// enrollment response-loss retry. The first edit saves the pre-edit
/// enrollment values that the original retry contract would have compared.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128200000_AcademyPlayerSportingCorrection")]
public sealed class AcademyPlayerSportingCorrection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE academy_intake.registration_sporting_baselines (
        registration_id uuid PRIMARY KEY REFERENCES academy_intake.registration_placements(registration_id)
          ON DELETE RESTRICT,
        position varchar(32) NOT NULL,
        number integer NOT NULL,
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      ALTER TABLE academy_intake.registration_sporting_baselines ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON academy_intake.registration_sporting_baselines FROM PUBLIC;
      CREATE TRIGGER registration_sporting_baseline_immutable
        BEFORE UPDATE OR DELETE ON academy_intake.registration_sporting_baselines
        FOR EACH ROW EXECUTE FUNCTION academy_intake.reject_evidence_mutation();

      CREATE OR REPLACE FUNCTION identity.enroll_admin_academy_registration(
        _digest text,_data jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $enrol$
      DECLARE
        v_actor uuid;
        v_role text;
        v_registration uuid;
        v_request uuid;
        v_expected int;
        v_city uuid;
        v_location uuid;
        v_group text;
        v_scope uuid;
        v_position text;
        v_number int;
        v_player text;
        v_name text;
        v_application academy_intake.registrations%ROWTYPE;
        v_required_count int;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _data IS NULL OR jsonb_typeof(_data)<>'object'
          OR (SELECT count(*) FROM jsonb_object_keys(_data))<>8
          OR EXISTS(SELECT 1 FROM jsonb_object_keys(_data) AS k(key)
            WHERE k.key NOT IN ('id','version','request_id','city_id',
              'location_id','group_id','position','number'))
          OR _data->>'id' IS NULL OR _data->>'request_id' IS NULL
          OR _data->>'id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'request_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'city_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'location_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'group_id' !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _data->>'version' !~ '^[0-9]{1,6}$'
          OR _data->>'number' !~ '^[0-9]{1,3}$'
        THEN RAISE EXCEPTION 'invalid_enrollment_request' USING ERRCODE='22023'; END IF;
        v_registration:=(_data->>'id')::uuid;
        v_request:=(_data->>'request_id')::uuid;
        v_city:=(_data->>'city_id')::uuid;
        v_location:=(_data->>'location_id')::uuid;
        v_group:=_data->>'group_id';
        v_expected:=(_data->>'version')::int;
        v_position:=_data->>'position';
        v_number:=(_data->>'number')::int;
        IF v_registration='00000000-0000-0000-0000-000000000000'
          OR v_request='00000000-0000-0000-0000-000000000000'
          OR v_expected NOT BETWEEN 1 AND 100000
          OR v_position NOT IN ('Goalkeeper','Defender','Midfielder','Forward')
          OR v_number NOT BETWEEN 1 AND 99
        THEN RAISE EXCEPTION 'invalid_enrollment_details' USING ERRCODE='22023'; END IF;
        v_player:='ply_'||replace(v_request::text,'-','');

        -- This function serializes against session/grant revocation.
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy');

        -- A group is bound to one location, and that location to one city.
        -- Super Admin or a delegated location/city manager must retain write
        -- authority in the *same* transaction.
        SELECT ga.scope_id INTO v_scope
          FROM club.academy_group_location_assignments ga
          JOIN club.football_locations l ON l.id=ga.location_id
            AND l.city_id=ga.city_id AND l.is_active
          JOIN club.football_cities c ON c.id=l.city_id AND c.is_active
          WHERE ga.organization_id='lefteria_fc_academy'
            AND ga.group_id=v_group AND ga.city_id=v_city
            AND ga.location_id=v_location
          FOR SHARE OF ga,l,c;
        IF v_scope IS NULL OR NOT club.admin_football_allowed(
            v_actor,v_role,'lefteria_fc_academy',v_scope,v_location,true)
        THEN RAISE EXCEPTION 'enrollment_location_denied' USING ERRCODE='42501'; END IF;

        SELECT * INTO v_application
          FROM academy_intake.registrations a
          WHERE a.id=v_registration AND a.organization_id='lefteria_fc_academy'
            AND (v_role='super_admin' OR a.recorded_by=v_actor)
          FOR UPDATE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'enrollment_application_denied' USING ERRCODE='42501';
        END IF;

        -- Response-loss retry: only the same request-id/player/group is safe.
        -- Never bypass current role or location authorization on replay.
        IF v_application.onboarded_player_id IS NOT NULL THEN
          IF v_application.onboarded_player_id=v_player AND
             EXISTS(SELECT 1 FROM academy_intake.registration_placements rp
                JOIN club.players p ON p.id=rp.player_id
                  AND p.organization_id=rp.organization_id
                LEFT JOIN academy_intake.registration_sporting_baselines b
                  ON b.registration_id=rp.registration_id
                WHERE rp.registration_id=v_registration
                  AND rp.player_id=v_player
                  AND rp.city_id=v_city AND rp.location_id=v_location
                  AND rp.group_id=v_group
                  AND (CASE WHEN b.registration_id IS NULL THEN p.position ELSE b.position END)=v_position
                  AND (CASE WHEN b.registration_id IS NULL THEN p.number ELSE b.number END)=v_number)
          THEN RETURN jsonb_build_object(
            'id',v_registration,'player_id',v_player,
            'city_id',v_city,'location_id',v_location,'group_id',v_group,
            'status','approved','status_version',v_application.status_version);
          END IF;
          RAISE EXCEPTION 'enrollment_already_completed' USING ERRCODE='23505';
        END IF;
        IF v_application.status<>'under_review'
          OR v_application.status_version<>v_expected
        THEN RAISE EXCEPTION 'enrollment_status_changed' USING ERRCODE='23505'; END IF;

        -- Public consent evidence only. A manually entered name/phone is
        -- not evidence that the guardian accepted medical/privacy policies.
        IF v_application.recorded_by IS NOT NULL
          OR NOT EXISTS(SELECT 1
            FROM academy_intake.registration_sealed_evidence e
            WHERE e.registration_id=v_registration)
          OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_consents c
            WHERE c.registration_id=v_registration
              AND c.consent_kind='media')
          OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_consents c
            WHERE c.registration_id=v_registration
              AND c.consent_kind='communications')
        THEN RAISE EXCEPTION 'enrollment_evidence_required' USING ERRCODE='42501'; END IF;
        SELECT count(DISTINCT c.consent_kind) INTO v_required_count
        FROM academy_intake.registration_consents c
        WHERE c.registration_id=v_registration AND c.granted
          AND c.consent_kind IN ('participation','medical_authorization',
            'privacy_notice','liability','financial')
          AND c.revision=(SELECT max(latest.revision)
            FROM academy_intake.registration_consents latest
            WHERE latest.registration_id=c.registration_id
              AND latest.consent_kind=c.consent_kind);
        IF v_required_count<>5 THEN
          RAISE EXCEPTION 'enrollment_current_consent_missing' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
          RAISE EXCEPTION 'enrollment_roster_unavailable' USING ERRCODE='55000'; END IF;

        v_name:=btrim(v_application.applicant_first_name)||' '||
          btrim(v_application.applicant_last_name);
        -- Imported roster records do not reliably contain date of birth.
        -- Refuse ambiguous names for manual identity resolution, never merge
        -- or attach a second child based solely on a name or phone number.
        PERFORM pg_advisory_xact_lock(hashtextextended(
          'academy:enrol:'||lower(v_name),0));
        IF EXISTS(SELECT 1 FROM club.players p
            WHERE p.organization_id='lefteria_fc_academy'
              AND lower(btrim(p.name))=lower(v_name))
          OR EXISTS(SELECT 1 FROM academy_intake.registrations a
            WHERE a.id<>v_registration AND
              a.organization_id='lefteria_fc_academy'
              AND lower(btrim(a.applicant_first_name))=
                  lower(btrim(v_application.applicant_first_name))
              AND lower(btrim(a.applicant_last_name))=
                  lower(btrim(v_application.applicant_last_name))
              AND a.applicant_birth_date=v_application.applicant_birth_date
              AND a.onboarded_player_id IS NOT NULL)
        THEN RAISE EXCEPTION 'enrollment_identity_review_needed' USING ERRCODE='23505'; END IF;

        INSERT INTO club.players(
          id,organization_id,name,number,position,team_type,
          team_id,primary_academy_group_id,image_url,is_active,
          is_archived,is_transferred,has_explicit_picker_fields)
        VALUES(v_player,'lefteria_fc_academy',v_name,v_number,v_position,
          'Academy',NULL,v_group,NULL,true,false,false,true);
        INSERT INTO club.player_academy_groups(
          organization_id,player_id,academy_group_id)
        VALUES('lefteria_fc_academy',v_player,v_group);

        INSERT INTO academy_intake.registration_placements(
          registration_id,organization_id,city_id,location_id,scope_id,
          group_id,player_id,enrolled_by)
        VALUES(v_registration,'lefteria_fc_academy',v_city,v_location,
          v_scope,v_group,v_player,v_actor);

        UPDATE academy_intake.registrations
        SET status='approved',status_version=v_expected+1,
          decided_by=v_actor,decided_at=clock_timestamp(),
          onboarded_player_id=v_player
        WHERE id=v_registration AND organization_id='lefteria_fc_academy';
        INSERT INTO academy_intake.registration_review_events(
          id,registration_id,actor_id,previous_status,next_status,
          status_version,reason_code)
        VALUES(gen_random_uuid(),v_registration,v_actor,'under_review',
          'approved',v_expected+1,'roster_enrollment');
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          'admin.academy.registration.enroll','succeeded',
          'lefteria_fc_academy','verified_group');
        RETURN jsonb_build_object(
          'id',v_registration,'player_id',v_player,
          'city_id',v_city,'location_id',v_location,'group_id',v_group,
          'status','approved','status_version',v_expected+1);
      END $enrol$;

      CREATE FUNCTION club.write_admin_academy_player_sporting(
        _digest text,_player text,_expected_position text,_expected_number integer,
        _position text,_number integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $sporting$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_position text;v_number integer;
      BEGIN
        IF _position IS NULL OR _position NOT IN ('Goalkeeper','Defender','Midfielder','Forward')
          OR _number IS NULL OR _number NOT BETWEEN 1 AND 99
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
        THEN RAISE EXCEPTION 'player_sporting_invalid' USING ERRCODE='22023'; END IF;
        -- Same per-player edit lock as the canonical name editor.
        PERFORM pg_advisory_xact_lock(hashtextextended('academy:player:rename:'||_player,0));
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,true);
        SELECT p.position,p.number INTO STRICT v_position,v_number FROM club.players p
          WHERE p.id=_player AND p.organization_id='lefteria_fc_academy' FOR UPDATE;
        IF v_position IS DISTINCT FROM _expected_position OR v_number IS DISTINCT FROM _expected_number
          THEN RAISE EXCEPTION 'player_sporting_revision_conflict' USING ERRCODE='23505'; END IF;
        IF v_position IS DISTINCT FROM _position OR v_number IS DISTINCT FROM _number THEN
          -- A linked enrollment's original retry comparison must survive
          -- current roster edits. Never modify the immutable placement.
          INSERT INTO academy_intake.registration_sporting_baselines(registration_id,position,number)
            SELECT rp.registration_id,v_position,v_number
            FROM academy_intake.registration_placements rp
            WHERE rp.organization_id='lefteria_fc_academy' AND rp.player_id=_player
            ON CONFLICT(registration_id) DO NOTHING;
          UPDATE club.players SET position=_position,number=_number
            WHERE organization_id='lefteria_fc_academy' AND id=_player;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.academy.player.sporting.correct','succeeded','lefteria_fc_academy','canonical_sporting_only');
        END IF;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','player_id',_player,
          'official_position',_position,'official_number',_number,'updated',true);
      END $sporting$;
      REVOKE ALL ON FUNCTION club.write_admin_academy_player_sporting(text,text,text,integer,text,integer) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM academy_intake.registration_sporting_baselines) THEN
          RAISE EXCEPTION 'sporting_baseline_requires_forward_recovery';
        END IF;
      END $guard$;
      CREATE OR REPLACE FUNCTION identity.enroll_admin_academy_registration(
        _digest text,_data jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $enrol$
      DECLARE
        v_actor uuid;
        v_role text;
        v_registration uuid;
        v_request uuid;
        v_expected int;
        v_city uuid;
        v_location uuid;
        v_group text;
        v_scope uuid;
        v_position text;
        v_number int;
        v_player text;
        v_name text;
        v_application academy_intake.registrations%ROWTYPE;
        v_required_count int;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _data IS NULL OR jsonb_typeof(_data)<>'object'
          OR (SELECT count(*) FROM jsonb_object_keys(_data))<>8
          OR EXISTS(SELECT 1 FROM jsonb_object_keys(_data) AS k(key)
            WHERE k.key NOT IN ('id','version','request_id','city_id',
              'location_id','group_id','position','number'))
          OR _data->>'id' IS NULL OR _data->>'request_id' IS NULL
          OR _data->>'id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'request_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'city_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'location_id' !~* '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'
          OR _data->>'group_id' !~ '^[A-Za-z0-9_-]{1,128}$'
          OR _data->>'version' !~ '^[0-9]{1,6}$'
          OR _data->>'number' !~ '^[0-9]{1,3}$'
        THEN RAISE EXCEPTION 'invalid_enrollment_request' USING ERRCODE='22023'; END IF;
        v_registration:=(_data->>'id')::uuid;
        v_request:=(_data->>'request_id')::uuid;
        v_city:=(_data->>'city_id')::uuid;
        v_location:=(_data->>'location_id')::uuid;
        v_group:=_data->>'group_id';
        v_expected:=(_data->>'version')::int;
        v_position:=_data->>'position';
        v_number:=(_data->>'number')::int;
        IF v_registration='00000000-0000-0000-0000-000000000000'
          OR v_request='00000000-0000-0000-0000-000000000000'
          OR v_expected NOT BETWEEN 1 AND 100000
          OR v_position NOT IN ('Goalkeeper','Defender','Midfielder','Forward')
          OR v_number NOT BETWEEN 1 AND 99
        THEN RAISE EXCEPTION 'invalid_enrollment_details' USING ERRCODE='22023'; END IF;
        v_player:='ply_'||replace(v_request::text,'-','');

        -- This function serializes against session/grant revocation.
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy');

        -- A group is bound to one location, and that location to one city.
        -- Super Admin or a delegated location/city manager must retain write
        -- authority in the *same* transaction.
        SELECT ga.scope_id INTO v_scope
          FROM club.academy_group_location_assignments ga
          JOIN club.football_locations l ON l.id=ga.location_id
            AND l.city_id=ga.city_id AND l.is_active
          JOIN club.football_cities c ON c.id=l.city_id AND c.is_active
          WHERE ga.organization_id='lefteria_fc_academy'
            AND ga.group_id=v_group AND ga.city_id=v_city
            AND ga.location_id=v_location
          FOR SHARE OF ga,l,c;
        IF v_scope IS NULL OR NOT club.admin_football_allowed(
            v_actor,v_role,'lefteria_fc_academy',v_scope,v_location,true)
        THEN RAISE EXCEPTION 'enrollment_location_denied' USING ERRCODE='42501'; END IF;

        SELECT * INTO v_application
          FROM academy_intake.registrations a
          WHERE a.id=v_registration AND a.organization_id='lefteria_fc_academy'
            AND (v_role='super_admin' OR a.recorded_by=v_actor)
          FOR UPDATE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'enrollment_application_denied' USING ERRCODE='42501';
        END IF;

        -- Response-loss retry: only the same request-id/player/group is safe.
        -- Never bypass current role or location authorization on replay.
        IF v_application.onboarded_player_id IS NOT NULL THEN
          IF v_application.onboarded_player_id=v_player AND
             EXISTS(SELECT 1 FROM academy_intake.registration_placements rp
                JOIN club.players p ON p.id=rp.player_id
                  AND p.organization_id=rp.organization_id
                WHERE rp.registration_id=v_registration
                  AND rp.player_id=v_player
                  AND rp.city_id=v_city AND rp.location_id=v_location
                  AND rp.group_id=v_group AND p.position=v_position
                  AND p.number=v_number)
          THEN RETURN jsonb_build_object(
            'id',v_registration,'player_id',v_player,
            'city_id',v_city,'location_id',v_location,'group_id',v_group,
            'status','approved','status_version',v_application.status_version);
          END IF;
          RAISE EXCEPTION 'enrollment_already_completed' USING ERRCODE='23505';
        END IF;
        IF v_application.status<>'under_review'
          OR v_application.status_version<>v_expected
        THEN RAISE EXCEPTION 'enrollment_status_changed' USING ERRCODE='23505'; END IF;

        -- Public consent evidence only. A manually entered name/phone is
        -- not evidence that the guardian accepted medical/privacy policies.
        IF v_application.recorded_by IS NOT NULL
          OR NOT EXISTS(SELECT 1
            FROM academy_intake.registration_sealed_evidence e
            WHERE e.registration_id=v_registration)
          OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_consents c
            WHERE c.registration_id=v_registration
              AND c.consent_kind='media')
          OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_consents c
            WHERE c.registration_id=v_registration
              AND c.consent_kind='communications')
        THEN RAISE EXCEPTION 'enrollment_evidence_required' USING ERRCODE='42501'; END IF;
        SELECT count(DISTINCT c.consent_kind) INTO v_required_count
        FROM academy_intake.registration_consents c
        WHERE c.registration_id=v_registration AND c.granted
          AND c.consent_kind IN ('participation','medical_authorization',
            'privacy_notice','liability','financial')
          AND c.revision=(SELECT max(latest.revision)
            FROM academy_intake.registration_consents latest
            WHERE latest.registration_id=c.registration_id
              AND latest.consent_kind=c.consent_kind);
        IF v_required_count<>5 THEN
          RAISE EXCEPTION 'enrollment_current_consent_missing' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
          RAISE EXCEPTION 'enrollment_roster_unavailable' USING ERRCODE='55000'; END IF;

        v_name:=btrim(v_application.applicant_first_name)||' '||
          btrim(v_application.applicant_last_name);
        -- Imported roster records do not reliably contain date of birth.
        -- Refuse ambiguous names for manual identity resolution, never merge
        -- or attach a second child based solely on a name or phone number.
        PERFORM pg_advisory_xact_lock(hashtextextended(
          'academy:enrol:'||lower(v_name),0));
        IF EXISTS(SELECT 1 FROM club.players p
            WHERE p.organization_id='lefteria_fc_academy'
              AND lower(btrim(p.name))=lower(v_name))
          OR EXISTS(SELECT 1 FROM academy_intake.registrations a
            WHERE a.id<>v_registration AND
              a.organization_id='lefteria_fc_academy'
              AND lower(btrim(a.applicant_first_name))=
                  lower(btrim(v_application.applicant_first_name))
              AND lower(btrim(a.applicant_last_name))=
                  lower(btrim(v_application.applicant_last_name))
              AND a.applicant_birth_date=v_application.applicant_birth_date
              AND a.onboarded_player_id IS NOT NULL)
        THEN RAISE EXCEPTION 'enrollment_identity_review_needed' USING ERRCODE='23505'; END IF;

        INSERT INTO club.players(
          id,organization_id,name,number,position,team_type,
          team_id,primary_academy_group_id,image_url,is_active,
          is_archived,is_transferred,has_explicit_picker_fields)
        VALUES(v_player,'lefteria_fc_academy',v_name,v_number,v_position,
          'Academy',NULL,v_group,NULL,true,false,false,true);
        INSERT INTO club.player_academy_groups(
          organization_id,player_id,academy_group_id)
        VALUES('lefteria_fc_academy',v_player,v_group);

        INSERT INTO academy_intake.registration_placements(
          registration_id,organization_id,city_id,location_id,scope_id,
          group_id,player_id,enrolled_by)
        VALUES(v_registration,'lefteria_fc_academy',v_city,v_location,
          v_scope,v_group,v_player,v_actor);

        UPDATE academy_intake.registrations
        SET status='approved',status_version=v_expected+1,
          decided_by=v_actor,decided_at=clock_timestamp(),
          onboarded_player_id=v_player
        WHERE id=v_registration AND organization_id='lefteria_fc_academy';
        INSERT INTO academy_intake.registration_review_events(
          id,registration_id,actor_id,previous_status,next_status,
          status_version,reason_code)
        VALUES(gen_random_uuid(),v_registration,v_actor,'under_review',
          'approved',v_expected+1,'roster_enrollment');
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          'admin.academy.registration.enroll','succeeded',
          'lefteria_fc_academy','verified_group');
        RETURN jsonb_build_object(
          'id',v_registration,'player_id',v_player,
          'city_id',v_city,'location_id',v_location,'group_id',v_group,
          'status','approved','status_version',v_expected+1);
      END $enrol$;
      DROP FUNCTION club.write_admin_academy_player_sporting(text,text,text,integer,text,integer);
      DROP TABLE academy_intake.registration_sporting_baselines;
      """);
}
