using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Two explicit, independently authenticated management decisions.
/// Review is separate from creation and publishing is separate from review.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113163000_HubPlayerQuizEditorialApproval")]
public sealed class HubPlayerQuizEditorialApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE FUNCTION hub.approve_admin_player_quiz(
            _digest text,_owner text,_id uuid
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_approve$
          DECLARE v_auth jsonb; v_actor uuid; v_session uuid;
            v_quiz hub.player_quizzes%ROWTYPE; v_questions integer;
          BEGIN
            IF _id IS NULL THEN
              RAISE EXCEPTION 'quiz_invalid_id' USING ERRCODE='22023';
            END IF;
            v_auth:=hub.authorize_admin_player_quiz(_digest,_owner,true);
            v_actor:=(v_auth->>'actor_id')::uuid;
            v_session:=(v_auth->>'session_id')::uuid;
            SELECT * INTO STRICT v_quiz FROM hub.player_quizzes
              WHERE id=_id AND organization_id=_owner FOR UPDATE;
            SELECT count(*) INTO v_questions FROM hub.player_quiz_questions
              WHERE quiz_id=_id;
            IF v_quiz.status<>'review_required' OR v_quiz.created_by IS NULL
              OR v_quiz.created_by=v_actor OR v_quiz.reviewed_by IS NOT NULL
              OR v_questions NOT BETWEEN 3 AND 20 THEN
              RAISE EXCEPTION 'quiz_review_conflict' USING ERRCODE='40001';
            END IF;
            UPDATE hub.player_quizzes
              SET status='approved',reviewed_by=v_actor,
                reviewed_at=clock_timestamp()
              WHERE id=_id AND organization_id=_owner;
            INSERT INTO hub.player_quiz_review_events(id,quiz_id,actor_id,action)
              VALUES(gen_random_uuid(),_id,v_actor,'approved');
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.hub.quiz.approve','succeeded',_owner,'quiz_human_reviewed');
            RETURN jsonb_build_object('id',_id,'organization_id',_owner,
              'status','approved','reviewed_by',v_actor);
          EXCEPTION WHEN no_data_found THEN
            RAISE EXCEPTION 'quiz_not_found' USING ERRCODE='P0002';
          END $quiz_admin_approve$;
          REVOKE ALL ON FUNCTION hub.approve_admin_player_quiz(
            text,text,uuid) FROM PUBLIC;

          CREATE FUNCTION hub.publish_admin_player_quiz(
            _digest text,_owner text,_id uuid
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_publish$
          DECLARE v_auth jsonb; v_actor uuid; v_session uuid;
            v_quiz hub.player_quizzes%ROWTYPE; v_questions integer;
          BEGIN
            IF _id IS NULL THEN
              RAISE EXCEPTION 'quiz_invalid_id' USING ERRCODE='22023';
            END IF;
            v_auth:=hub.authorize_admin_player_quiz(_digest,_owner,true);
            v_actor:=(v_auth->>'actor_id')::uuid;
            v_session:=(v_auth->>'session_id')::uuid;
            SELECT * INTO STRICT v_quiz FROM hub.player_quizzes
              WHERE id=_id AND organization_id=_owner FOR UPDATE;
            SELECT count(*) INTO v_questions FROM hub.player_quiz_questions
              WHERE quiz_id=_id;
            IF v_quiz.status<>'approved' OR v_quiz.reviewed_by IS NULL
              OR v_quiz.reviewed_at IS NULL OR v_quiz.published_at IS NOT NULL
              OR v_quiz.created_by IS NULL OR v_quiz.created_by=v_quiz.reviewed_by
              OR v_questions NOT BETWEEN 3 AND 20 THEN
              RAISE EXCEPTION 'quiz_publication_conflict' USING ERRCODE='40001';
            END IF;
            UPDATE hub.player_quizzes
              SET status='published',published_at=clock_timestamp()
              WHERE id=_id AND organization_id=_owner;
            INSERT INTO hub.player_quiz_review_events(id,quiz_id,actor_id,action)
              VALUES(gen_random_uuid(),_id,v_actor,'published');
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.hub.quiz.publish','succeeded',_owner,'quiz_published');
            RETURN jsonb_build_object('id',_id,'organization_id',_owner,
              'status','published');
          EXCEPTION WHEN no_data_found THEN
            RAISE EXCEPTION 'quiz_not_found' USING ERRCODE='P0002';
          END $quiz_admin_publish$;
          REVOKE ALL ON FUNCTION hub.publish_admin_player_quiz(
            text,text,uuid) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.publish_admin_player_quiz(text,text,uuid);
          DROP FUNCTION hub.approve_admin_player_quiz(text,text,uuid);
          """);
}
