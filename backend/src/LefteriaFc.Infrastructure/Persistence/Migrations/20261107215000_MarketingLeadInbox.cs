using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Schema-only security foundation. NO public marketing endpoint or privileged
// reader is enabled by this migration. Data collection remains unavailable.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107215000_MarketingLeadInbox")]
public sealed class MarketingLeadInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS marketing;

            CREATE TABLE marketing.public_leads (
              id uuid PRIMARY KEY,
              organization_id varchar(32) NOT NULL
                REFERENCES club.organizations(id) ON DELETE RESTRICT,
              contact_name varchar(120) NOT NULL,
              phone varchar(32),
              email varchar(254),
              player_age smallint,
              interested_in varchar(120),
              message varchar(2000),
              source varchar(24) NOT NULL,
              partner_slug varchar(64),
              consent_gdpr boolean NOT NULL,
              consent_marketing boolean NOT NULL DEFAULT false,
              consent_recorded_at timestamptz NOT NULL,
              status varchar(16) NOT NULL DEFAULT 'new',
              created_at timestamptz NOT NULL DEFAULT statement_timestamp(),

              CONSTRAINT ck_marketing_lead_organization
                CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
              CONSTRAINT ck_marketing_lead_name
                CHECK (length(btrim(contact_name)) BETWEEN 1 AND 120 AND
                       contact_name = btrim(contact_name)),
              CONSTRAINT ck_marketing_lead_contact
                CHECK (NULLIF(btrim(phone),'') IS NOT NULL OR
                       NULLIF(btrim(email),'') IS NOT NULL),
              CONSTRAINT ck_marketing_lead_age
                CHECK (player_age IS NULL OR player_age BETWEEN 4 AND 99),
              CONSTRAINT ck_marketing_lead_source
                CHECK (source IN ('website','partner_referral')),
              CONSTRAINT ck_marketing_lead_partner
                CHECK ((source='partner_referral') = (partner_slug IS NOT NULL) AND
                       (partner_slug IS NULL OR partner_slug ~
                         '^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$')),
              CONSTRAINT ck_marketing_lead_consent CHECK (consent_gdpr IS TRUE),
              CONSTRAINT ck_marketing_lead_status
                CHECK (status IN ('new','contacted','trial_booked','registered','lost'))
            );

            CREATE INDEX ix_marketing_leads_scope_time
              ON marketing.public_leads (organization_id, created_at DESC, id);
            CREATE INDEX ix_marketing_leads_partner
              ON marketing.public_leads (organization_id, partner_slug)
              WHERE partner_slug IS NOT NULL;

            -- Restrict all access by default, including a later app role grant
            -- until a separately reviewed RLS policy and audited write path
            -- explicitly allow it. No user or test data is inserted.
            REVOKE ALL ON SCHEMA marketing FROM PUBLIC;
            REVOKE ALL ON TABLE marketing.public_leads FROM PUBLIC;
            ALTER TABLE marketing.public_leads ENABLE ROW LEVEL SECURITY;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE marketing.public_leads;
            -- Leave schema in place to avoid dropping unrelated later modules.
            """);
    }
}
