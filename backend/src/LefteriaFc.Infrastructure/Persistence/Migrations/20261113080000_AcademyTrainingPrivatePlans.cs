using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-owned, versioned PRIVATE training plans. Never mutates imported
/// calendar items, RSVP, attendance, or member-facing publication.
/// Operator grants are a separate explicit staging-only action.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113080000_AcademyTrainingPrivatePlans")]
public sealed class AcademyTrainingPrivatePlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_training_plan_drafts (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          CHECK(organization_id='lefteria_fc_academy'),
        group_id varchar(128) COLLATE "C" NOT NULL,
        training_day date NOT NULL,
        start_time time without time zone NOT NULL,
        duration_minutes integer NOT NULL CHECK(duration_minutes BETWEEN 15 AND 240),
        title varchar(180) NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 180 AND title=btrim(title) AND title !~ '[[:cntrl:]]'),
        location varchar(200) NOT NULL DEFAULT '' CHECK(length(location)<=200 AND location !~ '[[:cntrl:]]'),
        notes varchar(2000) NOT NULL DEFAULT '' CHECK(length(notes)<=2000 AND notes !~ '[[:cntrl:]]'),
        status varchar(16) NOT NULL DEFAULT 'draft' CHECK(status IN ('draft','archived')),
        version integer NOT NULL DEFAULT 1 CHECK(version BETWEEN 1 AND 10000),
        created_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY (organization_id,group_id)
          REFERENCES club.academy_groups(organization_id,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_academy_training_plan_scope ON
        club.academy_training_plan_drafts(organization_id,group_id,training_day,id);
      CREATE TABLE club.academy_training_plan_events (
        id uuid PRIMARY KEY,
        plan_id uuid NOT NULL REFERENCES club.academy_training_plan_drafts(id) ON DELETE RESTRICT,
        version integer NOT NULL CHECK(version BETWEEN 1 AND 10000),
        action varchar(16) NOT NULL CHECK(action IN ('created','updated','archived')),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
        UNIQUE(plan_id,version)
      );
      REVOKE ALL ON club.academy_training_plan_drafts,
        club.academy_training_plan_events FROM PUBLIC;
      ALTER TABLE club.academy_training_plan_drafts ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.academy_training_plan_events ENABLE ROW LEVEL SECURITY;

      -- Lock the live admin session, owner and current grant before touching
      -- drafts. No direct login or read grant to public/mobile app roles.
      CREATE FUNCTION club.require_academy_training_planner(_digest text,_owner text)
      RETURNS TABLE(actor_id uuid,session_id uuid)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_authority$
      DECLARE v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner IS DISTINCT FROM 'lefteria_fc_academy' THEN
          RAISE EXCEPTION 'training_plan_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'training_plan_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO actor_id,session_id,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF actor_id IS NULL OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'training_plan_manager_denied' USING ERRCODE='42501';
        END IF;
        RETURN NEXT;
      END $plan_authority$;

      CREATE FUNCTION club.read_admin_academy_training_plans(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_read$
      DECLARE v_rows jsonb;v_total integer;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_plan_filters_invalid' USING ERRCODE='22023';
        END IF;
        PERFORM club.require_academy_training_planner(_digest,_owner);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_group_denied' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        IF v_total>500 THEN
          RAISE EXCEPTION 'training_plan_list_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',p.id,'organization_id',p.organization_id,
          'group_id',p.group_id,'day',p.training_day,
          'start_time',to_char(p.start_time,'HH24:MI'),
          'duration_minutes',p.duration_minutes,'title',p.title,
          'location',p.location,'notes',p.notes,
          'status',p.status,'version',p.version)
          ORDER BY p.training_day,p.start_time,p.id),'[]'::jsonb)
        INTO v_rows FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'total',v_total,'plans',v_rows);
      END $plan_read$;

      CREATE FUNCTION club.write_admin_academy_training_plan(
        _digest text,_owner text,_id uuid,_group text,_day date,_start time,
        _duration integer,_title text,_location text,_notes text,
        _expected integer,_archive boolean
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_write$
      DECLARE
        v_actor uuid;v_session uuid;v_version integer;v_status text;
        v_current club.academy_training_plan_drafts%ROWTYPE;
        v_action text;v_snapshot jsonb;
      BEGIN
        IF _id IS NULL OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _day IS NULL
          OR _start IS NULL OR _duration IS NULL
          OR _duration NOT BETWEEN 15 AND 240
          OR _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180
          OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
          OR _location IS NULL OR length(_location)>200
          OR _location ~ '[[:cntrl:]]'
          OR _notes IS NULL OR length(_notes)>2000
          OR _notes ~ '[[:cntrl:]]'
          OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR _archive IS NULL OR (_expected=0 AND _archive) THEN
          RAISE EXCEPTION 'training_plan_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session FROM
          club.require_academy_training_planner(_digest,_owner);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_group_denied' USING ERRCODE='42501';
        END IF;
        IF _expected=0 THEN
          IF _archive THEN RAISE EXCEPTION 'cannot_archive_uncreated_plan'
            USING ERRCODE='22023'; END IF;
          INSERT INTO club.academy_training_plan_drafts(
            id,organization_id,group_id,training_day,start_time,
            duration_minutes,title,location,notes,created_by,updated_by)
          VALUES(_id,_owner,_group,_day,_start,_duration,_title,
            _location,_notes,v_actor,v_actor);
          v_version:=1;v_status:='draft';v_action:='created';
        ELSE
          SELECT * INTO v_current FROM club.academy_training_plan_drafts
            WHERE id=_id AND organization_id=_owner FOR UPDATE;
          IF NOT FOUND OR v_current.group_id<>_group OR
            v_current.version<>_expected OR v_current.status<>'draft' THEN
            RAISE EXCEPTION 'training_plan_version_conflict' USING ERRCODE='23505';
          END IF;
          v_status:=CASE WHEN _archive THEN 'archived' ELSE 'draft' END;
          v_action:=CASE WHEN _archive THEN 'archived' ELSE 'updated' END;
          UPDATE club.academy_training_plan_drafts SET
            training_day=_day,start_time=_start,duration_minutes=_duration,
            title=_title,location=_location,notes=_notes,status=v_status,
            version=version+1,updated_by=v_actor,updated_at=clock_timestamp()
          WHERE id=_id AND version=_expected RETURNING version INTO v_version;
        END IF;
        SELECT jsonb_build_object('id',p.id,'organization_id',p.organization_id,
          'group_id',p.group_id,'day',p.training_day,
          'start_time',to_char(p.start_time,'HH24:MI'),
          'duration_minutes',p.duration_minutes,'title',p.title,
          'location',p.location,'notes',p.notes,'status',p.status,
          'version',p.version) INTO v_snapshot
        FROM club.academy_training_plan_drafts p WHERE p.id=_id;
        INSERT INTO club.academy_training_plan_events(
          id,plan_id,version,action,actor_id,snapshot)
        VALUES(gen_random_uuid(),_id,v_version,v_action,v_actor,v_snapshot);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          device_session_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          v_session,'admin.training.plan.'||v_action,'succeeded',
          _owner,'private_unpublished_plan');
        RETURN v_snapshot;
      END $plan_write$;

      REVOKE ALL ON FUNCTION club.require_academy_training_planner(text,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_admin_academy_training_plans(
        text,text,text,date,date) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.write_admin_academy_training_plan(
        text,text,uuid,text,date,time,integer,text,text,text,integer,boolean) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $protect$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_events LIMIT 1)
          OR EXISTS(SELECT 1 FROM club.academy_training_plan_drafts LIMIT 1) THEN
          RAISE EXCEPTION 'training_plan_history_exists';
        END IF;
      END $protect$;
      DROP FUNCTION club.write_admin_academy_training_plan(
        text,text,uuid,text,date,time,integer,text,text,text,integer,boolean);
      DROP FUNCTION club.read_admin_academy_training_plans(
        text,text,text,date,date);
      DROP FUNCTION club.require_academy_training_planner(text,text);
      DROP TABLE club.academy_training_plan_events;
      DROP TABLE club.academy_training_plan_drafts;
      """);
}
