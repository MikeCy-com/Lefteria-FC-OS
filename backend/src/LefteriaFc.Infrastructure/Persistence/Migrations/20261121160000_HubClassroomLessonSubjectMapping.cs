using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Audited, supervised mapping of already reviewed and published lessons
/// into the existing football subjects. No change to delivery sequence.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121160000_HubClassroomLessonSubjectMapping")]
public sealed class HubClassroomLessonSubjectMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE hub.classroom_lesson_subjects (
        organization_id varchar(32) NOT NULL CHECK (
          organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
        revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
        topic varchar(24) NULL CHECK(topic IN (
          'positioning','defending','attacking','transition','passing',
          'movement','teamwork','rules','decision_making','goalkeeping','general')),
        version bigint NOT NULL CHECK(version BETWEEN 1 AND 9223372036854775806),
        classified_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        classified_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        reason varchar(250) NOT NULL CHECK(length(btrim(reason)) BETWEEN 8 AND 250),
        PRIMARY KEY(organization_id,content_id)
      );
      CREATE TABLE hub.classroom_lesson_subject_events (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
        revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
        previous_topic varchar(24),next_topic varchar(24),
        version bigint NOT NULL CHECK(version>=1),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        reason varchar(250) NOT NULL,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      CREATE INDEX ix_hub_classroom_lesson_events
        ON hub.classroom_lesson_subject_events(
          organization_id,content_id,occurred_at DESC,id);
      ALTER TABLE hub.classroom_lesson_subjects ENABLE ROW LEVEL SECURITY;
      ALTER TABLE hub.classroom_lesson_subject_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE hub.classroom_lesson_subjects,
        hub.classroom_lesson_subject_events FROM PUBLIC;

      CREATE FUNCTION hub.read_admin_classroom_curriculum(
        _digest text,_owner text,_offset integer,_limit integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $curriculum_list$
      DECLARE v_total integer;v_items jsonb;
      BEGIN
        PERFORM hub.assert_admin_learning_position_authority(_digest,_owner);
        IF _offset IS NULL OR _offset<0 OR _offset>20000
          OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
        THEN RAISE EXCEPTION 'curriculum_page_invalid' USING ERRCODE='22023'; END IF;
        SELECT count(*)::integer INTO v_total FROM hub.content c
        JOIN LATERAL (
          SELECT rev.id,rev.text_review,rev.visual_review
          FROM hub.revisions rev WHERE rev.content_id=c.id
          ORDER BY rev.revision DESC LIMIT 1
        ) rev ON true
        WHERE c.organization_id=_owner AND c.kind IN ('article','course')
          AND c.audience_player AND c.status='published'
          AND c.approved_by IS NOT NULL AND c.approved_at IS NOT NULL
          AND c.published_at IS NOT NULL AND c.published_at<=clock_timestamp()
          AND c.age_min IS NULL AND c.age_max IS NULL
          AND rev.text_review='passed' AND rev.visual_review='passed'
          AND NOT EXISTS (SELECT 1 FROM hub.media_assets m
            WHERE m.revision_id=rev.id
              AND (m.kind<>'image' OR m.moderation<>'reviewed'));
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',page.id,'title',page.title,'kind',page.kind,
          'revision_id',page.revision_id,
          'topic',CASE WHEN page.map_revision=page.revision_id
            THEN page.topic ELSE NULL END,
          'version',coalesce(page.version,0),
          'needs_reverification',page.map_revision IS NOT NULL
            AND page.map_revision<>page.revision_id
        ) ORDER BY page.published_at DESC,page.id DESC),'[]'::jsonb)
        INTO v_items FROM (
          SELECT c.id,c.title,c.kind,c.published_at,
            rev.id AS revision_id,map.revision_id AS map_revision,
            map.topic,map.version
          FROM hub.content c
          JOIN LATERAL (
            SELECT rev.id,rev.text_review,rev.visual_review
            FROM hub.revisions rev WHERE rev.content_id=c.id
            ORDER BY rev.revision DESC LIMIT 1
          ) rev ON true
          LEFT JOIN hub.classroom_lesson_subjects map
            ON map.organization_id=c.organization_id AND map.content_id=c.id
          WHERE c.organization_id=_owner AND c.kind IN ('article','course')
            AND c.audience_player AND c.status='published'
            AND c.approved_by IS NOT NULL AND c.approved_at IS NOT NULL
            AND c.published_at IS NOT NULL
            AND c.published_at<=clock_timestamp()
            AND c.age_min IS NULL AND c.age_max IS NULL
            AND rev.text_review='passed' AND rev.visual_review='passed'
            AND NOT EXISTS (SELECT 1 FROM hub.media_assets m
              WHERE m.revision_id=rev.id
                AND (m.kind<>'image' OR m.moderation<>'reviewed'))
          ORDER BY c.published_at DESC,c.id DESC
          LIMIT _limit OFFSET _offset
        ) page;
        RETURN jsonb_build_object('organization_id',_owner,
          'offset',_offset,'limit',_limit,'total',v_total,'items',v_items);
      END $curriculum_list$;
      REVOKE ALL ON FUNCTION hub.read_admin_classroom_curriculum(
        text,text,integer,integer) FROM PUBLIC;

      CREATE FUNCTION hub.set_admin_classroom_lesson_subject(
        _digest text,_owner text,_content uuid,_revision uuid,
        _expected_version bigint,_topic text,_reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $curriculum_set$
      DECLARE v_actor uuid;v_latest uuid;v_old_revision uuid;
        v_old_topic text;v_next text;v_version bigint;
      BEGIN
        v_actor:=hub.assert_admin_learning_position_authority(_digest,_owner);
        IF _content IS NULL OR _revision IS NULL
          OR _expected_version IS NULL OR _expected_version<0
          OR _expected_version>=9223372036854775806
          OR _topic IS NULL OR _topic NOT IN (
            'unassigned','positioning','defending','attacking',
            'transition','passing','movement','teamwork','rules',
            'decision_making','goalkeeping','general')
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 250
          OR btrim(_reason)<>_reason OR _reason ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'curriculum_mapping_invalid' USING ERRCODE='22023'; END IF;
        -- This lock prevents a concurrent withdrawal while assigning a topic.
        PERFORM 1 FROM hub.content c WHERE c.id=_content
          AND c.organization_id=_owner AND c.kind IN ('article','course')
          AND c.audience_player AND c.status='published'
          AND c.approved_by IS NOT NULL AND c.approved_at IS NOT NULL
          AND c.published_at IS NOT NULL AND c.published_at<=clock_timestamp()
          AND c.age_min IS NULL AND c.age_max IS NULL FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'curriculum_not_published'
          USING ERRCODE='P0002'; END IF;
        SELECT rev.id INTO v_latest FROM hub.revisions rev
          WHERE rev.content_id=_content ORDER BY rev.revision DESC LIMIT 1;
        IF v_latest IS DISTINCT FROM _revision THEN
          RAISE EXCEPTION 'curriculum_revision_conflict' USING ERRCODE='40001';
        END IF;
        PERFORM 1 FROM hub.revisions rev WHERE rev.id=_revision
          AND rev.content_id=_content AND rev.text_review='passed'
          AND rev.visual_review='passed'
          AND NOT EXISTS (SELECT 1 FROM hub.media_assets m
            WHERE m.revision_id=rev.id
              AND (m.kind<>'image' OR m.moderation<>'reviewed'));
        IF NOT FOUND THEN RAISE EXCEPTION 'curriculum_review_denied'
          USING ERRCODE='42501'; END IF;
        v_next:=nullif(_topic,'unassigned');
        SELECT map.topic,map.revision_id,map.version
          INTO v_old_topic,v_old_revision,v_version
        FROM hub.classroom_lesson_subjects map
        WHERE map.organization_id=_owner AND map.content_id=_content
        FOR UPDATE;
        v_version:=coalesce(v_version,0);
        IF _expected_version<>v_version THEN
          RAISE EXCEPTION 'curriculum_version_conflict' USING ERRCODE='40001';
        END IF;
        IF v_version>0 AND v_old_topic IS NOT DISTINCT FROM v_next
          AND v_old_revision=v_latest
        THEN RETURN jsonb_build_object(
          'id',_content,'organization_id',_owner,
          'revision_id',v_latest,'topic',v_next,'version',v_version,
          'needs_reverification',false); END IF;
        INSERT INTO hub.classroom_lesson_subjects(
          organization_id,content_id,revision_id,topic,
          version,classified_by,classified_at,reason
        ) VALUES(_owner,_content,v_latest,v_next,v_version+1,
          v_actor,clock_timestamp(),_reason)
        ON CONFLICT(organization_id,content_id) DO UPDATE SET
          revision_id=EXCLUDED.revision_id,topic=EXCLUDED.topic,
          version=EXCLUDED.version,classified_by=EXCLUDED.classified_by,
          classified_at=EXCLUDED.classified_at,reason=EXCLUDED.reason;
        INSERT INTO hub.classroom_lesson_subject_events(
          id,organization_id,content_id,revision_id,previous_topic,
          next_topic,version,actor_id,reason
        ) VALUES(gen_random_uuid(),_owner,_content,v_latest,v_old_topic,
          v_next,v_version+1,v_actor,_reason);
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,action,
          outcome,organization_id,subject_digest,reason_code,actor_realm)
        SELECT gen_random_uuid(),clock_timestamp(),v_actor,s.id,
          'admin.hub.classroom_subject','succeeded',_owner,
          md5(_content::text),'topic_'||coalesce(v_next,'unassigned'),'admin'
        FROM identity.admin_sessions s WHERE s.token_digest=_digest
          AND s.admin_account_id=v_actor
          AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        RETURN jsonb_build_object('id',_content,'organization_id',_owner,
          'revision_id',v_latest,'topic',v_next,'version',v_version+1,
          'needs_reverification',false);
      END $curriculum_set$;
      REVOKE ALL ON FUNCTION hub.set_admin_classroom_lesson_subject(
        text,text,uuid,uuid,bigint,text,text) FROM PUBLIC;

      -- Only the existing private player summary grows two counts per topic.
      -- Completion and weekly-release writers are never changed.
      CREATE OR REPLACE FUNCTION hub.read_mobile_player_classrooms(
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
        , lesson_marks AS (
          SELECT map.topic,count(*)::integer AS approved_lessons,
            count(done.content_id)::integer AS completed_lessons
          FROM hub.classroom_lesson_subjects map
          JOIN hub.content c ON c.id=map.content_id
            AND c.organization_id=map.organization_id
          JOIN LATERAL (
            SELECT rev.id,rev.text_review,rev.visual_review
            FROM hub.revisions rev WHERE rev.content_id=c.id
            ORDER BY rev.revision DESC LIMIT 1
          ) rev ON rev.id=map.revision_id
          LEFT JOIN hub.education_completions done
            ON done.identity_id=_identity AND done.organization_id=_owner
            AND done.viewer_role='player' AND done.audience='player'
            AND done.content_id=c.id AND done.revision_id=rev.id
          WHERE map.organization_id=_owner AND map.topic IS NOT NULL
            AND c.kind IN ('article','course')
            AND c.audience_player AND c.status='published'
            AND c.approved_by IS NOT NULL AND c.approved_at IS NOT NULL
            AND c.published_at IS NOT NULL AND c.published_at<=clock_timestamp()
            AND c.age_min IS NULL AND c.age_max IS NULL
            AND rev.text_review='passed' AND rev.visual_review='passed'
            AND NOT EXISTS (
              SELECT 1 FROM hub.media_assets media WHERE media.revision_id=rev.id
                AND (media.kind<>'image' OR media.moderation<>'reviewed'))
          GROUP BY map.topic
        )
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'topic',room.topic,
          'published_quizzes',coalesce(mark.published_quizzes,0),
          'completed_quizzes',coalesce(mark.completed_quizzes,0),
          'correct_answers',coalesce(mark.correct_answers,0),
          'total_answers',coalesce(mark.total_answers,0),
          'approved_lessons',coalesce(lesson_marks.approved_lessons,0),
          'completed_lessons',coalesce(lesson_marks.completed_lessons,0)
        ) ORDER BY room.ordinal),'[]'::jsonb) INTO v_topics
        FROM rooms room LEFT JOIN marks mark ON mark.topic=room.topic
          LEFT JOIN lesson_marks ON lesson_marks.topic=room.topic;

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
          -- Restore the old classroom reader before removing its mapping.
          CREATE OR REPLACE FUNCTION hub.read_mobile_player_classrooms(
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
          DROP FUNCTION hub.set_admin_classroom_lesson_subject(
            text,text,uuid,uuid,bigint,text,text);
          DROP FUNCTION hub.read_admin_classroom_curriculum(
            text,text,integer,integer);
          DROP TABLE hub.classroom_lesson_subject_events;
          DROP TABLE hub.classroom_lesson_subjects;
          """);
}
