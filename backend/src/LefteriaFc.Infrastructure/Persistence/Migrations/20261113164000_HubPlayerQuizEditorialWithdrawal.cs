using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Explicit human withdrawal removes unsafe quizzes from future delivery.
/// Historical private scores are retained, never reassigned or republished.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113164000_HubPlayerQuizEditorialWithdrawal")]
public sealed class HubPlayerQuizEditorialWithdrawal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE FUNCTION hub.withdraw_admin_player_quiz(
            _digest text,_owner text,_id uuid
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_admin_withdraw$
          DECLARE v_auth jsonb; v_actor uuid; v_session uuid;
            v_quiz hub.player_quizzes%ROWTYPE;
          BEGIN
            IF _id IS NULL THEN
              RAISE EXCEPTION 'quiz_invalid_id' USING ERRCODE='22023';
            END IF;
            v_auth:=hub.authorize_admin_player_quiz(_digest,_owner,true);
            v_actor:=(v_auth->>'actor_id')::uuid;
            v_session:=(v_auth->>'session_id')::uuid;
            SELECT * INTO STRICT v_quiz FROM hub.player_quizzes
              WHERE id=_id AND organization_id=_owner FOR UPDATE;
            IF v_quiz.status NOT IN ('review_required','approved','published') THEN
              RAISE EXCEPTION 'quiz_withdrawal_conflict' USING ERRCODE='40001';
            END IF;
            UPDATE hub.player_quizzes
              SET status='withdrawn',published_at=NULL
              WHERE id=_id AND organization_id=_owner;
            INSERT INTO hub.player_quiz_review_events(id,quiz_id,actor_id,action)
              VALUES(gen_random_uuid(),_id,v_actor,'withdrawn');
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
              'admin.hub.quiz.withdraw','succeeded',_owner,'quiz_human_withdrawal');
            RETURN jsonb_build_object('id',_id,'organization_id',_owner,
              'status','withdrawn');
          EXCEPTION WHEN no_data_found THEN
            RAISE EXCEPTION 'quiz_not_found' USING ERRCODE='P0002';
          END $quiz_admin_withdraw$;
          REVOKE ALL ON FUNCTION hub.withdraw_admin_player_quiz(
            text,text,uuid) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.withdraw_admin_player_quiz(text,text,uuid);
          """);
}
