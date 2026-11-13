using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A player may submit only the exact currently eligible published quiz.
/// Scores are private, one attempt per quiz, and answer keys are never
/// returned by the reader or grader.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113161000_HubPlayerWeeklyQuizScoring")]
public sealed class HubPlayerWeeklyQuizScoring : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE FUNCTION hub.submit_mobile_player_weekly_quiz(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_quiz uuid,_answers jsonb
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_submit$
          DECLARE
            v_track timestamptz; v_page jsonb; v_current uuid;
            v_item jsonb; v_question uuid; v_choice integer;
            v_correct integer; v_seen uuid[]:=ARRAY[]::uuid[];
            v_score integer:=0; v_total integer;
          BEGIN
            PERFORM hub.assert_current_quiz_player(
              _identity,_phone,_role,_expires,_owner);
            IF _quiz IS NULL OR _answers IS NULL OR
               jsonb_typeof(_answers) IS DISTINCT FROM 'array' THEN
              RAISE EXCEPTION 'quiz_answers_invalid' USING ERRCODE='22023';
            END IF;
            IF jsonb_array_length(_answers) NOT BETWEEN 3 AND 20 THEN
              RAISE EXCEPTION 'quiz_answers_invalid' USING ERRCODE='22023';
            END IF;

            SELECT started_at INTO v_track FROM hub.player_quiz_tracks
              WHERE identity_id=_identity AND organization_id=_owner FOR UPDATE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'quiz_track_missing' USING ERRCODE='42501';
            END IF;
            v_page:=hub.read_mobile_player_weekly_quiz(
              _identity,_phone,_role,_expires,_owner);
            IF v_page->'quiz' IS NULL OR v_page->'quiz'='null'::jsonb THEN
              RAISE EXCEPTION 'quiz_not_available' USING ERRCODE='40001';
            END IF;
            v_current:=(v_page->'quiz'->>'id')::uuid;
            IF v_current<>_quiz THEN
              RAISE EXCEPTION 'quiz_out_of_sequence' USING ERRCODE='40001';
            END IF;
            SELECT count(*) INTO v_total FROM hub.player_quiz_questions
              WHERE quiz_id=_quiz;
            IF v_total<>jsonb_array_length(_answers) THEN
              RAISE EXCEPTION 'quiz_incomplete_answers' USING ERRCODE='22023';
            END IF;

            FOR v_item IN SELECT value FROM jsonb_array_elements(_answers)
            LOOP
              IF jsonb_typeof(v_item)<>'object'
                OR NOT (v_item ? 'question_id')
                OR NOT (v_item ? 'selected_index')
                OR jsonb_typeof(v_item->'question_id')<>'string'
                OR jsonb_typeof(v_item->'selected_index')<>'number'
                OR (v_item->>'question_id') !~
                  '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
                OR (v_item->>'selected_index') !~ '^[0-3]$' THEN
                RAISE EXCEPTION 'quiz_answer_shape_invalid' USING ERRCODE='22023';
              END IF;
              v_question:=(v_item->>'question_id')::uuid;
              v_choice:=(v_item->>'selected_index')::integer;
              IF v_question=ANY(v_seen) THEN
                RAISE EXCEPTION 'quiz_duplicate_answer' USING ERRCODE='22023';
              END IF;
              v_seen:=array_append(v_seen,v_question);
              SELECT correct_index INTO v_correct
                FROM hub.player_quiz_questions
                WHERE quiz_id=_quiz AND id=v_question;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'quiz_foreign_answer' USING ERRCODE='22023';
              END IF;
              IF v_choice=v_correct THEN v_score:=v_score+1; END IF;
            END LOOP;
            INSERT INTO hub.player_quiz_attempts(
              identity_id,organization_id,quiz_id,score,total)
            VALUES(_identity,_owner,_quiz,v_score,v_total);
            RETURN jsonb_build_object('organization_id',_owner,
              'quiz_id',_quiz,'score',v_score,'total',v_total,
              'submitted',true);
          END $quiz_submit$;
          REVOKE ALL ON FUNCTION hub.submit_mobile_player_weekly_quiz(
            uuid,text,text,timestamptz,text,uuid,jsonb) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.submit_mobile_player_weekly_quiz(
            uuid,text,text,timestamptz,text,uuid,jsonb);
          """);
}
