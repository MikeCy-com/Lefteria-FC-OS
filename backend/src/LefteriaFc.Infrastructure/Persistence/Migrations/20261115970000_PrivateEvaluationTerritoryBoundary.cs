using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Fail-closed private evaluation origin and geographic authority. Legacy
/// records remain Super Admin-only, and managers cannot read another site's
/// evaluation history even after a player changes location.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115970000_PrivateEvaluationTerritoryBoundary")]
public sealed class PrivateEvaluationTerritoryBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      -- Null historical provenance is intentional: legacy assessments stay
      -- visible to Super Admin only, never inferred from today's roster.
      ALTER TABLE academy_private.evaluations
        ADD COLUMN origin_scope_id uuid,
        ADD COLUMN origin_city_id uuid,
        ADD COLUMN origin_location_id uuid,
        ADD COLUMN origin_recorded_at timestamptz,
        ADD CONSTRAINT ck_evaluation_origin_complete CHECK (
          (origin_scope_id IS NULL AND origin_city_id IS NULL
            AND origin_location_id IS NULL AND origin_recorded_at IS NULL)
          OR (origin_scope_id IS NOT NULL AND origin_recorded_at IS NOT NULL
            AND ((organization_id='lefteria_fc' AND origin_city_id IS NULL
                    AND origin_location_id IS NULL)
              OR (organization_id='lefteria_fc_academy'
                    AND origin_city_id IS NOT NULL
                    AND origin_location_id IS NOT NULL)))
        ),
        ADD CONSTRAINT fk_evaluation_origin_scope
          FOREIGN KEY (organization_id,origin_scope_id)
            REFERENCES club.football_operational_scopes(organization_id,id)
            ON DELETE RESTRICT,
        ADD CONSTRAINT fk_evaluation_origin_city_scope
          FOREIGN KEY (origin_city_id,origin_scope_id)
            REFERENCES club.football_operational_scopes(academy_city_id,id)
            ON DELETE RESTRICT,
        ADD CONSTRAINT fk_evaluation_origin_city_location
          FOREIGN KEY (origin_city_id,origin_location_id)
            REFERENCES club.football_locations(city_id,id) ON DELETE RESTRICT;
      CREATE INDEX ix_evaluation_origin_territory
        ON academy_private.evaluations
          (organization_id,player_id,origin_scope_id,origin_location_id)
        WHERE origin_scope_id IS NOT NULL;

      -- Single source of truth for BOTH manager write authorization and the
      -- creation-time territory capture. No player can be resolved by a
      -- guessed address, imported label or a subset of memberships.
      CREATE FUNCTION academy_private.resolve_private_player_territory(
        _owner text,_player text
      ) RETURNS TABLE(scope_id uuid,city_id uuid,location_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $resolve_private_territory$
      DECLARE v_total bigint;v_mapped bigint;v_scopes bigint;
        v_cities bigint;v_locations bigint;
        v_scope uuid;v_city uuid;v_location uuid;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR NOT EXISTS (SELECT 1 FROM club.players p
            WHERE p.organization_id=_owner AND p.id=_player
              AND p.team_type=CASE WHEN _owner='lefteria_fc'
                THEN 'First Team' ELSE 'Academy' END
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred)
        THEN RETURN; END IF;
        IF _owner='lefteria_fc' THEN
          SELECT count(*),min(s.id::text)::uuid
          INTO v_total,v_scope
          FROM club.players p
          JOIN club.football_operational_scopes s
            ON s.team_id=p.team_id AND s.organization_id=_owner
          WHERE p.organization_id=_owner AND p.id=_player;
          IF v_total=1 THEN RETURN QUERY SELECT v_scope,NULL::uuid,NULL::uuid; END IF;
          RETURN;
        END IF;
        WITH memberships AS (
          SELECT m.academy_group_id group_id
          FROM club.player_academy_groups m
          WHERE m.organization_id=_owner AND m.player_id=_player
          UNION
          SELECT p.primary_academy_group_id
          FROM club.players p
          WHERE p.organization_id=_owner AND p.id=_player
            AND p.primary_academy_group_id IS NOT NULL
        )
        SELECT count(*),
          count(*) FILTER (WHERE ag.id IS NOT NULL
            AND s.id IS NOT NULL AND c.is_active AND l.is_active),
          count(DISTINCT a.scope_id),count(DISTINCT a.city_id),
          count(DISTINCT a.location_id),min(a.scope_id::text)::uuid,
          min(a.city_id::text)::uuid,min(a.location_id::text)::uuid
        INTO v_total,v_mapped,v_scopes,v_cities,v_locations,
             v_scope,v_city,v_location
        FROM memberships m
        LEFT JOIN club.academy_groups ag
          ON ag.organization_id=_owner AND ag.id=m.group_id
        LEFT JOIN club.academy_group_location_assignments a
          ON a.organization_id=_owner AND a.group_id=m.group_id
        LEFT JOIN club.football_operational_scopes s
          ON s.id=a.scope_id AND s.organization_id=_owner
            AND s.academy_city_id=a.city_id
        LEFT JOIN club.football_cities c ON c.id=a.city_id
        LEFT JOIN club.football_locations l
          ON l.id=a.location_id AND l.city_id=a.city_id;
        IF v_total BETWEEN 1 AND 20 AND v_total=v_mapped
          AND v_scopes=1 AND v_cities=1 AND v_locations=1
        THEN RETURN QUERY SELECT v_scope,v_city,v_location; END IF;
      END $resolve_private_territory$;
      REVOKE ALL ON FUNCTION
        academy_private.resolve_private_player_territory(text,text) FROM PUBLIC;

      -- Called only inside authenticated SECURITY DEFINER evaluation routines
      -- and the immutable insert trigger. Never grant this helper to runtime.
      CREATE FUNCTION academy_private.authorize_private_player_manager(
        _actor uuid,_owner text,_player text
      ) RETURNS TABLE(scope_id uuid,city_id uuid,location_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $authorize_private_manager$
      DECLARE v_scope uuid;v_city uuid;v_location uuid;
      BEGIN
        IF _actor IS NULL OR NOT EXISTS(
          SELECT 1 FROM identity.admin_accounts a
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE a.id=_actor AND a.role='manager' AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL
        ) THEN RAISE EXCEPTION 'evaluation_manager_authority_denied'
          USING ERRCODE='42501'; END IF;
        SELECT t.scope_id,t.city_id,t.location_id
          INTO v_scope,v_city,v_location
        FROM academy_private.resolve_private_player_territory(_owner,_player) t;
        IF v_scope IS NULL THEN RAISE EXCEPTION 'evaluation_territory_unverified'
          USING ERRCODE='42501'; END IF;
        IF NOT (
          (_owner='lefteria_fc' AND EXISTS(
            SELECT 1 FROM club.football_team_admin_grants g
            WHERE g.admin_account_id=_actor AND g.scope_id=v_scope
              AND g.role='team_manager' FOR SHARE))
          OR (_owner='lefteria_fc_academy' AND (
            EXISTS(SELECT 1 FROM club.football_city_admin_grants g
              WHERE g.admin_account_id=_actor AND g.city_id=v_city
                AND g.role='city_manager' FOR SHARE)
            OR EXISTS(SELECT 1 FROM club.football_location_admin_grants g
              WHERE g.admin_account_id=_actor AND g.location_id=v_location
                AND g.role='location_manager' FOR SHARE)))
        ) THEN RAISE EXCEPTION 'evaluation_territory_access_denied'
          USING ERRCODE='42501'; END IF;
        RETURN QUERY SELECT v_scope,v_city,v_location;
      END $authorize_private_manager$;
      REVOKE ALL ON FUNCTION academy_private.authorize_private_player_manager(
        uuid,text,text) FROM PUBLIC;

      CREATE FUNCTION academy_private.capture_evaluation_origin()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $capture_eval$
      DECLARE v_role text;v_scope uuid;v_city uuid;v_location uuid;
      BEGIN
        IF TG_TABLE_SCHEMA<>'academy_private' OR TG_TABLE_NAME<>'evaluations'
        THEN RAISE EXCEPTION 'evaluation_origin_invalid_trigger'
          USING ERRCODE='42501'; END IF;
        IF TG_OP='UPDATE' THEN
          IF ROW(NEW.origin_scope_id,NEW.origin_city_id,
                 NEW.origin_location_id,NEW.origin_recorded_at)
            IS DISTINCT FROM ROW(OLD.origin_scope_id,OLD.origin_city_id,
                 OLD.origin_location_id,OLD.origin_recorded_at)
          THEN RAISE EXCEPTION 'evaluation_origin_immutable'
            USING ERRCODE='42501'; END IF;
          RETURN NEW;
        END IF;
        IF NEW.origin_scope_id IS NOT NULL OR NEW.origin_city_id IS NOT NULL
          OR NEW.origin_location_id IS NOT NULL
          OR NEW.origin_recorded_at IS NOT NULL
        THEN RAISE EXCEPTION 'evaluation_origin_server_only'
          USING ERRCODE='42501'; END IF;
        SELECT a.role INTO v_role FROM identity.admin_accounts a
          WHERE a.id=NEW.author_id;
        IF v_role='manager' THEN
          SELECT t.scope_id,t.city_id,t.location_id
            INTO v_scope,v_city,v_location
          FROM academy_private.authorize_private_player_manager(
            NEW.author_id,NEW.organization_id,NEW.player_id) t;
        ELSIF v_role='super_admin' THEN
          SELECT t.scope_id,t.city_id,t.location_id
            INTO v_scope,v_city,v_location
          FROM academy_private.resolve_private_player_territory(
            NEW.organization_id,NEW.player_id) t;
        ELSE
          RAISE EXCEPTION 'evaluation_origin_invalid_author' USING ERRCODE='42501';
        END IF;
        IF v_scope IS NOT NULL THEN
          NEW.origin_scope_id:=v_scope;
          NEW.origin_city_id:=v_city;
          NEW.origin_location_id:=v_location;
          NEW.origin_recorded_at:=clock_timestamp();
        END IF;
        RETURN NEW;
      END $capture_eval$;
      REVOKE ALL ON FUNCTION academy_private.capture_evaluation_origin() FROM PUBLIC;
      CREATE TRIGGER tr_evaluation_origin_capture
        BEFORE INSERT ON academy_private.evaluations
        FOR EACH ROW EXECUTE FUNCTION academy_private.capture_evaluation_origin();
      CREATE TRIGGER tr_evaluation_origin_immutable
        BEFORE UPDATE ON academy_private.evaluations
        FOR EACH ROW EXECUTE FUNCTION academy_private.capture_evaluation_origin();

      CREATE OR REPLACE FUNCTION academy_private.read_admin_player_evaluations(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_eval_read$
      DECLARE v_role text; v_actor uuid; v_count integer; v_rows jsonb;
        v_scope uuid; v_city uuid; v_location uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'evaluation_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_read_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            AND (
              (_owner='lefteria_fc' AND p.team_type='First Team'
                AND p.team_id IS NOT NULL
                AND EXISTS (SELECT 1 FROM club.teams t
                  WHERE t.organization_id=_owner AND t.id=p.team_id))
              OR
              (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                AND (
                  EXISTS (SELECT 1 FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                  OR EXISTS (SELECT 1 FROM club.player_academy_groups m
                    JOIN club.academy_groups g
                      ON g.organization_id=m.organization_id
                        AND g.id=m.academy_group_id
                    WHERE m.organization_id=_owner AND m.player_id=p.id)
                ))
            )
        ) THEN
          RAISE EXCEPTION 'evaluation_player_not_available' USING ERRCODE='42501';
        END IF;
        IF v_role='manager' THEN
          SELECT t.scope_id,t.city_id,t.location_id
          INTO v_scope,v_city,v_location
          FROM academy_private.authorize_private_player_manager(
            v_actor,_owner,_player) t;
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.evaluations e
          WHERE e.organization_id=_owner AND e.player_id=_player
            AND e.withdrawn_at IS NULL
          AND (v_role='super_admin' OR
              (e.origin_scope_id=v_scope AND
               (_owner='lefteria_fc' OR
                (e.origin_city_id=v_city AND e.origin_location_id=v_location))));
        IF v_count>200 THEN
          RAISE EXCEPTION 'evaluation_timeline_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',e.id,'created_at',e.created_at,
          'edit_until',e.edit_until,
          'revision',e.current_revision,
          'summary',r.summary,'notes',r.notes,
          'technical',r.technical,'tactical',r.tactical,
          'physical',r.physical,'character',r.character,
          'overall',r.overall)
          ORDER BY e.created_at DESC,e.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.evaluations e
        JOIN academy_private.evaluation_revisions r
          ON r.evaluation_id=e.id AND r.revision=e.current_revision
        WHERE e.organization_id=_owner AND e.player_id=_player
          AND e.withdrawn_at IS NULL
          AND (v_role='super_admin' OR
              (e.origin_scope_id=v_scope AND
               (_owner='lefteria_fc' OR
                (e.origin_city_id=v_city AND e.origin_location_id=v_location))));
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'evaluations',v_rows);
      END $academy_eval_read$;
      CREATE OR REPLACE FUNCTION academy_private.create_admin_player_evaluation(
        _digest text,_owner text,_player text,_request uuid,
        _summary text,_notes text,_technical jsonb,_tactical jsonb,
        _physical jsonb,_character jsonb,_overall jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $private_evaluation_create$
      DECLARE
        v_actor uuid; v_session uuid; v_role text;
        v_existing academy_private.evaluation_create_requests%ROWTYPE;
        v_id uuid:=gen_random_uuid();
        v_created timestamptz:=clock_timestamp();
        v_fingerprint char(32);
        v_affected integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
          OR _request IS NULL
          OR _summary IS NULL OR length(btrim(_summary)) NOT BETWEEN 1 AND 500
          OR _summary<>btrim(_summary)
          OR _notes IS NULL OR length(_notes)>4000
          OR _overall IS NULL OR jsonb_typeof(_overall)<>'object'
          OR pg_column_size(_overall)>4096
        THEN RAISE EXCEPTION 'evaluation_payload_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM academy_private.validate_academy_score_group(_technical,'technical');
        PERFORM academy_private.validate_academy_score_group(_tactical,'tactical');
        PERFORM academy_private.validate_academy_score_group(_physical,'physical');
        PERFORM academy_private.validate_academy_score_group(_character,'character');
        IF EXISTS(SELECT 1 FROM jsonb_each(_overall) kv
          WHERE kv.key NOT IN ('strengths','areas_to_improve','current_focus',
            'goal_3_month','goal_6_month','goal_12_month')
            OR jsonb_typeof(kv.value)<>'string'
            OR length(kv.value #>> '{}')>1000)
        THEN RAISE EXCEPTION 'evaluation_overall_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_writer_denied' USING ERRCODE='42501';
        END IF;
        PERFORM 1 FROM club.players p WHERE p.organization_id=_owner
          AND p.id=_player AND p.is_active
          AND NOT p.is_archived AND NOT p.is_transferred
          AND (
            (_owner='lefteria_fc' AND p.team_type='First Team'
              AND EXISTS(SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=p.team_id))
            OR (_owner='lefteria_fc_academy' AND p.team_type='Academy'
              AND (EXISTS(SELECT 1 FROM club.academy_groups g
                     WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                     JOIN club.academy_groups g
                       ON g.organization_id=m.organization_id AND g.id=m.academy_group_id
                     WHERE m.organization_id=_owner AND m.player_id=p.id)))
          ) FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'evaluation_player_not_assigned' USING ERRCODE='42501';
        END IF;
        IF v_role='manager' THEN
          PERFORM 1 FROM academy_private.authorize_private_player_manager(
            v_actor,_owner,_player);
        END IF;
        v_fingerprint:=md5(jsonb_build_array(_player,_summary,_notes,
          _technical,_tactical,_physical,_character,_overall)::text);
        INSERT INTO academy_private.evaluation_create_requests(
          organization_id,request_id,player_id,actor_id,evaluation_id,payload_fingerprint)
        VALUES(_owner,_request,_player,v_actor,v_id,v_fingerprint)
        ON CONFLICT DO NOTHING;
        GET DIAGNOSTICS v_affected=ROW_COUNT;
        IF v_affected=0 THEN
          SELECT * INTO v_existing FROM academy_private.evaluation_create_requests
            WHERE organization_id=_owner AND request_id=_request;
          IF NOT FOUND OR v_existing.actor_id<>v_actor
            OR v_existing.player_id<>_player
            OR v_existing.payload_fingerprint<>v_fingerprint THEN
            RAISE EXCEPTION 'evaluation_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_existing.evaluation_id,
            'player_id',_player,'organization_id',_owner,'replay',true);
        END IF;
        INSERT INTO academy_private.evaluations(
          id,organization_id,player_id,author_id,created_at,edit_until)
        VALUES(v_id,_owner,_player,v_actor,v_created,v_created+interval '24 hours');
        INSERT INTO academy_private.evaluation_revisions(
          evaluation_id,revision,editor_id,summary,notes,technical,tactical,
          physical,character,overall)
        VALUES(v_id,1,v_actor,_summary,_notes,_technical,_tactical,
          _physical,_character,_overall);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.player.evaluation.create','succeeded',_owner,'snapshot');
        RETURN jsonb_build_object('id',v_id,'player_id',_player,
          'organization_id',_owner,'revision',1,'replay',false);
      END $private_evaluation_create$;

      -- No additional runtime grants. Original reader/writer signatures
      -- remain stable; each now independently checks manager geography.
      REVOKE ALL ON FUNCTION academy_private.read_admin_player_evaluations(
        text,text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION academy_private.create_admin_player_evaluation(
        text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb) FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER tr_evaluation_origin_immutable ON academy_private.evaluations;
      DROP TRIGGER tr_evaluation_origin_capture ON academy_private.evaluations;
      DROP FUNCTION academy_private.capture_evaluation_origin();
      CREATE OR REPLACE FUNCTION academy_private.read_admin_player_evaluations(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_eval_read$
      DECLARE v_role text; v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'evaluation_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_read_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
            AND (
              (_owner='lefteria_fc' AND p.team_type='First Team'
                AND p.team_id IS NOT NULL
                AND EXISTS (SELECT 1 FROM club.teams t
                  WHERE t.organization_id=_owner AND t.id=p.team_id))
              OR
              (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                AND (
                  EXISTS (SELECT 1 FROM club.academy_groups g
                    WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                  OR EXISTS (SELECT 1 FROM club.player_academy_groups m
                    JOIN club.academy_groups g
                      ON g.organization_id=m.organization_id
                        AND g.id=m.academy_group_id
                    WHERE m.organization_id=_owner AND m.player_id=p.id)
                ))
            )
        ) THEN
          RAISE EXCEPTION 'evaluation_player_not_available' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.evaluations e
          WHERE e.organization_id=_owner AND e.player_id=_player
            AND e.withdrawn_at IS NULL;
        IF v_count>200 THEN
          RAISE EXCEPTION 'evaluation_timeline_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',e.id,'created_at',e.created_at,
          'edit_until',e.edit_until,
          'revision',e.current_revision,
          'summary',r.summary,'notes',r.notes,
          'technical',r.technical,'tactical',r.tactical,
          'physical',r.physical,'character',r.character,
          'overall',r.overall)
          ORDER BY e.created_at DESC,e.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.evaluations e
        JOIN academy_private.evaluation_revisions r
          ON r.evaluation_id=e.id AND r.revision=e.current_revision
        WHERE e.organization_id=_owner AND e.player_id=_player
          AND e.withdrawn_at IS NULL;
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'evaluations',v_rows);
      END $academy_eval_read$;
      CREATE OR REPLACE FUNCTION academy_private.create_admin_player_evaluation(
        _digest text,_owner text,_player text,_request uuid,
        _summary text,_notes text,_technical jsonb,_tactical jsonb,
        _physical jsonb,_character jsonb,_overall jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $private_evaluation_create$
      DECLARE
        v_actor uuid; v_session uuid; v_role text;
        v_existing academy_private.evaluation_create_requests%ROWTYPE;
        v_id uuid:=gen_random_uuid();
        v_created timestamptz:=clock_timestamp();
        v_fingerprint char(32);
        v_affected integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
          OR _request IS NULL
          OR _summary IS NULL OR length(btrim(_summary)) NOT BETWEEN 1 AND 500
          OR _summary<>btrim(_summary)
          OR _notes IS NULL OR length(_notes)>4000
          OR _overall IS NULL OR jsonb_typeof(_overall)<>'object'
          OR pg_column_size(_overall)>4096
        THEN RAISE EXCEPTION 'evaluation_payload_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM academy_private.validate_academy_score_group(_technical,'technical');
        PERFORM academy_private.validate_academy_score_group(_tactical,'tactical');
        PERFORM academy_private.validate_academy_score_group(_physical,'physical');
        PERFORM academy_private.validate_academy_score_group(_character,'character');
        IF EXISTS(SELECT 1 FROM jsonb_each(_overall) kv
          WHERE kv.key NOT IN ('strengths','areas_to_improve','current_focus',
            'goal_3_month','goal_6_month','goal_12_month')
            OR jsonb_typeof(kv.value)<>'string'
            OR length(kv.value #>> '{}')>1000)
        THEN RAISE EXCEPTION 'evaluation_overall_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_writer_denied' USING ERRCODE='42501';
        END IF;
        PERFORM 1 FROM club.players p WHERE p.organization_id=_owner
          AND p.id=_player AND p.is_active
          AND NOT p.is_archived AND NOT p.is_transferred
          AND (
            (_owner='lefteria_fc' AND p.team_type='First Team'
              AND EXISTS(SELECT 1 FROM club.teams t
                WHERE t.organization_id=_owner AND t.id=p.team_id))
            OR (_owner='lefteria_fc_academy' AND p.team_type='Academy'
              AND (EXISTS(SELECT 1 FROM club.academy_groups g
                     WHERE g.organization_id=_owner AND g.id=p.primary_academy_group_id)
                OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                     JOIN club.academy_groups g
                       ON g.organization_id=m.organization_id AND g.id=m.academy_group_id
                     WHERE m.organization_id=_owner AND m.player_id=p.id)))
          ) FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'evaluation_player_not_assigned' USING ERRCODE='42501';
        END IF;
        v_fingerprint:=md5(jsonb_build_array(_player,_summary,_notes,
          _technical,_tactical,_physical,_character,_overall)::text);
        INSERT INTO academy_private.evaluation_create_requests(
          organization_id,request_id,player_id,actor_id,evaluation_id,payload_fingerprint)
        VALUES(_owner,_request,_player,v_actor,v_id,v_fingerprint)
        ON CONFLICT DO NOTHING;
        GET DIAGNOSTICS v_affected=ROW_COUNT;
        IF v_affected=0 THEN
          SELECT * INTO v_existing FROM academy_private.evaluation_create_requests
            WHERE organization_id=_owner AND request_id=_request;
          IF NOT FOUND OR v_existing.actor_id<>v_actor
            OR v_existing.player_id<>_player
            OR v_existing.payload_fingerprint<>v_fingerprint THEN
            RAISE EXCEPTION 'evaluation_request_conflict' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_existing.evaluation_id,
            'player_id',_player,'organization_id',_owner,'replay',true);
        END IF;
        INSERT INTO academy_private.evaluations(
          id,organization_id,player_id,author_id,created_at,edit_until)
        VALUES(v_id,_owner,_player,v_actor,v_created,v_created+interval '24 hours');
        INSERT INTO academy_private.evaluation_revisions(
          evaluation_id,revision,editor_id,summary,notes,technical,tactical,
          physical,character,overall)
        VALUES(v_id,1,v_actor,_summary,_notes,_technical,_tactical,
          _physical,_character,_overall);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.player.evaluation.create','succeeded',_owner,'snapshot');
        RETURN jsonb_build_object('id',v_id,'player_id',_player,
          'organization_id',_owner,'revision',1,'replay',false);
      END $private_evaluation_create$;
      DROP FUNCTION academy_private.authorize_private_player_manager(uuid,text,text);
      DROP FUNCTION academy_private.resolve_private_player_territory(text,text);
      DROP INDEX academy_private.ix_evaluation_origin_territory;
      ALTER TABLE academy_private.evaluations
        DROP CONSTRAINT fk_evaluation_origin_city_location,
        DROP CONSTRAINT fk_evaluation_origin_city_scope,
        DROP CONSTRAINT fk_evaluation_origin_scope,
        DROP CONSTRAINT ck_evaluation_origin_complete,
        DROP COLUMN origin_recorded_at,
        DROP COLUMN origin_location_id,
        DROP COLUMN origin_city_id,
        DROP COLUMN origin_scope_id;
    """);
}
