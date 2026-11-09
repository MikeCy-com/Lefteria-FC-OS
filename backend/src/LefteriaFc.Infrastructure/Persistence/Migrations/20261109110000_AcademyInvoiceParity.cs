using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Reviewed cash receipts, immutable invoice due dates and scoped PDF data.
/// Reproduces the original billing PDF without exposing raw finance tables.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261109110000_AcademyInvoiceParity")]
public sealed class AcademyInvoiceParity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE finance.training_fee_cash_receipts(
        id uuid PRIMARY KEY,
        fee_issue_id uuid NOT NULL UNIQUE
          REFERENCES finance.training_fee_issues(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL REFERENCES club.organizations(id),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        amount_cents bigint NOT NULL CHECK (amount_cents BETWEEN 1 AND 100000000),
        received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        notes varchar(280) NOT NULL DEFAULT '',
        created_at timestamptz NOT NULL DEFAULT clock_timestamp()
      );
      ALTER TABLE finance.training_fee_cash_receipts ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON finance.training_fee_cash_receipts FROM PUBLIC;

      CREATE FUNCTION finance.record_admin_training_fee_cash(
        _digest text,_owner text,_issue uuid,_receipt uuid,_notes text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $fee_cash$
      DECLARE v_actor uuid; v_session uuid; v_role text;
              v_fee finance.training_fee_issues%ROWTYPE;
              v_existing finance.training_fee_cash_receipts%ROWTYPE;
      BEGIN
        IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
          OR _issue IS NULL OR _receipt IS NULL OR
          _notes IS NULL OR length(_notes)>280 OR _notes ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'invalid_cash_receipt' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
          AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'cash_finance_access_denied' USING ERRCODE='42501';
        END IF;
        SELECT * INTO v_fee FROM finance.training_fee_issues
          WHERE id=_issue AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'cash_invoice_missing' USING ERRCODE='42501';
        END IF;
        SELECT * INTO v_existing FROM finance.training_fee_cash_receipts
          WHERE fee_issue_id=_issue;
        IF FOUND THEN
          IF v_existing.id<>_receipt OR v_existing.actor_id<>v_actor
            OR v_existing.notes<>_notes THEN
            RAISE EXCEPTION 'cash_receipt_already_exists' USING ERRCODE='23505';
          END IF;
          RETURN jsonb_build_object('id',v_existing.id,'fee_issue_id',_issue,
            'payment_method','cash','status','paid','replay',true);
        END IF;
        -- Do not accept cash while a card checkout may still complete.
        -- Staff must first reconcile/void that Payabl attempt out of band.
        IF v_fee.status<>'issued' OR v_fee.payment_request_id IS NOT NULL
          THEN RAISE EXCEPTION 'cash_conflicts_with_card_attempt'
            USING ERRCODE='23505'; END IF;
        INSERT INTO finance.training_fee_cash_receipts(
          id,fee_issue_id,organization_id,actor_id,amount_cents,notes)
        VALUES(_receipt,_issue,_owner,v_actor,v_fee.amount_cents,_notes);
        UPDATE finance.training_fee_issues SET status='paid',
          paid_at=clock_timestamp() WHERE id=_issue AND status='issued';
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.finance.training_fee.cash_payment','succeeded',_owner,'exact_invoice');
        RETURN jsonb_build_object('id',_receipt,'fee_issue_id',_issue,
          'payment_method','cash','status','paid','replay',false);
      END $fee_cash$;

      CREATE FUNCTION finance.read_admin_invoice_pdf(
        _digest text,_owner text,_ids uuid[]
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $invoice_admin$
      DECLARE v_role text; v_result jsonb; v_count integer; v_payers integer;
      BEGIN
        IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
          OR _ids IS NULL OR cardinality(_ids) NOT BETWEEN 1 AND 20
          OR EXISTS(SELECT 1 FROM unnest(_ids) x WHERE x IS NULL)
          OR (SELECT count(DISTINCT x) FROM unnest(_ids) x)<>cardinality(_ids)
        THEN RAISE EXCEPTION 'invalid_invoice_selection' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'invoice_access_denied' USING ERRCODE='42501'; END IF;
        SELECT count(*),count(DISTINCT f.payer_identity_id),
          jsonb_agg(jsonb_build_object(
          'id',f.id,'organization_id',f.organization_id,
          'payer_identity_id',f.payer_identity_id,
          'player_id',f.player_id,'player_name',p.name,
          'group_name',coalesce(g.name,'Academy'),
          'payer_name',f.payer_first_name||' '||f.payer_last_name,
          'payer_email',f.payer_email,'payer_phone','+'||i.normalized_phone,
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
        INTO v_count,v_payers,v_result
        FROM finance.training_fee_issues f
          JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
          JOIN identity.mobile_identities i ON i.id=f.payer_identity_id
          LEFT JOIN club.academy_groups g ON g.id=p.primary_academy_group_id
            AND g.organization_id=p.organization_id
          LEFT JOIN finance.training_fee_cash_receipts cr ON cr.fee_issue_id=f.id
          LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id
        WHERE f.id=ANY(_ids) AND f.organization_id=_owner;
        IF v_count<>cardinality(_ids) OR v_payers<>1 THEN
          -- Never put fees from different guardians on a document with
          -- the first guardian's payer heading.
          RAISE EXCEPTION 'invoice_outside_scope' USING ERRCODE='42501';
        END IF;
        RETURN jsonb_build_object('items',v_result,'organization_id',_owner);
      END $invoice_admin$;

      CREATE FUNCTION finance.read_parent_invoice_pdf(
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
          'payer_email',f.payer_email,'payer_phone','+'||_phone,
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

      REVOKE ALL ON FUNCTION finance.set_training_invoice_due_date() FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.record_admin_training_fee_cash(
        text,text,uuid,uuid,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.read_admin_invoice_pdf(
        text,text,uuid[]) FROM PUBLIC;
      REVOKE ALL ON FUNCTION finance.read_parent_invoice_pdf(
        uuid,text,text,timestamptz,uuid[]) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $rollback_guard$
      BEGIN
        IF EXISTS(SELECT 1 FROM finance.training_fee_cash_receipts LIMIT 1) THEN
          RAISE EXCEPTION 'cannot_erase_cash_receipts' USING ERRCODE='23505';
        END IF;
      END $rollback_guard$;
      DROP FUNCTION finance.read_parent_invoice_pdf(uuid,text,text,timestamptz,uuid[]);
      DROP FUNCTION finance.read_admin_invoice_pdf(text,text,uuid[]);
      DROP FUNCTION finance.record_admin_training_fee_cash(text,text,uuid,uuid,text);
      DROP TABLE finance.training_fee_cash_receipts;
    """);
}
