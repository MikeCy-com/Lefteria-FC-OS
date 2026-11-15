using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// A player's private classroom: aggregate learning and quiz evidence only.
/// No unpublished content, answer keys, family data, or sibling player access.
/// The curriculum clock and completion writers remain unchanged.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115999000_HubPlayerClassroomProgress")]
public sealed class HubPlayerClassroomProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION hub.read_mobile_player_classrooms(
        _identity uuid,_phone text,_role text,_expires timestamptz,_owner text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $player_classrooms$
      DECLARE
        v_position text; v_learning jsonb; v_quiz jsonb;
        v_lessons integer; v_completed integer; v_topics jsonb;
        v_completed_weeks integer;
      BEGIN
        -- Both independent live boundary checks revalidate identity,
        -- canonical player/phone ownership, active roster and organization.
        -- A JWT role hint, a supplied position, or a parent/guardian
        -- relationship is never sufficient.
        v_position:=hub.assert_current_quiz_player(
          _identity,_phone,_role,_expires,_owner);
        v_learning:=hub.current_mobile_education_lesson(
          _identity,_phone,_role,_expires,_owner);
        v_quiz:=hub.read_mobile_player_weekly_quiz(
          _identity,_phone,_role,_expires,_owner);

        -- The approved catalogue is the same age-unrestricted, reviewed
        -- revision set eligible for the ordinary personal lesson pathway.
        -- Count current revisions only; withdrawn or revised lessons are
        -- not incorrectly credited as completed in today's catalogue.
        SELECT count(*)::integer,
          count(done.content_id)::integer INTO v_lessons,v_completed
        FROM hub.content c
        JOIN LATERAL (
          SELECT rr.id,rr.text_review,rr.visual_review
          FROM hub.revisions rr WHERE rr.content_id=c.id
          ORDER BY rr.revision DESC LIMIT 1
        ) rev ON true
        LEFT JOIN hub.education_completions done
          ON done.identity_id=_identity AND done.organization_id=_owner
          AND done.viewer_role='player' AND done.audience='player'
          AND done.content_id=c.id AND done.revision_id=rev.id
        WHERE c.organization_id=_owner AND c.kind IN ('article','course')
          AND c.audience_player AND c.status='published'
          AND c.approved_by IS NOT NULL AND c.approved_at IS NOT NULL
          AND c.published_at IS NOT NULL
          AND c.published_at<=clock_timestamp()
          AND c.age_min IS NULL AND c.age_max IS NULL
          AND rev.text_review='passed' AND rev.visual_review='passed'
          AND NOT EXISTS (
            SELECT 1 FROM hub.media_assets media
            WHERE media.revision_id=rev.id
              AND (media.kind<>'image' OR media.moderation<>'reviewed')
          );

        SELECT count(DISTINCT q.week_number)::integer
        INTO v_completed_weeks
        FROM hub.player_quiz_attempts a
        JOIN hub.player_quizzes q ON q.id=a.quiz_id
        WHERE a.identity_id=_identity AND a.organization_id=_owner
          AND q.organization_id=_owner;

        -- A classroom room is a football topic, not a promise of a
        -- released lesson. Count only fully reviewed, published quizzes
        -- applicable to the player's VERIFIED position. The normal
        -- weekly reader still enforces each learner's Week 1 clock.
        WITH rooms(topic,ordinal) AS (VALUES
          ('positioning',1),('defending',2),('attacking',3),
          ('transition',4),('passing',5),('movement',6),
          ('teamwork',7),('rules',8),('decision_making',9),
          ('goalkeeping',10),('general',11)
        ), eligible AS (
          SELECT q.id,q.topic
          FROM hub.player_quizzes q
          WHERE q.organization_id=_owner AND q.status='published'
            AND q.reviewed_by IS NOT NULL AND q.reviewed_at IS NOT NULL
            AND q.published_at IS NOT NULL
            AND q.published_at<=clock_timestamp()
            AND (SELECT count(*) FROM hub.player_quiz_questions qq
                 WHERE qq.quiz_id=q.id) BETWEEN 3 AND 20
            AND (
              q.position=v_position OR
              (q.position='all' AND NOT EXISTS (
                SELECT 1 FROM hub.player_quizzes specialized
                WHERE specialized.organization_id=q.organization_id
                  AND specialized.week_number=q.week_number
                  AND specialized.position=v_position
                  AND specialized.status='published'
                  AND specialized.reviewed_by IS NOT NULL
                  AND specialized.reviewed_at IS NOT NULL
                  AND specialized.published_at<=clock_timestamp()
                  AND (SELECT count(*) FROM hub.player_quiz_questions sq
                       WHERE sq.quiz_id=specialized.id) BETWEEN 3 AND 20
              ))
            )
        ), marks AS (
          SELECT e.topic,count(*)::integer AS published_quizzes,
            count(a.quiz_id)::integer AS completed_quizzes,
            coalesce(sum(a.score),0)::integer AS correct_answers,
            coalesce(sum(a.total),0)::integer AS total_answers
          FROM eligible e
          LEFT JOIN hub.player_quiz_attempts a
            ON a.quiz_id=e.id AND a.identity_id=_identity
              AND a.organization_id=_owner
          GROUP BY e.topic
        )
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'topic',room.topic,
          'published_quizzes',coalesce(mark.published_quizzes,0),
          'completed_quizzes',coalesce(mark.completed_quizzes,0),
          'correct_answers',coalesce(mark.correct_answers,0),
          'total_answers',coalesce(mark.total_answers,0)
        ) ORDER BY room.ordinal),'[]'::jsonb) INTO v_topics
        FROM rooms room LEFT JOIN marks mark ON mark.topic=room.topic;

        RETURN jsonb_build_object(
          'organization_id',_owner,'audience','player',
          'verified_position',v_position,
          'lessons',jsonb_build_object(
            'approved',v_lessons,'completed',v_completed,
            'next_title',v_learning->'next'->>'title',
            'next_kind',v_learning->'next'->>'kind'),
          'quizzes',jsonb_build_object(
            'started',(v_quiz->>'started')::boolean,
            'completed_weeks',v_completed_weeks,
            'current_week',least(521,v_completed_weeks+1),
            'next_quiz_title',v_quiz->'quiz'->>'title',
            'next_quiz_topic',v_quiz->'quiz'->>'topic'),
          'subjects',v_topics);
      END $player_classrooms$;
      REVOKE ALL ON FUNCTION hub.read_mobile_player_classrooms(
        uuid,text,text,timestamptz,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.read_mobile_player_classrooms(
            uuid,text,text,timestamptz,text);
          """);
}
