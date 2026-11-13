using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Fan education is read only after capture-verified Payabl entitlement.
/// Each subscribed interest has independent, persistent completion history.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113171000_HubFanPersonalEducation")]
public sealed class HubFanPersonalEducation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          CREATE TABLE hub.fan_learning_completions (
            identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id),
            organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
            audience varchar(8) NOT NULL CHECK(audience IN ('parent','player')),
            content_id uuid NOT NULL REFERENCES hub.content(id),
            revision_id uuid NOT NULL REFERENCES hub.revisions(id),
            completed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(identity_id,organization_id,audience,content_id,revision_id)
          );
          ALTER TABLE hub.fan_learning_completions ENABLE ROW LEVEL SECURITY;
          REVOKE ALL ON hub.fan_learning_completions FROM PUBLIC;

          CREATE FUNCTION hub.current_mobile_fan_lesson(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_lesson$
          DECLARE v_item jsonb;
          BEGIN
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
              OR _audience NOT IN ('parent','player') THEN
              RAISE EXCEPTION 'fan_lesson_invalid' USING ERRCODE='42501';
            END IF;
            PERFORM hub.sync_mobile_fan_payabl(_identity,_owner);
            -- Even an active subscription row must also have an original
            -- payment backed by the signed-callback finance ledger.
            PERFORM 1 FROM hub.fan_subscriptions s
              JOIN hub.fan_checkout_links link ON link.subscription_id=s.id
              JOIN finance.payment_requests p ON p.id=link.payment_request_id
              WHERE s.subscriber_identity_id=_identity
                AND s.organization_id=_owner AND s.status='active'
                AND s.cancelled_at IS NULL
                AND s.verified_at IS NOT NULL
                AND s.expires_at>clock_timestamp()
                AND s.provider='payabl'
                AND ((_audience='parent' AND s.audience_parent) OR
                     (_audience='player' AND s.audience_player))
                AND p.actor_id=_identity AND p.organization_id=_owner
                AND p.status='paid' AND p.captured_transaction_id IS NOT NULL
                AND p.captured_transaction_id=s.provider_reference
              FOR SHARE OF s,p;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_subscription_required' USING ERRCODE='42501';
            END IF;
            SELECT jsonb_build_object(
              'id',c.id,'revision_id',r.id,
              'organization_id',c.organization_id,'audience',_audience,
              'title',c.title,'body',r.body_text,'published_at',c.published_at)
            INTO v_item
            FROM hub.content c
            JOIN LATERAL (
              SELECT rr.id,rr.body_text,rr.text_review,rr.visual_review
              FROM hub.revisions rr WHERE rr.content_id=c.id
              ORDER BY rr.revision DESC LIMIT 1
            ) r ON true
            WHERE c.organization_id=_owner AND c.kind='article'
              AND c.status='published' AND c.approved_by IS NOT NULL
              AND c.approved_at IS NOT NULL
              AND c.published_at IS NOT NULL
              AND c.published_at<=clock_timestamp()
              AND r.text_review='passed' AND r.visual_review='passed'
              AND c.age_min IS NULL AND c.age_max IS NULL
              AND ((_audience='parent' AND c.audience_parent) OR
                   (_audience='player' AND c.audience_player))
              AND NOT EXISTS (
                SELECT 1 FROM hub.media_assets m WHERE m.revision_id=r.id
                  AND (m.kind<>'image' OR m.moderation<>'reviewed'))
              AND NOT EXISTS (
                SELECT 1 FROM hub.fan_learning_completions done
                WHERE done.identity_id=_identity
                  AND done.organization_id=_owner
                  AND done.audience=_audience AND done.content_id=c.id
                  AND done.revision_id=r.id)
            ORDER BY c.published_at ASC,c.id ASC LIMIT 1;
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_audience,'next',v_item);
          END $fan_lesson$;
          REVOKE ALL ON FUNCTION hub.current_mobile_fan_lesson(
            uuid,text,text,timestamptz,text,text) FROM PUBLIC;

          CREATE FUNCTION hub.read_mobile_fan_next_lesson(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_read$
          BEGIN
            RETURN hub.current_mobile_fan_lesson(
              _identity,_phone,_role,_expires,_owner,_audience);
          END $fan_read$;
          REVOKE ALL ON FUNCTION hub.read_mobile_fan_next_lesson(
            uuid,text,text,timestamptz,text,text) FROM PUBLIC;

          CREATE FUNCTION hub.complete_mobile_fan_next_lesson(
            _identity uuid,_phone text,_role text,_expires timestamptz,
            _owner text,_audience text,_content uuid,_revision uuid
          ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp AS $fan_complete$
          DECLARE v_next jsonb; v_item jsonb;
          BEGIN
            IF _content IS NULL OR _revision IS NULL THEN
              RAISE EXCEPTION 'invalid_lesson_completion' USING ERRCODE='22023';
            END IF;
            PERFORM hub.assert_mobile_fan(_identity,_phone,_role,_expires);
            PERFORM 1 FROM identity.mobile_identities
              WHERE id=_identity AND normalized_phone=_phone AND is_active
              FOR UPDATE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'fan_identity_denied' USING ERRCODE='42501';
            END IF;
            v_next:=hub.current_mobile_fan_lesson(
              _identity,_phone,_role,_expires,_owner,_audience);
            v_item:=v_next->'next';
            IF v_item IS NULL OR v_item='null'::jsonb
              OR (v_item->>'id')::uuid<>_content
              OR (v_item->>'revision_id')::uuid<>_revision THEN
              RAISE EXCEPTION 'fan_lesson_conflict' USING ERRCODE='40001';
            END IF;
            INSERT INTO hub.fan_learning_completions(
              identity_id,organization_id,audience,content_id,revision_id)
              VALUES(_identity,_owner,_audience,_content,_revision);
            RETURN jsonb_build_object('organization_id',_owner,
              'audience',_audience,'completed_id',_content,
              'completed_revision_id',_revision);
          END $fan_complete$;
          REVOKE ALL ON FUNCTION hub.complete_mobile_fan_next_lesson(
            uuid,text,text,timestamptz,text,text,uuid,uuid) FROM PUBLIC;
          """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          DROP FUNCTION hub.complete_mobile_fan_next_lesson(
            uuid,text,text,timestamptz,text,text,uuid,uuid);
          DROP FUNCTION hub.read_mobile_fan_next_lesson(
            uuid,text,text,timestamptz,text,text);
          DROP FUNCTION hub.current_mobile_fan_lesson(
            uuid,text,text,timestamptz,text,text);
          DROP TABLE hub.fan_learning_completions;
          """);
}
