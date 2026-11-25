using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace LefteriaFc.Infrastructure.Persistence.Migrations;
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261125121000_AcademyDrillVariantsAndRevisions")]
public sealed class AcademyDrillVariantsAndRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE club.academy_coaching_drills ADD COLUMN difficulty text
        CHECK(difficulty IN ('beginner','intermediate','advanced'));
      ALTER TABLE club.academy_coaching_drills ADD COLUMN variant_of uuid
        REFERENCES club.academy_coaching_drills(id) ON DELETE RESTRICT;
      ALTER TABLE club.academy_coaching_drills ADD COLUMN variant_base_version integer CHECK(variant_base_version BETWEEN 1 AND 10000);
      ALTER TABLE club.academy_coaching_drills ADD CONSTRAINT drill_variant_lineage
        CHECK((variant_of IS NULL)=(variant_base_version IS NULL) AND variant_of IS DISTINCT FROM id);
      ALTER TABLE club.academy_coach_drill_proposals ADD COLUMN proposal_kind text NOT NULL DEFAULT 'new'
        CHECK(proposal_kind IN ('new','revision','variant'));
      ALTER TABLE club.academy_coach_drill_proposals ADD COLUMN base_drill_id uuid REFERENCES club.academy_coaching_drills(id) ON DELETE RESTRICT;
      ALTER TABLE club.academy_coach_drill_proposals ADD COLUMN base_version integer CHECK(base_version BETWEEN 1 AND 10000);
      ALTER TABLE club.academy_coach_drill_proposals ADD COLUMN applied_version integer CHECK(applied_version BETWEEN 1 AND 10000);
      ALTER TABLE club.academy_coach_drill_proposals ADD CONSTRAINT drill_proposal_lineage
        CHECK((proposal_kind='new' AND base_drill_id IS NULL AND base_version IS NULL) OR
          (proposal_kind IN ('revision','variant') AND base_drill_id IS NOT NULL AND base_version IS NOT NULL AND base_drill_id<>drill_id));
      CREATE OR REPLACE FUNCTION club.central_academy_drill_view(d club.academy_coaching_drills)
      RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,pg_temp AS $view$
        SELECT jsonb_build_object('id',d.id,'organization_id',d.organization_id,'title',d.title,
          'category',d.category,'age_band',d.age_band,'duration_minutes',d.duration_minutes,
          'objective',d.objective,'instructions',d.instructions,'status',d.status,'version',d.version)
          ||CASE WHEN d.difficulty IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('difficulty',d.difficulty) END
          ||CASE WHEN d.variant_of IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('variant_of',d.variant_of,'variant_base_version',d.variant_base_version) END
      $view$;
      CREATE FUNCTION club.coach_drill_proposal_view(d club.academy_coaching_drills,p club.academy_coach_drill_proposals)
      RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,pg_temp AS $proposal_view$
        SELECT club.central_academy_drill_view(d)||CASE WHEN p.proposal_kind='new' THEN '{}'::jsonb ELSE
          jsonb_build_object('proposal_kind',p.proposal_kind,'base_drill_id',p.base_drill_id,'base_version',p.base_version,
            'applied_version',p.applied_version) END
      $proposal_view$;
      CREATE FUNCTION club.submit_linked_coach_drill_proposal(_digest text,_id uuid,_base uuid,_base_version integer,
        _kind text,_difficulty text,_title text,_category text,_age text,_duration integer,_objective text,_instructions text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $linked$
      DECLARE v_source club.academy_coaching_drills%ROWTYPE;v_row club.academy_coaching_drills%ROWTYPE;
        v_proposal club.academy_coach_drill_proposals%ROWTYPE;v_actor uuid;v_session uuid;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_drill_proposal_coach(_digest);
        IF _id IS NULL OR _base IS NULL OR _id=_base OR _base_version IS NULL OR _base_version NOT BETWEEN 1 AND 10000
          OR _kind IS NULL OR _kind NOT IN ('revision','variant') OR _difficulty IS NULL OR _difficulty NOT IN ('beginner','intermediate','advanced')
          THEN RAISE EXCEPTION 'linked_drill_proposal_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_source FROM club.academy_coaching_drills WHERE id=_base AND status='approved' AND version=_base_version FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'linked_drill_base_conflict' USING ERRCODE='23505'; END IF;
          IF _title IS NULL OR length(_title) NOT BETWEEN 1 AND 180 OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
            OR _category IS NULL OR _category NOT IN ('technical','tactical','physical','goalkeeping','warmup','game','recovery','other')
            OR _age IS NULL OR _age NOT IN ('all','u6-u8','u9-u11','u12-u14','u15-u18','adult')
            OR _duration IS NULL OR _duration NOT BETWEEN 1 AND 120
            OR _objective IS NULL OR length(_objective) NOT BETWEEN 1 AND 1000 OR _objective<>btrim(_objective) OR _objective ~ '[[:cntrl:]]'
            OR _instructions IS NULL OR length(_instructions) NOT BETWEEN 1 AND 4000 OR _instructions<>btrim(_instructions,' '||chr(10)) OR replace(_instructions,chr(10),'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'drill_content_invalid' USING ERRCODE='22023'; END IF;
        -- Serialize the pending quota per coach without exposing any table.
        PERFORM pg_advisory_xact_lock(hashtextextended('coach_drill_proposal:'||v_actor::text,0));
        IF (SELECT count(*) FROM club.academy_coach_drill_proposals p JOIN club.academy_coaching_drills d ON d.id=p.drill_id
          WHERE p.coach_id=v_actor AND d.status='draft')>=50
        THEN RAISE EXCEPTION 'drill_proposal_pending_limit' USING ERRCODE='54000'; END IF;
        INSERT INTO club.academy_coaching_drills(id,organization_id,title,category,age_band,duration_minutes,objective,instructions,status,version,difficulty,variant_of,variant_base_version)
          VALUES(_id,'lefteria_fc_academy',_title,_category,_age,_duration,_objective,_instructions,'draft',1,_difficulty,
            CASE WHEN _kind='variant' THEN _base ELSE NULL END,CASE WHEN _kind='variant' THEN _base_version ELSE NULL END)
          RETURNING * INTO v_row;
        INSERT INTO club.academy_coach_drill_proposals(drill_id,coach_id,proposal_kind,base_drill_id,base_version)
          VALUES(_id,v_actor,_kind,_base,_base_version) RETURNING * INTO v_proposal;
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(_id,1,'create',v_actor,club.coach_drill_proposal_view(v_row,v_proposal));
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.drill.proposed','succeeded','lefteria_fc_academy','coach_linked_proposal_not_approved');
        RETURN club.coach_drill_proposal_view(v_row,v_proposal);
      END $linked$;
      CREATE FUNCTION club.apply_coach_drill_revision(_digest text,_id uuid,_expected integer,_base_version integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $apply$
      DECLARE v_actor uuid;v_session uuid;v_source club.academy_coaching_drills%ROWTYPE;
        v_candidate club.academy_coaching_drills%ROWTYPE;v_proposal club.academy_coach_drill_proposals%ROWTYPE;
      BEGIN
        SELECT actor_id,session_id INTO v_actor,v_session FROM club.require_central_academy_drills(_digest,true);
        IF _id IS NULL OR _expected IS NULL OR _expected NOT BETWEEN 1 AND 9999 OR _base_version IS NULL OR _base_version NOT BETWEEN 1 AND 9999
          THEN RAISE EXCEPTION 'drill_revision_review_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_proposal FROM club.academy_coach_drill_proposals WHERE drill_id=_id AND proposal_kind='revision' FOR UPDATE;
        IF NOT FOUND OR v_proposal.applied_version IS NOT NULL OR v_proposal.base_version<>_base_version
          THEN RAISE EXCEPTION 'drill_revision_conflict' USING ERRCODE='23505'; END IF;
        SELECT * INTO v_candidate FROM club.academy_coaching_drills WHERE id=_id AND status='draft' AND version=_expected FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'drill_revision_conflict' USING ERRCODE='23505'; END IF;
        SELECT * INTO v_source FROM club.academy_coaching_drills WHERE id=v_proposal.base_drill_id AND status='approved' AND version=_base_version FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'drill_revision_base_changed' USING ERRCODE='23505'; END IF;
        UPDATE club.academy_coaching_drills SET title=v_candidate.title,category=v_candidate.category,age_band=v_candidate.age_band,
          duration_minutes=v_candidate.duration_minutes,objective=v_candidate.objective,instructions=v_candidate.instructions,
          difficulty=v_candidate.difficulty,version=version+1 WHERE id=v_source.id RETURNING * INTO v_source;
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(v_source.id,v_source.version,'approve',v_actor,club.central_academy_drill_view(v_source));
        UPDATE club.academy_coaching_drills SET status='archived',version=version+1 WHERE id=_id RETURNING * INTO v_candidate;
        UPDATE club.academy_coach_drill_proposals SET applied_version=v_source.version WHERE drill_id=_id RETURNING * INTO v_proposal;
        INSERT INTO club.academy_coaching_drill_versions(drill_id,version,action,actor_id,snapshot)
          VALUES(_id,v_candidate.version,'archive',v_actor,club.coach_drill_proposal_view(v_candidate,v_proposal));
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,'admin.drill.revision.applied','succeeded','lefteria_fc_academy','coach_proposal_exact_base_version');
        RETURN club.coach_drill_proposal_view(v_candidate,v_proposal);
      END $apply$;
      REVOKE ALL ON FUNCTION club.coach_drill_proposal_view(club.academy_coaching_drills,club.academy_coach_drill_proposals),
        club.submit_linked_coach_drill_proposal(text,uuid,uuid,integer,text,text,text,text,text,integer,text,text),
        club.apply_coach_drill_revision(text,uuid,integer,integer) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION club.read_coach_drill_proposals(_digest text,_offset integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $proposals$
      DECLARE v_actor uuid;v_role text;v_count integer;v_rows jsonb;
      BEGIN
        SELECT actor_id,actor_role INTO v_actor,v_role FROM club.require_central_academy_drills(_digest,false);
        IF v_role='coach' THEN PERFORM club.require_drill_proposal_coach(_digest);
        ELSIF v_role IS DISTINCT FROM 'super_admin' THEN RAISE EXCEPTION 'drill_proposal_list_denied' USING ERRCODE='42501'; END IF;
        IF _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN RAISE EXCEPTION 'drill_proposal_offset_invalid' USING ERRCODE='22023'; END IF;
        WITH page AS (
          SELECT d AS drill,p AS proposal,p.submitted_at,p.drill_id FROM club.academy_coach_drill_proposals p
          JOIN club.academy_coaching_drills d ON d.id=p.drill_id
          WHERE (v_role='coach' AND p.coach_id=v_actor) OR (v_role='super_admin' AND d.status='draft')
          ORDER BY p.submitted_at DESC,p.drill_id LIMIT 31 OFFSET _offset
        ), numbered AS (SELECT drill,proposal,row_number() OVER(ORDER BY submitted_at DESC,drill_id) rn FROM page)
        SELECT coalesce(jsonb_agg(club.coach_drill_proposal_view(drill,proposal) ORDER BY rn) FILTER(WHERE rn<=30),'[]'::jsonb),
          count(*)::integer INTO v_rows,v_count FROM numbered;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','drills',v_rows,
          'next_offset',CASE WHEN v_count=31 AND _offset<=99970 THEN _offset+30 ELSE NULL END);
      END $proposals$;
      CREATE OR REPLACE FUNCTION club.write_central_academy_drill(_digest text,_id uuid,_expected integer,_action text,
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
            OR _instructions IS NULL OR length(_instructions) NOT BETWEEN 1 AND 4000 OR _instructions<>btrim(_instructions,' '||chr(10)) OR replace(_instructions,chr(10),'') ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'drill_content_invalid' USING ERRCODE='22023'; END IF;
        ELSE
          IF _title IS NOT NULL OR _category IS NOT NULL OR _age IS NOT NULL OR _duration IS NOT NULL
            OR _objective IS NOT NULL OR _instructions IS NOT NULL
          THEN RAISE EXCEPTION 'drill_lifecycle_content_invalid' USING ERRCODE='22023'; END IF;
        END IF;
        IF EXISTS(SELECT 1 FROM club.academy_coach_drill_proposals WHERE drill_id=_id AND proposal_kind='revision'
          AND (_action='approve' OR applied_version IS NOT NULL))
          THEN RAISE EXCEPTION 'use_exact_revision_review' USING ERRCODE='23505'; END IF;
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
      """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Preserve reviewed drill lineage using forward recovery.");
}
