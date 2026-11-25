using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125094000_AcademyTrainingAttendanceExactTerritory")]
public sealed class AcademyTrainingAttendanceExactTerritory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Event attendance exposes child names, so all explicit host groups
      -- must be within the actor's current active territory. An event that
      -- lacks a verified group assignment is not a manager attendance surface.
      CREATE FUNCTION club.admin_training_event_scope_allowed(_actor uuid,_owner text,_event text)
      RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $event_scope$
      DECLARE v_group text;v_groups integer:=0;
      BEGIN
        IF _owner IS DISTINCT FROM 'lefteria_fc_academy' THEN RETURN false; END IF;
        PERFORM 1 FROM club.calendar_items c WHERE c.organization_id=_owner AND c.kind='training'
          AND c.id=_event FOR SHARE;
        IF NOT FOUND THEN RETURN false; END IF;
        IF EXISTS(SELECT 1 FROM club.calendar_item_scopes s WHERE s.organization_id=_owner
          AND s.kind='training' AND s.event_id=_event AND s.scope_kind<>'group')
        THEN RETURN false; END IF;
        FOR v_group IN SELECT s.academy_group_id FROM club.calendar_item_scopes s
          WHERE s.organization_id=_owner AND s.kind='training' AND s.event_id=_event
            AND s.scope_kind='group' ORDER BY s.academy_group_id FOR SHARE
        LOOP
          PERFORM club.require_training_drill_group(_actor,v_group);
          v_groups:=v_groups+1;
        END LOOP;
        RETURN v_groups>0;
      EXCEPTION WHEN insufficient_privilege THEN RETURN false;
      END $event_scope$;
      REVOKE ALL ON FUNCTION club.admin_training_event_scope_allowed(uuid,text,text) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_sessions(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_training_list$
      DECLARE v_role text;v_actor uuid; v_rows jsonb; v_count integer;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
           OR _group ~ '[[:cntrl:]]'
           OR _from IS NULL OR _to IS NULL
           OR _from>_to OR _to>_from+93 THEN
          RAISE EXCEPTION 'academy_training_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role,a.id INTO v_role,v_actor FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'academy_training_access_denied' USING ERRCODE='42501';
        END IF;
        PERFORM club.require_training_drill_group(v_actor,_group);
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'academy_training_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'academy_training_group_not_found' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM club.calendar_items c
          WHERE c.organization_id=_owner AND c.kind='training'
          AND c.staff_day BETWEEN _from AND _to
          AND club.admin_training_event_scope_allowed(v_actor,_owner,c.id)
          AND EXISTS (SELECT 1 FROM club.calendar_item_scopes x
            WHERE x.organization_id=c.organization_id AND x.kind=c.kind
              AND x.event_id=c.id AND x.scope_kind='group'
              AND x.academy_group_id=_group);
        IF v_count>500 THEN
          RAISE EXCEPTION 'academy_training_many_events' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',c.id,'day',c.staff_day,'title',
            coalesce(nullif(c.staff_projection_json->>'title',''),c.id),
            'is_cancelled',c.is_cancelled,'is_archived',c.is_archived)
          ORDER BY c.staff_day DESC,c.id COLLATE "C"),'[]'::jsonb)
        INTO v_rows FROM club.calendar_items c
        WHERE c.organization_id=_owner AND c.kind='training'
          AND c.staff_day BETWEEN _from AND _to
          AND club.admin_training_event_scope_allowed(v_actor,_owner,c.id)
          AND EXISTS (SELECT 1 FROM club.calendar_item_scopes x
            WHERE x.organization_id=c.organization_id AND x.kind=c.kind
              AND x.event_id=c.id AND x.scope_kind='group'
              AND x.academy_group_id=_group);
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_count,'sessions',v_rows);
      END $academy_training_list$;
      CREATE OR REPLACE FUNCTION club.read_admin_academy_training_attendance(
        _digest text,_owner text,_event text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_attendance_read$
      DECLARE
        v_role text;v_actor uuid; v_date date; v_title text;
        v_cancelled boolean; v_archived boolean;
        v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _event IS NULL OR length(_event) NOT BETWEEN 1 AND 128
           OR _event ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'attendance_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role,a.id INTO v_role,v_actor FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'attendance_access_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'attendance_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT club.admin_training_event_scope_allowed(v_actor,_owner,_event) THEN
          RAISE EXCEPTION 'attendance_event_scope_denied' USING ERRCODE='42501'; END IF;
        SELECT c.staff_day,
          coalesce(nullif(c.staff_projection_json->>'title',''),c.id),
          c.is_cancelled,c.is_archived
        INTO v_date,v_title,v_cancelled,v_archived
        FROM club.calendar_items c
        WHERE c.organization_id=_owner AND c.kind='training' AND c.id=_event;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'attendance_event_not_found' USING ERRCODE='42501';
        END IF;

        SELECT count(*) INTO v_count FROM club.players p
          WHERE p.organization_id=_owner AND
            club.academy_training_player_eligible(_owner,_event,p.id);
        IF v_count>200 THEN
          RAISE EXCEPTION 'attendance_roster_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'player_id',p.id,'name',p.name,
          'rsvp',rsvp.status,
          'historical_attendance',hist.status,
          'attendance',coalesce(m.status,hist.status),
          'attendance_source',CASE WHEN m.version IS NOT NULL
            THEN 'os_manager' WHEN hist.status IS NOT NULL
            THEN 'legacy_import' ELSE 'not_recorded' END,
          'version',coalesce(m.version,0))
          ORDER BY p.name COLLATE "C",p.id COLLATE "C"),'[]'::jsonb)
        INTO v_rows FROM club.players p
        LEFT JOIN club.academy_training_attendance m
          ON m.organization_id=_owner AND m.kind='training'
          AND m.event_id=_event AND m.player_id=p.id
        LEFT JOIN club.calendar_responses hist
          ON hist.organization_id=_owner AND hist.kind='training'
          AND hist.event_id=_event AND hist.player_id=p.id
          AND hist.collection='attendance'
        LEFT JOIN LATERAL (
          SELECT r.status FROM club.calendar_responses r
          WHERE r.organization_id=_owner AND r.kind='training'
            AND r.event_id=_event AND r.player_id=p.id
            AND r.collection IN ('availability','event_attendance')
          ORDER BY CASE WHEN r.collection='availability' THEN 0 ELSE 1 END
          LIMIT 1
        ) rsvp ON true
        WHERE p.organization_id=_owner AND
          club.academy_training_player_eligible(_owner,_event,p.id);

        RETURN jsonb_build_object('organization_id',_owner,
          'event_id',_event,'title',v_title,'day',v_date,
          'is_cancelled',v_cancelled,'is_archived',v_archived,
          'writable',NOT v_cancelled AND NOT v_archived AND
            v_date<=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date,
          'total',v_count,'players',v_rows);
      END $academy_attendance_read$;
      CREATE OR REPLACE FUNCTION club.record_admin_academy_training_attendance(
        _digest text,_owner text,_event text,_player text,
        _status text,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_attendance_write$
      DECLARE
        v_role text; v_actor uuid; v_session uuid;
        v_day date; v_cancelled boolean; v_archived boolean;
        v_player text; v_previous text; v_existing text; v_imported text;
        v_version integer; v_next integer; v_event uuid:=gen_random_uuid();
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _event IS NULL OR length(_event) NOT BETWEEN 1 AND 128
           OR _event ~ '[[:cntrl:]]'
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]'
           OR _status NOT IN ('present','absent') OR _status IS NULL
           OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
        THEN RAISE EXCEPTION 'attendance_write_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'attendance_authority_revoked' USING ERRCODE='42501';
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
          RAISE EXCEPTION 'attendance_write_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM club.calendar_import_state
          WHERE id='initial' AND is_complete) THEN
          RAISE EXCEPTION 'attendance_calendar_not_ready' USING ERRCODE='55000';
        END IF;
        IF NOT club.admin_training_event_scope_allowed(v_actor,_owner,_event) THEN
          RAISE EXCEPTION 'attendance_event_scope_denied' USING ERRCODE='42501'; END IF;
        SELECT staff_day,is_cancelled,is_archived
        INTO v_day,v_cancelled,v_archived FROM club.calendar_items
        WHERE organization_id=_owner AND kind='training' AND id=_event FOR SHARE;
        IF NOT FOUND OR v_cancelled OR v_archived OR
           v_day>(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'attendance_event_not_writable' USING ERRCODE='42501';
        END IF;
        -- Lock the player record to serialize simultaneous manager changes,
        -- and re-evaluate membership instead of trusting a UI-supplied name.
        SELECT p.id INTO v_player FROM club.players p
        WHERE p.organization_id=_owner AND p.id=_player
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        FOR UPDATE;
        IF NOT FOUND OR NOT club.academy_training_player_eligible(
          _owner,_event,_player) THEN
          RAISE EXCEPTION 'attendance_player_not_eligible' USING ERRCODE='42501';
        END IF;
        SELECT hist.status INTO v_imported FROM club.calendar_responses hist
        WHERE hist.organization_id=_owner AND hist.kind='training'
          AND hist.event_id=_event AND hist.player_id=_player
          AND hist.collection='attendance';
        SELECT m.status,m.version INTO v_existing,v_version
        FROM club.academy_training_attendance m
        WHERE m.organization_id=_owner AND m.kind='training'
          AND m.event_id=_event AND m.player_id=_player FOR UPDATE;
        IF FOUND THEN
          IF v_existing=_status THEN
            RETURN jsonb_build_object('event_id',_event,
              'player_id',_player,'status',v_existing,
              'version',v_version,'changed',false,'replay',true);
          END IF;
          IF v_version<>_expected THEN
            RAISE EXCEPTION 'attendance_stale_version' USING ERRCODE='23505';
          END IF;
          v_previous:=v_existing; v_next:=v_version+1;
          UPDATE club.academy_training_attendance
            SET status=_status,version=v_next,
                recorded_by=v_actor,recorded_at=clock_timestamp()
          WHERE organization_id=_owner AND kind='training'
            AND event_id=_event AND player_id=_player;
        ELSE
          IF _expected<>0 THEN
            RAISE EXCEPTION 'attendance_stale_version' USING ERRCODE='23505';
          END IF;
          IF v_imported=_status THEN
            RETURN jsonb_build_object('event_id',_event,
              'player_id',_player,'status',v_imported,
              'version',0,'changed',false,'replay',true);
          END IF;
          v_previous:=v_imported; v_next:=1;
          INSERT INTO club.academy_training_attendance(
            organization_id,kind,event_id,player_id,status,version,recorded_by)
          VALUES(_owner,'training',_event,_player,_status,v_next,v_actor);
        END IF;
        INSERT INTO club.academy_training_attendance_events(
          id,organization_id,kind,event_id,player_id,previous_status,
          status,version,actor_id)
        VALUES(v_event,_owner,'training',_event,_player,v_previous,
          _status,v_next,v_actor);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.attendance.record','succeeded',_owner,_status);
        RETURN jsonb_build_object('event_id',_event,
          'player_id',_player,'status',_status,'version',v_next,
          'changed',true,'replay',false,'audit_event_id',v_event);
      END $academy_attendance_write$;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Training attendance territory security requires forward recovery.");
}
