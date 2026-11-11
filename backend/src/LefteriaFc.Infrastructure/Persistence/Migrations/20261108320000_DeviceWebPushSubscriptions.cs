using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Explicit device opt-in, private subscription keys and a bounded, leased,
/// deduplicated per-announcement/subscription push-service attempt ledger.
/// This migration never sends a push. Rollback only in disposable CI.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108320000_DeviceWebPushSubscriptions")]
public sealed class DeviceWebPushSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.mobile_push_subscriptions (
          id uuid PRIMARY KEY,
          identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          endpoint_hash varchar(64) COLLATE "C" NOT NULL UNIQUE
            CHECK(endpoint_hash ~ '^[0-9a-f]{64}$'),
          endpoint varchar(2048) NOT NULL,
          p256dh varchar(128) NOT NULL,
          auth_secret varchar(64) NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          revoked_at timestamptz
        );
        CREATE INDEX ix_mobile_push_subscription_owner
          ON club.mobile_push_subscriptions(identity_id,revoked_at);
        REVOKE ALL ON TABLE club.mobile_push_subscriptions FROM PUBLIC;

        CREATE TABLE club.app_announcement_push_attempts (
          id uuid PRIMARY KEY,
          announcement_id varchar(128) COLLATE "C" NOT NULL
            REFERENCES club.app_announcements(id) ON DELETE RESTRICT,
          subscription_id uuid NOT NULL
            REFERENCES club.mobile_push_subscriptions(id) ON DELETE RESTRICT,
          status text NOT NULL DEFAULT 'pending'
            CHECK(status IN ('pending','leased','accepted','gone','rejected','failed','cancelled')),
          attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),
          lease_token uuid,
          lease_until timestamptz,
          next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          last_http_status integer CHECK(last_http_status IS NULL OR last_http_status BETWEEN 100 AND 599),
          accepted_at timestamptz,
          updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(announcement_id,subscription_id)
        );
        CREATE INDEX ix_app_push_attempts_claim
          ON club.app_announcement_push_attempts(next_attempt_at,id)
          WHERE status IN ('pending','leased');
        REVOKE ALL ON TABLE club.app_announcement_push_attempts FROM PUBLIC;

        CREATE FUNCTION club.save_mobile_push_subscription(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _hash text,_endpoint text,_p256dh text,_auth text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_subscribe$
        DECLARE v_id uuid;v_existing uuid;v_existing_revoked boolean;v_count integer;
        BEGIN
          IF _hash IS NULL OR _hash !~ '^[0-9a-f]{64}$'
            OR _endpoint IS NULL OR length(_endpoint) NOT BETWEEN 24 AND 2048
            OR _endpoint !~ '^https://[A-Za-z0-9.-]+/'
            OR _p256dh IS NULL OR length(_p256dh) NOT BETWEEN 80 AND 128
            OR _p256dh !~ '^[A-Za-z0-9_-]+$'
            OR _auth IS NULL OR length(_auth) NOT BETWEEN 16 AND 64
            OR _auth !~ '^[A-Za-z0-9_-]+$' THEN
            RAISE EXCEPTION 'push_invalid' USING ERRCODE='22023';
          END IF;
          -- The canonical roster and profile preference are checked on opt-in.
          PERFORM club.read_mobile_chat_groups(_actor,_phone,_role,_expires);
          PERFORM 1 FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp()
            AND NOT coalesce(i.notification_preferences @>
              '{"administrative":false}'::jsonb,false)
          FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'push_denied' USING ERRCODE='42501';
          END IF;
          SELECT id,(revoked_at IS NOT NULL)
          INTO v_existing,v_existing_revoked
          FROM club.mobile_push_subscriptions
          WHERE endpoint_hash=_hash FOR UPDATE;
          IF v_existing IS NOT NULL AND NOT EXISTS(
            SELECT 1 FROM club.mobile_push_subscriptions s
            WHERE s.id=v_existing AND s.identity_id=_actor) THEN
            RAISE EXCEPTION 'push_denied' USING ERRCODE='42501';
          END IF;
          SELECT count(*)::integer INTO v_count
          FROM club.mobile_push_subscriptions
          WHERE identity_id=_actor AND revoked_at IS NULL;
          IF (v_existing IS NULL OR v_existing_revoked) AND v_count>=3 THEN
            RAISE EXCEPTION 'push_limit' USING ERRCODE='54000';
          END IF;
          IF v_existing IS NULL THEN
            v_id:=gen_random_uuid();
            INSERT INTO club.mobile_push_subscriptions(
              id,identity_id,endpoint_hash,endpoint,p256dh,auth_secret)
            VALUES(v_id,_actor,_hash,_endpoint,_p256dh,_auth);
          ELSE
            v_id:=v_existing;
            UPDATE club.mobile_push_subscriptions SET
              endpoint=_endpoint,p256dh=_p256dh,auth_secret=_auth,
              created_at=CASE WHEN revoked_at IS NULL THEN created_at
                ELSE clock_timestamp() END,
              revoked_at=NULL,last_seen_at=clock_timestamp()
            WHERE id=v_id AND identity_id=_actor;
          END IF;
          -- Returned state contains no push URL, auth secret or key material.
          RETURN jsonb_build_object('subscribed',true);
        END $push_subscribe$;
        REVOKE ALL ON FUNCTION club.save_mobile_push_subscription(
          uuid,text,text,timestamptz,text,text,text,text) FROM PUBLIC;

        CREATE FUNCTION club.remove_mobile_push_subscription(
          _actor uuid,_phone text,_role text,_expires timestamptz,_hash text)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_unsubscribe$
        BEGIN
          IF _hash IS NULL OR _hash !~ '^[0-9a-f]{64}$' THEN
            RAISE EXCEPTION 'push_invalid' USING ERRCODE='22023';
          END IF;
          PERFORM 1 FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp();
          IF NOT FOUND THEN
            RAISE EXCEPTION 'push_denied' USING ERRCODE='42501';
          END IF;
          UPDATE club.mobile_push_subscriptions
          SET revoked_at=coalesce(revoked_at,clock_timestamp())
          WHERE identity_id=_actor AND endpoint_hash=_hash;
          RETURN jsonb_build_object('subscribed',false);
        END $push_unsubscribe$;
        REVOKE ALL ON FUNCTION club.remove_mobile_push_subscription(
          uuid,text,text,timestamptz,text) FROM PUBLIC;

        CREATE FUNCTION club.mobile_push_subscription_status(
          _actor uuid,_phone text,_role text,_expires timestamptz)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_status$
        DECLARE v_count integer;
        BEGIN
          PERFORM 1 FROM identity.mobile_identities i
          WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role=_role
            AND i.is_active AND _expires>statement_timestamp();
          IF NOT FOUND THEN RAISE EXCEPTION 'push_denied' USING ERRCODE='42501'; END IF;
          SELECT count(*)::integer INTO v_count
          FROM club.mobile_push_subscriptions s
          WHERE s.identity_id=_actor AND s.revoked_at IS NULL;
          RETURN jsonb_build_object('active_devices',v_count);
        END $push_status$;
        REVOKE ALL ON FUNCTION club.mobile_push_subscription_status(
          uuid,text,text,timestamptz) FROM PUBLIC;

        -- Only a deployment-configured worker calls this. It cannot enumerate
        -- people, names or app contents and never returns a notice title.
        -- Recompute the roster, preference and read status before every lease.
        CREATE FUNCTION club.claim_app_announcement_push_attempts(_limit integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_claim$
        DECLARE v_batch jsonb;
        BEGIN
          IF _limit IS NULL OR _limit NOT BETWEEN 1 AND 15 THEN
            RAISE EXCEPTION 'push_invalid' USING ERRCODE='22023';
          END IF;
          -- Bound backfill. An existing in-app notice older than 48h never
          -- generates a new remote push, and old events are not pushed to
          -- devices registered after the publication.
          INSERT INTO club.app_announcement_push_attempts(
            id,announcement_id,subscription_id)
          SELECT gen_random_uuid(),e.announcement_id,s.id
          FROM club.app_announcement_notification_events e
          JOIN club.app_announcements a ON a.id=e.announcement_id
          JOIN club.mobile_push_subscriptions s
            ON s.created_at<=e.queued_at
          JOIN identity.mobile_identities i ON i.id=s.identity_id
          WHERE e.queued_at>clock_timestamp()-interval '48 hours'
            AND a.removed_at IS NULL AND s.revoked_at IS NULL
            AND s.last_seen_at>clock_timestamp()-interval '30 days'
            AND i.is_active AND i.role<>'fan'
            AND NOT coalesce(i.notification_preferences @>
              '{"administrative":false}'::jsonb,false)
            AND NOT EXISTS(
              SELECT 1 FROM club.app_announcement_read_receipts r
              WHERE r.announcement_id=a.id AND r.reader_identity_id=i.id)
            AND club.is_current_mobile_chat_reader(
              i.id,a.organization_id,a.group_id)
            AND NOT EXISTS(
              SELECT 1 FROM club.app_announcement_push_attempts prior
              WHERE prior.announcement_id=e.announcement_id
                AND prior.subscription_id=s.id)
          ORDER BY e.queued_at DESC,s.id
          LIMIT 150
          ON CONFLICT(announcement_id,subscription_id) DO NOTHING;

          UPDATE club.app_announcement_push_attempts d
          SET status='cancelled',updated_at=clock_timestamp(),lease_until=NULL
          FROM club.app_announcements a
          WHERE a.id=d.announcement_id AND d.status IN ('pending','leased')
            AND (a.removed_at IS NOT NULL OR a.published_at<
              clock_timestamp()-interval '48 hours');
          -- Atomic row-level leases avoid parallel workers issuing the same
          -- notification concurrently. Retry expired leases at most 5 times.
          WITH selected AS (
            SELECT d.id FROM club.app_announcement_push_attempts d
            JOIN club.app_announcements a ON a.id=d.announcement_id
            JOIN club.mobile_push_subscriptions s ON s.id=d.subscription_id
            JOIN identity.mobile_identities i ON i.id=s.identity_id
            WHERE d.status IN ('pending','leased')
              AND d.next_attempt_at<=clock_timestamp()
              AND (d.lease_until IS NULL OR d.lease_until<clock_timestamp())
              AND d.attempts<5 AND a.removed_at IS NULL
              AND a.published_at>clock_timestamp()-interval '48 hours'
              AND s.revoked_at IS NULL
              AND s.last_seen_at>clock_timestamp()-interval '30 days'
              AND i.is_active AND i.role<>'fan'
              AND NOT coalesce(i.notification_preferences @>
                '{"administrative":false}'::jsonb,false)
              AND NOT EXISTS(
                SELECT 1 FROM club.app_announcement_read_receipts r
                WHERE r.announcement_id=a.id AND r.reader_identity_id=i.id)
              AND club.is_current_mobile_chat_reader(
                i.id,a.organization_id,a.group_id)
            ORDER BY d.next_attempt_at,d.id
            FOR UPDATE OF d SKIP LOCKED LIMIT _limit
          ), updated AS (
            UPDATE club.app_announcement_push_attempts d
            SET status='leased',lease_token=gen_random_uuid(),
              lease_until=clock_timestamp()+interval '2 minutes',
              attempts=d.attempts+1,updated_at=clock_timestamp()
            WHERE d.id IN (SELECT id FROM selected)
            RETURNING d.id,d.lease_token,d.attempts,d.subscription_id
          )
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',u.id,'lease_token',u.lease_token,'attempts',u.attempts,
            'endpoint',s.endpoint)),'[]'::jsonb)
          INTO v_batch
          FROM updated u JOIN club.mobile_push_subscriptions s
            ON s.id=u.subscription_id;
          RETURN jsonb_build_object('deliveries',v_batch);
        END $push_claim$;
        REVOKE ALL ON FUNCTION club.claim_app_announcement_push_attempts(integer)
          FROM PUBLIC;

        CREATE FUNCTION club.check_app_announcement_push_lease(
          _delivery uuid,_lease uuid)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_recheck$
        BEGIN
          RETURN EXISTS(
            SELECT 1 FROM club.app_announcement_push_attempts d
            JOIN club.app_announcements a ON a.id=d.announcement_id
            JOIN club.mobile_push_subscriptions s ON s.id=d.subscription_id
            JOIN identity.mobile_identities i ON i.id=s.identity_id
            WHERE d.id=_delivery AND d.lease_token=_lease
              AND d.status='leased' AND d.lease_until>clock_timestamp()
              AND a.removed_at IS NULL
              AND a.published_at>clock_timestamp()-interval '48 hours'
              AND s.revoked_at IS NULL
              AND s.last_seen_at>clock_timestamp()-interval '30 days'
              AND i.is_active AND i.role<>'fan'
              AND NOT coalesce(i.notification_preferences @>
                '{"administrative":false}'::jsonb,false)
              AND NOT EXISTS(
                SELECT 1 FROM club.app_announcement_read_receipts r
                WHERE r.announcement_id=a.id AND r.reader_identity_id=i.id)
              AND club.is_current_mobile_chat_reader(
                i.id,a.organization_id,a.group_id));
        END $push_recheck$;
        REVOKE ALL ON FUNCTION club.check_app_announcement_push_lease(uuid,uuid)
          FROM PUBLIC;

        CREATE FUNCTION club.finish_app_announcement_push_attempt(
          _delivery uuid,_lease uuid,_result text,_http integer)
        RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $push_finish$
        DECLARE v_sub uuid;v_status text;v_attempts integer;
        BEGIN
          IF _result IS NULL
            OR _result NOT IN ('accepted','gone','rejected','retry','cancelled')
            OR (_http IS NOT NULL AND _http NOT BETWEEN 100 AND 599) THEN
            RAISE EXCEPTION 'push_invalid' USING ERRCODE='22023';
          END IF;
          SELECT subscription_id,attempts INTO v_sub,v_attempts
          FROM club.app_announcement_push_attempts
          WHERE id=_delivery AND lease_token=_lease AND status='leased'
            AND lease_until>clock_timestamp()
          FOR UPDATE;
          IF NOT FOUND THEN RETURN false; END IF;
          v_status:=CASE WHEN _result='retry'
            THEN CASE WHEN v_attempts>=5 THEN 'failed' ELSE 'pending' END
            ELSE _result END;
          UPDATE club.app_announcement_push_attempts
          SET status=v_status,lease_token=NULL,lease_until=NULL,
            last_http_status=_http,updated_at=clock_timestamp(),
            accepted_at=CASE WHEN v_status='accepted' THEN clock_timestamp()
              ELSE accepted_at END,
            next_attempt_at=CASE WHEN v_status='pending'
              THEN clock_timestamp()+make_interval(secs=>least(3600,
                30*power(2,v_attempts-1)::integer))
              ELSE next_attempt_at END
          WHERE id=_delivery;
          IF _result='gone' THEN
            UPDATE club.mobile_push_subscriptions
            SET revoked_at=coalesce(revoked_at,clock_timestamp())
            WHERE id=v_sub;
          END IF;
          RETURN true;
        END $push_finish$;
        REVOKE ALL ON FUNCTION club.finish_app_announcement_push_attempt(
          uuid,uuid,text,integer) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.finish_app_announcement_push_attempt(
          uuid,uuid,text,integer);
        DROP FUNCTION club.check_app_announcement_push_lease(uuid,uuid);
        DROP FUNCTION club.claim_app_announcement_push_attempts(integer);
        DROP FUNCTION club.mobile_push_subscription_status(
          uuid,text,text,timestamptz);
        DROP FUNCTION club.remove_mobile_push_subscription(
          uuid,text,text,timestamptz,text);
        DROP FUNCTION club.save_mobile_push_subscription(
          uuid,text,text,timestamptz,text,text,text,text);
        DROP TABLE club.app_announcement_push_attempts;
        DROP TABLE club.mobile_push_subscriptions;
        """);
}
