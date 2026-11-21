using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Parent wallet and invoice PDF use the same live child permissions as Family
/// and Academy schedules, including the OTP-verified new Academy guardian.
/// Keep the original exact payer identity, organization, active-player and
/// multi-invoice all-or-nothing checks. No issuance or payment mutation.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261115980000_ParentFinanceCurrentGuardianScope")]
public sealed class ParentFinanceCurrentGuardianScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Single source for child authorization: canonical guardians, valid
      -- delegated guardians and approved OTP-bound original Academy guardians.
      -- Recomputed on every statement; a revoked claim immediately disappears.
      CREATE OR REPLACE FUNCTION finance.read_parent_training_fees(
          _identity uuid,_phone text,_role text,_expires timestamptz
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_parent_read$
        DECLARE v_fees jsonb; v_day integer; v_save boolean; v_auto boolean;
        BEGIN
          IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
            OR _role<>'parent' OR _expires<=clock_timestamp()
            OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='parent' AND i.is_active)
          THEN RAISE EXCEPTION 'parent_fee_access_denied' USING ERRCODE='42501'; END IF;
          SELECT preferred_day,save_card_requested,automatic_collection_requested
            INTO v_day,v_save,v_auto FROM finance.parent_fee_preferences
            WHERE parent_identity_id=_identity;
          v_day:=COALESCE(v_day,5); v_save:=COALESCE(v_save,false);
          v_auto:=COALESCE(v_auto,false);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',f.id,'player_id',f.player_id,'child_name',p.name,
            'fee_month',f.fee_month,'amount_cents',f.amount_cents,
            'currency',f.currency,'status',f.status,
            'payment_status',coalesce(r.status,'not_created'),
            'checkout_url',CASE WHEN r.status='ready' AND f.status='issued'
              THEN r.checkout_url ELSE NULL END,
            'due_date',f.invoice_due_date)
            ORDER BY f.fee_month DESC,f.id DESC),'[]'::jsonb)
          INTO v_fees
          FROM (SELECT * FROM finance.training_fee_issues
            WHERE payer_identity_id=_identity
              AND EXISTS (
                SELECT 1 FROM identity.current_phone_granted_children(
                  _identity,_phone,_role,_expires) authorized
                WHERE authorized.player_id=finance.training_fee_issues.player_id
                  AND authorized.organization_id=finance.training_fee_issues.organization_id)
            ORDER BY fee_month DESC,id DESC LIMIT 100) f
          JOIN club.players p ON p.id=f.player_id
            AND p.organization_id=f.organization_id
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
          RETURN jsonb_build_object('fees',v_fees,
            'preferred_day',v_day,'save_card_requested',v_save,
            'automatic_collection_requested',v_auto,
            'automatic_collection_active',false,
            'saved_card_available',false);
        END $fee_parent_read$;

      CREATE OR REPLACE FUNCTION finance.read_parent_invoice_pdf(
        _identity uuid,_phone text,_role text,_expires timestamptz,_ids uuid[]
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $invoice_parent$
      DECLARE v_result jsonb; v_count integer;
      BEGIN
        IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
          OR _role<>'parent' OR _expires<=clock_timestamp()
          OR _ids IS NULL OR cardinality(_ids) NOT BETWEEN 1 AND 20
          OR EXISTS(SELECT 1 FROM unnest(_ids) x WHERE x IS NULL)
          OR (SELECT count(DISTINCT x) FROM unnest(_ids) x)<>cardinality(_ids)
          OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
            WHERE i.id=_identity AND i.normalized_phone=_phone
              AND i.role='parent' AND i.is_active)
        THEN RAISE EXCEPTION 'parent_invoice_denied' USING ERRCODE='42501'; END IF;
        SELECT count(*),jsonb_agg(jsonb_build_object(
          'id',f.id,'organization_id',f.organization_id,
          'payer_identity_id',f.payer_identity_id,
          'player_id',f.player_id,'player_name',p.name,
          'group_name',coalesce(g.name,'Academy'),
          'payer_name',f.payer_first_name||' '||f.payer_last_name,
          'payer_email',f.payer_email,'payer_phone',f.payer_phone,
          'fee_month',f.fee_month,'due_date',f.invoice_due_date,
          'issued_at',f.issued_at,'paid_at',f.paid_at,'amount_cents',f.amount_cents,
          'description','Training '||to_char(f.fee_month,'YYYY-MM'),
          'currency',f.currency,'status',f.status,
          'payment_method',CASE WHEN cr.id IS NOT NULL THEN 'cash'
            WHEN r.status='paid' THEN 'card' ELSE 'unpaid' END,
          'transaction_reference',CASE WHEN cr.id IS NOT NULL THEN cr.id::text
            WHEN r.status='paid' THEN coalesce(r.captured_transaction_id,'')
            ELSE '' END,
          'payment_status',coalesce(r.status,'not_created'))
          ORDER BY f.fee_month ASC,f.player_id COLLATE "C",f.id)
        INTO v_count,v_result
        FROM finance.training_fee_issues f
        JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        LEFT JOIN club.academy_groups g ON g.id=p.primary_academy_group_id
          AND g.organization_id=p.organization_id
        LEFT JOIN finance.training_fee_cash_receipts cr ON cr.fee_issue_id=f.id
        LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id
        WHERE f.id=ANY(_ids) AND f.payer_identity_id=_identity
          AND f.organization_id='lefteria_fc_academy'
          AND EXISTS (
            SELECT 1 FROM identity.current_phone_granted_children(
              _identity,_phone,_role,_expires) authorized
            WHERE authorized.player_id=f.player_id
              AND authorized.organization_id=f.organization_id);
        IF v_count<>cardinality(_ids) THEN
          RAISE EXCEPTION 'parent_invoice_scope_denied' USING ERRCODE='42501';
        END IF;
        RETURN jsonb_build_object('items',v_result,'organization_id','lefteria_fc_academy');
      END $invoice_parent$;
      REVOKE ALL ON FUNCTION finance.read_parent_training_fees(
        uuid,text,text,timestamptz) FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.read_parent_invoice_pdf(
        uuid,text,text,timestamptz,uuid[]) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Restore previous stricter legacy-import-only read semantics.
      CREATE OR REPLACE FUNCTION finance.read_parent_training_fees(
          _identity uuid,_phone text,_role text,_expires timestamptz
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $fee_parent_read$
        DECLARE v_fees jsonb; v_day integer; v_save boolean; v_auto boolean;
        BEGIN
          IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
            OR _role<>'parent' OR _expires<=clock_timestamp()
            OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='parent' AND i.is_active)
          THEN RAISE EXCEPTION 'parent_fee_access_denied' USING ERRCODE='42501'; END IF;
          SELECT preferred_day,save_card_requested,automatic_collection_requested
            INTO v_day,v_save,v_auto FROM finance.parent_fee_preferences
            WHERE parent_identity_id=_identity;
          v_day:=COALESCE(v_day,5); v_save:=COALESCE(v_save,false);
          v_auto:=COALESCE(v_auto,false);
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',f.id,'player_id',f.player_id,'child_name',p.name,
            'fee_month',f.fee_month,'amount_cents',f.amount_cents,
            'currency',f.currency,'status',f.status,
            'payment_status',coalesce(r.status,'not_created'),
            'checkout_url',CASE WHEN r.status='ready' AND f.status='issued'
              THEN r.checkout_url ELSE NULL END,
            'due_date',f.invoice_due_date)
            ORDER BY f.fee_month DESC,f.id DESC),'[]'::jsonb)
          INTO v_fees
          FROM (SELECT * FROM finance.training_fee_issues
            WHERE payer_identity_id=_identity
            ORDER BY fee_month DESC,id DESC LIMIT 100) f
          JOIN club.players p ON p.id=f.player_id
            AND p.organization_id=f.organization_id
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          JOIN identity.mobile_record_links l
            ON l.collection='players' AND l.record_id=f.player_id
            AND l.kind='guardian' AND l.role='parent' AND l.identity_id=_identity
            AND l.normalized_phone=_phone
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.source_id=l.record_id
            AND b.kind='guardian' AND b.normalized_phone=_phone
          LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
          RETURN jsonb_build_object('fees',v_fees,
            'preferred_day',v_day,'save_card_requested',v_save,
            'automatic_collection_requested',v_auto,
            'automatic_collection_active',false,
            'saved_card_available',false);
        END $fee_parent_read$;

      CREATE OR REPLACE FUNCTION finance.read_parent_invoice_pdf(
        _identity uuid,_phone text,_role text,_expires timestamptz,_ids uuid[]
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $invoice_parent$
      DECLARE v_result jsonb; v_count integer;
      BEGIN
        IF _identity IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
          OR _role<>'parent' OR _expires<=clock_timestamp()
          OR _ids IS NULL OR cardinality(_ids) NOT BETWEEN 1 AND 20
          OR EXISTS(SELECT 1 FROM unnest(_ids) x WHERE x IS NULL)
          OR (SELECT count(DISTINCT x) FROM unnest(_ids) x)<>cardinality(_ids)
          OR NOT EXISTS(SELECT 1 FROM identity.mobile_identities i
            WHERE i.id=_identity AND i.normalized_phone=_phone
              AND i.role='parent' AND i.is_active)
        THEN RAISE EXCEPTION 'parent_invoice_denied' USING ERRCODE='42501'; END IF;
        SELECT count(*),jsonb_agg(jsonb_build_object(
          'id',f.id,'organization_id',f.organization_id,
          'payer_identity_id',f.payer_identity_id,
          'player_id',f.player_id,'player_name',p.name,
          'group_name',coalesce(g.name,'Academy'),
          'payer_name',f.payer_first_name||' '||f.payer_last_name,
          'payer_email',f.payer_email,'payer_phone',f.payer_phone,
          'fee_month',f.fee_month,'due_date',f.invoice_due_date,
          'issued_at',f.issued_at,'paid_at',f.paid_at,'amount_cents',f.amount_cents,
          'description','Training '||to_char(f.fee_month,'YYYY-MM'),
          'currency',f.currency,'status',f.status,
          'payment_method',CASE WHEN cr.id IS NOT NULL THEN 'cash'
            WHEN r.status='paid' THEN 'card' ELSE 'unpaid' END,
          'transaction_reference',CASE WHEN cr.id IS NOT NULL THEN cr.id::text
            WHEN r.status='paid' THEN coalesce(r.captured_transaction_id,'')
            ELSE '' END,
          'payment_status',coalesce(r.status,'not_created'))
          ORDER BY f.fee_month ASC,f.player_id COLLATE "C",f.id)
        INTO v_count,v_result
        FROM finance.training_fee_issues f
        JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        JOIN identity.mobile_record_links l
          ON l.identity_id=_identity AND l.normalized_phone=_phone AND l.role='parent'
          AND l.collection='players' AND l.record_id=f.player_id AND l.kind='guardian'
        JOIN identity.canonical_contact_bindings b
          ON b.collection=l.collection AND b.source_id=l.record_id
          AND b.kind='guardian' AND b.normalized_phone=_phone
        LEFT JOIN club.academy_groups g ON g.id=p.primary_academy_group_id
          AND g.organization_id=p.organization_id
        LEFT JOIN finance.training_fee_cash_receipts cr ON cr.fee_issue_id=f.id
        LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id
        WHERE f.id=ANY(_ids) AND f.payer_identity_id=_identity
          AND f.organization_id='lefteria_fc_academy';
        IF v_count<>cardinality(_ids) THEN
          RAISE EXCEPTION 'parent_invoice_scope_denied' USING ERRCODE='42501';
        END IF;
        RETURN jsonb_build_object('items',v_result,'organization_id','lefteria_fc_academy');
      END $invoice_parent$;
      REVOKE ALL ON FUNCTION finance.read_parent_training_fees(
        uuid,text,text,timestamptz) FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.read_parent_invoice_pdf(
        uuid,text,text,timestamptz,uuid[]) FROM PUBLIC;
    """);
}
