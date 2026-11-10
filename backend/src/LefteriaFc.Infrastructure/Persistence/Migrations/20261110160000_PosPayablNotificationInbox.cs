using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Store authenticated payabl. notification evidence for independent
/// server-side verification without posting ledger transactions.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110160000_PosPayablNotificationInbox")]
public sealed class PosPayablNotificationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos.payabl_notification_inbox (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT,
        sale_id uuid NOT NULL,
        merchant_id varchar(80) NOT NULL
          CHECK(length(btrim(merchant_id)) BETWEEN 1 AND 80),
        order_id varchar(40) NOT NULL
          CHECK(length(btrim(order_id)) BETWEEN 1 AND 40),
        provider_transaction_id varchar(30) NOT NULL
          CHECK(provider_transaction_id ~ '^[0-9]{1,30}$'),
        notification_type varchar(32) NOT NULL
          CHECK(notification_type IN ('capture','authorization','refund')),
        gateway_error_code varchar(8) NOT NULL
          CHECK(gateway_error_code ~ '^[0-9]{1,8}$'),
        gateway_timestamp timestamptz NOT NULL,
        evidence_sha256 char(64) COLLATE "C" NOT NULL
          CHECK(evidence_sha256 ~ '^[0-9a-f]{64}$'),
        -- Receipt of a signed notification is NOT payment confirmation.
        status varchar(24) NOT NULL DEFAULT 'pending_recheck'
          CHECK(status IN ('pending_recheck','rejected','provider_confirmed')),
        provider_rechecked_at timestamptz,
        received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK(status<>'provider_confirmed' OR provider_rechecked_at IS NOT NULL),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos.sales(organization_id,id) ON DELETE RESTRICT,
        UNIQUE(merchant_id,provider_transaction_id,notification_type,gateway_error_code),
        UNIQUE(organization_id,evidence_sha256)
      );
      CREATE INDEX ix_pos_payabl_inbox_pending
        ON pos.payabl_notification_inbox(organization_id,received_at)
        WHERE status='pending_recheck';
      ALTER TABLE pos.payabl_notification_inbox ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos.payabl_notification_inbox FROM PUBLIC;

      -- A separate, isolated gateway verifier must bind:
      -- signed timestamp + orderid + merchant + exact sale EUR amount +
      -- provider-side transaction status. Ingest cannot issue a receipt,
      -- update pos.payment_events or change sale/stock.
      CREATE FUNCTION pos.assert_payabl_inbox_not_ledger()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $inbox$
      BEGIN
        IF TG_OP='DELETE' THEN
          RAISE EXCEPTION 'gateway_evidence_delete_denied' USING ERRCODE='42501';
        END IF;
        IF OLD.status<>'pending_recheck'
           OR NEW.status NOT IN ('rejected','provider_confirmed')
           OR (to_jsonb(NEW)-'status'-'provider_rechecked_at')
              IS DISTINCT FROM
              (to_jsonb(OLD)-'status'-'provider_rechecked_at')
        THEN RAISE EXCEPTION 'gateway_evidence_mutation_denied'
          USING ERRCODE='42501';
        END IF;
        RETURN NEW;
      END $inbox$;
      CREATE TRIGGER pos_payabl_inbox_immutable
        BEFORE UPDATE OR DELETE ON pos.payabl_notification_inbox
        FOR EACH ROW EXECUTE FUNCTION pos.assert_payabl_inbox_not_ledger();
      REVOKE ALL ON FUNCTION pos.assert_payabl_inbox_not_ledger() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.payabl_notification_inbox)
        THEN RAISE EXCEPTION 'payabl_evidence_requires_forward_recovery'; END IF;
      END $guard$;
      DROP TRIGGER pos_payabl_inbox_immutable ON pos.payabl_notification_inbox;
      DROP FUNCTION pos.assert_payabl_inbox_not_ledger();
      DROP TABLE pos.payabl_notification_inbox;
    """);
}
