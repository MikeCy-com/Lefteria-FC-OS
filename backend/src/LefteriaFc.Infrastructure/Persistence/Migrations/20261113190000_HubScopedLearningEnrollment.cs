using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Exact, per-user weekly lesson enrollment. A fan is eligible only after
/// independently verified Payabl settlement, not checkout or interest alone.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113190000_HubScopedLearningEnrollment")]
public sealed class HubScopedLearningEnrollment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION hub.authorize_mobile_learning_enrollment(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _owner text,_audience text
            ) RETURNS void
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $authorize$
            BEGIN
              IF _identity IS NULL OR _phone IS NULL
                OR _phone !~ '^[1-9][0-9]{7,14}$'
                OR _expires IS NULL OR _expires<=clock_timestamp()
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR NOT (
                  (_role='parent' AND _audience='parent') OR
                  (_role='player' AND _audience='player') OR
                  (_role='fan' AND _audience IN ('fan_parent','fan_player')))
              THEN
                RAISE EXCEPTION 'learning_scope_denied' USING ERRCODE='42501';
              END IF;
              PERFORM 1 FROM identity.mobile_identities i
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role=_role AND i.is_active FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'learning_identity_denied' USING ERRCODE='42501';
              END IF;

              IF _role='fan' THEN
                IF NOT hub.fan_delivery_entitled(
                  _identity,_owner,_audience,clock_timestamp()) THEN
                  RAISE EXCEPTION 'learning_subscription_required' USING ERRCODE='42501';
                END IF;
              ELSIF _role='player' THEN
                PERFORM 1
                  FROM identity.mobile_record_links l
                  JOIN identity.canonical_contact_bindings b
                    ON b.collection=l.collection AND b.kind=l.kind
                      AND b.source_id=l.record_id
                      AND b.normalized_phone=_phone
                  JOIN club.players p ON p.id=l.record_id
                    AND p.organization_id=_owner AND p.is_active
                    AND NOT p.is_archived AND NOT p.is_transferred
                  WHERE l.identity_id=_identity AND l.normalized_phone=_phone
                    AND l.role='player' AND l.collection='players'
                    AND l.kind='player_own' FOR SHARE OF l,b,p;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'learning_roster_denied' USING ERRCODE='42501';
                END IF;
              ELSE
                -- Either the canonical guardian of an active rostered child or
                -- a currently delegated and still valid guardian relationship.
                IF NOT EXISTS (
                  SELECT 1 FROM identity.mobile_record_links l
                  JOIN identity.canonical_contact_bindings b
                    ON b.collection=l.collection AND b.kind='guardian'
                      AND b.source_id=l.record_id
                      AND b.normalized_phone=_phone
                  JOIN club.players p ON p.id=l.record_id
                    AND p.organization_id=_owner AND p.is_active
                    AND NOT p.is_archived AND NOT p.is_transferred
                  WHERE l.identity_id=_identity AND l.normalized_phone=_phone
                    AND l.role='parent' AND l.collection='players'
                    AND l.kind='guardian'
                ) AND NOT EXISTS (
                  SELECT 1 FROM identity.current_phone_granted_children(
                    _identity,_phone,'parent',_expires) grant_row
                  JOIN club.players p ON p.id=grant_row.player_id
                    AND p.organization_id=_owner
                  WHERE grant_row.organization_id=_owner AND p.is_active
                    AND NOT p.is_archived AND NOT p.is_transferred
                ) THEN
                  RAISE EXCEPTION 'learning_guardian_denied' USING ERRCODE='42501';
                END IF;
              END IF;
            END $authorize$;
            REVOKE ALL ON FUNCTION hub.authorize_mobile_learning_enrollment(
              uuid,text,text,timestamptz,text,text) FROM PUBLIC;

            CREATE FUNCTION hub.enroll_mobile_weekly_learning(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _owner text,_audience text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $enroll$
            DECLARE v_row hub.learning_enrollments%ROWTYPE;
                    v_now timestamptz:=clock_timestamp();
            BEGIN
              PERFORM hub.authorize_mobile_learning_enrollment(
                _identity,_phone,_role,_expires,_owner,_audience);
              -- New members start at week one, including after all other
              -- members have advanced. Existing members never reset.
              INSERT INTO hub.learning_enrollments(
                id,identity_id,organization_id,audience,
                started_at,next_due_at,delivery_week,status)
              VALUES(gen_random_uuid(),_identity,_owner,_audience,
                v_now,v_now,1,'active')
              ON CONFLICT (identity_id,organization_id,audience)
              DO NOTHING;
              SELECT * INTO STRICT v_row FROM hub.learning_enrollments
                WHERE identity_id=_identity AND organization_id=_owner
                  AND audience=_audience;
              -- A cancelled track cannot silently re-enable via simple replay.
              RETURN jsonb_build_object(
                'organization_id',_owner,'audience',_audience,
                'started_at',v_row.started_at,
                'next_due_at',v_row.next_due_at,
                'delivery_week',v_row.delivery_week,'status',v_row.status);
            END $enroll$;
            REVOKE ALL ON FUNCTION hub.enroll_mobile_weekly_learning(
              uuid,text,text,timestamptz,text,text) FROM PUBLIC;

            CREATE FUNCTION hub.read_mobile_weekly_learning(
              _identity uuid,_phone text,_role text,_expires timestamptz,
              _owner text,_audience text
            ) RETURNS jsonb
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $read$
            DECLARE v_row hub.learning_enrollments%ROWTYPE; v_items jsonb;
            BEGIN
              PERFORM hub.authorize_mobile_learning_enrollment(
                _identity,_phone,_role,_expires,_owner,_audience);
              SELECT * INTO v_row FROM hub.learning_enrollments
                WHERE identity_id=_identity AND organization_id=_owner
                  AND audience=_audience;
              IF NOT FOUND THEN
                RETURN jsonb_build_object('organization_id',_owner,
                  'audience',_audience,'enrolled',false,'deliveries','[]'::jsonb);
              END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'id',sub.id,'week',sub.delivery_week,
                'content_id',sub.content_id,'title',sub.title,
                'body',sub.body_text,'delivered_at',sub.delivered_at)
                ORDER BY sub.delivery_week DESC),'[]'::jsonb)
              INTO v_items FROM (
                SELECT d.id,d.delivery_week,d.content_id,d.delivered_at,
                  c.title,r.body_text
                FROM hub.learning_delivery_outbox d
                JOIN hub.content c ON c.id=d.content_id
                JOIN hub.revisions r ON r.id=d.revision_id
                  AND r.content_id=c.id
                WHERE d.enrollment_id=v_row.id AND d.state='delivered'
                  AND c.status='published' AND c.approved_by IS NOT NULL
                  AND c.approved_at IS NOT NULL
                  AND r.text_review='passed' AND r.visual_review='passed'
                  AND NOT EXISTS (
                    SELECT 1 FROM hub.media_assets media
                    WHERE media.revision_id=r.id
                      AND (media.moderation<>'reviewed' OR media.kind<>'image'))
                ORDER BY d.delivery_week DESC LIMIT 20
              ) sub;
              RETURN jsonb_build_object(
                'organization_id',_owner,'audience',_audience,
                'enrolled',true,'started_at',v_row.started_at,
                'next_due_at',v_row.next_due_at,
                'delivery_week',v_row.delivery_week,'status',v_row.status,
                'deliveries',v_items);
            END $read$;
            REVOKE ALL ON FUNCTION hub.read_mobile_weekly_learning(
              uuid,text,text,timestamptz,text,text) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION hub.read_mobile_weekly_learning(
              uuid,text,text,timestamptz,text,text);
            DROP FUNCTION hub.enroll_mobile_weekly_learning(
              uuid,text,text,timestamptz,text,text);
            DROP FUNCTION hub.authorize_mobile_learning_enrollment(
              uuid,text,text,timestamptz,text,text);
            """);
}
