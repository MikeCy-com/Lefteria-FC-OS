using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Audited cancellation of an unpaid FUTURE Academy training month; no hard
/// deletion, no provider reversal and no change to verified income.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122235500_AcademyFutureFeeCancellation")]
public sealed class AcademyFutureFeeCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
-- A cancellation is a retained ledger state, not a DELETE. Paid invoices,
      -- cash receipts, provider-linked attempts and history survive unchanged.
      ALTER TABLE finance.training_fee_issues
        DROP CONSTRAINT training_fee_issues_status_check;
      ALTER TABLE finance.training_fee_issues
        ADD CONSTRAINT training_fee_issues_status_check
        CHECK (status IN ('issued','paid','review_required','cancelled'));
      ALTER TABLE finance.training_fee_issues
        ADD COLUMN cancelled_at timestamptz,
        ADD COLUMN cancelled_by uuid REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        ADD COLUMN cancel_reason varchar(240),
        ADD COLUMN cancel_request_id uuid UNIQUE;
      ALTER TABLE finance.training_fee_issues
        ADD CONSTRAINT training_fee_cancelled_complete CHECK (
          (status='cancelled' AND cancelled_at IS NOT NULL
            AND cancelled_by IS NOT NULL AND cancel_reason IS NOT NULL
            AND cancel_request_id IS NOT NULL)
          OR (status<>'cancelled' AND cancelled_at IS NULL
            AND cancelled_by IS NULL AND cancel_reason IS NULL
            AND cancel_request_id IS NULL)
        );
      
CREATE FUNCTION finance.cancel_admin_future_training_fee(
          _digest text,_owner text,_fee uuid,_request uuid,_reason text
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $cancel_future$
        DECLARE
          v_actor uuid; v_session uuid; v_role text;
          v_row finance.training_fee_issues%ROWTYPE;
          v_current_month date;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner<>'lefteria_fc_academy'
            OR _fee IS NULL OR _request IS NULL
            OR _reason IS NULL OR length(btrim(_reason)) NOT BETWEEN 8 AND 240
            OR btrim(_reason)<>_reason OR _reason ~ '[[:cntrl:]]'
          THEN RAISE EXCEPTION 'fee_cancellation_invalid' USING ERRCODE='22023'; END IF;
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
          IF NOT FOUND OR v_role<>'super_admin'
          THEN RAISE EXCEPTION 'fee_cancellation_denied' USING ERRCODE='42501'; END IF;

          SELECT * INTO v_row FROM finance.training_fee_issues
          WHERE id=_fee AND organization_id=_owner FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'fee_cancellation_not_found' USING ERRCODE='42501';
          END IF;
          -- Explicit future *training* months only, using Academy local time.
          -- Previously paid and current/past months remain immutable here.
          v_current_month:=date_trunc('month',
            clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
          IF v_row.fee_month<=v_current_month
          THEN RAISE EXCEPTION 'fee_month_not_future' USING ERRCODE='23505'; END IF;
          IF v_row.status='cancelled' AND
            v_row.cancel_request_id=_request AND v_row.cancel_reason=_reason
            AND v_row.cancelled_by=v_actor
          THEN
            RETURN jsonb_build_object('id',v_row.id,'player_id',v_row.player_id,
              'fee_month',v_row.fee_month,'status','cancelled','replay',true);
          END IF;
          -- No payment request, captured funds, cash receipt, SMS, or review case
          -- may be silently cancelled. Reconcile provider activity separately.
          IF v_row.status<>'issued' OR v_row.payment_request_id IS NOT NULL
             OR v_row.paid_at IS NOT NULL OR EXISTS(
               SELECT 1 FROM finance.training_fee_cash_receipts cr
               WHERE cr.fee_issue_id=_fee)
             OR EXISTS(SELECT 1 FROM finance.training_fee_sms_outbox sms
               WHERE sms.fee_issue_id=_fee)
          THEN RAISE EXCEPTION 'fee_requires_payment_review' USING ERRCODE='23505'; END IF;
          UPDATE finance.training_fee_issues SET
            status='cancelled',cancelled_at=clock_timestamp(),
            cancelled_by=v_actor,cancel_reason=_reason,cancel_request_id=_request
          WHERE id=_fee AND status='issued';
          INSERT INTO audit.security_events(
            id,actor_realm,occurred_at,actor_identity_id,device_session_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.finance.training_fee.cancel_future','succeeded',_owner,
            'future_unpaid_cancelled');
          RETURN jsonb_build_object('id',v_row.id,'player_id',v_row.player_id,
            'fee_month',v_row.fee_month,'status','cancelled','replay',false);
        END $cancel_future$;
REVOKE ALL ON FUNCTION finance.cancel_admin_future_training_fee(text,text,uuid,uuid,text) FROM PUBLIC;
CREATE OR REPLACE FUNCTION finance.read_admin_training_fees(_digest text,_owner text)
              RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_admin_read$
              DECLARE v_role text; v_fees jsonb;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
                  THEN RAISE EXCEPTION 'fee_scope_invalid' USING ERRCODE='22023'; END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.role INTO v_role FROM identity.admin_sessions s
                  JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                  JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                    AND g.organization_id=_owner
                  WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                    AND s.expires_at>clock_timestamp() AND a.is_active
                    AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                    AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_admin_read_denied' USING ERRCODE='42501'; END IF;
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                    'id',f.id,'player_id',f.player_id,'child_name',p.name,
                    'fee_month',f.fee_month,'amount_cents',f.amount_cents,
                    'currency',f.currency,'status',f.status,
                    'payer_email',f.payer_email,
                    'payer_first_name',f.payer_first_name,
                    'payer_last_name',f.payer_last_name,
                    'cancelled_at',f.cancelled_at,'cancel_reason',f.cancel_reason,
                  'payment_request_id',f.payment_request_id,
                    'payment_status',COALESCE(r.status,'not_created'),
                    'checkout_url',CASE WHEN r.status='ready' THEN r.checkout_url ELSE NULL END,
                    'sms_status',(SELECT o.state FROM finance.training_fee_sms_outbox o
                        WHERE o.fee_issue_id=f.id ORDER BY o.created_at DESC LIMIT 1))
                    ORDER BY f.fee_month DESC,f.issued_at DESC,f.id DESC),'[]'::jsonb)
                  INTO v_fees
                  FROM (SELECT * FROM finance.training_fee_issues
                    WHERE organization_id=_owner
                    ORDER BY fee_month DESC,issued_at DESC,id DESC LIMIT 100) f
                  JOIN club.players p ON p.id=f.player_id
                  LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
                RETURN jsonb_build_object('organization_id',_owner,'fees',v_fees);
              END $fee_admin_read$;

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
            WHERE payer_identity_id=_identity AND status<>'cancelled'
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

