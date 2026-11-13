using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private, fail-closed scheduling foundation. No runtime grants, payment
/// collection, fan activation or outbound notification worker are installed.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113180000_HubScheduledDeliveryFoundation")]
public sealed class HubScheduledDeliveryFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            -- A member's delivery clock starts at registration/enrolment,
            -- not at the publication time of a shared lesson.
            CREATE TABLE hub.learning_enrollments (
              id uuid PRIMARY KEY,
              identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              audience varchar(12) NOT NULL,
              started_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              next_due_at timestamptz NOT NULL,
              delivery_week integer NOT NULL DEFAULT 1,
              status varchar(12) NOT NULL DEFAULT 'paused',
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_enrollment_owner CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_hub_enrollment_audience CHECK (audience IN ('parent','player','coach','fan_parent','fan_player')),
              CONSTRAINT ck_hub_enrollment_week CHECK (delivery_week BETWEEN 1 AND 5200),
              CONSTRAINT ck_hub_enrollment_status CHECK (status IN ('paused','active','cancelled')),
              CONSTRAINT ck_hub_enrollment_due CHECK (next_due_at>=started_at),
              CONSTRAINT uq_hub_enrollment_identity_audience UNIQUE (identity_id,organization_id,audience)
            );
            CREATE TABLE hub.learning_delivery_outbox (
              id uuid PRIMARY KEY,
              enrollment_id uuid NOT NULL REFERENCES hub.learning_enrollments(id) ON DELETE RESTRICT,
              content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
              revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
              delivery_week integer NOT NULL,
              due_at timestamptz NOT NULL,
              channel varchar(12) NOT NULL DEFAULT 'in_app',
              state varchar(16) NOT NULL DEFAULT 'pending',
              attempt_count smallint NOT NULL DEFAULT 0,
              delivered_at timestamptz,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_delivery_week CHECK (delivery_week BETWEEN 1 AND 5200),
              CONSTRAINT ck_hub_delivery_channel CHECK (channel='in_app'),
              CONSTRAINT ck_hub_delivery_state CHECK (state IN ('pending','delivered','cancelled')),
              CONSTRAINT ck_hub_delivery_attempts CHECK (attempt_count BETWEEN 0 AND 10),
              CONSTRAINT ck_hub_delivery_timestamp CHECK ((state='delivered')=(delivered_at IS NOT NULL)),
              CONSTRAINT uq_hub_delivery_week UNIQUE (enrollment_id,delivery_week,channel),
              CONSTRAINT uq_hub_delivery_content UNIQUE (enrollment_id,content_id,channel)
            );
            CREATE INDEX ix_hub_delivery_due ON hub.learning_delivery_outbox(due_at,id)
              WHERE state='pending';
            CREATE INDEX ix_hub_enrollment_due ON hub.learning_enrollments(next_due_at,id)
              WHERE status='active';

            -- Eligibility is recalculated against the live, verified Payabl
            -- subscription at dispatch time. A fan interest selection or
            -- a browser checkout redirect cannot authorize delivery.
            CREATE FUNCTION hub.fan_delivery_entitled(
              _identity uuid,_owner text,_audience text,_at timestamptz
            ) RETURNS boolean
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $fan_entitlement$
              SELECT _identity IS NOT NULL
                AND _owner IN ('lefteria_fc','lefteria_fc_academy')
                AND _audience IN ('fan_parent','fan_player')
                AND _at IS NOT NULL
                AND EXISTS (
                  SELECT 1 FROM identity.mobile_identities i
                  JOIN hub.fan_subscriptions s ON s.subscriber_identity_id=i.id
                  WHERE i.id=_identity AND i.is_active AND i.role='fan'
                    AND s.organization_id=_owner AND s.provider='payabl'
                    AND s.status='active'
                    AND s.provider_reference IS NOT NULL
                    AND length(s.provider_reference)>0
                    AND s.verified_at IS NOT NULL AND s.verified_at<=_at
                    AND s.expires_at>_at AND s.cancelled_at IS NULL
                    AND ((_audience='fan_parent' AND s.audience_parent) OR
                         (_audience='fan_player' AND s.audience_player))
                );
            $fan_entitlement$;

            -- No worker, user or public role can call this until reviewed
            -- reconciliation and delivery workers are installed.
            REVOKE ALL ON TABLE hub.learning_enrollments FROM PUBLIC;
            REVOKE ALL ON TABLE hub.learning_delivery_outbox FROM PUBLIC;
            REVOKE ALL ON FUNCTION hub.fan_delivery_entitled(uuid,text,text,timestamptz) FROM PUBLIC;
            ALTER TABLE hub.learning_enrollments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hub.learning_delivery_outbox ENABLE ROW LEVEL SECURITY;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DO $guard$
            BEGIN
              IF EXISTS(SELECT 1 FROM hub.learning_enrollments)
                OR EXISTS(SELECT 1 FROM hub.learning_delivery_outbox) THEN
                RAISE EXCEPTION 'Refusing to delete enrolled members or delivery history';
              END IF;
            END $guard$;
            DROP FUNCTION hub.fan_delivery_entitled(uuid,text,text,timestamptz);
            DROP TABLE hub.learning_delivery_outbox;
            DROP TABLE hub.learning_enrollments;
            """);
}
