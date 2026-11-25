using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125090000_CentralAcademyDrillLibrary")]
public sealed class CentralAcademyDrillLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- One central catalogue, not a city, location or group resource.
      CREATE TABLE club.academy_coaching_drills (
        id uuid PRIMARY KEY,
        organization_id text NOT NULL CHECK(organization_id='lefteria_fc_academy'),
        title text NOT NULL CHECK(length(title) BETWEEN 1 AND 180 AND title=btrim(title) AND title !~ '[[:cntrl:]]'),
        category text NOT NULL CHECK(category IN ('technical','tactical','physical','goalkeeping','warmup','game','recovery','other')),
        age_band text NOT NULL CHECK(age_band IN ('all','u6-u8','u9-u11','u12-u14','u15-u18','adult')),
        duration_minutes integer NOT NULL CHECK(duration_minutes BETWEEN 1 AND 120),
        objective text NOT NULL CHECK(length(objective) BETWEEN 1 AND 1000 AND objective=btrim(objective) AND objective !~ '[[:cntrl:]]'),
        instructions text NOT NULL CHECK(length(instructions) BETWEEN 1 AND 4000 AND instructions=btrim(instructions,E' \\n') AND replace(instructions,chr(10),'') !~ '[[:cntrl:]]'),
        status text NOT NULL CHECK(status IN ('draft','approved','archived')),
        version integer NOT NULL CHECK(version BETWEEN 1 AND 10000)
      );
      CREATE INDEX academy_drills_list ON club.academy_coaching_drills(organization_id,status,title,id);
      CREATE TABLE club.academy_coaching_drill_versions (
        drill_id uuid NOT NULL REFERENCES club.academy_coaching_drills(id) ON DELETE RESTRICT,
        version integer NOT NULL,
        action text NOT NULL CHECK(action IN ('create','edit','approve','archive','restore')),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
        PRIMARY KEY(drill_id,version)
      );
      ALTER TABLE club.academy_coaching_drills ENABLE ROW LEVEL SECURITY;
      ALTER TABLE club.academy_coaching_drill_versions ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_coaching_drills,club.academy_coaching_drill_versions FROM PUBLIC;

      CREATE FUNCTION club.require_central_academy_drills(_digest text,_write boolean)
      RETURNS TABLE(actor_id uuid,session_id uuid,actor_role text)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $authority$
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$' OR _write IS NULL THEN
          RAISE EXCEPTION 'drill_authority_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,'lefteria_fc_academy');
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'drill_authority_denied' USING ERRCODE='42501';
        END;
        SELECT a.id,s.id,a.role INTO actor_id,session_id,actor_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
          AND g.organization_id='lefteria_fc_academy'
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
          AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF actor_id IS NULL OR actor_role NOT IN ('super_admin','manager','coach')
          OR (_write AND actor_role<>'super_admin') THEN
          RAISE EXCEPTION 'drill_authority_denied' USING ERRCODE='42501';
        END IF;
        RETURN NEXT;
      END $authority$;

      CREATE FUNCTION club.central_academy_drill_view(d club.academy_coaching_drills)
      RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,pg_temp AS $view$
        SELECT jsonb_build_object('id',d.id,'organization_id',d.organization_id,'title',d.title,
          'category',d.category,'age_band',d.age_band,'duration_minutes',d.duration_minutes,
          'objective',d.objective,'instructions',d.instructions,'status',d.status,'version',d.version)
      $view$;

      CREATE FUNCTION club.read_central_academy_drills(_digest text,_search text,_category text,_age text,_status text,_offset integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_role text;v_rows jsonb;v_count integer;
      BEGIN
        SELECT actor_role INTO v_role FROM club.require_central_academy_drills(_digest,false);
        IF _search IS NULL OR length(_search)>100 OR _search ~ '[[:cntrl:]]'
          OR _category IS NULL OR _category NOT IN ('all','technical','tactical','physical','goalkeeping','warmup','game','recovery','other')
          OR _age IS NULL OR _age NOT IN ('any','all','u6-u8','u9-u11','u12-u14','u15-u18','adult')
          OR _status IS NULL OR _status NOT IN ('all','draft','approved','archived')
          OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
          RAISE EXCEPTION 'drill_filters_invalid' USING ERRCODE='22023';
        END IF;
        IF v_role<>'super_admin' AND _status<>'approved' THEN
          RAISE EXCEPTION 'drill_drafts_denied' USING ERRCODE='42501';
        END IF;
        WITH page AS (
          SELECT d AS drill,d.title,d.id FROM club.academy_coaching_drills d
          WHERE d.organization_id='lefteria_fc_academy'
            AND (_status='all' OR d.status=_status)
            AND (_category='all' OR d.category=_category)
            AND (_age='any' OR d.age_band=_age OR d.age_band='all')
            AND (_search='' OR strpos(lower(d.title),lower(_search))>0 OR strpos(lower(d.objective),lower(_search))>0)
          ORDER BY d.title COLLATE "C",d.id LIMIT 31 OFFSET _offset
        ), numbered AS (
          SELECT drill,row_number() OVER (ORDER BY title COLLATE "C",id) AS rn FROM page
        )
        SELECT coalesce(jsonb_agg(club.central_academy_drill_view(drill) ORDER BY rn)
          FILTER (WHERE rn<=30),'[]'::jsonb),count(*)::integer INTO v_rows,v_count FROM numbered;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','drills',v_rows,
          'next_offset',CASE WHEN v_count=31 AND _offset<=99970 THEN _offset+30 ELSE NULL END);
      END $read$;

      CREATE FUNCTION club.write_central_academy_drill(_digest text,_id uuid,_expected integer,_action text,
        _title text,_category text,_age text,_duration integer,_objective text,_instructions text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $write$
      DECLARE v_actor uuid;v_session uuid;v_row club.academy_coaching_drills%ROWTYPE;v_snapshot jsonb;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_central_academy_drills(_digest,true);
        IF _id IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR _action IS NULL OR _action NOT IN ('create','edit','approve','archive','restore')
          OR (_action='create') IS DISTINCT FROM (_expected=0) THEN
          RAISE EXCEPTION 'drill_command_invalid' USING ERRCODE='22023';
        END IF;
        IF _action IN ('create','edit') THEN
          IF _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180 OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _category IS NULL OR _category NOT IN ('technical','tactical','physical','goalkeeping','warmup','game','recovery','other')
            OR _age IS NULL OR _age NOT IN ('all','u6-u8','u9-u11','u12-u14','u15-u18','adult')
            OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 120
            OR _objective IS NULL OR length(_objective) NOT BETWEEN 1 AND 1000 OR _objective<>btrim(_objective) OR _objective ~ '[[:cntrl:]]'
            OR _instructions IS NULL OR length(_instructions) NOT BETWEEN 1 AND 4000 OR _instructions<>btrim(_instructions,E' \\n') OR replace(_instructions,chr(10),'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'drill_content_invalid' USING ERRCODE='22023'; END IF;
        ELSE
          IF _title IS NOT NULL OR _category IS NOT NULL OR _age IS NOT NULL OR _duration IS NOT NULL
            OR _objective IS NOT NULL OR _instructions IS NOT NULL
          THEN RAISE EXCEPTION 'drill_lifecycle_content_invalid' USING ERRCODE='22023'; END IF;
        END IF;
        IF _action='create' THEN
          INSERT INTO club.academy_coaching_drills(id,organization_id,title,category,age_band,duration_minutes,objective,instructions,status,version)
          VALUES(_id,'lefteria_fc_academy',_title,_category,_age,_duration,_objective,_instructions,'draft',1)
          RETURNING * INTO v_row;
        ELSE
          SELECT * INTO v_row FROM club.academy_coaching_drills
            WHERE id=_id AND organization_id='lefteria_fc_academy' FOR UPDATE;
          IF NOT FOUND OR v_row.version<>_expected OR
            (_action IN ('edit','archive') AND v_row.status NOT IN ('draft','approved')) OR
            (_action='approve' AND v_row.status<>'draft') OR
            (_action='restore' AND v_row.status<>'archived')
          THEN RAISE EXCEPTION 'drill_version_conflict' USING ERRCODE='23505'; END IF;
          UPDATE club.academy_coaching_drills SET
            title=CASE WHEN _action='edit' THEN _title ELSE title END,
            category=CASE WHEN _action='edit' THEN _category ELSE category END,
            age_band=CASE WHEN _action='edit' THEN _age ELSE age_band END,
            duration_minutes=CASE WHEN _action='edit' THEN _duration ELSE duration_minutes END,
            objective=CASE WHEN _action='edit' THEN _objective ELSE objective END,
            instructions=CASE WHEN _action='edit' THEN _instructions ELSE instructions END,
            status=CASE _action WHEN 'approve' THEN 'approved' WHEN 'archive' THEN 'archived' ELSE 'draft' END,
            version=version+1 WHERE id=_id AND version=_expected RETURNING * INTO v_row;
        END IF;
        v_snapshot:=club.central_academy_drill_view(v_row);
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(_id,v_row.version,_action,v_actor,v_snapshot);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.drill.'||_action,'succeeded','lefteria_fc_academy','central_coaching_library');
        RETURN v_snapshot;
      END $write$;

      REVOKE ALL ON FUNCTION club.require_central_academy_drills(text,boolean) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.central_academy_drill_view(club.academy_coaching_drills) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_central_academy_drills(text,text,text,text,text,integer) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $protect$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_coaching_drills) OR EXISTS(SELECT 1 FROM club.academy_coaching_drill_versions)
          THEN RAISE EXCEPTION 'central_drill_history_requires_forward_recovery'; END IF;
      END $protect$;
      DROP FUNCTION club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text);
      DROP FUNCTION club.read_central_academy_drills(text,text,text,text,text,integer);
      DROP FUNCTION club.central_academy_drill_view(club.academy_coaching_drills);
      DROP FUNCTION club.require_central_academy_drills(text,boolean);
      DROP TABLE club.academy_coaching_drill_versions;
      DROP TABLE club.academy_coaching_drills;
      """);
}
