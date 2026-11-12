using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Coach sends only a signed-off feedback note to the exact current player
/// identity. Delivery means available in private player inbox, not read or push.
/// No admin, parent, fan or AI sender can call these procedures.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112160000_CoachFeedbackPrivateDelivery")]
public sealed class CoachFeedbackPrivateDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE club.coach_feedback_drafts
              DROP CONSTRAINT ck_coach_feedback_status,
              DROP CONSTRAINT ck_coach_feedback_version,
              DROP CONSTRAINT ck_coach_feedback_signoff;
            ALTER TABLE club.coach_feedback_drafts
              ADD COLUMN recipient_identity_id uuid REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              ADD COLUMN sent_at timestamptz,
              ADD CONSTRAINT ck_coach_feedback_status CHECK (status IN ('draft','approved','sent')),
              ADD CONSTRAINT ck_coach_feedback_version CHECK (version BETWEEN 1 AND 3),
              ADD CONSTRAINT ck_coach_feedback_signoff CHECK (
                (status IN ('approved','sent')) =
                (final_text IS NOT NULL AND approved_at IS NOT NULL AND approved_by IS NOT NULL)),
              ADD CONSTRAINT ck_coach_feedback_send CHECK (
                (status='sent')=(recipient_identity_id IS NOT NULL AND sent_at IS NOT NULL));
            CREATE INDEX ix_coach_feedback_player_inbox
              ON club.coach_feedback_drafts(recipient_identity_id,sent_at DESC,id)
              WHERE status='sent';

            -- Definer-only authority helper; NEVER grant it directly to the app.
            CREATE FUNCTION club.current_feedback_coach_assignment(
              _identity uuid,_phone text,_expires timestamptz,
              _owner text,_staff text,_player text
            ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $coach_can_send$
            DECLARE v_available boolean;
            BEGIN
              SELECT EXISTS (
                SELECT 1 FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l ON l.identity_id=i.id
                  AND l.role='coach' AND l.collection='staff'
                  AND l.kind='staff_own'
                  AND l.normalized_phone=i.normalized_phone
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id
                  AND b.normalized_phone=i.normalized_phone
                JOIN club.staff s ON s.id=l.record_id
                JOIN club.players p ON p.id=_player
                  AND p.organization_id=_owner
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role='coach' AND i.is_active
                  AND _expires>clock_timestamp()
                  AND s.id=_staff AND s.is_active AND NOT s.is_archived
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                  AND (
                    (_owner='lefteria_fc' AND p.team_type='First Team'
                      AND EXISTS (
                        SELECT 1 FROM club.staff_teams st
                        WHERE st.staff_id=s.id AND st.organization_id=_owner
                          AND st.team_id=p.team_id))
                    OR
                    (_owner='lefteria_fc_academy' AND p.team_type='Academy'
                      AND EXISTS (
                        SELECT 1 FROM (
                          SELECT sg.academy_group_id AS group_id
                          FROM club.staff_academy_groups sg
                          WHERE sg.staff_id=s.id AND sg.organization_id=_owner
                            AND (sg.from_plural OR sg.from_singular OR
                              (sg.from_group_coach AND EXISTS (
                                SELECT 1 FROM club.academy_group_coaches gc
                                WHERE gc.staff_id=s.id
                                  AND gc.academy_group_id=sg.academy_group_id
                                  AND gc.organization_id=_owner)))
                          UNION
                          SELECT gc.academy_group_id FROM club.academy_group_coaches gc
                          WHERE gc.staff_id=s.id AND gc.organization_id=_owner
                        ) assigned
                        WHERE assigned.group_id=p.primary_academy_group_id
                          OR EXISTS (
                            SELECT 1 FROM club.player_academy_groups m
                            WHERE m.organization_id=_owner AND m.player_id=p.id
                              AND m.academy_group_id=assigned.group_id)))
                  )
                FOR SHARE OF i,l,b,s,p
              ) INTO v_available;
              RETURN coalesce(v_available,false);
            END $coach_can_send$;
            REVOKE ALL ON FUNCTION club.current_feedback_coach_assignment(
              uuid,text,timestamptz,text,text,text) FROM PUBLIC;

            CREATE FUNCTION club.send_mobile_coach_feedback(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _id uuid,_version bigint
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $coach_send$
            DECLARE
              v_draft club.coach_feedback_drafts%ROWTYPE;
              v_recipient uuid;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
                OR _role IS DISTINCT FROM 'coach' OR _expires IS NULL
                OR _expires<=clock_timestamp() OR _id IS NULL
                OR _version IS DISTINCT FROM 2
              THEN RAISE EXCEPTION 'invalid_coach_send' USING ERRCODE='22023';
              END IF;
              SELECT * INTO v_draft FROM club.coach_feedback_drafts
                WHERE id=_id FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_feedback_draft_not_found' USING ERRCODE='P0002';
              END IF;
              IF v_draft.coach_identity_id<>_identity
                OR v_draft.approved_by<>_identity THEN
                RAISE EXCEPTION 'coach_send_denied' USING ERRCODE='42501';
              END IF;
              IF v_draft.status<>'approved' OR v_draft.version<>_version
                OR v_draft.final_text IS NULL OR v_draft.recipient_identity_id IS NOT NULL
              THEN RAISE EXCEPTION 'coach_send_conflict' USING ERRCODE='40001';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial')
                OR NOT EXISTS (SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial')
              THEN RAISE EXCEPTION 'coach_send_roster_unready' USING ERRCODE='55000';
              END IF;
              IF NOT club.current_feedback_coach_assignment(
                _identity,_phone,_expires,v_draft.organization_id,
                v_draft.staff_id,v_draft.player_id)
              THEN RAISE EXCEPTION 'coach_send_assignment_denied' USING ERRCODE='42501';
              END IF;

              -- Exactly one currently authenticated player-own mobile identity
              -- must match the verified roster source. Ambiguity fails closed.
              SELECT i.id INTO STRICT v_recipient
                FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l
                  ON l.identity_id=i.id AND l.role='player'
                  AND l.normalized_phone=i.normalized_phone
                  AND l.collection='players' AND l.kind='player_own'
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id
                  AND b.normalized_phone=i.normalized_phone
                JOIN club.players p
                  ON p.id=l.record_id AND p.organization_id=v_draft.organization_id
                WHERE i.role='player' AND i.is_active
                  AND p.id=v_draft.player_id
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                FOR SHARE OF i,l,b,p;

              UPDATE club.coach_feedback_drafts
                SET status='sent',version=version+1,
                  recipient_identity_id=v_recipient,sent_at=clock_timestamp()
                WHERE id=_id AND coach_identity_id=_identity
                  AND status='approved' AND version=_version;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'coach_send_conflict' USING ERRCODE='40001';
              END IF;
              INSERT INTO audit.security_events
                (id,actor_realm,occurred_at,actor_identity_id,action,outcome,
                 organization_id,reason_code)
              VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_identity,
                'mobile.coach.feedback.send','succeeded',
                v_draft.organization_id,'private_player_inbox');

              RETURN jsonb_build_object('id',_id,'organization_id',v_draft.organization_id,
                'status','sent','version',3,'inbox_available',true,'read',false);
            EXCEPTION WHEN no_data_found OR too_many_rows THEN
              RAISE EXCEPTION 'coach_send_recipient_unavailable' USING ERRCODE='42501';
            END $coach_send$;
            REVOKE ALL ON FUNCTION club.send_mobile_coach_feedback(
              uuid,text,text,timestamptz,uuid,bigint) FROM PUBLIC;

            CREATE FUNCTION club.read_mobile_player_coach_feedback(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _offset integer
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $player_feedback_inbox$
            DECLARE v_player text; v_items jsonb;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
                OR _role IS DISTINCT FROM 'player' OR _expires IS NULL
                OR _expires<=clock_timestamp()
                OR _offset IS NULL OR _offset<0 OR _offset>10000
              THEN RAISE EXCEPTION 'invalid_player_feedback_scope' USING ERRCODE='42501';
              END IF;
              SELECT p.id INTO STRICT v_player
                FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l ON l.identity_id=i.id
                  AND l.role='player' AND l.normalized_phone=i.normalized_phone
                  AND l.collection='players' AND l.kind='player_own'
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
                JOIN club.players p ON p.id=l.record_id
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role='player' AND i.is_active
                FOR SHARE OF i,l,b,p;
              WITH selected AS (
                SELECT d.id,d.final_text,d.sent_at,d.organization_id,s.name AS coach_name
                  FROM club.coach_feedback_drafts d
                  JOIN club.staff s ON s.id=d.staff_id
                WHERE d.player_id=v_player AND d.recipient_identity_id=_identity
                  AND d.status='sent' AND d.approved_by=d.coach_identity_id
                ORDER BY d.sent_at DESC,d.id DESC LIMIT 20 OFFSET _offset
              )
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',selected.id,'organization_id',selected.organization_id,
                'coach_name',selected.coach_name,'message',selected.final_text,
                'sent_at',selected.sent_at)
                ORDER BY selected.sent_at DESC,selected.id DESC),'[]'::jsonb)
              INTO v_items FROM selected;
              RETURN jsonb_build_object('items',v_items);
            EXCEPTION WHEN no_data_found OR too_many_rows THEN
              RAISE EXCEPTION 'player_feedback_identity_denied' USING ERRCODE='42501';
            END $player_feedback_inbox$;
            REVOKE ALL ON FUNCTION club.read_mobile_player_coach_feedback(
              uuid,text,text,timestamptz,integer) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.read_mobile_player_coach_feedback(
              uuid,text,text,timestamptz,integer);
            DROP FUNCTION club.send_mobile_coach_feedback(
              uuid,text,text,timestamptz,uuid,bigint);
            DROP FUNCTION club.current_feedback_coach_assignment(
              uuid,text,timestamptz,text,text,text);
            DROP INDEX club.ix_coach_feedback_player_inbox;
            ALTER TABLE club.coach_feedback_drafts
              DROP CONSTRAINT ck_coach_feedback_send,
              DROP CONSTRAINT ck_coach_feedback_signoff,
              DROP CONSTRAINT ck_coach_feedback_version,
              DROP CONSTRAINT ck_coach_feedback_status,
              DROP COLUMN sent_at,
              DROP COLUMN recipient_identity_id;
            ALTER TABLE club.coach_feedback_drafts
              ADD CONSTRAINT ck_coach_feedback_status CHECK (status IN ('draft','approved')),
              ADD CONSTRAINT ck_coach_feedback_version CHECK (version BETWEEN 1 AND 2),
              ADD CONSTRAINT ck_coach_feedback_signoff CHECK (
                (status='approved') =
                (final_text IS NOT NULL AND approved_at IS NOT NULL AND approved_by IS NOT NULL));
            """);
}
