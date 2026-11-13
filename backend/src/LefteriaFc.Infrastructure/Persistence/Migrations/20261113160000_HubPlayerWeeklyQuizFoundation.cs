using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, individually paced player football quizzes. No questions are
/// enabled by this migration: qualified coaches and admins must publish
/// separate reviewed quiz material before any member can receive it.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113160000_HubPlayerWeeklyQuizFoundation")]
public sealed class HubPlayerWeeklyQuizFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.player_quizzes (
            id uuid PRIMARY KEY,
            organization_id varchar(32) NOT NULL
              REFERENCES club.organizations(id) ON DELETE RESTRICT
              CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
            week_number integer NOT NULL CHECK(week_number BETWEEN 1 AND 520),
            topic varchar(32) NOT NULL CHECK(topic IN (
              'positioning','defending','attacking','transition',
              'passing','movement','teamwork','rules','decision_making',
              'goalkeeping','general')),
            position varchar(24) NOT NULL CHECK(position IN (
              'all','goalkeeper','defender','fullback','winger',
              'midfielder','striker')),
            title varchar(180) NOT NULL CHECK(
              length(btrim(title)) BETWEEN 5 AND 180),
            status varchar(16) NOT NULL DEFAULT 'draft' CHECK(
              status IN ('draft','review_required','approved','published','withdrawn')),
            reviewed_by uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
            reviewed_at timestamptz,
            published_at timestamptz,
            created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            CONSTRAINT ck_weekly_quiz_review CHECK (
              status NOT IN ('approved','published') OR
              (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)),
            CONSTRAINT ck_weekly_quiz_publish CHECK (
              (status='published')=(published_at IS NOT NULL)),
            UNIQUE(organization_id,week_number,position)
          );
          CREATE TABLE hub.player_quiz_questions (
            id uuid PRIMARY KEY,
            quiz_id uuid NOT NULL REFERENCES hub.player_quizzes(id) ON DELETE RESTRICT,
            sequence integer NOT NULL CHECK(sequence BETWEEN 1 AND 20),
            prompt varchar(500) NOT NULL CHECK(
              length(btrim(prompt)) BETWEEN 10 AND 500),
            choices jsonb NOT NULL CHECK(
              jsonb_typeof(choices)='array' AND
              jsonb_array_length(choices)=4),
            correct_index smallint NOT NULL CHECK(correct_index BETWEEN 0 AND 3),
            explanation text NOT NULL DEFAULT '' CHECK(length(explanation)<=2000),
            UNIQUE(quiz_id,sequence)
          );
          CREATE TABLE hub.player_quiz_tracks (
            identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
            started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(identity_id,organization_id)
          );
          CREATE TABLE hub.player_quiz_attempts (
            identity_id uuid NOT NULL,
            organization_id varchar(32) NOT NULL,
            quiz_id uuid NOT NULL REFERENCES hub.player_quizzes(id) ON DELETE RESTRICT,
            answer_digest char(64) NOT NULL CHECK(answer_digest ~ '^[0-9a-f]{64}$'),
            score integer NOT NULL CHECK(score BETWEEN 0 AND 20),
            total integer NOT NULL CHECK(total BETWEEN 1 AND 20 AND score<=total),
            submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(identity_id,organization_id,quiz_id),
            FOREIGN KEY(identity_id,organization_id)
              REFERENCES hub.player_quiz_tracks(identity_id,organization_id)
              ON DELETE RESTRICT
          );
          CREATE INDEX ix_player_quiz_approved_week
            ON hub.player_quizzes(organization_id,week_number)
            WHERE status='published';
          ALTER TABLE hub.player_quizzes ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.player_quiz_questions ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.player_quiz_tracks ENABLE ROW LEVEL SECURITY;
          ALTER TABLE hub.player_quiz_attempts ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.player_quizzes,hub.player_quiz_questions,
            hub.player_quiz_tracks,hub.player_quiz_attempts FROM PUBLIC;

          CREATE FUNCTION hub.assert_current_quiz_player(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_auth$
          DECLARE v_position text;
          BEGIN
            IF _identity IS NULL OR _phone IS NULL
              OR _phone !~ '^[1-9][0-9]{7,14}$'
              OR _role IS DISTINCT FROM 'player'
              OR _expires IS NULL OR _expires<=clock_timestamp()
              OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            THEN
              RAISE EXCEPTION 'quiz_scope_denied' USING ERRCODE='42501';
            END IF;
            -- Recheck the live canonical player ownership and exact
            -- organization. An active role alone cannot prove a roster link,
            -- selected position or cross-club membership.
            SELECT p.position INTO STRICT v_position
              FROM identity.mobile_identities i
              JOIN identity.mobile_record_links l
                ON l.identity_id=i.id AND l.role='player'
                  AND l.normalized_phone=i.normalized_phone
                  AND l.collection='players' AND l.kind='player_own'
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id
                  AND b.normalized_phone=i.normalized_phone
              JOIN club.players p ON p.id=l.record_id
                AND p.organization_id=_owner
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='player' AND i.is_active
              FOR SHARE OF i,l,b,p;
            RETURN CASE v_position
              WHEN 'Defender' THEN 'defender'
              WHEN 'Midfielder' THEN 'midfielder'
              WHEN 'Goalkeeper' THEN 'goalkeeper'
              ELSE 'all'
            END;
          EXCEPTION WHEN no_data_found OR too_many_rows THEN
            RAISE EXCEPTION 'quiz_identity_denied' USING ERRCODE='42501';
          END $quiz_auth$;
          REVOKE ALL ON FUNCTION hub.assert_current_quiz_player(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          -- A first explicit start creates this learner's Week 1 clock.
          -- No shared publication date or another member's progress advances it.
          CREATE FUNCTION hub.start_mobile_player_quiz_track(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_start$
          DECLARE v_started timestamptz;
          BEGIN
            PERFORM hub.assert_current_quiz_player(
              _identity,_phone,_role,_expires,_owner);
            INSERT INTO hub.player_quiz_tracks(identity_id,organization_id)
              VALUES(_identity,_owner) ON CONFLICT DO NOTHING;
            SELECT started_at INTO v_started FROM hub.player_quiz_tracks
              WHERE identity_id=_identity AND organization_id=_owner;
            RETURN jsonb_build_object('organization_id',_owner,
              'started_at',v_started);
          END $quiz_start$;
          REVOKE ALL ON FUNCTION hub.start_mobile_player_quiz_track(
            uuid,text,text,timestamptz,text) FROM PUBLIC;

          -- No answer keys or explanations before an assessed submission.
          -- No quiz is visible unless a human-approved, published complete bank
          -- exists for the learner's own elapsed curriculum week.
          CREATE FUNCTION hub.read_mobile_player_weekly_quiz(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_read$
          DECLARE v_started timestamptz; v_quiz jsonb; v_position text;
          BEGIN
            v_position:=hub.assert_current_quiz_player(
              _identity,_phone,_role,_expires,_owner);
            SELECT started_at INTO v_started FROM hub.player_quiz_tracks
              WHERE identity_id=_identity AND organization_id=_owner;
            IF v_started IS NULL THEN
              RETURN jsonb_build_object('organization_id',_owner,
                'started',false,'quiz',NULL);
            END IF;
            SELECT jsonb_build_object(
                'id',q.id,'title',q.title,'topic',q.topic,
                'position',q.position,'week_number',q.week_number,
                'questions',(
                  SELECT jsonb_agg(jsonb_build_object(
                    'id',qq.id,'sequence',qq.sequence,'prompt',qq.prompt,
                    'choices',qq.choices) ORDER BY qq.sequence)
                  FROM hub.player_quiz_questions qq WHERE qq.quiz_id=q.id))
              INTO v_quiz
              FROM hub.player_quizzes q
              WHERE q.organization_id=_owner AND q.status='published'
                AND q.reviewed_by IS NOT NULL AND q.reviewed_at IS NOT NULL
                AND q.published_at IS NOT NULL AND q.published_at<=clock_timestamp()
                AND (q.position=v_position OR
                     (q.position='all' AND NOT EXISTS (
                       SELECT 1 FROM hub.player_quizzes specialized
                       WHERE specialized.organization_id=_owner
                         AND specialized.week_number=q.week_number
                         AND specialized.position=v_position
                         AND specialized.status='published'
                         AND specialized.reviewed_by IS NOT NULL
                         AND specialized.reviewed_at IS NOT NULL
                         AND specialized.published_at<=clock_timestamp()
                         AND (SELECT count(*) FROM hub.player_quiz_questions sq
                              WHERE sq.quiz_id=specialized.id) BETWEEN 3 AND 20
                     )))
                AND q.week_number<=1+floor(extract(epoch from
                  (clock_timestamp()-v_started))/604800)::integer
                AND (SELECT count(*) FROM hub.player_quiz_questions qq
                     WHERE qq.quiz_id=q.id) BETWEEN 3 AND 20
                AND NOT EXISTS(
                  SELECT 1 FROM hub.player_quiz_attempts a
                  WHERE a.identity_id=_identity AND a.organization_id=_owner
                    AND a.quiz_id=q.id)
              ORDER BY q.week_number ASC,q.published_at ASC,q.id ASC
              LIMIT 1;
            RETURN jsonb_build_object('organization_id',_owner,
              'started',true,'quiz',v_quiz);
          END $quiz_read$;
          REVOKE ALL ON FUNCTION hub.read_mobile_player_weekly_quiz(
            uuid,text,text,timestamptz,text) FROM PUBLIC;
          """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.read_mobile_player_weekly_quiz(
            uuid,text,text,timestamptz,text);
          DROP FUNCTION hub.start_mobile_player_quiz_track(
            uuid,text,text,timestamptz,text);
          DROP FUNCTION hub.assert_current_quiz_player(
            uuid,text,text,timestamptz,text);
          DROP TABLE hub.player_quiz_attempts;
          DROP TABLE hub.player_quiz_tracks;
          DROP TABLE hub.player_quiz_questions;
          DROP TABLE hub.player_quizzes;
          """);
}
