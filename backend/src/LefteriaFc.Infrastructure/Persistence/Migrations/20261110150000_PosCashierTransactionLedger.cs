using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// POS transactional storage: real register, cashier, stock, immutable sale
/// snapshots, payment evidence, numbered receipts and bounded return ledger.
/// No runtime grants or automatic activation of live payments.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110210000_PosCashierTransactionLedger")]
public sealed class PosCashierTransactionLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE SCHEMA IF NOT EXISTS pos_cashier;
      REVOKE ALL ON SCHEMA pos_cashier FROM PUBLIC;
      CREATE TABLE pos_cashier.registers (
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) NOT NULL,
        id varchar(80) NOT NULL CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
        label varchar(140) NOT NULL,
        is_active boolean NOT NULL DEFAULT false,
        PRIMARY KEY(organization_id,id),
        UNIQUE(organization_id,location_id,id),
        FOREIGN KEY(organization_id,location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.cashier_assignments (
        organization_id varchar(32) NOT NULL,
        register_id varchar(80) NOT NULL,
        admin_account_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        is_active boolean NOT NULL DEFAULT false,
        assigned_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,register_id,admin_account_id),
        FOREIGN KEY(organization_id,register_id)
          REFERENCES pos_cashier.registers(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.shifts (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        register_id varchar(80) NOT NULL,
        cashier_id uuid NOT NULL,
        opened_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        closed_at timestamptz,
        opening_cash_cents bigint NOT NULL CHECK(opening_cash_cents BETWEEN 0 AND 100000000),
        counted_cash_cents bigint CHECK(counted_cash_cents BETWEEN 0 AND 100000000),
        CHECK ((closed_at IS NULL)=(counted_cash_cents IS NULL)),
        CHECK (closed_at IS NULL OR closed_at>=opened_at),
        FOREIGN KEY(organization_id,register_id,cashier_id)
          REFERENCES pos_cashier.cashier_assignments(organization_id,register_id,admin_account_id),
        UNIQUE(id,organization_id,register_id)
      );
      CREATE UNIQUE INDEX uq_pos_one_open_shift ON pos_cashier.shifts(organization_id,register_id)
        WHERE closed_at IS NULL;

      -- Owner+location+variant is the inventory identity. A sale must reserve
      -- stock in the same transaction and keep nonnegative on-hand/reserved.
      CREATE TABLE pos_cashier.stock (
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        on_hand integer NOT NULL DEFAULT 0 CHECK(on_hand BETWEEN 0 AND 1000000),
        reserved integer NOT NULL DEFAULT 0 CHECK(reserved>=0 AND reserved<=on_hand),
        version bigint NOT NULL DEFAULT 0 CHECK(version>=0),
        PRIMARY KEY(organization_id,location_id,product_id,variant_id),
        FOREIGN KEY(organization_id,location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.sales (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        register_id varchar(80) NOT NULL,
        shift_id uuid NOT NULL,
        cashier_id uuid NOT NULL
          REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        request_id uuid NOT NULL,
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK(currency='EUR'),
        status varchar(24) NOT NULL DEFAULT 'awaiting_payment'
          CHECK(status IN ('awaiting_payment','partially_paid','paid','voided')),
        subtotal_cents bigint NOT NULL CHECK(subtotal_cents BETWEEN 1 AND 100000000),
        discount_cents bigint NOT NULL DEFAULT 0 CHECK(discount_cents>=0),
        tax_cents bigint NOT NULL DEFAULT 0 CHECK(tax_cents>=0),
        total_cents bigint NOT NULL CHECK(total_cents BETWEEN 1 AND 100000000),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        paid_at timestamptz,
        voided_at timestamptz,
        CHECK (total_cents=subtotal_cents-discount_cents+tax_cents),
        CHECK ((status='paid')=(paid_at IS NOT NULL)),
        CHECK ((status='voided')=(voided_at IS NOT NULL)),
        FOREIGN KEY(shift_id,organization_id,register_id)
          REFERENCES pos_cashier.shifts(id,organization_id,register_id) ON DELETE RESTRICT,
        UNIQUE(organization_id,request_id),
        UNIQUE(organization_id,id)
      );
      CREATE INDEX ix_pos_sales_shift ON pos_cashier.sales(organization_id,shift_id,created_at);
      CREATE TABLE pos_cashier.sale_lines (
        sale_id uuid NOT NULL,
        organization_id varchar(32) NOT NULL,
        line_number integer NOT NULL CHECK(line_number BETWEEN 1 AND 30),
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        product_name varchar(100) NOT NULL,
        variant_name varchar(80) NOT NULL,
        quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 99),
        unit_price_cents integer NOT NULL CHECK(unit_price_cents BETWEEN 1 AND 10000000),
        unit_cost_cents integer NOT NULL CHECK(unit_cost_cents BETWEEN 0 AND 10000000),
        line_total_cents bigint GENERATED ALWAYS AS
          (quantity::bigint*unit_price_cents) STORED,
        PRIMARY KEY(sale_id,line_number),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos_cashier.sales(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id) ON DELETE RESTRICT,
        UNIQUE(sale_id,product_id,variant_id)
      );

      -- Only externally VERIFIED card capture, or explicitly audited counted
      -- cashier cash, may be marked captured. A redirect/intent is not capture.
      CREATE TABLE pos_cashier.payment_events (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        sale_id uuid NOT NULL,
        payment_kind varchar(12) NOT NULL
          CHECK(payment_kind IN ('cash','card')),
        status varchar(12) NOT NULL
          CHECK(status IN ('pending','verified','failed')),
        amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 1 AND 100000000),
        provider varchar(64),
        provider_payment_id varchar(200),
        provider_evidence_sha256 char(64) COLLATE "C",
        verification_reference varchar(200),
        recorded_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        verified_at timestamptz,
        CHECK ((status='verified')=(verified_at IS NOT NULL)),
        CHECK (payment_kind='card' OR
          (provider IS NULL AND provider_payment_id IS NULL)),
        CHECK (payment_kind<>'cash' OR status<>'verified'
          OR (verification_reference IS NOT NULL
              AND length(btrim(verification_reference)) BETWEEN 1 AND 200)),
        CHECK (payment_kind='cash' OR
          (status<>'verified' OR
           (provider IS NOT NULL AND provider_payment_id IS NOT NULL AND
            provider_evidence_sha256 ~ '^[0-9a-f]{64}$'))),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos_cashier.sales(organization_id,id) ON DELETE RESTRICT
      );
      CREATE UNIQUE INDEX uq_pos_card_provider_capture
        ON pos_cashier.payment_events(provider,provider_payment_id)
        WHERE payment_kind='card' AND provider_payment_id IS NOT NULL;
      CREATE UNIQUE INDEX uq_pos_cash_verification_reference
        ON pos_cashier.payment_events(organization_id,verification_reference)
        WHERE payment_kind='cash' AND verification_reference IS NOT NULL;
      CREATE INDEX ix_pos_payment_sale
        ON pos_cashier.payment_events(organization_id,sale_id,status);

      -- The official receipt is only issued for a fully reconciled paid sale
      -- by a separately audited commit operation; never from the UI.
      CREATE TABLE pos_cashier.receipt_sequences (
        organization_id varchar(32) NOT NULL,
        register_id varchar(80) NOT NULL,
        next_number bigint NOT NULL DEFAULT 1 CHECK(next_number BETWEEN 1 AND 999999999),
        PRIMARY KEY(organization_id,register_id),
        FOREIGN KEY(organization_id,register_id)
          REFERENCES pos_cashier.registers(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.receipts (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        sale_id uuid NOT NULL,
        register_id varchar(80) NOT NULL,
        receipt_number bigint NOT NULL CHECK(receipt_number BETWEEN 1 AND 999999999),
        issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        immutable_payload jsonb NOT NULL CHECK(jsonb_typeof(immutable_payload)='object'),
        payload_sha256 char(64) COLLATE "C" NOT NULL
          CHECK(payload_sha256 ~ '^[0-9a-f]{64}$'),
        UNIQUE(organization_id,register_id,receipt_number),
        UNIQUE(organization_id,sale_id),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos_cashier.sales(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,register_id)
          REFERENCES pos_cashier.registers(organization_id,id) ON DELETE RESTRICT
      );

      CREATE TABLE pos_cashier.returns (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        sale_id uuid NOT NULL,
        request_id uuid NOT NULL,
        reason_code varchar(48) NOT NULL
          CHECK(reason_code IN ('defective','wrong_item','customer_return','other')),
        status varchar(20) NOT NULL DEFAULT 'requested'
          CHECK(status IN ('requested','approved','rejected','refunded')),
        created_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        approved_by uuid REFERENCES identity.admin_accounts(id),
        requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        approved_at timestamptz,
        CHECK((approved_at IS NULL)=(approved_by IS NULL)),
        UNIQUE(organization_id,request_id),
        UNIQUE(organization_id,id),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos_cashier.sales(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.return_lines (
        organization_id varchar(32) NOT NULL,
        return_id uuid NOT NULL,
        sale_id uuid NOT NULL,
        sale_line_number integer NOT NULL,
        quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 99),
        PRIMARY KEY(return_id,sale_line_number),
        FOREIGN KEY(organization_id,return_id)
          REFERENCES pos_cashier.returns(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(sale_id,sale_line_number)
          REFERENCES pos_cashier.sale_lines(sale_id,line_number) ON DELETE RESTRICT
      );
      CREATE TABLE pos_cashier.refund_events (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        return_id uuid NOT NULL,
        payment_event_id uuid NOT NULL REFERENCES pos_cashier.payment_events(id),
        refund_kind varchar(12) NOT NULL CHECK(refund_kind IN ('cash','card')),
        amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 1 AND 100000000),
        status varchar(16) NOT NULL CHECK(status IN ('pending','verified','failed')),
        provider_refund_id varchar(200),
        provider_evidence_sha256 char(64) COLLATE "C",
        recorded_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        verified_at timestamptz,
        CHECK((status='verified')=(verified_at IS NOT NULL)),
        CHECK(refund_kind='cash' OR status<>'verified' OR
          (provider_refund_id IS NOT NULL AND
           provider_evidence_sha256 ~ '^[0-9a-f]{64}$')),
        FOREIGN KEY(organization_id,return_id)
          REFERENCES pos_cashier.returns(organization_id,id) ON DELETE RESTRICT
      );
      CREATE UNIQUE INDEX uq_pos_provider_refund
        ON pos_cashier.refund_events(provider_refund_id)
        WHERE provider_refund_id IS NOT NULL;

      CREATE TABLE pos_cashier.transaction_audit (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        sale_id uuid,
        action varchar(48) NOT NULL CHECK(action IN (
          'shift_open','shift_close','sale_create','cash_verify',
          'card_capture_verify','sale_paid','receipt_issue',
          'return_request','return_approve','return_reject','refund_verify')),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        request_id uuid NOT NULL,
        UNIQUE(organization_id,request_id,action)
      );

      -- The private schema is deliberately NOT an enabled operational POS yet.
      -- No app/admin grants, provider callbacks or cashier endpoints here.
      REVOKE ALL ON ALL TABLES IN SCHEMA pos_cashier FROM PUBLIC;
      ALTER TABLE pos_cashier.registers ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.cashier_assignments ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.shifts ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.stock ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.sales ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.sale_lines ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.payment_events ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.receipt_sequences ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.receipts ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.returns ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.return_lines ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.refund_events ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos_cashier.transaction_audit ENABLE ROW LEVEL SECURITY;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $pos_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.shifts)
          OR EXISTS(SELECT 1 FROM pos_cashier.sales)
          OR EXISTS(SELECT 1 FROM pos_cashier.receipts)
          OR EXISTS(SELECT 1 FROM pos_cashier.returns)
          OR EXISTS(SELECT 1 FROM pos_cashier.refund_events) THEN
          RAISE EXCEPTION 'pos_financial_history_requires_forward_recovery';
        END IF;
      END $pos_recovery$;
      DROP TABLE pos_cashier.transaction_audit;
      DROP TABLE pos_cashier.refund_events;
      DROP TABLE pos_cashier.return_lines;
      DROP TABLE pos_cashier.returns;
      DROP TABLE pos_cashier.receipts;
      DROP TABLE pos_cashier.receipt_sequences;
      DROP TABLE pos_cashier.payment_events;
      DROP TABLE pos_cashier.sale_lines;
      DROP TABLE pos_cashier.sales;
      DROP TABLE pos_cashier.stock;
      DROP TABLE pos_cashier.shifts;
      DROP TABLE pos_cashier.cashier_assignments;
      DROP TABLE pos_cashier.registers;
      DROP SCHEMA pos_cashier;
    """);
}