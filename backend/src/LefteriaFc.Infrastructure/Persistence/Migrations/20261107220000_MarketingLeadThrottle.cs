using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Private infrastructure for the future explicit-consent Academy lead write.
// No execution permission, public endpoint, user data, or automatic cleanup.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107220000_MarketingLeadThrottle")]
public sealed class MarketingLeadThrottle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE marketing.lead_rate_buckets (
              fingerprint varchar(64) PRIMARY KEY,
              window_started_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              attempts smallint NOT NULL DEFAULT 1,
              last_attempt_at timestamptz NOT NULL DEFAULT statement_timestamp(),
              CONSTRAINT ck_marketing_bucket_fingerprint
                CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
              CONSTRAINT ck_marketing_bucket_attempts
                CHECK (attempts BETWEEN 1 AND 5),
              CONSTRAINT ck_marketing_bucket_order
                CHECK (last_attempt_at >= window_started_at)
            );
            CREATE INDEX ix_marketing_rate_buckets_expiry
              ON marketing.lead_rate_buckets (window_started_at);

            REVOKE ALL ON TABLE marketing.lead_rate_buckets FROM PUBLIC;
            ALTER TABLE marketing.lead_rate_buckets ENABLE ROW LEVEL SECURITY;
            -- No permissive RLS policy or application grants: any eventual
            -- throttle mutation must be authorized by a separately tested,
            -- fixed-search-path server-controlled stored operation.
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE marketing.lead_rate_buckets;");
    }
}
