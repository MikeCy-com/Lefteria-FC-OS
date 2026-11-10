using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transactional POS to finance handoff; no direct mutation of Academy/parent
/// balances. Only fully issued sales and independently verified refunds
/// become immutable queued financial events. Downstream posting is disabled
/// until the original finance contracts and journal rules are verified.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110164000_PosFinancePostingOutbox")]
public sealed class PosFinancePostingOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos.finance_posting_outbox (
        id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        event_kind varchar(24) NOT NULL
          CHECK(event_kind IN ('sale_receipted','refund_verified')),
        source_id uuid NOT NULL,
        sale_id uuid NOT NULL,
        amount_cents bigint NOT NULL
          CHECK(amount_cents BETWEEN 1 AND 100000000),
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK(currency='EUR'),
        source_sha256 char(64) COLLATE "C" NOT NULL
          CHECK(source_sha256 ~ '^[a-f0-9]{64}$'),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        -- Intentionally no consumed/posted flag: no unreviewed accounting
        -- consumer is permitted to mark a journal entry as completed.
        UNIQUE(organization_id,event_kind,source_id),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos.sales(organization_id,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_pos_finance_outbox_unposted
        ON pos.finance_posting_outbox(organization_id,created_at,id);
      ALTER TABLE pos.finance_posting_outbox ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos.finance_posting_outbox FROM PUBLIC;

      CREATE FUNCTION pos.enqueue_receipted_sale_for_finance()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $receipt_to_finance$
      DECLARE v_sale pos.sales%ROWTYPE;
      BEGIN
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
        IF NOT FOUND OR v_sale.status<>'paid'
          OR v_sale.currency<>'EUR'
          OR (NEW.immutable_payload->>'sale_id') IS DISTINCT FROM NEW.sale_id::text
          OR (NEW.immutable_payload->>'total_cents') IS DISTINCT FROM
            v_sale.total_cents::text
          OR NEW.payload_sha256 IS DISTINCT FROM
            encode(sha256(convert_to(NEW.immutable_payload::text,'UTF8')),'hex')
        THEN RAISE EXCEPTION 'pos_finance_receipt_not_reconciled'
          USING ERRCODE='23514'; END IF;
        INSERT INTO pos.finance_posting_outbox(
          organization_id,event_kind,source_id,sale_id,amount_cents,
          source_sha256
        ) VALUES(
          NEW.organization_id,'sale_receipted',NEW.id,NEW.sale_id,
          v_sale.total_cents,NEW.payload_sha256
        ) ON CONFLICT(organization_id,event_kind,source_id) DO NOTHING;
        RETURN NEW;
      END $receipt_to_finance$;

      CREATE FUNCTION pos.enqueue_verified_refund_for_finance()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $refund_to_finance$
      DECLARE v_return pos.returns%ROWTYPE;
        v_payment pos.payment_events%ROWTYPE;
        v_sale pos.sales%ROWTYPE;
        v_evidence text;
      BEGIN
        IF NEW.status<>'verified' OR
           (TG_OP='UPDATE' AND OLD.status='verified') THEN
          RETURN NEW;
        END IF;
        SELECT * INTO v_return FROM pos.returns
          WHERE organization_id=NEW.organization_id AND id=NEW.return_id;
        SELECT * INTO v_payment FROM pos.payment_events
          WHERE id=NEW.payment_event_id;
        SELECT * INTO v_sale FROM pos.sales
          WHERE organization_id=NEW.organization_id AND id=v_return.sale_id;
        IF v_return.id IS NULL OR v_return.status NOT IN ('approved','refunded')
          OR v_payment.id IS NULL OR v_payment.status<>'verified'
          OR v_payment.organization_id<>NEW.organization_id
          OR v_payment.sale_id<>v_return.sale_id
          OR v_payment.payment_kind<>NEW.refund_kind
          OR v_sale.id IS NULL OR v_sale.status<>'paid'
        THEN RAISE EXCEPTION 'pos_finance_refund_not_reconciled'
          USING ERRCODE='23514'; END IF;
        -- Cash refunds do not have a provider evidence hash, so identify
        -- the signed historical request and verified cash payout record.
        v_evidence:=encode(sha256(convert_to(
          NEW.id::text||':'||NEW.organization_id||':'||
          NEW.payment_event_id::text||':'||NEW.amount_cents::text||':'||
          coalesce(NEW.provider_evidence_sha256,NEW.request_id::text,''),
          'UTF8')),'hex');
        INSERT INTO pos.finance_posting_outbox(
          organization_id,event_kind,source_id,sale_id,amount_cents,
          source_sha256
        ) VALUES(
          NEW.organization_id,'refund_verified',NEW.id,v_return.sale_id,
          NEW.amount_cents,v_evidence
        ) ON CONFLICT(organization_id,event_kind,source_id) DO NOTHING;
        RETURN NEW;
      END $refund_to_finance$;

      CREATE TRIGGER pos_finance_sale_receipted
        AFTER INSERT ON pos.receipts FOR EACH ROW
        EXECUTE FUNCTION pos.enqueue_receipted_sale_for_finance();
      CREATE TRIGGER pos_finance_refund_verified
        AFTER INSERT OR UPDATE OF status ON pos.refund_events
        FOR EACH ROW EXECUTE FUNCTION pos.enqueue_verified_refund_for_finance();

      CREATE FUNCTION pos.reject_finance_posting_mutation()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $immutable$
      BEGIN
        RAISE EXCEPTION 'pos_finance_posting_immutable' USING ERRCODE='42501';
      END $immutable$;
      CREATE TRIGGER pos_finance_outbox_immutable
        BEFORE UPDATE OR DELETE ON pos.finance_posting_outbox
        FOR EACH ROW EXECUTE FUNCTION pos.reject_finance_posting_mutation();

      REVOKE ALL ON FUNCTION pos.enqueue_receipted_sale_for_finance() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.enqueue_verified_refund_for_finance() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.reject_finance_posting_mutation() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $preserve$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.finance_posting_outbox)
        THEN RAISE EXCEPTION 'finance_posting_history_requires_forward_recovery'; END IF;
      END $preserve$;
      DROP TRIGGER pos_finance_outbox_immutable ON pos.finance_posting_outbox;
      DROP TRIGGER pos_finance_refund_verified ON pos.refund_events;
      DROP TRIGGER pos_finance_sale_receipted ON pos.receipts;
      DROP FUNCTION pos.reject_finance_posting_mutation();
      DROP FUNCTION pos.enqueue_verified_refund_for_finance();
      DROP FUNCTION pos.enqueue_receipted_sale_for_finance();
      DROP TABLE pos.finance_posting_outbox;
    """);
}
