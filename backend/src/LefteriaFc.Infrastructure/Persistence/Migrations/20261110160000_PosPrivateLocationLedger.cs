using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// POS-only, owner-scoped location/inventory/checkout ledger foundation.
/// The operator, payment, sale and stock endpoints remain OFF until audited
/// transaction functions and verified isolated roles exist.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110160000_PosPrivateLocationLedger")]
public sealed class PosPrivateLocationLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos.cities (
        organization_id varchar(32) NOT NULL
          REFERENCES club.organizations(id) ON DELETE RESTRICT
          CHECK (organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        id varchar(80) COLLATE "C" NOT NULL
          CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
        name varchar(100) NOT NULL
          CHECK (name=btrim(name) AND length(name) BETWEEN 1 AND 100),
        active boolean NOT NULL DEFAULT true,
        version bigint NOT NULL DEFAULT 0 CHECK (version>=0),
        PRIMARY KEY(organization_id,id)
      );
      CREATE TABLE pos.locations (
        organization_id varchar(32) NOT NULL,
        id varchar(80) COLLATE "C" NOT NULL
          CHECK (id ~ '^[A-Za-z0-9_-]{1,80}$'),
        city_id varchar(80) COLLATE "C" NOT NULL,
        code varchar(64) COLLATE "C" NOT NULL
          CHECK (code ~ '^[a-z0-9][a-z0-9-]{1,63}$'),
        name varchar(100) NOT NULL
          CHECK (name=btrim(name) AND length(name) BETWEEN 1 AND 100),
        active boolean NOT NULL DEFAULT true,
        version bigint NOT NULL DEFAULT 0 CHECK (version>=0),
        PRIMARY KEY(organization_id,id),
        UNIQUE(organization_id,code),
        FOREIGN KEY(organization_id,city_id)
          REFERENCES pos.cities(organization_id,id) ON DELETE RESTRICT
      );

      -- Sale availability is not inferred from a generic public product list.
      -- Every location must have explicit product assignment.
      CREATE TABLE pos.location_products (
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        product_id varchar(80) NOT NULL,
        enabled boolean NOT NULL DEFAULT false,
        version bigint NOT NULL DEFAULT 0 CHECK (version>=0),
        PRIMARY KEY(organization_id,location_id,product_id),
        FOREIGN KEY(organization_id,location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,product_id)
          REFERENCES pos.products(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE pos.location_stock (
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        on_hand bigint NOT NULL DEFAULT 0 CHECK (on_hand BETWEEN 0 AND 100000000),
        version bigint NOT NULL DEFAULT 0 CHECK (version>=0),
        PRIMARY KEY(organization_id,location_id,product_id,variant_id),
        FOREIGN KEY(organization_id,location_id,product_id)
          REFERENCES pos.location_products(organization_id,location_id,product_id)
          ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id)
          ON DELETE RESTRICT
      );

      -- Operator sessions are NOT mobile account sessions or web admin cookies.
      -- No plaintext PIN/code, password or bearer token is persisted.
      CREATE TABLE pos.operators (
        organization_id varchar(32) NOT NULL,
        id uuid NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        display_name varchar(100) NOT NULL
          CHECK (length(btrim(display_name)) BETWEEN 1 AND 100),
        code_digest char(64) COLLATE "C" NOT NULL
          CHECK (code_digest ~ '^[0-9a-f]{64}$'),
        active boolean NOT NULL DEFAULT false,
        starts_at timestamptz,
        ends_at timestamptz,
        session_version bigint NOT NULL DEFAULT 1 CHECK (session_version>0),
        PRIMARY KEY(organization_id,id),
        UNIQUE(organization_id,id,location_id),
        FOREIGN KEY(organization_id,location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT,
        CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at>starts_at),
        UNIQUE(organization_id,code_digest)
      );
      CREATE TABLE pos.operator_sessions (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        operator_id uuid NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        token_digest char(64) COLLATE "C" NOT NULL UNIQUE
          CHECK (token_digest ~ '^[0-9a-f]{64}$'),
        version bigint NOT NULL CHECK (version>0),
        expires_at timestamptz NOT NULL,
        revoked_at timestamptz,
        FOREIGN KEY(organization_id,operator_id,location_id)
          REFERENCES pos.operators(organization_id,id,location_id) ON DELETE RESTRICT
      );

      -- Totals are immutable EUR cents. Client-side prices must NEVER be used
      -- for issuance; the future checkout must price from locked POS variants.
      CREATE TABLE pos.sales (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        operator_id uuid NOT NULL,
        client_sale_id uuid NOT NULL,
        business_date date NOT NULL,
        receipt_number bigint NOT NULL CHECK (receipt_number>0),
        total_cents bigint NOT NULL CHECK (total_cents BETWEEN 1 AND 100000000),
        currency char(3) NOT NULL DEFAULT 'EUR' CHECK(currency='EUR'),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,location_id)
          REFERENCES pos.locations(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,operator_id,location_id)
          REFERENCES pos.operators(organization_id,id,location_id) ON DELETE RESTRICT,
        UNIQUE(id,organization_id,location_id),
        UNIQUE(id,organization_id),
        UNIQUE(organization_id,location_id,client_sale_id),
        UNIQUE(organization_id,location_id,business_date,receipt_number)
      );
      CREATE INDEX ix_pos_sales_business_day
        ON pos.sales(organization_id,location_id,business_date,recorded_at,id);
      CREATE TABLE pos.sale_lines (
        sale_id uuid NOT NULL REFERENCES pos.sales(id) ON DELETE RESTRICT,
        line_number integer NOT NULL CHECK(line_number BETWEEN 1 AND 100),
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        product_name varchar(100) NOT NULL,
        variant_name varchar(80) NOT NULL,
        quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 999),
        unit_price_cents integer NOT NULL
          CHECK(unit_price_cents BETWEEN 1 AND 10000000),
        unit_cost_cents integer NOT NULL
          CHECK(unit_cost_cents BETWEEN 0 AND 10000000),
        line_total_cents bigint GENERATED ALWAYS AS (
          quantity::bigint * unit_price_cents::bigint) STORED,
        PRIMARY KEY(sale_id,line_number),
        FOREIGN KEY(sale_id,organization_id,location_id)
          REFERENCES pos.sales(id,organization_id,location_id) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,location_id,product_id)
          REFERENCES pos.location_products(organization_id,location_id,product_id)
          ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,product_id,variant_id)
          REFERENCES pos.product_variants(organization_id,product_id,id)
          ON DELETE RESTRICT,
        CHECK (quantity::bigint*unit_price_cents::bigint<=100000000)
      );

      -- Settlement and refunds are journal events, NEVER mutable sale flags.
      -- A card payment must have provider evidence before it can be recorded.
      CREATE TABLE pos.sale_payment_events (
        id uuid PRIMARY KEY,
        sale_id uuid NOT NULL REFERENCES pos.sales(id) ON DELETE RESTRICT,
        event_type varchar(16) NOT NULL CHECK(event_type IN (
          'cash_received','card_capture','cash_refund','card_refund')),
        amount_cents bigint NOT NULL CHECK(amount_cents BETWEEN 1 AND 100000000),
        cash_tendered_cents bigint
          CHECK(cash_tendered_cents BETWEEN 1 AND 100000000),
        evidence_reference varchar(200),
        event_request_id uuid NOT NULL UNIQUE,
        reversed_event_id uuid UNIQUE REFERENCES pos.sale_payment_events(id)
          ON DELETE RESTRICT,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK (
          (event_type='cash_received' AND cash_tendered_cents IS NOT NULL
            AND cash_tendered_cents>=amount_cents
            AND evidence_reference IS NULL AND reversed_event_id IS NULL)
          OR
          (event_type='card_capture' AND cash_tendered_cents IS NULL
            AND evidence_reference IS NOT NULL
            AND length(btrim(evidence_reference)) BETWEEN 1 AND 200
            AND reversed_event_id IS NULL)
          OR
          (event_type IN ('cash_refund','card_refund')
            AND cash_tendered_cents IS NULL AND reversed_event_id IS NOT NULL
            AND (event_type='cash_refund' OR
              (evidence_reference IS NOT NULL AND
              length(btrim(evidence_reference)) BETWEEN 1 AND 200)))
        )
      );

      -- Inventory changes are separate, evidence-linked movements. Checkout
      -- cannot change quantities merely by inserting a sale header.
      CREATE TABLE pos.stock_movements (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) COLLATE "C" NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        request_id uuid NOT NULL UNIQUE,
        sale_id uuid REFERENCES pos.sales(id) ON DELETE RESTRICT,
        reason varchar(16) NOT NULL CHECK(reason IN (
          'opening','purchase','sale','return','correction')),
        delta bigint NOT NULL CHECK(delta BETWEEN -10000000 AND 10000000
          AND delta<>0),
        before_quantity bigint NOT NULL CHECK(before_quantity>=0),
        after_quantity bigint NOT NULL CHECK(after_quantity>=0
          AND after_quantity=before_quantity+delta),
        actor_operator_id uuid,
        actor_admin_id uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        CHECK ((actor_operator_id IS NULL) <> (actor_admin_id IS NULL)),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(sale_id,organization_id,location_id)
          REFERENCES pos.sales(id,organization_id,location_id) ON DELETE RESTRICT,
        CHECK ((reason IN ('sale','return'))=(sale_id IS NOT NULL)),
        FOREIGN KEY(organization_id,location_id,product_id,variant_id)
          REFERENCES pos.location_stock(organization_id,location_id,product_id,variant_id)
          ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,actor_operator_id)
          REFERENCES pos.operators(organization_id,id) ON DELETE RESTRICT
      );

      -- Neither a sale snapshot nor its payment/stock evidence is editable.
      CREATE FUNCTION pos.reject_private_pos_ledger_change()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      BEGIN
        RAISE EXCEPTION 'immutable_pos_ledger' USING ERRCODE='42501';
      END $$;
      CREATE TRIGGER pos_sale_immutable BEFORE UPDATE OR DELETE
        ON pos.sales FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();
      CREATE TRIGGER pos_sale_lines_immutable BEFORE UPDATE OR DELETE
        ON pos.sale_lines FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();
      CREATE TRIGGER pos_payment_events_immutable BEFORE UPDATE OR DELETE
        ON pos.sale_payment_events FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();
      CREATE TRIGGER pos_stock_movement_immutable BEFORE UPDATE OR DELETE
        ON pos.stock_movements FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();

      CREATE VIEW pos.sale_balances AS
        SELECT s.id AS sale_id,s.organization_id,s.location_id,s.total_cents,
          coalesce(sum(CASE
            WHEN e.event_type IN ('cash_received','card_capture') THEN e.amount_cents
            WHEN e.event_type IN ('cash_refund','card_refund') THEN -e.amount_cents
            ELSE 0 END),0)::bigint AS settled_cents
        FROM pos.sales s
        LEFT JOIN pos.sale_payment_events e ON e.sale_id=s.id
        GROUP BY s.id,s.organization_id,s.location_id,s.total_cents;
      REVOKE ALL ON FUNCTION pos.reject_private_pos_ledger_change() FROM PUBLIC;
      REVOKE ALL ON ALL TABLES IN SCHEMA pos FROM PUBLIC;
      REVOKE ALL ON pos.sale_balances FROM PUBLIC;
      ALTER TABLE pos.cities ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.locations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.location_products ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.location_stock ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.operators ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.operator_sessions ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.sales ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.sale_lines ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.sale_payment_events ENABLE ROW LEVEL SECURITY;
      ALTER TABLE pos.stock_movements ENABLE ROW LEVEL SECURITY;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $pos_guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM pos.sales)
          OR EXISTS(SELECT 1 FROM pos.locations)
          OR EXISTS(SELECT 1 FROM pos.cities)
          OR EXISTS(SELECT 1 FROM pos.operators)
          OR EXISTS(SELECT 1 FROM pos.location_products)
          OR EXISTS(SELECT 1 FROM pos.location_stock) THEN
          RAISE EXCEPTION 'pos_ledger_data_requires_forward_recovery';
        END IF;
      END $pos_guard$;
      DROP VIEW pos.sale_balances;
      DROP TABLE pos.stock_movements;
      DROP TABLE pos.sale_payment_events;
      DROP TABLE pos.sale_lines;
      DROP TABLE pos.sales;
      DROP TABLE pos.operator_sessions;
      DROP TABLE pos.operators;
      DROP TABLE pos.location_stock;
      DROP TABLE pos.location_products;
      DROP TABLE pos.locations;
      DROP TABLE pos.cities;
      DROP FUNCTION pos.reject_private_pos_ledger_change();
    """);
}