CREATE OR REPLACE FUNCTION finance.read_admin_invoice_pdf(
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
              IF NOT FOUND OR v_role<>'super_admin' THEN
                RAISE EXCEPTION 'invoice_access_denied' USING ERRCODE='42501'; END IF;
              SELECT count(*),count(DISTINCT f.payer_identity_id),
                jsonb_agg(jsonb_build_object(
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
              INTO v_count,v_payers,v_result
              FROM finance.training_fee_issues f
                JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
                LEFT JOIN club.academy_groups g ON g.id=p.primary_academy_group_id
                  AND g.organization_id=p.organization_id
                LEFT JOIN finance.training_fee_cash_receipts cr ON cr.fee_issue_id=f.id
                LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id
              WHERE f.id=ANY(_ids) AND f.status<>'cancelled' AND f.organization_id=_owner;
              IF v_count<>cardinality(_ids) OR v_payers<>1 THEN
                -- Never put fees from different guardians on a document with
                -- the first guardian's payer heading.
                RAISE EXCEPTION 'invoice_outside_scope' USING ERRCODE='42501';
              END IF;
              RETURN jsonb_build_object('items',v_result,'organization_id',_owner);
            END $invoice_admin$;

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
        WHERE f.id=ANY(_ids) AND f.status<>'cancelled' AND f.payer_identity_id=_identity
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

CREATE OR REPLACE FUNCTION finance.read_admin_accounting_overview(
          _digest text, _owner text, _month date
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $overview$
        DECLARE v_actor uuid; v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _month IS NULL OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
            OR extract(day FROM _month)<>1
          THEN RAISE EXCEPTION 'accounting_overview_invalid' USING ERRCODE='22023'; END IF;

          -- This helper locks the current session and organization grant.
          -- A role alone does not authorize a different organization.
          BEGIN
            PERFORM identity.lock_admin_club_authority(_digest,_owner);
          EXCEPTION WHEN raise_exception THEN
            RAISE EXCEPTION 'accounting_overview_denied'
              USING ERRCODE='42501';
          END;
          SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          -- Location/city managers MUST use the site-scoped finance reader.
          -- Owner-wide PII-free aggregates are for an explicitly granted
          -- accountant or Super Admin only, not a general "manager" grant.
          IF v_actor IS NULL OR v_role NOT IN ('super_admin','accountant')
          THEN RAISE EXCEPTION 'accounting_overview_denied' USING ERRCODE='42501'; END IF;

          RETURN jsonb_build_object(
            'organization_id',_owner,'month',_month,'currency','EUR',
            'scope','organization','is_statutory_statement',false,
            'academy_training_fees',(
              SELECT jsonb_build_object(
                'issued_count',count(*) FILTER(WHERE f.status<>'cancelled')::int,
                'cancelled_count',count(*) FILTER(WHERE f.status='cancelled')::int,
                'cancelled_cents',coalesce(sum(f.amount_cents) FILTER(WHERE f.status='cancelled'),0)::bigint,
                'issued_cents',coalesce(sum(f.amount_cents) FILTER(WHERE f.status<>'cancelled'),0)::bigint,
                'paid_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='paid'),0)::bigint,
                'open_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='issued'),0)::bigint,
                'review_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='review_required'),0)::bigint)
              FROM finance.training_fee_issues f
              WHERE _owner='lefteria_fc_academy'
                AND f.organization_id=_owner AND f.fee_month=_month
            ),
            'general_charges',(
              SELECT jsonb_build_object(
                'issued_count',count(*)::int,
                'issued_cents',coalesce(sum(g.amount_cents),0)::bigint,
                'collected_cents',coalesce(sum(g.paid_cents),0)::bigint,
                'outstanding_cents',coalesce(sum(g.due_cents),0)::bigint,
                'overdue_count',count(*) FILTER(WHERE g.status='overdue')::int)
              FROM finance.general_charge_balances g
              WHERE g.organization_id=_owner AND g.cancelled_at IS NULL
                AND g.issued_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND g.issued_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            ),
            'payabl_sandbox',(
              SELECT jsonb_build_object(
                'request_count',count(*)::int,
                'confirmed_count',count(*) FILTER(WHERE p.status='paid')::int,
                'review_count',count(*) FILTER(WHERE p.status='review_required')::int)
              FROM finance.payment_requests p
              WHERE p.organization_id=_owner
                AND p.created_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND p.created_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            )
          );
        END $overview$;

CREATE OR REPLACE FUNCTION finance.read_admin_academy_location_finance(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_finance$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_finance_month_invalid'
          USING ERRCODE='22023'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'training_issued_count',t.issued_count,
              'training_cancelled_count',t.cancelled_count,
              'training_cancelled_cents',t.cancelled_cents,
              'training_issued_cents',t.issued_cents,
              'training_paid_cents',t.paid_cents,
              'training_review_count',t.review_count,
              'general_issued_count',g.issued_count,
              'general_issued_cents',g.issued_cents,
              'general_paid_cents',g.paid_cents,
              'general_overdue_count',g.overdue_count
            ) ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            CROSS JOIN LATERAL (
              SELECT count(*) FILTER(WHERE i.status<>'cancelled')::int AS issued_count,
                count(*) FILTER(WHERE i.status='cancelled')::int AS cancelled_count,
                coalesce(sum(i.amount_cents) FILTER(WHERE i.status='cancelled'),0)::bigint AS cancelled_cents,
                coalesce(sum(i.amount_cents) FILTER(WHERE i.status<>'cancelled'),0)::bigint AS issued_cents,
                coalesce(sum(i.amount_cents)
                  FILTER(WHERE i.status='paid'),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE i.status='review_required')::int AS review_count
              FROM finance.academy_training_fee_locations a
              JOIN finance.training_fee_issues i ON i.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
            ) t
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS issued_count,
                coalesce(sum(b.amount_cents),0)::bigint AS issued_cents,
                coalesce(sum(b.paid_cents),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE b.status='overdue')::int AS overdue_count
              FROM finance.academy_general_charge_locations a
              JOIN finance.general_charge_balances b ON b.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND b.organization_id='lefteria_fc_academy'
                AND b.cancelled_at IS NULL
                AND b.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND b.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
            ) g
            WHERE club.admin_football_allowed(
              v_actor,v_role,_owner,s.id,l.id,false)
          )
        );
      END $read_finance$;

CREATE OR REPLACE FUNCTION finance.read_admin_academy_unallocated_issues(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $unallocated$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR v_role<>'super_admin'
          OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_unallocated_denied'
          USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,
          'total_count',(
            SELECT count(*)::int FROM (
              SELECT i.id FROM finance.training_fee_issues i
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month AND i.status<>'cancelled'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT g.id FROM finance.general_charge_issues g
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
            ) q
          ),
          'issues',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'kind',q.kind,'id',q.id,'player_id',q.player_id,
              'player_name',q.player_name,'amount_cents',q.amount_cents,
              'month',q.month
            ) ORDER BY q.kind,q.player_name,q.id),'[]'::jsonb)
            FROM (
              SELECT 'training_fee'::text AS kind,i.id,i.player_id,
                p.name AS player_name,i.amount_cents,
                i.fee_month AS month
              FROM finance.training_fee_issues i
              JOIN club.players p ON p.id=i.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month AND i.status<>'cancelled'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT 'general_charge'::text AS kind,g.id,g.player_id,
                p.name AS player_name,g.amount_cents,
                date_trunc('month',g.issued_at AT TIME ZONE 'Asia/Nicosia')::date
                  AS month
              FROM finance.general_charge_issues g
              JOIN club.players p ON p.id=g.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
              ORDER BY kind,player_name,id LIMIT 100
            ) q
          )
        );
      END $unallocated$;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DO $guard_recovery$ BEGIN
         IF EXISTS(SELECT 1 FROM finance.training_fee_issues WHERE status='cancelled'
           OR cancel_request_id IS NOT NULL)
         THEN RAISE EXCEPTION 'cancelled_fees_require_forward_recovery'; END IF;
       END $guard_recovery$;
       DROP FUNCTION finance.cancel_admin_future_training_fee(text,text,uuid,uuid,text);
       CREATE OR REPLACE FUNCTION finance.read_admin_training_fees(_digest text,_owner text)
              RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
              SET search_path=pg_catalog,pg_temp AS $fee_admin_read$
              DECLARE v_role text; v_fees jsonb;
              BEGIN
                IF _digest !~ '^[0-9a-f]{64}$' OR _owner<>'lefteria_fc_academy'
                  THEN RAISE EXCEPTION 'fee_scope_invalid' USING ERRCODE='22023'; END IF;
                PERFORM identity.lock_admin_club_authority(_digest,_owner);
                SELECT a.role INTO v_role FROM identity.admin_sessions s
                  JOIN identity.admin_accounts a ON a.id=s.admin_account_id
                  JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                    AND g.organization_id=_owner
                  WHERE s.token_digest=_digest AND s.revoked_at IS NULL
                    AND s.expires_at>clock_timestamp() AND a.is_active
                    AND NOT a.invite_pending AND NOT a.otp2fa_enabled
                    AND a.password_hash IS NOT NULL;
                IF NOT FOUND OR v_role<>'super_admin' THEN
                  RAISE EXCEPTION 'fee_admin_read_denied' USING ERRCODE='42501'; END IF;
                SELECT coalesce(jsonb_agg(jsonb_build_object(
                    'id',f.id,'player_id',f.player_id,'child_name',p.name,
                    'fee_month',f.fee_month,'amount_cents',f.amount_cents,
                    'currency',f.currency,'status',f.status,
                    'payer_email',f.payer_email,
                    'payer_first_name',f.payer_first_name,
                    'payer_last_name',f.payer_last_name,
                    'payment_request_id',f.payment_request_id,
                    'payment_status',COALESCE(r.status,'not_created'),
                    'checkout_url',CASE WHEN r.status='ready' THEN r.checkout_url ELSE NULL END,
                    'sms_status',(SELECT o.state FROM finance.training_fee_sms_outbox o
                        WHERE o.fee_issue_id=f.id ORDER BY o.created_at DESC LIMIT 1))
                    ORDER BY f.fee_month DESC,f.issued_at DESC,f.id DESC),'[]'::jsonb)
                  INTO v_fees
                  FROM (SELECT * FROM finance.training_fee_issues
                    WHERE organization_id=_owner
                    ORDER BY fee_month DESC,issued_at DESC,id DESC LIMIT 100) f
                  JOIN club.players p ON p.id=f.player_id
                  LEFT JOIN finance.payment_requests r ON r.id=f.payment_request_id;
                RETURN jsonb_build_object('organization_id',_owner,'fees',v_fees);
              END $fee_admin_read$;

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

