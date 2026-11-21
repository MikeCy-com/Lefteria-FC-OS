using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Permit the named, active, assigned Cashier to issue the original receipt
/// only for the exact sale and still-open shift they own. Without this fix
/// cash tender always rolled back at receipt creation for cashier accounts.
/// Keep fiscal policies and database runtime grants default-denied.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115953000_PosCashierAtomicReceiptAuthority")]
public sealed class PosCashierAtomicReceiptAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION pos_cashier.issue_verified_internal_receipt(
              _digest text,_owner text,_sale uuid,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $pos_issue_receipt$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_sale pos_cashier.sales%ROWTYPE;
              v_policy text;
              v_number bigint;
              v_receipt uuid;
              v_data jsonb;
              v_lines jsonb;
              v_verified bigint;
              v_line_sum bigint;
              v_exists pos_cashier.receipts%ROWTYPE;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _sale IS NULL OR _request IS NULL
              THEN RAISE EXCEPTION 'receipt_input_invalid' USING ERRCODE='22023';
              END IF;
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin','cashier') THEN
                RAISE EXCEPTION 'receipt_actor_not_authorized' USING ERRCODE='42501';
              END IF;
              -- Cashier access is narrowly confined to that cashier's own
              -- currently assigned register and OPEN exact shift. A global role or
              -- the mere ability to call the function can never issue a receipt for
              -- someone else's paid sale; original manager behavior is preserved.
              IF v_role='cashier' THEN
                SELECT * INTO v_sale FROM pos_cashier.sales
                  WHERE organization_id=_owner AND id=_sale;
                IF NOT FOUND OR v_sale.cashier_id<>v_actor OR
                   pos_cashier.current_cashier_authority(
                     _digest,_owner,v_sale.register_id)<>v_actor
                THEN RAISE EXCEPTION 'receipt_cashier_scope_denied'
                  USING ERRCODE='42501'; END IF;
                IF NOT EXISTS(
                  SELECT 1 FROM pos_cashier.shifts sh
                  WHERE sh.id=v_sale.shift_id AND sh.organization_id=_owner
                    AND sh.register_id=v_sale.register_id
                    AND sh.cashier_id=v_actor AND sh.closed_at IS NULL
                ) THEN RAISE EXCEPTION 'receipt_cashier_shift_closed'
                  USING ERRCODE='42501'; END IF;
              END IF;
              SELECT policy_version INTO v_policy FROM pos_cashier.receipt_policies
                WHERE organization_id=_owner AND enabled
                  AND reviewed_at<=clock_timestamp() FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'fiscal_receipt_policy_disabled' USING ERRCODE='55000';
              END IF;
              SELECT * INTO v_sale FROM pos_cashier.sales
                WHERE organization_id=_owner AND id=_sale FOR UPDATE;
              IF v_role='cashier' AND
                 (NOT FOUND OR v_sale.cashier_id<>v_actor OR
                   NOT EXISTS(
                     SELECT 1 FROM pos_cashier.shifts sh
                     WHERE sh.id=v_sale.shift_id AND sh.organization_id=_owner
                       AND sh.register_id=v_sale.register_id
                       AND sh.cashier_id=v_actor AND sh.closed_at IS NULL)
                  OR pos_cashier.current_cashier_authority(
                    _digest,_owner,v_sale.register_id)<>v_actor)
              THEN RAISE EXCEPTION 'receipt_cashier_scope_changed'
                USING ERRCODE='42501'; END IF;
              IF NOT FOUND OR v_sale.status='voided' THEN
                RAISE EXCEPTION 'sale_unavailable_for_receipt' USING ERRCODE='42501';
              END IF;
              SELECT * INTO v_exists FROM pos_cashier.receipts
                WHERE organization_id=_owner AND sale_id=_sale;
              IF FOUND THEN
                RETURN jsonb_build_object('id',v_exists.id,
                  'receipt_number',v_exists.receipt_number,
                  'sale_id',_sale,'replay',true);
              END IF;
              SELECT coalesce(sum(amount_cents),0) INTO v_verified
                FROM pos_cashier.payment_events
                WHERE organization_id=_owner AND sale_id=_sale AND status='verified';
              SELECT coalesce(sum(line_total_cents),0) INTO v_line_sum
                FROM pos_cashier.sale_lines WHERE organization_id=_owner AND sale_id=_sale;
              IF v_verified<>v_sale.total_cents OR v_line_sum<>v_sale.subtotal_cents
                OR v_sale.currency<>'EUR' OR v_sale.status NOT IN
                  ('awaiting_payment','partially_paid','paid') THEN
                RAISE EXCEPTION 'receipt_requires_exact_verified_payment'
                  USING ERRCODE='23514';
              END IF;
      
              IF v_sale.status<>'paid' THEN
                UPDATE pos_cashier.sales SET status='paid',paid_at=clock_timestamp()
                  WHERE organization_id=_owner AND id=_sale;
              END IF;
      
              -- Atomic register-level number assignment, serialized by the
              -- receipt_sequences row lock. Never allocate a number in the UI.
              INSERT INTO pos_cashier.receipt_sequences(organization_id,register_id,next_number)
              VALUES(_owner,v_sale.register_id,1)
              ON CONFLICT(organization_id,register_id) DO NOTHING;
              UPDATE pos_cashier.receipt_sequences SET next_number=next_number+1
                WHERE organization_id=_owner AND register_id=v_sale.register_id
                  AND next_number<999999999
                RETURNING next_number-1 INTO v_number;
              IF v_number IS NULL THEN
                RAISE EXCEPTION 'receipt_sequence_exhausted' USING ERRCODE='54000';
              END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'line_number',line_number,'product_id',product_id,
                'variant_id',variant_id,'product_name',product_name,
                'variant_name',variant_name,'quantity',quantity,
                'unit_price_cents',unit_price_cents,
                'line_total_cents',line_total_cents)
                ORDER BY line_number),'[]'::jsonb) INTO v_lines
                FROM pos_cashier.sale_lines
                WHERE organization_id=_owner AND sale_id=_sale;
              v_data:=jsonb_build_object(
                'schema_version',1,'receipt_kind','pos_sale',
                'organization_id',_owner,'register_id',v_sale.register_id,
                'sale_id',_sale,'receipt_number',v_number,
                'currency','EUR','subtotal_cents',v_sale.subtotal_cents,
                'tax_cents',v_sale.tax_cents,'discount_cents',v_sale.discount_cents,
                'total_cents',v_sale.total_cents,'lines',v_lines,
                'fiscal_policy_version',v_policy
              );
              v_receipt:=gen_random_uuid();
              INSERT INTO pos_cashier.receipts(
                id,organization_id,sale_id,register_id,
                receipt_number,immutable_payload,payload_sha256
              ) VALUES(v_receipt,_owner,_sale,v_sale.register_id,v_number,
                v_data,encode(sha256(convert_to(v_data::text,'UTF8')),'hex'));
              INSERT INTO pos_cashier.transaction_audit(
                id,organization_id,actor_id,sale_id,action,request_id)
              VALUES(gen_random_uuid(),_owner,v_actor,_sale,'receipt_issue',_request);
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code
              ) VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.pos_cashier.receipt.issue','succeeded',_owner,'verified_capture');
              RETURN jsonb_build_object('id',v_receipt,'sale_id',_sale,
                'receipt_number',v_number,'replay',false);
            END $pos_issue_receipt$;
      REVOKE ALL ON FUNCTION pos_cashier.issue_verified_internal_receipt(
        text,text,uuid,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $rollback_guard$ BEGIN
        IF EXISTS(SELECT 1 FROM pos_cashier.receipts r
          JOIN pos_cashier.sales s ON s.organization_id=r.organization_id
            AND s.id=r.sale_id
          JOIN identity.admin_accounts a ON a.id=s.cashier_id
          WHERE a.role='cashier')
        THEN RAISE EXCEPTION 'cashier_receipt_history_requires_forward_recovery'
          USING ERRCODE='23514'; END IF;
      END $rollback_guard$;
      CREATE OR REPLACE FUNCTION pos_cashier.issue_verified_internal_receipt(
              _digest text,_owner text,_sale uuid,_request uuid
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp AS $pos_issue_receipt$
            DECLARE
              v_actor uuid; v_session uuid; v_role text;
              v_sale pos_cashier.sales%ROWTYPE;
              v_policy text;
              v_number bigint;
              v_receipt uuid;
              v_data jsonb;
              v_lines jsonb;
              v_verified bigint;
              v_line_sum bigint;
              v_exists pos_cashier.receipts%ROWTYPE;
            BEGIN
              IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
                OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
                OR _sale IS NULL OR _request IS NULL
              THEN RAISE EXCEPTION 'receipt_input_invalid' USING ERRCODE='22023';
              END IF;
              PERFORM identity.lock_admin_club_authority(_digest,_owner);
              SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
                FROM identity.admin_sessions s
                JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                JOIN identity.admin_organization_grants g
                  ON g.admin_account_id=a.id AND g.organization_id=_owner
                WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                  AND s.expires_at>clock_timestamp() AND a.is_active
                  AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                  AND a.password_hash IS NOT NULL;
              IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
                RAISE EXCEPTION 'receipt_actor_not_authorized' USING ERRCODE='42501';
              END IF;
              SELECT policy_version INTO v_policy FROM pos_cashier.receipt_policies
                WHERE organization_id=_owner AND enabled
                  AND reviewed_at<=clock_timestamp() FOR SHARE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'fiscal_receipt_policy_disabled' USING ERRCODE='55000';
              END IF;
              SELECT * INTO v_sale FROM pos_cashier.sales
                WHERE organization_id=_owner AND id=_sale FOR UPDATE;
              IF NOT FOUND OR v_sale.status='voided' THEN
                RAISE EXCEPTION 'sale_unavailable_for_receipt' USING ERRCODE='42501';
              END IF;
              SELECT * INTO v_exists FROM pos_cashier.receipts
                WHERE organization_id=_owner AND sale_id=_sale;
              IF FOUND THEN
                RETURN jsonb_build_object('id',v_exists.id,
                  'receipt_number',v_exists.receipt_number,
                  'sale_id',_sale,'replay',true);
              END IF;
              SELECT coalesce(sum(amount_cents),0) INTO v_verified
                FROM pos_cashier.payment_events
                WHERE organization_id=_owner AND sale_id=_sale AND status='verified';
              SELECT coalesce(sum(line_total_cents),0) INTO v_line_sum
                FROM pos_cashier.sale_lines WHERE organization_id=_owner AND sale_id=_sale;
              IF v_verified<>v_sale.total_cents OR v_line_sum<>v_sale.subtotal_cents
                OR v_sale.currency<>'EUR' OR v_sale.status NOT IN
                  ('awaiting_payment','partially_paid','paid') THEN
                RAISE EXCEPTION 'receipt_requires_exact_verified_payment'
                  USING ERRCODE='23514';
              END IF;
      
              IF v_sale.status<>'paid' THEN
                UPDATE pos_cashier.sales SET status='paid',paid_at=clock_timestamp()
                  WHERE organization_id=_owner AND id=_sale;
              END IF;
      
              -- Atomic register-level number assignment, serialized by the
              -- receipt_sequences row lock. Never allocate a number in the UI.
              INSERT INTO pos_cashier.receipt_sequences(organization_id,register_id,next_number)
              VALUES(_owner,v_sale.register_id,1)
              ON CONFLICT(organization_id,register_id) DO NOTHING;
              UPDATE pos_cashier.receipt_sequences SET next_number=next_number+1
                WHERE organization_id=_owner AND register_id=v_sale.register_id
                  AND next_number<999999999
                RETURNING next_number-1 INTO v_number;
              IF v_number IS NULL THEN
                RAISE EXCEPTION 'receipt_sequence_exhausted' USING ERRCODE='54000';
              END IF;
              SELECT coalesce(jsonb_agg(jsonb_build_object(
                'line_number',line_number,'product_id',product_id,
                'variant_id',variant_id,'product_name',product_name,
                'variant_name',variant_name,'quantity',quantity,
                'unit_price_cents',unit_price_cents,
                'line_total_cents',line_total_cents)
                ORDER BY line_number),'[]'::jsonb) INTO v_lines
                FROM pos_cashier.sale_lines
                WHERE organization_id=_owner AND sale_id=_sale;
              v_data:=jsonb_build_object(
                'schema_version',1,'receipt_kind','pos_sale',
                'organization_id',_owner,'register_id',v_sale.register_id,
                'sale_id',_sale,'receipt_number',v_number,
                'currency','EUR','subtotal_cents',v_sale.subtotal_cents,
                'tax_cents',v_sale.tax_cents,'discount_cents',v_sale.discount_cents,
                'total_cents',v_sale.total_cents,'lines',v_lines,
                'fiscal_policy_version',v_policy
              );
              v_receipt:=gen_random_uuid();
              INSERT INTO pos_cashier.receipts(
                id,organization_id,sale_id,register_id,
                receipt_number,immutable_payload,payload_sha256
              ) VALUES(v_receipt,_owner,_sale,v_sale.register_id,v_number,
                v_data,encode(sha256(convert_to(v_data::text,'UTF8')),'hex'));
              INSERT INTO pos_cashier.transaction_audit(
                id,organization_id,actor_id,sale_id,action,request_id)
              VALUES(gen_random_uuid(),_owner,v_actor,_sale,'receipt_issue',_request);
              INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code
              ) VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
                'admin.pos_cashier.receipt.issue','succeeded',_owner,'verified_capture');
              RETURN jsonb_build_object('id',v_receipt,'sale_id',_sale,
                'receipt_number',v_number,'replay',false);
            END $pos_issue_receipt$;
      REVOKE ALL ON FUNCTION pos_cashier.issue_verified_internal_receipt(
        text,text,uuid,uuid) FROM PUBLIC;
    """);
}
