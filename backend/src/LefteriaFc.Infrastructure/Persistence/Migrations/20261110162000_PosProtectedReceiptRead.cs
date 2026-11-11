using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Tamper-checked receipt read for the original, currently assigned cashier.
/// Returns only the immutable fiscal snapshot and issued timestamp, not money
/// movement, payment tokens, keys, other customers, or database storage paths.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110230000_PosProtectedReceiptRead")]
public sealed class PosProtectedReceiptRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION pos_cashier.read_assigned_cashier_receipt(
        _digest text,_owner text,_receipt uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $receipt$
      DECLARE
        v_receipt pos_cashier.receipts%ROWTYPE;
        v_sale pos_cashier.sales%ROWTYPE;
        v_actor uuid;
        v_actual_hash text;
      BEGIN
        IF _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _receipt IS NULL
        THEN RAISE EXCEPTION 'pos_receipt_read_invalid' USING ERRCODE='22023'; END IF;
        SELECT * INTO v_receipt FROM pos_cashier.receipts
          WHERE organization_id=_owner AND id=_receipt;
        IF NOT FOUND THEN RAISE EXCEPTION 'pos_receipt_unavailable' USING ERRCODE='42501'; END IF;
        v_actor:=pos_cashier.current_cashier_authority(
          _digest,_owner,v_receipt.register_id);
        SELECT * INTO v_sale FROM pos_cashier.sales
          WHERE organization_id=_owner AND id=v_receipt.sale_id;
        IF NOT FOUND OR v_sale.cashier_id<>v_actor OR
          v_sale.register_id<>v_receipt.register_id OR v_sale.status<>'paid' THEN
          RAISE EXCEPTION 'pos_receipt_access_denied' USING ERRCODE='42501'; END IF;

        v_actual_hash:=encode(
          sha256(convert_to(v_receipt.immutable_payload::text,'UTF8')),'hex');
        IF v_actual_hash IS DISTINCT FROM v_receipt.payload_sha256
          OR (v_receipt.immutable_payload->>'total_cents')
               IS DISTINCT FROM v_sale.total_cents::text
          OR (v_receipt.immutable_payload->>'receipt_number')
               IS DISTINCT FROM v_receipt.receipt_number::text
          OR (v_receipt.immutable_payload->>'organization_id')
               IS DISTINCT FROM _owner
          OR (v_receipt.immutable_payload->>'sale_id')
               IS DISTINCT FROM v_receipt.sale_id::text
        THEN RAISE EXCEPTION 'pos_receipt_integrity_failed' USING ERRCODE='23514'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'receipt_id',_receipt,
          'sale_id',v_receipt.sale_id,'register_id',v_receipt.register_id,
          'receipt_number',v_receipt.receipt_number,
          'issued_at',v_receipt.issued_at,
          'payload_sha256',v_receipt.payload_sha256,
          'receipt',v_receipt.immutable_payload);
      END $receipt$;
      REVOKE ALL ON FUNCTION pos_cashier.read_assigned_cashier_receipt(text,text,uuid)
        FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION pos_cashier.read_assigned_cashier_receipt(text,text,uuid);
    """);
}