CREATE OR REPLACE FUNCTION finance.read_admin_invoice_pdf(
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
              IF NOT FOUND OR v_role<>'super_admin' THEN
                RAISE EXCEPTION 'invoice_access_denied' USING ERRCODE='42501'; END IF;
              SELECT count(*),count(DISTINCT f.payer_identity_id),
                jsonb_agg(jsonb_build_object(
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
              INTO v_count,v_payers,v_result
              FROM finance.training_fee_issues f
                JOIN club.players p ON p.id=f.player_id AND p.organization_id=f.organization_id
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

CREATE OR REPLACE FUNCTION finance.read_admin_accounting_overview(
          _digest text, _owner text, _month date
        ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $overview$
        DECLARE v_actor uuid; v_role text;
        BEGIN
          IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
            OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _month IS NULL OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
            OR extract(day FROM _month)<>1
          THEN RAISE EXCEPTION 'accounting_overview_invalid' USING ERRCODE='22023'; END IF;

          -- This helper locks the current session and organization grant.
          -- A role alone does not authorize a different organization.
          BEGIN
            PERFORM identity.lock_admin_club_authority(_digest,_owner);
          EXCEPTION WHEN raise_exception THEN
            RAISE EXCEPTION 'accounting_overview_denied'
              USING ERRCODE='42501';
          END;
          SELECT a.id,a.role INTO v_actor,v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
          -- Location/city managers MUST use the site-scoped finance reader.
          -- Owner-wide PII-free aggregates are for an explicitly granted
          -- accountant or Super Admin only, not a general "manager" grant.
          IF v_actor IS NULL OR v_role NOT IN ('super_admin','accountant')
          THEN RAISE EXCEPTION 'accounting_overview_denied' USING ERRCODE='42501'; END IF;

          RETURN jsonb_build_object(
            'organization_id',_owner,'month',_month,'currency','EUR',
            'scope','organization','is_statutory_statement',false,
            'academy_training_fees',(
              SELECT jsonb_build_object(
                'issued_count',count(*)::int,
                'issued_cents',coalesce(sum(f.amount_cents),0)::bigint,
                'paid_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='paid'),0)::bigint,
                'open_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='issued'),0)::bigint,
                'review_cents',coalesce(sum(f.amount_cents)
                  FILTER(WHERE f.status='review_required'),0)::bigint)
              FROM finance.training_fee_issues f
              WHERE _owner='lefteria_fc_academy'
                AND f.organization_id=_owner AND f.fee_month=_month
            ),
            'general_charges',(
              SELECT jsonb_build_object(
                'issued_count',count(*)::int,
                'issued_cents',coalesce(sum(g.amount_cents),0)::bigint,
                'collected_cents',coalesce(sum(g.paid_cents),0)::bigint,
                'outstanding_cents',coalesce(sum(g.due_cents),0)::bigint,
                'overdue_count',count(*) FILTER(WHERE g.status='overdue')::int)
              FROM finance.general_charge_balances g
              WHERE g.organization_id=_owner AND g.cancelled_at IS NULL
                AND g.issued_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND g.issued_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            ),
            'payabl_sandbox',(
              SELECT jsonb_build_object(
                'request_count',count(*)::int,
                'confirmed_count',count(*) FILTER(WHERE p.status='paid')::int,
                'review_count',count(*) FILTER(WHERE p.status='review_required')::int)
              FROM finance.payment_requests p
              WHERE p.organization_id=_owner
                AND p.created_at>=(_month::timestamp AT TIME ZONE 'Asia/Nicosia')
                AND p.created_at<((_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia')
            )
          );
        END $overview$;

CREATE OR REPLACE FUNCTION finance.read_admin_academy_location_finance(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_finance$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_finance_month_invalid'
          USING ERRCODE='22023'; END IF;

        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,'currency','EUR',
          'locations',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'city_id',l.city_id,'location_id',l.id,
              'training_issued_count',t.issued_count,
              'training_issued_cents',t.issued_cents,
              'training_paid_cents',t.paid_cents,
              'training_review_count',t.review_count,
              'general_issued_count',g.issued_count,
              'general_issued_cents',g.issued_cents,
              'general_paid_cents',g.paid_cents,
              'general_overdue_count',g.overdue_count
            ) ORDER BY l.city_id,l.id),'[]'::jsonb)
            FROM club.football_locations l
            JOIN club.football_operational_scopes s
              ON s.academy_city_id=l.city_id
              AND s.organization_id='lefteria_fc_academy'
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS issued_count,
                coalesce(sum(i.amount_cents),0)::bigint AS issued_cents,
                coalesce(sum(i.amount_cents)
                  FILTER(WHERE i.status='paid'),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE i.status='review_required')::int AS review_count
              FROM finance.academy_training_fee_locations a
              JOIN finance.training_fee_issues i ON i.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
            ) t
            CROSS JOIN LATERAL (
              SELECT count(*)::int AS issued_count,
                coalesce(sum(b.amount_cents),0)::bigint AS issued_cents,
                coalesce(sum(b.paid_cents),0)::bigint AS paid_cents,
                count(*) FILTER(WHERE b.status='overdue')::int AS overdue_count
              FROM finance.academy_general_charge_locations a
              JOIN finance.general_charge_balances b ON b.id=a.issue_id
              WHERE a.location_id=l.id AND a.city_id=l.city_id
                AND b.organization_id='lefteria_fc_academy'
                AND b.cancelled_at IS NULL
                AND b.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND b.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
            ) g
            WHERE club.admin_football_allowed(
              v_actor,v_role,_owner,s.id,l.id,false)
          )
        );
      END $read_finance$;

CREATE OR REPLACE FUNCTION finance.read_admin_academy_unallocated_issues(
        _digest text,_owner text,_month date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $unallocated$
      DECLARE v_actor uuid; v_role text;
      BEGIN
        SELECT actor_id,account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,_owner);
        IF _owner<>'lefteria_fc_academy' OR v_role<>'super_admin'
          OR _month IS NULL
          OR _month NOT BETWEEN DATE '2020-01-01' AND DATE '2100-12-01'
          OR EXTRACT(day FROM _month)<>1
        THEN RAISE EXCEPTION 'academy_unallocated_denied'
          USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object(
          'organization_id',_owner,'month',_month,
          'total_count',(
            SELECT count(*)::int FROM (
              SELECT i.id FROM finance.training_fee_issues i
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT g.id FROM finance.general_charge_issues g
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
            ) q
          ),
          'issues',(
            SELECT coalesce(jsonb_agg(jsonb_build_object(
              'kind',q.kind,'id',q.id,'player_id',q.player_id,
              'player_name',q.player_name,'amount_cents',q.amount_cents,
              'month',q.month
            ) ORDER BY q.kind,q.player_name,q.id),'[]'::jsonb)
            FROM (
              SELECT 'training_fee'::text AS kind,i.id,i.player_id,
                p.name AS player_name,i.amount_cents,
                i.fee_month AS month
              FROM finance.training_fee_issues i
              JOIN club.players p ON p.id=i.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE i.organization_id='lefteria_fc_academy'
                AND i.fee_month=_month
                AND NOT EXISTS(SELECT 1 FROM finance.academy_training_fee_locations a
                  WHERE a.issue_id=i.id)
              UNION ALL
              SELECT 'general_charge'::text AS kind,g.id,g.player_id,
                p.name AS player_name,g.amount_cents,
                date_trunc('month',g.issued_at AT TIME ZONE 'Asia/Nicosia')::date
                  AS month
              FROM finance.general_charge_issues g
              JOIN club.players p ON p.id=g.player_id
                AND p.organization_id='lefteria_fc_academy'
              WHERE g.organization_id='lefteria_fc_academy'
                AND g.cancelled_at IS NULL
                AND g.issued_at>=_month::timestamp AT TIME ZONE 'Asia/Nicosia'
                AND g.issued_at<(_month+interval '1 month')::timestamp
                  AT TIME ZONE 'Asia/Nicosia'
                AND NOT EXISTS(SELECT 1 FROM finance.academy_general_charge_locations a
                  WHERE a.issue_id=g.id)
              ORDER BY kind,player_name,id LIMIT 100
            ) q
          )
        );
      END $unallocated$;
       ALTER TABLE finance.training_fee_issues DROP CONSTRAINT training_fee_cancelled_complete;
       ALTER TABLE finance.training_fee_issues DROP COLUMN cancelled_at,
         DROP COLUMN cancelled_by,DROP COLUMN cancel_reason,DROP COLUMN cancel_request_id;
       ALTER TABLE finance.training_fee_issues DROP CONSTRAINT training_fee_issues_status_check;
       ALTER TABLE finance.training_fee_issues ADD CONSTRAINT training_fee_issues_status_check
         CHECK(status IN ('issued','paid','review_required'));
    """);
}
