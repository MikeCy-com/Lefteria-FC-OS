using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Human-admin quiz drafting and preview. Answer keys never enter a mobile
/// projection. Draft creation submits for separate human review, not publish.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113162000_HubPlayerQuizEditorialDraft")]
public sealed class HubPlayerQuizEditorialDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          ALTER TABLE hub.player_quizzes ADD COLUMN created_by uuid
            REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT;

          CREATE TABLE hub.player_quiz_review_events (
            id uuid PRIMARY KEY,
            quiz_id uuid NOT NULL REFERENCES hub.player_quizzes(id) ON DELETE RESTRICT,
            actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
            action varchar(20) NOT NULL CHECK(action IN ('created','approved','published')),
            occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
          );
          ALTER TABLE hub.player_quiz_review_events ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.player_quiz_review_events FROM PUBLIC;

          CREATE FUNCTION hub.authorize_admin_player_quiz(
            _digest text,_owner text,_manager boolean
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_auth$
          DECLARE v_actor uuid; v_session uuid; v_role text;
          BEGIN
            IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
              OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              OR _manager IS NULL THEN
              RAISE EXCEPTION 'quiz_admin_invalid' USING ERRCODE='42501';
            END IF;
            PERFORM identity.lock_admin_club_authority(_digest,_owner);
            SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
              FROM identity.admin_sessions s
              JOIN identity.admin_accounts a ON a.id=s.admin_account_id
              JOIN identity.admin_organization_grants g
                ON g.admin_account_id=a.id AND g.organization_id=_owner
              WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                AND s.expires_at>clock_timestamp() AND a.is_active
                AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                AND a.password_hash IS NOT NULL;
            IF v_actor IS NULL OR
              (_manager AND v_role NOT IN ('manager','super_admin')) OR
              (NOT _manager AND v_role NOT IN ('manager','super_admin','editor'))
            THEN
              RAISE EXCEPTION 'quiz_admin_denied' USING ERRCODE='42501';
            END IF;
            RETURN jsonb_build_object(
              'actor_id',v_actor,'session_id',v_session,'role',v_role);
          END $quiz_admin_auth$;
          REVOKE ALL ON FUNCTION hub.authorize_admin_player_quiz(
            text,text,boolean) FROM PUBLIC;

          CREATE FUNCTION hub.create_admin_player_quiz(
            _digest text,_owner text,_id uuid,_week integer,
            _topic text,_position text,_title text,_questions jsonb
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_create$
          DECLARE v_auth jsonb; v_actor uuid; v_session uuid;
            v_question jsonb; v_choice jsonb; v_seq integer:=0;
            v_prompt text; v_explanation text; v_correct integer;
          BEGIN
            IF _id IS NULL OR _week NOT BETWEEN 1 AND 520
              OR _topic NOT IN ('positioning','defending','attacking',
                'transition','passing','movement','teamwork','rules',
                'decision_making','goalkeeping','general')
              OR _position NOT IN ('all','goalkeeper','defender',
                'fullback','winger','midfielder','striker')
              OR _title IS NULL OR length(btrim(_title)) NOT BETWEEN 5 AND 180
              OR _title<>btrim(_title) OR _title ~ '[[:cntrl:]]'
              OR _questions IS NULL
              OR jsonb_typeof(_questions) IS DISTINCT FROM 'array'
            THEN
              RAISE EXCEPTION 'quiz_invalid_draft' USING ERRCODE='22023';
            END IF;
            IF jsonb_array_length(_questions) NOT BETWEEN 3 AND 20 THEN
              RAISE EXCEPTION 'quiz_invalid_question_count' USING ERRCODE='22023';
            END IF;
            v_auth:=hub.authorize_admin_player_quiz(_digest,_owner,false);
            v_actor:=(v_auth->>'actor_id')::uuid;
            v_session:=(v_auth->>'session_id')::uuid;

            INSERT INTO hub.player_quizzes(id,organization_id,week_number,topic,
              position,title,status,created_by)
            VALUES(_id,_owner,_week,_topic,_position,_title,
              'review_required',v_actor);

            FOR v_question IN SELECT value FROM jsonb_array_elements(_questions)
            LOOP
              v_seq:=v_seq+1;
              IF jsonb_typeof(v_question) IS DISTINCT FROM 'object'
                OR jsonb_typeof(v_question->'prompt') IS DISTINCT FROM 'string'
                OR jsonb_typeof(v_question->'choices') IS DISTINCT FROM 'array'
                OR jsonb_typeof(v_question->'correct_index') IS DISTINCT FROM 'number'
              THEN RAISE EXCEPTION 'quiz_invalid_question' USING ERRCODE='22023';
              END IF;
              v_prompt:=v_question->>'prompt';
              IF length(btrim(v_prompt)) NOT BETWEEN 10 AND 500
                OR v_prompt<>btrim(v_prompt) OR v_prompt ~ '[[:cntrl:]]'
                OR jsonb_array_length(v_question->'choices')<>4
                OR (v_question->>'correct_index') !~ '^[0-3]$'
              THEN RAISE EXCEPTION 'quiz_invalid_answer' USING ERRCODE='22023';
              END IF;
              v_correct:=(v_question->>'correct_index')::integer;
              v_explanation:=coalesce(v_question->>'explanation','');
              IF length(v_explanation)>2000 OR v_explanation ~ '[[:cntrl:]]' THEN
                RAISE EXCEPTION 'quiz_invalid_explanation' USING ERRCODE='22023';
              END IF;
              FOR v_choice IN SELECT value FROM jsonb_array_elements(v_question->'choices')
              LOOP
                IF jsonb_typeof(v_choice) IS DISTINCT FROM 'string'
                  OR length(btrim(v_choice #>> '{}')) NOT BETWEEN 1 AND 500
                  OR v_choice #>> '{}' <> btrim(v_choice #>> '{}')
                  OR (v_choice #>> '{}') ~ '[[:cntrl:]]'
                THEN RAISE EXCEPTION 'quiz_invalid_option' USING ERRCODE='22023';
                END IF;
              END LOOP;
              IF (SELECT count(DISTINCT x.value) FROM
                jsonb_array_elements_text(v_question->'choices') AS x(value))<>4
              THEN RAISE EXCEPTION 'quiz_repeated_option' USING ERRCODE='22023';
              END IF;
              INSERT INTO hub.player_quiz_questions
                (id,quiz_id,sequence,prompt,choices,correct_index,explanation)
              VALUES (gen_random_uuid(),_id,v_seq,v_prompt,v_question->'choices',
                v_correct,v_explanation);
            END LOOP;

            INSERT INTO hub.player_quiz_review_events(id,quiz_id,actor_id,action)
              VALUES(gen_random_uuid(),_id,v_actor,'created');
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.hub.quiz.create','succeeded',_owner,'quiz_review_required');
            RETURN jsonb_build_object('id',_id,'organization_id',_owner,
              'status','review_required','question_count',v_seq);
          END $quiz_admin_create$;
          REVOKE ALL ON FUNCTION hub.create_admin_player_quiz(
            text,text,uuid,integer,text,text,text,jsonb) FROM PUBLIC;

          -- A safe admin directory lists no choices, answer keys, or player results.
          CREATE FUNCTION hub.list_admin_player_quizzes(
            _digest text,_owner text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_list$
          DECLARE v_items jsonb;
          BEGIN
            PERFORM hub.authorize_admin_player_quiz(_digest,_owner,false);
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',sub.id,'title',sub.title,'week_number',sub.week_number,
              'topic',sub.topic,'position',sub.position,'status',sub.status,
              'question_count',sub.question_count)
              ORDER BY sub.week_number,sub.id),'[]'::jsonb)
            INTO v_items FROM (
              SELECT q.id,q.title,q.week_number,q.topic,q.position,q.status,
                (SELECT count(*) FROM hub.player_quiz_questions qq
                 WHERE qq.quiz_id=q.id) AS question_count
              FROM hub.player_quizzes q WHERE q.organization_id=_owner
              ORDER BY q.week_number,q.id LIMIT 100
            ) sub;
            RETURN jsonb_build_object('organization_id',_owner,'items',v_items);
          END $quiz_admin_list$;
          REVOKE ALL ON FUNCTION hub.list_admin_player_quizzes(
            text,text) FROM PUBLIC;

          -- Only a currently authorized manager can see the private answer key.
          CREATE FUNCTION hub.detail_admin_player_quiz(
            _digest text,_owner text,_id uuid
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_detail$
          DECLARE v_quiz hub.player_quizzes%ROWTYPE; v_questions jsonb;
          BEGIN
            PERFORM hub.authorize_admin_player_quiz(_digest,_owner,true);
            SELECT * INTO STRICT v_quiz FROM hub.player_quizzes
              WHERE id=_id AND organization_id=_owner FOR SHARE;
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'id',qq.id,'sequence',qq.sequence,'prompt',qq.prompt,
              'choices',qq.choices,'correct_index',qq.correct_index,
              'explanation',qq.explanation)
              ORDER BY qq.sequence),'[]'::jsonb)
            INTO v_questions FROM hub.player_quiz_questions qq
              WHERE qq.quiz_id=_id;
            RETURN jsonb_build_object('id',_id,'organization_id',_owner,
              'status',v_quiz.status,'title',v_quiz.title,
              'topic',v_quiz.topic,'position',v_quiz.position,
              'week_number',v_quiz.week_number,'questions',v_questions);
          EXCEPTION WHEN no_data_found THEN
            RAISE EXCEPTION 'quiz_not_found' USING ERRCODE='P0002';
          END $quiz_admin_detail$;
          REVOKE ALL ON FUNCTION hub.detail_admin_player_quiz(
            text,text,uuid) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.detail_admin_player_quiz(text,text,uuid);
          DROP FUNCTION hub.list_admin_player_quizzes(text,text);
          DROP FUNCTION hub.create_admin_player_quiz(
            text,text,uuid,integer,text,text,text,jsonb);
          DROP FUNCTION hub.authorize_admin_player_quiz(text,text,boolean);
          DROP TABLE hub.player_quiz_review_events;
          ALTER TABLE hub.player_quizzes DROP COLUMN created_by;
          """);
}
