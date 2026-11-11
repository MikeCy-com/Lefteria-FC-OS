using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private schema only. No grants, reader, editor, generation worker or billing
/// endpoint is enabled by this migration. Publication and subscription status
/// must be changed only by separately reviewed transactional SQL functions.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110180000_HubPrivateFoundation")]
public sealed class HubPrivateFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS hub;
            REVOKE ALL ON SCHEMA hub FROM PUBLIC;

            CREATE TABLE hub.content (
              id uuid PRIMARY KEY,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              kind varchar(20) NOT NULL,
              status varchar(20) NOT NULL DEFAULT 'ai_draft',
              title varchar(180) NOT NULL,
              audience_parent boolean NOT NULL DEFAULT false,
              audience_player boolean NOT NULL DEFAULT false,
              age_min smallint,
              age_max smallint,
              provenance varchar(12) NOT NULL,
              version bigint NOT NULL DEFAULT 1,
              approved_by uuid,
              approved_at timestamptz,
              published_at timestamptz,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              updated_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_content_scope CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_hub_content_kind CHECK (kind IN ('article','drill','video','visual','course')),
              CONSTRAINT ck_hub_content_state CHECK (status IN ('ai_draft','review_required','approved','published','rejected')),
              CONSTRAINT ck_hub_content_provenance CHECK (provenance IN ('ai','human')),
              CONSTRAINT ck_hub_content_title CHECK (length(btrim(title)) BETWEEN 1 AND 180 AND title=btrim(title)),
              CONSTRAINT ck_hub_content_audiences CHECK (audience_parent OR audience_player),
              CONSTRAINT ck_hub_content_ages CHECK (
                (age_min IS NULL AND age_max IS NULL) OR
                (age_min BETWEEN 4 AND 99 AND age_max BETWEEN age_min AND 99)),
              CONSTRAINT ck_hub_content_version CHECK (version>=1),
              CONSTRAINT ck_hub_content_approval CHECK (
                (status IN ('approved','published')) = (approved_by IS NOT NULL AND approved_at IS NOT NULL)),
              CONSTRAINT ck_hub_content_publication CHECK ((status='published') = (published_at IS NOT NULL))
            );

            CREATE TABLE hub.revisions (
              id uuid PRIMARY KEY,
              content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
              revision bigint NOT NULL,
              body_text text NOT NULL,
              text_review varchar(12) NOT NULL DEFAULT 'pending',
              visual_review varchar(12) NOT NULL DEFAULT 'pending',
              source_provenance text,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT uq_hub_content_revision UNIQUE(content_id,revision),
              CONSTRAINT ck_hub_revision_num CHECK (revision>=1),
              CONSTRAINT ck_hub_revision_text CHECK (length(body_text) BETWEEN 1 AND 20000),
              CONSTRAINT ck_hub_revision_reviews CHECK (
                text_review IN ('pending','passed','rejected') AND
                visual_review IN ('pending','passed','rejected')),
              CONSTRAINT ck_hub_revision_provenance CHECK (source_provenance IS NULL OR length(source_provenance)<=2000)
            );

            CREATE TABLE hub.media_assets (
              id uuid PRIMARY KEY,
              revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
              kind varchar(12) NOT NULL,
              storage_key varchar(200) NOT NULL UNIQUE,
              sha256 varchar(64) NOT NULL,
              mime_type varchar(48) NOT NULL,
              byte_count bigint NOT NULL,
              alt_text varchar(500),
              moderation varchar(16) NOT NULL DEFAULT 'quarantined',
              generated_by_ai boolean NOT NULL,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_media_kind CHECK (kind IN ('image','video','attachment')),
              CONSTRAINT ck_hub_media_sha CHECK (sha256 ~ '^[0-9a-f]{64}$'),
              CONSTRAINT ck_hub_media_bytes CHECK (byte_count BETWEEN 1 AND 524288000),
              CONSTRAINT ck_hub_media_moderation CHECK (moderation IN ('quarantined','reviewed','rejected'))
            );

            CREATE TABLE hub.review_events (
              id uuid PRIMARY KEY,
              content_id uuid NOT NULL REFERENCES hub.content(id) ON DELETE RESTRICT,
              revision_id uuid NOT NULL REFERENCES hub.revisions(id) ON DELETE RESTRICT,
              admin_account_id uuid,
              action varchar(24) NOT NULL,
              occurred_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
              CONSTRAINT ck_hub_review_action CHECK (action IN (
                'generated','submitted','text_reviewed','visual_reviewed','approved','rejected','published','revised')),
              CONSTRAINT ck_hub_review_admin CHECK (
                (action IN ('approved','rejected','published')) = (admin_account_id IS NOT NULL)),
              CONSTRAINT ck_hub_review_metadata CHECK (jsonb_typeof(metadata)='object')
            );

            CREATE TABLE hub.fan_subscriptions (
              id uuid PRIMARY KEY,
              subscriber_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              organization_id varchar(32) NOT NULL REFERENCES club.organizations(id) ON DELETE RESTRICT,
              status varchar(16) NOT NULL DEFAULT 'pending',
              audience_parent boolean NOT NULL DEFAULT false,
              audience_player boolean NOT NULL DEFAULT false,
              provider varchar(16) NOT NULL,
              provider_reference varchar(180),
              verified_at timestamptz,
              expires_at timestamptz,
              cancelled_at timestamptz,
              consent_at timestamptz NOT NULL,
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_hub_sub_scope CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_hub_sub_status CHECK (status IN ('pending','active','past_due','cancelled','expired')),
              CONSTRAINT ck_hub_sub_provider CHECK (provider='payabl'),
              CONSTRAINT ck_hub_sub_interest CHECK (audience_parent OR audience_player),
              CONSTRAINT ck_hub_sub_verified CHECK (status<>'active' OR (
                provider_reference IS NOT NULL AND length(provider_reference)>0 AND
                verified_at IS NOT NULL AND expires_at IS NOT NULL AND expires_at>verified_at))
            );

            CREATE UNIQUE INDEX ux_hub_subscription_subscriber_owner_active
              ON hub.fan_subscriptions(subscriber_identity_id,organization_id)
              WHERE status IN ('pending','active','past_due');
            CREATE INDEX ix_hub_content_owner_status
              ON hub.content(organization_id,status,updated_at DESC,id);
            CREATE INDEX ix_hub_revisions_content ON hub.revisions(content_id,revision DESC);
            CREATE INDEX ix_hub_reviews_content ON hub.review_events(content_id,occurred_at DESC);
            CREATE INDEX ix_hub_subscription_expiry ON hub.fan_subscriptions(status,expires_at)
              WHERE status='active';

            -- Deliberately no reader or writer grants and no automatic policies.
            -- The runtime account cannot see even published text until verified
            -- owner/role/entitlement SQL functions and RLS policies are installed.
            REVOKE ALL ON ALL TABLES IN SCHEMA hub FROM PUBLIC;
            ALTER TABLE hub.content ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hub.revisions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hub.media_assets ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hub.review_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hub.fan_subscriptions ENABLE ROW LEVEL SECURITY;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Explicit, destructive rollback only. Regular startup never migrates.
        migrationBuilder.Sql("""
            DROP TABLE hub.fan_subscriptions;
            DROP TABLE hub.review_events;
            DROP TABLE hub.media_assets;
            DROP TABLE hub.revisions;
            DROP TABLE hub.content;
            """);
    }
}
