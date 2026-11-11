using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-only location product assignment and positive stock receiving.
/// No cashier sale, card processing or refund endpoint is enabled.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110190000_PosAdminStockOperations")]
public sealed class PosAdminStockOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos.write_admin_location_product(
        _digest text,_owner text,_location text,_product text,
        _enabled boolean,_expected bigint
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $location_product_write$
      DECLARE
        v_actor uuid; v_session uuid; v_version bigint;
      BEGIN
        IF _location IS NULL OR _location !~ '^[A-Za-z0-9_-]{1,80}$'
          OR _product IS NULL OR _product !~ '^[A-Za-z0-9_-]{1,80}$'
          OR _enabled IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN -1 AND 9007199254740991 THEN
          RAISE EXCEPTION 'pos_location_product_invalid' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM pos.require_admin_location_owner(_digest,_owner);
        IF NOT EXISTS (SELECT 1 FROM pos.locations
          WHERE organization_id=_owner AND id=_location AND active)
          OR NOT EXISTS (SELECT 1 FROM pos.products
          WHERE organization_id=_owner AND id=_product
            AND (active OR NOT _enabled)) THEN
          RAISE EXCEPTION 'pos_location_product_unavailable' USING ERRCODE='22023';
        END IF;
        IF _expected=-1 THEN
          INSERT INTO pos.location_products(
            organization_id,location_id,product_id,enabled)
          VALUES(_owner,_location,_product,_enabled)
          ON CONFLICT DO NOTHING RETURNING version INTO v_version;
        ELSE
          UPDATE pos.location_products
            SET enabled=_enabled,version=version+1
          WHERE organization_id=_owner AND location_id=_location
            AND product_id=_product AND version=_expected
            AND version<9007199254740991
          RETURNING version INTO v_version;
        END IF;
        IF v_version IS NULL THEN
          RAISE EXCEPTION 'pos_location_product_conflict' USING ERRCODE='23505';
        END IF;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.pos.location.product','succeeded',_owner,
          CASE WHEN _enabled THEN 'enabled' ELSE 'disabled' END);
        RETURN jsonb_build_object('organization_id',_owner,
          'location_id',_location,'product_id',_product,
          'enabled',_enabled,'version',v_version);
      END $location_product_write$;

      CREATE FUNCTION pos.receive_admin_stock(
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
      REVOKE ALL ON FUNCTION pos.write_admin_location_product(
        text,text,text,text,boolean,bigint) FROM PUBLIC;
      REVOKE ALL ON FUNCTION pos.receive_admin_stock(
        text,text,text,text,text,integer,uuid) FROM PUBLIC;
    """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos.receive_admin_stock(text,text,text,text,text,integer,uuid);
      DROP FUNCTION pos.write_admin_location_product(text,text,text,text,boolean,bigint);
    """);
}