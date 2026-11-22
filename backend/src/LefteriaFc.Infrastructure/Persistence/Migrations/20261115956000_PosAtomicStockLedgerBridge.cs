using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Transactional forward-only bridge between the historical POS movement
/// ledger and named cashier reservations/receipts. Refuses diverged balances;
/// never backfills or assumes unapproved physical opening counts.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115956000_PosAtomicStockLedgerBridge")]
public sealed class PosAtomicStockLedgerBridge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- No automatic physical count / historical backfill.
      ALTER TABLE pos.stock_movements
        ADD COLUMN cashier_sale_id uuid;
      ALTER TABLE pos.stock_movements
        ADD CONSTRAINT fk_pos_stock_movement_cashier_sale
        FOREIGN KEY(organization_id,cashier_sale_id)
        REFERENCES pos_cashier.sales(organization_id,id) ON DELETE RESTRICT;
      ALTER TABLE pos.stock_movements
        DROP CONSTRAINT stock_movements_reason_check;
      ALTER TABLE pos.stock_movements
        DROP CONSTRAINT stock_movements_check1;
      ALTER TABLE pos.stock_movements
        ADD CONSTRAINT ck_pos_stock_reason_bridge
        CHECK(reason IN ('opening','purchase','sale','return','correction','cashier_sale'));
      ALTER TABLE pos.stock_movements
        ADD CONSTRAINT ck_pos_stock_origin_bridge
        CHECK(
          (reason IN ('sale','return')
            AND sale_id IS NOT NULL AND cashier_sale_id IS NULL)
          OR (reason='cashier_sale'
            AND sale_id IS NULL AND cashier_sale_id IS NOT NULL)
          OR (reason IN ('opening','purchase','correction')
            AND sale_id IS NULL AND cashier_sale_id IS NULL)
        );
      CREATE UNIQUE INDEX uq_pos_cashier_sale_stock_movement
        ON pos.stock_movements(cashier_sale_id,location_id,product_id,variant_id)
        WHERE cashier_sale_id IS NOT NULL;
      
      CREATE TABLE pos_cashier.stock_bridge_events(
        movement_id uuid PRIMARY KEY REFERENCES pos.stock_movements(id)
          ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL,
        location_id varchar(80) NOT NULL,
        product_id varchar(80) NOT NULL,
        variant_id varchar(60) NOT NULL,
        reason varchar(16) NOT NULL,
        legacy_before bigint NOT NULL CHECK(legacy_before BETWEEN 0 AND 100000000),
        legacy_after bigint NOT NULL CHECK(legacy_after BETWEEN 0 AND 100000000),
        cashier_before bigint NOT NULL CHECK(cashier_before BETWEEN 0 AND 1000000),
        cashier_after bigint NOT NULL CHECK(cashier_after BETWEEN 0 AND 1000000),
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        CHECK(legacy_before=cashier_before AND legacy_after=cashier_after),
        FOREIGN KEY(organization_id,location_id,product_id,variant_id)
          REFERENCES pos.location_stock(organization_id,location_id,product_id,variant_id)
          ON DELETE RESTRICT
      );
      ALTER TABLE pos_cashier.stock_bridge_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON pos_cashier.stock_bridge_events FROM PUBLIC;
      CREATE TRIGGER pos_stock_bridge_events_immutable
        BEFORE UPDATE OR DELETE ON pos_cashier.stock_bridge_events
        FOR EACH ROW EXECUTE FUNCTION pos.reject_private_pos_ledger_change();
      
      -- This trigger name sorts BEFORE pos_stock_movement_apply. Every legacy
      -- stock movement locks cashier stock first, then the existing legacy guard
      -- locks the store row. Receipt fulfillment follows the SAME lock order.
      CREATE FUNCTION pos_cashier.guard_legacy_stock_mirror()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $bridge_guard$
      DECLARE v_cashier bigint;v_reserved bigint;
      BEGIN
        IF NEW.reason='cashier_sale' THEN
          IF NOT EXISTS(SELECT 1 FROM pos_cashier.receipts rc
            WHERE rc.organization_id=NEW.organization_id
              AND rc.sale_id=NEW.cashier_sale_id)
          THEN RAISE EXCEPTION 'pos_stock_cashier_receipt_required'
            USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END IF;
        SELECT st.on_hand,st.reserved INTO v_cashier,v_reserved
          FROM pos_cashier.stock st
          WHERE st.organization_id=NEW.organization_id
            AND st.location_id=NEW.location_id
            AND st.product_id=NEW.product_id
            AND st.variant_id=NEW.variant_id FOR UPDATE;
        IF NOT FOUND THEN
          -- Only a zero-to-positive first movement is safe to initialize.
          -- Existing nonzero legacy balances require a physical count, NEVER
          -- an automatic presumed cashier opening quantity.
          IF NEW.before_quantity<>0 OR NEW.delta<=0 THEN
            RAISE EXCEPTION 'pos_stock_bridge_initial_count_required'
              USING ERRCODE='23514'; END IF;
          INSERT INTO pos_cashier.stock(
            organization_id,location_id,product_id,variant_id,on_hand,reserved)
          VALUES(NEW.organization_id,NEW.location_id,NEW.product_id,
            NEW.variant_id,0,0)
          ON CONFLICT DO NOTHING;
          SELECT st.on_hand,st.reserved INTO v_cashier,v_reserved
            FROM pos_cashier.stock st
            WHERE st.organization_id=NEW.organization_id
              AND st.location_id=NEW.location_id
              AND st.product_id=NEW.product_id
              AND st.variant_id=NEW.variant_id FOR UPDATE;
        END IF;
        IF v_cashier IS DISTINCT FROM NEW.before_quantity
          OR v_cashier+NEW.delta NOT BETWEEN v_reserved AND 1000000
        THEN RAISE EXCEPTION 'pos_stock_bridge_balance_conflict'
          USING ERRCODE='23514'; END IF;
        RETURN NEW;
      END $bridge_guard$;
      CREATE TRIGGER pos_a_cashier_mirror_guard
        BEFORE INSERT ON pos.stock_movements FOR EACH ROW
        EXECUTE FUNCTION pos_cashier.guard_legacy_stock_mirror();
      
      CREATE FUNCTION pos_cashier.commit_legacy_stock_mirror()
      RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $bridge_commit$
      BEGIN
        -- Cashier-sale fulfillment updates cashier stock in its original trigger;
        -- never double-apply the same sale through the reverse mirror.
        IF NEW.reason='cashier_sale' THEN RETURN NEW; END IF;
        UPDATE pos_cashier.stock SET on_hand=NEW.after_quantity,version=version+1
          WHERE organization_id=NEW.organization_id AND location_id=NEW.location_id
            AND product_id=NEW.product_id AND variant_id=NEW.variant_id
            AND on_hand=NEW.before_quantity;
        IF NOT FOUND THEN RAISE EXCEPTION 'pos_stock_bridge_mirror_conflict'
          USING ERRCODE='23514'; END IF;
        INSERT INTO pos_cashier.stock_bridge_events(
          movement_id,organization_id,location_id,product_id,variant_id,reason,
          legacy_before,legacy_after,cashier_before,cashier_after)
        VALUES(NEW.id,NEW.organization_id,NEW.location_id,NEW.product_id,
          NEW.variant_id,NEW.reason,NEW.before_quantity,NEW.after_quantity,
          NEW.before_quantity,NEW.after_quantity);
        RETURN NEW;
      END $bridge_commit$;
      CREATE TRIGGER pos_z_cashier_mirror_commit
        AFTER INSERT ON pos.stock_movements FOR EACH ROW
        EXECUTE FUNCTION pos_cashier.commit_legacy_stock_mirror();
      
      CREATE OR REPLACE FUNCTION pos.receive_admin_stock(
              _digest text,_owner text,_location text,_product text,_variant text,
              _quantity integer,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $stock_receive$
            DECLARE
              v_actor uuid; v_session uuid;
              v_before bigint;
              v_previous pos.stock_movements%ROWTYPE;
            BEGIN
              IF _request IS NULL OR _quantity IS NULL
                OR _quantity NOT BETWEEN 1 AND 100000
                OR _location IS NULL OR _location !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _product IS NULL OR _product !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _variant IS NULL OR _variant !~ '^[A-Za-z0-9_-]{1,60}$'
              THEN RAISE EXCEPTION 'pos_stock_receipt_invalid' USING ERRCODE='22023';
              END IF;
              SELECT actor_id,session_id INTO v_actor,v_session
                FROM pos.require_admin_location_owner(_digest,_owner);
              -- Serialize exact request replays to avoid duplicate stock receipts.
              PERFORM pg_advisory_xact_lock(hashtext(_request::text));
              SELECT * INTO v_previous FROM pos.stock_movements
                WHERE request_id=_request;
              IF FOUND THEN
                IF v_previous.organization_id<>_owner OR
                  v_previous.location_id<>_location OR
                  v_previous.product_id<>_product OR
                  v_previous.variant_id<>_variant OR
                  v_previous.delta<>_quantity OR
                  v_previous.reason<>'purchase' OR
                  v_previous.actor_admin_id<>v_actor THEN
                  RAISE EXCEPTION 'pos_stock_receipt_reused_request'
                    USING ERRCODE='23505';
                END IF;
                RETURN jsonb_build_object('organization_id',_owner,
                  'location_id',_location,'product_id',_product,
                  'variant_id',_variant,'quantity',_quantity,
                  'after_quantity',v_previous.after_quantity,'replay',true);
              END IF;
              IF NOT EXISTS(SELECT 1 FROM pos.locations
                WHERE organization_id=_owner AND id=_location AND active)
                OR NOT EXISTS(SELECT 1 FROM pos.location_products
                WHERE organization_id=_owner AND location_id=_location
                  AND product_id=_product AND enabled)
                OR NOT EXISTS(SELECT 1 FROM pos.product_variants
                WHERE organization_id=_owner AND product_id=_product AND id=_variant)
              THEN RAISE EXCEPTION 'pos_stock_product_not_available'
                USING ERRCODE='42501';
              END IF;
              INSERT INTO pos.location_stock(
                organization_id,location_id,product_id,variant_id,on_hand)
              VALUES(_owner,_location,_product,_variant,0)
              ON CONFLICT DO NOTHING;
              -- Physical stock receipts must lock cashier SKU before store SKU.
              -- This keeps consistent lock order with cashier sale fulfillment.
              PERFORM 1 FROM pos_cashier.stock
                WHERE organization_id=_owner AND location_id=_location
                  AND product_id=_product AND variant_id=_variant
                FOR UPDATE;
              SELECT on_hand INTO v_before FROM pos.location_stock
                WHERE organization_id=_owner AND location_id=_location
                  AND product_id=_product AND variant_id=_variant FOR UPDATE;
              IF v_before+_quantity>100000000 THEN
                RAISE EXCEPTION 'pos_stock_limit_exceeded' USING ERRCODE='54000';
              END IF;
              INSERT INTO pos.stock_movements(
                id,organization_id,location_id,product_id,variant_id,
                request_id,reason,delta,before_quantity,after_quantity,actor_admin_id)
              VALUES(gen_random_uuid(),_owner,_location,_product,_variant,
                _request,'purchase',_quantity,v_before,v_before+_quantity,v_actor);
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.pos.stock.receive','succeeded',_owner,'purchase');
              RETURN jsonb_build_object('organization_id',_owner,
                'location_id',_location,'product_id',_product,'variant_id',_variant,
                'quantity',_quantity,'after_quantity',v_before+_quantity,
                'replay',false);
            END $stock_receive$;
      REVOKE ALL ON FUNCTION pos.receive_admin_stock(
        text,text,text,text,text,integer,uuid) FROM PUBLIC;

      CREATE OR REPLACE FUNCTION pos_cashier.fulfill_receipt_reserved_inventory()
            RETURNS trigger LANGUAGE plpgsql
            SET search_path=pg_catalog,pg_temp AS $fulfill$
            DECLARE
              v_sale pos_cashier.sales%ROWTYPE;
              v_line_count integer;
              v_reserved_count integer;
              v_row record;
              v_before bigint;
              v_movement uuid;
            BEGIN
              SELECT * INTO v_sale FROM pos_cashier.sales
                WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
              IF NOT FOUND OR v_sale.status<>'paid' THEN
                RAISE EXCEPTION 'inventory_fulfillment_requires_paid_sale'
                  USING ERRCODE='42501';
              END IF;
              SELECT count(*) INTO v_line_count FROM pos_cashier.sale_lines
                WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id;
              SELECT count(*) INTO v_reserved_count FROM pos_cashier.sale_reservations
                WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id
                  AND released_at IS NULL AND consumed_at IS NULL;
              -- Count equality alone is insufficient: every reserved line must
              -- refer to the exact priced product, variant and quantity sold.
              IF EXISTS(
                SELECT 1 FROM pos_cashier.sale_reservations rs
                JOIN pos_cashier.sale_lines sl ON sl.sale_id=rs.sale_id
                  AND sl.line_number=rs.line_number
                WHERE rs.organization_id=NEW.organization_id
                  AND rs.sale_id=NEW.sale_id
                  AND (rs.product_id<>sl.product_id
                    OR rs.variant_id<>sl.variant_id
                    OR rs.quantity<>sl.quantity)
              ) THEN
                RAISE EXCEPTION 'inventory_receipt_reservation_line_mismatch'
                  USING ERRCODE='23514';
              END IF;
              IF v_line_count=0 OR v_line_count<>v_reserved_count THEN
                RAISE EXCEPTION 'inventory_receipt_reservations_incomplete'
                  USING ERRCODE='23514';
              END IF;
              FOR v_row IN
                SELECT rs.location_id,rs.product_id,rs.variant_id,rs.quantity,
                       rs.line_number,rs.sale_id
                FROM pos_cashier.sale_reservations rs
                WHERE rs.organization_id=NEW.organization_id AND rs.sale_id=NEW.sale_id
                  AND rs.released_at IS NULL AND rs.consumed_at IS NULL
                ORDER BY rs.location_id,rs.product_id,rs.variant_id
                FOR UPDATE
              LOOP
                SELECT on_hand INTO v_before FROM pos_cashier.stock
                  WHERE organization_id=NEW.organization_id
                    AND location_id=v_row.location_id
                    AND product_id=v_row.product_id
                    AND variant_id=v_row.variant_id
                    AND reserved>=v_row.quantity AND on_hand>=v_row.quantity
                  FOR UPDATE;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'inventory_receipt_cashier_stock_missing'
                    USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS(SELECT 1 FROM pos.location_products
                    WHERE organization_id=NEW.organization_id
                      AND location_id=v_row.location_id
                      AND product_id=v_row.product_id AND enabled)
                THEN RAISE EXCEPTION 'inventory_receipt_product_not_assigned'
                  USING ERRCODE='23514'; END IF;
                -- No live cash ledger or receipt survives a mismatched store balance.
                -- The legacy movement trigger checks its independently locked row.
                INSERT INTO pos.stock_movements(
                  id,organization_id,location_id,product_id,variant_id,
                  request_id,cashier_sale_id,reason,delta,
                  before_quantity,after_quantity,actor_admin_id
                ) VALUES(
                  gen_random_uuid(),NEW.organization_id,v_row.location_id,
                  v_row.product_id,v_row.variant_id,gen_random_uuid(),
                  NEW.sale_id,'cashier_sale',-v_row.quantity,
                  v_before,v_before-v_row.quantity,v_sale.cashier_id
                ) RETURNING id INTO v_movement;
                UPDATE pos_cashier.stock
                  SET on_hand=on_hand-v_row.quantity,
                      reserved=reserved-v_row.quantity,
                      version=version+1
                  WHERE organization_id=NEW.organization_id
                    AND location_id=v_row.location_id
                    AND product_id=v_row.product_id
                    AND variant_id=v_row.variant_id
                    AND reserved>=v_row.quantity AND on_hand>=v_row.quantity;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'inventory_receipt_stock_mismatch'
                    USING ERRCODE='23514';
                END IF;
                INSERT INTO pos_cashier.stock_bridge_events(
                  movement_id,organization_id,location_id,product_id,variant_id,
                  reason,legacy_before,legacy_after,cashier_before,cashier_after
                ) VALUES(v_movement,NEW.organization_id,v_row.location_id,
                  v_row.product_id,v_row.variant_id,'cashier_sale',
                  v_before,v_before-v_row.quantity,v_before,v_before-v_row.quantity);
                UPDATE pos_cashier.sale_reservations SET consumed_at=clock_timestamp()
                  WHERE sale_id=v_row.sale_id AND line_number=v_row.line_number
                    AND released_at IS NULL AND consumed_at IS NULL;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'inventory_receipt_double_consumption'
                    USING ERRCODE='23505';
                END IF;
              END LOOP;
              RETURN NEW;
            END $fulfill$;
      REVOKE ALL ON FUNCTION pos_cashier.guard_legacy_stock_mirror() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.commit_legacy_stock_mirror() FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos_cashier.fulfill_receipt_reserved_inventory() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.stock_bridge_events) THEN
          RAISE EXCEPTION 'pos_stock_bridge_history_requires_forward_recovery'
            USING ERRCODE='23514'; END IF;
      END $guard$;
      CREATE OR REPLACE FUNCTION pos_cashier.fulfill_receipt_reserved_inventory()
            RETURNS trigger LANGUAGE plpgsql
            SET search_path=pg_catalog,pg_temp AS $fulfill$
            DECLARE
              v_sale pos_cashier.sales%ROWTYPE;
              v_line_count integer;
              v_reserved_count integer;
              v_row record;
            BEGIN
              SELECT * INTO v_sale FROM pos_cashier.sales
                WHERE organization_id=NEW.organization_id AND id=NEW.sale_id FOR UPDATE;
              IF NOT FOUND OR v_sale.status<>'paid' THEN
                RAISE EXCEPTION 'inventory_fulfillment_requires_paid_sale'
                  USING ERRCODE='42501';
              END IF;
              SELECT count(*) INTO v_line_count FROM pos_cashier.sale_lines
                WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id;
              SELECT count(*) INTO v_reserved_count FROM pos_cashier.sale_reservations
                WHERE organization_id=NEW.organization_id AND sale_id=NEW.sale_id
                  AND released_at IS NULL AND consumed_at IS NULL;
              -- Count equality alone is insufficient: every reserved line must
              -- refer to the exact priced product, variant and quantity sold.
              IF EXISTS(
                SELECT 1 FROM pos_cashier.sale_reservations rs
                JOIN pos_cashier.sale_lines sl ON sl.sale_id=rs.sale_id
                  AND sl.line_number=rs.line_number
                WHERE rs.organization_id=NEW.organization_id
                  AND rs.sale_id=NEW.sale_id
                  AND (rs.product_id<>sl.product_id
                    OR rs.variant_id<>sl.variant_id
                    OR rs.quantity<>sl.quantity)
              ) THEN
                RAISE EXCEPTION 'inventory_receipt_reservation_line_mismatch'
                  USING ERRCODE='23514';
              END IF;
              IF v_line_count=0 OR v_line_count<>v_reserved_count THEN
                RAISE EXCEPTION 'inventory_receipt_reservations_incomplete'
                  USING ERRCODE='23514';
              END IF;
              FOR v_row IN
                SELECT rs.location_id,rs.product_id,rs.variant_id,rs.quantity,
                       rs.line_number,rs.sale_id
                FROM pos_cashier.sale_reservations rs
                WHERE rs.organization_id=NEW.organization_id AND rs.sale_id=NEW.sale_id
                  AND rs.released_at IS NULL AND rs.consumed_at IS NULL
                ORDER BY rs.location_id,rs.product_id,rs.variant_id
                FOR UPDATE
              LOOP
                UPDATE pos_cashier.stock
                  SET on_hand=on_hand-v_row.quantity,
                      reserved=reserved-v_row.quantity,
                      version=version+1
                  WHERE organization_id=NEW.organization_id
                    AND location_id=v_row.location_id
                    AND product_id=v_row.product_id
                    AND variant_id=v_row.variant_id
                    AND reserved>=v_row.quantity AND on_hand>=v_row.quantity;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'inventory_receipt_stock_mismatch'
                    USING ERRCODE='23514';
                END IF;
                UPDATE pos_cashier.sale_reservations SET consumed_at=clock_timestamp()
                  WHERE sale_id=v_row.sale_id AND line_number=v_row.line_number
                    AND released_at IS NULL AND consumed_at IS NULL;
                IF NOT FOUND THEN
                  RAISE EXCEPTION 'inventory_receipt_double_consumption'
                    USING ERRCODE='23505';
                END IF;
              END LOOP;
              RETURN NEW;
            END $fulfill$;
      REVOKE ALL ON FUNCTION pos_cashier.fulfill_receipt_reserved_inventory() FROM PUBLIC;
      CREATE OR REPLACE FUNCTION pos.receive_admin_stock(
              _digest text,_owner text,_location text,_product text,_variant text,
              _quantity integer,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $stock_receive$
            DECLARE
              v_actor uuid; v_session uuid;
              v_before bigint;
              v_previous pos.stock_movements%ROWTYPE;
            BEGIN
              IF _request IS NULL OR _quantity IS NULL
                OR _quantity NOT BETWEEN 1 AND 100000
                OR _location IS NULL OR _location !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _product IS NULL OR _product !~ '^[A-Za-z0-9_-]{1,80}$'
                OR _variant IS NULL OR _variant !~ '^[A-Za-z0-9_-]{1,60}$'
              THEN RAISE EXCEPTION 'pos_stock_receipt_invalid' USING ERRCODE='22023';
              END IF;
              SELECT actor_id,session_id INTO v_actor,v_session
                FROM pos.require_admin_location_owner(_digest,_owner);
              -- Serialize exact request replays to avoid duplicate stock receipts.
              PERFORM pg_advisory_xact_lock(hashtext(_request::text));
              SELECT * INTO v_previous FROM pos.stock_movements
                WHERE request_id=_request;
              IF FOUND THEN
                IF v_previous.organization_id<>_owner OR
                  v_previous.location_id<>_location OR
                  v_previous.product_id<>_product OR
                  v_previous.variant_id<>_variant OR
                  v_previous.delta<>_quantity OR
                  v_previous.reason<>'purchase' OR
                  v_previous.actor_admin_id<>v_actor THEN
                  RAISE EXCEPTION 'pos_stock_receipt_reused_request'
                    USING ERRCODE='23505';
                END IF;
                RETURN jsonb_build_object('organization_id',_owner,
                  'location_id',_location,'product_id',_product,
                  'variant_id',_variant,'quantity',_quantity,
                  'after_quantity',v_previous.after_quantity,'replay',true);
              END IF;
              IF NOT EXISTS(SELECT 1 FROM pos.locations
                WHERE organization_id=_owner AND id=_location AND active)
                OR NOT EXISTS(SELECT 1 FROM pos.location_products
                WHERE organization_id=_owner AND location_id=_location
                  AND product_id=_product AND enabled)
                OR NOT EXISTS(SELECT 1 FROM pos.product_variants
                WHERE organization_id=_owner AND product_id=_product AND id=_variant)
              THEN RAISE EXCEPTION 'pos_stock_product_not_available'
                USING ERRCODE='42501';
              END IF;
              INSERT INTO pos.location_stock(
                organization_id,location_id,product_id,variant_id,on_hand)
              VALUES(_owner,_location,_product,_variant,0)
              ON CONFLICT DO NOTHING;
              SELECT on_hand INTO v_before FROM pos.location_stock
                WHERE organization_id=_owner AND location_id=_location
                  AND product_id=_product AND variant_id=_variant FOR UPDATE;
              IF v_before+_quantity>100000000 THEN
                RAISE EXCEPTION 'pos_stock_limit_exceeded' USING ERRCODE='54000';
              END IF;
              INSERT INTO pos.stock_movements(
                id,organization_id,location_id,product_id,variant_id,
                request_id,reason,delta,before_quantity,after_quantity,actor_admin_id)
              VALUES(gen_random_uuid(),_owner,_location,_product,_variant,
                _request,'purchase',_quantity,v_before,v_before+_quantity,v_actor);
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code)
              VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.pos.stock.receive','succeeded',_owner,'purchase');
              RETURN jsonb_build_object('organization_id',_owner,
                'location_id',_location,'product_id',_product,'variant_id',_variant,
                'quantity',_quantity,'after_quantity',v_before+_quantity,
                'replay',false);
            END $stock_receive$;
      REVOKE ALL ON FUNCTION pos.receive_admin_stock(
        text,text,text,text,text,integer,uuid) FROM PUBLIC;

      DROP TRIGGER pos_a_cashier_mirror_guard ON pos.stock_movements;
      DROP TRIGGER pos_z_cashier_mirror_commit ON pos.stock_movements;
      DROP FUNCTION pos_cashier.guard_legacy_stock_mirror();
      DROP FUNCTION pos_cashier.commit_legacy_stock_mirror();
      DROP TRIGGER pos_stock_bridge_events_immutable ON pos_cashier.stock_bridge_events;
      DROP TABLE pos_cashier.stock_bridge_events;
      DROP INDEX pos.uq_pos_cashier_sale_stock_movement;
      ALTER TABLE pos.stock_movements DROP CONSTRAINT ck_pos_stock_reason_bridge;
      ALTER TABLE pos.stock_movements DROP CONSTRAINT ck_pos_stock_origin_bridge;
      ALTER TABLE pos.stock_movements DROP CONSTRAINT fk_pos_stock_movement_cashier_sale;
      ALTER TABLE pos.stock_movements
        ADD CONSTRAINT stock_movements_reason_check
        CHECK(reason IN ('opening','purchase','sale','return','correction'));
      ALTER TABLE pos.stock_movements
        ADD CONSTRAINT stock_movements_check1
        CHECK((reason IN ('sale','return'))=(sale_id IS NOT NULL));
      ALTER TABLE pos.stock_movements DROP COLUMN cashier_sale_id;
    """);
}
