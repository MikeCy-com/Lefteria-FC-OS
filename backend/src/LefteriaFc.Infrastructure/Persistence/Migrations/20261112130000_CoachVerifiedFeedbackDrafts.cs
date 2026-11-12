using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Coach-authored private feedback drafts only. Never sends a notification,
/// fabricates coach identity or exposes the content to the player or parents.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112130000_CoachVerifiedFeedbackDrafts")]
public sealed class CoachVerifiedFeedbackDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE club.coach_feedback_drafts (
              id uuid PRIMARY KEY,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              coach_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              staff_id varchar(128) NOT NULL REFERENCES club.staff(id) ON DELETE RESTRICT,
              player_id varchar(128) NOT NULL REFERENCES club.players(id) ON DELETE RESTRICT,
              observation varchar(500) NOT NULL,
              focus_point varchar(500) NOT NULL,
              language varchar(2) NOT NULL,
              status varchar(12) NOT NULL DEFAULT 'draft',
              version bigint NOT NULL DEFAULT 1,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_coach_feedback_owner CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_coach_feedback_language CHECK (language IN ('el','en')),
              CONSTRAINT ck_coach_feedback_status CHECK (status='draft'),
              CONSTRAINT ck_coach_feedback_version CHECK (version=1),
              CONSTRAINT ck_coach_feedback_observation CHECK
                (length(observation) BETWEEN 5 AND 500 AND observation=btrim(observation)
                 AND observation !~ '[[:cntrl:]]'),
              CONSTRAINT ck_coach_feedback_focus CHECK
                (length(focus_point) BETWEEN 5 AND 500 AND focus_point=btrim(focus_point)
                 AND focus_point !~ '[[:cntrl:]]')
            );
            CREATE INDEX ix_coach_feedback_drafts_coach ON club.coach_feedback_drafts(
              coach_identity_id,created_at DESC,id);
            REVOKE ALL ON TABLE club.coach_feedback_drafts FROM PUBLIC;
            ALTER TABLE club.coach_feedback_drafts ENABLE ROW LEVEL SECURITY;

            CREATE FUNCTION club.create_mobile_coach_feedback_draft(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _id uuid,_owner text,_player text,_observation text,
              _focus text,_language text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $coach_feedback$
            DECLARE
              v_staff text;
              v_player club.players%ROWTYPE;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
                OR _role IS DISTINCT FROM 'coach' OR _expires IS NULL
                OR _expires<=clock_timestamp() OR _id IS NULL
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
                OR _observation IS NULL OR length(_observation) NOT BETWEEN 5 AND 500
                OR btrim(_observation)<>_observation
                OR _observation ~ '[[:cntrl:]]'
                OR _focus IS NULL OR length(_focus) NOT BETWEEN 5 AND 500
                OR btrim(_focus)<>_focus OR _focus ~ '[[:cntrl:]]'
                OR _language NOT IN ('el','en')
              THEN RAISE EXCEPTION 'invalid_coach_feedback_draft' USING ERRCODE='22023';
              END IF;

              -- Revalidate canonical coach identity, active phone binding and
              -- staff record. This is not trusting a user-supplied coach name.
              SELECT s.id INTO STRICT v_staff
                FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l ON l.identity_id=i.id
                  AND l.role='coach' AND l.normalized_phone=i.normalized_phone
                  AND l.collection='staff' AND l.kind='staff_own'
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
                JOIN club.staff s ON s.id=l.record_id AND s.is_active AND NOT s.is_archived
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role='coach' AND i.is_active
                FOR SHARE OF i,l,b,s;
              -- All session and source bindings are locked during the draft
              -- transaction, preventing simultaneous revocation races.
              SELECT * INTO v_player FROM club.players
                WHERE id=_player AND organization_id=_owner
                  AND is_active AND NOT is_archived AND NOT is_transferred
                FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_feedback_player_not_found' USING ERRCODE='P0002';
              END IF;

              IF NOT EXISTS (
                SELECT 1 FROM club.roster_import_state WHERE id='initial'
              ) OR NOT EXISTS (
                SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial'
              ) THEN
                RAISE EXCEPTION 'coach_feedback_roster_unready' USING ERRCODE='55000';
              END IF;

              IF NOT (
                (_owner='lefteria_fc' AND v_player.team_type='First Team'
                  AND EXISTS (
                    SELECT 1 FROM club.staff_teams st
                    WHERE st.staff_id=v_staff AND st.organization_id=_owner
                      AND st.team_id=v_player.team_id
                  ))
                OR
                (_owner='lefteria_fc_academy' AND v_player.team_type='Academy'
                  AND EXISTS (
                    SELECT 1 FROM (
                      SELECT sg.academy_group_id AS group_id
                        FROM club.staff_academy_groups sg
                        WHERE sg.staff_id=v_staff AND sg.organization_id=_owner
                          AND (sg.from_plural OR sg.from_singular OR
                            (sg.from_group_coach AND EXISTS (
                              SELECT 1 FROM club.academy_group_coaches gc
                              WHERE gc.staff_id=v_staff
                                AND gc.academy_group_id=sg.academy_group_id
                                AND gc.organization_id=_owner)))
                      UNION
                      SELECT gc.academy_group_id FROM club.academy_group_coaches gc
                        WHERE gc.staff_id=v_staff AND gc.organization_id=_owner
                    ) assigned
                    WHERE assigned.group_id=v_player.primary_academy_group_id
                      OR EXISTS (
                        SELECT 1 FROM club.player_academy_groups membership
                        WHERE membership.organization_id=_owner
                          AND membership.player_id=v_player.id
                          AND membership.academy_group_id=assigned.group_id
                      )
                  ))
              ) THEN
                RAISE EXCEPTION 'coach_feedback_assignment_denied' USING ERRCODE='42501';
              END IF;

              INSERT INTO club.coach_feedback_drafts
                (id,organization_id,coach_identity_id,staff_id,player_id,
                 observation,focus_point,language)
              VALUES (_id,_owner,_identity,v_staff,_player,
                      _observation,_focus,_language);
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,action,outcome,
                 organization_id,reason_code)
              VALUES (gen_random_uuid(),'mobile',clock_timestamp(),_identity,
                'mobile.coach.feedback.draft','succeeded',_owner,'coach_authored');

              RETURN jsonb_build_object('id',_id,'organization_id',_owner,
                'player_id',_player,'status','draft','version',1,
                'language',_language,'requires_coach_approval',true);
            EXCEPTION
              WHEN no_data_found OR too_many_rows THEN
                RAISE EXCEPTION 'coach_feedback_identity_denied' USING ERRCODE='42501';
            END $coach_feedback$;
            REVOKE ALL ON FUNCTION club.create_mobile_coach_feedback_draft(
              uuid,text,text,timestamptz,uuid,text,text,text,text,text) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.create_mobile_coach_feedback_draft(
              uuid,text,text,timestamptz,uuid,text,text,text,text,text);
            DROP TABLE club.coach_feedback_drafts;
            """);
}
