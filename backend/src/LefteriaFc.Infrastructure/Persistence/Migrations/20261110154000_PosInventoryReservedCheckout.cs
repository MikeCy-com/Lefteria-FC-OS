using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Atomically prepare a server-priced inventory-reserved sale. Disabled until
/// fiscal policy is independently reviewed and runtime execution is granted.
/// No payment processing, receipt or physical stock deduction here.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110154000_PosInventoryReservedCheckout")]
public sealed class PosInventoryReservedCheckout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE pos.checkout_policies (
        organization_id varchar(32) PRIMARY KEY REFERENCES club.organizations(id)
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        enabled boolean NOT NULL DEFAULT false,
        tax_treatment varchar(32) NOT NULL CHECK(tax_treatment='verified_exempt'),
        reviewed_by uuid NOT NULL REFERENCES identity.admin_accounts(id),
        reviewed_at timestamptz NOT NULL,
        policy_reference varchar(160) NOT NULL CHECK(length(btrim(policy_reference))>=1)
      );
      ALTER TABLE pos.checkout_policies ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos.checkout_policies FROM PUBLIC;

      ALTER TABLE pos.sales ADD COLUMN checkout_request_sha256 char(64) COLLATE "C";
      ALTER TABLE pos.sales ADD CONSTRAINT ck_pos_checkout_hash
        CHECK (checkout_request_sha256 IS NULL
          OR checkout_request_sha256 ~ '^[0-9a-f]{64}$');

      CREATE TABLE pos.sale_reservations (
        organization_id varchar(32) NOT NULL,
        sale_id uuid NOT NULL,
        line_number integer NOT NULL CHECK(line_number BETWEEN 1 AND 30),
        location_id varchar(80) NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 99),
        released_at timestamptz,
        consumed_at timestamptz,
        CHECK(released_at IS NULL OR consumed_at IS NULL),
        PRIMARY KEY(sale_id,line_number),
        FOREIGN KEY(organization_id,sale_id)
          REFERENCES pos.sales(organization_id,id) ON DELETE RESTRICT,
        FOREIGN KEY(sale_id,line_number)
          REFERENCES pos.sale_lines(sale_id,line_number) ON DELETE RESTRICT,
        FOREIGN KEY(organization_id,location_id,product_id,variant_id)
          REFERENCES pos.stock(organization_id,location_id,product_id,variant_id)
          ON DELETE RESTRICT
      );
      CREATE INDEX ix_pos_reservations_active
        ON pos.sale_reservations(organization_id,location_id,product_id,variant_id)
        WHERE released_at IS NULL AND consumed_at IS NULL;
      ALTER TABLE pos.sale_reservations ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos.sale_reservations FROM PUBLIC;

      CREATE FUNCTION pos.prepare_cashier_checkout(
        _digest text,_owner text,_register text,_shift uuid,
        _request uuid,_items jsonb
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $checkout$
      DECLARE
        v_actor uuid; v_shift pos.shifts%ROWTYPE;
        v_location varchar(80); v_sale uuid; v_existing pos.sales%ROWTYPE;
        v_hash text; v_line record; v_stock pos.stock%ROWTYPE;
        v_price pos.product_variants%ROWTYPE; v_product pos.products%ROWTYPE;
        v_sum bigint:=0; v_lines int:=0;
      BEGIN
        IF _shift IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _items IS NULL OR jsonb_typeof(_items)<>'array'
          OR jsonb_array_length(_items) NOT BETWEEN 1 AND 30
        THEN RAISE EXCEPTION 'checkout_request_invalid' USING ERRCODE='22023';
        END IF;
        -- A fully verified tax policy is a mandatory release gate.
        -- Exempt-only is explicit: never silently invent VAT rates.
        IF NOT EXISTS(SELECT 1 FROM pos.checkout_policies
          WHERE organization_id=_owner AND enabled
            AND tax_treatment='verified_exempt' AND reviewed_at<=clock_timestamp())
        THEN RAISE EXCEPTION 'checkout_tax_policy_disabled' USING ERRCODE='55000';
        END IF;
        v_actor:=pos.current_cashier_authority(_digest,_owner,_register);
        SELECT * INTO v_shift FROM pos.shifts
          WHERE id=_shift AND organization_id=_owner
            AND register_id=_register AND cashier_id=v_actor FOR UPDATE;
        IF NOT FOUND OR v_shift.closed_at IS NOT NULL THEN
          RAISE EXCEPTION 'checkout_shift_closed_or_unassigned' USING ERRCODE='42501';
        END IF;
        SELECT r.location_id INTO v_location FROM pos.registers r
          WHERE r.organization_id=_owner AND r.id=_register AND r.is_active FOR SHARE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'checkout_register_unavailable' USING ERRCODE='42501';
        END IF;

        -- Validate canonical, strictly bounded cart before any write.
        IF EXISTS (
          SELECT 1 FROM jsonb_array_elements(_items) e
          WHERE jsonb_typeof(e.value)<>'object'
            OR (SELECT count(*) FROM jsonb_object_keys(e.value))<>3
            OR NOT (e.value ?& ARRAY['product_id','variant_id','quantity'])
            OR jsonb_typeof(e.value->'product_id')<>'string'
            OR jsonb_typeof(e.value->'variant_id')<>'string'
            OR jsonb_typeof(e.value->'quantity')<>'number'
            OR e.value->>'product_id' !~ '^[A-Za-z0-9_-]{1,80}$'
            OR e.value->>'variant_id' !~ '^[A-Za-z0-9_-]{1,60}$'
            OR e.value->>'quantity' !~ '^[0-9]{1,2}$'
            OR (e.value->>'quantity')::integer NOT BETWEEN 1 AND 99
        ) OR (
          SELECT count(DISTINCT (e.value->>'product_id',e.value->>'variant_id'))
          FROM jsonb_array_elements(_items) e
        ) <> jsonb_array_length(_items)
        THEN RAISE EXCEPTION 'checkout_items_invalid_or_duplicate' USING ERRCODE='22023';
        END IF;

        v_hash:=encode(sha256(convert_to(_items::text,'UTF8')),'hex');
        SELECT * INTO v_existing FROM pos.sales
          WHERE organization_id=_owner AND request_id=_request FOR UPDATE;
        IF FOUND THEN
          IF v_existing.checkout_request_sha256 IS DISTINCT FROM v_hash
             OR v_existing.register_id<>_register
             OR v_existing.shift_id<>_shift
             OR v_existing.cashier_id<>v_actor
          THEN RAISE EXCEPTION 'checkout_replay_mismatch' USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('organization_id',_owner,
            'sale_id',v_existing.id,'status',v_existing.status,
            'total_cents',v_existing.total_cents,'replay',true);
        END IF;

        -- Lock each stock row in deterministic key order, preventing the
        -- cashier from overselling an already reserved item.
        FOR v_line IN
          SELECT value->>'product_id' AS product_id,
            value->>'variant_id' AS variant_id,
            (value->>'quantity')::integer AS quantity
          FROM jsonb_array_elements(_items)
          ORDER BY product_id,variant_id
        LOOP
          SELECT * INTO v_stock FROM pos.stock
            WHERE organization_id=_owner AND location_id=v_location
              AND product_id=v_line.product_id AND variant_id=v_line.variant_id
            FOR UPDATE;
          IF NOT FOUND OR v_stock.on_hand-v_stock.reserved<v_line.quantity THEN
            RAISE EXCEPTION 'checkout_insufficient_available_stock'
              USING ERRCODE='23514';
          END IF;
          SELECT * INTO v_product FROM pos.products
            WHERE organization_id=_owner AND id=v_line.product_id AND active FOR SHARE;
          IF NOT FOUND OR NOT EXISTS(
            SELECT 1 FROM pos.categories WHERE organization_id=_owner
              AND id=v_product.category_id AND active
          ) THEN RAISE EXCEPTION 'checkout_product_not_sellable' USING ERRCODE='22023'; END IF;
          SELECT * INTO v_price FROM pos.product_variants
            WHERE organization_id=_owner AND product_id=v_line.product_id
              AND id=v_line.variant_id FOR SHARE;
          IF NOT FOUND OR v_price.price_cents<=0 THEN
            RAISE EXCEPTION 'checkout_variant_not_sellable' USING ERRCODE='22023';
          END IF;
          v_sum:=v_sum+(v_line.quantity::bigint*v_price.price_cents);
          IF v_sum>100000000 THEN
            RAISE EXCEPTION 'checkout_total_out_of_range' USING ERRCODE='23514';
          END IF;
        END LOOP;

        v_sale:=gen_random_uuid();
        INSERT INTO pos.sales(
          id,organization_id,register_id,shift_id,cashier_id,
          request_id,checkout_request_sha256,subtotal_cents,total_cents)
        VALUES(v_sale,_owner,_register,_shift,v_actor,
          _request,v_hash,v_sum,v_sum);

        FOR v_line IN
          SELECT value->>'product_id' AS product_id,
            value->>'variant_id' AS variant_id,
            (value->>'quantity')::integer AS quantity
          FROM jsonb_array_elements(_items)
          ORDER BY product_id,variant_id
        LOOP
          v_lines:=v_lines+1;
          SELECT * INTO v_product FROM pos.products
            WHERE organization_id=_owner AND id=v_line.product_id AND active;
          SELECT * INTO v_price FROM pos.product_variants
            WHERE organization_id=_owner AND product_id=v_line.product_id
              AND id=v_line.variant_id;
          INSERT INTO pos.sale_lines(
            sale_id,organization_id,line_number,product_id,variant_id,
            product_name,variant_name,quantity,unit_price_cents,unit_cost_cents)
          VALUES(v_sale,_owner,v_lines,v_line.product_id,v_line.variant_id,
            v_product.name,v_price.name,v_line.quantity,
            v_price.price_cents,v_price.cost_cents);
          UPDATE pos.stock SET reserved=reserved+v_line.quantity,version=version+1
            WHERE organization_id=_owner AND location_id=v_location
              AND product_id=v_line.product_id AND variant_id=v_line.variant_id
              AND on_hand-reserved>=v_line.quantity;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'checkout_stock_conflict' USING ERRCODE='40001';
          END IF;
          INSERT INTO pos.sale_reservations(
            organization_id,sale_id,line_number,location_id,
            product_id,variant_id,quantity)
          VALUES(_owner,v_sale,v_lines,v_location,
            v_line.product_id,v_line.variant_id,v_line.quantity);
        END LOOP;
        INSERT INTO pos.transaction_audit(id,organization_id,actor_id,sale_id,action,request_id)
        VALUES(gen_random_uuid(),_owner,v_actor,v_sale,'sale_create',_request);
        RETURN jsonb_build_object('organization_id',_owner,
          'sale_id',v_sale,'status','awaiting_payment',
          'total_cents',v_sum,'replay',false);
      END $checkout$;
      REVOKE ALL ON FUNCTION pos.prepare_cashier_checkout(
        text,text,text,uuid,uuid,jsonb) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos.sale_reservations) THEN
          RAISE EXCEPTION 'pos_reservation_history_requires_forward_recovery'; END IF;
        IF EXISTS(SELECT 1 FROM pos.sales
          WHERE checkout_request_sha256 IS NOT NULL) THEN
          RAISE EXCEPTION 'pos_checkout_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION pos.prepare_cashier_checkout(text,text,text,uuid,uuid,jsonb);
      DROP TABLE pos.sale_reservations;
      ALTER TABLE pos.sales DROP CONSTRAINT ck_pos_checkout_hash;
      ALTER TABLE pos.sales DROP COLUMN checkout_request_sha256;
      DROP TABLE pos.checkout_policies;
    """);
}
