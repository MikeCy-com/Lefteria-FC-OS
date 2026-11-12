using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Final wording can be explicitly signed off only by the same authenticated
/// and currently assigned coach who authored the private draft. No delivery.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112150000_CoachFeedbackExplicitSignoff")]
public sealed class CoachFeedbackExplicitSignoff : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE club.coach_feedback_drafts
              DROP CONSTRAINT ck_coach_feedback_status,
              DROP CONSTRAINT ck_coach_feedback_version;
            ALTER TABLE club.coach_feedback_drafts
              ADD COLUMN final_text text,
              ADD COLUMN approved_at timestamptz,
              ADD COLUMN approved_by uuid REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              ADD CONSTRAINT ck_coach_feedback_status
                CHECK (status IN ('draft','approved')),
              ADD CONSTRAINT ck_coach_feedback_version CHECK (version BETWEEN 1 AND 2),
              ADD CONSTRAINT ck_coach_feedback_signoff CHECK (
                (status='approved') =
                (final_text IS NOT NULL AND approved_at IS NOT NULL AND approved_by IS NOT NULL)),
              ADD CONSTRAINT ck_coach_feedback_final_text CHECK (
                final_text IS NULL OR (length(final_text) BETWEEN 30 AND 1600
                  AND final_text=btrim(final_text)
                  AND final_text !~ '[[:cntrl:]]'));

            CREATE FUNCTION club.approve_mobile_coach_feedback_draft(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _id uuid,_version bigint,_final_text text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $coach_signoff$
            DECLARE
              v_draft club.coach_feedback_drafts%ROWTYPE;
              v_staff text;
              v_player club.players%ROWTYPE;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
                OR _role IS DISTINCT FROM 'coach' OR _expires IS NULL
                OR _expires<=clock_timestamp() OR _id IS NULL
                OR _version IS DISTINCT FROM 1
                OR _final_text IS NULL OR length(_final_text) NOT BETWEEN 30 AND 1600
                OR _final_text<>btrim(_final_text)
                OR _final_text ~ '[[:cntrl:]]'
              THEN RAISE EXCEPTION 'invalid_coach_signoff' USING ERRCODE='22023';
              END IF;
              -- Lock first; only the original coach can sign off on this row.
              SELECT * INTO v_draft FROM club.coach_feedback_drafts
                WHERE id=_id FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_feedback_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_draft.coach_identity_id<>_identity THEN
                RAISE EXCEPTION 'coach_signoff_denied' USING ERRCODE='42501';
              END IF;
              IF v_draft.status<>'draft' OR v_draft.version<>_version
                OR v_draft.final_text IS NOT NULL THEN
                RAISE EXCEPTION 'coach_signoff_conflict' USING ERRCODE='40001';
              END IF;

              SELECT s.id INTO STRICT v_staff
                FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l ON l.identity_id=i.id
                  AND l.role='coach' AND l.normalized_phone=i.normalized_phone
                  AND l.collection='staff' AND l.kind='staff_own'
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
                JOIN club.staff s ON s.id=l.record_id
                  AND s.is_active AND NOT s.is_archived
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role='coach' AND i.is_active
                  AND s.id=v_draft.staff_id
                FOR SHARE OF i,l,b,s;

              SELECT * INTO v_player FROM club.players
                WHERE id=v_draft.player_id
                  AND organization_id=v_draft.organization_id
                  AND is_active AND NOT is_archived AND NOT is_transferred
                FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_signoff_player_unavailable' USING ERRCODE='P0002';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial')
                OR NOT EXISTS (SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial')
              THEN RAISE EXCEPTION 'coach_signoff_roster_unready' USING ERRCODE='55000';
              END IF;

              -- Refuse stale team or academy assignments; draft creation
              -- does not grant indefinite coach access to the player.
              IF NOT (
                (v_draft.organization_id='lefteria_fc'
                  AND v_player.team_type='First Team'
                  AND EXISTS (
                    SELECT 1 FROM club.staff_teams st
                    WHERE st.staff_id=v_staff
                      AND st.organization_id=v_draft.organization_id
                      AND st.team_id=v_player.team_id))
                OR
                (v_draft.organization_id='lefteria_fc_academy'
                  AND v_player.team_type='Academy'
                  AND EXISTS (
                    SELECT 1 FROM (
                      SELECT sg.academy_group_id AS group_id
                        FROM club.staff_academy_groups sg
                        WHERE sg.staff_id=v_staff
                          AND sg.organization_id=v_draft.organization_id
                          AND (sg.from_plural OR sg.from_singular OR
                            (sg.from_group_coach AND EXISTS (
                              SELECT 1 FROM club.academy_group_coaches actual
                                WHERE actual.academy_group_id=sg.academy_group_id
                                  AND actual.staff_id=v_staff
                                  AND actual.organization_id=v_draft.organization_id)))
                      UNION
                      SELECT gc.academy_group_id FROM club.academy_group_coaches gc
                        WHERE gc.staff_id=v_staff AND gc.organization_id=v_draft.organization_id
                    ) assigned
                    WHERE assigned.group_id=v_player.primary_academy_group_id
                      OR EXISTS (
                        SELECT 1 FROM club.player_academy_groups membership
                        WHERE membership.organization_id=v_draft.organization_id
                          AND membership.player_id=v_player.id
                          AND membership.academy_group_id=assigned.group_id)))
              ) THEN RAISE EXCEPTION 'coach_signoff_assignment_denied' USING ERRCODE='42501';
              END IF;

              UPDATE club.coach_feedback_drafts
                SET status='approved',version=version+1,
                  final_text=_final_text,approved_at=clock_timestamp(),approved_by=_identity
                WHERE id=_id AND coach_identity_id=_identity
                  AND version=_version AND status='draft';
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_signoff_conflict' USING ERRCODE='40001';
              END IF;
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,action,outcome,
                 organization_id,reason_code)
              VALUES (gen_random_uuid(),'mobile',clock_timestamp(),_identity,
                'mobile.coach.feedback.signoff','succeeded',
                v_draft.organization_id,'coach_final_text_approved');

              -- This is authorization evidence, NOT a sent message.
              RETURN jsonb_build_object('id',_id,'organization_id',v_draft.organization_id,
                'player_id',v_draft.player_id,'status','approved','version',2,
                'authorized_by_coach',true,'delivered',false);
            EXCEPTION WHEN no_data_found OR too_many_rows THEN
              RAISE EXCEPTION 'coach_signoff_identity_denied' USING ERRCODE='42501';
            END $coach_signoff$;
            REVOKE ALL ON FUNCTION club.approve_mobile_coach_feedback_draft(
              uuid,text,text,timestamptz,uuid,bigint,text) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.approve_mobile_coach_feedback_draft(
              uuid,text,text,timestamptz,uuid,bigint,text);
            ALTER TABLE club.coach_feedback_drafts
              DROP CONSTRAINT ck_coach_feedback_final_text,
              DROP CONSTRAINT ck_coach_feedback_signoff,
              DROP CONSTRAINT ck_coach_feedback_version,
              DROP CONSTRAINT ck_coach_feedback_status,
              DROP COLUMN approved_by,
              DROP COLUMN approved_at,
              DROP COLUMN final_text;
            ALTER TABLE club.coach_feedback_drafts
              ADD CONSTRAINT ck_coach_feedback_status CHECK (status='draft'),
              ADD CONSTRAINT ck_coach_feedback_version CHECK (version=1);
            """);
}
